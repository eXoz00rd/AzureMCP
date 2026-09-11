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
}
