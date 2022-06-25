using System;
using System.IO;
using UnityEngine;

namespace OneJS.Utils {
    public class ImageLoader {
        public static Texture2D Load(string path) {
            path = Path.IsPathRooted(path) ? path : Path.Combine(ScriptEngine.WorkingDir, path);
            var rawData = System.IO.File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2); // Create an empty Texture; size doesn't matter
            tex.LoadImage(rawData);
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }
    }
}