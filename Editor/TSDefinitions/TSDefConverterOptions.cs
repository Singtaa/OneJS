using System;

namespace OneJS.Editor.TSDefinitions
{
    public class TSDefConverterOptions
    {
        public Type Type { get; set; }

        public bool   GenerateAllTypesInNamespace { get; set; }
        public Type[] TypesInNamespace            { get; set; }

        public bool JintSyntaxForEvents    { get; set; }
        public bool IncludeBaseMembers     { get; set; }
        public bool ExcludeUnityBaseTypes  { get; set; }
        public bool IncludeDeclare         { get; set; }
        public bool ExtractBaseDefinitions { get; set; }


    }
}