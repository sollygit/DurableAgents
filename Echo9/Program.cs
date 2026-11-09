using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;

// Load environment variables from a .env file if present (for local development).
Env.NoClobber().TraversePath().Load();

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var appInsightsConnectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

if (string.IsNullOrEmpty(appInsightsConnectionString))
{
    Console.Error.WriteLine(
        "[WARNING] APPLICATIONINSIGHTS_CONNECTION_STRING not set — traces will not be sent " +
        "to Application Insights. Set it to enable local telemetry. " +
        "(This variable is auto-injected in hosted Foundry containers — do not declare it in agent.manifest.yaml.)");
}

var projectEndpoint = new Uri(Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT environment variable is not set."));
var deployment = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? throw new InvalidOperationException("AZURE_AI_MODEL_DEPLOYMENT_NAME environment variable is not set.");
var env = Environment.GetEnvironmentVariable("AZURE_ENV_NAME");

TokenCredential credential = env switch
{
    "develepment" => new AzureCliCredential(),
    _ => new DefaultAzureCredential()
};

AIAgent agent = new AIProjectClient(projectEndpoint, credential)
    .AsAIAgent(
        model: deployment,
        name: "echo9-agent",
        instructions: "You are a helpful AI assistant. Be concise and informative.",
        description: "A minimal echo agent using the Agent Framework");

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();
