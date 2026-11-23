namespace Runesgalder.Sandbox.Compilation;

/// <summary>The severity of a script diagnostic.</summary>
public enum ScriptDiagnosticSeverity
{
    /// <summary>An informational message.</summary>
    Info,

    /// <summary>A warning.</summary>
    Warning,

    /// <summary>An error that prevents execution.</summary>
    Error
}

/// <summary>A structured diagnostic produced while compiling a script.</summary>
/// <param name="Id">The diagnostic identifier.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Message">The human-readable message.</param>
/// <param name="Line">The one-based line number, or zero when unknown.</param>
/// <param name="Column">The one-based column number, or zero when unknown.</param>
public sealed record ScriptDiagnostic(
    string Id,
    ScriptDiagnosticSeverity Severity,
    string Message,
    int Line,
    int Column);

/// <summary>The outcome of compiling a generated script.</summary>
/// <param name="Success">Whether the script passed every check and emitted an assembly.</param>
/// <param name="Diagnostics">The diagnostics, ordered by position.</param>
/// <param name="SourceHash">The lowercase hexadecimal SHA-256 hash of the script source.</param>
/// <param name="AssemblyHash">The lowercase hexadecimal SHA-256 hash of the emitted assembly, or null when emission failed.</param>
/// <param name="Assembly">The emitted assembly bytes, or null when emission failed.</param>
public sealed record ScriptCompilationResult(
    bool Success,
    IReadOnlyList<ScriptDiagnostic> Diagnostics,
    string SourceHash,
    string? AssemblyHash,
    byte[]? Assembly);
