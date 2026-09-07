using InventoryOrderAssistant.Agent;
using InventoryOrderAssistant.Database;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var dbOptions = config.GetSection("Database").Get<DatabaseOptions>()
    ?? throw new InvalidOperationException("Missing 'Database' configuration section.");
var model = config["Agent:Model"] ?? "gpt-4.1";
var maxIterations = int.TryParse(config["Agent:MaxIterations"], out var m) ? m : 10;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OPENAI_API_KEY environment variable is not set.");
    return 1;
}

var database = new SqlServerService(dbOptions);

try
{
    await database.EnsureConnectableAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var dbTools = new SqlReadOnlyTools(database);
var functions = new[]
{
    AIFunctionFactory.Create(dbTools.ListTablesAsync, "list_tables"),
    AIFunctionFactory.Create(dbTools.DescribeTableAsync, "describe_table"),
    AIFunctionFactory.Create(dbTools.ExecuteQueryAsync, "execute_query")
};

IChatClient chatClient = new OpenAIClient(apiKey)
    .GetChatClient(model)
    .AsIChatClient();

var agent = new InventoryAgent(chatClient, functions, maxIterations);

Console.WriteLine("Inventory & Order Assistant  (type 'exit' to quit)");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var question = Console.ReadLine();

    if (string.Equals(question, "exit", StringComparison.OrdinalIgnoreCase))
        break;
    if (string.IsNullOrWhiteSpace(question))
        continue;

    try
    {
        Console.WriteLine();
        Console.WriteLine(await agent.AskAsync(question));
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
    }
}

return 0;
