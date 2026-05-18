using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    public class WebGLMicPermissionOverlay : MonoBehaviour
    {
        private const string TitleCopy = "Let's get your voice ready";

        private readonly Vector2 _cardBaseSize = new Vector2(740f, 320f);

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private RectTransform _shadowRect;
        private RectTransform _cardRect;
        private RectTransform _buttonRect;
        private RectTransform _loadingRect;

        private Text _titleText;
        private Text _buttonText;

        private Image _buttonFillImage;
        private Image _buttonGlowImage;

        private Button _ctaButton;
        private RuntimeButtonFeedback _buttonFeedback;
        private WaveIndicatorAnimator _waveIndicator;

        private bool _isBuilt;
        private bool _isVisible;
        private bool _isLoading;
        private bool _isTransitioningOut;
        private Coroutine _fadeCoroutine;
        private Coroutine _hideDelayCoroutine;
        private Font _font;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        public event Action OnEnableMicrophoneClicked;

        public bool IsVisible => _isVisible;

        public void Initialize(Action onEnableMicrophoneClicked)
        {
            if (onEnableMicrophoneClicked != null)
            {
                OnEnableMicrophoneClicked += onEnableMicrophoneClicked;
            }

            BuildIfNeeded();
        }

        public void Teardown()
        {
            OnEnableMicrophoneClicked = null;
            if (_ctaButton != null)
            {
                _ctaButton.onClick.RemoveListener(HandleEnableMicrophoneClicked);
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_hideDelayCoroutine != null)
            {
                StopCoroutine(_hideDelayCoroutine);
                _hideDelayCoroutine = null;
            }
        }

        public void Show()
        {
            BuildIfNeeded();

            if (_isVisible && !_isTransitioningOut)
            {
                return;
            }

            _isTransitioningOut = false;

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            if (_hideDelayCoroutine != null)
            {
                StopCoroutine(_hideDelayCoroutine);
                _hideDelayCoroutine = null;
            }

            _isVisible = true;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;

            StartFade(targetAlpha: 1f, duration: 0.32f, deactivateOnComplete: false);
        }

        public void Hide(bool successTransition)
        {
            if ((_isTransitioningOut && gameObject.activeSelf) || (!_isVisible && !gameObject.activeSelf))
            {
                return;
            }

            _isVisible = false;
            _isTransitioningOut = true;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            if (_hideDelayCoroutine != null)
            {
                StopCoroutine(_hideDelayCoroutine);
                _hideDelayCoroutine = null;
            }

            if (successTransition)
            {
                SetLoading(true);
                _hideDelayCoroutine = StartCoroutine(HideAfterDelay(0.12f));
                return;
            }

            StartFade(targetAlpha: 0f, duration: 0.24f, deactivateOnComplete: true);
        }

        public void SetConnectionState(bool hasUserGesture, bool isConnected, bool hasSession)
        {
            if (!_isBuilt)
            {
                return;
            }

            _ = isConnected;
            _ = hasSession;
            SetLoading(hasUserGesture);
        }

        private void Awake()
        {
            BuildIfNeeded();
        }

        private void LateUpdate()
        {
            if (!_isVisible || !_isBuilt)
            {
                return;
            }

            RefreshResponsiveLayout();
        }

        private void OnDestroy()
        {
            Teardown();
        }

        private void HandleEnableMicrophoneClicked()
        {
            if (_isLoading)
            {
                return;
            }

            SetLoading(true);
            OnEnableMicrophoneClicked?.Invoke();
        }

        private void SetLoading(bool loading)
        {
            if (!_isBuilt)
            {
                return;
            }

            _isLoading = loading;
            _ctaButton.interactable = !loading;
            _buttonText.gameObject.SetActive(!loading);
            _loadingRect.gameObject.SetActive(loading);

            if (_buttonFeedback != null)
            {
                _buttonFeedback.SetLoading(loading);
            }
        }

        private IEnumerator HideAfterDelay(float delay)
        {
            float elapsed = 0f;
            while (elapsed < delay)
            {
                if (!_isTransitioningOut)
                {
                    _hideDelayCoroutine = null;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _hideDelayCoroutine = null;

            if (_isTransitioningOut)
            {
                StartFade(targetAlpha: 0f, duration: 0.24f, deactivateOnComplete: true);
            }
        }

        private void StartFade(float targetAlpha, float duration, bool deactivateOnComplete)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }

            _fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha, duration, deactivateOnComplete));
        }

        private IEnumerator FadeRoutine(float targetAlpha, float duration, bool deactivateOnComplete)
        {
            float initialAlpha = _canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = targetAlpha > initialAlpha ? EaseOutCubic(t) : EaseInCubic(t);

                _canvasGroup.alpha = Mathf.Lerp(initialAlpha, targetAlpha, eased);

                float fromScale = targetAlpha > initialAlpha ? 0.965f : 1f;
                float toScale = targetAlpha > initialAlpha ? 1f : 0.985f;
                float scale = Mathf.Lerp(fromScale, toScale, eased);

                _cardRect.localScale = Vector3.one * scale;
                _shadowRect.localScale = Vector3.one * scale;

                yield return null;
            }

            _canvasGroup.alpha = targetAlpha;
            _cardRect.localScale = Vector3.one;
            _shadowRect.localScale = Vector3.one;

            if (deactivateOnComplete)
            {
                gameObject.SetActive(false);
            }

            _isTransitioningOut = targetAlpha <= 0f;
            _isVisible = targetAlpha > 0f;

            _fadeCoroutine = null;
        }

        private void BuildIfNeeded()
        {
            if (_isBuilt)
            {
                return;
            }

            _isBuilt = true;
            EnsureEventSystem();

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
            }

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;

            RectTransform rootRect = transform as RectTransform;
            if (rootRect != null)
            {
                StretchToParent(rootRect, Vector2.zero, Vector2.zero);
            }

            _font = ResolveFont();

            BuildBackground();
            BuildCard();

            _titleText.text = TitleCopy;
            SetConnectionState(hasUserGesture: false, isConnected: false, hasSession: false);

            gameObject.SetActive(false);
        }

        private void BuildBackground()
        {
            Image dimBackdrop = CreateImage("DimBackdrop", transform, new Color(0.02f, 0.04f, 0.08f, 0.78f), raycastTarget: true);
            StretchToParent(dimBackdrop.rectTransform, Vector2.zero, Vector2.zero);

            RectTransform ambientRoot = CreateRectTransform("AmbientPulseRoot", transform);
            StretchToParent(ambientRoot, Vector2.zero, Vector2.zero);

            Sprite pulseSprite = RuntimeSpriteFactory.CreateSoftCircleSprite(
                256,
                new Color(0.4f, 0.86f, 1f, 0.5f),
                new Color(0.2f, 0.38f, 0.5f, 0f));

            Image pulseA;
            Image pulseB;
            Image pulseC;

            RectTransform pulseARect = CreatePulse(ambientRoot, "PulseA", pulseSprite, new Vector2(-460f, 140f), new Vector2(920f, 920f), new Color(0.16f, 0.56f, 0.74f, 0.15f), out pulseA);
            RectTransform pulseBRect = CreatePulse(ambientRoot, "PulseB", pulseSprite, new Vector2(520f, -150f), new Vector2(760f, 760f), new Color(0.19f, 0.62f, 0.83f, 0.17f), out pulseB);
            RectTransform pulseCRect = CreatePulse(ambientRoot, "PulseC", pulseSprite, new Vector2(60f, -310f), new Vector2(1080f, 1080f), new Color(0.14f, 0.47f, 0.65f, 0.12f), out pulseC);

            AmbientPulseAnimator animator = ambientRoot.gameObject.AddComponent<AmbientPulseAnimator>();
            animator.Configure(
                new[] { pulseARect, pulseBRect, pulseCRect },
                new[] { pulseA, pulseB, pulseC });
        }

        private void BuildCard()
        {
            Sprite cardSprite = RuntimeSpriteFactory.CreateRoundedGradientSprite(
                900,
                560,
                50f,
                new Color(0.12f, 0.2f, 0.31f, 0.95f),
                new Color(0.06f, 0.12f, 0.2f, 0.95f),
                1.5f);

            Sprite innerSprite = RuntimeSpriteFactory.CreateRoundedGradientSprite(
                860,
                520,
                44f,
                new Color(0.2f, 0.34f, 0.48f, 0.26f),
                new Color(0.08f, 0.15f, 0.23f, 0.2f),
                1.5f);

            _shadowRect = CreateRectTransform("CardShadow", transform);
            SetAnchorCenter(_shadowRect);
            _shadowRect.sizeDelta = _cardBaseSize;
            _shadowRect.anchoredPosition = new Vector2(0f, -11f);

            Image shadow = _shadowRect.gameObject.AddComponent<Image>();
            shadow.sprite = RuntimeSpriteFactory.CreateRoundedGradientSprite(720, 440, 48f, Color.white, Color.white, 1.5f);
            shadow.color = new Color(0f, 0f, 0f, 0.35f);
            shadow.raycastTarget = false;

            _cardRect = CreateRectTransform("Card", transform);
            SetAnchorCenter(_cardRect);
            _cardRect.sizeDelta = _cardBaseSize;

            Image cardImage = _cardRect.gameObject.AddComponent<Image>();
            cardImage.sprite = cardSprite;
            cardImage.color = Color.white;
            cardImage.raycastTarget = true;

            Outline cardOutline = _cardRect.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = new Color(0.74f, 0.9f, 1f, 0.16f);
            cardOutline.effectDistance = new Vector2(1f, -1f);

            RectTransform innerRect = CreateRectTransform("CardInner", _cardRect);
            StretchToParent(innerRect, new Vector2(12f, 12f), new Vector2(-12f, -12f));

            Image innerImage = innerRect.gameObject.AddComponent<Image>();
            innerImage.sprite = innerSprite;
            innerImage.color = Color.white;
            innerImage.raycastTarget = false;

            RectTransform topSheenRect = CreateRectTransform("TopSheen", _cardRect);
            topSheenRect.anchorMin = new Vector2(0f, 1f);
            topSheenRect.anchorMax = new Vector2(1f, 1f);
            topSheenRect.pivot = new Vector2(0.5f, 1f);
            topSheenRect.offsetMin = new Vector2(20f, -130f);
            topSheenRect.offsetMax = new Vector2(-20f, -14f);

            Image topSheen = topSheenRect.gameObject.AddComponent<Image>();
            topSheen.sprite = RuntimeSpriteFactory.CreateRoundedGradientSprite(
                760,
                220,
                34f,
                new Color(0.44f, 0.86f, 1f, 0.19f),
                new Color(0.18f, 0.42f, 0.58f, 0.03f),
                1.3f);
            topSheen.color = Color.white;
            topSheen.raycastTarget = false;

            BuildCardContent();
            RefreshResponsiveLayout(force: true);
        }

        private void BuildCardContent()
        {
            _titleText = CreateText("Title", _cardRect, TitleCopy, 42, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
            _titleText.rectTransform.offsetMin = new Vector2(40f, -136f);
            _titleText.rectTransform.offsetMax = new Vector2(-40f, -72f);

            _buttonRect = CreateRectTransform("EnableButton", _cardRect);
            _buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            _buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            _buttonRect.pivot = new Vector2(0.5f, 0.5f);
            _buttonRect.sizeDelta = new Vector2(420f, 86f);
            _buttonRect.anchoredPosition = new Vector2(0f, -24f);

            _buttonGlowImage = CreateImage("ButtonGlow", _buttonRect, new Color(0.19f, 0.83f, 1f, 0.3f), raycastTarget: false);
            _buttonGlowImage.sprite = RuntimeSpriteFactory.CreateSoftCircleSprite(
                180,
                new Color(0.35f, 0.9f, 1f, 0.5f),
                new Color(0.16f, 0.42f, 0.56f, 0f));
            StretchToParent(_buttonGlowImage.rectTransform, new Vector2(-24f, -18f), new Vector2(24f, 18f));

            RectTransform fillRect = CreateRectTransform("ButtonFill", _buttonRect);
            StretchToParent(fillRect, Vector2.zero, Vector2.zero);

            _buttonFillImage = fillRect.gameObject.AddComponent<Image>();
            _buttonFillImage.sprite = RuntimeSpriteFactory.CreateRoundedGradientSprite(
                620,
                160,
                36f,
                new Color(0.22f, 0.8f, 0.99f, 0.98f),
                new Color(0.1f, 0.58f, 0.8f, 0.98f),
                1.5f);
            _buttonFillImage.color = Color.white;

            RectTransform sheenRect = CreateRectTransform("ButtonSheen", fillRect);
            sheenRect.anchorMin = new Vector2(0f, 1f);
            sheenRect.anchorMax = new Vector2(1f, 1f);
            sheenRect.pivot = new Vector2(0.5f, 1f);
            sheenRect.offsetMin = new Vector2(10f, -30f);
            sheenRect.offsetMax = new Vector2(-10f, -8f);

            Image sheenImage = sheenRect.gameObject.AddComponent<Image>();
            sheenImage.sprite = RuntimeSpriteFactory.CreateRoundedGradientSprite(
                540,
                60,
                22f,
                new Color(1f, 1f, 1f, 0.32f),
                new Color(1f, 1f, 1f, 0.05f),
                1.2f);
            sheenImage.color = Color.white;
            sheenImage.raycastTarget = false;

            _ctaButton = fillRect.gameObject.AddComponent<Button>();
            _ctaButton.transition = Selectable.Transition.None;
            _ctaButton.targetGraphic = _buttonFillImage;
            _ctaButton.onClick.AddListener(HandleEnableMicrophoneClicked);

            _buttonText = CreateText("ButtonText", fillRect, "Enable Microphone", 27, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            StretchToParent(_buttonText.rectTransform, Vector2.zero, Vector2.zero);

            _loadingRect = CreateRectTransform("LoadingWave", fillRect);
            _loadingRect.anchorMin = new Vector2(0.5f, 0.5f);
            _loadingRect.anchorMax = new Vector2(0.5f, 0.5f);
            _loadingRect.pivot = new Vector2(0.5f, 0.5f);
            _loadingRect.sizeDelta = new Vector2(96f, 34f);
            _loadingRect.anchoredPosition = Vector2.zero;

            RectTransform[] bars = new RectTransform[4];
            for (int i = 0; i < bars.Length; i++)
            {
                RectTransform bar = CreateRectTransform("Bar" + i, _loadingRect);
                bar.anchorMin = new Vector2(0f, 0.5f);
                bar.anchorMax = new Vector2(0f, 0.5f);
                bar.pivot = new Vector2(0.5f, 0.5f);
                bar.sizeDelta = new Vector2(12f, 8f);
                bar.anchoredPosition = new Vector2(14f + i * 22f, 0f);

                Image barImage = bar.gameObject.AddComponent<Image>();
                barImage.sprite = RuntimeSpriteFactory.CreateRoundedGradientSprite(
                    32,
                    64,
                    9f,
                    new Color(1f, 1f, 1f, 0.98f),
                    new Color(0.74f, 0.93f, 1f, 0.92f),
                    1.2f);
                barImage.color = Color.white;
                barImage.raycastTarget = false;

                bars[i] = bar;
            }

            _waveIndicator = _loadingRect.gameObject.AddComponent<WaveIndicatorAnimator>();
            _waveIndicator.Configure(bars);
            _loadingRect.gameObject.SetActive(false);

            _buttonFeedback = fillRect.gameObject.AddComponent<RuntimeButtonFeedback>();
            _buttonFeedback.Configure(_buttonRect, _buttonFillImage, _buttonGlowImage);
        }

        private void RefreshResponsiveLayout(bool force = false)
        {
            int width = Screen.width;
            int height = Screen.height;
            if (!force && width == _lastScreenWidth && height == _lastScreenHeight)
            {
                return;
            }

            _lastScreenWidth = width;
            _lastScreenHeight = height;

            bool compact = width < 860;

            float cardWidth = Mathf.Min(_cardBaseSize.x, width - (compact ? 34f : 72f));
            cardWidth = Mathf.Max(cardWidth, 320f);
            float cardHeight = compact ? 290f : _cardBaseSize.y;

            _cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);
            _shadowRect.sizeDelta = _cardRect.sizeDelta;

            _titleText.fontSize = compact ? 31 : 42;
            _buttonText.fontSize = compact ? 22 : 27;

            _buttonRect.sizeDelta = new Vector2(Mathf.Min(420f, cardWidth - 84f), compact ? 78f : 86f);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystemObject);
        }

        private static float EaseOutCubic(float t)
        {
            float oneMinusT = 1f - t;
            return 1f - oneMinusT * oneMinusT * oneMinusT;
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }

        private static Font ResolveFont()
        {
            try
            {
                string[] families = { "Segoe UI", "Helvetica Neue", "Arial" };
                Font dynamicFont = Font.CreateDynamicFontFromOSFont(families, 16);
                if (dynamicFont != null)
                {
                    return dynamicFont;
                }
            }
            catch
            {
                // Fall through to built-in font.
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static RectTransform CreatePulse(Transform parent, string name, Sprite sprite, Vector2 anchoredPosition, Vector2 sizeDelta, Color color, out Image image)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;

            return rect;
        }

        private static RectTransform CreateRectTransform(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private Text CreateText(string name, Transform parent, string text, int fontSize, FontStyle style, TextAnchor anchor, Color color)
        {
            RectTransform rect = CreateRectTransform(name, parent);
            Text uiText = rect.gameObject.AddComponent<Text>();
            uiText.font = _font;
            uiText.text = text;
            uiText.fontSize = fontSize;
            uiText.fontStyle = style;
            uiText.alignment = anchor;
            uiText.color = color;
            uiText.supportRichText = false;
            uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;
            uiText.raycastTarget = false;
            return uiText;
        }

        private static void StretchToParent(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetAnchorCenter(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private sealed class AmbientPulseAnimator : MonoBehaviour
        {
            private RectTransform[] _pulses;
            private Image[] _images;

            public void Configure(RectTransform[] pulses, Image[] images)
            {
                _pulses = pulses;
                _images = images;
            }

            private void Update()
            {
                if (_pulses == null || _images == null)
                {
                    return;
                }

                float t = Time.unscaledTime;
                for (int i = 0; i < _pulses.Length; i++)
                {
                    float phase = t * (0.62f + i * 0.17f) + i * 1.25f;
                    float pulse = 1f + Mathf.Sin(phase) * 0.06f;
                    _pulses[i].localScale = Vector3.one * pulse;

                    Color baseColor = _images[i].color;
                    baseColor.a = Mathf.Clamp01(baseColor.a * (0.74f + Mathf.Sin(phase * 0.9f) * 0.22f));
                    _images[i].color = baseColor;
                }
            }
        }

        private sealed class WaveIndicatorAnimator : MonoBehaviour
        {
            private RectTransform[] _bars;

            public void Configure(RectTransform[] bars)
            {
                _bars = bars;
            }

            private void Update()
            {
                if (_bars == null || _bars.Length == 0)
                {
                    return;
                }

                float t = Time.unscaledTime * 5.4f;
                for (int i = 0; i < _bars.Length; i++)
                {
                    float wave = 0.5f + 0.5f * Mathf.Sin(t + i * 0.75f);
                    float h = Mathf.Lerp(7f, 24f, wave);
                    Vector2 size = _bars[i].sizeDelta;
                    size.y = h;
                    _bars[i].sizeDelta = size;
                }
            }
        }

        private sealed class RuntimeButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
        {
            private RectTransform _target;
            private Image _fillImage;
            private Image _glowImage;
            private bool _isHovered;
            private bool _isPressed;
            private bool _isLoading;

            public void Configure(RectTransform target, Image fillImage, Image glowImage)
            {
                _target = target;
                _fillImage = fillImage;
                _glowImage = glowImage;
            }

            public void SetLoading(bool loading)
            {
                _isLoading = loading;
                _isHovered = false;
                _isPressed = false;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (_isLoading)
                {
                    return;
                }

                _isHovered = true;
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                _isHovered = false;
                _isPressed = false;
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                if (_isLoading)
                {
                    return;
                }

                _isPressed = true;
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _isPressed = false;
            }

            private void Update()
            {
                if (_target == null || _fillImage == null || _glowImage == null)
                {
                    return;
                }

                float targetScale = 1f;
                if (!_isLoading)
                {
                    if (_isPressed)
                    {
                        targetScale = 0.97f;
                    }
                    else if (_isHovered)
                    {
                        targetScale = 1.03f;
                    }
                }

                _target.localScale = Vector3.Lerp(_target.localScale, Vector3.one * targetScale, Time.unscaledDeltaTime * 16f);

                Color fillColor;
                if (_isLoading)
                {
                    fillColor = new Color(0.14f, 0.56f, 0.77f, 0.97f);
                }
                else if (_isPressed)
                {
                    fillColor = new Color(0.11f, 0.54f, 0.75f, 0.98f);
                }
                else if (_isHovered)
                {
                    fillColor = new Color(0.28f, 0.86f, 1f, 0.98f);
                }
                else
                {
                    fillColor = Color.white;
                }

                _fillImage.color = Color.Lerp(_fillImage.color, fillColor, Time.unscaledDeltaTime * 12f);

                float glowAlpha;
                if (_isLoading)
                {
                    glowAlpha = 0.44f;
                }
                else if (_isPressed)
                {
                    glowAlpha = 0.2f;
                }
                else if (_isHovered)
                {
                    glowAlpha = 0.58f;
                }
                else
                {
                    glowAlpha = 0.32f;
                }

                Color glowColor = _glowImage.color;
                glowColor.a = Mathf.Lerp(glowColor.a, glowAlpha, Time.unscaledDeltaTime * 11f);
                _glowImage.color = glowColor;
            }
        }

        private static class RuntimeSpriteFactory
        {
            public static Sprite CreateRoundedGradientSprite(int width, int height, float radius, Color topColor, Color bottomColor, float edgeSoftness)
            {
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };

                Color[] pixels = new Color[width * height];

                float halfWidth = width * 0.5f;
                float halfHeight = height * 0.5f;
                float clampedRadius = Mathf.Min(radius, Mathf.Min(halfWidth, halfHeight) - 1f);

                for (int y = 0; y < height; y++)
                {
                    float v = y / (height - 1f);
                    Color gradientColor = Color.Lerp(bottomColor, topColor, v);

                    for (int x = 0; x < width; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f - halfWidth, y + 0.5f - halfHeight);
                        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y))
                                    - new Vector2(halfWidth - clampedRadius, halfHeight - clampedRadius);

                        float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                        float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
                        float distance = outside + inside - clampedRadius;

                        float alpha = Mathf.Clamp01(1f - Mathf.Max(distance, 0f) / Mathf.Max(0.25f, edgeSoftness));

                        Color pixel = gradientColor;
                        pixel.a *= alpha;
                        pixels[y * width + x] = pixel;
                    }
                }

                texture.SetPixels(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
            }

            public static Sprite CreateSoftCircleSprite(int size, Color centerColor, Color edgeColor)
            {
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };

                Color[] pixels = new Color[size * size];
                float radius = size * 0.5f;
                Vector2 center = new Vector2(radius, radius);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                        float t = Mathf.Clamp01(distance / radius);

                        Color color = Color.Lerp(centerColor, edgeColor, t);
                        float falloff = Mathf.SmoothStep(1f, 0f, t);
                        color.a *= falloff;

                        pixels[y * size + x] = color;
                    }
                }

                texture.SetPixels(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
