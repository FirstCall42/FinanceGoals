using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FinanceGoals.Architecture.Tests;

/// <summary>
/// Classes and structs use explicit constructors that assign _fields, never primary
/// constructors (CLAUDE.md). No analyzer rule flags them, so this test does.
/// Records are exempt: their positional parameters define properties, not captured state.
/// </summary>
[TestClass]
public sealed class PrimaryConstructorTests
{
    [TestMethod]
    public void No_class_or_struct_in_src_declares_a_primary_constructor()
    {
        var offenders = Directory
            .EnumerateFiles(RepositoryRoot.SourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => FindPrimaryConstructors(File.ReadAllText(path))
                .Select(type => $"{Path.GetRelativePath(RepositoryRoot.Directory.FullName, path)}: {type}"))
            .ToList();

        offenders.ShouldBeEmpty("Use an explicit constructor instead of a primary constructor");
    }

    [TestMethod]
    public void Finds_primary_constructors_on_classes_and_structs()
    {
        const string source = """
            public sealed class Account(string name) { }
            public readonly struct Rate(decimal value) { }
            public partial class Outer { private sealed class Inner(int x) { } }
            """;

        FindPrimaryConstructors(source).ShouldBe(["Account", "Rate", "Inner"]);
    }

    [TestMethod]
    public void Ignores_explicit_constructors_and_records()
    {
        const string source = """
            public sealed class Account
            {
                private readonly string _name;
                public Account(string name) { _name = name; }
            }
            public sealed record Snapshot(DateOnly Date, decimal Value);
            public readonly record struct Period(int Months);
            """;

        FindPrimaryConstructors(source).ShouldBeEmpty();
    }

    private static List<string> FindPrimaryConstructors(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(type => type is ClassDeclarationSyntax or StructDeclarationSyntax && type.ParameterList is not null)
            .Select(type => type.Identifier.Text)
            .ToList();

    private static bool IsBuildOutput(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var parts = path.Split(separators);
        return parts.Contains("bin") || parts.Contains("obj");
    }
}
