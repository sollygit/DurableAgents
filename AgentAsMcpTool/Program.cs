using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DurableTask;
using Microsoft.Agents.AI.Hosting.AzureFunctions;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

// Get the Foundry project endpoint and model deployment name from environment variables.
string projectEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("FOUNDRY_MODEL")
    ?? throw new InvalidOperationException("FOUNDRY_MODEL is not set.");

// The Azure OpenAI endpoint is the authority (scheme + host) of the Foundry project endpoint.
string endpoint = new Uri(projectEndpoint).GetLeftPart(UriPartial.Authority);
var credential = new ChainedTokenCredential(new AzureCliCredential());

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AzureOpenAIClient client = new(new Uri(endpoint), credential);

// Define three AI agents we are going to use in this application.
AIAgent joker = client.GetChatClient(deploymentName)
    .AsAIAgent("You are good at telling jokes.", "Joker");

AIAgent stockAdvisor = client.GetChatClient(deploymentName)
    .AsAIAgent("Check stock prices.", "StockAdvisor");

AIAgent plantAdvisor = client.GetChatClient(deploymentName)
    .AsAIAgent("Recommend plants.", "PlantAdvisor", description: "Get plant recommendations.");

using IHost app = FunctionsApplication
    .CreateBuilder(args)
    .ConfigureFunctionsWebApplication()
    .ConfigureDurableAgents(options =>
    {
        options
        .AddAIAgent(joker)  // Enables HTTP trigger by default.
        .AddAIAgent(stockAdvisor, enableHttpTrigger: false, enableMcpToolTrigger: true) // Disable HTTP trigger, enable MCP Tool trigger.
        .AddAIAgent(plantAdvisor, agentOptions =>
        {
            agentOptions.McpToolTrigger.IsEnabled = true; // Enable MCP Tool trigger.
        });
    })
    .Build();
app.Run();
