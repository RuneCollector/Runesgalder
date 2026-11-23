using System.Text.Json.Serialization;

namespace Runesgalder.Mcp;

internal sealed record ErrorDto(string Code, string Message);

internal sealed record DiagnosticDto(string Id, string Severity, string Message, int Line, int Column);

internal sealed record ArtifactDto([property: JsonPropertyName("sha256")] string Sha256, int Bytes);

internal sealed record CompileResponseDto(
    bool Ok,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    ArtifactDto? Artifact,
    double DurationMs,
    ErrorDto? Error);

internal sealed record ChartDto(string Kind, string Title, IReadOnlyList<string> Labels, IReadOnlyList<double> Values);

internal sealed record ResultDto(string Summary, IReadOnlyList<ChartDto> Charts, IReadOnlyList<string> Logs);

internal sealed record ResourceDto(double CompileMs, double SandboxMs, double WallMs, long OutputBytes);

internal sealed record RunEnvelopeDto(
    string? RunId,
    string Status,
    ResultDto? Result,
    IReadOnlyList<DiagnosticDto>? Diagnostics,
    ResourceDto? Resource,
    ErrorDto? Error);

internal sealed record CancelResponseDto(string? RunId, string Status, ErrorDto? Error);
