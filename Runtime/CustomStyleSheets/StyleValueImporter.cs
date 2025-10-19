using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExCSS;
using UnityEngine;

namespace OneJS.CustomStyleSheets {
    public enum URIValidationResult {
        OK,
        InvalidURILocation,
        InvalidURIScheme,
        InvalidURIProjectAssetPath,
    }

    public abstract class StyleValueImporter {
        protected class UnityStylesheetParser : StylesheetParser {
            public UnityStylesheetParser() : base(true, true, true, true, false, false, true, false, null) {
                this.ErrorHandler = new EventHandler<TokenizerError>(this.HandleError);
            }

            public override Stylesheet Parse(string content) {
                this.errors.Clear();
                return base.Parse(content);
            }

            private void HandleError(object sender, TokenizerError tokenizerError) {
                this.errors.Add(tokenizerError);
            }

            public readonly List<TokenizerError> errors = new List<TokenizerError>();
        }

        private struct StoredAsset {
            public UnityEngine.Object resource;

            public ScalableImage si;

            public int index;
        }

        private static StyleSheetImportGlossary s_Glossary;

        private static readonly Dictionary<string, Dimension.Unit> s_UnitNameToDimensionUnit;

        private static Dictionary<string, StyleValueKeyword> s_NameCache;

        private const string k_ResourcePathFunctionName = "resource";

        // protected readonly AssetImportContext m_Context;

        protected readonly UnityStylesheetParser m_Parser;

        protected readonly StyleSheetBuilderWrapper m_Builder;

        internal readonly StyleSheetImportErrors m_Errors;

        protected readonly StyleValidatorWrapper m_Validator;

        protected string m_AssetPath;

        protected int m_CurrentLine;

        private readonly StringBuilder m_StringBuilder = new StringBuilder();

        private static readonly string kThemePrefix;

        internal static StyleSheetImportGlossary glossary => s_Glossary ?? (s_Glossary = new StyleSheetImportGlossary());

        public bool disableValidation { get; set; }

        public StyleSheetImportErrors importErrors => m_Errors;

        public string assetPath => m_AssetPath;

        IScriptEngine _scriptEngine;

        // public StyleValueImporter(AssetImportContext context) {
        //     if (context == null) {
        //         throw new ArgumentNullException("context");
        //     }
        //     m_Context = context;
        //     m_AssetPath = context.assetPath;
        //     m_Parser = new UnityStylesheetParser();
        //     m_Builder = new StyleSheetBuilderWrapper();
        //     m_Errors = new StyleSheetImportErrors {
        //         assetPath = context.assetPath
        //     };
        //     m_Validator = new StyleValidatorWrapper();
        // }

        internal StyleValueImporter(IScriptEngine scriptEngine) {
            _scriptEngine = scriptEngine;
            // m_Context = null;
            m_AssetPath = null;
            m_Parser = new UnityStylesheetParser();
            m_Builder = new StyleSheetBuilderWrapper();
            m_Errors = new StyleSheetImportErrors();
            m_Validator = new StyleValidatorWrapper();
        }

        static StyleValueImporter() {
            s_UnitNameToDimensionUnit = new Dictionary<string, Dimension.Unit> {
                {
                    UnitNames.Px,
                    Dimension.Unit.Pixel
                }, {
                    UnitNames.Percent,
                    Dimension.Unit.Percent
                }, {
                    UnitNames.S,
                    Dimension.Unit.Second
                }, {
                    UnitNames.Ms,
                    Dimension.Unit.Millisecond
                }, {
                    UnitNames.Deg,
                    Dimension.Unit.Degree
                }, {
                    UnitNames.Grad,
                    Dimension.Unit.Gradian
                }, {
                    UnitNames.Rad,
                    Dimension.Unit.Radian
                }, {
                    UnitNames.Turn,
                    Dimension.Unit.Turn
                }
            };
            kThemePrefix = "unity-theme://";
            PseudoClassSelectorFactory.Selectors["selected"] = PseudoClassSelector.Create("selected");
        }

