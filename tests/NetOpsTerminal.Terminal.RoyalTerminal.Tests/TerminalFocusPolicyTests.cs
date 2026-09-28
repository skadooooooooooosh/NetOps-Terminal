namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class TerminalFocusPolicyTests
{
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void FocusRequiresAttachedVisibleActiveTerminal(
        bool isAttached,
        bool isVisible,
        bool isWindowActive,
        bool hasTerminalControl,
        bool expected)
    {
        Assert.Equal(
            expected,
            TerminalFocusPolicy.CanFocus(
                isAttached,
                isVisible,
                isWindowActive,
                hasTerminalControl));
    }
}
