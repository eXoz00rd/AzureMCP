using System.Net;
using System.Text;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

// Azure DevOps rich-text fields (System.Description, Repro Steps, Acceptance Criteria, ...) are
// stored and returned as HTML. This converts a field value to plain text without pulling in a
// full HTML parser dependency for what is, in practice, a small set of tags emitted by the
// Azure DevOps Server rich text editor. Only WorkItemTools.RichTextFields decides which fields
// are ever passed through here; this converter has no opinion on that.
internal static class HtmlText
{
    // A Private Use Area character standing in for a newline that must survive
    // CollapseWhitespace's per-line trimming untouched, because it falls inside (or bounds) a
    // <pre>/<code> block where whitespace is significant. Restored to a real '\n' once
    // collapsing is done.
    private const char PreservedNewline = (char)0xE000;

    private static readonly HashSet<string> KnownTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "span", "br", "ul", "ol", "li", "a", "b", "i", "strong", "em", "u",
        "table", "thead", "tbody", "tr", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6",
        "blockquote", "pre", "code", "img", "hr"
    };

    public static string ToPlainText(string html)
    {
        var builder = new StringBuilder(html.Length);
        var anchorHrefs = new Stack<string?>();
        var listCounters = new Stack<int>();
        var index = 0;
        var preserveDepth = 0;
        var cellDepth = 0;

        while (index < html.Length)
        {
            var tagStart = html.IndexOf('<', index);
            if (tagStart < 0)
            {
                AppendLiteral(builder, html, index, html.Length - index, preserveDepth > 0);
                break;
            }

            // Only a span that is both syntactically a tag and names a tag this converter knows
            // about is treated as markup. A syntactically valid but unrecognized tag (for
            // example a custom element) is preserved verbatim as one unit, quote-aware, so a
            // "<" inside one of its own quoted attribute values is never mistaken for a nested
            // tag of its own. Anything that is not tag-shaped at all — a bare comparison like
            // "x < 5", or a generic type like "List<Item>" — is literal text one character at a
            // time, including its angle brackets.
            if (!TryReadKnownTag(html, tagStart, out var tagEnd, out var tag, out var closing))
            {
                var unknownTagEnd = IsTagStart(html, tagStart) ? FindTagEnd(html, tagStart) : -1;
                var skipLength = unknownTagEnd >= 0 ? unknownTagEnd - tagStart + 1 : 1;
                AppendLiteral(builder, html, index, tagStart - index + skipLength, preserveDepth > 0);
                index = tagStart + skipLength;
                continue;
            }

            AppendLiteral(builder, html, index, tagStart - index, preserveDepth > 0);

            var name = ExtractTagName(tag, closing);
            if (name is "pre" or "code")
            {
                // <pre>/<code> content is whitespace-significant end to end, including leading
                // or trailing spaces with no adjacent newline, so a sentinel boundary is emitted
                // unconditionally on both sides: it shields the block's true first and last
                // characters from CollapseWhitespace's per-line Trim() below, and it also keeps
                // adjacent content from being joined onto the block when nothing else already
                // separates them.
                preserveDepth = closing ? Math.Max(0, preserveDepth - 1) : preserveDepth + 1;
                builder.Append(PreservedNewline);
            }
            else if (name is "td" or "th")
            {
                // Tracked here (rather than left to AppendTagReplacement) so nested block tags
                // inside a cell (case below) can be told to stay silent instead of breaking the
                // cell across lines, which would otherwise strip the tab as leading whitespace.
                if (closing)
                {
                    cellDepth = Math.Max(0, cellDepth - 1);
                    builder.Append('\t');
                }
                else
                {
                    cellDepth++;
                }
            }
            else
            {
                AppendTagReplacement(builder, tag, anchorHrefs, listCounters, preserveDepth > 0, cellDepth > 0);
            }

            index = tagEnd + 1;
        }

        var collapsed = CollapseWhitespace(WebUtility.HtmlDecode(builder.ToString()));
        return collapsed.Replace(PreservedNewline, '\n');
    }

    // Copies a literal (non-tag) span of html into the builder.
    // - Outside a preserve region, a text node that is nothing but whitespace containing a
    //   newline (the indentation pretty-printed HTML leaves between tags) carries no content and
    //   is dropped, so it cannot show up as a spurious blank line; a plain inline space (no
    //   newline) is kept, since that is likely the actual space between two elements.
    // - Inside a preserve region, any newline in the span is replaced with the sentinel so
    //   CollapseWhitespace leaves it, and the indentation around it, alone.
    private static void AppendLiteral(StringBuilder builder, string html, int start, int length, bool preserving)
    {
        if (!preserving)
        {
            if (IsInsignificantWhitespace(html, start, length))
            {
                return;
            }

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

    private static bool IsInsignificantWhitespace(string html, int start, int length)
    {
        var sawNewline = false;
        for (var i = start; i < start + length; i++)
        {
            var c = html[i];
            if (!char.IsWhiteSpace(c))
            {
                return false;
            }

            sawNewline = sawNewline || c is '\n' or '\r';
        }

        return sawNewline;
    }

    // Whether the "<" at tagStart begins something syntactically tag-shaped (immediately, or
    // after "/", followed by an ASCII letter), as opposed to arbitrary text like "x < 5" or
    // "List<Item>".
    private static bool IsTagStart(string html, int tagStart)
    {
        var i = tagStart + 1;
        if (i < html.Length && html[i] == '/')
        {
            i++;
        }

        return i < html.Length && char.IsAsciiLetter(html[i]);
    }

    // Reads the tag starting at "<" (tagStart) and reports whether it is both a syntactically
    // valid, quote-aware tag and one of the known tag names this converter understands.
    private static bool TryReadKnownTag(string html, int tagStart, out int tagEnd, out string tag, out bool closing)
    {
        tagEnd = -1;
        tag = string.Empty;
        closing = tagStart + 1 < html.Length && html[tagStart + 1] == '/';

        if (!IsTagStart(html, tagStart))
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

    private static void AppendTagReplacement(
        StringBuilder builder,
        string tag,
        Stack<string?> anchorHrefs,
        Stack<int> listCounters,
        bool preserving,
        bool insideTableCell)
    {
        var closing = tag.StartsWith('/');
        var name = ExtractTagName(tag, closing);
        var newline = preserving ? PreservedNewline : '\n';

        switch (name)
        {
            case "br":
                builder.Append(newline);
                break;
            // The marker (bullet, or a running number for an <ol>) is emitted on open and the
            // line break on close, so consecutive items are separated without an extra blank
            // line, and content that follows the list still gets a break after the last item.
            case "li":
                if (closing)
                {
                    builder.Append(newline);
                }
                else if (listCounters.Count > 0 && listCounters.Peek() > 0)
                {
                    var ordinal = listCounters.Pop();
                    builder.Append(ordinal).Append(". ");
                    listCounters.Push(ordinal + 1);
                }
                else
                {
                    builder.Append("- ");
                }

                break;
            case "ul" or "ol":
                if (closing)
                {
                    if (listCounters.Count > 0)
                    {
                        listCounters.Pop();
                    }
                }
                else
                {
                    AppendBoundaryIfNeeded(builder, newline);
                    listCounters.Push(name == "ol" ? 1 : 0);
                }

                break;
            case "p" or "div" or "blockquote" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                // A block tag nested inside a table cell would otherwise split the cell across
                // lines, stranding the closing td/th's tab as leading whitespace that
                // CollapseWhitespace then trims away, so it stays silent on both open and close.
                if (insideTableCell)
                {
                    break;
                }

                if (closing)
                {
                    builder.Append(newline).Append(newline);
                }
                else if (builder.Length > 0 && builder[^1] is not ('\n' or PreservedNewline))
                {
                    // Content that precedes this block with no separator of its own (for
                    // example inline text right before a <p>) would otherwise be joined onto it.
                    builder.Append(newline).Append(newline);
                }

                break;
            case "tr":
                if (closing)
                {
                    builder.Append(newline);
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
                    anchorHrefs.Push(FindHrefValue(tag));
                }

                break;
        }
    }

    // Appends nothing when the builder already ends in a line break (real or preserved); the
    // caller uses this to avoid opening a redundant blank line ahead of a block-level boundary
    // that already has one.
    private static void AppendBoundaryIfNeeded(StringBuilder builder, char newline)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var last = builder[^1];
        if (last is '\n' or PreservedNewline)
        {
            return;
        }

        builder.Append(newline);
    }

    // Quote-aware search for an "href" attribute: a candidate at a proper attribute-name
    // boundary (start of the tag content, or after whitespace) that is not inside another
    // attribute's quoted value. This keeps "data-href" from matching and keeps an "href"-shaped
    // substring inside another attribute's value (for example title="… href='fake'") from being
    // read as the real link target.
    private static string? FindHrefValue(string tag)
    {
        var inQuote = '\0';

        for (var i = 0; i < tag.Length; i++)
        {
            var c = tag[i];
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
                continue;
            }

            var atBoundary = i == 0 || char.IsWhiteSpace(tag[i - 1]);
            if (!atBoundary || i + 4 > tag.Length ||
                string.Compare(tag, i, "href", 0, 4, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            var j = i + 4;
            while (j < tag.Length && char.IsWhiteSpace(tag[j]))
            {
                j++;
            }

            if (j >= tag.Length || tag[j] != '=')
            {
                continue;
            }

            j++;
            while (j < tag.Length && char.IsWhiteSpace(tag[j]))
            {
                j++;
            }

            if (j >= tag.Length || tag[j] is not ('"' or '\''))
            {
                continue;
            }

            var quote = tag[j];
            var valueStart = j + 1;
            var valueEnd = tag.IndexOf(quote, valueStart);
            if (valueEnd >= 0)
            {
                return tag[valueStart..valueEnd];
            }
        }

        return null;
    }

    // A name must be followed by whitespace, the self-closing "/", or the end of the tag
    // content to be accepted; otherwise it is a prefix of a longer, unrecognized name (for
    // example "p-custom" must not be read as the known tag "p").
    private static string ExtractTagName(string tag, bool closing)
    {
        var start = closing ? 1 : 0;
        var end = start;
        while (end < tag.Length && char.IsLetterOrDigit(tag[end]))
        {
            end++;
        }

        if (end < tag.Length && tag[end] != '/' && !char.IsWhiteSpace(tag[end]))
        {
            return string.Empty;
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
}
