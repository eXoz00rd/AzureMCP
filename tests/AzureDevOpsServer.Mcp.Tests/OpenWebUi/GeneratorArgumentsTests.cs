using OpenWebUiPluginGenerator;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.OpenWebUi;

public sealed class GeneratorArgumentsTests
{
    [Fact]
    public void TryParse_WithAllOptions_ReturnsThem()
    {
        var parsed = GeneratorArguments.TryParse(
            ["--output", "plugin.py", "--toolsets", "workitems", "--read-only", "--server-url", "http://azuremcp:8080/mcp"],
            out var arguments,
            out var error
        );

        Assert.True(parsed, error);
        Assert.Equal(new GeneratorArguments("plugin.py", "workitems", true, "http://azuremcp:8080/mcp"), arguments);
    }

    [Fact]
    public void TryParse_WithoutServerUrl_LeavesItForTheAdministrator()
    {
        var parsed = GeneratorArguments.TryParse(["--output", "plugin.py"], out var arguments, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(arguments);
        Assert.Equal(string.Empty, arguments.ServerUrl);
    }

    [Theory]
    [InlineData("--output --toolsets workitems", "--output requires a value.")]
    [InlineData("--output", "--output requires a value.")]
    [InlineData("--output plugin.py --verbose", "Unknown argument '--verbose'.")]
    [InlineData("--toolsets workitems", "--output is required.")]
    [InlineData("--output plugin.py --server-url ftp://azuremcp/mcp", "--server-url must be an absolute http or https URL.")]
    [InlineData("--output plugin.py --server-url azuremcp:8080/mcp", "--server-url must be an absolute http or https URL.")]
    [InlineData("--output plugin.py --download-url https://example.test/server", "Unknown argument '--download-url'.")]
    public void TryParse_WithMalformedArguments_ReportsTheProblem(string commandLine, string expectedError)
    {
        var parsed = GeneratorArguments.TryParse(commandLine.Split(' '), out var arguments, out var error);

        Assert.False(parsed);
        Assert.Null(arguments);
        Assert.Equal(expectedError, error);
    }
}
