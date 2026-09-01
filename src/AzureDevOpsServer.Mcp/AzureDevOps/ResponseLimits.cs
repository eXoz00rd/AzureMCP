namespace AzureDevOpsServer.Mcp.AzureDevOps;

public static class ResponseLimits
{
    public const int DefaultMaxChars = 30_000;
    public const int DefaultMaxItems = 500;
    public const int DefaultListTop = 100;
    public const int DefaultBuildCount = 20;
    public const int DefaultReleaseCount = 20;
    public const int DefaultCommitCount = 20;
    public const int DefaultQueryDepth = 2;
    public const int DefaultNodeDepth = 3;

    public const int MinTop = 1;
    public const int MaxTop = 1_000;
    public const int MinMaxChars = 1;
    public const int MaxMaxChars = 1_000_000;
    public const int MinMaxItems = 1;
    public const int MaxMaxItems = 10_000;
    public const int MinDepth = 0;
    public const int MaxDepth = 10;

    public const string TopRange = "1-1000";
    public const string MaxCharsRange = "1-1000000";
    public const string MaxItemsRange = "1-10000";
    public const string DepthRange = "0-10";

    public static int ResolveTop(int? value, int defaultValue = DefaultListTop)
    {
        return Resolve(value, defaultValue, MinTop, MaxTop, "top");
    }

    public static int ResolveMaxChars(int? value, int defaultValue = DefaultMaxChars)
    {
        return Resolve(value, defaultValue, MinMaxChars, MaxMaxChars, "maxChars");
    }

    public static int ResolveMaxItems(int? value, int defaultValue = DefaultMaxItems)
    {
        return Resolve(value, defaultValue, MinMaxItems, MaxMaxItems, "maxItems");
    }

    public static int ResolveDepth(int? value, int defaultValue)
    {
        return Resolve(value, defaultValue, MinDepth, MaxDepth, "depth");
    }

    // Rejects out-of-range values instead of clamping them, so a caller that asks for more
    // than the server-side safety limit learns about it rather than silently getting less.
    private static int Resolve(int? value, int defaultValue, int minimum, int maximum, string parameterName)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (value.Value < minimum || value.Value > maximum)
        {
            throw new AzureDevOpsClientException(
                $"'{parameterName}' must be between {minimum} and {maximum}. Received {value.Value}."
            );
        }

        return value.Value;
    }
}
