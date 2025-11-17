using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OneJS.CustomStyleSheets {
    public enum StyleSheetImportErrorType {
        Syntax,
        Semantic,
        Validation,
        Internal,
    }

    public enum StyleSheetImportErrorCode {
        None,
        Internal,
        UnsupportedUnit,
        UnsupportedTerm,
        InvalidSelectorListDelimiter,
        InvalidComplexSelectorDelimiter,
        UnsupportedSelectorFormat,
        RecursiveSelectorDetected,
        MissingFunctionArgument,
        InvalidProperty,
        InvalidURILocation,
        InvalidURIScheme,
        InvalidURIProjectAssetPath,
        InvalidVarFunction,
        InvalidHighResolutionImage,
    }

    public class StyleSheetImportErrors : IEnumerable<StyleSheetImportError>, IEnumerable {
        private List<StyleSheetImportError> m_Errors = new List<StyleSheetImportError>();

        public string assetPath { get; set; }

        public StyleSheetImporter.ErrorHandling unsupportedSelectorAction { get; set; }

        public StyleSheetImporter.ErrorHandling unsupportedTermAction { get; set; }

        public bool hasErrors => m_Errors.Any((StyleSheetImportError e) => !e.isWarning);

        public bool hasWarning => m_Errors.Any((StyleSheetImportError e) => e.isWarning);

        private StyleSheetImporter.ErrorHandling GetHandling(StyleSheetImportErrorCode errorType, StyleSheetImporter.ErrorHandling defaultHandling) {
            switch (errorType) {
                case StyleSheetImportErrorCode.UnsupportedTerm:
                    return (StyleSheetImporter.ErrorHandling)Mathf.Max((int)unsupportedTermAction, (int)defaultHandling);
                default:
                    if (errorType != StyleSheetImportErrorCode.RecursiveSelectorDetected) {
                        return defaultHandling;
                    }
                    goto case StyleSheetImportErrorCode.UnsupportedSelectorFormat;
                case StyleSheetImportErrorCode.UnsupportedSelectorFormat:
                    return (StyleSheetImporter.ErrorHandling)Mathf.Max((int)unsupportedSelectorAction, (int)defaultHandling);
            }
        }

        public void AddSyntaxError(string message, int line, int column = -1) {
            m_Errors.Add(new StyleSheetImportError(StyleSheetImportErrorType.Syntax, StyleSheetImportErrorCode.None, assetPath, message, line, column));
        }

        public void AddSemanticError(StyleSheetImportErrorCode code, string message, int line, int column = -1) {
            StyleSheetImporter.ErrorHandling handling = GetHandling(code, StyleSheetImporter.ErrorHandling.Error);
            if (handling != StyleSheetImporter.ErrorHandling.Ignore) {
                m_Errors.Add(new StyleSheetImportError(StyleSheetImportErrorType.Semantic, code, assetPath, message, line, column, handling == StyleSheetImporter.ErrorHandling.Warning));
            }
        }

        public void AddSemanticWarning(StyleSheetImportErrorCode code, string message, int line) {
            StyleSheetImporter.ErrorHandling handling = GetHandling(code, StyleSheetImporter.ErrorHandling.Warning);
            if (handling != StyleSheetImporter.ErrorHandling.Ignore) {
                m_Errors.Add(new StyleSheetImportError(StyleSheetImportErrorType.Semantic, code, assetPath, message, line, -1, isWarning: true));
            }
        }

        public void AddInternalError(string message, int line = -1) {
            m_Errors.Add(new StyleSheetImportError(StyleSheetImportErrorType.Internal, StyleSheetImportErrorCode.None, assetPath, message, line));
        }

        public void AddValidationWarning(string message, int line, int column = -1) {
            m_Errors.Add(new StyleSheetImportError(StyleSheetImportErrorType.Validation, StyleSheetImportErrorCode.InvalidProperty, assetPath, message, line, column, isWarning: true));
        }

        public IEnumerator<StyleSheetImportError> GetEnumerator() {
            return m_Errors.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return m_Errors.GetEnumerator();
        }
    }
}