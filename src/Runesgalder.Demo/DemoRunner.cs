using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.Demo;

/// <summary>Builds and starts one demo flow per scenario.</summary>
internal sealed class DemoRunner : IDisposable
{
    private readonly FakeChatClient _chat = new();
    private readonly ScriptCompiler _compiler = new();
    private readonly DemoOptions _options;

    /// <summary>Creates a runner.</summary>
    /// <param name="options">The runtime options.</param>
    public DemoRunner(DemoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>The options used by this runner.</summary>
    public DemoOptions Options => _options;

    /// <summary>Starts a run and returns its live event stream.</summary>
    /// <param name="scenario">The scenario name.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The run handle and the shared session.</returns>
    public DemoRun Start(string scenario, CancellationToken cancellationToken = default)
    {
        var session = DemoSession.Create(scenario);
        var flow = Flow
            .Start(Agent.Text<DemoSession>(
                "plan",
                _chat,
                planSession => planSession.Request,
                DemoScripts.AnalystSystem,
                FakeChatClient.ModelName))
            .Then(Step.Define<string, DemoSession>("plan.capture", text =>
            {
                session.Plan = text;
                return session;
            }))
            .Then(Agent.Text<DemoSession>(
                "write-script",
                _chat,
                scriptSession => scriptSession.ScriptPrompt(),
                DemoScripts.AnalystSystem,
                FakeChatClient.ModelName))
            .Then(Step.Define<string, DemoSession>("write-script.capture", text =>
            {
                session.Script = text;
                return session;
            }))
            .Then(new CompileStep(_compiler, _chat, _options))
            .Then(new ExecuteStep(_options));

        return new DemoRun(flow.BeginRun(session, new InMemoryJournal(), cancellationToken), session);
    }

    /// <summary>Releases the chat client.</summary>
    public void Dispose() => _chat.Dispose();
}

/// <summary>A started demo run.</summary>
/// <param name="Run">The flow run handle.</param>
/// <param name="Session">The session the steps share.</param>
internal sealed record DemoRun(FlowRun<DemoSession> Run, DemoSession Session);
