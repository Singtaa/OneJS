using NUnit.Framework;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// ElementPlacer is how a 2D physics world in JS moves the elements its
    /// bodies carry, every frame, in one call. A wrong offset or a dropped id
    /// shows as shapes drawn beside their bodies, which no JS test can see.
    /// </summary>
    public class ElementPlacerTests {
        [Test]
        public void PlacesEachRegisteredElementByCentreAndAngle() {
            var a = new VisualElement();
            var b = new VisualElement();
            int ia = ElementPlacer.Register(a), ib = ElementPlacer.Register(b);
            Assert.AreNotEqual(ia, ib);
            try {
                ElementPlacer.PlaceMany(new float[] { ia, 120, 80, 30, ib, -5, 7, 0 });
                // Not laid out yet: no size, at its parent's corner, so the offset is the centre itself.
                Assert.AreEqual(120f, a.style.translate.value.x.value, 1e-4f);
                Assert.AreEqual(80f, a.style.translate.value.y.value, 1e-4f);
                Assert.AreEqual(30f, a.style.rotate.value.angle.ToDegrees(), 1e-4f);
                Assert.AreEqual(-5f, b.style.translate.value.x.value, 1e-4f);
                Assert.AreEqual(7f, b.style.translate.value.y.value, 1e-4f);
            } finally {
                ElementPlacer.Release(ia);
                ElementPlacer.Release(ib);
            }
        }

        [Test]
        public void SkipsNumbersItDoesNotKnow() {
            var a = new VisualElement();
            int id = ElementPlacer.Register(a);
            ElementPlacer.Release(id);
            Assert.DoesNotThrow(() => ElementPlacer.PlaceMany(new float[] { id, 1, 2, 3, 99999, 4, 5, 6 }));
            Assert.AreEqual(StyleKeyword.Null, a.style.translate.keyword);
            Assert.AreEqual(0, ElementPlacer.Register(null));
        }
    }
}
