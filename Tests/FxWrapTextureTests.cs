using System;
using NUnit.Framework;
using OneJS.Fx;
using UnityEngine;

namespace OneJS.Tests {
    /// <summary>
    /// `image.texture(tex)` starts an fx chain from a texture the caller holds.
    /// Wrapping the same texture again hands back the same handle, so a chain
    /// rebuilt every frame does not mint a handle per frame, and releasing the
    /// handle never destroys the caller's texture.
    /// </summary>
    [TestFixture]
    public class FxWrapTextureTests {
        Texture2D _texture;

        [SetUp]
        public void SetUp() {
            _texture = new Texture2D(2, 2);
        }

        [TearDown]
        public void TearDown() {
            if (_texture != null) UnityEngine.Object.DestroyImmediate(_texture);
        }

        [Test]
        public void WrappingTheSameTexture_ReusesItsHandle() {
            int handles = FxBridge.LiveHandleCount;
            int a = FxBridge.WrapTexture(_texture);
            int b = FxBridge.WrapTexture(_texture);
            Assert.AreEqual(a, b);
            Assert.AreSame(_texture, FxBridge.GetTexture(a));
            Assert.AreEqual(handles + 1, FxBridge.LiveHandleCount);

            FxBridge.Release(a);
            Assert.IsNull(FxBridge.GetTexture(a));
            Assert.IsTrue(_texture != null, "releasing the handle must not destroy the caller's texture");
            Assert.AreEqual(handles, FxBridge.LiveHandleCount);

            int c = FxBridge.WrapTexture(_texture);
            Assert.AreSame(_texture, FxBridge.GetTexture(c), "a released texture wraps again");
            FxBridge.Release(c);
        }

        [Test]
        public void WrappingNull_SaysSo() {
            var e = Assert.Throws<ArgumentNullException>(() => FxBridge.WrapTexture(null));
            StringAssert.Contains("image.texture", e.Message);
        }
    }
}
