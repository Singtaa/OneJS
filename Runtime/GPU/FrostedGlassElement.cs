using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.GPU {
    /// <summary>
    /// A VisualElement that displays a frosted glass effect by blurring
    /// the camera's rendered output behind it.
    ///
    /// Usage from OneJS React:
    ///   <FrostedGlass blur={10} tint="rgba(255,255,255,0.15)">
    ///       <Label>Content</Label>
    ///   </FrostedGlass>
    ///
    /// The blur pipeline is fully automatic — no camera or RT setup needed.
    /// </summary>
    [UxmlElement]
    public partial class FrostedGlassElement : VisualElement {
        float _blurRadius = 10f;
        Color _tintColor = new Color(1f, 1f, 1f, 0.15f);
        VisualElement _blurBackground;
        VisualElement _tintOverlay;

        /// <summary>
        /// Blur radius in screen pixels. Higher = more blurry. Default: 10.
        /// </summary>
        [UxmlAttribute("blur")]
        public float BlurRadius {
            get => _blurRadius;
            set => _blurRadius = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Tint color overlaid on the blurred background.
        /// The RGB channels set the tint hue; the alpha controls opacity
        /// (0 = pure blur, 1 = solid tint color).
        /// </summary>
        [UxmlAttribute("tint")]
        public Color TintColor {
            get => _tintColor;
            set {
                _tintColor = value;
                ApplyTint();
            }
        }

        public FrostedGlassElement() {
            style.overflow = Overflow.Hidden;

            // Internal child for the blurred background. Uses counter-rotation to stay
            // screen-aligned when the element (or its ancestors) are rotated.
            _blurBackground = new VisualElement();
            _blurBackground.style.position = Position.Absolute;
            _blurBackground.pickingMode = PickingMode.Ignore;
            hierarchy.Add(_blurBackground);

            // Internal overlay for tint color, rendered ON TOP of blur
            // but BEHIND user children.
            _tintOverlay = new VisualElement();
            _tintOverlay.style.position = Position.Absolute;
            _tintOverlay.style.top = 0;
            _tintOverlay.style.left = 0;
            _tintOverlay.style.right = 0;
            _tintOverlay.style.bottom = 0;
            _tintOverlay.pickingMode = PickingMode.Ignore;
            hierarchy.Add(_tintOverlay);

            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        void OnAttach(AttachToPanelEvent evt) {
            BackdropBlurManager.Register(this);
            ApplyTint();
        }

        void ApplyTint() {
            if (_tintOverlay != null)
                _tintOverlay.style.backgroundColor = _tintColor;
        }

        void OnDetach(DetachFromPanelEvent evt) {
            BackdropBlurManager.Unregister(this);
            if (_blurBackground != null) {
                _blurBackground.style.backgroundImage = StyleKeyword.Null;
                _blurBackground.style.backgroundSize = StyleKeyword.Null;
                _blurBackground.style.backgroundPositionX = StyleKeyword.Null;
                _blurBackground.style.backgroundPositionY = StyleKeyword.Null;
            }
        }

        /// <summary>
        /// Called by BackdropBlurManager each frame with the blurred screen texture.
        /// Positions and counter-rotates the blur child so it stays screen-aligned.
        /// </summary>
        internal void UpdateBlurredBackground(RenderTexture blurredRT, int screenW, int screenH) {
            if (panel == null || _blurBackground == null) return;

            float w = resolvedStyle.width;
            float h = resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0) return;

            var panelRoot = panel.visualTree;
            var panelBounds = panelRoot.worldBound;
            if (panelBounds.width <= 0 || panelBounds.height <= 0) return;

            // Extract cumulative rotation from world transform
            var m = worldTransform;
            float angle = Mathf.Atan2(m.m10, m.m00) * Mathf.Rad2Deg;

            // Size the blur child to the diagonal so it covers the parent at any rotation
            float d = Mathf.Sqrt(w * w + h * h);
            _blurBackground.style.width = d;
            _blurBackground.style.height = d;
            _blurBackground.style.left = (w - d) / 2f;
            _blurBackground.style.top = (h - d) / 2f;

            // Counter-rotate to cancel parent rotation and stay screen-aligned
            _blurBackground.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(-angle)));

            // Set blurred texture
            _blurBackground.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(blurredRT));

            // Compute UV based on the blur child's effective screen-space position.
            // After counter-rotation it occupies a d×d rect centered at the element's world center.
            var center = worldBound.center;
            float childScreenLeft = center.x - d / 2f;
            float childScreenTop = center.y - d / 2f;

            float u = (childScreenLeft - panelBounds.x) / panelBounds.width;
            float v = (childScreenTop - panelBounds.y) / panelBounds.height;

            _blurBackground.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(
                    new Length(panelBounds.width / d * 100f, LengthUnit.Percent),
                    new Length(panelBounds.height / d * 100f, LengthUnit.Percent)
                )
            );

            _blurBackground.style.backgroundPositionX = new StyleBackgroundPosition(
                new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(-(u * panelBounds.width), LengthUnit.Pixel))
            );
            _blurBackground.style.backgroundPositionY = new StyleBackgroundPosition(
                new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(-(v * panelBounds.height), LengthUnit.Pixel))
            );
        }
    }
}
