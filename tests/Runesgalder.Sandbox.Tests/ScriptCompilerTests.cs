using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.SandboxTests;

public sealed class ScriptCompilerTests
{
    private const string ValidScript = """
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class Analysis : IAnalysis
        {
            public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
            {
                context.Log(table.Name);
                return Task.FromResult(new AnalysisResult("rows", []));
            }
        }
        """;

    [Fact]
    public void CompilesAValidScript()
    {
        var result = new ScriptCompiler().Compile(ValidScript);

        Assert.True(result.Success);
        Assert.NotNull(result.Assembly);
        Assert.NotEmpty(result.Assembly);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(64, result.SourceHash.Length);
        Assert.NotNull(result.AssemblyHash);
        Assert.Equal(64, result.AssemblyHash.Length);
    }

    [Fact]
    public void ProducesTheSameAssemblyHashForTheSameSource()
    {
        var compiler = new ScriptCompiler();

        var first = compiler.Compile(ValidScript);
        var second = compiler.Compile(ValidScript);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.SourceHash, second.SourceHash);
        Assert.Equal(first.AssemblyHash, second.AssemblyHash);
    }

    [Fact]
    public void RejectsABannedNamespaceImport()
    {
        var source = """
            using System.IO;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult("rows", []));
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        Assert.Null(result.Assembly);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "RGS0001");
        Assert.Equal(ScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("System.IO", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAQualifiedUseOfABannedNamespace()
    {
        var source = """
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    var text = System.IO.File.ReadAllText("data.csv");
                    return Task.FromResult(new AnalysisResult(text, []));
                }
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == "RGS0001");
    }

    [Fact]
    public void RejectsAScriptWithoutTheContract()
    {
        var source = """
            public sealed class Analysis
            {
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "RGS0002");
        Assert.Equal(ScriptDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void RejectsMultipleContractImplementations()
    {
        var source = """
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class First : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult("first", []));
            }

            public sealed class Second : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult("second", []));
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == "RGS0003");
    }

    [Fact]
    public void RejectsANonPublicImplementation()
    {
        var source = """
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            internal sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult("rows", []));
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == "RGS0004");
    }

    [Theory]
    [InlineData("System.IO")]
    [InlineData("System.Net")]
    [InlineData("System.Reflection")]
    [InlineData("System.Diagnostics")]
    [InlineData("System.Runtime.InteropServices")]
    [InlineData("System.Runtime.Loader")]
    [InlineData("System.Management")]
    [InlineData("Microsoft.Win32")]
    public void RejectsEveryBannedNamespace(string bannedNamespace)
    {
        var source = $$"""
            using {{bannedNamespace}};
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult("rows", []));
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        Assert.Null(result.Assembly);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "RGS0001");
        Assert.Equal(ScriptDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains($"'{bannedNamespace}'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsABannedNamespaceImportedUnderAnAlias()
    {
        var source = """
            using FS = System.IO;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    var text = FS.File.ReadAllText("data.csv");
                    return Task.FromResult(new AnalysisResult(text, []));
                }
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == "RGS0001" && d.Line == 1);
    }

    [Fact]
    public void RejectsABannedTypeImportedStatically()
    {
        var source = """
            using static System.IO.File;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    var text = ReadAllText("data.csv");
                    return Task.FromResult(new AnalysisResult(text, []));
                }
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == "RGS0001" && d.Line == 1);
    }

    [Fact]
    public void RejectsABannedNamespaceQualifiedWithTheGlobalAlias()
    {
        var source = """
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    var text = global::System.IO.File.ReadAllText("data.csv");
                    return Task.FromResult(new AnalysisResult(text, []));
                }
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == "RGS0001" && d.Line == 8);
    }

    [Fact]
    public void ReportsSyntaxErrorsWithAPosition()
    {
        var result = new ScriptCompiler().Compile("public sealed class Analysis {");

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
        Assert.Equal(ScriptDiagnosticSeverity.Error, error.Severity);
        Assert.True(error.Line >= 1);
    }

    [Fact]
    public void RejectsSourceOverTheSizeLimit()
    {
        var options = ScriptCompilerOptions.Default with { MaxSourceBytes = 16 };

        var result = new ScriptCompiler(options).Compile(ValidScript);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == "RGS0000");
    }
}
