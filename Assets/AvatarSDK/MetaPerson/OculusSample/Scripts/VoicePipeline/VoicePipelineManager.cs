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

        [Header("Settings")]
        public bool autoStartOnConnect = true;

        public PipelineState State { get; private set; } = PipelineState.Idle;
        public event Action<PipelineState> OnStateChanged;

        private string _sessionId;

        void OnEnable()
        {
            if (wsClient != null)
            {
                wsClient.OnConnected += HandleConnected;
                wsClient.OnDisconnected += HandleDisconnected;
                wsClient.OnMessageReceived += HandleMessage;
            }
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
        }

        void Update()
        {
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
                    type = "end_session",
                    session_id = _sessionId
                });
                wsClient.Send(msg);
            }

            _sessionId = null;
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
            SetState(PipelineState.Idle);
            Debug.Log("[Pipeline] WebSocket disconnected");
        }

        private void HandleMessage(string json)
        {
            try
            {
                ServerMessage msg = JsonUtility.FromJson<ServerMessage>(json);

                switch (msg.type)
                {
                    case "session_started":
                        _sessionId = msg.session_id;
                        Debug.Log($"[Pipeline] Session started: {_sessionId}");

                        if (autoStartOnConnect)
                        {
                            StartPipeline();
                        }
                        break;

                    case "audio_response":
                        if (msg.data == "[INTERRUPT]")
                        {
                            Debug.Log("[Pipeline] Received INTERRUPT signal. Aborting playback.");
                            if (audioPlayer != null)
                            {
                                audioPlayer.Stop();
                            }
                            break;
                        }

                        Debug.Log("[Pipeline] Received audio response");

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

                    case "transcript":
                        Debug.Log($"[Pipeline] Transcript: {msg.data}");
                        SetState(PipelineState.Processing);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipeline] Error handling message: {ex.Message}");
            }
        }

        private void SetState(PipelineState newState)
        {
            if (State == newState) return;

            Debug.Log($"[Pipeline] {State} → {newState}");
            State = newState;
            OnStateChanged?.Invoke(newState);
        }

        [Serializable]
        private struct ServerMessage
        {
            public string type;
            public string session_id;
            public string data;
        }

        [Serializable]
        private struct EndSessionMessage
        {
            public string type;
            public string session_id;
        }
    }
}
