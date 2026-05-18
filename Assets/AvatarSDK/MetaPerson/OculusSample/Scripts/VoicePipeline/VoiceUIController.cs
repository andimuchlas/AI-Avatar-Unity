using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    public class VoiceUIController : MonoBehaviour
    {
        [Header("Voice Pipeline References")]
        public VoicePipelineManager pipelineManager;
        public MicrophoneStreamer micStreamer;
        public AudioResponsePlayer audioPlayer;

        [Header("UI Elements (Optional)")]
        [Tooltip("Text UI untuk menampilkan status AI (Listening, Processing, dll)")]
        public Text statusText;
        [Tooltip("TMP Text alternatif untuk status AI")]
        public TMP_Text statusTmpText;
        [Tooltip("Slider UI untuk mengatur volume AI (0.0 sampai 1.0)")]
        public Slider volumeSlider;

        private AudioSource _audioSource;

        void Start()
        {
            if (audioPlayer != null)
            {
                _audioSource = audioPlayer.GetComponent<AudioSource>();
            }
            else
            {
                Debug.LogWarning("[VoiceUIController] AudioResponsePlayer reference is missing!");
            }

            if (pipelineManager != null)
            {
                pipelineManager.OnStateChanged += HandleStateChanged;
                HandleStateChanged(pipelineManager.State);
            }

            if (volumeSlider != null && _audioSource != null)
            {
                volumeSlider.value = _audioSource.volume;
                volumeSlider.onValueChanged.AddListener(SetVolume);
            }
        }

        void OnDestroy()
        {
            if (pipelineManager != null)
            {
                pipelineManager.OnStateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(VoicePipelineManager.PipelineState state)
        {
            string label = string.Empty;
            Color labelColor = Color.white;

            switch (state)
            {
                case VoicePipelineManager.PipelineState.Idle:
                    label = "Idle";
                    labelColor = Color.red;
                    break;
                case VoicePipelineManager.PipelineState.Connecting:
                    label = "Connecting...";
                    labelColor = new Color(1f, 0.5f, 0f); // Orange
                    break;
                case VoicePipelineManager.PipelineState.Listening:
                    label = "Listening";
                    labelColor = Color.green;
                    break;
                case VoicePipelineManager.PipelineState.Processing:
                    label = "Processing...";
                    labelColor = Color.yellow;
                    break;
                case VoicePipelineManager.PipelineState.Speaking:
                    label = "Speaking";
                    labelColor = Color.cyan;
                    break;
            }

            if (statusText != null)
            {
                statusText.text = label;
                statusText.color = labelColor;
            }

            if (statusTmpText != null)
            {
                statusTmpText.text = label;
                statusTmpText.color = labelColor;
            }

            if (statusText == null && statusTmpText == null)
            {
                Debug.LogWarning("[VoiceUIController] No status text assigned. Assign statusText or statusTmpText.");
            }
        }

        public void ToggleMicMute()
        {
            if (micStreamer != null)
            {
                micStreamer.IsMuted = !micStreamer.IsMuted;
                Debug.Log($"[VoiceUIController] Mic Muted: {micStreamer.IsMuted}");
            }
        }

        public void SetMicMuted(bool isMuted)
        {
            if (micStreamer != null)
            {
                micStreamer.IsMuted = isMuted;
                Debug.Log($"[VoiceUIController] Mic Muted set to: {isMuted}");
            }
        }

        public void ToggleAudioMute()
        {
            if (_audioSource != null)
            {
                _audioSource.mute = !_audioSource.mute;
                Debug.Log($"[VoiceUIController] Audio Muted: {_audioSource.mute}");
            }
        }

        public void SetAudioMuted(bool isMuted)
        {
            if (_audioSource != null)
            {
                _audioSource.mute = isMuted;
                Debug.Log($"[VoiceUIController] Audio Muted set to: {isMuted}");
            }
        }

        /// <summary>
        /// Menginterupsi ucapan AI (potong pembicaraan) tanpa memutus koneksi server.
        /// Langsung nyetop suara dan mic otomatis mendengarkan lagi (kalo auto-listen).
        /// </summary>
        public void InterruptVoiceAI()
        {
            if (audioPlayer != null)
            {
                audioPlayer.Stop();
                Debug.Log("[VoiceUIController] AI Speech Interrupted (Barge-in).");
            }
        }

        public void ForceStopVoiceAI()
        {
            if (pipelineManager != null)
            {
                pipelineManager.StopPipeline();
                Debug.Log("[VoiceUIController] Force stopped Voice AI Pipeline");
            }
        }

        /// <summary>
        /// Mereset ulang koneksi / obrolan dengan cara drop WebSocket dan koneksi ulang.
        /// Memancing server buat kirim "session_started" baru.
        /// </summary>
        public void ReconnectSession()
        {
            if (pipelineManager != null && pipelineManager.wsClient != null)
            {
                pipelineManager.StopPipeline();
                pipelineManager.wsClient.Reconnect();
                Debug.Log("[VoiceUIController] Requested Session Reconnect.");
            }
        }

        /// <summary>
        /// Mengatur besar-kecil suara AI dari UI Slider.
        /// </summary>
        public void SetVolume(float volume)
        {
            if (_audioSource != null)
            {
                _audioSource.volume = Mathf.Clamp01(volume);
            }
        }

        /// <summary>
        /// Explicitly start listening for voice input. Use this for WebGL where user gesture is required.
        /// Can be hooked to a UI button (OnClick -> VoiceUIController.StartListening).
        /// </summary>
        public void StartListening()
        {
            if (pipelineManager == null)
            {
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[VoiceUIController] User pressed 'Start Listening' button (WebGL).");
            pipelineManager.RegisterUserGestureStartRequest();
#else
            if (pipelineManager.State == VoicePipelineManager.PipelineState.Idle)
            {
                Debug.Log("[VoiceUIController] User pressed 'Start Listening' button.");
                pipelineManager.StartPipeline();
            }
            else
            {
                Debug.LogWarning($"[VoiceUIController] Cannot start listening. Current state: {pipelineManager.State}");
            }
#endif
        }

        /// <summary>
        /// Stop listening and reset pipeline. Hook to a UI button as needed.
        /// </summary>
        public void StopListening()
        {
            if (pipelineManager != null && pipelineManager.State != VoicePipelineManager.PipelineState.Idle)
            {
                Debug.Log("[VoiceUIController] User pressed 'Stop Listening' button.");
                pipelineManager.StopPipeline();
            }
        }
    }
}
