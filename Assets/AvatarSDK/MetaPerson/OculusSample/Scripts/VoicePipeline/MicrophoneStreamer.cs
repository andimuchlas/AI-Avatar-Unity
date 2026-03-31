using System;
using UnityEngine;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    [RequireComponent(typeof(AudioSource))]
    public class MicrophoneStreamer : MonoBehaviour
    {
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
        private string _sessionId;

        public void StartStreaming(string sessionId)
        {
            if (IsRecording) return;

            _sessionId = sessionId;

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
        }

        public void StopStreaming()
        {
            if (!IsRecording) return;

            Microphone.End(_micDevice);
            IsRecording = false;

            Debug.Log("[MicStreamer] Recording stopped");
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
                session_id = _sessionId,
                data = base64Audio
            });

            wsClient.Send(message);
        }

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
            public string session_id;
            public string data;
        }
    }
}
