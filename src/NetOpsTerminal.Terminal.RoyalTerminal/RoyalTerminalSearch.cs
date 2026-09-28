using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Avalonia.Controls;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal static class RoyalTerminalSearch
{
    public static TerminalSearchResult Start(TerminalControl control, string? searchText)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.StartSearch(searchText);
        return Snapshot(control);
    }

    public static TerminalSearchResult Next(TerminalControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _ = control.SelectNextSearchMatch();
        return Snapshot(control);
    }

    public static TerminalSearchResult Previous(TerminalControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _ = control.SelectPreviousSearchMatch();
        return Snapshot(control);
    }

    public static TerminalSearchResult Snapshot(TerminalControl control)
    {
        int selected = control.SearchSelectedDisplayIndex;
        return new TerminalSearchResult(
            control.SearchTotal,
            selected < 0 ? 0 : selected + 1);
    }
}
