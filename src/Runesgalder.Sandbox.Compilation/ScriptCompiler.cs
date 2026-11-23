using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace Runesgalder.Sandbox.Compilation;

/// <summary>
/// Compiles generated analysis scripts against a whitelisted reference set and validates the
/// <see cref="IAnalysis"/> contract. Compilation only produces bytes; it never loads the result.
/// </summary>
public sealed class ScriptCompiler
{
    private const string AnalysisTypeName = "Runesgalder.Sandbox.IAnalysis";
    private const string AssemblyName = "Runesgalder.Script";

    private readonly ScriptCompilerOptions _options;
    private readonly Lazy<ImmutableArray<MetadataReference>> _references;

    /// <summary>Creates a compiler.</summary>
    /// <param name="options">The options, or null for the defaults.</param>
    public ScriptCompiler(ScriptCompilerOptions? options = null)
    {
        _options = options ?? ScriptCompilerOptions.Default;
        _references = new Lazy<ImmutableArray<MetadataReference>>(CreateReferences);
    }

    /// <summary>Compiles and validates a script.</summary>
    /// <param name="source">The script source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The compilation result.</returns>
    public ScriptCompilationResult Compile(string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var sourceHash = Hash(sourceBytes);
        if (sourceBytes.Length > _options.MaxSourceBytes)
        {
            var sizeDiagnostic = new ScriptDiagnostic(
                "RGS0000",
                ScriptDiagnosticSeverity.Error,
                $"The script is larger than the {_options.MaxSourceBytes} byte limit.",
                0,
                0);
            return new ScriptCompilationResult(false, [sizeDiagnostic], sourceHash, null, null);
        }

        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None),
            path: "script.cs",
            encoding: Encoding.UTF8,
            cancellationToken: cancellationToken);

        var diagnostics = new List<ScriptDiagnostic>();
        diagnostics.AddRange(FindBannedNamespaceUses(tree));

        var compilation = CSharpCompilation.Create(
            AssemblyName,
            [tree],
            _references.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false,
                deterministic: true,
                nullableContextOptions: NullableContextOptions.Enable,
                concurrentBuild: false));

        diagnostics.AddRange(ValidateContract(compilation, tree, cancellationToken));

        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
        {
            var mapped = MapDiagnostic(diagnostic);
            if (mapped is not null)
            {
                diagnostics.Add(mapped);
            }
        }

        var ordered = Order(diagnostics);
        if (ordered.Any(d => d.Severity == ScriptDiagnosticSeverity.Error))
        {
            return new ScriptCompilationResult(false, ordered, sourceHash, null, null);
        }

        using var assembly = new MemoryStream();
        var emitResult = compilation.Emit(
            assembly,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded),
            cancellationToken: cancellationToken);

        if (!emitResult.Success)
        {
            foreach (var diagnostic in emitResult.Diagnostics)
            {
                var mapped = MapDiagnostic(diagnostic);
                if (mapped is not null)
                {
                    diagnostics.Add(mapped);
                }
            }

            return new ScriptCompilationResult(false, Order(diagnostics), sourceHash, null, null);
        }

        var assemblyBytes = assembly.ToArray();
        return new ScriptCompilationResult(
            true,
            ordered,
            sourceHash,
            Hash(assemblyBytes),
            assemblyBytes);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static ScriptDiagnostic[] Order(List<ScriptDiagnostic> diagnostics) =>
        diagnostics
            .OrderBy(d => d.Line)
            .ThenBy(d => d.Column)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();

    private static ScriptDiagnostic? MapDiagnostic(Diagnostic diagnostic)
    {
        if (diagnostic.IsSuppressed || diagnostic.Severity < DiagnosticSeverity.Warning)
        {
            return null;
        }

        var line = 0;
        var column = 0;
        if (diagnostic.Location.IsInSource)
        {
            var position = diagnostic.Location.GetLineSpan().StartLinePosition;
            line = position.Line + 1;
            column = position.Character + 1;
        }

        var severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => ScriptDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => ScriptDiagnosticSeverity.Warning,
            _ => ScriptDiagnosticSeverity.Info
        };

        return new ScriptDiagnostic(
            diagnostic.Id,
            severity,
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            line,
            column);
    }

    private List<ScriptDiagnostic> FindBannedNamespaceUses(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var found = new List<ScriptDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in root.DescendantNodes())
        {
            var name = node switch
            {
                UsingDirectiveSyntax directive => directive.Name?.ToString(),
                MemberAccessExpressionSyntax memberAccess => memberAccess.ToString(),
                QualifiedNameSyntax qualified => qualified.ToString(),
                _ => null
            };

            if (name is null)
            {
                continue;
            }

            if (name.StartsWith("global::", StringComparison.Ordinal))
            {
                name = name["global::".Length..];
            }

            var banned = _options.BannedNamespaces.FirstOrDefault(candidate =>
                name.Equals(candidate, StringComparison.Ordinal) ||
                name.StartsWith(candidate + ".", StringComparison.Ordinal));

            if (banned is null)
            {
                continue;
            }

            var position = node.GetLocation().GetLineSpan().StartLinePosition;
            var key = $"{banned}:{position.Line}";
            if (!seen.Add(key))
            {
                continue;
            }

            found.Add(new ScriptDiagnostic(
                "RGS0001",
                ScriptDiagnosticSeverity.Error,
                $"The namespace '{banned}' is not available to generated analysis code.",
                position.Line + 1,
                position.Character + 1));
        }

        return found;
    }

    private static List<ScriptDiagnostic> ValidateContract(
        CSharpCompilation compilation,
        SyntaxTree tree,
        CancellationToken cancellationToken)
    {
        var analysisSymbol = compilation.GetTypeByMetadataName(AnalysisTypeName)
            ?? throw new InvalidOperationException($"The {AnalysisTypeName} contract is not referenced.");

        var root = (CompilationUnitSyntax)tree.GetRoot(cancellationToken);
        var model = compilation.GetSemanticModel(tree);
        var implementers = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Select(declaration => model.GetDeclaredSymbol(declaration, cancellationToken))
            .OfType<INamedTypeSymbol>()
            .Where(symbol => symbol.AllInterfaces.Any(
                implemented => SymbolEqualityComparer.Default.Equals(implemented, analysisSymbol)))
            .ToArray();

        if (implementers.Length == 0)
        {
            return
            [
                new ScriptDiagnostic(
                    "RGS0002",
                    ScriptDiagnosticSeverity.Error,
                    $"The script must declare a public class that implements {AnalysisTypeName}.",
                    0,
                    0)
            ];
        }

        if (implementers.Length > 1)
        {
            return
            [
                new ScriptDiagnostic(
                    "RGS0003",
                    ScriptDiagnosticSeverity.Error,
                    $"The script must declare exactly one {AnalysisTypeName} implementation; found {implementers.Length}.",
                    0,
                    0)
            ];
        }

        var implementer = implementers[0];
        var hasPublicDefaultConstructor = implementer.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 &&
            constructor.DeclaredAccessibility == Accessibility.Public);

        if (implementer.DeclaredAccessibility != Accessibility.Public ||
            implementer.IsAbstract ||
            !hasPublicDefaultConstructor)
        {
            var position = implementer.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition;
            return
            [
                new ScriptDiagnostic(
                    "RGS0004",
                    ScriptDiagnosticSeverity.Error,
                    $"The class '{implementer.Name}' must be public, non-abstract, and have a public parameterless constructor.",
                    position is null ? 0 : position.Value.Line + 1,
                    position is null ? 0 : position.Value.Character + 1)
            ];
        }

        return [];
    }

    private ImmutableArray<MetadataReference> CreateReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        var wanted = new HashSet<string>(_options.ReferenceAssemblyNames, StringComparer.OrdinalIgnoreCase);
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (wanted.Contains(name))
            {
                builder.Add(MetadataReference.CreateFromFile(path));
                wanted.Remove(name);
            }
        }

        var abstractionsPath = typeof(IAnalysis).Assembly.Location;
        if (string.IsNullOrEmpty(abstractionsPath))
        {
            throw new InvalidOperationException("The sandbox abstractions assembly path is unavailable.");
        }

        builder.Add(MetadataReference.CreateFromFile(abstractionsPath));
        return builder.ToImmutable();
    }
}
