namespace Runesgalder;

/// <summary>Stable error codes shared by Flow and Sandbox.</summary>
public static class ErrorCodes
{
    /// <summary>A flow step failed.</summary>
    public const string FlowStepFailed = "flow.step_failed";

    /// <summary>A flow run was cancelled.</summary>
    public const string FlowCancelled = "flow.cancelled";

    /// <summary>The sandbox rejected the submitted source before compilation.</summary>
    public const string SandboxSourceRejected = "sandbox.source_rejected";

    /// <summary>Generated code failed to compile.</summary>
    public const string SandboxCompileFailed = "sandbox.compile_failed";

    /// <summary>Generated code failed at runtime.</summary>
    public const string SandboxRuntimeFailed = "sandbox.runtime_failed";

    /// <summary>The sandbox denied an effect under the active policy.</summary>
    public const string SandboxPolicyDenied = "sandbox.policy_denied";

    /// <summary>The sandbox exceeded a resource limit.</summary>
    public const string SandboxResourceExceeded = "sandbox.resource_exceeded";

    /// <summary>The sandbox operation timed out.</summary>
    public const string SandboxTimedOut = "sandbox.timed_out";
}
