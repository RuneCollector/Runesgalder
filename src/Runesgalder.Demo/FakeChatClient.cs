using System.Globalization;
using Microsoft.Extensions.AI;

namespace Runesgalder.Demo;

/// <summary>
/// A deterministic stand-in for a hosted model. It picks its reply from the prompt content so
/// the demo shows a full plan, first-attempt script, repair, and refusal without a network call.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    /// <summary>The model identifier recorded in journal events.</summary>
    public const string ModelName = "fake-analyst";

    /// <summary>Returns a canned reply for the prompt.</summary>
    /// <param name="messages">The chat messages.</param>
    /// <param name="options">The chat options; ignored.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The reply.</returns>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var prompt = string.Join(
            "\n",
            messages.Where(message => message.Role == ChatRole.User).Select(message => message.Text));
        var reply = Reply(prompt);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            ModelId = ModelName,
            Usage = new UsageDetails
            {
                InputTokenCount = prompt.Length / 4,
                OutputTokenCount = reply.Length / 4
            }
        };

        return Task.FromResult(response);
    }

    /// <summary>Streaming is not used by the demo.</summary>
    /// <param name="messages">The chat messages.</param>
    /// <param name="options">The chat options; ignored.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Never returns a value.</returns>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The demo does not stream model output.");

    /// <summary>Returns no provider services.</summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The service key.</param>
    /// <returns>Always null.</returns>
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <summary>Releases no resources.</summary>
    public void Dispose()
    {
    }

    private static string Reply(string prompt)
    {
        if (prompt.Contains(DemoScripts.RepairMarker, StringComparison.Ordinal))
        {
            return DemoScripts.FixedScript;
        }

        if (prompt.Contains(DemoScripts.RefusalMarker, StringComparison.Ordinal))
        {
            return DemoScripts.RefusalScript;
        }

        if (prompt.Contains(DemoScripts.ScriptMarker, StringComparison.Ordinal))
        {
            return DemoScripts.BuggyScript;
        }

        if (prompt.Contains(DemoScripts.PlanMarker, StringComparison.Ordinal))
        {
            return DemoScripts.Plan;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"I cannot help with: {prompt[..Math.Min(prompt.Length, 64)]}");
    }
}
