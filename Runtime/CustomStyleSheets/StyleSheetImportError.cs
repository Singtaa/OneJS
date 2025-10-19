namespace OneJS.CustomStyleSheets {
    public struct StyleSheetImportError
    {
        public readonly StyleSheetImportErrorType error;

        public readonly StyleSheetImportErrorCode code;

        public readonly string assetPath;

        public readonly string message;

        public readonly int line;

        public readonly int column;

        public readonly bool isWarning;

        public StyleSheetImportError(StyleSheetImportErrorType error, StyleSheetImportErrorCode code, string assetPath, string message, int line = -1, int column = -1, bool isWarning = false)
        {
            this.error = error;
            this.code = code;
            this.assetPath = assetPath;
            this.message = message;
            this.line = line;
            this.column = column;
            this.isWarning = isWarning;
        }

        public override string ToString()
        {
            return ToString(StyleValueImporter.glossary);
        }

        public string ToString(StyleSheetImportGlossary glossary)
        {
            string text = (isWarning ? glossary.warning : glossary.error);
            if (line > -1)
            {
                if (column > -1)
                {
                    return $"{assetPath} ({glossary.line} {line}, {glossary.column} {column}): {text}: {message}";
                }
                return $"{assetPath} ({glossary.line} {line}): {text}: {message}";
            }
            return assetPath + ": " + text + ": " + message;
        }
    }
}