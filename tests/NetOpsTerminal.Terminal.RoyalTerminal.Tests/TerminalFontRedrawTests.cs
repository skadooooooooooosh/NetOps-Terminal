namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class TerminalFontRedrawTests
{
    [Fact]
    public void ApplyChangesFontThenInvalidatesTextAndTerminal()
    {
        List<string> calls = [];

        bool applied = TerminalFontRedraw.Apply(
            currentFontSize: 14,
            fontSize: 18,
            value => calls.Add($"font:{value}"),
            () => calls.Add("text"),
            () => calls.Add("terminal"));

        Assert.True(applied);
        Assert.Equal(["font:18", "text", "terminal"], calls);
    }

    [Fact]
    public void ApplySkipsRedrawWhenFontSizeIsUnchanged()
    {
        List<string> calls = [];

        bool applied = TerminalFontRedraw.Apply(
            currentFontSize: 18,
            fontSize: 18,
            value => calls.Add($"font:{value}"),
            () => calls.Add("text"),
            () => calls.Add("terminal"));

        Assert.False(applied);
        Assert.Empty(calls);
    }
}
