using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.VectorGraphics;

namespace OneJS {
    /// <summary>
    /// Utility for loading SVG content as VectorImage at runtime.
    /// Used by JS-side loadImage() for .svg files.
    ///
    /// This helper exists because SVGParser.ImportSVG returns a SceneInfo struct
    /// that cannot survive the JS interop boundary (struct serialization loses
    /// complex reference-type properties). By doing the full pipeline in C#,
    /// only the final VectorImage (a ScriptableObject) crosses the boundary.
    /// </summary>
    public static class SVGUtils {
        public static VectorImage LoadFromString(string svgContent) {
            using var reader = new StringReader(svgContent);
            var sceneInfo = SVGParser.ImportSVG(reader);
            return VectorUtils.BuildVectorImage(sceneInfo);
        }
    }
}
