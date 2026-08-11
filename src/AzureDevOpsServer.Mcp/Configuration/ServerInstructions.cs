using System.Text;

namespace AzureDevOpsServer.Mcp.Configuration;

public static class ServerInstructions
{
    public static string Build(AzureDevOpsServerOptions options, int toolCount)
    {
        var text = new StringBuilder();

        text.AppendLine(
            "This server talks to an on-premises Azure DevOps Server collection at " +
            $"{options.CollectionUrl} using a Personal Access Token. It exposes {toolCount} tools."
        );
        text.AppendLine();

        text.AppendLine(
            string.IsNullOrWhiteSpace(options.DefaultProject) ?
                "No default project is configured, so pass a project name to tools that need one." :
                $"The default project is '{options.DefaultProject}'. Omit the project argument unless the user asks about a different project."
        );

        if (options.ReadOnly)
        {
            text.AppendLine(
                "The server runs in read-only mode: tools that create, update, or delete anything are not available. " +
                "If the user asks for a change, explain that this server is configured for read access only."
            );
        }

        text.AppendLine();
        text.AppendLine("How to use these tools well:");
        text.AppendLine(
            "- Work items: pass a field list to get_work_item and get_work_items instead of pulling every field, " +
            "because descriptions can be large HTML documents. Use list_work_item_types and list_work_item_states " +
            "before setting a state, since process templates differ between projects."
        );
        text.AppendLine(
            "- Builds: start with get_build_timeline to find the failing task and its log id, then read only the " +
            "relevant part of that log with get_build_log and a line range."
        );
        text.AppendLine(
            "- Pull requests: get_pull_request already returns reviewer votes and merge status. " +
            "Use get_pull_request_policies to explain why a pull request cannot be completed."
        );
        text.AppendLine(
            "- Lists are capped and report truncation. When a result looks incomplete, raise the limit " +
            "instead of concluding that something does not exist."
        );

        if (!options.ReadOnly)
        {
            text.AppendLine();
            text.AppendLine(
                "Write operations change what other people see. Do not vote on pull requests, change their status, " +
                "update work items, approve deployments, or overwrite wiki pages unless the user asked for that specific action. " +
                "Note that create_or_update_wiki_page replaces the whole page content."
            );
        }

        return text.ToString().TrimEnd();
    }
}
