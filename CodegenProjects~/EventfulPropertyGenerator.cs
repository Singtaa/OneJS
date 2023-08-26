using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace OneJS.Codegen;

[Generator]
public class EventfulPropertyGenerator : ISourceGenerator {
  const string MARKER_ATTRIBUTE_FULLY_QUALIFIED_NAME = "global::OneJS.EventfulPropertyAttribute";

  public void Initialize(GeneratorInitializationContext context) {
    context.RegisterForSyntaxNotifications(() => new PartialClassFinder());
  }

  public void Execute(GeneratorExecutionContext context) {
    if (context.SyntaxReceiver is not PartialClassFinder finder) return;

    foreach (var classDeclaration in finder.PartialClassDeclarations) {
      var semanticModel = context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
      var fieldDeclarations = GetMarkedFieldDeclarations(semanticModel, classDeclaration).ToList();

      if (fieldDeclarations.Count > 0) {
        AddEventfulSource(context, classDeclaration, fieldDeclarations);
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

  static void AddEventfulSource(GeneratorExecutionContext context, ClassDeclarationSyntax classDeclaration, IEnumerable<FieldDeclarationSyntax> fieldDeclarations) {
    var compilationUnit = GenerateEventfulCompilationUnit(classDeclaration, fieldDeclarations);
    context.AddSource($"{classDeclaration.Identifier.ValueText}.EventfulProperty.cs", compilationUnit.GetText(Encoding.UTF8));
  }

  static CompilationUnitSyntax GenerateEventfulCompilationUnit(ClassDeclarationSyntax classDeclaration, IEnumerable<FieldDeclarationSyntax> fieldDeclarations) =>
    CompilationUnit()
      .WithUsings(classDeclaration.SyntaxTree.GetCompilationUnitRoot().Usings)
      .WithMembers(
        SingletonList<MemberDeclarationSyntax>(
          GenerateEventfulClass(classDeclaration, fieldDeclarations)
        )
      )
      .NormalizeWhitespace();

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
          IdentifierName("System"),
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
    fieldName = fieldName.TrimStart('_');
    return char.ToUpper(fieldName[0]) + fieldName[1..];
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