        // public virtual UnityEngine.Object DeclareDependencyAndLoad(string path) {
        //     return DeclareDependencyAndLoad(path, null);
        // }

        // public virtual UnityEngine.Object DeclareDependencyAndLoad(string path, string subAssetPath) {
        //     // // MOD: Disabled theme support 
        //     // if (path.StartsWith(kThemePrefix)) {
        //     //     string text = path.Substring(kThemePrefix.Length);
        //     //     if (!ThemeRegistry.themes.TryGetValue(text, out var value)) {
        //     //         return null;
        //     //     }
        //     //     UnityEngine.Object @object = EditorGUIUtility.Load(value);
        //     //     Debug.Assert(@object != null, "Theme not found searching for '" + text + "' at <" + value + ">.");
        //     //     if (@object != null) {
        //     //         List<UnityEngine.Object> list = DeepCopyAsset(@object);
        //     //         if (list.Count > 0) {
        //     //             list[0].name = text;
        //     //             int num = 0;
        //     //             foreach (UnityEngine.Object item in list) {
        //     //                 m_Context.AddObjectToAsset($"asset {num++}: clonedAsset.name", item);
        //     //             }
        //     //             return list[0];
        //     //         }
        //     //     }
        //     //     return null;
        //     // }
        //     // m_Context?.DependsOnSourceAsset(path);
        //     // if (string.IsNullOrEmpty(subAssetPath)) {
        //     //     return AssetDatabase.LoadMainAssetAtPath(path);
        //     // }
        //     // UnityEngine.Object object2 = AssetDatabase.LoadMainAssetAtPath(path);
        //     // UnityEngine.Object[] array = AssetDatabase.LoadAllAssetsAtPath(path);
        //     // foreach (UnityEngine.Object object3 in array) {
        //     //     if (!(object3 == object2) && object3.name == subAssetPath) {
        //     //         return object3;
        //     //     }
        //     // }
        //     // if (object2 != null && object2.name == subAssetPath) {
        //     //     return object2;
        //     // }
        //     return null;
        // }

        // private static UnityEngine.Object LoadResource(string path) {
        //     // MOD: Disabled StyleSheetResourceUtil usage
        //     // return StyleSheetResourceUtil.LoadResource(path, typeof(UnityEngine.Object));
        //     return Resources.Load<UnityEngine.Object>(path);
        // }

