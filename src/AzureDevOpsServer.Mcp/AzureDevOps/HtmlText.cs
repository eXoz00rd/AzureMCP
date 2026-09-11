using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

// Azure DevOps rich-text fields (System.Description, Repro Steps, Acceptance Criteria, ...) are
// stored and returned as HTML. This converts a field value to plain text without pulling in a
// full HTML parser dependency for what is, in practice, a small set of tags emitted by the
// Azure DevOps Server rich text editor.
internal static partial class HtmlText
{
    // A value is only treated as HTML when it contains at least one recognizable tag, so a plain
    // value that happens to contain "<" or ">" (a title like "List<Item>") is left untouched
    // instead of being misread as markup.
    public static bool LooksLikeHtml(string value)
    {
        return KnownTagRegex().IsMatch(value);
    }

    public static string ToPlainText(string html)
    {
        var builder = new StringBuilder(html.Length);
        var anchorHrefs = new Stack<string?>();
        var index = 0;

        while (index < html.Length)
        {
            var tagStart = html.IndexOf('<', index);
            if (tagStart < 0)
            {
                builder.Append(html, index, html.Length - index);
                break;
            }

            builder.Append(html, index, tagStart - index);

            var tagEnd = FindTagEnd(html, tagStart);
            if (tagEnd < 0)
            {
                builder.Append(html, tagStart, html.Length - tagStart);
                break;
            }

            AppendTagReplacement(builder, html[(tagStart + 1)..tagEnd], anchorHrefs);
            index = tagEnd + 1;
        }

        return CollapseWhitespace(WebUtility.HtmlDecode(builder.ToString()));
    }

    // Tag boundaries are quote-aware so a ">" inside an attribute value (for example
    // title="1 > 0") does not end the tag early.
    private static int FindTagEnd(string html, int tagStart)
    {
        var inQuote = '\0';
        for (var i = tagStart + 1; i < html.Length; i++)
        {
            var c = html[i];
            if (inQuote != '\0')
            {
                if (c == inQuote)
                {
                    inQuote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                inQuote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private static void AppendTagReplacement(StringBuilder builder, string tag, Stack<string?> anchorHrefs)
    {
        var closing = tag.StartsWith('/');
        var name = ExtractTagName(tag, closing);

        switch (name)
        {
            case "br":
                builder.Append('\n');
                break;
            case "li":
                if (!closing)
                {
                    builder.Append("\n- ");
                }

                break;
            case "p" or "div" or "blockquote" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (closing)
                {
                    builder.Append("\n\n");
                }

                break;
            case "tr":
                if (closing)
                {
                    builder.Append('\n');
                }

                break;
            case "a":
                if (closing)
                {
                    if (anchorHrefs.Count > 0 && anchorHrefs.Pop() is { Length: > 0 } href)
                    {
                        builder.Append(" (").Append(href).Append(')');
                    }
                }
                else
                {
                    var match = HrefRegex().Match(tag);
                    anchorHrefs.Push(match.Success ? match.Groups[1].Value : null);
                }

                break;
        }
    }

    private static string ExtractTagName(string tag, bool closing)
    {
        var start = closing ? 1 : 0;
        var end = start;
        while (end < tag.Length && char.IsLetterOrDigit(tag[end]))
        {
            end++;
        }

        return tag[start..end].ToLowerInvariant();
    }

    // Collapses runs of blank lines to a single blank line and trims trailing whitespace per
    // line, so tag-driven line breaks read as normal paragraphs instead of ragged spacing.
    private static string CollapseWhitespace(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder();
        var pendingBlankLine = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                pendingBlankLine = builder.Length > 0;
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(pendingBlankLine ? "\n\n" : "\n");
            }

            builder.Append(line);
            pendingBlankLine = false;
        }

        return builder.ToString();
    }

    [GeneratedRegex(
        "</?(?:p|div|span|br|ul|ol|li|a|b|i|strong|em|u|table|thead|tbody|tr|td|th|h[1-6]|blockquote|pre|code|img|hr)\\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex KnownTagRegex();

    [GeneratedRegex("href\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();
}
