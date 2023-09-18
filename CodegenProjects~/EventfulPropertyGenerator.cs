using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace OneJS.Codegen;

[Generator]
public class EventfulPropertyGenerator : ISourceGenerator {
  const string MARKER_ATTRIBUTE_FULLY_QUALIFIED_NAME = "global::OneJS.EventfulPropertyAttribute";
  static readonly string OUTPUT_DIRECTORY = Path.Combine("Temp", "GeneratedCode", "OneJS");

  public void Initialize(GeneratorInitializationContext context) {
    context.RegisterForSyntaxNotifications(() => new PartialClassFinder());
  }

  public void Execute(GeneratorExecutionContext context) {
    if (context.SyntaxReceiver is not PartialClassFinder finder) return;

    foreach (var classDeclaration in finder.PartialClassDeclarations) {
      var semanticModel = context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
      var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
      var fieldDeclarations = GetMarkedFieldDeclarations(semanticModel, classDeclaration).ToList();

      if (fieldDeclarations.Count == 0) continue;

      var projectPath = GetProjectPath(context);
      var outputFileName = $"{classSymbol}.g.cs";
      var outputFilePath = Path.Combine(projectPath ?? "", OUTPUT_DIRECTORY, outputFileName);
      var compilationUnit = GenerateEventfulCompilationUnit(classSymbol, classDeclaration, fieldDeclarations)
        .WithLeadingTrivia(
          Trivia(
            LineDirectiveTrivia(
              Literal(2),
              Literal(outputFilePath.Replace('\\', '/')),
              true
            )
          )
        )
        .NormalizeWhitespace();
      var sourceText = compilationUnit.GetText(Encoding.UTF8);
      context.AddSource(outputFileName, sourceText);

      if (projectPath != null) {
        WriteOutputToFile(outputFilePath, sourceText);
      }
    }
  }

  static IEnumerable<FieldDeclarationSyntax> GetMarkedFieldDeclarations(SemanticModel semanticModel, ClassDeclarationSyntax classDeclaration) =>
    classDeclaration.Members
      .OfType<FieldDeclarationSyntax>()
      .Where(f =>
        f.AttributeLists.SelectMany(al => al.Attributes).Any(a =>
          semanticModel.GetTypeInfo(a).Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == MARKER_ATTRIBUTE_FULLY_QUALIFIED_NAME
        )
      );

  static CompilationUnitSyntax GenerateEventfulCompilationUnit(INamedTypeSymbol classSymbol, ClassDeclarationSyntax classDeclaration, IEnumerable<FieldDeclarationSyntax> fieldDeclarations) {
    var namespaceSyntax = GenerateEventfulNamespace(classSymbol.ContainingNamespace);
    var compilationUnit = CompilationUnit().WithUsings(classDeclaration.SyntaxTree.GetCompilationUnitRoot().Usings);

    if (namespaceSyntax != null) {
      compilationUnit = compilationUnit.WithMembers(
        SingletonList<MemberDeclarationSyntax>(
          NamespaceDeclaration(namespaceSyntax)
            .WithMembers(
              SingletonList<MemberDeclarationSyntax>(
                GenerateEventfulClass(classDeclaration, fieldDeclarations)
              )
            )
        )
      );
    } else {
      compilationUnit = compilationUnit.WithMembers(
        SingletonList<MemberDeclarationSyntax>(
          GenerateEventfulClass(classDeclaration, fieldDeclarations)
        )
      );
    }

    return compilationUnit;
  }

  static NameSyntax GenerateEventfulNamespace(INamespaceSymbol namespaceSymbol) {
    if (namespaceSymbol.IsGlobalNamespace) return null;

    var namespaceSyntax = GenerateEventfulNamespace(namespaceSymbol.ContainingNamespace);

    if (namespaceSyntax != null) {
      return QualifiedName(namespaceSyntax, IdentifierName(namespaceSymbol.Name));
    } else {
      return IdentifierName(namespaceSymbol.Name);
    }
  }

