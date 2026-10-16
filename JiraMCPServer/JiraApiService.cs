using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace JiraMCPServer;

/// <summary>A trimmed-down view of a Jira issue, returned by GetAllWorkItemsAsync.</summary>
public record WorkItemSummary(string Key, string? Summary, string? Status, string? Type, string? Assignee);

public class JiraApiService(
    IHttpClientFactory httpClientFactory,
    string baseUrl,
    string email,
    string apiToken,
    string projectKey)
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly string _email = email;
    private readonly string _apiToken = apiToken;
    private readonly string _projectKey = projectKey;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    /// <summary>
    /// Fetches the statuses available to each issue type in the configured project via
    /// GET /rest/api/3/project/{projectKey}/statuses, returning the raw JSON response.
    /// </summary>
    public async Task<string> GetProjectStatusesAsync()
    {
        var httpClient = CreateAuthenticatedClient();

        var url = $"{_baseUrl}/rest/api/3/project/{_projectKey}/statuses";

        var response = await httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        EnsureSuccessWithBody(response, content);

        return content;
    }

    /// <summary>
    /// Fetches the issue types available in the configured project via
    /// GET /rest/api/3/issue/createmeta/{projectKey}/issuetypes, returning the raw JSON response.
    /// </summary>
    public async Task<string> GetAllWorkItemTypesAsync()
    {
        var httpClient = CreateAuthenticatedClient();

        var url = $"{_baseUrl}/rest/api/3/issue/createmeta/{_projectKey}/issuetypes";

        var response = await httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        EnsureSuccessWithBody(response, content);

        return content;
    }

    /// <summary>
    /// Fetches all work items in the configured project via GET /rest/api/3/search/jql, following
    /// nextPageToken until the last page, and trims each issue down to its key, summary, status,
    /// type and assignee. The enhanced search endpoint only returns issue ids unless fields are
    /// requested explicitly.
    /// </summary>
    public async Task<List<WorkItemSummary>> GetAllWorkItemsAsync()
    {
        var httpClient = CreateAuthenticatedClient();

        var jql = Uri.EscapeDataString($"project = {_projectKey} ORDER BY key ASC");
        var baseSearchUrl = $"{_baseUrl}/rest/api/3/search/jql?jql={jql}" +
                            "&fields=summary,status,issuetype,assignee&maxResults=100";

        var workItems = new List<WorkItemSummary>();
        string? nextPageToken = null;

        do
        {
            var url = nextPageToken is null
                ? baseSearchUrl
                : $"{baseSearchUrl}&nextPageToken={Uri.EscapeDataString(nextPageToken)}";

            var response = await httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            EnsureSuccessWithBody(response, content);

            using var page = JsonDocument.Parse(content);
            var root = page.RootElement;

            foreach (var issue in root.GetProperty("issues").EnumerateArray())
            {
                var fields = issue.GetProperty("fields");
                workItems.Add(new WorkItemSummary(
                    Key: issue.GetProperty("key").GetString()!,
                    Summary: GetString(fields, "summary"),
                    Status: GetNestedString(fields, "status", "name"),
                    Type: GetNestedString(fields, "issuetype", "name"),
                    Assignee: GetNestedString(fields, "assignee", "displayName")));
            }

            var isLast = !root.TryGetProperty("isLast", out var isLastElement) || isLastElement.GetBoolean();
            nextPageToken = !isLast && root.TryGetProperty("nextPageToken", out var tokenElement)
                ? tokenElement.GetString()
                : null;
        } while (nextPageToken is not null);

        return workItems;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string property, string nestedProperty) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? GetString(value, nestedProperty)
            : null;

    /// <summary>
    /// Creates a new issue in the configured project via POST /rest/api/3/issue, returning the
    /// raw JSON response (including the new issue's key and id).
    /// </summary>
    /// <param name="summary">The issue's title. Required by Jira.</param>
    /// <param name="description">
    /// Plain text for the issue body, wrapped in a minimal Atlassian Document Format document
    /// before sending. Omitted from the request entirely when null/empty/whitespace, since ADF
    /// rejects a text node with no content.
    /// </param>
    /// <param name="issueTypeId">
    /// The numeric issue type id (e.g. "10001"), not its name. Fetch valid ids via
    /// GetAllWorkItemTypesAsync. To use a name instead, change the "issuetype" field below to
    /// new { name = issueTypeName }.
    /// </param>
    /// <param name="statusId">
    /// Status id to move the issue to after creation (Jira does not support setting status at
    /// creation time - it always starts in the workflow's default status, so this requires a
    /// separate transition call). Fetch valid ids via GetProjectStatusesAsync. When null, the
    /// issue is transitioned to whichever available status is named "Backlog", if any.
    /// </param>
    public async Task<string> CreateWorkItemAsync(string summary, string? description, string issueTypeId, string? statusId = null)
    {
        var httpClient = CreateAuthenticatedClient();

        var url = $"{_baseUrl}/rest/api/3/issue";

        var fields = new Dictionary<string, object?>
        {
            ["project"] = new { key = _projectKey },
            ["summary"] = summary,
            ["issuetype"] = new { id = issueTypeId }
        };

        // ADF rejects a text node with an empty/whitespace string, so omit description entirely
        // rather than sending one.
        if (!string.IsNullOrWhiteSpace(description))
        {
            fields["description"] = new
            {
                type = "doc",
                version = 1,
                content = new object[]
                {
                    new
                    {
                        type = "paragraph",
                        content = new object[]
                        {
                            new { type = "text", text = description }
                        }
                    }
                }
            };
        }

        var jsonContent = JsonSerializer.Serialize(new { fields });
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        EnsureSuccessWithBody(response, responseContent);

        using var createdIssue = JsonDocument.Parse(responseContent);
        var issueKey = createdIssue.RootElement.GetProperty("key").GetString()!;

        var statusTransitionWarning = await TryTransitionToStatusAsync(httpClient, issueKey, statusId);

        if (statusTransitionWarning is null)
        {
            return responseContent;
        }

        var createdFields = createdIssue.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        createdFields["statusTransitionWarning"] = statusTransitionWarning;
        return JsonSerializer.Serialize(createdFields);
    }

    /// <summary>
    /// Attempts to move a newly created issue into the requested status via
    /// GET/POST /rest/api/3/issue/{issueKey}/transitions, since Jira has no way to set status
    /// directly on create. Returns null on success, or a human-readable warning if no matching
    /// transition was available or the transition call failed - the issue itself still exists.
    /// </summary>
    private async Task<string?> TryTransitionToStatusAsync(HttpClient httpClient, string issueKey, string? statusId)
    {
        var transitionsUrl = $"{_baseUrl}/rest/api/3/issue/{issueKey}/transitions";

        var transitionsResponse = await httpClient.GetAsync(transitionsUrl);
        var transitionsContent = await transitionsResponse.Content.ReadAsStringAsync();
        if (!transitionsResponse.IsSuccessStatusCode)
        {
            return $"Issue {issueKey} was created, but fetching available status transitions failed with " +
                   $"{(int)transitionsResponse.StatusCode} {transitionsResponse.StatusCode}: {transitionsContent}";
        }

        using var transitionsDoc = JsonDocument.Parse(transitionsContent);
        string? transitionId = null;

        foreach (var transition in transitionsDoc.RootElement.GetProperty("transitions").EnumerateArray())
        {
            var to = transition.GetProperty("to");
            var matches = statusId is not null
                ? to.GetProperty("id").GetString() == statusId
                : string.Equals(to.GetProperty("name").GetString(), "Backlog", StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                transitionId = transition.GetProperty("id").GetString();
                break;
            }
        }

        if (transitionId is null)
        {
            return statusId is not null
                ? $"Issue {issueKey} was created, but no transition to status id '{statusId}' was available from its initial status."
                : $"Issue {issueKey} was created, but no transition to the default 'Backlog' status was available from its initial status.";
        }

        var transitionBody = JsonSerializer.Serialize(new { transition = new { id = transitionId } });
        var transitionContent = new StringContent(transitionBody, Encoding.UTF8, "application/json");

        var transitionResponse = await httpClient.PostAsync(transitionsUrl, transitionContent);
        if (transitionResponse.IsSuccessStatusCode) return null;

        var transitionErrorBody = await transitionResponse.Content.ReadAsStringAsync();
        return $"Issue {issueKey} was created, but transitioning it failed with " +
               $"{(int)transitionResponse.StatusCode} {transitionResponse.StatusCode}: {transitionErrorBody}";
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var httpClient = _httpClientFactory.CreateClient();

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_email}:{_apiToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return httpClient;
    }

    /// <summary>
    /// Throws with Jira's response body included when the request failed, instead of the bare
    /// status-code message EnsureSuccessStatusCode() gives.
    /// </summary>
    private static void EnsureSuccessWithBody(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;

        throw new HttpRequestException(
            $"Jira request failed with {(int)response.StatusCode} {response.StatusCode}: {body}");
    }
}
