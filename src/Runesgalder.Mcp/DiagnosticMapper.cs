using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.Mcp;

internal static class DiagnosticMapper
{
    private const int MaxMessageLength = 4096;

    public static DiagnosticDto Map(ScriptDiagnostic diagnostic) => new(
        diagnostic.Id,
        diagnostic.Severity switch
        {
            ScriptDiagnosticSeverity.Error => "error",
            ScriptDiagnosticSeverity.Warning => "warning",
            _ => "info"
        },
        Truncate(diagnostic.Message),
        diagnostic.Line,
        diagnostic.Column);

    public static IReadOnlyList<DiagnosticDto> Map(IEnumerable<ScriptDiagnostic> diagnostics) =>
        diagnostics.Select(Map).ToArray();

    public static ErrorDto MapCompileFailure(ScriptCompilationResult compilation)
    {
        var error = compilation.Diagnostics.FirstOrDefault(d => d.Severity == ScriptDiagnosticSeverity.Error);
        var code = error?.Id switch
        {
            "RGS0000" => ErrorCodes.SourceTooLarge,
            "RGS0001" => ErrorCodes.PolicyDenied,
            "RGS0002" or "RGS0003" or "RGS0004" => ErrorCodes.ContractInvalid,
            _ => ErrorCodes.CompileFailed
        };

        var message = error is null
            ? "The script did not compile."
            : Truncate(error.Message);

        return new ErrorDto(code, message);
    }

    private static string Truncate(string message) =>
        message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
}
