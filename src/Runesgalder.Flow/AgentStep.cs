using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace Runesgalder;

/// <summary>A step that sends a single turn to an <see cref="IChatClient"/> and returns the text response.</summary>
/// <typeparam name="TIn">The step input type.</typeparam>
public sealed class AgentStep<TIn> : Step<TIn, string>
{
    private readonly IChatClient _chatClient;
    private readonly Func<TIn, string> _prompt;
    private readonly string? _systemPrompt;
    private readonly string? _modelName;
    private readonly ChatOptions? _options;

    /// <summary>Creates an agent step.</summary>
    /// <param name="name">The step name.</param>
    /// <param name="chatClient">The chat client; model binding is left to the provider adapter.</param>
    /// <param name="prompt">Builds the user message from the step input.</param>
    /// <param name="systemPrompt">An optional system message sent with every call.</param>
    /// <param name="modelName">An optional logical model name recorded in events, for clients that do not report one.</param>
    /// <param name="options">Optional chat options.</param>
    public AgentStep(
        string name,
        IChatClient chatClient,
        Func<TIn, string> prompt,
        string? systemPrompt = null,
        string? modelName = null,
        ChatOptions? options = null)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(prompt);
        _chatClient = chatClient;
        _prompt = prompt;
        _systemPrompt = systemPrompt;
        _modelName = modelName;
        _options = options;
    }

    /// <inheritdoc />
    public override async ValueTask<string> ExecuteAsync(
        TIn input,
        FlowContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = new List<ChatMessage>(2);
        if (_systemPrompt is not null)
        {
            messages.Add(new ChatMessage(ChatRole.System, _systemPrompt));
        }

        messages.Add(new ChatMessage(ChatRole.User, _prompt(input)));

        var started = Stopwatch.GetTimestamp();
        await context.Journal.AppendAsync(
            new ModelCallStarted(context.RunId, Id, Name, _modelName, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        var response = await _chatClient.GetResponseAsync(messages, _options, cancellationToken)
            .ConfigureAwait(false);
        var usage = response.Usage;
        var modelId = response.ModelId ?? _modelName;

        var activity = Activity.Current;
        if (activity is not null)
        {
            if (modelId is not null)
            {
                activity.SetTag(FlowDiagnostics.ModelNameTag, modelId);
            }

            if (usage?.InputTokenCount is { } inputTokens)
            {
                activity.SetTag(FlowDiagnostics.TokenInputTag, inputTokens);
            }

            if (usage?.OutputTokenCount is { } outputTokens)
            {
                activity.SetTag(FlowDiagnostics.TokenOutputTag, outputTokens);
            }
        }

        await context.Journal.AppendAsync(
            new ModelCallCompleted(
                context.RunId,
                Id,
                Name,
                modelId,
                usage?.InputTokenCount,
                usage?.OutputTokenCount,
                Stopwatch.GetElapsedTime(started),
                DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        return response.Text ?? string.Empty;
    }
}

/// <summary>Creates agent steps backed by an <see cref="IChatClient"/>.</summary>
public static class Agent
{
    /// <summary>Creates a text-in, text-out agent step.</summary>
    /// <typeparam name="TIn">The step input type.</typeparam>
    /// <param name="name">The step name.</param>
    /// <param name="chatClient">The chat client.</param>
    /// <param name="prompt">Builds the user message from the step input.</param>
    /// <param name="systemPrompt">An optional system message.</param>
    /// <param name="modelName">An optional logical model name for events.</param>
    /// <param name="options">Optional chat options.</param>
    /// <returns>A step that returns the model's text response.</returns>
    public static Step<TIn, string> Text<TIn>(
        string name,
        IChatClient chatClient,
        Func<TIn, string> prompt,
        string? systemPrompt = null,
        string? modelName = null,
        ChatOptions? options = null) =>
        new AgentStep<TIn>(name, chatClient, prompt, systemPrompt, modelName, options);
}
