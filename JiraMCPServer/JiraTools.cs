using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace JiraMCPServer;

[McpServerToolType]
public class JiraTools
{
    [McpServerTool, Description("Lists the work item statuses available in the configured Jira project (e.g. Backlog, In Progress, Done). " +
                                "Each status has an ID that other tools require, e.g. when creating a work item.")]
    public static async Task<string> GetAllWorkItemStatuses(JiraApiService jiraApiService)
    {
        var statuses = await jiraApiService.GetProjectStatusesAsync();
        return JsonSerializer.Serialize(statuses);
    }

    [McpServerTool, Description("Retrieves every work item in the configured Jira project, regardless of its status. " +
                                "Each item includes its key, summary (title), status name, issue type name and assignee.")]
    public static async Task<string> GetAllWorkItems(JiraApiService jiraApiService)
    {
        var workItems = await jiraApiService.GetAllWorkItemsAsync();
        return JsonSerializer.Serialize(workItems);
    }

    [McpServerTool, Description("Lists the issue types available in the configured Jira project (e.g. Task, Bug, Story). " +
                                "Each type has an ID that must be used when creating a work item.")]
    public static async Task<string> GetAllWorkItemTypes(JiraApiService jiraApiService)
    {
        var types = await jiraApiService.GetAllWorkItemTypesAsync();
        return JsonSerializer.Serialize(types);
    }

    [McpServerTool, Description("Creates a new work item (issue) in the configured Jira project with a title, description, issue type, and optional status. " +
                                "Call GetAllWorkItemTypes first to look up a valid issue type ID. " +
                                "Jira cannot set a status at creation time, so if a status ID is supplied it is applied as a follow-up transition; " +
                                "if omitted, the item is moved to Backlog when that transition is available.")]
    public static async Task<string> CreateWorkItem(
        JiraApiService jiraApiService,
        [Description("The title/summary for the work item")] string name,
        [Description("The detailed description for the work item")] string description,
        [Description("The issue type ID for the work item, obtained from the GetAllWorkItemTypes tool")] string issueTypeId,
        [Description("Optional status ID to move the work item to after creation, obtained from the GetAllWorkItemStatuses tool. Defaults to Backlog when omitted.")] string? statusId = null)
    {
        var workItem = await jiraApiService.CreateWorkItemAsync(name, description, issueTypeId, statusId);
        return JsonSerializer.Serialize(workItem);
    }
}