        // // MOD: disabled now that themes are disabled
        // internal static List<UnityEngine.Object> DeepCopyAsset(UnityEngine.Object original) {
        //     StyleSheet styleSheet = original as StyleSheet;
        //     if (styleSheet == null) {
        //         return new List<UnityEngine.Object>();
        //     }
        //     StyleSheet styleSheet2 = UnityEngine.Object.Instantiate(styleSheet);
        //     Dictionary<UnityEngine.Object, List<UnityEngine.Object>> dictionary = new Dictionary<UnityEngine.Object, List<UnityEngine.Object>>();
        //     List<UnityEngine.Object> list = new List<UnityEngine.Object>();
        //     List<ScalableImage> list2 = new List<ScalableImage>();
        //     for (int i = 0; i < styleSheet2.assets.Length; i++) {
        //         UnityEngine.Object @object = styleSheet2.assets[i];
        //         List<UnityEngine.Object> value = null;
        //         if (!dictionary.TryGetValue(@object, out value)) {
        //             value = CloneAsset(@object);
        //             if (value.Count > 0) {
        //                 dictionary[@object] = value;
        //             }
        //         }
        //         if (value != null && value.Count > 0) {
        //             list.Add(value[0]);
        //         }
        //     }
        //     for (int j = 0; j < styleSheet2.scalableImages.Length; j++) {
        //         ScalableImage scalableImage = styleSheet2.scalableImages[j];
        //         List<UnityEngine.Object> value2 = null;
        //         if (!dictionary.TryGetValue(scalableImage.normalImage, out value2)) {
        //             List<UnityEngine.Object> list3 = CloneAsset(scalableImage.normalImage);
        //             List<UnityEngine.Object> list4 = CloneAsset(scalableImage.highResolutionImage);
        //             if (list3.Count > 0 && list4.Count > 0) {
        //                 value2 = new List<UnityEngine.Object> {
        //                     list3[0],
        //                     list4[0]
        //                 };
        //                 dictionary[scalableImage.normalImage] = value2;
        //             }
        //         }
        //         if (value2 != null && value2.Count > 0) {
        //             list2.Add(new ScalableImage {
        //                 normalImage = (value2[0] as Texture2D),
        //                 highResolutionImage = (value2[1] as Texture2D)
        //             });
        //         }
        //     }
        //     Dictionary<string, StoredAsset> dictionary2 = new Dictionary<string, StoredAsset>();
        //     Dictionary<string, StoredAsset> dictionary3 = new Dictionary<string, StoredAsset>();
        //     StyleRule[] rules = styleSheet2.rules;
        //     foreach (StyleRule styleRule in rules) {
        //         StyleProperty[] properties = styleRule.properties;
        //         foreach (StyleProperty styleProperty in properties) {
        //             for (int m = 0; m < styleProperty.values.Length; m++) {
        //                 StyleValueHandle styleValueHandle = styleProperty.values[m];
        //                 if (styleValueHandle.valueType != StyleValueType.ResourcePath) {
        //                     continue;
        //                 }
        //                 string text = styleSheet2.strings[styleValueHandle.valueIndex];
        //                 bool flag = false;
        //                 int num = -1;
        //                 bool flag2 = false;
        //                 int num2 = -1;
        //                 if (dictionary3.TryGetValue(text, out var value3)) {
        //                     num2 = value3.index;
        //                     flag2 = true;
        //                 } else if (dictionary2.TryGetValue(text, out value3)) {
        //                     num = value3.index;
        //                     flag = true;
        //                 } else {
        //                     UnityEngine.Object object2 = LoadResource(text);
        //                     List<UnityEngine.Object> list6 = (dictionary[object2] = CloneAsset(object2));
        //                     if (object2 is Texture2D) {
        //                         string path = Path.Combine(Path.GetDirectoryName(text), Path.GetFileNameWithoutExtension(text) + "@2x" + Path.GetExtension(text));
        //                         UnityEngine.Object object3 = LoadResource(path);
        //                         if (object3 != null) {
        //                             num2 = list2.Count;
        //                             List<UnityEngine.Object> list7 = CloneAsset(object3);
        //                             list2.Add(new ScalableImage {
        //                                 normalImage = (list6[0] as Texture2D),
        //                                 highResolutionImage = (list7[0] as Texture2D)
        //                             });
        //                             dictionary3[text] = new StoredAsset {
        //                                 si = list2[list2.Count - 1],
        //                                 index = num2
        //                             };
        //                             list6.Add(list7[0]);
        //                             dictionary[object2] = list6;
        //                             flag2 = true;
        //                         }
        //                     }
        //                     if (!flag2 && list6.Count > 0) {
        //                         num = list.Count;
        //                         list.AddRange(list6);
        //                         UnityEngine.Object resource = list6[0];
        //                         dictionary2[text] = new StoredAsset {
        //                             resource = resource,
        //                             index = num
        //                         };
        //                         flag = true;
        //                     }
        //                 }
        //                 if (flag) {
        //                     styleValueHandle.valueType = StyleValueType.AssetReference;
        //                     styleValueHandle.valueIndex = num;
        //                     styleProperty.values[m] = styleValueHandle;
        //                 } else if (flag2) {
        //                     styleValueHandle.valueType = StyleValueType.ScalableImage;
        //                     styleValueHandle.valueIndex = num2;
        //                     styleProperty.values[m] = styleValueHandle;
        //                 } else {
        //                     Debug.LogError("ResourcePath was not converted to AssetReference when converting stylesheet :  " + text);
        //                 }
        //             }
        //         }
        //     }
        //     styleSheet2.assets = list.ToArray();
        //     styleSheet2.scalableImages = list2.ToArray();
        //     HashSet<UnityEngine.Object> hashSet = new HashSet<UnityEngine.Object>();
        //     foreach (List<UnityEngine.Object> value4 in dictionary.Values) {
        //         foreach (UnityEngine.Object item in value4) {
        //             hashSet.Add(item);
        //         }
        //     }
        //     List<UnityEngine.Object> list8 = hashSet.ToList();
        //     list8.Insert(0, styleSheet2);
        //     return list8;
        // }

