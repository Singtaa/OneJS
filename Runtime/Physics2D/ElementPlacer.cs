using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Moves many elements in one call. Each element is registered once under a
    /// number; after that a frame places every one of them by number, centre and
    /// angle in a single array. A 2D physics world's bodies carry their elements
    /// this way (onejs-play's physics2d), so a frame costs one crossing rather
    /// than two style writes an element.
    ///
    /// Placing writes translate and rotate, which apply at draw time, so no body
    /// moves the layout. Elements are held weakly: a panel torn down, or a
    /// context reloaded, takes its elements with it.
    /// </summary>
    public static class ElementPlacer {
        static readonly Dictionary<int, WeakReference<VisualElement>> _elements = new Dictionary<int, WeakReference<VisualElement>>();
        static int _next;

        /// <summary>Keeps an element under a number for PlaceMany. 0 for no element.</summary>
        public static int Register(VisualElement element) {
            if (element == null) return 0;
            _next++;
            _elements[_next] = new WeakReference<VisualElement>(element);
            return _next;
        }

        /// <summary>Forgets a number Register gave.</summary>
        public static void Release(int id) {
            _elements.Remove(id);
        }

        /// <summary>How many elements are registered: for tests and leak checks.</summary>
        public static int Count => _elements.Count;

        /// <summary>
        /// Every [id, x, y, degrees] in <paramref name="data"/>: the element's
        /// centre to (x, y) in its parent's pixels, wherever layout put it, and
        /// turned clockwise by degrees about its centre.
        /// </summary>
        public static void PlaceMany(float[] data) {
            if (data == null) return;
            for (int i = 0; i + 3 < data.Length; i += 4) {
                int id = (int)data[i];
                if (!_elements.TryGetValue(id, out var weak)) continue;
                if (!weak.TryGetTarget(out var e)) {
                    _elements.Remove(id);
                    continue;
                }
                // translate is an offset from where layout put the element's
                // top left, so the target centre becomes an offset from there.
                var r = e.layout;
                float left = Finite(r.x), top = Finite(r.y);
                float halfW = Finite(r.width) / 2, halfH = Finite(r.height) / 2;
                e.style.translate = new Translate(new Length(data[i + 1] - left - halfW), new Length(data[i + 2] - top - halfH));
                e.style.rotate = new Rotate(Angle.Degrees(data[i + 3]));
            }
        }

        /// <summary>An element not laid out yet reports NaN; it is placed as if at its parent's corner with no size.</summary>
        static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0 : v;
    }
}
