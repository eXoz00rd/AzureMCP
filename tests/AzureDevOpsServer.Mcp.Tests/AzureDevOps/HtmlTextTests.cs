using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class HtmlTextTests
{
    [Fact]
    public void ToPlainText_StripsNestedTagsAndKeepsInnerText()
    {
        var text = HtmlText.ToPlainText("<p>Text with <b>bold</b> and <i>italic</i>.</p>");

        Assert.Equal("Text with bold and italic.", text);
    }

    [Fact]
    public void ToPlainText_ConvertsListItemsToBullets()
    {
        var text = HtmlText.ToPlainText("<ul><li>First</li><li>Second</li></ul>");

        Assert.Equal("- First\n- Second", text);
    }

    [Fact]
    public void ToPlainText_ConvertsOrderedListItemsToNumbers()
    {
        var text = HtmlText.ToPlainText("<ol><li>First</li><li>Second</li></ol>");

        Assert.Equal("1. First\n2. Second", text);
    }

    [Fact]
    public void ToPlainText_WithBlockElementWrappingListItemText_KeepsTheMarkerWithItsText()
    {
        var text = HtmlText.ToPlainText("<li><p>One</p></li>");

        Assert.Equal("- One", text);
    }

    [Fact]
    public void ToPlainText_WithBlockElementWrappingMultipleListItems_KeepsItemsAdjacent()
    {
        var text = HtmlText.ToPlainText("<ul><li><p>One</p></li><li><p>Two</p></li></ul>");

        Assert.Equal("- One\n- Two", text);
    }

    [Fact]
    public void ToPlainText_WithMultipleBlocksInOneListItem_SeparatesThem()
    {
        var text = HtmlText.ToPlainText("<li><p>One</p><p>Two</p></li>");

        Assert.Equal("- One Two", text);
    }

    [Fact]
    public void ToPlainText_WithMultipleBlocksInOneTableCell_SeparatesThem()
    {
        var text = HtmlText.ToPlainText("<table><tr><td><p>One</p><p>Two</p></td><td>Value</td></tr></table>");

        Assert.Equal("One Two\tValue", text);
    }

    [Fact]
    public void ToPlainText_WithNestedList_DoesNotAddBlankLineBeforeNextSibling()
    {
        var text = HtmlText.ToPlainText("<ul><li>Parent<ul><li>Child</li></ul></li><li>Next</li></ul>");

        Assert.Equal("- Parent\n- Child\n- Next", text);
    }

    [Fact]
    public void ToPlainText_WithLiteralSentinelCharacterInContent_ReplacesItInsteadOfMisreadingItAsAMarker()
    {
        var strayMarker = (char)0xE000;
        var replacementCharacter = (char)0xFFFD;

        var text = HtmlText.ToPlainText($"<p>Weird{strayMarker}char</p>");

        Assert.Equal($"Weird{replacementCharacter}char", text);
    }

    [Fact]
    public void ToPlainText_WithEntityEncodedSentinelCharacter_ReplacesItInsteadOfMisreadingItAsAMarker()
    {
        var replacementCharacter = (char)0xFFFD;

        var text = HtmlText.ToPlainText("<p>Weird&#xE000;char</p>");

        Assert.Equal($"Weird{replacementCharacter}char", text);
    }

    [Fact]
    public void ToPlainText_WithInlineCode_KeepsItInTheSurroundingSentence()
    {
        var text = HtmlText.ToPlainText("<p>Run <code>foo()</code> now</p>");

        Assert.Equal("Run foo() now", text);
    }

    [Fact]
    public void ToPlainText_StripsStrikethroughTag()
    {
        var text = HtmlText.ToPlainText("<p><s>strike</s></p>");

        Assert.Equal("strike", text);
    }

    [Fact]
    public void ToPlainText_WithEmptyLeadingTableCell_KeepsTheSeparatorForTheSecondCell()
    {
        var text = HtmlText.ToPlainText("<tr><td></td><td>Value</td></tr>");

        Assert.Equal("\tValue", text);
    }

    [Fact]
    public void ToPlainText_ConvertsLinkToTextWithUrl()
    {
        var text = HtmlText.ToPlainText("""<p>See <a href="https://example.com/doc">the doc</a> for details.</p>""");

        Assert.Equal("See the doc (https://example.com/doc) for details.", text);
    }

    [Fact]
    public void ToPlainText_DecodesEntities()
    {
        var text = HtmlText.ToPlainText("<p>Fish &amp; Chips &lt;tasty&gt;</p>");

        Assert.Equal("Fish & Chips <tasty>", text);
    }

    [Fact]
    public void ToPlainText_ConvertsBreaksAndParagraphsToLines()
    {
        var text = HtmlText.ToPlainText("<p>First line<br>Second line</p><p>New paragraph</p>");

        Assert.Equal("First line\nSecond line\n\nNew paragraph", text);
    }

    [Fact]
    public void ToPlainText_WithAttributeContainingAngleBracket_DoesNotEndTagEarly()
    {
        var text = HtmlText.ToPlainText("""<p title="1 > 0">Kept</p>""");

        Assert.Equal("Kept", text);
    }

    [Fact]
    public void ToPlainText_WithUnterminatedTagFollowedByUnrelatedClosingTag_DoesNotConsumeTheLaterTag()
    {
        var text = HtmlText.ToPlainText("<b unterminated</p>");

        Assert.Equal("<b unterminated</p>", text);
    }

    [Fact]
    public void ToPlainText_WithNewlineInsideATextNode_CollapsesItToASingleSpace()
    {
        var text = HtmlText.ToPlainText("<p>one\n  two</p>");

        Assert.Equal("one two", text);
    }

    [Fact]
    public void ToPlainText_WithMultipleSpacesAndNoNewline_CollapsesToASingleSpace()
    {
        var text = HtmlText.ToPlainText("<p>one    two</p>");

        Assert.Equal("one two", text);
    }

    [Fact]
    public void ToPlainText_WithUnterminatedTag_KeepsRemainingTextVerbatim()
    {
        var text = HtmlText.ToPlainText("<p>Before <b unterminated");

        Assert.Equal("Before <b unterminated", text);
    }

    [Fact]
    public void ToPlainText_WithDataHrefAttribute_DoesNotFabricateLinkUrl()
    {
        var text = HtmlText.ToPlainText("""<p>See <a data-href="https://tracking.example.com">the doc</a>.</p>""");

        Assert.Equal("See the doc.", text);
    }

    [Fact]
    public void ToPlainText_WithDataHrefBeforeRealHref_UsesTheRealHrefValue()
    {
        var text = HtmlText.ToPlainText(
            """<p><a data-href="https://tracking.example.com" href="https://example.com/doc">the doc</a></p>"""
        );

        Assert.Equal("the doc (https://example.com/doc)", text);
    }

    [Fact]
    public void ToPlainText_WithHrefLookingTextInsideAnotherAttribute_UsesTheRealHrefValue()
    {
        var text = HtmlText.ToPlainText(
            """<a title="tooltip href='fake'" href="https://example.com">the doc</a>"""
        );

        Assert.Equal("the doc (https://example.com)", text);
    }

    [Fact]
    public void ToPlainText_SeparatesTableCellsInsteadOfConcatenatingThem()
    {
        var text = HtmlText.ToPlainText("<table><tr><td>Owner</td><td>Value</td></tr></table>");

        Assert.Equal("Owner\tValue", text);
    }

    [Fact]
    public void ToPlainText_WithPrettyPrintedWhitespaceBetweenTableCells_DoesNotLeaveASpaceBeforeTheSeparator()
    {
        var text = HtmlText.ToPlainText("<table><tr>\n  <td>A</td>\n  <td>B</td>\n</tr></table>");

        Assert.Equal("A\tB", text);
    }

    [Fact]
    public void ToPlainText_WithLiteralTabBetweenTableCells_DoesNotProduceADoubleSeparator()
    {
        var text = HtmlText.ToPlainText("<table><tr><td>A</td>\t<td>B</td></tr></table>");

        Assert.Equal("A\tB", text);
    }

    [Fact]
    public void ToPlainText_SeparatesMultipleTableRows()
    {
        var text = HtmlText.ToPlainText(
            "<table><tr><td>A</td><td>B</td></tr><tr><td>C</td><td>D</td></tr></table>"
        );

        Assert.Equal("A\tB\nC\tD", text);
    }

    [Fact]
    public void ToPlainText_WithBlockTagInsideTableCell_StillSeparatesCells()
    {
        var text = HtmlText.ToPlainText("<table><tr><td><p>Owner</p></td><td>Value</td></tr></table>");

        Assert.Equal("Owner\tValue", text);
    }

    [Fact]
    public void ToPlainText_WithComparisonTextInsideRealHtml_PreservesTheComparisonText()
    {
        var text = HtmlText.ToPlainText("<p>if (x &lt; 5 and y &gt; 3)</p>");

        Assert.Equal("if (x < 5 and y > 3)", text);
    }

    [Fact]
    public void ToPlainText_WithUnescapedAngleBracketsInsideRealHtml_TreatsThemAsLiteralText()
    {
        var text = HtmlText.ToPlainText("<p>if (x < 5 and y > 3)</p>");

        Assert.Equal("if (x < 5 and y > 3)", text);
    }

    [Fact]
    public void ToPlainText_WithGenericTypeTextInsideRealHtml_KeepsTheGenericTypeText()
    {
        var text = HtmlText.ToPlainText("<p>Returns a <b>List<Item></b> result.</p>");

        Assert.Equal("Returns a List<Item> result.", text);
    }

    [Fact]
    public void ToPlainText_WithCustomElementName_DoesNotMisreadItAsAKnownTag()
    {
        var text = HtmlText.ToPlainText("<p-custom>Note</p-custom>");

        Assert.Equal("<p-custom>Note</p-custom>", text);
    }

    [Fact]
    public void ToPlainText_WithKnownTagInsideUnknownTagsQuotedAttribute_DoesNotStripIt()
    {
        var text = HtmlText.ToPlainText("""<custom data="<b>">x</custom>""");

        Assert.Equal("""<custom data="<b>">x</custom>""", text);
    }

    [Fact]
    public void ToPlainText_WithoutAnyTags_StillDecodesEntities()
    {
        var text = HtmlText.ToPlainText("Fish &amp; Chips");

        Assert.Equal("Fish & Chips", text);
    }

    [Fact]
    public void ToPlainText_PreservesIndentationInsidePreBlock()
    {
        var text = HtmlText.ToPlainText("<pre>\n    first line\n    second line\n</pre>");

        Assert.Equal("\n\n    first line\n    second line\n\n", text);
    }

    [Fact]
    public void ToPlainText_PreservesLeadingSpacesInsidePreBlockWithNoNewline()
    {
        var text = HtmlText.ToPlainText("<pre>    code</pre>");

        Assert.Equal("\n    code\n", text);
    }

    [Fact]
    public void ToPlainText_PreservesIndentationInsideCodeBlockSurroundedByParagraphs()
    {
        var text = HtmlText.ToPlainText(
            "<p>Before</p><pre>\n    indented\n</pre><p>After</p>"
        );

        Assert.Contains("\n    indented\n", text);
        Assert.StartsWith("Before", text);
        Assert.EndsWith("After", text);
    }

    [Fact]
    public void ToPlainText_SeparatesCodeBlockFromFollowingContent()
    {
        var text = HtmlText.ToPlainText("<pre>code</pre><p>After</p>");

        Assert.Equal("\ncode\nAfter", text);
    }

    [Fact]
    public void ToPlainText_SeparatesPrecedingInlineContentFromCodeBlock()
    {
        var text = HtmlText.ToPlainText("<b>Note:</b><pre>code</pre>");

        Assert.Equal("Note:\ncode\n", text);
    }

    [Fact]
    public void ToPlainText_SeparatesListFromFollowingContent()
    {
        var text = HtmlText.ToPlainText("<ul><li>One</li></ul><p>After</p>");

        Assert.Equal("- One\nAfter", text);
    }

    [Fact]
    public void ToPlainText_SeparatesInlineContentFromFollowingBlock()
    {
        var text = HtmlText.ToPlainText("<b>Note:</b><p>Block</p>");

        Assert.Equal("Note:\n\nBlock", text);
    }

    [Fact]
    public void ToPlainText_WithPrettyPrintedWhitespaceBetweenListItems_ProducesTheSameResultAsMinifiedSource()
    {
        var text = HtmlText.ToPlainText("<ul>\n<li>One</li>\n<li>Two</li>\n</ul>");

        Assert.Equal("- One\n- Two", text);
    }

    [Fact]
    public void ToPlainText_WithNewlineBetweenInlineElements_CollapsesToASingleSpace()
    {
        var text = HtmlText.ToPlainText("<span>one</span>\n<span>two</span>");

        Assert.Equal("one two", text);
    }

    [Fact]
    public void ToPlainText_WithManyUnterminatedTagLikeFragments_ReturnsInputVerbatim()
    {
        var malformed = string.Concat(Enumerable.Repeat("<a", 5000));

        var text = HtmlText.ToPlainText(malformed);

        Assert.Equal(malformed, text);
    }
}
