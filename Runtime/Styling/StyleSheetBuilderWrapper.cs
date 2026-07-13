using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.CustomStyleSheets {
    /// <summary>
    /// Reflection wrapper around Unity's internal StyleSheetBuilder.
    /// This class uses reflection to call internal Unity APIs for building StyleSheet assets at runtime.
    /// </summary>
    public class StyleSheetBuilderWrapper {
        readonly Type _builderType;
        object _instance;

        // Cached MethodInfo for frequently called methods
        MethodInfo _addValueFloat;
        MethodInfo _addValueColor;
        MethodInfo _addValueObject;
        MethodInfo _addValueDimension;
        MethodInfo _addValueKeyword;
        MethodInfo _addValueStringType;
        MethodInfo _addValueFunction;
        MethodInfo _addResourcePath;

        Type _dimensionType;
        Type _resolvedResourcePathType;
        Type _styleValueKeywordType;
        Type _styleValueTypeType;
        Type _styleValueFunctionType;
        Type _styleSelectorPartType;
        Type _styleSelectorRelationshipType;

        public StyleSheetBuilderWrapper() {
            var assembly = typeof(VisualElement).Assembly;
            _builderType = RequireType(assembly, "UnityEngine.UIElements.StyleSheets.StyleSheetBuilder");
            _instance = Activator.CreateInstance(_builderType);

            // Cache type references
            _dimensionType = RequireType(assembly, "UnityEngine.UIElements.StyleSheets.Dimension");
            _styleValueKeywordType = RequireType(assembly, "UnityEngine.UIElements.StyleValueKeyword");
            _styleValueTypeType = RequireType(assembly, "UnityEngine.UIElements.StyleValueType");
            _styleValueFunctionType = RequireType(assembly, "UnityEngine.UIElements.StyleValueFunction");
            _styleSelectorPartType = RequireType(assembly, "UnityEngine.UIElements.StyleSelectorPart");
            _styleSelectorRelationshipType = RequireType(assembly, "UnityEngine.UIElements.StyleSelectorRelationship");

            // Cache method references
            _addValueFloat = _builderType.GetMethod("AddValue", new[] { typeof(float) });
            _addValueColor = _builderType.GetMethod("AddValue", new[] { typeof(Color) });
            _addValueObject = _builderType.GetMethod("AddValue", new[] { typeof(UnityEngine.Object) });
            _addValueDimension = _builderType.GetMethod("AddValue", new[] { _dimensionType });
            _addValueKeyword = _builderType.GetMethod("AddValue", new[] { _styleValueKeywordType });
            _addValueStringType = _builderType.GetMethod("AddValue", new[] { typeof(string), _styleValueTypeType });
            _addValueFunction = _builderType.GetMethod("AddValue", new[] { _styleValueFunctionType });

            // Unity 6.5 moved ResourcePath values out of the string table: handles now index a
            // dedicated resourcePaths array populated via AddResourcePath(ResolvedResourcePath).
            // Writing them with AddValue(string, ResourcePath) produces handles whose index
            // overruns that (empty) array, so every resource() font/image resolves to an empty
            // path. Feature-detect the new API instead of gating on a version define.
            _resolvedResourcePathType = assembly.GetType("UnityEngine.UIElements.StyleSheets.ResolvedResourcePath");
            _addResourcePath = _resolvedResourcePathType != null
                ? _builderType.GetMethod("AddResourcePath", new[] { _resolvedResourcePathType })
                : null;
        }

        /// <summary>
        /// Discard any in-progress build state by recreating the underlying
        /// StyleSheetBuilder instance. Cheap: the cached Type/MethodInfo lookups are
        /// bound to the type (not the instance) so they remain valid and are not
        /// re-resolved. Call before each compile so a previous compile that threw
        /// mid-rule (leaving a half-open selector/property) cannot bleed scratch
        /// state into the next sheet.
        /// </summary>
        public void Reset() {
            _instance = Activator.CreateInstance(_builderType);
        }

        static Type RequireType(Assembly assembly, string fullName) {
            var t = assembly.GetType(fullName);
            if (t == null) {
                throw new TypeLoadException(
                    $"OneJS could not resolve internal Unity type '{fullName}'. " +
                    "This usually means IL2CPP managed-code stripping removed it " +
                    "(common in WebGL/iOS Release builds). OneJS ships a link.xml " +
                    "at Assets/Singtaa/OneJS/Plugins/link.xml to preserve these - " +
                    "if it's missing or your build still strips, add the type to your project's link.xml.");
            }
            return t;
        }

        public void BuildTo(StyleSheet styleSheet) {
            _builderType.InvokeMember("BuildTo",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _instance, new object[] { styleSheet });
        }

        public void BeginRule(int line) {
            _builderType.InvokeMember("BeginRule",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _instance, new object[] { line });
        }

        public void EndRule() {
            _builderType.InvokeMember("EndRule",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _instance, new object[] { });
        }

        public IDisposable BeginComplexSelector(int specificity) {
            return (IDisposable)_builderType.InvokeMember("BeginComplexSelector",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _instance, new object[] { specificity });
        }

        public void AddSimpleSelector(SelectorPart[] parts, SelectorRelationship relationship) {
            // Convert our SelectorPart array to Unity's internal StyleSelectorPart array
            var partArray = Array.CreateInstance(_styleSelectorPartType, parts.Length);
            for (int i = 0; i < parts.Length; i++) {
                var part = Activator.CreateInstance(_styleSelectorPartType);
                _styleSelectorPartType.GetField("m_Value", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(part, parts[i].Value);
                _styleSelectorPartType.GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(part, (int)parts[i].Type);
                partArray.SetValue(part, i);
            }

            var method = _builderType.GetMethod("AddSimpleSelector",
                new[] { _styleSelectorPartType.MakeArrayType(), _styleSelectorRelationshipType });
            method.Invoke(_instance, new object[] { partArray, (int)relationship });
        }

        public void BeginProperty(string name, int line) {
            _builderType.InvokeMember("BeginProperty",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _instance, new object[] { name, line });
        }

        public void EndProperty() {
            _builderType.InvokeMember("EndProperty",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _instance, new object[] { });
        }

        public void AddCommaSeparator() {
            _builderType.InvokeMember("AddCommaSeparator",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, _instance, new object[] { });
        }

        public void AddValue(float value) {
            _addValueFloat.Invoke(_instance, new object[] { value });
        }

        public void AddValue(Color color) {
            _addValueColor.Invoke(_instance, new object[] { color });
        }

        public void AddValue(UnityEngine.Object obj) {
            _addValueObject.Invoke(_instance, new object[] { obj });
        }

        public void AddValue(float value, DimensionUnit unit) {
            var dimension = Activator.CreateInstance(_dimensionType);
            _dimensionType.GetField("value").SetValue(dimension, value);
            _dimensionType.GetField("unit").SetValue(dimension, (int)unit);
            _addValueDimension.Invoke(_instance, new object[] { dimension });
        }

        public void AddValue(StyleKeyword keyword) {
            _addValueKeyword.Invoke(_instance, new object[] { (int)keyword });
        }

        public void AddValue(string value, StyleValueType type) {
            _addValueStringType.Invoke(_instance, new object[] { value, (int)type });
        }

        public void AddResourcePath(string path) {
            if (_addResourcePath != null) {
                var resolved = Activator.CreateInstance(_resolvedResourcePathType, path);
                _addResourcePath.Invoke(_instance, new object[] { resolved });
            } else {
                AddValue(path, StyleValueType.ResourcePath);
            }
        }

        public void AddValue(StyleFunction function) {
            _addValueFunction.Invoke(_instance, new object[] { (int)function });
        }
    }

    /// <summary>
    /// Represents a part of a CSS selector (class, id, type, pseudo-class, etc.)
    /// </summary>
    public struct SelectorPart {
        public string Value;
        public SelectorType Type;

        public static SelectorPart Class(string name) => new SelectorPart { Value = name, Type = SelectorType.Class };
        public static SelectorPart Id(string name) => new SelectorPart { Value = name, Type = SelectorType.ID };
        public static SelectorPart TypeName(string name) => new SelectorPart { Value = name, Type = SelectorType.Type };
        public static SelectorPart PseudoClass(string name) => new SelectorPart { Value = name, Type = SelectorType.PseudoClass };
        public static SelectorPart Wildcard() => new SelectorPart { Value = "*", Type = SelectorType.Wildcard };
    }

    /// <summary>
    /// Matches Unity's internal StyleSelectorType enum
    /// </summary>
    public enum SelectorType {
        Unknown = 0,
        Wildcard = 1,
        Type = 2,
        Class = 3,
        PseudoClass = 4,
        RecursivePseudoClass = 5,
        ID = 6,
        Predicate = 7,
    }

    /// <summary>
    /// Matches Unity's internal StyleSelectorRelationship enum
    /// </summary>
    public enum SelectorRelationship {
        None = 0,
        Child = 1,
        Descendent = 2,
    }

    /// <summary>
    /// Matches Unity's internal Dimension.Unit enum
    /// </summary>
    public enum DimensionUnit {
        Unitless = 0,
        Pixel = 1,
        Percent = 2,
        Second = 3,
        Millisecond = 4,
        Degree = 5,
        Gradian = 6,
        Radian = 7,
        Turn = 8,
    }

    /// <summary>
    /// Matches Unity's internal StyleValueKeyword enum
    /// </summary>
    public enum StyleKeyword {
        Undefined = 0,
        Null = 1,
        Auto = 2,
        None = 3,
        Initial = 4,
    }

    /// <summary>
    /// Matches Unity's internal StyleValueType enum
    /// </summary>
    public enum StyleValueType {
        Invalid = 0,
        Keyword = 1,
        Float = 2,
        Dimension = 3,
        Color = 4,
        ResourcePath = 5,
        AssetReference = 6,
        Enum = 7,
        Variable = 8,
        String = 9,
        Function = 10,
        CommaSeparator = 11,
        ScalableImage = 12,
        MissingAssetReference = 13,
    }

    /// <summary>
    /// Matches Unity's internal StyleValueFunction enum
    /// </summary>
    public enum StyleFunction {
        Unknown = 0,
        Var = 1,
        Env = 2,
        LinearGradient = 3,
    }
}
