using OpenWebUiPluginGenerator;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.OpenWebUi;

public sealed class GeneratorArgumentsTests
{
    private static readonly string Sha256 = new('a', 64);

    [Fact]
    public void TryParse_WithAllOptions_ReturnsThem()
    {
        var parsed = GeneratorArguments.TryParse(
            ["--output", "plugin.py", "--toolsets", "workitems", "--read-only", "--download-url", "https://example.test/server", "--sha256", Sha256.ToUpperInvariant()],
            out var arguments,
            out var error
        );

        Assert.True(parsed, error);
        Assert.Equal(new GeneratorArguments("plugin.py", "workitems", true, "https://example.test/server", Sha256), arguments);
    }

    [Theory]
    [InlineData("--output --toolsets workitems", "--output requires a value.")]
    [InlineData("--output", "--output requires a value.")]
    [InlineData("--output plugin.py --verbose", "Unknown argument '--verbose'.")]
    [InlineData("--toolsets workitems", "--output is required.")]
    [InlineData("--output plugin.py --download-url https://example.test/server", "--download-url and --sha256 must be given together.")]
    [InlineData("--output plugin.py --download-url ftp://example.test/server --sha256 SHA", "--download-url must be an absolute http or https URL.")]
    [InlineData("--output plugin.py --download-url server --sha256 SHA", "--download-url must be an absolute http or https URL.")]
    [InlineData("--output plugin.py --download-url https://example.test/server --sha256 abc", "--sha256 must be 64 hexadecimal characters.")]
    public void TryParse_WithMalformedArguments_ReportsTheProblem(string commandLine, string expectedError)
    {
        var args = commandLine.Replace("SHA", Sha256).Split(' ');

        var parsed = GeneratorArguments.TryParse(args, out var arguments, out var error);

        Assert.False(parsed);
        Assert.Null(arguments);
        Assert.Equal(expectedError, error);
    }
}
