using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    [RequireComponent(typeof(AudioSource))]
    public class MicrophoneStreamer : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // JavaScript interop for WebGL microphone
        [DllImport("__Internal")]
        private static extern int WebGLMicrophoneStart();

        [DllImport("__Internal")]
        private static extern int WebGLMicrophoneGetPosition();

        [DllImport("__Internal")]
        private static extern int WebGLMicrophoneGetChunk(float[] outputBuffer, int maxSamples);

        [DllImport("__Internal")]
        private static extern int WebGLMicrophoneIsRecording();

        [DllImport("__Internal")]
        private static extern int WebGLMicrophoneStop();

        [DllImport("__Internal")]
        private static extern int WebGLMicrophoneGetSampleRate();
#endif

        [Header("Microphone Settings")]
        public int sampleRate = 16000;
        public float chunkIntervalSeconds = 0.1f; // 100ms chunks

        [Header("References")]
        public VoiceWebSocketClient wsClient;

        public bool IsRecording { get; private set; }
        public bool IsMuted { get; set; } = false;

        private AudioClip _micClip;
        private string _micDevice;
        private int _lastReadPos;
        private float _chunkTimer;
    #if UNITY_WEBGL && !UNITY_EDITOR
        private readonly List<float> _webglAccumulatedSamples = new List<float>(3200);
        private const int WebGLMinSendSamples = 3200; // 200ms @ 16kHz
    #endif

        public void StartStreaming(string sessionId)
        {
            if (IsRecording) return;

            // Backend binds session by active WebSocket connection.
            _ = sessionId;

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: Use JavaScript microphone
            Debug.Log("[MicStreamer] Requesting WebGL microphone access...");
            WebGLMicrophoneStart();
            _lastReadPos = 0;
            _chunkTimer = 0f;
            _webglAccumulatedSamples.Clear();
            IsRecording = true;
            Debug.Log("[MicStreamer] WebGL microphone recording started");
#else
            // Desktop: Use Unity Microphone class
            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[MicStreamer] No microphone found!");
                return;
            }

            _micDevice = Microphone.devices[0];
            Debug.Log($"[MicStreamer] Using mic: {_micDevice}");

            _micClip = Microphone.Start(_micDevice, true, 1, sampleRate);

            while (Microphone.GetPosition(_micDevice) <= 0) { }

            _lastReadPos = 0;
            _chunkTimer = 0f;
            IsRecording = true;

            Debug.Log("[MicStreamer] Recording started");
#endif
        }

        public void StopStreaming()
        {
            if (!IsRecording) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: Stop JavaScript microphone
            WebGLMicrophoneStop();
            _webglAccumulatedSamples.Clear();
            IsRecording = false;
            Debug.Log("[MicStreamer] WebGL recording stopped");
#else
            // Desktop: Stop Unity Microphone
            Microphone.End(_micDevice);
            IsRecording = false;
            Debug.Log("[MicStreamer] Recording stopped");
#endif
        }

        public void PrimePermissionFromUserGesture()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (IsRecording)
            {
                return;
            }

            Debug.Log("[MicStreamer] Priming browser microphone permission from user gesture...");
            int result = WebGLMicrophoneStart();
            if (result == 1)
            {
                WebGLMicrophoneStop();
            }
            else
            {
                Debug.LogWarning("[MicStreamer] Microphone permission priming did not start immediately.");
            }
#endif
        }

        void Update()
        {
            if (!IsRecording || wsClient == null || !wsClient.IsConnected) return;

            _chunkTimer += Time.deltaTime;

            if (_chunkTimer >= chunkIntervalSeconds)
            {
                _chunkTimer = 0f;
                SendAudioChunk();
            }
        }

        private void SendAudioChunk()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: Get chunk from JavaScript buffer
            SendWebGLAudioChunk();
#else
            // Desktop: Get chunk from Microphone
            SendDesktopAudioChunk();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void SendWebGLAudioChunk()
        {
            if (WebGLMicrophoneIsRecording() == 0)
            {
                return;
            }

            // Request up to 1600 samples (100ms at 16kHz)
            float[] sampleBuffer = new float[1600];
            int samplesRead = WebGLMicrophoneGetChunk(sampleBuffer, 1600);

            if (samplesRead <= 0) return;

            if (IsMuted) return;

            for (int i = 0; i < samplesRead; i++)
            {
                _webglAccumulatedSamples.Add(sampleBuffer[i]);
            }

            if (_webglAccumulatedSamples.Count < WebGLMinSendSamples)
            {
                return;
            }

            // Convert retrieved samples to PCM16
            float[] samples = _webglAccumulatedSamples.ToArray();
            _webglAccumulatedSamples.Clear();

            byte[] pcmBytes = FloatToPCM16(samples);
            string base64Audio = Convert.ToBase64String(pcmBytes);

            string message = JsonUtility.ToJson(new AudioChunkMessage
            {
                type = "audio_chunk",
                data = base64Audio
            });

            wsClient.Send(message);
        }
#else
        private void SendDesktopAudioChunk()
        {
            int currentPos = Microphone.GetPosition(_micDevice);
            if (currentPos == _lastReadPos) return;

            int samplesToRead;
            if (currentPos > _lastReadPos)
            {
                samplesToRead = currentPos - _lastReadPos;
            }
            else
            {
                samplesToRead = (_micClip.samples - _lastReadPos) + currentPos;
            }

            if (samplesToRead <= 0) return;

            float[] samples = new float[samplesToRead];
            _micClip.GetData(samples, _lastReadPos);
            _lastReadPos = currentPos;

            if (IsMuted) return;

            byte[] pcmBytes = FloatToPCM16(samples);

            string base64Audio = Convert.ToBase64String(pcmBytes);

            string message = JsonUtility.ToJson(new AudioChunkMessage
            {
                type = "audio_chunk",
                data = base64Audio
            });

            wsClient.Send(message);
        }
#endif

        private byte[] FloatToPCM16(float[] samples)
        {
            byte[] pcm = new byte[samples.Length * 2];

            for (int i = 0; i < samples.Length; i++)
            {
                // Clamp and convert float [-1.0, 1.0] to int16 [-32768, 32767]
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short value = (short)(clamped * 32767f);

                // Little-endian
                pcm[i * 2] = (byte)(value & 0xFF);
                pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            return pcm;
        }

        void OnDisable()
        {
            StopStreaming();
        }

        void OnDestroy()
        {
            StopStreaming();
        }

        [Serializable]
        private struct AudioChunkMessage
        {
            public string type;
            public string data;
        }
    }
}
