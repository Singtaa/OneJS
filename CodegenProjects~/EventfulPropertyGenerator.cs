using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

[Generator]
public class EventfulPropertyGenerator : ISourceGenerator {
    public void Initialize(GeneratorInitializationContext context) {
        // No initialization needed
    }

    public void Execute(GeneratorExecutionContext context) {
        foreach (var syntaxTree in context.Compilation.SyntaxTrees) {
            var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);

            var classNodes = syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classNode in classNodes) {
                var isPartial = classNode.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
                if (!isPartial) continue;
                var classSymbol = semanticModel.GetDeclaredSymbol(classNode);
                if (classSymbol is null) continue;

                var propertiesCode = "";

                var fieldNodes = classNode.DescendantNodes().OfType<FieldDeclarationSyntax>();
                foreach (var fieldNode in fieldNodes) {
                    var fieldSymbol =
                        semanticModel.GetDeclaredSymbol(fieldNode.Declaration.Variables.First()) as IFieldSymbol;
                    var originalTypeSyntax = fieldNode.Declaration.Type.ToFullString().Trim();

                    if (fieldSymbol is null) continue;

                    var attributes = fieldSymbol.GetAttributes();
                    if (attributes.Any(attr => attr.AttributeClass.Name == "EventfulPropertyAttribute")) {
                        propertiesCode += GeneratePropertyWithEvent(fieldSymbol, originalTypeSyntax);
                    }
                }

                if (!string.IsNullOrEmpty(propertiesCode)) {
                    var generatedCode = $@"
using System;

namespace {classSymbol.ContainingNamespace} {{
    public partial class {classSymbol.Name} {{
        {propertiesCode.Trim()}
    }}
}}".TrimStart('\n', '\r');
                    context.AddSource($"{classSymbol.Name}.EventfulProperties.cs",
                        SourceText.From(generatedCode, Encoding.UTF8));
                }
            }
        }
    }

    private string GeneratePropertyWithEvent(IFieldSymbol fieldSymbol, string originalTypeSyntax = null) {
        var fieldType = string.IsNullOrEmpty(originalTypeSyntax) ? fieldSymbol.Type.Name : originalTypeSyntax;
        var fieldName = fieldSymbol.Name;
        var propName = TitleCase(fieldName);

        return $@"
        public {fieldType} {propName} {{
            get => {fieldName};
            set {{
                {fieldName} = value;
                On{propName}Changed?.Invoke(value);
            }}
        }}
        public event Action<{fieldType}> On{propName}Changed;".TrimStart('\n', '\r')
               + Environment.NewLine + Environment.NewLine;
    }

    public string TitleCase(string input) {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        input = input.Replace("-", "").Replace("_", "");
        input = char.ToUpper(input[0]) + input.Substring(1);
        return input;
    }
}