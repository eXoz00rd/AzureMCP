using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AzureDevOpsServer.Mcp.Prompts;

[McpServerPromptType]
public static class WorkflowPrompts
{
    [McpServerPrompt(Name = "review_pull_request")]
    [Description("Reviews a pull request: changed files, existing discussion, and a summary with concrete findings.")]
    public static string ReviewPullRequest(
        [Description("Repository name or id.")] string repository,
        [Description("Pull request id.")] int pullRequestId,
        [Description("Optional project name.")] string? project = null)
    {
        return $"""
               Review pull request {pullRequestId} in repository '{repository}'{ProjectClause(project)}.

               Steps:
               1. Call get_pull_request for the title, description, and source and target branches.
               2. Call get_pull_request_changes to see which files changed.
               3. Call get_file_content for the changed files that matter most; use a line budget instead of reading everything.
               4. Call list_pull_request_threads to see what reviewers already raised, and do not repeat those points.

               Then summarize: what the change does, concrete findings ordered by severity with file and line references,
               and anything that looks missing such as tests or documentation. Do not post comments or vote unless I ask.
               """;
    }

    [McpServerPrompt(Name = "diagnose_build_failure")]
    [Description("Finds the root cause of a failed build from its timeline and the log of the failing task.")]
    public static string DiagnoseBuildFailure(
        [Description("Build id.")] int buildId,
        [Description("Optional project name.")] string? project = null)
    {
        return $"""
               Diagnose why build {buildId} failed{ProjectClause(project)}.

               Steps:
               1. Call get_build_timeline for build {buildId} and find the records whose result is failed.
               2. Read the issue messages on those records; they usually name the error directly.
               3. Call get_build_log with the log id of the earliest failing task, and use startLine and endLine
                  to read around the failure instead of the whole log.
               4. If the failure looks like a code change, call list_commits for the build source branch to see recent commits.

               Then report: the failing stage and task, the underlying error, the most likely cause, and the smallest fix.
               """;
    }

    [McpServerPrompt(Name = "sprint_status")]
    [Description("Summarizes the current sprint: scope, states, and items that need attention.")]
    public static string SprintStatus(
        [Description("Optional project name.")] string? project = null)
    {
        return $"""
               Summarize the current sprint status{ProjectClause(project)}.

               Steps:
               1. Call list_iterations to find the iteration whose start and finish dates contain today.
               2. Call query_work_items with a WIQL query filtered on that iteration path, selecting id, title, state,
                  assigned to, and work item type.
               3. Call get_work_items for the returned ids with an explicit field list to keep the response small.

               Then report: how many items are in each state, which items are blocked or unassigned, what changed state
               most recently, and the risks to closing the sprint on time.
               """;
    }

    private static string ProjectClause(string? project)
    {
        return string.IsNullOrWhiteSpace(project) ?
            " in the default project" :
            $" in project '{project}'";
    }
}