        // private static List<UnityEngine.Object> CloneAsset(UnityEngine.Object o) {
        //     if (o == null) {
        //         return null;
        //     }
        //     List<UnityEngine.Object> list = new List<UnityEngine.Object>();
        //     if (o is Texture2D) {
        //         Texture2D texture2D = new Texture2D(0, 0);
        //         EditorUtility.CopySerialized(o, texture2D);
        //         list.Add(texture2D);
        //     } else if (o is Font) {
        //         Font font = new Font();
        //         EditorUtility.CopySerialized(o, font);
        //         font.hideFlags = HideFlags.None;
        //         list.Add(font);
        //         if (font.material != null) {
        //             Material material = new Material(font.material.shader);
        //             EditorUtility.CopySerialized(font.material, material);
        //             material.hideFlags = HideFlags.None;
        //             font.material = material;
        //             list.Add(material);
        //             if (material.mainTexture != null) {
        //                 Texture2D texture2D2 = new Texture2D(0, 0);
        //                 EditorUtility.CopySerialized(material.mainTexture, texture2D2);
        //                 texture2D2.hideFlags = HideFlags.None;
        //                 material.mainTexture = texture2D2;
        //                 list.Add(texture2D2);
        //             }
        //         }
        //         using SerializedObject serializedObject = new SerializedObject(font);
        //         UnityEngine.Object objectReferenceValue = serializedObject.FindProperty("m_Texture").objectReferenceValue;
        //         if (objectReferenceValue != null) {
        //             if (font.material != null && objectReferenceValue == (o as Font).material.mainTexture) {
        //                 serializedObject.FindProperty("m_Texture").objectReferenceValue = font.material.mainTexture;
        //             } else {
        //                 Texture2D texture2D3 = new Texture2D(0, 0);
        //                 EditorUtility.CopySerialized(objectReferenceValue, texture2D3);
        //                 texture2D3.hideFlags = HideFlags.None;
        //                 serializedObject.FindProperty("m_Texture").objectReferenceValue = font.material.mainTexture;
        //                 list.Add(texture2D3);
        //             }
        //             serializedObject.ApplyModifiedProperties();
        //         }
        //     }
        //     return list;
        // }

        internal static (StyleSheetImportErrorCode, string) ConvertErrorCode(URIValidationResult result) {
            return result switch {
                URIValidationResult.InvalidURILocation => (StyleSheetImportErrorCode.InvalidURILocation, glossary.invalidUriLocation),
                URIValidationResult.InvalidURIScheme => (StyleSheetImportErrorCode.InvalidURIScheme, glossary.invalidUriScheme),
                URIValidationResult.InvalidURIProjectAssetPath => (StyleSheetImportErrorCode.InvalidURIProjectAssetPath, glossary.invalidAssetPath),
                _ => (StyleSheetImportErrorCode.Internal, glossary.internalErrorWithStackTrace),
            };
        }

        private void VisitUrlFunction(string path) {
            var workingDir = _scriptEngine.WorkingDir;
            var fullpath = Path.Combine(workingDir, path);
            if (File.Exists(fullpath)) {
                // test path ends in .jpg or .png
                if (path.EndsWith(".jpg") || path.EndsWith(".png")) {
                    Texture2D tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(fullpath));
                    tex.filterMode = FilterMode.Bilinear;

                    m_Builder.AddValue(tex);

                    // TODO ScalableImage with @2x
                } else if (path.EndsWith(".ttf")) {
                    Font font = new Font(fullpath);
                    m_Builder.AddValue(font);
                } else {
                    m_Errors.AddSemanticError(StyleSheetImportErrorCode.InvalidURILocation,
                        string.Format(StyleValueImporter.glossary.invalidUriLocation, path), m_CurrentLine);
                }
            }
        }

