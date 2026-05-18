using System;
using UnityEngine;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    public class VoicePipelineManager : MonoBehaviour
    {
        public enum PipelineState
        {
            Idle,
            Connecting,
            Listening,
            Processing,
            Speaking
        }

        [Header("Components")]
        public VoiceWebSocketClient wsClient;
        public MicrophoneStreamer micStreamer;
        public AudioResponsePlayer audioPlayer;
        public StateLipSync stateLipSync;

        [Header("Settings")]
        public bool autoStartOnConnect = true;

        public PipelineState State { get; private set; } = PipelineState.Idle;
        public event Action<PipelineState> OnStateChanged;

        private string _sessionId;
        private int _minAcceptedTurnId;
    #if UNITY_WEBGL && !UNITY_EDITOR
        private bool _pendingUserGestureStart;
        private bool _hasUserGesture;
        private bool _loggedWaitingSession;
        private WebGLMicPermissionOverlay _webglOverlay;
    #endif

        void OnEnable()
        {
            if (stateLipSync == null)
            {
                stateLipSync = FindObjectOfType<StateLipSync>(true);
            }

            if (wsClient != null)
            {
                wsClient.OnConnected += HandleConnected;
                wsClient.OnDisconnected += HandleDisconnected;
                wsClient.OnMessageReceived += HandleMessage;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            EnsureWebGLOverlay();
            UpdateWebGLOverlayState();
#endif
        }

        void OnDisable()
        {
            StopPipeline();

            if (wsClient != null)
            {
                wsClient.OnConnected -= HandleConnected;
                wsClient.OnDisconnected -= HandleDisconnected;
                wsClient.OnMessageReceived -= HandleMessage;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            DestroyWebGLOverlay();
#endif
        }

        void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_pendingUserGestureStart && _hasUserGesture)
            {
                TryStartPendingWebGLRequest();
            }

            UpdateWebGLOverlayState();
#endif

            // Transition from Speaking → Listening when playback finishes
            if (State == PipelineState.Speaking && audioPlayer != null && !audioPlayer.IsPlaying)
            {
                SetState(PipelineState.Listening);

                // Resume mic streaming
                if (micStreamer != null && !micStreamer.IsRecording)
                {
                    micStreamer.StartStreaming(_sessionId);
                }
            }
        }

        public void StartPipeline()
        {
            if (wsClient == null || !wsClient.IsConnected)
            {
                Debug.LogWarning("[Pipeline] Cannot start: WebSocket not connected");
                return;
            }

            if (string.IsNullOrEmpty(_sessionId))
            {
                Debug.LogWarning("[Pipeline] Cannot start: No session ID");
                return;
            }

            // Start mic streaming
            if (micStreamer != null)
            {
                micStreamer.StartStreaming(_sessionId);
            }

            SetState(PipelineState.Listening);
            Debug.Log("[Pipeline] Started — listening for voice input");
        }

        public void StopPipeline()
        {
            if (micStreamer != null)
            {
                micStreamer.StopStreaming();
            }

            if (audioPlayer != null)
            {
                audioPlayer.Stop();
            }

            // Send end_session
            if (wsClient != null && wsClient.IsConnected && !string.IsNullOrEmpty(_sessionId))
            {
                string msg = JsonUtility.ToJson(new EndSessionMessage
                {
                    type = "end_session"
                });
                wsClient.Send(msg);
            }

            _sessionId = null;
            _minAcceptedTurnId = 0;

#if UNITY_WEBGL && !UNITY_EDITOR
            _pendingUserGestureStart = false;
#endif

            SetState(PipelineState.Idle);
            Debug.Log("[Pipeline] Stopped");
        }

        private void HandleConnected()
        {
            Debug.Log("[Pipeline] WebSocket connected, waiting for session...");
            SetState(PipelineState.Connecting);
        }

        private void HandleDisconnected()
        {
            if (micStreamer != null)
            {
                micStreamer.StopStreaming();
            }

            _minAcceptedTurnId = 0;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (autoStartOnConnect && _hasUserGesture)
            {
                _pendingUserGestureStart = true;
                _loggedWaitingSession = false;
            }
#endif

            SetState(PipelineState.Idle);
            Debug.Log("[Pipeline] WebSocket disconnected");
        }

        private void HandleMessage(string json)
        {
            try
            {
                ServerMessage msg = JsonUtility.FromJson<ServerMessage>(json);
                bool hasTurnId = HasField(json, "turn_id");

                switch (msg.type)
                {
                    case "session_started":
                        _sessionId = msg.session_id;
                        _minAcceptedTurnId = 0;
                        Debug.Log($"[Pipeline] Session started: {_sessionId}");

#if UNITY_WEBGL && !UNITY_EDITOR
                        if (autoStartOnConnect)
                        {
                            _pendingUserGestureStart = true;
                            _loggedWaitingSession = false;

                            if (_hasUserGesture)
                            {
                                Debug.Log("[Pipeline] User gesture already captured. Starting now.");
                                TryStartPendingWebGLRequest();
                            }
                            else
                            {
                                Debug.Log("[Pipeline] Waiting for user to enable microphone.");
                            }
                        }
#else
                        if (autoStartOnConnect)
                        {
                            StartPipeline();
                        }
#endif
                        break;

                    case "audio_response":
                        if (hasTurnId && msg.turn_id < _minAcceptedTurnId)
                        {
                            Debug.Log($"[Pipeline] Dropped stale audio response. turn_id={msg.turn_id}, min_allowed={_minAcceptedTurnId}");
                            break;
                        }

                        if (string.IsNullOrEmpty(msg.data))
                        {
                            Debug.LogWarning("[Pipeline] Received empty audio_response payload.");
                            break;
                        }

                        Debug.Log(hasTurnId
                            ? $"[Pipeline] Received audio response (turn_id={msg.turn_id})"
                            : "[Pipeline] Received audio response");

                        // Phase 2: DO NOT stop mic while avatar is speaking so barge-in can work.
                        // if (micStreamer != null)
                        // {
                        //     micStreamer.StopStreaming();
                        // }

                        SetState(PipelineState.Speaking);

                        // Play response audio — lip sync happens automatically
                        if (audioPlayer != null)
                        {
                            audioPlayer.PlayBase64WAV(msg.data);
                        }
                        break;

                    case "interrupt":
                        Debug.Log(hasTurnId
                            ? $"[Pipeline] Interrupt received for turn_id={msg.turn_id}. Stopping playback and clearing queue."
                            : "[Pipeline] Interrupt received without turn_id. Stopping playback and clearing queue.");

                        if (hasTurnId)
                        {
                            // Ignore delayed chunks from the interrupted turn and older turns.
                            _minAcceptedTurnId = Math.Max(_minAcceptedTurnId, msg.turn_id + 1);
                        }

                        if (audioPlayer != null)
                        {
                            audioPlayer.Stop();
                        }

                        if (micStreamer != null &&
                            !micStreamer.IsRecording &&
                            wsClient != null &&
                            wsClient.IsConnected &&
                            !string.IsNullOrEmpty(_sessionId))
                        {
                            micStreamer.StartStreaming(_sessionId);
                        }

                        SetState(PipelineState.Listening);
                        break;

                    case "transcript":
                        Debug.Log($"[Pipeline] Transcript: {msg.data}");
                        if (stateLipSync != null)
                        {
                            stateLipSync.SetTranscriptForAnimation(msg.data);
                        }
                        SetState(PipelineState.Processing);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipeline] Error handling message: {ex.Message}");
            }
        }

        private static bool HasField(string json, string fieldName)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            return json.IndexOf($"\"{fieldName}\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetState(PipelineState newState)
        {
            if (State == newState) return;

            Debug.Log($"[Pipeline] {State} → {newState}");
            State = newState;
            OnStateChanged?.Invoke(newState);

#if UNITY_WEBGL && !UNITY_EDITOR
            UpdateWebGLOverlayState();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        public void RegisterUserGestureStartRequest()
        {
            _hasUserGesture = true;
            _pendingUserGestureStart = true;

            if (micStreamer != null)
            {
                micStreamer.PrimePermissionFromUserGesture();
            }

            TryStartPendingWebGLRequest();
            UpdateWebGLOverlayState();
        }

        private void TryStartPendingWebGLRequest()
        {
            if (!_pendingUserGestureStart || !_hasUserGesture)
            {
                return;
            }

            if (CanStartPipelineNow())
            {
                _pendingUserGestureStart = false;
                _loggedWaitingSession = false;
                Debug.Log("[Pipeline] User gesture detected. Starting microphone pipeline...");
                StartPipeline();
                return;
            }

            if (!_loggedWaitingSession)
            {
                _loggedWaitingSession = true;
                bool connected = wsClient != null && wsClient.IsConnected;
                bool hasSession = !string.IsNullOrEmpty(_sessionId);
                Debug.Log($"[Pipeline] Gesture captured. Waiting before listening. connected={connected}, session_started={hasSession}, state={State}");
            }
        }

        private bool CanStartPipelineNow()
        {
            // Connecting is valid while waiting for session handshake.
            // Do not re-start if already active.
            if (State == PipelineState.Listening || State == PipelineState.Processing || State == PipelineState.Speaking)
            {
                return false;
            }

            return wsClient != null &&
                   wsClient.IsConnected &&
                   !string.IsNullOrEmpty(_sessionId);
        }

        private bool ShouldShowWebGLStartOverlay()
        {
            if (!autoStartOnConnect)
            {
                return false;
            }

            // Hide overlay once voice pipeline is active.
            if (State == PipelineState.Listening || State == PipelineState.Processing || State == PipelineState.Speaking)
            {
                return false;
            }

            // Show while waiting for initial user gesture, or while connecting/session handshake.
            return !_hasUserGesture || _pendingUserGestureStart || State == PipelineState.Connecting;
        }

        private void EnsureWebGLOverlay()
        {
            if (_webglOverlay != null)
            {
                return;
            }

            GameObject overlayObject = new GameObject("WebGLMicPermissionOverlay", typeof(RectTransform));
            overlayObject.transform.SetParent(transform, false);

            _webglOverlay = overlayObject.AddComponent<WebGLMicPermissionOverlay>();
            _webglOverlay.Initialize(RegisterUserGestureStartRequest);
        }

        private void DestroyWebGLOverlay()
        {
            if (_webglOverlay == null)
            {
                return;
            }

            _webglOverlay.Teardown();
            Destroy(_webglOverlay.gameObject);
            _webglOverlay = null;
        }

        private void UpdateWebGLOverlayState()
        {
            if (_webglOverlay == null)
            {
                return;
            }

            if (!ShouldShowWebGLStartOverlay())
            {
                bool successTransition = State == PipelineState.Listening ||
                                         State == PipelineState.Processing ||
                                         State == PipelineState.Speaking;

                _webglOverlay.Hide(successTransition);
                return;
            }

            _webglOverlay.Show();

            bool connected = wsClient != null && wsClient.IsConnected;
            bool hasSession = !string.IsNullOrEmpty(_sessionId);
            _webglOverlay.SetConnectionState(_hasUserGesture, connected, hasSession);
        }
#endif

        [Serializable]
        private struct ServerMessage
        {
            public string type;
            public string session_id;
            public int turn_id;
            public string data;
        }

        [Serializable]
        private struct EndSessionMessage
        {
            public string type;
        }
    }
}
