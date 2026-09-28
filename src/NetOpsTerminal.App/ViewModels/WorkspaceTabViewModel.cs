using CommunityToolkit.Mvvm.ComponentModel;
using NetOpsTerminal.Application.Workspaces;

namespace NetOpsTerminal.App.ViewModels;

public sealed class WorkspaceTabViewModel : ObservableObject
{
    private WorkspacePaneNodeViewModel _root;
    private TerminalPaneViewModel _activePane;

    public WorkspaceTabViewModel(
        Guid id,
        WorkspacePaneNodeViewModel root,
        TerminalPaneViewModel activePane)
    {
        Id = id == Guid.Empty
            ? throw new ArgumentException("A workspace tab ID is required.", nameof(id))
            : id;
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _activePane = activePane ?? throw new ArgumentNullException(nameof(activePane));
        if (!root.EnumerateLeaves().Contains(activePane))
        {
            throw new ArgumentException("The active pane must belong to the workspace tab.", nameof(activePane));
        }

        _activePane.IsActive = true;
    }

    public Guid Id { get; }

    public WorkspacePaneNodeViewModel Root
    {
        get => _root;
        private set => SetProperty(ref _root, value);
    }

    public TerminalPaneViewModel ActivePane
    {
        get => _activePane;
        private set
        {
            if (ReferenceEquals(_activePane, value))
            {
                return;
            }

            _activePane.IsActive = false;
            _activePane = value;
            _activePane.IsActive = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
        }
    }

    public string Title => ActivePane.Title;

    public IReadOnlyList<TerminalPaneViewModel> Panes => Root.EnumerateLeaves().ToArray();

    public void Activate(TerminalPaneViewModel pane)
    {
        EnsureContains(pane);
        ActivePane = pane;
    }

    public TerminalPaneViewModel Split(
        TerminalPaneViewModel pane,
        PaneSplitDirection direction)
    {
        EnsureContains(pane);
        var newPane = new TerminalPaneViewModel(Guid.NewGuid());
        SplitPaneViewModel? oldParent = pane.Parent;
        var split = new SplitPaneViewModel(Guid.NewGuid(), direction, 0.5, pane, newPane);
        if (oldParent is null)
        {
            Root = split;
        }
        else
        {
            oldParent.ReplaceChild(pane, split);
        }

        pane.Parent = split;
        newPane.Parent = split;
        split.Parent = oldParent;
        ActivePane = newPane;
        NotifyTreeChanged();
        return newPane;
    }

    public bool Remove(TerminalPaneViewModel pane)
    {
        EnsureContains(pane);
        SplitPaneViewModel? parent = pane.Parent;
        if (parent is null)
        {
            return false;
        }

        WorkspacePaneNodeViewModel survivor = ReferenceEquals(parent.First, pane)
            ? parent.Second
            : parent.First;
        SplitPaneViewModel? grandParent = parent.Parent;
        if (grandParent is null)
        {
            survivor.Parent = null;
            Root = survivor;
        }
        else
        {
            grandParent.ReplaceChild(parent, survivor);
        }

        ActivePane = survivor.EnumerateLeaves().First();
        NotifyTreeChanged();
        return true;
    }

    public void FocusNext(bool reverse)
    {
        IReadOnlyList<TerminalPaneViewModel> panes = Panes;
        if (panes.Count < 2)
        {
            return;
        }

        int current = panes.IndexOf(ActivePane);
        int offset = reverse ? -1 : 1;
        ActivePane = panes[(current + offset + panes.Count) % panes.Count];
    }

    public WorkspaceTabDocument ToDocument()
    {
        return new WorkspaceTabDocument
        {
            TabId = Id,
            ActivePaneId = ActivePane.Id,
            Root = Root.ToDocument(),
        };
    }

    public static WorkspaceTabViewModel FromDocument(WorkspaceTabDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        WorkspacePaneNodeViewModel root = CreateNode(document.Root);
        TerminalPaneViewModel active = root
            .EnumerateLeaves()
            .First(pane => pane.Id == document.ActivePaneId);
        return new WorkspaceTabViewModel(document.TabId, root, active);
    }

    public void NotifyActiveContentChanged()
    {
        OnPropertyChanged(nameof(Title));
    }

    private static WorkspacePaneNodeViewModel CreateNode(PaneNodeDocument document)
    {
        if (!document.IsSplit)
        {
            return new TerminalPaneViewModel(document.NodeId, document.Launch);
        }

        return new SplitPaneViewModel(
            document.NodeId,
            document.SplitDirection,
            document.SplitRatio,
            CreateNode(document.First!),
            CreateNode(document.Second!));
    }

    private void EnsureContains(TerminalPaneViewModel pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (!Root.EnumerateLeaves().Contains(pane))
        {
            throw new InvalidOperationException("The pane does not belong to this workspace tab.");
        }
    }

    private void NotifyTreeChanged()
    {
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(Panes));
        OnPropertyChanged(nameof(Title));
    }
}

internal static class WorkspacePaneListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> source, T value)
    {
        for (int index = 0; index < source.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(source[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
