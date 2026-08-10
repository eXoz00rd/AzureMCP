using AzureDevOpsServer.Mcp.Prompts;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Prompts;

public sealed class WorkflowPromptsTests
{
    [Fact]
    public void ReviewPullRequest_WithProject_NamesProjectAndTools()
    {
        var prompt = WorkflowPrompts.ReviewPullRequest("WebApp", 7, "Alpha");

        Assert.Contains("pull request 7", prompt);
        Assert.Contains("in project 'Alpha'", prompt);
        Assert.Contains("get_pull_request_changes", prompt);
        Assert.Contains("list_pull_request_threads", prompt);
    }

    [Fact]
    public void ReviewPullRequest_WithoutProject_RefersToDefaultProject()
    {
        var prompt = WorkflowPrompts.ReviewPullRequest("WebApp", 7);

        Assert.Contains("in the default project", prompt);
    }

    [Fact]
    public void DiagnoseBuildFailure_ChainsTimelineAndLog()
    {
        var prompt = WorkflowPrompts.DiagnoseBuildFailure(500, "Alpha");

        Assert.Contains("get_build_timeline", prompt);
        Assert.Contains("get_build_log", prompt);
        Assert.Contains("startLine", prompt);
    }

    [Fact]
    public void SprintStatus_UsesIterationsAndFieldList()
    {
        var prompt = WorkflowPrompts.SprintStatus("Alpha");

        Assert.Contains("list_iterations", prompt);
        Assert.Contains("query_work_items", prompt);
        Assert.Contains("field list", prompt);
    }
}
