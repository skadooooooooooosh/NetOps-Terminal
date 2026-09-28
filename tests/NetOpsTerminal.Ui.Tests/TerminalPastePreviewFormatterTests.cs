using NetOpsTerminal.App.Services;

namespace NetOpsTerminal.Ui.Tests;

public sealed class TerminalPastePreviewFormatterTests
{
    [Fact]
    public void FormatPreservesReadableTerminalTextAndLineEndings()
    {
        const string text = "configure terminal\r\n\tinterface Gi1/0/1";

        string preview = TerminalPastePreviewFormatter.Format(text);

        Assert.Equal(text, preview);
    }

    [Fact]
    public void FormatMakesHiddenControlCharactersVisible()
    {
        string preview = TerminalPastePreviewFormatter.Format("show clock\u001B\u007F");

        Assert.Equal("show clock<0x1B><0x7F>", preview);
    }
}
