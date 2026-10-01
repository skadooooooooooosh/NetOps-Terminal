using Avalonia.Input;

namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class TerminalKeyRoutingPolicyTests
{
    [Theory]
    [InlineData(Key.Back, KeyModifiers.None, false, false, false, null)]
    [InlineData(Key.Back, KeyModifiers.None, false, true, false, "\b")]
    [InlineData(Key.Back, KeyModifiers.None, false, false, true, "\u007f")]
    [InlineData(Key.Back, KeyModifiers.None, true, true, false, null)]
    [InlineData(Key.Back, KeyModifiers.None, true, false, true, null)]
    [InlineData(Key.Back, KeyModifiers.Control, false, true, false, null)]
    [InlineData(Key.Delete, KeyModifiers.None, false, false, true, null)]
    public void BackspaceModeOverridesOnlyUnlockedPlainBackspace(
        Key key, KeyModifiers modifiers, bool locked, bool sendControlH,
        bool sendDelete, string? expected)
    {
        Assert.Equal(expected, TerminalKeyRoutingPolicy.GetBackspaceInput(
            key, modifiers, locked, sendControlH, sendDelete));
    }

    [Theory]
    [InlineData(KeyModifiers.None)]
    [InlineData(KeyModifiers.Shift)]
    public void PaneNavigationKeyIsAlwaysBlockedFromTerminal(
        KeyModifiers modifiers)
    {
        Assert.True(
            TerminalKeyRoutingPolicy.ShouldBlock(
                Key.F6,
                modifiers,
                isInputLocked: false));
        Assert.True(
            TerminalKeyRoutingPolicy.ShouldBlock(
                Key.F6,
                modifiers,
                isInputLocked: true));
    }

    [Fact]
    public void OrdinaryInputIsAllowedOnlyWhenSessionIsUnlocked()
    {
        Assert.False(
            TerminalKeyRoutingPolicy.ShouldBlock(
                Key.A,
                KeyModifiers.None,
                isInputLocked: false));
        Assert.True(
            TerminalKeyRoutingPolicy.ShouldBlock(
                Key.A,
                KeyModifiers.None,
                isInputLocked: true));
    }

    [Theory]
    [InlineData(Key.Tab, KeyModifiers.Control)]
    [InlineData(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.OemPlus, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.N, KeyModifiers.Control)]
    [InlineData(Key.W, KeyModifiers.Control)]
    [InlineData(Key.F, KeyModifiers.Control)]
    [InlineData(Key.OemQuestion, KeyModifiers.Control)]
    [InlineData(Key.R, KeyModifiers.Alt)]
    [InlineData(Key.G, KeyModifiers.Alt)]
    [InlineData(Key.L, KeyModifiers.Alt)]
    [InlineData(Key.X, KeyModifiers.Alt)]
    [InlineData(Key.Right, KeyModifiers.Alt)]
    [InlineData(Key.Down, KeyModifiers.Alt)]
    [InlineData(Key.P, KeyModifiers.Alt)]
    [InlineData(Key.C, KeyModifiers.Alt)]
    public void ApplicationShortcutsAreAlwaysBlockedFromTerminalInput(
        Key key,
        KeyModifiers modifiers)
    {
        Assert.True(
            TerminalKeyRoutingPolicy.ShouldBlock(
                key,
                modifiers,
                isInputLocked: false));
        Assert.True(
            TerminalKeyRoutingPolicy.ShouldBlock(
                key,
                modifiers,
                isInputLocked: true));
    }

    [Theory]
    [InlineData(Key.R, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(Key.W, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(Key.P, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(Key.C, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(Key.E, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(Key.D, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(Key.N, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.M, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.L, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.G, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.D, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.R, KeyModifiers.Control | KeyModifiers.Shift)]
    public void RemovedAliasesAreNotReservedWhenSessionIsUnlocked(
        Key key,
        KeyModifiers modifiers)
    {
        Assert.False(
            TerminalKeyRoutingPolicy.ShouldBlock(
                key,
                modifiers,
                isInputLocked: false));
    }

    [Fact]
    public void LockedSessionStillAllowsCopyShortcut()
    {
        Assert.False(
            TerminalKeyRoutingPolicy.ShouldBlock(
                Key.C,
                KeyModifiers.Control | KeyModifiers.Shift,
                isInputLocked: true));
    }

    [Theory]
    [InlineData(Key.C, KeyModifiers.Control)]
    [InlineData(Key.V, KeyModifiers.Control)]
    [InlineData(Key.Left, KeyModifiers.None)]
    [InlineData(Key.F1, KeyModifiers.None)]
    public void OrdinaryTerminalInputIsNotCapturedWhenUnlocked(
        Key key,
        KeyModifiers modifiers)
    {
        Assert.False(
            TerminalKeyRoutingPolicy.ShouldBlock(
                key,
                modifiers,
                isInputLocked: false));
    }
}
