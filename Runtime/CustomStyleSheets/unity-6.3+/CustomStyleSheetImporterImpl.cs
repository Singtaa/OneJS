using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExCSS;
using UnityEngine;

namespace OneJS.CustomStyleSheets {
    public class CustomStyleSheetImporterImpl : StyleValueImporter {
        public CustomStyleSheetImporterImpl(IScriptEngine scriptEngine) : base(scriptEngine) {
        }

        public void BuildStyleSheet(UnityEngine.UIElements.StyleSheet asset, string contents) {
            ExCSS.Stylesheet styleSheet = ((StylesheetParser)this.m_Parser).Parse(contents);
            this.ImportParserStyleSheet(asset, styleSheet, m_Parser.errors);
            Hash128 hash = new Hash128();
            byte[] bytes = Encoding.UTF8.GetBytes(contents);
            if (bytes.Length != 0)
                HashUtilities.ComputeHash128(bytes, ref hash);
            asset.contentHash = hash.GetHashCode();
        }

        void AddUssParserError(TokenizerError error) {
            ParseError parseError = (ParseError)error.Code;
            string arg = error.Message;
            bool flag = parseError == ParseError.InvalidBlockStart;
            if (flag) {
                arg = "Invalid block start, no selector found before the opening curly bracket.";
            }
            string arg2 = string.Format("{0} : {1}", (ParseError)error.Code, arg);
            this.m_Errors.AddSyntaxError(string.Format(StyleValueImporter.glossary.ussParsingError, arg2), error.Position.Line, error.Position.Column);
        }

        protected void ImportParserStyleSheet(UnityEngine.UIElements.StyleSheet asset, ExCSS.Stylesheet styleSheet, List<TokenizerError> errors) {
            this.m_Errors.assetPath = this.assetPath;
            if (errors.Count > 0) {
                foreach (TokenizerError error in errors) {
                    AddUssParserError(error);
                }
            } else {
                // try {
                this.VisitSheet(styleSheet);
                // } catch (Exception ex) {
                // this.m_Errors.AddInternalError(
                //     string.Format(StyleValueImporter.glossary.internalErrorWithStackTrace, (object)ex.Message,
                //         (object)ex.StackTrace), this.m_CurrentLine);
                // }
            }
            bool hasErrors = this.m_Errors.hasErrors;
            if (!hasErrors) {
                this.m_Builder.BuildTo(asset);
            }
        }

        int GetPropertyLine(Property property) {
            return property.DeclaredValue.Original[0].Position.Line;
        }

        void VisitSheet(Stylesheet styleSheet) {
            foreach (IStyleRule styleRule in styleSheet.StyleRules) {
                this.m_Builder.BeginRule(styleRule.StylesheetText.Range.Start.Line);
                this.m_CurrentLine = styleRule.StylesheetText.Range.Start.Line;
                this.VisitBaseSelector(styleRule.Selector);
                foreach (Property property in styleRule.Style.Declarations) {
                    int propertyLine = this.GetPropertyLine(property);
                    this.m_CurrentLine = propertyLine;
                    this.ValidateProperty(property);
                    this.m_Builder.BeginProperty(property.Name, propertyLine);
                    base.VisitValue(property);
                    this.m_Builder.EndProperty();
                }
                this.m_Builder.EndRule();
            }
        }

        void VisitBaseSelector(ISelector selector) {
            AllSelector allSelector = selector as AllSelector;
            if (allSelector == null) {
                ClassSelector classSelector = selector as ClassSelector;
                if (classSelector == null) {
                    ComplexSelector complexSelector = selector as ComplexSelector;
                    if (complexSelector == null) {
                        CompoundSelector compoundSelector = selector as CompoundSelector;
                        if (compoundSelector == null) {
                            IdSelector idSelector = selector as IdSelector;
                            if (idSelector == null) {
                                ListSelector listSelector = selector as ListSelector;
                                if (listSelector == null) {
                                    PseudoClassSelector pseudoClassSelector = selector as PseudoClassSelector;
                                    if (pseudoClassSelector == null) {
                                        TypeSelector typeSelector = selector as TypeSelector;
                                        if (typeSelector == null) {
                                            UnknownSelector unknownSelector = selector as UnknownSelector;
                                            if (unknownSelector == null) {
                                                this.m_Errors.AddSemanticError(StyleSheetImportErrorCode.UnsupportedSelectorFormat, string.Format(StyleValueImporter.glossary.unsupportedSelectorFormat, selector.GetType().Name + ": `" + selector.Text + "`"), this.m_CurrentLine, -1);
                                            } else {
                                                this.VisitUnknownSelector(unknownSelector);
                                            }
                                        } else {
                                            this.VisitSelectorParts(new StyleSelectorPart[] {
                                                StyleSelectorPart.CreateType(typeSelector.Name)
                                            }, typeSelector);
                                        }
                                    } else {
                                        this.ValidatePsuedoClassName(pseudoClassSelector.Class, pseudoClassSelector.Text);
                                        this.VisitSelectorParts(new StyleSelectorPart[] {
                                            StyleSelectorPart.CreatePseudoClass(pseudoClassSelector.Class)
                                        }, pseudoClassSelector);
                                    }
                                } else {
                                    foreach (ISelector selector2 in listSelector) {
                                        this.VisitBaseSelector(selector2);
                                    }
                                }
                            } else {
                                this.VisitSelectorParts(new StyleSelectorPart[] {
                                    StyleSelectorPart.CreateId(idSelector.Id)
                                }, idSelector);
                            }
                        } else {
                            StyleSelectorPart[] parts;
                            bool flag = this.TryExtractSelectorsParts(compoundSelector, out parts);
                            if (flag) {
                                this.VisitSelectorParts(parts, compoundSelector);
                            }
                        }
                    } else {
                        this.VisitComplexSelector(complexSelector);
                    }
                } else {
                    this.VisitSelectorParts(new StyleSelectorPart[] {
                        StyleSelectorPart.CreateClass(classSelector.Class)
                    }, classSelector);
                }
            } else {
                this.VisitSelectorParts(new StyleSelectorPart[] {
                    StyleSelectorPart.CreateWildCard()
                }, allSelector);
            }
        }

