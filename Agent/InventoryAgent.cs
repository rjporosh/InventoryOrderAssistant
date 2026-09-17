using Microsoft.Extensions.AI;

namespace InventoryOrderAssistant.Agent;

/// <summary>
/// A single agent running a tool-calling loop: discover tables, inspect columns,
/// write one read-only query, repeat until it can answer in plain language.
/// </summary>
public sealed class InventoryAgent
{
    private const string SystemPrompt =
        """
        You are an inventory and order database assistant for shop staff.

        You do NOT know the schema. Work it out with the tools:
        1. list_tables when you don't know what exists.
        2. describe_table on the tables that look relevant.
        3. Write ONE read-only SELECT and run it with execute_query.
        Repeat if a result reveals you need another table or column.

        Rules:
        - Never guess table or column names — inspect first.
        - Read-only SELECT/WITH only. One statement. No writes, no DDL.
        - Never invent data; every fact must come from a query result.
        - When you have enough, answer briefly in plain language with the numbers.
        """;

    private readonly IChatClient _chatClient;
    private readonly Dictionary<string, AIFunction> _functions;
    private readonly int _maxIterations;
    private readonly int _maxHistoryMessages;
    private readonly List<ChatMessage> _conversation;

    public InventoryAgent(IChatClient chatClient, IEnumerable<AIFunction> functions, int maxIterations = 10, int maxHistoryMessages = 40)
    {
        _chatClient = chatClient;
        _functions = functions.ToDictionary(f => f.Name);
        _maxIterations = maxIterations;
        _maxHistoryMessages = maxHistoryMessages;
        _conversation = [new ChatMessage(ChatRole.System, SystemPrompt)];
    }

    /// <summary>
    /// Forgets prior turns and starts a fresh conversation, keeping only the system prompt.
    /// </summary>
    public void ResetConversation()
    {
        _conversation.Clear();
        _conversation.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        _conversation.Add(new ChatMessage(ChatRole.User, question));

        var options = new ChatOptions { Tools = [.. _functions.Values] };

        for (var iteration = 0; iteration < _maxIterations; iteration++)
        {
            Console.WriteLine($"[Iteration {iteration + 1}] ");

            var response = await _chatClient.GetResponseAsync(_conversation, options, cancellationToken);
            _conversation.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (calls.Count == 0)
            {
                TrimHistory();
                return response.Text is { Length: > 0 } text ? text : "I couldn't determine an answer. Pl contact Developer MD IKRAMUL ISLAM SIDDIQUE POROSH Phone : +8801672896992";
            }

            var results = new List<AIContent>();
            foreach (var call in calls)
                results.Add(await InvokeAsync(call, cancellationToken));

            _conversation.Add(new ChatMessage(ChatRole.Tool, results));
        }

        TrimHistory();
        throw new InvalidOperationException(
            $"Agent stopped after {_maxIterations} iterations without reaching an answer.");
    }

    /// <summary>
    /// Keeps the conversation bounded by dropping the oldest turns (after the system prompt)
    /// once the history grows past <see cref="_maxHistoryMessages"/>, so long sessions don't
    /// grow the prompt unboundedly while still remembering recent context.
    /// </summary>
    private void TrimHistory()
    {
        if (_conversation.Count <= _maxHistoryMessages)
            return;

        var excess = _conversation.Count - _maxHistoryMessages;
        _conversation.RemoveRange(1, excess);
    }

    private async Task<AIContent> InvokeAsync(FunctionCallContent call, CancellationToken cancellationToken)
    {
        try
        {
            if (!_functions.TryGetValue(call.Name, out var function))
                return new FunctionResultContent(call.CallId, $"Unknown tool '{call.Name}'.");

            var result = await function.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken);
            return new FunctionResultContent(call.CallId, result ?? "null");
        }
        catch (Exception ex)
        {
            // Hand the error back to the model so it can correct the query instead of crashing.
            return new FunctionResultContent(call.CallId, $"Tool error: {ex.Message}");
        }
    }
}
