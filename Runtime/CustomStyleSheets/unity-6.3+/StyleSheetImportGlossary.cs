// Got rid of UnityEditor.L10n dependency for runtime usage
namespace OneJS.CustomStyleSheets {
    public class StyleSheetImportGlossary {
        static string Tr(string text) {
            // Plug your own localization here later if needed
            return text;
        }

        public readonly string internalError = Tr("Internal import error: {0}");

        public readonly string internalErrorWithStackTrace = Tr("Internal import error: {0}\n{1}");

        public readonly string error = Tr("error");

        public readonly string warning = Tr("warning");

        public readonly string line = Tr("line");

        public readonly string column = Tr("column");

        public readonly string unsupportedUnit = Tr("Unsupported unit: '{0}'");

        public readonly string ussParsingError = Tr("USS parsing error: {0}");

        public readonly string unsupportedTerm = Tr("Unsupported USS term: `{0}` ({1})");

        public readonly string missingFunctionArgument = Tr("Missing function argument: '{0}'");

        public readonly string missingVariableName = Tr("Missing variable name");

        public readonly string emptyVariableName = Tr("Empty variable name");

        public readonly string tooManyFunctionArguments = Tr("Too many function arguments");

        public readonly string emptyFunctionArgument = Tr("Empty function argument");

        public readonly string unexpectedTokenInFunction = Tr("Expected ',', got '{0}'");

        public readonly string missingVariablePrefix = Tr("Variable '{0}' is missing '--' prefix");

        public readonly string invalidHighResAssetType = Tr("Unsupported type {0} for asset at path '{1}' ; only Texture2D is supported for variants with @2x suffix\nSuggestion: verify the import settings of this asset.");

        public readonly string invalidSelectorListDelimiter = Tr("Invalid selector list delimiter: '{0}'");

        public readonly string invalidComplexSelectorDelimiter = Tr("Invalid complex selector delimiter: '{0}'");

        public readonly string unsupportedSelectorFormat = Tr("Unsupported selector format: '{0}'");

        public readonly string selectorStartsWithDigitFormat = Tr("Unsupported selector format. Selector names can not start with a digit. '{0}'");

        public readonly string unknownFunction = Tr("Unknown function '{0}' in declaration '{1}: {0}'");

        public readonly string circularImport = Tr("Circular @import dependencies detected. All @import directives will be ignored for this StyleSheet.");

        public readonly string invalidUriLocation = Tr("Invalid URI location: '{0}'");

        public readonly string invalidUriScheme = Tr("Invalid URI scheme: '{0}'");

        public readonly string invalidAssetPath = Tr("Invalid asset path: '{0}'");

        public readonly string invalidAssetType = Tr("Unsupported type {0} for asset at path '{1}' ; only the following types are supported: {2}\nSuggestion: verify the import settings of this asset.");

        public readonly string unknownPsuedoClass = Tr("Unknown psuedo class '{0}' in selector '{1}`");
    }
}