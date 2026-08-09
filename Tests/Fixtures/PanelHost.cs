using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// A real UI Toolkit panel rendering into a RenderTexture.
    ///
    /// Anything that needs a resolved rect needs one of these: elements only get
    /// a contentRect once a panel has laid them out, so features keyed off layout
    /// (particle edge collision, ShaderFX render-target sizing) cannot be tested
    /// against a detached VisualElement. The RenderTexture also makes the result
    /// readable, so a test can assert on pixels rather than on internal state.
    ///
    /// Layout is not synchronous: yield two frames after adding children before
    /// asserting on geometry.
    /// </summary>
    public class PanelHost {
        public RenderTexture rt;
        public PanelSettings ps;
        public GameObject go;
        public VisualElement root;

        public static PanelHost Create(int w, int h, string name = "OneJSTestPanel") {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.targetTexture = rt;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.scale = 1f;
            var go = new GameObject(name);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            return new PanelHost { rt = rt, ps = ps, go = go, root = doc.rootVisualElement };
        }

        /// <summary>Absolutely positioned, explicitly sized child: the shape most hosts need.</summary>
        public VisualElement AddRect(int w, int h, Color? background = null) {
            var ve = new VisualElement();
            Place(ve, w, h);
            if (background.HasValue)
                ve.style.backgroundColor = background.Value;
            root.Add(ve);
            return ve;
        }

        /// <summary>Positions and sizes an existing element, then parents it to the panel root.</summary>
        public T Add<T>(T ve, int w, int h) where T : VisualElement {
            Place(ve, w, h);
            root.Add(ve);
            return ve;
        }

        static void Place(VisualElement ve, int w, int h) {
            ve.style.position = Position.Absolute;
            ve.style.left = 0;
            ve.style.top = 0;
            ve.style.width = w;
            ve.style.height = h;
        }

        /// <summary>
        /// Reads a pixel in UI coordinates: y counts DOWN from the top, matching
        /// how the panel lays out. GetPixel is bottom-up, so the flip happens here
        /// rather than at every call site (an inverted y silently turns an
        /// "above/below" assertion into its opposite).
        /// </summary>
        public Color32 ReadPixel(int x, int yFromTop) {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var c = (Color32)tex.GetPixel(x, rt.height - 1 - yFromTop);
            Object.DestroyImmediate(tex);
            return c;
        }

        public void Destroy() {
            Object.Destroy(go);
            Object.Destroy(ps);
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
