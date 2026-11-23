namespace Runesgalder.Mcp;

internal static class ErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string SourceTooLarge = "source_too_large";
    public const string DataTooLarge = "data_too_large";
    public const string ContractInvalid = "contract_invalid";
    public const string PolicyDenied = "policy_denied";
    public const string CompileFailed = "compile_failed";
    public const string SandboxUnavailable = "sandbox_unavailable";
    public const string RunTimeout = "run_timeout";
    public const string Cancelled = "cancelled";
    public const string ServerBusy = "server_busy";
    public const string RunNotFound = "run_not_found";
    public const string Internal = "internal";
}

internal sealed class ToolInputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
