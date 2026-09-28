using RoyalTerminal.Avalonia.Controls;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal interface IRoyalTerminalSession
{
    TerminalControl Control { get; }
}
