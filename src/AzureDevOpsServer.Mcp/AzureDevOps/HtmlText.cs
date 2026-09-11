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
    // A Private Use Area character standing in for a newline that must survive
    // CollapseWhitespace's per-line trimming untouched, because it falls inside a <pre>/<code>
    // block where whitespace is significant. Restored to a real '\n' once collapsing is done.
    private const char PreservedNewline = (char)0xE000;

    private static readonly HashSet<string> KnownTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "span", "br", "ul", "ol", "li", "a", "b", "i", "strong", "em", "u",
        "table", "thead", "tbody", "tr", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6",
        "blockquote", "pre", "code", "img", "hr"
    };

    // A value is only treated as HTML when it contains at least one recognizable tag, so a plain
    // value that happens to contain "<" or ">" (a title like "List<Item>") is left untouched
    // instead of being misread as markup. Uses the same tag scan as ToPlainText, so detection and
    // conversion always agree on what counts as a real tag.
    public static bool LooksLikeHtml(string value)
    {
        var index = 0;
        while (index < value.Length)
        {
            var tagStart = value.IndexOf('<', index);
            if (tagStart < 0)
            {
                return false;
            }

            if (TryReadKnownTag(value, tagStart, out _, out _, out _))
            {
                return true;
            }

            index = tagStart + 1;
        }

        return false;
    }

    public static string ToPlainText(string html)
    {
        var builder = new StringBuilder(html.Length);
        var anchorHrefs = new Stack<string?>();
        var index = 0;
        var preserveDepth = 0;

        while (index < html.Length)
        {
            var tagStart = html.IndexOf('<', index);
            if (tagStart < 0)
            {
                AppendLiteral(builder, html, index, html.Length - index, preserveDepth > 0);
                break;
            }

            // Only a span that is both syntactically a tag and names a tag this converter knows
            // about is treated as markup. Anything else — a bare comparison like "x < 5", or a
            // generic type like "List<Item>" — is literal text, including its angle brackets, so
            // it is preserved rather than silently swallowed as an unrecognized tag.
            if (!TryReadKnownTag(html, tagStart, out var tagEnd, out var tag, out var closing))
            {
                AppendLiteral(builder, html, index, tagStart - index + 1, preserveDepth > 0);
                index = tagStart + 1;
                continue;
            }

            AppendLiteral(builder, html, index, tagStart - index, preserveDepth > 0);

            // <pre>/<code> content is whitespace-significant, so its newlines and indentation
            // must survive CollapseWhitespace's per-line trimming below untouched.
            if (ExtractTagName(tag, closing) is "pre" or "code")
            {
                preserveDepth = closing ? Math.Max(0, preserveDepth - 1) : preserveDepth + 1;
            }
            else
            {
                AppendTagReplacement(builder, tag, anchorHrefs, preserveDepth > 0);
            }

            index = tagEnd + 1;
        }

        var collapsed = CollapseWhitespace(WebUtility.HtmlDecode(builder.ToString()));
        return collapsed.Replace(PreservedNewline, '\n');
    }

    // Copies a literal (non-tag) span of html into the builder. Inside a preserve region, any
    // newline in that span is replaced with the sentinel so CollapseWhitespace leaves it, and the
    // indentation around it, alone.
    private static void AppendLiteral(StringBuilder builder, string html, int start, int length, bool preserving)
    {
        if (!preserving)
        {
            builder.Append(html, start, length);
            return;
        }

        var end = start + length;
        var i = start;
        while (i < end)
        {
            var c = html[i];
            if (c == '\r' && i + 1 < end && html[i + 1] == '\n')
            {
                builder.Append(PreservedNewline);
                i += 2;
                continue;
            }

            builder.Append(c is '\n' or '\r' ? PreservedNewline : c);
            i++;
        }
    }

    // Reads the tag starting at "<" (tagStart) and reports whether it is both a syntactically
    // valid, quote-aware tag and one of the known tag names this converter understands. A span
    // that merely looks tag-shaped ("<Item>" inside "List<Item>") is rejected here rather than
    // silently consumed, so its text survives in the output.
    private static bool TryReadKnownTag(string html, int tagStart, out int tagEnd, out string tag, out bool closing)
    {
        tagEnd = -1;
        tag = string.Empty;
        closing = false;

        var nameStart = tagStart + 1;
        if (nameStart < html.Length && html[nameStart] == '/')
        {
            closing = true;
            nameStart++;
        }

        if (nameStart >= html.Length || !char.IsAsciiLetter(html[nameStart]))
        {
            return false;
        }

        var end = FindTagEnd(html, tagStart);
        if (end < 0)
        {
            return false;
        }

        var content = html[(tagStart + 1)..end];
        if (!KnownTags.Contains(ExtractTagName(content, closing)))
        {
            return false;
        }

        tagEnd = end;
        tag = content;
        return true;
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

    private static void AppendTagReplacement(StringBuilder builder, string tag, Stack<string?> anchorHrefs, bool preserving)
    {
        var closing = tag.StartsWith('/');
        var name = ExtractTagName(tag, closing);
        var newline = preserving ? PreservedNewline : '\n';

        switch (name)
        {
            case "br":
                builder.Append(newline);
                break;
            case "li":
                if (!closing)
                {
                    builder.Append(newline).Append("- ");
                }

                break;
            case "p" or "div" or "blockquote" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (closing)
                {
                    builder.Append(newline).Append(newline);
                }

                break;
            case "tr":
                if (closing)
                {
                    builder.Append(newline);
                }

                break;
            case "td" or "th":
                if (closing)
                {
                    builder.Append('\t');
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

    // Requires "href" to start at the beginning of the tag content or after whitespace, so
    // "data-href" is not misread as the "href" attribute.
    [GeneratedRegex("(?:^|\\s)href\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();
}
