using ProjectManagement.Services.ConferenceRemarks;

namespace ProjectManagement.Tests;

public sealed class ConferenceDirectionTextFormatterTests
{
    [Fact]
    public void ToDisplayText_PreservesPlainTextLineBreaks()
    {
        const string input = "1. Complete TEC proceedings\r\n2. Submit for approval";

        var result = ConferenceDirectionTextFormatter.ToDisplayText(input);

        Assert.Equal("1. Complete TEC proceedings\n2. Submit for approval", result);
    }

    [Fact]
    public void ToDisplayText_PreservesStructuralHtmlBreaksAndLists()
    {
        const string input = "<p>First line<br>Second line</p><ul><li>Third line</li><li>Fourth line</li></ul>";

        var result = ConferenceDirectionTextFormatter.ToDisplayText(input);

        Assert.Equal("First line\nSecond line\n• Third line\n• Fourth line", result);
    }

    [Fact]
    public void ToDisplayText_RemovesEmbeddedContentAndNormalisesHorizontalWhitespace()
    {
        const string input = "<style>body{display:none}</style><script>alert('x')</script><div>  Safe\t  text </div>";

        var result = ConferenceDirectionTextFormatter.ToDisplayText(input);

        Assert.Equal("Safe text", result);
    }

    [Fact]
    public void ToDisplayText_CollapsesExcessBlankLinesButRetainsParagraphSeparation()
    {
        const string input = "Alpha\n\n\n\nBeta";

        var result = ConferenceDirectionTextFormatter.ToDisplayText(input);

        Assert.Equal("Alpha\n\nBeta", result);
    }
}
