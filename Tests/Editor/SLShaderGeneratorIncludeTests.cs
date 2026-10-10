using System.IO;
using NUnit.Framework;
using OneJS.Editor;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The shader generator reads its includes from the OneJS package wherever
    /// it is installed.
    ///
    /// It looked in two fixed folders, Packages/com.singtaa.onejs and
    /// Assets/Singtaa/OneJS. A clone into Assets/OneJS, the one the README
    /// gives, was in neither, so the generator logged that SLCommon.cginc was
    /// missing and every shader language program drew nothing. A git URL install
    /// was never affected: the editor maps Packages/com.singtaa.onejs onto the
    /// package cache, measured in a fresh project.
    /// </summary>
    public class SLShaderGeneratorIncludeTests {
        string _dir;

        [SetUp]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "OneJS-SLIncludes-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown() {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        // A package somewhere neither fixed folder names, whose SLCommon reaches
        // an include the real one does not
        [Test]
        public void Includes_AreReadFromThePackageGiven() {
            File.WriteAllText(Path.Combine(_dir, SLShaderGenerator.RootInclude), "#include \"Elsewhere.cginc\"\n");
            File.WriteAllText(Path.Combine(_dir, "Elsewhere.cginc"), "// nothing further\n");

            CollectionAssert.AreEqual(new[] { SLShaderGenerator.RootInclude, "Elsewhere.cginc" },
                SLShaderGenerator.Includes(_dir));
        }

        [Test]
        public void IncludeDir_IsThisPackagesResources() {
            var dir = SLShaderGenerator.IncludeDir();
            Assert.IsNotNull(dir, "The OneJS package was not found.");
            var root = Path.GetFullPath(OneJSPackage.Root());
            Assert.AreEqual(Path.Combine(root, "Resources", "OneJS"), dir);
            Assert.IsTrue(File.Exists(Path.Combine(dir, SLShaderGenerator.RootInclude)),
                $"{SLShaderGenerator.RootInclude} is not in {dir}.");
        }
    }
}
