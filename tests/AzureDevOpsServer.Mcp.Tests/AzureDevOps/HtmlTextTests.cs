using AzureDevOpsServer.Mcp.AzureDevOps;
using Xunit;

namespace AzureDevOpsServer.Mcp.Tests.AzureDevOps;

public sealed class HtmlTextTests
{
    [Theory]
    [InlineData("<p>Plain paragraph.</p>")]
    [InlineData("Has a <br> line break")]
    [InlineData("<div><b>bold</b></div>")]
    public void LooksLikeHtml_WithKnownTag_ReturnsTrue(string value)
    {
        Assert.True(HtmlText.LooksLikeHtml(value));
    }

    [Theory]
    [InlineData("List<Item> without a real tag")]
    [InlineData("if (x < 5 and y > 3)")]
    [InlineData("Plain text with no markup")]
    public void LooksLikeHtml_WithoutKnownTag_ReturnsFalse(string value)
    {
        Assert.False(HtmlText.LooksLikeHtml(value));
    }

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
    public void ToPlainText_WithUnterminatedTag_KeepsRemainingTextVerbatim()
    {
        var text = HtmlText.ToPlainText("<p>Before <b unterminated");

        Assert.Equal("Before <b unterminated", text);
    }

    [Fact]
    public void LooksLikeHtml_WithAngleBracketInsideQuotedAttribute_StillDetectsTheTag()
    {
        Assert.True(HtmlText.LooksLikeHtml("""<br title="1 > 0">After"""));
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
    public void ToPlainText_SeparatesTableCellsInsteadOfConcatenatingThem()
    {
        var text = HtmlText.ToPlainText("<table><tr><td>Owner</td><td>Value</td></tr></table>");

        Assert.Equal("Owner\tValue", text);
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
    public void ToPlainText_WithoutAnyTags_StillDecodesEntities()
    {
        var text = HtmlText.ToPlainText("Fish &amp; Chips");

        Assert.Equal("Fish & Chips", text);
    }

    [Fact]
    public void ToPlainText_PreservesIndentationInsidePreBlock()
    {
        var text = HtmlText.ToPlainText("<pre>\n    first line\n    second line\n</pre>");

        Assert.Equal("\n    first line\n    second line\n", text);
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

        Assert.Equal("code\nAfter", text);
    }

    [Fact]
    public void ToPlainText_SeparatesPrecedingInlineContentFromCodeBlock()
    {
        var text = HtmlText.ToPlainText("<b>Note:</b><pre>code</pre>");

        Assert.Equal("Note:\ncode", text);
    }

    [Fact]
    public void ToPlainText_WithCustomElementName_DoesNotMisreadItAsAKnownTag()
    {
        var text = HtmlText.ToPlainText("<p-custom>Note</p-custom>");

        Assert.Equal("<p-custom>Note</p-custom>", text);
    }

    [Fact]
    public void LooksLikeHtml_WithCustomElementName_ReturnsFalse()
    {
        Assert.False(HtmlText.LooksLikeHtml("<p-custom>Note</p-custom>"));
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
    public void ToPlainText_WithBlockTagInsideTableCell_StillSeparatesCells()
    {
        var text = HtmlText.ToPlainText("<table><tr><td><p>Owner</p></td><td>Value</td></tr></table>");

        Assert.Equal("Owner\tValue", text);
    }

    [Fact]
    public void ToPlainText_SeparatesListFromFollowingContent()
    {
        var text = HtmlText.ToPlainText("<ul><li>One</li></ul><p>After</p>");

        Assert.Equal("- One\nAfter", text);
    }
}
