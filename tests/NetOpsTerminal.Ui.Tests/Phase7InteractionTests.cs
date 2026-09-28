using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Core.Sessions;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Ui.Tests;

public sealed class Phase7InteractionTests
{
    [Fact]
    public void TerminalFindTracksQueryNavigationAndClose()
    {
        var session = new SearchSession();
        using var viewModel = new TerminalTabViewModel(session, _ => Task.CompletedTask);

        viewModel.OpenFind();
        viewModel.FindText = "error";
        viewModel.FindNext();

        Assert.True(viewModel.IsFindVisible);
        Assert.Equal("2/3", viewModel.FindStatusText);
        viewModel.CloseFind();
        Assert.False(viewModel.IsFindVisible);
        Assert.True(session.EndSearchCalled);
    }

    private sealed class SearchSession : ITerminalSession
    {
        private int _selected = 1;
        public bool EndSearchCalled { get; private set; }
        public Guid Id { get; } = Guid.NewGuid();
        public string DisplayName => "Search";
        public ConnectionType ConnectionType => ConnectionType.LocalShell;
        public string ConnectionLabel => "LOCAL";
        public LocalShellKind? ShellKind => LocalShellKind.PowerShell;
        public SessionState State => SessionState.Connected;
        public int FontSize => 14;
        public bool IsInputLocked => false;
        public event EventHandler<SessionState>? StateChanged { add { } remove { } }
        public event EventHandler<Exception>? ErrorOccurred { add { } remove { } }
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetFontSize(int fontSize) { }
        public void SetInputLocked(bool isLocked) { }
        public TerminalSearchResult StartSearch(string? searchText) =>
            string.IsNullOrWhiteSpace(searchText) ? TerminalSearchResult.Empty : new(3, _selected);
        public TerminalSearchResult SelectNextSearchMatch() => new(3, ++_selected);
        public TerminalSearchResult SelectPreviousSearchMatch() => new(3, --_selected);
        public void EndSearch() => EndSearchCalled = true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
