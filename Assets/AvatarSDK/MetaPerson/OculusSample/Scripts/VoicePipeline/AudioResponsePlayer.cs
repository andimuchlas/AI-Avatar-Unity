using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioResponsePlayer : MonoBehaviour
    {
        [Header("Audio Settings")]
        public int expectedSampleRate = 22050; // Piper default output rate

        private AudioSource _audioSource;
        private readonly Queue<AudioClip> _clipQueue = new Queue<AudioClip>();
        private float _playStartTime;
        private bool _isPlaying;

        void Awake()
        {
            _audioSource = GetComponent<AudioSource>();

            // Fix No Sound issue: OVRLipSyncContext by default mutes the audio (audioLoopback = false)
            // because it expects a mic input. We need it to pass the AI voice through!
            var lipSync = GetComponent("OVRLipSyncContext");
            if (lipSync != null)
            {
                var field = lipSync.GetType().GetField("audioLoopback");
                field?.SetValue(lipSync, true);
            }
        }

        void Update()
        {
            // Play next clip in queue when current finishes
            if (!_audioSource.isPlaying && _clipQueue.Count > 0)
            {
                AudioClip nextClip = _clipQueue.Dequeue();
                _audioSource.clip = nextClip;
                _audioSource.Play();
                _isPlaying = true;
                _playStartTime = Time.time;
            }
            // Add minimum 0.1s delay before checking !isPlaying to avoid 1-frame Unity bug
            else if (!_audioSource.isPlaying && _isPlaying && Time.time - _playStartTime > 0.1f)
            {
                _isPlaying = false;
            }
        }

        /// <summary>
        /// Enqueue a base64-encoded WAV for playback.
        /// The OVRLipSyncContext attached to this AudioSource will
        /// automatically process the audio via OnAudioFilterRead.
        /// </summary>
        public void PlayBase64WAV(string base64Wav)
        {
            try
            {
                byte[] wavBytes = Convert.FromBase64String(base64Wav);
                AudioClip clip = WAVToAudioClip(wavBytes);

                if (clip != null)
                {
                    _clipQueue.Enqueue(clip);
                    Debug.Log($"[AudioPlayer] Queued clip: {clip.length:F2}s, {clip.frequency}Hz");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioPlayer] Error: {ex.Message}");
            }
        }

        public bool IsPlaying => _audioSource != null && (_audioSource.isPlaying || _clipQueue.Count > 0 || _isPlaying);

        public void Stop()
        {
            _clipQueue.Clear();
            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
            _isPlaying = false;
        }

        /// <summary>
        /// Parse WAV bytes and create an AudioClip.
        /// Supports 16-bit and 32-bit float PCM.
        /// </summary>
        private AudioClip WAVToAudioClip(byte[] wavData)
        {
            if (wavData.Length < 44)
            {
                Debug.LogError("[AudioPlayer] WAV data too short");
                return null;
            }

            // Parse WAV header
            // Bytes 0-3: "RIFF"
            // Bytes 8-11: "WAVE"
            // Bytes 20-21: Audio format (1 = PCM, 3 = IEEE float)
            // Bytes 22-23: Number of channels
            // Bytes 24-27: Sample rate
            // Bytes 34-35: Bits per sample

            int audioFormat = BitConverter.ToInt16(wavData, 20);
            int channels = BitConverter.ToInt16(wavData, 22);
            int sampleRate = BitConverter.ToInt32(wavData, 24);
            int bitsPerSample = BitConverter.ToInt16(wavData, 34);

            // Find "data" chunk
            int dataOffset = 12;
            int dataSize = 0;

            while (dataOffset < wavData.Length - 8)
            {
                string chunkId = System.Text.Encoding.ASCII.GetString(wavData, dataOffset, 4);
                int chunkSize = BitConverter.ToInt32(wavData, dataOffset + 4);

                if (chunkId == "data")
                {
                    dataOffset += 8;
                    dataSize = chunkSize;
                    break;
                }

                dataOffset += 8 + chunkSize;
            }

            if (dataSize == 0)
            {
                Debug.LogError("[AudioPlayer] No data chunk found in WAV");
                return null;
            }

            // Convert to float samples
            float[] samples;

            if (audioFormat == 1 && bitsPerSample == 16)
            {
                // PCM 16-bit
                int numSamples = dataSize / 2;
                samples = new float[numSamples];
                for (int i = 0; i < numSamples; i++)
                {
                    short sample = BitConverter.ToInt16(wavData, dataOffset + i * 2);
                    samples[i] = sample / 32768f;
                }
            }
            else if (audioFormat == 3 && bitsPerSample == 32)
            {
                // IEEE float 32-bit
                int numSamples = dataSize / 4;
                samples = new float[numSamples];
                for (int i = 0; i < numSamples; i++)
                {
                    samples[i] = BitConverter.ToSingle(wavData, dataOffset + i * 4);
                }
            }
            else
            {
                Debug.LogError($"[AudioPlayer] Unsupported WAV format: fmt={audioFormat}, bits={bitsPerSample}");
                return null;
            }

            int samplesPerChannel = samples.Length / channels;
            AudioClip clip = AudioClip.Create("VoiceResponse", samplesPerChannel, channels, sampleRate, false);
            clip.SetData(samples, 0);

            return clip;
        }
    }
}
