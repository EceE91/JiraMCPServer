namespace JiraMCPServer;

/// <summary>
/// Settings bound from the "Jira" configuration section.
/// Non-secret defaults live in appsettings.json; Email and ApiToken come from .env (Jira__Email, Jira__ApiToken).
/// </summary>
public sealed class JiraOptions
{
    public const string SectionName = "Jira";

    public string BaseUrl { get; set; } = "";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string DefaultProjectKey { get; set; } = "";
    public string DefaultIssueType { get; set; } = "Task";
    public int MaxResults { get; set; } = 50;
    public int TimeoutSeconds { get; set; } = 30;
}
