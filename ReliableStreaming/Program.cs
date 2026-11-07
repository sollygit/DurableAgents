using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI.DurableTask;
using Microsoft.Agents.AI.Hosting.AzureFunctions;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;
using ReliableStreaming;
using StackExchange.Redis;

// Get the Foundry project endpoint and model deployment name from environment variables.
string projectEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("FOUNDRY_MODEL")
    ?? throw new InvalidOperationException("FOUNDRY_MODEL is not set.");

// The Azure OpenAI endpoint is the authority (scheme + host) of the Foundry project endpoint.
string endpoint = new Uri(projectEndpoint).GetLeftPart(UriPartial.Authority);

// Get Redis connection string from environment variable.
string redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
    ?? "localhost:6379";

// Get the Redis stream TTL from environment variable (default: 10 minutes).
int redisStreamTtlMinutes = int.TryParse(
    Environment.GetEnvironmentVariable("REDIS_STREAM_TTL_MINUTES"),
    out int ttlMinutes) ? ttlMinutes : 10;

var credential = new ChainedTokenCredential(new AzureCliCredential());

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AzureOpenAIClient client = new(new Uri(endpoint), credential);

// Travel Planner agent instructions - designed to produce longer responses for demonstrating streaming.
const string TravelPlannerName = "TravelPlanner";
const string TravelPlannerInstructions =
    """
    You are an expert travel planner who creates detailed, personalized travel itineraries.
    When asked to plan a trip, you should:
    1. Create a comprehensive day-by-day itinerary
    2. Include specific recommendations for activities, restaurants, and attractions
    3. Provide practical tips for each destination
    4. Consider weather and local events when making recommendations
    5. Include estimated times and logistics between activities
    
    Always use the available tools to get current weather forecasts and local events
    for the destination to make your recommendations more relevant and timely.
    
    Format your response with clear headings for each day and include emoji icons
    to make the itinerary easy to scan and visually appealing.
    """;

// Configure the function app to host the AI agent.
FunctionsApplicationBuilder builder = FunctionsApplication
    .CreateBuilder(args)
    .ConfigureFunctionsWebApplication()
    .ConfigureDurableAgents(options =>
    {
        // Define the Travel Planner agent with tools for weather and events
        options.AddAIAgentFactory(TravelPlannerName, sp =>
        {
            return client.GetChatClient(deploymentName).AsAIAgent(
                instructions: TravelPlannerInstructions,
                name: TravelPlannerName,
                services: sp,
                tools: [
                    AIFunctionFactory.Create(TravelTools.GetWeatherForecast),
                    AIFunctionFactory.Create(TravelTools.GetLocalEvents),
                ]);
        });
    });

// Register Redis connection as a singleton
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnectionString));

// Register the Redis stream response handler - this captures agent responses
// and publishes them to Redis Streams for reliable delivery.
// Registered as both the concrete type (for FunctionTriggers) and the interface (for the agent framework).
builder.Services.AddSingleton(sp =>
    new RedisStreamResponseHandler(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        TimeSpan.FromMinutes(redisStreamTtlMinutes)));
builder.Services.AddSingleton<IAgentResponseHandler>(sp =>
    sp.GetRequiredService<RedisStreamResponseHandler>());

using IHost app = builder.Build();

app.Run();