        private bool ValidateFunction(FunctionToken functionToken, out StyleValueFunction func) {
            func = StyleValueFunction.Unknown;
            TextPosition position;
            if (functionToken.ArgumentTokens.Count() == 0) {
                StyleSheetImportErrors errors = m_Errors;
                string message = string.Format(glossary.missingFunctionArgument, ((Token)functionToken).Data);
                position = ((Token)functionToken).Position;
                int line = ((TextPosition)(position)).Line;
                position = ((Token)functionToken).Position;
                errors.AddSemanticError(StyleSheetImportErrorCode.MissingFunctionArgument, message, line, ((TextPosition)(position)).Column);
                return false;
            }
            if (((Token)functionToken).Data == "var") {
                func = StyleValueFunction.Var;
                return ValidateVarFunction(functionToken);
            }
            try {
                func = StyleValueFunctionExtension.FromUssString(((Token)functionToken).Data);
            } catch (Exception) {
                var currentProperty = m_Builder.currentProperty;
                StyleSheetImportErrors errors2 = m_Errors;
                string message2 = string.Format(glossary.unknownFunction, ((Token)functionToken).Data, currentProperty);
                position = ((Token)functionToken).Position;
                int line2 = ((TextPosition)(position)).Line;
                position = ((Token)functionToken).Position;
                errors2.AddValidationWarning(message2, line2, ((TextPosition)(position)).Column);
                return false;
            }
            return true;
        }

        private bool ValidateVarFunction(FunctionToken functionToken) {
            bool flag = false;
            bool flag2 = false;
            var list = new List<Token>();
            foreach (var token in functionToken.ArgumentTokens) {
                list.Add(token);
            }
            ValueExtensions.Trim(list);
            for (int i = 0; i < list.Count; i++) {
                Token val = list[i];
                if ((int)val.Type == 26) {
                    continue;
                }
                TextPosition position;
                if (!flag) {
                    string text = val.ToValue();
                    if (string.IsNullOrEmpty(text)) {
                        StyleSheetImportErrors errors = m_Errors;
                        string missingVariableName = glossary.missingVariableName;
                        position = val.Position;
                        int line = ((TextPosition)(position)).Line;
                        position = val.Position;
                        errors.AddSemanticError(StyleSheetImportErrorCode.InvalidVarFunction, missingVariableName, line, ((TextPosition)(position)).Column);
                        return false;
                    }
                    if (!text.StartsWith("--")) {
                        StyleSheetImportErrors errors2 = m_Errors;
                        string message = string.Format(glossary.missingVariablePrefix, text);
                        position = val.Position;
                        int line2 = ((TextPosition)(position)).Line;
                        position = val.Position;
                        errors2.AddSemanticError(StyleSheetImportErrorCode.InvalidVarFunction, message, line2, ((TextPosition)(position)).Column);
                        return false;
                    }
                    if (text.Length < 3) {
                        StyleSheetImportErrors errors3 = m_Errors;
                        string emptyVariableName = glossary.emptyVariableName;
                        position = val.Position;
                        int line3 = ((TextPosition)(position)).Line;
                        position = val.Position;
                        errors3.AddSemanticError(StyleSheetImportErrorCode.InvalidVarFunction, emptyVariableName, line3, ((TextPosition)(position)).Column);
                        return false;
                    }
                    flag = true;
                } else if ((int)val.Type == 24) {
                    if (flag2) {
                        StyleSheetImportErrors errors4 = m_Errors;
                        string tooManyFunctionArguments = glossary.tooManyFunctionArguments;
                        position = val.Position;
                        int line4 = ((TextPosition)(position)).Line;
                        position = val.Position;
                        errors4.AddSemanticError(StyleSheetImportErrorCode.InvalidVarFunction, tooManyFunctionArguments, line4, ((TextPosition)(position)).Column);
                        return false;
                    }
                    flag2 = true;
                    i++;
                    if (i >= list.Count) {
                        StyleSheetImportErrors errors5 = m_Errors;
                        string emptyFunctionArgument = glossary.emptyFunctionArgument;
                        position = val.Position;
                        int line5 = ((TextPosition)(position)).Line;
                        position = val.Position;
                        errors5.AddSemanticError(StyleSheetImportErrorCode.InvalidVarFunction, emptyFunctionArgument, line5, ((TextPosition)(position)).Column);
                        return false;
                    }
                } else if (!flag2) {
                    string arg = "";
                    while ((int)val.Type == 26 && i + 1 < list.Count) {
                        val = list[++i];
                    }
                    if ((int)val.Type != 26) {
                        arg = val.Data;
                    }
                    StyleSheetImportErrors errors6 = m_Errors;
                    string message2 = string.Format(glossary.unexpectedTokenInFunction, arg);
                    position = val.Position;
                    int line6 = ((TextPosition)(position)).Line;
                    position = val.Position;
                    errors6.AddSemanticError(StyleSheetImportErrorCode.InvalidVarFunction, message2, line6, ((TextPosition)(position)).Column);
                    return false;
                }
            }
            return true;
        }

