using Microsoft.Extensions.AI;
using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.Demo.Tests;

public sealed class DemoScenarioTests
{
    [Fact]
    public void TheFirstScriptIsRefusedAndTheRepairCompiles()
    {
        var compiler = new ScriptCompiler();

        var first = compiler.Compile(DemoScripts.BuggyScript);

        Assert.False(first.Success);
        Assert.Null(first.Assembly);
        var error = Assert.Single(
            first.Diagnostics,
            diagnostic => diagnostic.Severity == ScriptDiagnosticSeverity.Error);
        Assert.Equal("RGS0001", error.Id);
        Assert.Contains("System.IO", error.Message, StringComparison.Ordinal);

        var repaired = compiler.Compile(DemoScripts.FixedScript);

        Assert.True(repaired.Success);
        Assert.NotNull(repaired.Assembly);
        Assert.DoesNotContain(
            repaired.Diagnostics,
            diagnostic => diagnostic.Severity == ScriptDiagnosticSeverity.Error);
    }

    [Fact]
    public void TheRefusalScriptIsRejectedBeforeExecution()
    {
        var result = new ScriptCompiler().Compile(DemoScripts.RefusalScript);

        Assert.False(result.Success);
        Assert.Null(result.Assembly);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "RGS0001" && diagnostic.Message.Contains("System.Net", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheFakeModelAnswersFromThePrompt()
    {
        using var client = new FakeChatClient();

        Assert.Equal(
            DemoScripts.Plan,
            await ReplyAsync(client, $"{DemoScripts.PlanMarker} for sales.csv."));
        Assert.Equal(
            DemoScripts.BuggyScript,
            await ReplyAsync(client, $"{DemoScripts.ScriptMarker} for sales.csv."));
        Assert.Equal(
            DemoScripts.FixedScript,
            await ReplyAsync(client, $"{DemoScripts.RepairMarker}: RGS0001."));
        Assert.Equal(
            DemoScripts.RefusalScript,
            await ReplyAsync(client, $"Write a script that will {DemoScripts.RefusalMarker} the table."));

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, DemoScripts.PlanMarker)]);
        Assert.Equal(FakeChatClient.ModelName, response.ModelId);
        Assert.True(response.Usage?.InputTokenCount > 0);
        Assert.True(response.Usage?.OutputTokenCount > 0);
    }

    [Fact]
    public async Task TheCompileStepRepairsOnceAndJournalsTheRetry()
    {
        var session = DemoSession.Create(DemoSession.Analysis);
        session.Script = DemoScripts.BuggyScript;
        var journal = new InMemoryJournal();
        var context = new FlowContext(RunId.New(), journal);
        using var chat = new FakeChatClient();
        var step = new CompileStep(new ScriptCompiler(), chat, new DemoOptions());

        var result = await step.RunAsync(session, context, CancellationToken.None);

        Assert.NotNull(result.Assembly);
        Assert.Equal(2, result.CompileAttempts.Count);
        Assert.False(result.CompileAttempts[0].Success);
        Assert.True(result.CompileAttempts[1].Success);
        Assert.Equal(DemoScripts.FixedScript, result.Script);

        var events = journal.Snapshot();
        var retry = Assert.Single(events.OfType<StepRetryScheduled>());
        Assert.Equal(2, retry.Attempt);
        Assert.Equal(ErrorCodes.SandboxCompileFailed, retry.ErrorCode);
        Assert.Contains("RGS0001", retry.Message, StringComparison.Ordinal);

        var repairCall = Assert.Single(events.OfType<ModelCallCompleted>());
        Assert.Equal("compile", repairCall.StepName);
        Assert.Equal(FakeChatClient.ModelName, repairCall.ModelId);
        Assert.Single(events.OfType<StepCompleted>());
    }

    [Fact]
    public async Task TheCompileStepRefusesWithoutRepair()
    {
        var session = DemoSession.Create(DemoSession.Refusal);
        session.Script = DemoScripts.RefusalScript;
        var journal = new InMemoryJournal();
        var context = new FlowContext(RunId.New(), journal);
        using var chat = new FakeChatClient();
        var step = new CompileStep(new ScriptCompiler(), chat, new DemoOptions());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await step.RunAsync(session, context, CancellationToken.None));

        Assert.Contains("Policy refused", failure.Message, StringComparison.Ordinal);
        Assert.Contains("System.Net", failure.Message, StringComparison.Ordinal);
        Assert.Single(session.CompileAttempts);
        Assert.Null(session.Assembly);
        Assert.Empty(journal.Snapshot().OfType<ModelCallStarted>());
        Assert.Contains(journal.Snapshot(), runEvent => runEvent is StepFailed);
    }

    private static async Task<string> ReplyAsync(FakeChatClient client, string prompt)
    {
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)]);
        return response.Text ?? string.Empty;
    }
}
