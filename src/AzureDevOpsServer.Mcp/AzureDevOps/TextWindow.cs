namespace AzureDevOpsServer.Mcp.AzureDevOps;

// Narrows text to a 1-based, inclusive line range and then to at most maxChars characters, so a
// caller can page through a long document instead of receiving all of it. TotalChars describes the
// selected range before the character limit applies, the way get_build_log reports it.
internal static class TextWindow
{
    public static TextWindowResult Apply(string text, int? startLine, int? endLine, int maxChars)
    {
        var selected = SelectLines(text, startLine ?? 1, endLine ?? int.MaxValue);
        var kept = Truncate(selected, maxChars);
        return new TextWindowResult(kept, selected.Length, CountLines(text), kept.Length < selected.Length);
    }

    // A trailing line terminator ends the last line rather than starting an empty one.
    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var terminators = text.AsSpan().Count('\n');
        return text[^1] == '\n' ? terminators : terminators + 1;
    }

    private static string SelectLines(string text, int startLine, int endLine)
    {
        if (startLine == 1 && endLine == int.MaxValue)
        {
            return text;
        }

        var start = 0;
        for (var line = 1; line < startLine; line++)
        {
            var terminator = text.IndexOf('\n', start);
            if (terminator < 0)
            {
                return string.Empty;
            }

            start = terminator + 1;
        }

        var end = start;
        for (var line = startLine; line <= endLine; line++)
        {
            var terminator = text.IndexOf('\n', end);
            if (terminator < 0)
            {
                return text[start..];
            }

            end = terminator + 1;
        }

        return text[start..end];
    }

    // Backs off by one character rather than leave half of a surrogate pair at the end.
    private static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var cut = Math.Max(maxChars, 0);
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut];
    }
}

internal readonly record struct TextWindowResult(string Text, int TotalChars, int TotalLines, bool Truncated);
