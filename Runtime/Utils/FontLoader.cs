using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Utils {
    public class FontLoader {
        public static Font Load(string path) {
            // TODO more path handling
            var font = new Font(Path.Combine(Application.persistentDataPath, path));
            return font;
        }

        public static FontDefinition LoadDefinition(string path) {
            // TODO more path handling
            var font = new Font(Path.Combine(Application.persistentDataPath, path));
            return FontDefinition.FromFont(font);
        }
    }
}