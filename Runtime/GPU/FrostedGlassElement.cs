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
    public class FrostedGlassElement : VisualElement {
        public new class UxmlFactory : UxmlFactory<FrostedGlassElement, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits {
            UxmlFloatAttributeDescription _blur = new UxmlFloatAttributeDescription {
                name = "blur",
                defaultValue = 10f
            };
            UxmlColorAttributeDescription _tint = new UxmlColorAttributeDescription {
                name = "tint",
                defaultValue = new Color(1f, 1f, 1f, 0.15f)
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc) {
                base.Init(ve, bag, cc);
                var el = (FrostedGlassElement)ve;
                el.BlurRadius = _blur.GetValueFromBag(bag, cc);
                el.TintColor = _tint.GetValueFromBag(bag, cc);
            }
        }

        float _blurRadius = 10f;
        Color _tintColor = new Color(1f, 1f, 1f, 0.15f);
        VisualElement _tintOverlay;

        /// <summary>
        /// Blur radius in screen pixels. Higher = more blurry. Default: 10.
        /// </summary>
        public float BlurRadius {
            get => _blurRadius;
            set => _blurRadius = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Tint color overlaid on the blurred background.
        /// The RGB channels set the tint hue; the alpha controls opacity
        /// (0 = pure blur, 1 = solid tint color).
        /// </summary>
        public Color TintColor {
            get => _tintColor;
            set {
                _tintColor = value;
                ApplyTint();
            }
        }

        public FrostedGlassElement() {
            style.overflow = Overflow.Hidden;

            // Internal overlay for tint color, rendered ON TOP of backgroundImage (blur)
            // but BEHIND user children (added at index 0 before any React children).
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
            style.backgroundImage = StyleKeyword.Null;
            style.backgroundSize = StyleKeyword.Null;
            style.backgroundPositionX = StyleKeyword.Null;
            style.backgroundPositionY = StyleKeyword.Null;
        }

        /// <summary>
        /// Called by BackdropBlurManager each frame with the blurred screen texture.
        /// Computes UV crop based on this element's screen-space position.
        /// </summary>
        internal void UpdateBlurredBackground(RenderTexture blurredRT, int screenW, int screenH) {
            if (panel == null || float.IsNaN(worldBound.width) || worldBound.width <= 0) return;

            // Set blurred texture as background (full opacity — tint is handled by _tintOverlay)
            style.backgroundImage = new StyleBackground(Background.FromRenderTexture(blurredRT));

            // Compute UV crop: map element's world bounds to normalized screen coords
            var bounds = worldBound;
            var panelRoot = panel.visualTree;
            var panelBounds = panelRoot.worldBound;

            if (panelBounds.width <= 0 || panelBounds.height <= 0) return;

            // Normalize element position within the panel (0..1)
            float u = (bounds.x - panelBounds.x) / panelBounds.width;
            float v = (bounds.y - panelBounds.y) / panelBounds.height;

            float scaleX = panelBounds.width / bounds.width * 100f;
            float scaleY = panelBounds.height / bounds.height * 100f;

            style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(
                    new Length(scaleX, LengthUnit.Percent),
                    new Length(scaleY, LengthUnit.Percent)
                )
            );

            float posX = -(u * panelBounds.width);
            float posY = -(v * panelBounds.height);

            style.backgroundPositionX = new StyleBackgroundPosition(
                new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(posX, LengthUnit.Pixel))
            );
            style.backgroundPositionY = new StyleBackgroundPosition(
                new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(posY, LengthUnit.Pixel))
            );
        }
    }
}
