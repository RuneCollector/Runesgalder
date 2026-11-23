using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.AI;
using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.Demo;

/// <summary>
/// Compiles the generated script, asks the model for a repair when compilation fails, and
/// refuses the script outright when the scenario does not allow repair.
/// </summary>
internal sealed class CompileStep(ScriptCompiler compiler, IChatClient chat, DemoOptions options)
    : Step<DemoSession, DemoSession>("compile")
{
    /// <inheritdoc />
    public override async ValueTask<DemoSession> ExecuteAsync(
        DemoSession session,
        FlowContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var source = session.Script ?? throw new InvalidOperationException("No script was generated.");

        for (var attempt = 1; ; attempt++)
        {
            var result = compiler.Compile(source, cancellationToken);
            var errors = result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == ScriptDiagnosticSeverity.Error)
                .Select(FormatDiagnostic)
                .ToArray();
            session.CompileAttempts.Add(new CompileAttempt(attempt, result.Success, errors));

            if (result.Success)
            {
                session.Script = source;
                session.Assembly = result.Assembly
                    ?? throw new InvalidOperationException("The compiler reported success without an assembly.");
                return session;
            }

            var summary = string.Join("; ", errors);
            if (!session.AllowRepair)
            {
                throw new InvalidOperationException($"Policy refused the script: {summary}");
            }

            if (attempt >= options.MaxCompileAttempts)
            {
                throw new InvalidOperationException($"The script still failed to compile: {summary}");
            }

            await context.Journal.AppendAsync(
                new StepRetryScheduled(
                    context.RunId,
                    Id,
                    attempt + 1,
                    ErrorCodes.SandboxCompileFailed,
                    summary,
                    DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);

            source = await RepairAsync(session, context, errors, source, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> RepairAsync(
        DemoSession session,
        FlowContext context,
        IReadOnlyList<string> errors,
        string source,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        await context.Journal.AppendAsync(
            new ModelCallStarted(context.RunId, Id, Name, FakeChatClient.ModelName, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        var messages = new List<ChatMessage>(2)
        {
            new(ChatRole.System, DemoScripts.AnalystSystem),
            new(ChatRole.User, DemoSession.RepairPrompt(errors, source))
        };

        var response = await chat.GetResponseAsync(messages, null, cancellationToken).ConfigureAwait(false);

        await context.Journal.AppendAsync(
            new ModelCallCompleted(
                context.RunId,
                Id,
                Name,
                response.ModelId ?? FakeChatClient.ModelName,
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount,
                Stopwatch.GetElapsedTime(started),
                DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        var repaired = response.Text;
        if (string.IsNullOrWhiteSpace(repaired))
        {
            throw new InvalidOperationException($"The model returned no repair for {session.Scenario}.");
        }

        return repaired;
    }

    private static string FormatDiagnostic(ScriptDiagnostic diagnostic) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{diagnostic.Id} at {diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}");
}