        void ValidatePsuedoClassName(string name, string selector) {
            bool flag = !base.disableValidation && !PseudoClassSelectorFactory.Selectors.ContainsKey(name);
            if (flag) {
                this.m_Errors.AddValidationWarning(string.Format(StyleValueImporter.glossary.unknownPsuedoClass, name, selector), this.m_CurrentLine, -1);
            }
        }

        // Token: 0x0600CC20 RID: 52256 RVA: 0x003BC304 File Offset: 0x003BA504
        void VisitUnknownSelector(UnknownSelector unknownSelector) {
            string text = unknownSelector.Text;
            bool flag = text.StartsWith(".") && text.Length > 1;
            if (flag) {
                bool flag2 = char.IsDigit(text[1]) || (text.Length >= 2 && text[1] == '-' && char.IsDigit(text[2]));
                if (flag2) {
                    this.m_Errors.AddSemanticError(StyleSheetImportErrorCode.UnsupportedSelectorFormat, string.Format(StyleValueImporter.glossary.selectorStartsWithDigitFormat, unknownSelector.Text), this.m_CurrentLine, -1);
                    return;
                }
            }
            this.m_Errors.AddSemanticError(StyleSheetImportErrorCode.UnsupportedSelectorFormat, string.Format(StyleValueImporter.glossary.unsupportedSelectorFormat, unknownSelector.Text), this.m_CurrentLine, -1);
        }

        void VisitSelectorParts(StyleSelectorPart[] parts, ISelector selector) {
            int selectorSpecificity = CSSSpec.GetSelectorSpecificity(parts);
            bool flag = selectorSpecificity == 0;
            if (flag) {
                this.m_Errors.AddInternalError(string.Format(StyleValueImporter.glossary.internalError, "Failed to calculate selector specificity " + selector.Text), this.m_CurrentLine);
            } else {
                using (this.m_Builder.BeginComplexSelector(selectorSpecificity)) {
                    this.m_Builder.AddSimpleSelector(parts, 0);
                }
            }
        }

        bool TryExtractSelectorsParts(Selectors selectors, out StyleSelectorPart[] parts) {
            parts = new StyleSelectorPart[selectors.Length];
            for (int i = 0; i < selectors.Length; i++) {
                ISelector selector = selectors[i];
                ISelector selector2 = selector;
                if (!(selector2 is AllSelector)) {
                    IdSelector idSelector = selector2 as IdSelector;
                    if (idSelector == null) {
                        ClassSelector classSelector = selector2 as ClassSelector;
                        if (classSelector == null) {
                            PseudoClassSelector pseudoClassSelector = selector2 as PseudoClassSelector;
                            if (pseudoClassSelector == null) {
                                TypeSelector typeSelector = selector2 as TypeSelector;
                                if (typeSelector == null) {
                                    if (!(selector2 is FirstChildSelector)) {
                                        StyleSelectorPart[] array = parts;
                                        int num = i;
                                        StyleSelectorPart styleSelectorPart = default(StyleSelectorPart);
                                        styleSelectorPart.type = 0;
                                        array[num] = styleSelectorPart;
                                    } else {
                                        StyleSelectorPart[] array2 = parts;
                                        int num2 = i;
                                        StyleSelectorPart styleSelectorPart = default(StyleSelectorPart);
                                        styleSelectorPart.type = (StyleSelectorType)5;
                                        array2[num2] = styleSelectorPart;
                                    }
                                } else {
                                    parts[i] = StyleSelectorPart.CreateType(typeSelector.Name);
                                }
                            } else {
                                bool flag = pseudoClassSelector.Class.Contains("(");
                                if (flag) {
                                    this.m_Errors.AddSemanticError(StyleSheetImportErrorCode.RecursiveSelectorDetected, string.Format(StyleValueImporter.glossary.unsupportedSelectorFormat, selectors.Text), this.m_CurrentLine, -1);
                                    return false;
                                }
                                parts[i] = StyleSelectorPart.CreatePseudoClass(pseudoClassSelector.Class);
                            }
                        } else {
                            parts[i] = StyleSelectorPart.CreateClass(classSelector.Class);
                        }
                    } else {
                        parts[i] = StyleSelectorPart.CreateId(idSelector.Id);
                    }
                } else {
                    parts[i] = StyleSelectorPart.CreateWildCard();
                }
            }
            return true;
        }