        protected void VisitValue(Property property) {
            if (IsTokenString((IEnumerable<Token>)property.DeclaredValue.Original)) {
                string value = BuildStringFromTokens((IEnumerable<Token>)property.DeclaredValue.Original);
                if (!string.IsNullOrEmpty(value)) {
                    m_Builder.AddValue(value, StyleValueType.String);
                    return;
                }
            }
            foreach (Token item in property.DeclaredValue.Original) {
                VisitToken(item);
            }
        }

        private void VisitToken(Token token) {
            ColorToken val = (ColorToken)(object)((token is ColorToken) ? token : null);
            TextPosition position;
            UnityEngine.Color color;
            if (val == null) {
                FunctionToken val2 = (FunctionToken)(object)((token is FunctionToken) ? token : null);
                if (val2 == null) {
                    KeywordToken val3 = (KeywordToken)(object)((token is KeywordToken) ? token : null);
                    if (val3 == null) {
                        NumberToken val4 = (NumberToken)(object)((token is NumberToken) ? token : null);
                        if (val4 == null) {
                            StringToken val5 = (StringToken)(object)((token is StringToken) ? token : null);
                            if (val5 == null) {
                                UnitToken val6 = (UnitToken)(object)((token is UnitToken) ? token : null);
                                Dimension.Unit value;
                                if (val6 == null) {
                                    UrlToken val7 = (UrlToken)(object)((token is UrlToken) ? token : null);
                                    if (val7 != null) {
                                        VisitUrlFunction(((Token)val7).Data);
                                        return;
                                    }
                                    TokenType type = token.Type;
                                    TokenType val8 = type;
                                    switch ((int)val8 - 23) {
                                        case 0:
                                        case 3:
                                            return;
                                        case 1:
                                            m_Builder.AddCommaSeparator();
                                            return;
                                    }
                                    StyleSheetImportErrors errors = m_Errors;
                                    string message = string.Format(glossary.unsupportedTerm, token.Data, token.Type);
                                    position = token.Position;
                                    int line = ((TextPosition)(position)).Line;
                                    position = token.Position;
                                    errors.AddSemanticError(StyleSheetImportErrorCode.UnsupportedTerm, message, line, ((TextPosition)(position)).Column);
                                } else if (s_UnitNameToDimensionUnit.TryGetValue(val6.Unit, out value)) {
                                    m_Builder.AddValue(new Dimension(val6.Value, value));
                                } else {
                                    StyleSheetImportErrors errors2 = m_Errors;
                                    string message2 = string.Format(glossary.unsupportedUnit, ((Token)val6).ToValue());
                                    position = ((Token)val6).Position;
                                    int line2 = ((TextPosition)(position)).Line;
                                    position = ((Token)val6).Position;
                                    errors2.AddSemanticError(StyleSheetImportErrorCode.UnsupportedUnit, message2, line2, ((TextPosition)(position)).Column);
                                }
                            } else {
                                m_Builder.AddValue(((Token)val5).Data, StyleValueType.String);
                            }
                        } else {
                            m_Builder.AddValue(val4.Value);
                        }
                    } else if ((int)((Token)val3).Type == 6) {
                        if (TryParseKeyword(((Token)val3).Data, out var value2)) {
                            m_Builder.AddValue(value2);
                        } else if (((Token)val3).Data.StartsWith("--")) {
                            m_Builder.AddValue(((Token)val3).Data, StyleValueType.Variable);
                        } else {
                            m_Builder.AddValue(((Token)val3).Data, StyleValueType.Enum);
                        }
                    } else {
                        StyleSheetImportErrors errors3 = m_Errors;
                        string message3 = string.Format(glossary.unsupportedTerm, ((Token)val3).Data, ((Token)val3).Type);
                        position = ((Token)val3).Position;
                        int line3 = ((TextPosition)(position)).Line;
                        position = ((Token)val3).Position;
                        errors3.AddSemanticError(StyleSheetImportErrorCode.UnsupportedTerm, message3, line3, ((TextPosition)(position)).Column);
                    }
                } else {
                    VisitFunctionToken(val2);
                }
            } else if (ColorUtility.TryParseHtmlString("#" + ((Token)val).Data, out color)) {
                m_Builder.AddValue(color);
            } else {
                StyleSheetImportErrors errors4 = m_Errors;
                string message4 = "Could not parse color token: " + ((Token)val).Data;
                position = ((Token)val).Position;
                int line4 = ((TextPosition)(position)).Line;
                position = ((Token)val).Position;
                errors4.AddSyntaxError(message4, line4, ((TextPosition)(position)).Column);
            }
        }

