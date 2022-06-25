using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Utils {
    public class FontLoader {
        public static Font Load(string path) {
            path = Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(ScriptEngine.WorkingDir, path));
            var font = new Font(path);
            return font;
        }

        public static FontDefinition LoadDefinition(string path) {
            path = Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(ScriptEngine.WorkingDir, path));
            var font = new Font(path);
            return FontDefinition.FromFont(font);
        }
    }
}