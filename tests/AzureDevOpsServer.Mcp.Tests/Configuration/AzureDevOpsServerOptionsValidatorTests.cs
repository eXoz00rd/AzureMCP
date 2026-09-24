using AzureDevOpsServer.Mcp.AzureDevOps;
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

    [Fact]
    public void Validate_OverHttpWithTokenAndNoPersonalAccessToken_Succeeds()
    {
        var result = _validator.Validate(null, CreateValidHttpOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_OverHttpWithAnonymousOptInInsteadOfToken_Succeeds()
    {
        var options = CreateValidHttpOptions();
        options.HttpToken = null;
        options.HttpAllowAnonymous = true;

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_OverHttpWithoutTokenOrAnonymousOptIn_Fails()
    {
        var options = CreateValidHttpOptions();
        options.HttpToken = null;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(AzureDevOpsServerOptions.HttpTokenVariable, result.FailureMessage);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("0123456789abcdef0123456789abcde")]
    public void Validate_OverHttpWithGuessableToken_Fails(string token)
    {
        var options = CreateValidHttpOptions();
        options.HttpToken = token;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"{AzureDevOpsServerOptions.HttpTokenVariable} must be at least 32 characters", result.FailureMessage);
    }

    [Fact]
    public void Validate_OverHttpWithTokenOfMinimumLength_Succeeds()
    {
        var options = CreateValidHttpOptions();
        options.HttpToken = new string('a', AzureDevOpsServerOptionsValidator.MinimumHttpTokenLength);

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_OverHttpWithSharedPersonalAccessToken_Fails()
    {
        var options = CreateValidHttpOptions();
        options.PersonalAccessToken = "shared-pat";

        var result = _validator.Validate(null, options);

        // A configured PAT over HTTP would silently act for anyone whose request lacks their own.
        Assert.True(result.Failed);
        Assert.Contains(AzureDevOpsServerOptions.PersonalAccessTokenVariable, result.FailureMessage);
        Assert.Contains(RequestCredentialProvider.HeaderName, result.FailureMessage);
    }

    [Theory]
    [InlineData("stdio")]
    [InlineData(null)]
    public void Validate_OverStdioWithoutHttpToken_Succeeds(string? transport)
    {
        var options = CreateValidOptions();
        options.Transport = transport;

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080")]
    [InlineData("http://+:8080")]
    [InlineData("http://*:8080")]
    [InlineData("http://devops-mcp.example.local:8080")]
    [InlineData("http://127.0.0.1:8080;http://0.0.0.0:8081")]
    public void Validate_WithAnonymousAccessOnARoutableAddress_Fails(string httpUrl)
    {
        var options = CreateValidHttpOptions();
        options.HttpToken = null;
        options.HttpAllowAnonymous = true;
        options.HttpUrl = httpUrl;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(AzureDevOpsServerOptions.HttpAllowAnonymousVariable, result.FailureMessage);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://127.0.0.1:8080;http://[::1]:8080")]
    public void Validate_WithAnonymousAccessOnLoopback_Succeeds(string httpUrl)
    {
        var options = CreateValidHttpOptions();
        options.HttpToken = null;
        options.HttpAllowAnonymous = true;
        options.HttpUrl = httpUrl;

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithTokenOnARoutableAddress_Succeeds()
    {
        var options = CreateValidHttpOptions();
        options.HttpAllowAnonymous = true;
        options.HttpUrl = "http://0.0.0.0:8080";

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    private static AzureDevOpsServerOptions CreateValidHttpOptions()
    {
        var options = CreateValidOptions();
        options.Transport = "http";
        options.PersonalAccessToken = string.Empty;
        options.HttpToken = "shared-token-0123456789abcdef0123456789";
        return options;
    }
}
