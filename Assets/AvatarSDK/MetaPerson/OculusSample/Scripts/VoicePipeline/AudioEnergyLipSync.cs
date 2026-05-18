using UnityEngine;
using System.Collections.Generic;
using System;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioEnergyLipSync : MonoBehaviour
    {
        [Header("References")]
        public AudioSource audioSource;
        public OVRLipSyncContextMorphTarget[] morphTargets;

        [Header("Mouth Drive")]
        [Tooltip("OVRLipSync viseme index used for mouth open shape. Default: aa (10)")]
        [Range(0, 14)]
        public int mouthVisemeIndex = 10;
        [Tooltip("Multiply RMS loudness before clamping")]
        public float loudnessScale = 12.0f;
        [Tooltip("Blendshape max weight")]
        public float maxBlendshapeWeight = 100.0f;
        [Tooltip("Ignore tiny audio noise floor")]
        [Range(0.0f, 0.2f)]
        public float noiseGate = 0.01f;

        [Header("Smoothing")]
        [Tooltip("Higher value opens mouth faster")]
        public float attack = 20.0f;
        [Tooltip("Higher value closes mouth faster")]
        public float release = 10.0f;

        [Header("Platform")]
        [Tooltip("If true, runs only in WebGL builds")]
        public bool webGLOnly = true;
        [Tooltip("Disable Oculus morph driver on WebGL to avoid conflicts")]
        public bool disableOvrMorphTargetsOnWebGL = true;
        [Tooltip("Try to discover morph targets scene-wide if none assigned")]
        public bool autoFindMorphTargetsInScene = true;
        [Tooltip("Retry binding interval (seconds) for async avatar loading")]
        public float rebindIntervalSeconds = 1.0f;
        [Tooltip("Also drive direct mouth blendshapes if viseme mapping is missing")]
        public bool useDirectBlendshapeFallback = true;
        [Tooltip("Drive jaw bones directly when blendshape-based fallback is unavailable")]
        public bool useJawBoneFallback = true;
        [Tooltip("Maximum local jaw-open rotation in degrees")]
        public float maxJawOpenAngle = 18.0f;
        [Tooltip("Only emit runtime logs when voice energy is detected")]
        public bool logOnlyWhenVoiceDetected = true;
        [Tooltip("Minimum target value considered as active voice for logging")]
        [Range(0.0f, 1.0f)]
        public float voiceLogThreshold = 0.02f;
        [Tooltip("Seconds between periodic logs while voice is active")]
        public float voiceLogIntervalSeconds = 0.5f;

        private readonly float[] _samples = new float[512];
        private float _mouthOpen;
        private float _nextRebindTime;
        private bool _voiceLogActive;
        private float _nextVoiceLogTime;
        private float _nextJawLogTime;
        private bool _loggedNoJawTargetsDuringVoice;
        private readonly List<DirectBlendTarget> _directBlendTargets = new List<DirectBlendTarget>();
        private readonly List<JawBoneTarget> _jawBoneTargets = new List<JawBoneTarget>();
        private static readonly string[] DirectBlendshapeKeywords =
        {
            "jawopen",
            "mouthopen",
            "viseme_aa",
            "viseme aa",
            "aa",
            "mouth_a",
            "viseme",
            "mouth",
            "lip"
        };

        private struct DirectBlendTarget
        {
            public SkinnedMeshRenderer Renderer;
            public int BlendShapeIndex;
        }

        private struct JawBoneTarget
        {
            public Transform Transform;
            public Quaternion BaseLocalRotation;
        }

        void Start()
        {
            Debug.Log("[AudioEnergyLipSync.Start] Initializing...");
            
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                Debug.Log($"[AudioEnergyLipSync.Start] AudioSource: {(audioSource != null ? "found" : "NOT found")}");
            }

            if (morphTargets == null || morphTargets.Length == 0)
            {
                morphTargets = GetComponents<OVRLipSyncContextMorphTarget>();
                Debug.Log($"[AudioEnergyLipSync.Start] OVRLipSyncContextMorphTarget components: {morphTargets?.Length ?? 0}");
            }

            TryAutoBindMorphTargets(false);
            TryBindDirectBlendshapeTargets(false);
            TryBindJawBones(false);

#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[AudioEnergyLipSync.Start] Running on WebGL - disabling OVRLipSyncContextMorphTarget");
            if (disableOvrMorphTargetsOnWebGL && morphTargets != null)
            {
                for (int i = 0; i < morphTargets.Length; i++)
                {
                    if (morphTargets[i] != null)
                    {
                        morphTargets[i].enabled = false;
                    }
                }
            }
#else
            if (webGLOnly)
            {
                Debug.Log("[AudioEnergyLipSync.Start] webGLOnly=true but NOT running on WebGL - disabling component");
                enabled = false;
                return;
            }
#endif

            if (morphTargets == null || morphTargets.Length == 0)
            {
                Debug.LogWarning("[AudioEnergyLipSync] No OVRLipSyncContextMorphTarget found on this GameObject.");
            }

            Debug.Log($"[AudioEnergyLipSync.Start] INIT COMPLETE - morphTargets={morphTargets?.Length ?? 0} | directBlends={_directBlendTargets.Count} | jawBones={_jawBoneTargets.Count}");
            
            _nextRebindTime = Time.time + rebindIntervalSeconds;
        }

        void LateUpdate()
        {
            if (autoFindMorphTargetsInScene && (morphTargets == null || morphTargets.Length == 0) && Time.time >= _nextRebindTime)
            {
                Debug.Log("[AudioEnergyLipSync.LateUpdate] Retrying bind (rebind interval triggered)");
                TryAutoBindMorphTargets(true);
                TryBindDirectBlendshapeTargets(true);
                TryBindJawBones(true);
                _nextRebindTime = Time.time + rebindIntervalSeconds;
            }

            if (audioSource == null)
            {
                SmoothSetMouth(0.0f);
                return;
            }

            if (!audioSource.isPlaying)
            {
                if (_voiceLogActive)
                {
                    Debug.Log("[AudioEnergyLipSync] Voice ended (audio stopped)");
                    _voiceLogActive = false;
                }
                SmoothSetMouth(0.0f);
                return;
            }

            audioSource.GetOutputData(_samples, 0);
            float rms = GetRms(_samples);

            float gated = Mathf.Max(0.0f, rms - noiseGate);
            float target = Mathf.Clamp01(gated * loudnessScale);

            bool voiceDetected = target >= voiceLogThreshold;
            if (!logOnlyWhenVoiceDetected)
            {
                Debug.Log($"[AudioEnergyLipSync] RMS={rms:F4}, target={target:F4}, mouth={_mouthOpen:F4}");
            }
            else if (voiceDetected)
            {
                if (!_voiceLogActive)
                {
                    Debug.Log($"[AudioEnergyLipSync] Voice detected: RMS={rms:F4}, target={target:F4}, directBlends={_directBlendTargets.Count}, jawBones={_jawBoneTargets.Count}");
                    _voiceLogActive = true;
                    _nextVoiceLogTime = Time.time + voiceLogIntervalSeconds;
                    _nextJawLogTime = Time.time;
                    _loggedNoJawTargetsDuringVoice = false;
                }
                else if (Time.time >= _nextVoiceLogTime)
                {
                    Debug.Log($"[AudioEnergyLipSync] Voice active: target={target:F4}, weight={_mouthOpen * maxBlendshapeWeight:F2}");
                    _nextVoiceLogTime = Time.time + voiceLogIntervalSeconds;
                }
            }
            else if (_voiceLogActive)
            {
                Debug.Log("[AudioEnergyLipSync] Voice ended");
                _voiceLogActive = false;
            }

            SmoothSetMouth(target);
        }

        private void SmoothSetMouth(float target)
        {
            float speed = target > _mouthOpen ? attack : release;
            float t = 1.0f - Mathf.Exp(-speed * Time.deltaTime);
            _mouthOpen = Mathf.Lerp(_mouthOpen, target, t);
            float weight = _mouthOpen * maxBlendshapeWeight;

            ApplyMouthWeight(weight);
        }

        private void ApplyMouthWeight(float weight)
        {
            bool appliedViseme = false;

            if (morphTargets == null)
            {
                morphTargets = Array.Empty<OVRLipSyncContextMorphTarget>();
            }

            for (int i = 0; i < morphTargets.Length; i++)
            {
                var target = morphTargets[i];
                if (target == null || target.skinnedMeshRenderer == null || target.visemeToBlendTargets == null)
                {
                    continue;
                }

                if (mouthVisemeIndex < 0 || mouthVisemeIndex >= target.visemeToBlendTargets.Length)
                {
                    continue;
                }

                int blendShapeIndex = target.visemeToBlendTargets[mouthVisemeIndex];
                if (blendShapeIndex >= 0)
                {
                    target.skinnedMeshRenderer.SetBlendShapeWeight(blendShapeIndex, weight);
                    appliedViseme = true;
                }
            }

            if (useDirectBlendshapeFallback && (!appliedViseme || _directBlendTargets.Count > 0))
            {
                ApplyDirectBlendshapeWeight(weight);
            }

            if (useJawBoneFallback)
            {
                float normalized = weight / Mathf.Max(1.0f, maxBlendshapeWeight);
                ApplyJawBoneWeight(normalized);
            }
        }

        private void TryAutoBindMorphTargets(bool logResult)
        {
            if (!autoFindMorphTargetsInScene)
            {
                return;
            }

            if (morphTargets != null && morphTargets.Length > 0)
            {
                if (logResult) Debug.Log($"[AudioEnergyLipSync.TryAutoBindMorphTargets] Already bound: {morphTargets.Length} morph targets");
                return;
            }

            var found = FindObjectsOfType<OVRLipSyncContextMorphTarget>(true);
            if (found == null || found.Length == 0)
            {
                if (logResult)
                {
                    Debug.Log("[AudioEnergyLipSync.TryAutoBindMorphTargets] Waiting for avatar morph targets...");
                }
                return;
            }

            var valid = new List<OVRLipSyncContextMorphTarget>(found.Length);
            for (int i = 0; i < found.Length; i++)
            {
                var mt = found[i];
                if (mt != null && mt.skinnedMeshRenderer != null && mt.visemeToBlendTargets != null)
                {
                    valid.Add(mt);
                    Debug.Log($"[AudioEnergyLipSync.TryAutoBindMorphTargets] ✓ Valid morph target[{i}]: {mt.name}");
                }
                else
                {
                    Debug.LogWarning($"[AudioEnergyLipSync.TryAutoBindMorphTargets]   ✗ Invalid morph target[{i}] (missing renderer or viseme targets)");
                }
            }

            morphTargets = valid.ToArray();

#if UNITY_WEBGL && !UNITY_EDITOR
            if (disableOvrMorphTargetsOnWebGL && morphTargets != null)
            {
                for (int i = 0; i < morphTargets.Length; i++)
                {
                    if (morphTargets[i] != null)
                    {
                        morphTargets[i].enabled = false;
                    }
                }
            }
#endif

            Debug.Log($"[AudioEnergyLipSync.TryAutoBindMorphTargets] BIND COMPLETE: {morphTargets.Length} valid morph target(s)");
        }

        private void TryBindDirectBlendshapeTargets(bool logResult)
        {
            if (!useDirectBlendshapeFallback)
            {
                if (logResult) Debug.Log("[AudioEnergyLipSync.TryBindDirectBlendshapeTargets] Disabled via useDirectBlendshapeFallback");
                return;
            }

            if (_directBlendTargets.Count > 0)
            {
                if (logResult) Debug.Log($"[AudioEnergyLipSync.TryBindDirectBlendshapeTargets] Already bound: {_directBlendTargets.Count} targets");
                return;
            }

            var renderers = FindObjectsOfType<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("[AudioEnergyLipSync.TryBindDirectBlendshapeTargets] No SkinnedMeshRenderer found in scene!");
                return;
            }

            Debug.Log($"[AudioEnergyLipSync.TryBindDirectBlendshapeTargets] Searching {renderers.Length} SkinnedMeshRenderer(s)...");

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var mesh = renderer.sharedMesh;
                int blendShapeCount = mesh.blendShapeCount;
                
                Debug.Log($"[AudioEnergyLipSync.TryBindDirectBlendshapeTargets]   Renderer[{i}] {renderer.name}: {blendShapeCount} blendshapes");

                for (int j = 0; j < blendShapeCount; j++)
                {
                    string blendName = mesh.GetBlendShapeName(j);
                    if (string.IsNullOrEmpty(blendName))
                    {
                        continue;
                    }

                    string lower = blendName.ToLowerInvariant();
                    for (int k = 0; k < DirectBlendshapeKeywords.Length; k++)
                    {
                        if (lower.Contains(DirectBlendshapeKeywords[k]))
                        {
                            _directBlendTargets.Add(new DirectBlendTarget
                            {
                                Renderer = renderer,
                                BlendShapeIndex = j
                            });
                            Debug.Log($"[AudioEnergyLipSync.TryBindDirectBlendshapeTargets]     ✓ MATCHED: {renderer.name}[{j}] '{blendName}' (keyword='{DirectBlendshapeKeywords[k]}')");
                            break;
                        }
                    }
                }
            }

            if (logResult || _directBlendTargets.Count > 0)
            {
                Debug.Log($"[AudioEnergyLipSync.TryBindDirectBlendshapeTargets] BIND COMPLETE: {_directBlendTargets.Count} mouth blendshape target(s) found");
            }
        }

        private void TryBindJawBones(bool logResult)
        {
            if (!useJawBoneFallback)
            {
                if (logResult) Debug.Log("[AudioEnergyLipSync.TryBindJawBones] Disabled via useJawBoneFallback");
                return;
            }

            if (_jawBoneTargets.Count > 0)
            {
                if (logResult) Debug.Log($"[AudioEnergyLipSync.TryBindJawBones] Already bound: {_jawBoneTargets.Count} jaw bones");
                return;
            }

            var transforms = FindObjectsOfType<Transform>(true);
            if (transforms == null || transforms.Length == 0)
            {
                Debug.LogWarning("[AudioEnergyLipSync.TryBindJawBones] No Transform found in scene!");
                return;
            }

            Debug.Log($"[AudioEnergyLipSync.TryBindJawBones] Searching {transforms.Length} Transform(s)...");

            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null || string.IsNullOrEmpty(t.name))
                {
                    continue;
                }

                string lower = t.name.ToLowerInvariant();
                if (lower.Contains("jaw") || lower.Contains("chin"))
                {
                    _jawBoneTargets.Add(new JawBoneTarget
                    {
                        Transform = t,
                        BaseLocalRotation = t.localRotation
                    });
                    Debug.Log($"[AudioEnergyLipSync.TryBindJawBones]   ✓ MATCHED: {t.name} (keywords: jaw OR chin)");
                }
            }

            if (logResult || _jawBoneTargets.Count > 0)
            {
                Debug.Log($"[AudioEnergyLipSync.TryBindJawBones] BIND COMPLETE: {_jawBoneTargets.Count} jaw bone target(s) found");
            }
        }

        private void ApplyDirectBlendshapeWeight(float weight)
        {
            if (_directBlendTargets.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _directBlendTargets.Count; i++)
            {
                var target = _directBlendTargets[i];
                if (target.Renderer == null || target.BlendShapeIndex < 0)
                {
                    continue;
                }

                target.Renderer.SetBlendShapeWeight(target.BlendShapeIndex, weight);
            }
        }

        private void ApplyJawBoneWeight(float normalizedMouth)
        {
            if (_jawBoneTargets.Count == 0)
            {
                if (_voiceLogActive && !_loggedNoJawTargetsDuringVoice)
                {
                    Debug.LogWarning("[AudioEnergyLipSync.Jaw] Voice detected but no jaw bone targets found. Jaw movement is skipped.");
                    _loggedNoJawTargetsDuringVoice = true;
                }
                return;
            }

            float clamped = Mathf.Clamp01(normalizedMouth);
            float angle = clamped * maxJawOpenAngle;

            if (_voiceLogActive && Time.time >= _nextJawLogTime)
            {
                string firstJawName = _jawBoneTargets[0].Transform != null ? _jawBoneTargets[0].Transform.name : "null";
                Debug.Log($"[AudioEnergyLipSync.Jaw] Moving jaw: targets={_jawBoneTargets.Count}, angle={angle:F2}deg, firstTarget={firstJawName}");
                _nextJawLogTime = Time.time + voiceLogIntervalSeconds;
            }

            for (int i = 0; i < _jawBoneTargets.Count; i++)
            {
                var target = _jawBoneTargets[i];
                if (target.Transform == null)
                {
                    continue;
                }

                target.Transform.localRotation = target.BaseLocalRotation * Quaternion.Euler(angle, 0f, 0f);
            }
        }

        private static float GetRms(float[] data)
        {
            if (data == null || data.Length == 0)
            {
                return 0.0f;
            }

            float sum = 0.0f;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i] * data[i];
            }

            return Mathf.Sqrt(sum / data.Length);
        }
    }
}
