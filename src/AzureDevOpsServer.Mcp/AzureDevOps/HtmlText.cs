using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AzureDevOpsServer.Mcp.AzureDevOps;

// Azure DevOps rich-text fields (System.Description, Repro Steps, Acceptance Criteria, ...) are
// stored and returned as HTML. This converts a field value to plain text without pulling in a
// full HTML parser dependency for what is, in practice, a small set of tags emitted by the
// Azure DevOps Server rich text editor. Only WorkItemTools.RichTextFields decides which fields
// are ever passed through here; this converter has no opinion on that.
internal static partial class HtmlText
{
    // A Private Use Area character standing in for a newline that must survive
    // CollapseWhitespace's per-line trimming untouched, because it falls inside (or bounds) a
    // <pre> block where whitespace is significant. Restored to a real '\n' once collapsing is
    // done.
    private const char PreservedNewline = (char)0xE000;

    // A Private Use Area character standing in for a table-cell tab that must survive
    // CollapseWhitespace's per-line trimming untouched, because a leading or trailing empty cell
    // would otherwise have its separator stripped as ordinary line-edge whitespace. Restored to
    // a real '\t' once collapsing is done.
    private const char PreservedTab = (char)0xE001;

    // The standard Unicode replacement character, used to neutralize a stray occurrence of
    // either sentinel above found in real input (see SanitizeReservedSentinels).
    private const char ReplacementCharacter = (char)0xFFFD;