        private void VisitFunctionToken(FunctionToken functionToken) {
            //IL_0128: Unknown result type (might be due to invalid IL or missing references)
            //IL_012d: Unknown result type (might be due to invalid IL or missing references)
            //IL_0137: Unknown result type (might be due to invalid IL or missing references)
            //IL_013c: Unknown result type (might be due to invalid IL or missing references)
            switch (((Token)functionToken).Data) {
                case "rgb": {
                    if (TryCreateColorFromFunctionToken(functionToken, 3, out var color)) {
                        m_Builder.AddValue(color);
                    }
                    return;
                }
                case "rgba": {
                    if (TryCreateColorFromFunctionToken(functionToken, 4, out var color2)) {
                        m_Builder.AddValue(color2);
                    }
                    return;
                }
                case "resource": {
                    Token obj = functionToken.ArgumentTokens.FirstOrDefault();
                    StringToken val = (StringToken)(object)((obj is StringToken) ? obj : null);
                    if (val != null) {
                        m_Builder.AddValue(((Token)val).Data, StyleValueType.ResourcePath);
                        return;
                    }
                    string value = BuildStringFromTokens(functionToken.ArgumentTokens);
                    if (!string.IsNullOrEmpty(value)) {
                        m_Builder.AddValue(m_StringBuilder.ToString(), StyleValueType.ResourcePath);
                        m_StringBuilder.Clear();
                        return;
                    }
                    StyleSheetImportErrors errors = m_Errors;
                    string data = ((Token)functionToken).Data;
                    TextPosition position = ((Token)functionToken).Position;
                    int line = ((TextPosition)(position)).Line;
                    position = ((Token)functionToken).Position;
                    errors.AddSemanticError(StyleSheetImportErrorCode.MissingFunctionArgument, data, line, ((TextPosition)(position)).Column);
                    return;
                }
                case "none":
                    m_Builder.AddValue((StyleValueFunction)4);
                    VisitCustomFilter(functionToken);
                    return;
                case "filter":
                    m_Builder.AddValue((StyleValueFunction)5);
                    VisitCustomFilter(functionToken);
                    return;
            }
            if (!ValidateFunction(functionToken, out var func)) {
                return;
            }
            m_Builder.AddValue(func);
            m_Builder.AddValue(functionToken.ArgumentTokens.Count((Token t) => (int)t.Type != 26));
            foreach (Token argumentToken in functionToken.ArgumentTokens) {
                VisitToken(argumentToken);
            }
        }

