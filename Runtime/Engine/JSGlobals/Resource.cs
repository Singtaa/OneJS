using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Engine.JSGlobals {
    public class Resource {
        public static void Setup(ScriptEngine engine) {
            var resource = new Resource(engine);
            engine.JintEngine.SetValue("resource", resource);
        }

        ScriptEngine _engine;

        public Resource(ScriptEngine engine) {
            _engine = engine;
        }

        public Font loadFont(string path) {
            path = Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(_engine.WorkingDir, path));
            var font = new Font(path);
            return font;
        }

        public FontDefinition loadFontDefinition(string path) {
            path = Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(_engine.WorkingDir, path));
            var font = new Font(path);
            return FontDefinition.FromFont(font);
        }

        public Texture2D loadImage(string path) {
            path = Path.IsPathRooted(path) ? path : Path.Combine(_engine.WorkingDir, path);
            var rawData = System.IO.File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2); // Create an empty Texture; size doesn't matter
            tex.LoadImage(rawData);
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }
    }
}