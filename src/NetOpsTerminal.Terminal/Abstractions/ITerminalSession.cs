using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Sessions;

namespace NetOpsTerminal.Terminal.Abstractions;

public interface ITerminalSession : IAsyncDisposable
{
    Guid Id { get; }

    string DisplayName { get; }

    ConnectionType ConnectionType { get; }

    string ConnectionLabel { get; }

    LocalShellKind? ShellKind { get; }

    SessionState State { get; }

    int FontSize { get; }

    bool IsInputLocked { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task ReconnectAsync(CancellationToken cancellationToken);

    Task SendTextAsync(string text, CancellationToken cancellationToken);

    Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken);

    void SetFontSize(int fontSize);

    void SetInputLocked(bool isLocked);

    TerminalSearchResult StartSearch(string? searchText) => TerminalSearchResult.Empty;

    TerminalSearchResult SelectNextSearchMatch() => TerminalSearchResult.Empty;

    TerminalSearchResult SelectPreviousSearchMatch() => TerminalSearchResult.Empty;

    void EndSearch()
    {
    }

    event EventHandler<SessionState>? StateChanged;

    event EventHandler<Exception>? ErrorOccurred;

    event EventHandler<TerminalOutputReceivedEventArgs>? OutputReceived
    {
        add { }
        remove { }
    }
}
