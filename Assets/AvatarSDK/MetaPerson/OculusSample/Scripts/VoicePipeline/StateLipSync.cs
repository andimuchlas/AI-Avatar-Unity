using UnityEngine;
using System.Collections.Generic;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    // WebGL-safe lipsync driver that does not depend on native OVR runtime.
    // It reuses OVRLipSyncContextMorphTarget viseme mappings configured in inspector.
    public class StateLipSync : MonoBehaviour
    {
        [Header("References")]
        public VoicePipelineManager pipelineManager;
        public AudioResponsePlayer audioPlayer;

        [Header("Animation")]
        [Tooltip("Maximum applied viseme weight")]
        [Range(10f, 100f)]
        public float maxVisemeWeight = 64.0f;
        [Tooltip("Hard limit for any viseme weight")]
        [Range(10f, 100f)]
        public float visemeHardCap = 70.0f;
        [Tooltip("Seconds per vowel step")]
        [Range(0.06f, 0.35f)]
        public float vowelDuration = 0.13f;
        [Tooltip("Fallback pulse frequency when no transcript available")]
        [Range(0.5f, 4f)]
        public float fallbackPulseFrequency = 2.2f;
        [Tooltip("Fallback minimum jaw-open")]
        [Range(0f, 1f)]
        public float fallbackBaseOpen = 0.18f;
        [Tooltip("Fallback pulse amplitude")]
        [Range(0f, 1f)]
        public float fallbackPulseAmplitude = 0.2f;
        [Tooltip("Small smoothing to reduce jitter")]
        [Range(0f, 30f)]
        public float weightSmoothing = 16f;
        [Tooltip("Neighbor viseme influence for coarticulation")]
        [Range(0f, 1f)]
        public float coarticulationStrength = 0.38f;
        [Tooltip("Small rhythmic accent so speech doesn't look robotic")]
        [Range(0f, 0.35f)]
        public float articulationAccent = 0.06f;

        [Header("Viseme Shaping")]
        [Tooltip("Multiplier for viseme aa (wide open vowel)")]
        [Range(0.2f, 1.2f)]
        public float visemeAA = 0.72f;
        [Tooltip("Multiplier for viseme E")]
        [Range(0.2f, 1.2f)]
        public float visemeE = 0.56f;
        [Tooltip("Multiplier for viseme ih")]
        [Range(0.2f, 1.2f)]
        public float visemeIH = 0.52f;
        [Tooltip("Multiplier for viseme oh")]
        [Range(0.2f, 1.2f)]
        public float visemeOH = 0.62f;
        [Tooltip("Multiplier for viseme ou")]
        [Range(0.2f, 1.2f)]
        public float visemeOU = 0.58f;

        // Oculus viseme indices from OVRLipSync:
        // 10=aa, 11=E, 12=ih, 13=oh, 14=ou
        private static readonly Dictionary<char, int> VowelToViseme = new Dictionary<char, int>
        {
            { 'a', 10 },
            { 'e', 11 },
            { 'i', 12 },
            { 'o', 13 },
            { 'u', 14 }
        };

        private struct BlendTargetCache
        {
            public SkinnedMeshRenderer Renderer;
            public int BlendShapeIndex;
        }

        private readonly Dictionary<int, List<BlendTargetCache>> _visemeTargets = new Dictionary<int, List<BlendTargetCache>>();
        private readonly List<BlendTargetCache> _allControlledTargets = new List<BlendTargetCache>();
        private readonly HashSet<string> _targetDedup = new HashSet<string>();

        private bool _isAnimating;
        private float _animationStartTime;
        private readonly List<int> _visemeSequence = new List<int>();
        private readonly Dictionary<int, float> _currentVisemeWeights = new Dictionary<int, float>();
        private VoicePipelineManager.PipelineState _lastObservedState = VoicePipelineManager.PipelineState.Idle;

        void Start()
        {
            Debug.Log("[StateLipSync] Initializing...");

            if (pipelineManager == null)
            {
                pipelineManager = FindObjectOfType<VoicePipelineManager>();
                if (pipelineManager == null)
                {
                    Debug.LogError("[StateLipSync] VoicePipelineManager not found!");
                    enabled = false;
                    return;
                }
            }

            if (audioPlayer == null)
            {
                audioPlayer = FindObjectOfType<AudioResponsePlayer>();
            }

            // Subscribe to state changes
            pipelineManager.OnStateChanged += OnPipelineStateChanged;

            // Discover blendshape targets
            DiscoverBlendshapeTargets();

            Debug.Log($"[StateLipSync] Start complete: {_allControlledTargets.Count} viseme target(s) found");
        }

        void OnDestroy()
        {
            if (pipelineManager != null)
            {
                pipelineManager.OnStateChanged -= OnPipelineStateChanged;
            }
        }

        void LateUpdate()
        {
            // Fallback guard: ensure animation state follows pipeline even if event hook misses.
            if (pipelineManager != null && pipelineManager.State != _lastObservedState)
            {
                _lastObservedState = pipelineManager.State;
                OnPipelineStateChanged(_lastObservedState);
            }

            if (!_isAnimating)
            {
                return;
            }

            float elapsed = Time.time - _animationStartTime;

            if (audioPlayer != null && audioPlayer.IsPlaying && _visemeSequence.Count > 0)
            {
                float pos;
                if (audioPlayer.CurrentClip != null && audioPlayer.CurrentClip.length > 0.05f)
                {
                    // Primary clock: follow real playback timeline so long clips stay animated.
                    float normalized = audioPlayer.CurrentPlaybackNormalized;
                    pos = normalized * Mathf.Max(0f, _visemeSequence.Count - 1);
                }
                else
                {
                    // Fallback clock when clip metadata is not ready yet.
                    float stepDuration = ResolveStepDuration(_visemeSequence.Count);
                    pos = elapsed / Mathf.Max(0.01f, stepDuration);
                }

                int i0 = Mathf.Clamp(Mathf.FloorToInt(pos), 0, _visemeSequence.Count - 1);
                int iPrev = Mathf.Max(i0 - 1, 0);
                int i1 = Mathf.Min(i0 + 1, _visemeSequence.Count - 1);
                int i2 = Mathf.Min(i0 + 2, _visemeSequence.Count - 1);
                float blend = i0 >= _visemeSequence.Count - 1 ? 0f : Mathf.Clamp01(pos - Mathf.Floor(pos));

                int visPrev = _visemeSequence[iPrev];
                int visA = _visemeSequence[i0];
                int visB = _visemeSequence[i1];
                int visNext = _visemeSequence[i2];

                float wA = Mathf.Lerp(maxVisemeWeight, 0f, blend);
                float wB = Mathf.Lerp(0f, maxVisemeWeight, blend);

                // Coarticulation: neighboring vowels still influence current mouth shape.
                float wPrev = wA * coarticulationStrength * 0.45f;
                float wNext = wB * coarticulationStrength * 0.45f;

                // Gentle rhythmic accent to avoid mechanical linear blend.
                float accent = 1f + articulationAccent * Mathf.Sin(elapsed * 8.7f);
                wA *= accent;
                wB *= accent;

                ApplyVisemeFrame(visA, wA, visB, wB, visPrev, wPrev, visNext, wNext);
            }
            else
            {
                // Fallback pulse on viseme aa when no transcript sequence.
                float breathe = Mathf.Sin(elapsed * fallbackPulseFrequency * Mathf.PI * 2f);
                float amount = fallbackBaseOpen + fallbackPulseAmplitude * breathe;
                amount = Mathf.Clamp01(amount);
                ApplySingleViseme(10, amount * maxVisemeWeight);
            }
        }

        private float ResolveStepDuration(int sequenceCount)
        {
            if (sequenceCount <= 0)
            {
                return Mathf.Max(0.01f, vowelDuration);
            }

            if (audioPlayer != null && audioPlayer.CurrentClip != null && audioPlayer.CurrentClip.length > 0.05f)
            {
                return Mathf.Max(0.035f, audioPlayer.CurrentClip.length / sequenceCount);
            }

            return Mathf.Max(0.01f, vowelDuration);
        }

        private void OnPipelineStateChanged(VoicePipelineManager.PipelineState newState)
        {
            if (newState == VoicePipelineManager.PipelineState.Speaking)
            {
                Debug.Log("[StateLipSync] State → Speaking: Starting mouth animation");
                _isAnimating = true;
                _animationStartTime = Time.time;

                if (_visemeSequence.Count == 0)
                {
                    // Seed a default sequence so mouth still moves naturally.
                    _visemeSequence.Add(10);
                    _visemeSequence.Add(11);
                    _visemeSequence.Add(13);
                    _visemeSequence.Add(12);
                }
            }
            else if (_isAnimating)
            {
                Debug.Log($"[StateLipSync] State → {newState}: Stopping mouth animation");
                _isAnimating = false;
                SetMouthClosed();
            }
        }

        public void SetTranscriptForAnimation(string transcript)
        {
            if (string.IsNullOrEmpty(transcript))
            {
                return;
            }

            _visemeSequence.Clear();

            string lowered = transcript.ToLowerInvariant();
            int lastViseme = -1;
            for (int i = 0; i < lowered.Length; i++)
            {
                char c = lowered[i];
                if (VowelToViseme.TryGetValue(c, out int viseme))
                {
                    // Avoid immediate duplicates that cause static-looking mouth hold.
                    if (viseme != lastViseme)
                    {
                        _visemeSequence.Add(viseme);
                        lastViseme = viseme;
                    }
                }
            }

            if (_visemeSequence.Count == 0)
            {
                // Default fallback pattern.
                _visemeSequence.Add(10);
                _visemeSequence.Add(11);
                _visemeSequence.Add(13);
            }

            Debug.Log($"[StateLipSync] Transcript: '{transcript}' → {_visemeSequence.Count} viseme step(s)");
        }

        private void DiscoverBlendshapeTargets()
        {
            _visemeTargets.Clear();
            _allControlledTargets.Clear();
            _currentVisemeWeights.Clear();
            _targetDedup.Clear();

            for (int i = 10; i <= 14; i++)
            {
                _visemeTargets[i] = new List<BlendTargetCache>();
                _currentVisemeWeights[i] = 0f;
            }

            var morphTargets = FindObjectsOfType<OVRLipSyncContextMorphTarget>(true);
            if (morphTargets == null || morphTargets.Length == 0)
            {
                Debug.LogWarning("[StateLipSync] No OVRLipSyncContextMorphTarget found. Using fallback viseme animation only.");
                return;
            }

            for (int i = 0; i < morphTargets.Length; i++)
            {
                var mt = morphTargets[i];
                if (mt == null || mt.skinnedMeshRenderer == null || mt.visemeToBlendTargets == null)
                {
                    continue;
                }

                for (int viseme = 10; viseme <= 14; viseme++)
                {
                    if (viseme >= mt.visemeToBlendTargets.Length)
                    {
                        continue;
                    }

                    int blendShapeIndex = mt.visemeToBlendTargets[viseme];
                    if (blendShapeIndex < 0)
                    {
                        continue;
                    }

                    var target = new BlendTargetCache
                    {
                        Renderer = mt.skinnedMeshRenderer,
                        BlendShapeIndex = blendShapeIndex
                    };
                    AddTargetIfUnique(viseme, target);
                }
            }

            // Fallback mapping by blendshape names in case OVR viseme mapping is missing/incomplete.
            AddDirectBlendshapeFallbackTargets();

            for (int viseme = 10; viseme <= 14; viseme++)
            {
                Debug.Log($"[StateLipSync] viseme {viseme} targets: {_visemeTargets[viseme].Count}");
            }

            Debug.Log($"[StateLipSync] Viseme mapping discovered from OVR morph targets: {_allControlledTargets.Count} target entries.");
        }

        private void AddDirectBlendshapeFallbackTargets()
        {
            var renderers = FindObjectsOfType<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var mesh = renderer.sharedMesh;
                for (int j = 0; j < mesh.blendShapeCount; j++)
                {
                    string name = mesh.GetBlendShapeName(j);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    string lower = name.ToLowerInvariant();
                    var target = new BlendTargetCache { Renderer = renderer, BlendShapeIndex = j };

                    if (lower.Contains("jawopen") || lower == "aa" || lower.Contains("viseme_aa"))
                    {
                        AddTargetIfUnique(10, target);
                    }
                    else if (lower == "e" || lower.Contains("viseme_e") || lower.Contains("mouthsmile"))
                    {
                        AddTargetIfUnique(11, target);
                    }
                    else if (lower == "i" || lower.Contains("viseme_i") || lower.Contains("ih"))
                    {
                        AddTargetIfUnique(12, target);
                    }
                    else if (lower == "oh" || lower == "o" || lower.Contains("viseme_oh") || lower.Contains("mouthfunnel"))
                    {
                        AddTargetIfUnique(13, target);
                    }
                    else if (lower == "u" || lower.Contains("ou") || lower.Contains("viseme_ou") || lower.Contains("mouthpucker"))
                    {
                        AddTargetIfUnique(14, target);
                    }
                }
            }
        }

        private void AddTargetIfUnique(int viseme, BlendTargetCache target)
        {
            if (!_visemeTargets.ContainsKey(viseme) || target.Renderer == null || target.BlendShapeIndex < 0)
            {
                return;
            }

            string key = viseme + "|" + target.Renderer.GetInstanceID() + "|" + target.BlendShapeIndex;
            if (_targetDedup.Contains(key))
            {
                return;
            }

            _targetDedup.Add(key);
            _visemeTargets[viseme].Add(target);
            _allControlledTargets.Add(target);
        }

        private void ApplyVisemeFrame(int visA, float wA, int visB, float wB, int visPrev, float wPrev, int visNext, float wNext)
        {
            // Reset all viseme targets first.
            for (int i = 0; i < _allControlledTargets.Count; i++)
            {
                var t = _allControlledTargets[i];
                if (t.Renderer != null && t.BlendShapeIndex >= 0)
                {
                    t.Renderer.SetBlendShapeWeight(t.BlendShapeIndex, 0f);
                }
            }

            ApplyVisemeWeight(visPrev, wPrev);
            ApplyVisemeWeight(visA, wA);
            ApplyVisemeWeight(visB, wB);
            ApplyVisemeWeight(visNext, wNext);
        }

        private void ApplySingleViseme(int viseme, float targetWeight)
        {
            // Reset all viseme targets first.
            for (int i = 0; i < _allControlledTargets.Count; i++)
            {
                var t = _allControlledTargets[i];
                if (t.Renderer != null && t.BlendShapeIndex >= 0)
                {
                    t.Renderer.SetBlendShapeWeight(t.BlendShapeIndex, 0f);
                }
            }

            ApplyVisemeWeight(viseme, targetWeight);
        }

        private void ApplyVisemeWeight(int viseme, float targetWeight)
        {
            if (!_visemeTargets.ContainsKey(viseme))
            {
                return;
            }

            targetWeight = Mathf.Min(targetWeight * GetVisemeMultiplier(viseme), visemeHardCap);

            float prev = _currentVisemeWeights.TryGetValue(viseme, out float v) ? v : 0f;
            float smooth = Mathf.Lerp(prev, targetWeight, 1f - Mathf.Exp(-weightSmoothing * Time.deltaTime));
            _currentVisemeWeights[viseme] = smooth;

            var targets = _visemeTargets[viseme];
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.Renderer != null && target.BlendShapeIndex >= 0)
                {
                    target.Renderer.SetBlendShapeWeight(target.BlendShapeIndex, smooth);
                }
            }
        }

        private float GetVisemeMultiplier(int viseme)
        {
            switch (viseme)
            {
                case 10:
                    return visemeAA;
                case 11:
                    return visemeE;
                case 12:
                    return visemeIH;
                case 13:
                    return visemeOH;
                case 14:
                    return visemeOU;
                default:
                    return 1f;
            }
        }

        private void SetMouthClosed()
        {
            for (int i = 0; i < _allControlledTargets.Count; i++)
            {
                var target = _allControlledTargets[i];
                if (target.Renderer != null && target.BlendShapeIndex >= 0)
                {
                    target.Renderer.SetBlendShapeWeight(target.BlendShapeIndex, 0f);
                }
            }

            for (int viseme = 10; viseme <= 14; viseme++)
            {
                _currentVisemeWeights[viseme] = 0f;
            }
        }
    }
}
