using NetOpsTerminal.App.Input;

namespace NetOpsTerminal.Ui.Tests;

public sealed class TerminalZoomGestureTests
{
    [Theory]
    [InlineData(true, 1, 1)]
    [InlineData(true, -1, -1)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 1, 0)]
    [InlineData(false, -1, 0)]
    public void GetIncrementRequiresControlAndUsesVerticalDirection(
        bool controlPressed,
        double verticalDelta,
        int expected)
    {
        int actual = TerminalZoomGesture.GetIncrement(controlPressed, verticalDelta);

        Assert.Equal(expected, actual);
    }
}
