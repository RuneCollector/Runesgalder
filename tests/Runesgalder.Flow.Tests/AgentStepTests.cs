using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace Runesgalder.FlowTests;

public sealed class AgentStepTests
{
    [Fact]
    public async Task AgentSendsMessagesAndJournalsModelCalls()
    {
        var client = new FakeChatClient("hello", inputTokens: 11, outputTokens: 5);
        var flow = Flow.Start(
            Agent.Text<string>(
                "planner",
                client,
                question => $"Answer: {question}",
                systemPrompt: "You plan.",
                modelName: "fake-model"));
        var journal = new InMemoryJournal();

        var result = await flow.RunAsync("why", journal);

        Assert.Equal("hello", result);
        var messages = Assert.Single(client.Calls);
        Assert.Collection(
            messages,
            message => Assert.Equal(ChatRole.System, message.Role),
            message => Assert.Equal("Answer: why", message.Text));

        var events = journal.Snapshot();
        var started = Assert.Single(events.OfType<ModelCallStarted>());
        Assert.Equal("fake-model", started.ModelId);
        Assert.Equal("planner", started.StepName);
        var completed = Assert.Single(events.OfType<ModelCallCompleted>());
        Assert.Equal(11, completed.InputTokens);
        Assert.Equal(5, completed.OutputTokens);
        Assert.True(completed.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task AgentWithoutUsageReportsNullTokenCounts()
    {
        var client = new FakeChatClient("ok");
        var flow = Flow.Start(Agent.Text<string>("agent", client, input => input));
        var journal = new InMemoryJournal();

        await flow.RunAsync("input", journal);

        var completed = Assert.Single(journal.Snapshot().OfType<ModelCallCompleted>());
        Assert.Null(completed.InputTokens);
        Assert.Null(completed.OutputTokens);
    }

    [Fact]
    public async Task SourceEmitsFlowAndStepSpans()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Runesgalder.Flow",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (spans)
                {
                    spans.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var client = new FakeChatClient("done");
        var flow = Flow.Start(Agent.Text<string>("writer", client, input => input, modelName: "fake-model")).Then(
            Step.Define<string, int>("count", text => text.Length));

        var run = flow.BeginRun("abc");
        await run.Result;
        var runId = run.RunId.ToString();

        lock (spans)
        {
            var owned = spans.Where(span => Equals(span.GetTagItem("run.id"), runId)).ToArray();
            var root = Assert.Single(owned, span => span.OperationName.StartsWith("flow ", StringComparison.Ordinal));
            Assert.Equal("flow writer -> count", root.OperationName);
            var writer = Assert.Single(owned, span => span.OperationName == "writer");
            Assert.Equal("writer", writer.GetTagItem("step.id"));
            Assert.Equal("fake-model", writer.GetTagItem("model.name"));
        }
    }

    private sealed class FakeChatClient(string reply, long? inputTokens = null, long? outputTokens = null)
        : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            Calls.Add(messages.ToArray());
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, reply));
            if (inputTokens is not null || outputTokens is not null)
            {
                response.Usage = new UsageDetails
                {
                    InputTokenCount = inputTokens,
                    OutputTokenCount = outputTokens
                };
            }

            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Streaming is not used by these tests.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
