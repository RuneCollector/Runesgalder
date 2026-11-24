using System.Text.Json;

namespace Runesgalder;

/// <summary>Encodes run events as a stable type tag plus a JSON payload.</summary>
internal static class RunEventCodec
{
    public static (string Type, string Payload) Encode(RunEvent runEvent) => runEvent switch
    {
        RunStarted value => ("run.started", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        StepStarted value => ("step.started", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        StepCompleted value => ("step.completed", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        StepFailed value => ("step.failed", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        StepRetryScheduled value => ("step.retry_scheduled", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        ModelCallStarted value => ("model.call_started", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        ModelCallCompleted value => ("model.call_completed", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        CheckpointStored value => ("checkpoint.stored", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        CheckpointRestored value => ("checkpoint.restored", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        BudgetWarning value => ("budget.warning", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        BudgetExceeded value => ("budget.exceeded", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        ApprovalRequested value => ("approval.requested", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        ApprovalResolved value => ("approval.resolved", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        RunCompleted value => ("run.completed", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        RunFailed value => ("run.failed", JsonSerializer.Serialize(value, CheckpointJson.Options)),
        _ => throw new InvalidOperationException($"The event type {runEvent.GetType()} has no journal encoding.")
    };

    public static RunEvent Decode(string type, string payload) => type switch
    {
        "run.started" => Deserialize<RunStarted>(payload),
        "step.started" => Deserialize<StepStarted>(payload),
        "step.completed" => Deserialize<StepCompleted>(payload),
        "step.failed" => Deserialize<StepFailed>(payload),
        "step.retry_scheduled" => Deserialize<StepRetryScheduled>(payload),
        "model.call_started" => Deserialize<ModelCallStarted>(payload),
        "model.call_completed" => Deserialize<ModelCallCompleted>(payload),
        "checkpoint.stored" => Deserialize<CheckpointStored>(payload),
        "checkpoint.restored" => Deserialize<CheckpointRestored>(payload),
        "budget.warning" => Deserialize<BudgetWarning>(payload),
        "budget.exceeded" => Deserialize<BudgetExceeded>(payload),
        "approval.requested" => Deserialize<ApprovalRequested>(payload),
        "approval.resolved" => Deserialize<ApprovalResolved>(payload),
        "run.completed" => Deserialize<RunCompleted>(payload),
        "run.failed" => Deserialize<RunFailed>(payload),
        _ => throw new InvalidOperationException($"The event tag '{type}' is not recognised by this schema version.")
    };

    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, CheckpointJson.Options)
        ?? throw new InvalidOperationException($"The '{typeof(T).Name}' payload was null.");
}
