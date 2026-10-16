using JiraMCPServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Load .env into environment variables before the host reads them. Search upwards from the
// executable's folder (bin/... -> solution root) so it is found regardless of the working directory.
// Variables already set in the environment (e.g. by the MCP client) take precedence.
var envFile = FindFileUpwards(AppContext.BaseDirectory, ".env") ?? FindFileUpwards(Directory.GetCurrentDirectory(), ".env");
if (envFile is not null)
{
    DotNetEnv.Env.NoClobber().Load(envFile);
}

// Use the build output folder as content root so appsettings.json is found
// even when the MCP client starts the server from another working directory.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// stdout is reserved for the MCP protocol over stdio, so send all logs to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var jiraSection = builder.Configuration.GetSection(JiraOptions.SectionName);
var jiraOptions = jiraSection.Get<JiraOptions>() ?? new JiraOptions();

var missing = new List<string>();
if (string.IsNullOrWhiteSpace(jiraOptions.BaseUrl)) missing.Add("Jira__BaseUrl");
if (string.IsNullOrWhiteSpace(jiraOptions.Email)) missing.Add("Jira__Email");
if (string.IsNullOrWhiteSpace(jiraOptions.ApiToken)) missing.Add("Jira__ApiToken");
if (string.IsNullOrWhiteSpace(jiraOptions.DefaultProjectKey)) missing.Add("Jira__DefaultProjectKey");
if (missing.Count > 0)
{
    Console.Error.WriteLine($"Missing configuration: {string.Join(", ", missing)}. Copy .env.example to .env and fill in the values.");
    return 1;
}

builder.Services.Configure<JiraOptions>(jiraSection);

builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp => new JiraApiService(
    sp.GetRequiredService<IHttpClientFactory>(),
    jiraOptions.BaseUrl,
    jiraOptions.Email,
    jiraOptions.ApiToken,
    jiraOptions.DefaultProjectKey)
);

// Picks up JiraTools' [McpServerTool] methods and serves them over stdio, the channel
// MCP clients use to talk to this process.
// Note: WithStdioServerTransport() means this server talks to its client over standard input/output rather than over
// a network port (no HTTP, no ModelContextProtocol.AspNetCore involved here).
// That's the standard setup for a local MCP server: the MCP client (e.g. Claude Desktop, Rider, etc.)
// launches JiraMCPServer.exe as a child process on your machine and exchanges JSON-RPC messages over its stdin/stdout.
// It's why Program.cs is careful to route logging to stderr — stdout is reserved entirely for the protocol.
// If you wanted it reachable remotely instead (e.g. other machines/users connecting over HTTP),
// you'd need the ModelContextProtocol.AspNetCore package and something like WithHttpTransport()
// in an ASP.NET Core app instead — but as configured now, it's local-only, one client per process instance.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();
return 0;

static string? FindFileUpwards(string startDirectory, string fileName)
{
    for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, fileName);
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}