    private static readonly HashSet<string> KnownTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "span", "br", "ul", "ol", "li", "a", "b", "i", "strong", "em", "u",
        "s", "strike", "del", "ins", "sub", "sup", "mark", "small", "font",
        "table", "thead", "tbody", "tr", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6",
        "blockquote", "pre", "code", "img", "hr"
    };

    // PreservedNewline and PreservedTab are also ordinary Unicode characters that a field value
    // could (vanishingly unlikely, but not impossible) already contain, either as the literal
    // character or as a numeric character reference (for example "&#xE000;") that WebUtility.
    // HtmlDecode resolves to the same character later, after this method has already run. Since
    // neither has a defined meaning outside a private, per-application agreement, a stray
    // occurrence of either form is replaced with the standard Unicode replacement character up
    // front, so the final Replace calls in ToPlainText can never mistake real field content —
    // however it was originally written — for one of this converter's own markers.
    private static string SanitizeReservedSentinels(string html)
    {
        html = NumericCharacterReference().Replace(
            html,
            match =>
            {
                var isHex = match.Groups["hex"].Success;
                var digits = isHex ? match.Groups["hex"].Value : match.Groups["dec"].Value;
                var style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
                return long.TryParse(digits, style, CultureInfo.InvariantCulture, out var value) &&
                    value is 0xE000 or 0xE001 ?
                    ReplacementCharacter.ToString() :
                    match.Value;
            }
        );

        return html.IndexOf(PreservedNewline) < 0 && html.IndexOf(PreservedTab) < 0 ?
            html :
            html.Replace(PreservedNewline, ReplacementCharacter).Replace(PreservedTab, ReplacementCharacter);
    }

    [GeneratedRegex(@"&#(?:x(?<hex>[0-9a-fA-F]+)|(?<dec>[0-9]+));", RegexOptions.IgnoreCase)]
    private static partial Regex NumericCharacterReference();

    public static string ToPlainText(string html)
    {
        html = SanitizeReservedSentinels(html);

        var builder = new StringBuilder(html.Length);
        var anchorHrefs = new Stack<string?>();
        var listCounters = new Stack<int>();
        var index = 0;
        var preserveDepth = 0;
        var cellDepth = 0;
        var isFirstCellInRow = true;
        var listItemDepth = 0;

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
                if (IsTagStart(html, tagStart))
                {
                    var unknownTagEnd = FindTagEnd(html, tagStart);
                    if (unknownTagEnd >= 0)
                    {
                        AppendLiteral(builder, html, index, unknownTagEnd - index + 1, preserveDepth > 0);
                        index = unknownTagEnd + 1;
                        continue;
                    }

                    // No terminator was found anywhere in the rest of the string, so no tag
                    // starting at or after this position can terminate either: append the
                    // remainder as one literal run and stop, rather than re-scanning it one "<"
                    // at a time, which would be quadratic for malformed input containing many
                    // unterminated tag-like fragments.
                    AppendLiteral(builder, html, index, html.Length - index, preserveDepth > 0);
                    break;
                }

                AppendLiteral(builder, html, index, tagStart - index + 1, preserveDepth > 0);
                index = tagStart + 1;
                continue;
            }

            AppendLiteral(builder, html, index, tagStart - index, preserveDepth > 0);

            var name = ExtractTagName(tag, closing);
            if (name is "pre")
            {
                // <pre> content is whitespace-significant end to end, including leading or
                // trailing spaces with no adjacent newline, so a sentinel boundary is emitted
                // unconditionally on both sides: it shields the block's true first and last
                // characters from CollapseWhitespace's per-line Trim() below, and it also keeps
                // adjacent content from being joined onto the block when nothing else already
                // separates them.
                preserveDepth = closing ? Math.Max(0, preserveDepth - 1) : preserveDepth + 1;
                builder.Append(PreservedNewline);
            }
            else if (name is "code")
            {
                // <code> is normally inline (for example "Run <code>foo()</code> now"), so
                // unlike <pre> it does not get its own boundary newline — only its own content's
                // whitespace is protected, which also covers the common <pre><code>...</code></pre>
                // block-code case since preserveDepth simply nests one level deeper there.
                preserveDepth = closing ? Math.Max(0, preserveDepth - 1) : preserveDepth + 1;
            }
            else if (name is "tr")
            {
                if (closing)
                {
                    builder.Append('\n');
                }
                else
                {
                    isFirstCellInRow = true;
                }
            }
            else if (name is "td" or "th")
            {
                // Tracked here (rather than left to AppendTagReplacement) so nested block tags
                // inside a cell (case below) can be told to stay silent instead of breaking the
                // cell across lines. The separator itself is emitted as a sentinel before each
                // non-first cell's content (not after every cell's close), so a leading or
                // trailing empty cell's tab is not later stripped as ordinary line-edge
                // whitespace by CollapseWhitespace's per-line Trim().
                if (closing)
                {
                    cellDepth = Math.Max(0, cellDepth - 1);
                }
                else
                {
                    if (!isFirstCellInRow)
                    {
                        // Pretty-printed whitespace between cells (for example a newline and
                        // indentation before this <td>) was already collapsed to a plain space
                        // by AppendLiteral; that space must not sit between the previous cell's
                        // content and this separator, so it is trimmed before the tab goes in.
                        TrimTrailingSpace(builder);
                        builder.Append(PreservedTab);
                    }

                    isFirstCellInRow = false;
                    cellDepth++;
                }
            }
            else
            {
                AppendTagReplacement(
                    builder,
                    tag,
                    anchorHrefs,
                    listCounters,
                    preserveDepth > 0,
                    cellDepth > 0,
                    ref listItemDepth
                );
            }

            index = tagEnd + 1;
        }

        var collapsed = CollapseWhitespace(WebUtility.HtmlDecode(builder.ToString()));
        return collapsed.Replace(PreservedNewline, '\n').Replace(PreservedTab, '\t');
    }

    // Copies a literal (non-tag) span of html into the builder.
    // - Outside a preserve region, HTML's own whitespace-collapsing rule applies: every maximal
    //   run of whitespace — a single space just as much as a run spanning a newline and several
    //   more characters — collapses to one space, wherever it falls in the span, not only when
    //   the whole span is nothing but whitespace. A run between two tags (for example between
    //   <ul> and <li>) is pure source indentation; the space it collapses to then lands at the
    //   edge of a line once CollapseWhitespace's per-line Trim() runs below, so it disappears
    //   there anyway. A run inside a normal text node (for example "one\n  two", or
    //   "one    two") is not at a line edge, so the collapsed space survives as the single word
    //   separator HTML renders it as, instead of a hard line break or untouched run of spaces.
    // - Inside a preserve region, any newline in the span is replaced with the sentinel so
    //   CollapseWhitespace leaves it, and the indentation around it, alone.
    private static void AppendLiteral(StringBuilder builder, string html, int start, int length, bool preserving)
    {
        var end = start + length;

        if (!preserving)
        {
            var i = start;
            while (i < end)
            {
                var c = html[i];
                if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                    i++;
                    continue;
                }

                builder.Append(' ');
                while (i < end && char.IsWhiteSpace(html[i]))
                {
                    i++;
                }
            }

            return;
        }

        var j = start;
        while (j < end)
        {
            var c = html[j];
            if (c == '\r' && j + 1 < end && html[j + 1] == '\n')
            {
                builder.Append(PreservedNewline);
                j += 2;
                continue;
            }

            builder.Append(c is '\n' or '\r' ? PreservedNewline : c);
            j++;
        }
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
    // title="1 > 0") does not end the tag early. An unquoted "<" invalidates the tag instead of
    // being skipped over: a real tag's own syntax never contains one outside a quoted attribute
    // value, so allowing it through would let a later, unrelated tag's ">" be misread as this
    // one's end — for example "<b unterminated</p>" must not consume the "</p>" that belongs to
    // something else entirely.
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
            else if (c == '<')
            {
                return -1;
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
        bool insideTableCell,
        ref int listItemDepth)
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
            // line break on close (only when the builder does not already end in one, so a
            // nested list's own closing line break is not duplicated by its parent item's),
            // so consecutive items are separated without an extra blank line, and content that
            // follows the list still gets a break after the last item.
            case "li":
                if (closing)
                {
                    listItemDepth = Math.Max(0, listItemDepth - 1);
                    AppendBoundaryIfNeeded(builder, newline);
                }
                else if (listCounters.Count > 0 && listCounters.Peek() > 0)
                {
                    var ordinal = listCounters.Pop();
                    builder.Append(ordinal).Append(". ");
                    listCounters.Push(ordinal + 1);
                    listItemDepth++;
                }
                else
                {
                    builder.Append("- ");
                    listItemDepth++;
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
                if (insideTableCell || listItemDepth > 0)
                {
                    // A paragraph break here would otherwise split a <li> or <td>/<th> across
                    // lines — stranding the closing td/th's tab as leading whitespace that
                    // CollapseWhitespace then trims away, or adding a blank line before the next
                    // list item — so it stays silent on both open and close, and a <li>/<td>
                    // reads as one continuous entry regardless of how its content is marked up
                    // internally. Two blocks in a row within that same entry (for example two
                    // <p>s in one <li>) still need some separator so their text does not run
                    // together, so a plain space is inserted on open when one is not already
                    // there — checked here rather than on close, so it is never followed by a
                    // redundant space right before that entry's own closing separator (a list
                    // marker's newline, or a table cell's tab).
                    if (!closing && builder.Length > 0 &&
                        builder[^1] is not (' ' or '\t' or '\n' or PreservedTab or PreservedNewline))
                    {
                        builder.Append(' ');
                    }

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

    private static void TrimTrailingSpace(StringBuilder builder)
    {
        while (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }
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