        void VisitComplexSelector(ComplexSelector complexSelector) {
            int selectorSpecificity = CSSSpec.GetSelectorSpecificity(complexSelector.Text);
            bool flag = selectorSpecificity == 0;
            if (flag) {
                this.m_Errors.AddInternalError(string.Format(StyleValueImporter.glossary.internalError, "Failed to calculate selector specificity " + ((complexSelector != null) ? complexSelector.ToString() : null)), this.m_CurrentLine);
            } else {
                using (this.m_Builder.BeginComplexSelector(selectorSpecificity)) {
                    StyleSelectorRelationship styleSelectorRelationship = 0;
                    int num = complexSelector.Length - 1;
                    int num2 = -1;
                    foreach (CombinatorSelector combinatorSelector in complexSelector) {
                        num2++;
                        string text = combinatorSelector.Selector.Text;
                        bool flag2 = string.IsNullOrEmpty(text);
                        if (flag2) {
                            this.m_Errors.AddInternalError(string.Format(StyleValueImporter.glossary.internalError, "Expected simple selector inside complex selector " + text), this.m_CurrentLine);
                            break;
                        }
                        StyleSelectorPart[] array;
                        bool flag3 = this.CheckSimpleSelector(text, out array);
                        if (!flag3) {
                            break;
                        }
                        this.m_Builder.AddSimpleSelector(array, styleSelectorRelationship);
                        bool flag4 = num2 != num;
                        if (flag4) {
                            bool flag5 = combinatorSelector.Delimiter == Combinators.Child;
                            if (flag5) {
                                styleSelectorRelationship = (StyleSelectorRelationship)1;
                            } else {
                                bool flag6 = combinatorSelector.Delimiter == Combinators.Descendent;
                                if (!flag6) {
                                    this.m_Errors.AddSemanticError(StyleSheetImportErrorCode.InvalidComplexSelectorDelimiter, string.Format(StyleValueImporter.glossary.invalidComplexSelectorDelimiter, complexSelector.Text), this.m_CurrentLine, -1);
                                    break;
                                }
                                styleSelectorRelationship = (StyleSelectorRelationship)2;
                            }
                        }
                    }
                }
            }
        }

        bool CheckSimpleSelector(string selector, out StyleSelectorPart[] parts) {
            bool flag = !CSSSpec.ParseSelector(selector, out parts);
            bool result;
            if (flag) {
                this.m_Errors.AddSemanticError(StyleSheetImportErrorCode.UnsupportedSelectorFormat, string.Format(StyleValueImporter.glossary.unsupportedSelectorFormat, selector), this.m_CurrentLine, -1);
                result = false;
            } else {
                bool flag2 = parts.Any((StyleSelectorPart p) => p.type == 0);
                if (flag2) {
                    this.m_Errors.AddSemanticError(StyleSheetImportErrorCode.UnsupportedSelectorFormat, string.Format(StyleValueImporter.glossary.unsupportedSelectorFormat, selector), this.m_CurrentLine, -1);
                    result = false;
                } else {
                    bool flag3 = parts.Any((StyleSelectorPart p) => p.type == (StyleSelectorType)5);
                    if (flag3) {
                        this.m_Errors.AddSemanticError(StyleSheetImportErrorCode.RecursiveSelectorDetected, string.Format(StyleValueImporter.glossary.unsupportedSelectorFormat, selector), this.m_CurrentLine, -1);
                        result = false;
                    } else {
                        bool flag4 = !base.disableValidation;
                        if (flag4) {
                            foreach (StyleSelectorPart styleSelectorPart in parts) {
                                bool flag5 = styleSelectorPart.type == (StyleSelectorType)4;
                                if (flag5) {
                                    this.ValidatePsuedoClassName(styleSelectorPart.value, selector);
                                }
                            }
                        }
                        result = true;
                    }
                }
            }
            return result;
        }

        void ValidateProperty(Property property) {
            bool flag = !base.disableValidation;
            if (flag) {
                string name = property.Name;
                string value = property.Value;
                StyleValidationResult styleValidationResult = this.m_Validator.ValidateProperty(name, value);
                bool flag2 = !styleValidationResult.success;
                if (flag2) {
                    string text = string.Concat(new string[] {
                        styleValidationResult.message,
                        "\n    ",
                        name,
                        ": ",
                        value
                    });
                    bool flag3 = !string.IsNullOrEmpty(styleValidationResult.hint);
                    if (flag3) {
                        text = text + " -> " + styleValidationResult.hint;
                    }
                    this.m_Errors.AddValidationWarning(text, this.GetPropertyLine(property), -1);
                }
            }
        }
    }
}