        private bool IsTokenString(IEnumerable<Token> tokens) {
            //IL_0022: Unknown result type (might be due to invalid IL or missing references)
            //IL_002a: Unknown result type (might be due to invalid IL or missing references)
            //IL_0031: Invalid comparison between Unknown and I4
            //IL_0034: Unknown result type (might be due to invalid IL or missing references)
            //IL_003a: Invalid comparison between Unknown and I4
            if (tokens.Count() > 1) {
                foreach (Token token in tokens) {
                    if ((int)token.Type != 0 && (int)token.Type != 15 && (int)token.Type != 6) {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        private string BuildStringFromTokens(IEnumerable<Token> tokens) {
            //IL_0020: Unknown result type (might be due to invalid IL or missing references)
            //IL_0027: Invalid comparison between Unknown and I4
            m_StringBuilder.Clear();
            foreach (Token token in tokens) {
                if ((int)token.Type != 26) {
                    m_StringBuilder.Append(token.Data);
                }
            }
            return m_StringBuilder.ToString();
        }

        private void VisitCustomFilter(FunctionToken functionToken) {
            var list = new List<Token>();
            foreach (var token in functionToken.ArgumentTokens) {
                list.Add(token);
            }
            m_Builder.AddValue(list.Count((Token a) => (int)a.Type != 26));
            if (list.Count > 0) {
                VisitUrlFunction(list[0].Data);
            }
            for (int i = 1; i < list.Count; i++) {
                VisitToken(list[i]);
            }
        }

        private bool TryCreateColorFromFunctionToken(FunctionToken functionToken, int expectedChannels, out UnityEngine.Color color) {
            //IL_00cd: Unknown result type (might be due to invalid IL or missing references)
            //IL_00d2: Unknown result type (might be due to invalid IL or missing references)
            //IL_00dc: Unknown result type (might be due to invalid IL or missing references)
            //IL_00e1: Unknown result type (might be due to invalid IL or missing references)
            bool flag = true;
            color = new UnityEngine.Color(0f, 0f, 0f, 1f);
            int num = 0;
            foreach (Token argumentToken in functionToken.ArgumentTokens) {
                NumberToken val = (NumberToken)(object)((argumentToken is NumberToken) ? argumentToken : null);
                if (val != null) {
                    color[num++] = val.Value;
                    if (!val.IsInteger && num != 4) {
                        flag = false;
                    }
                    if (num == 4) {
                        break;
                    }
                }
            }
            if (num != expectedChannels) {
                StyleSheetImportErrors errors = m_Errors;
                string message = string.Format(glossary.missingFunctionArgument, ((Token)functionToken).Data);
                TextPosition position = ((Token)functionToken).Position;
                int line = ((TextPosition)(position)).Line;
                position = ((Token)functionToken).Position;
                errors.AddSemanticError(StyleSheetImportErrorCode.MissingFunctionArgument, message, line, ((TextPosition)(position)).Column);
                return false;
            }
            if (flag) {
                for (int i = 0; i < Mathf.Min(3, expectedChannels); i++) {
                    color[i] /= 255f;
                }
            }
            return true;
        }

        private static bool TryParseKeyword(string rawStr, out StyleValueKeyword value) {
            if (s_NameCache == null) {
                s_NameCache = new Dictionary<string, StyleValueKeyword>();
                foreach (StyleValueKeyword value2 in Enum.GetValues(typeof(StyleValueKeyword))) {
                    s_NameCache[value2.ToString().ToLowerInvariant()] = value2;
                }
            }
            return s_NameCache.TryGetValue(rawStr.ToLowerInvariant(), out value);
        }
    }
}