  static ClassDeclarationSyntax GenerateEventfulClass(ClassDeclarationSyntax classDeclaration, IEnumerable<FieldDeclarationSyntax> fieldDeclarations) =>
    ClassDeclaration(classDeclaration.Identifier)
      .WithModifiers(classDeclaration.Modifiers)
      .WithMembers(
        List(
          fieldDeclarations.SelectMany(fieldDeclaration =>
            fieldDeclaration.Declaration.Variables.SelectMany(variableDeclarator => {
              var fieldName = variableDeclarator.Identifier.ValueText;
              var propertyName = ConvertToPropertyName(fieldName);
              var eventName = $"On{propertyName}Changed";

              return new MemberDeclarationSyntax[] {
                GenerateEventfulProperty(fieldDeclaration.Declaration.Type, fieldName, propertyName, eventName),
                GenerateEventfulEvent(fieldDeclaration.Declaration.Type, eventName)
              };
            })
          )
        )
      );

  static PropertyDeclarationSyntax GenerateEventfulProperty(TypeSyntax typeSyntax, string fieldName, string propertyName, string eventName) {
    var fieldNameSyntax = IdentifierName(fieldName);
    var valueSyntax = IdentifierName("value");

    return PropertyDeclaration(typeSyntax, propertyName)
      .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
      .WithAccessorList(
        AccessorList(
          List(new[] {
            AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
              .WithExpressionBody(ArrowExpressionClause(fieldNameSyntax))
              .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
            AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
              .WithBody(
                Block(
                  ExpressionStatement(
                    AssignmentExpression(
                      SyntaxKind.SimpleAssignmentExpression,
                      fieldNameSyntax,
                      valueSyntax
                    )
                  ),
                  ExpressionStatement(
                    ConditionalAccessExpression(
                      IdentifierName(eventName),
                      InvocationExpression(
                        MemberBindingExpression(
                          IdentifierName("Invoke")
                        ),
                        ArgumentList(
                          SingletonSeparatedList(
                            Argument(valueSyntax)
                          )
                        )
                      )
                    )
                  )
                )
              )
          })
        )
      );
  }

  static EventFieldDeclarationSyntax GenerateEventfulEvent(TypeSyntax typeSyntax, string eventName) =>
    EventFieldDeclaration(
      VariableDeclaration(
        QualifiedName(
          AliasQualifiedName(
            IdentifierName(Token(SyntaxKind.GlobalKeyword)),
            IdentifierName("System")
          ),
          GenericName(
            Identifier("Action"),
            TypeArgumentList(
              SingletonSeparatedList(typeSyntax)
            )
          )
        ),
        SingletonSeparatedList(
          VariableDeclarator(eventName)
        )
      )
    ).WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));

  static string ConvertToPropertyName(string fieldName) {
    var sb = new StringBuilder();

    for (var i = 0; i < fieldName.Length; i++) {
      if (fieldName[i] == '_') continue;

      sb.Append(i == 0 || fieldName[i-1] == '_' ? char.ToUpper(fieldName[i]) : fieldName[i]);
    }

    return sb.ToString();
  }

  static void WriteOutputToFile(string outputFilePath, SourceText sourceText) {
    Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));
    File.WriteAllText(outputFilePath, sourceText.ToString());
  }

  static string GetProjectPath(GeneratorExecutionContext context) {
    var isLanguageServer = context.AdditionalFiles.Length == 0;

    if (isLanguageServer) return null;

    // In Unity 2021.1 or newer, the content of the first additional file is the project path
    var additionalFile = context.AdditionalFiles[0];
    var projectPath = additionalFile.GetText(context.CancellationToken)?.ToString();

    // Fallback to file path
    if (string.IsNullOrEmpty(projectPath)) {
      projectPath = Path.GetDirectoryName(additionalFile.Path);
    }

    return projectPath;
  }

  class PartialClassFinder : ISyntaxReceiver {
    public readonly List<ClassDeclarationSyntax> PartialClassDeclarations = new();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode) {
      if (
        syntaxNode is ClassDeclarationSyntax classDeclaration &&
        classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))
      ) {
        PartialClassDeclarations.Add(classDeclaration);
      }
    }
  }
}
