using System.Security.Cryptography;
using System.Text;
using AzureDevOpsServer.Mcp.Configuration;
using Microsoft.AspNetCore.Http;

namespace AzureDevOpsServer.Mcp.Http;

public sealed class HttpAccessMiddleware
{
    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;
    private readonly byte[]? _tokenHash;
    private readonly HashSet<string> _allowedOrigins;

    public HttpAccessMiddleware(RequestDelegate next, AzureDevOpsServerOptions options)
    {
        _next = next;
        _tokenHash = string.IsNullOrWhiteSpace(options.HttpToken) ? null : Hash(options.HttpToken.Trim());
        _allowedOrigins = new HashSet<string>(
            (options.HttpAllowedOrigins ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(origin => origin.TrimEnd('/')),
            StringComparer.OrdinalIgnoreCase
        );
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<HttpAccessGuardMetadata>() is null)
        {
            return _next(context);
        }

        // Browsers always send Origin and server-side callers such as Open WebUI do not; rejecting unknown ones blocks DNS rebinding.
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !_allowedOrigins.Contains(origin.TrimEnd('/')))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        if (_tokenHash is { } expected && !PresentsToken(context.Request, expected))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }

        return _next(context);
    }

    private static bool PresentsToken(HttpRequest request, byte[] expected)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Hashing first makes the comparison take the same time whatever the length of the presented token.
        return CryptographicOperations.FixedTimeEquals(Hash(header[BearerPrefix.Length..].Trim()), expected);
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
