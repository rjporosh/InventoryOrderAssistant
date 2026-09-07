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

    public InventoryAgent(IChatClient chatClient, IEnumerable<AIFunction> functions, int maxIterations = 10)
    {
        _chatClient = chatClient;
        _functions = functions.ToDictionary(f => f.Name);
        _maxIterations = maxIterations;
    }

    public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, question)
        };

        var options = new ChatOptions { Tools = [.. _functions.Values] };

        for (var iteration = 0; iteration < _maxIterations; iteration++)
        {
            var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
            messages.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (calls.Count == 0)
                return response.Text is { Length: > 0 } text ? text : "I couldn't determine an answer.";

            var results = new List<AIContent>();
            foreach (var call in calls)
                results.Add(await InvokeAsync(call, cancellationToken));

            messages.Add(new ChatMessage(ChatRole.Tool, results));
        }

        throw new InvalidOperationException(
            $"Agent stopped after {_maxIterations} iterations without reaching an answer.");
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
