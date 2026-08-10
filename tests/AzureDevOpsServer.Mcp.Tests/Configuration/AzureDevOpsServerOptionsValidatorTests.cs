using AzureDevOpsServer.Mcp.Configuration;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.Configuration;

public sealed class AzureDevOpsServerOptionsValidatorTests
{
    private readonly AzureDevOpsServerOptionsValidator _validator = new();

    private static AzureDevOpsServerOptions CreateValidOptions()
    {
        return new AzureDevOpsServerOptions
        {
            CollectionUrl = "https://devops.example.local/DefaultCollection",
            PersonalAccessToken = "pat-value",
            ApiVersion = "7.0"
        };
    }

    [Fact]
    public void Validate_WithValidOptions_Succeeds()
    {
        var result = _validator.Validate(null, CreateValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithMissingCollectionUrl_Fails()
    {
        var options = CreateValidOptions();
        options.CollectionUrl = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(AzureDevOpsServerOptions.CollectionUrlVariable, result.FailureMessage);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://devops.example.local")]
    [InlineData("/relative/path")]
    public void Validate_WithInvalidCollectionUrl_Fails(string collectionUrl)
    {
        var options = CreateValidOptions();
        options.CollectionUrl = collectionUrl;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("absolute http(s) URL", result.FailureMessage);
    }

    [Fact]
    public void Validate_WithMissingPersonalAccessToken_Fails()
    {
        var options = CreateValidOptions();
        options.PersonalAccessToken = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(AzureDevOpsServerOptions.PersonalAccessTokenVariable, result.FailureMessage);
    }

    [Fact]
    public void Validate_WithEmptyApiVersion_Fails()
    {
        var options = CreateValidOptions();
        options.ApiVersion = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(AzureDevOpsServerOptions.ApiVersionVariable, result.FailureMessage);
    }
}