using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.App.ViewModels;

public sealed partial class DeviceExplorerViewModel : ObservableObject
{
    private readonly IDeviceCatalogService _catalogService;
    private DeviceCatalogSnapshot _snapshot = new([], [], [], [], []);
    private DeviceExplorerNodeViewModel? _selectedNode;
    private readonly Dictionary<(DeviceExplorerNodeKind Kind, Guid? Id), bool> _expandedNodes = [];
    private IReadOnlyList<DeviceExplorerNodeViewModel> _selectedNodes = [];
    private DeviceCatalogFilter _filter;
    private string _searchText = string.Empty;
    private bool _isLoading;
    private string? _errorMessage;

    public DeviceExplorerViewModel(IDeviceCatalogService catalogService)
    {
        _catalogService = catalogService;
        ShowAllCommand = new AsyncRelayCommand(() => SetFilterAsync(DeviceCatalogFilter.All));
        ShowFavoritesCommand = new AsyncRelayCommand(() => SetFilterAsync(DeviceCatalogFilter.Favorites));
        ShowRecentCommand = new AsyncRelayCommand(() => SetFilterAsync(DeviceCatalogFilter.Recent));
    }

    public ObservableCollection<DeviceExplorerNodeViewModel> Nodes { get; } = [];

    public IAsyncRelayCommand ShowAllCommand { get; }

    public IAsyncRelayCommand ShowFavoritesCommand { get; }

    public IAsyncRelayCommand ShowRecentCommand { get; }

    public DeviceExplorerNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                OnPropertyChanged(nameof(SelectedDevice));
                OnPropertyChanged(nameof(CanCreateFolder));
                OnPropertyChanged(nameof(CanSortSelectedAlphabetically));
                OnPropertyChanged(nameof(HasSelectedDevice));
                OnPropertyChanged(nameof(HasSelectedDevices));
                OnPropertyChanged(nameof(CanOpenSelectedTerminal));
                OnPropertyChanged(nameof(CanOpenSelectedWeb));
                OnPropertyChanged(nameof(SelectedOpenLabel));
            }
        }
    }

    public DeviceProfile? SelectedDevice => SelectedNode?.Device;

    public void SetSelectedNodes(IReadOnlyList<DeviceExplorerNodeViewModel> selectedNodes)
    {
        _selectedNodes = selectedNodes;
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(HasSelectedDevices));
    }

    public IReadOnlyList<Guid> GetSelectedDeviceIds()
    {
        IEnumerable<DeviceExplorerNodeViewModel> selectedNodes = _selectedNodes.Count > 0
            ? _selectedNodes
            : SelectedNode is { } selectedNode ? [selectedNode] : [];
        var ids = new HashSet<Guid>();
        foreach (DeviceExplorerNodeViewModel node in selectedNodes)
        {
            CollectDeviceIds(node, ids);
        }

        return ids.ToArray();
    }

    public bool HasSelectedDevice => SelectedDevice is not null && GetSelectedDeviceIds().Count == 1;

    public bool HasSelectedDevices => GetSelectedDeviceIds().Count > 0;

    public bool CanOpenSelectedTerminal => HasSelectedDevice
        && SelectedDevice?.AccessMode != DeviceAccessMode.WebOnly;

    public bool CanOpenSelectedWeb => HasSelectedDevice
        && SelectedDevice?.WebAccess is not null;

    public string SelectedOpenLabel => SelectedDevice?.WebAccess?.DefaultAction == DeviceOpenAction.Web
        ? "Open web GUI" : "Connect";

    public IReadOnlyList<CredentialProfile> Credentials => _snapshot.Credentials;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => ErrorMessage is not null;

    public int AllCount => _snapshot.AllDeviceCount ?? _snapshot.Devices.Count;

    public int FavoriteCount => _snapshot.FavoriteDeviceCount
        ?? _snapshot.Devices.Count(device => device.IsFavorite);

    public int RecentCount => _snapshot.RecentDeviceCount
        ?? _snapshot.RecentDeviceIds.Count;

    public bool IsAllSelected => _filter == DeviceCatalogFilter.All;

    public bool CanOrganize => IsAllSelected && string.IsNullOrWhiteSpace(_searchText);

    public bool CanCreateFolder => CanOrganize && SelectedNode?.SiteId is not null;

    public bool CanSortSelectedAlphabetically => CanOrganize
        && SelectedNode is { Kind: DeviceExplorerNodeKind.Site or DeviceExplorerNodeKind.Folder }
        && SelectedNode.Children.Count > 1;

    public string GetFolderLocation(DeviceExplorerNodeViewModel? node)
    {
        return node?.SiteId is { } siteId
            ? ResolveSiteName(siteId)
            : "Select a site";
    }

    public bool IsFavoritesSelected => _filter == DeviceCatalogFilter.Favorites;

    public bool IsRecentSelected => _filter == DeviceCatalogFilter.Recent;

    public string FilterLabel => _filter switch
    {
        DeviceCatalogFilter.Favorites => "FAVORITES",
        DeviceCatalogFilter.Recent => "RECENT DEVICES",
        _ => "SITES AND GROUPS",
    };

    public string EmptyMessage => _snapshot.Credentials.Count == 0
        ? "Add a saved device. SSH devices require a credential profile."
        : _filter switch
        {
            DeviceCatalogFilter.Favorites => "No favorite devices match this view.",
            DeviceCatalogFilter.Recent => "No saved-device sessions have been opened yet.",
            _ => "No saved devices match this view.",
        };

    public bool HasNoNodes => Nodes.Count == 0 && !IsLoading;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return RefreshAsync(null, cancellationToken);
    }

    public async Task RefreshAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        if (searchText is not null)
        {
            _searchText = searchText;
            OnPropertyChanged(nameof(CanOrganize));
            OnPropertyChanged(nameof(CanCreateFolder));
            OnPropertyChanged(nameof(CanSortSelectedAlphabetically));
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _snapshot = await _catalogService
                .LoadAsync(_searchText, _filter, cancellationToken);
            BuildNodes();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Device inventory could not be loaded ({exception.GetType().Name}).";
            Nodes.Clear();
        }
        finally
        {
            IsLoading = false;
            NotifyCollectionState();
        }
    }

    public async Task SaveAsync(
        DeviceEditRequest request,
        CancellationToken cancellationToken = default)
    {
        await _catalogService.SaveAsync(request, cancellationToken);
        await RefreshAsync(null, cancellationToken);
    }

    public async Task DeleteDevicesAsync(
        IReadOnlyList<Guid> deviceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        foreach (Guid id in deviceIds.Distinct())
        {
            await _catalogService.DeleteAsync(id, cancellationToken);
        }

        _selectedNodes = [];
        SelectedNode = null;
        await RefreshAsync(null, cancellationToken);
    }

    public Task DeleteSelectedAsync(CancellationToken cancellationToken = default)
        => DeleteDevicesAsync(GetSelectedDeviceIds(), cancellationToken);

    public DeviceEditRequest CreateEditRequest(DeviceProfile device)
    {
        ArgumentNullException.ThrowIfNull(device);
        string siteName = device.SiteId is { } siteId
            ? _snapshot.Sites.FirstOrDefault(site => site.Id == siteId)?.Name ?? string.Empty
            : string.Empty;
        string groupName = GetFolderPath(device.GroupId);
        return new DeviceEditRequest(
            device.Id,
            device.Name,
            device.Description,
            device.Connection.Host,
            device.Connection.Port,
            device.DeviceType,
            device.Vendor,
            device.Model,
            siteName,
            groupName,
            device.Environment,
            device.CredentialProfileId,
            device.UsernameOverride,
            device.IsFavorite,
            device.Notes)
        {
            ConnectionType = device.ConnectionType,
            AccessMode = device.AccessMode,
            WebAccess = device.WebAccess,
            TerminalType = device.Connection.TerminalType,
            Encoding = device.Connection.Encoding,
            ConnectTimeoutSeconds = device.Connection.ConnectTimeoutSeconds,
            SerialDataBits = device.Connection.SerialDataBits,
            SerialParity = device.Connection.SerialParity,
            SerialStopBits = device.Connection.SerialStopBits,
            SerialFlowControl = device.Connection.SerialFlowControl,
            RawLineEnding = device.Connection.RawLineEnding,
            TelnetAcknowledged = device.Connection.TelnetAcknowledged,
            BackspaceSendsControlH = device.Connection.BackspaceSendsControlH,
            BackspaceSendsDelete = device.Connection.BackspaceSendsDelete,
        };
    }

    private async Task SetFilterAsync(DeviceCatalogFilter filter)
    {
        _filter = filter;
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(CanOrganize));
        OnPropertyChanged(nameof(CanCreateFolder));
        OnPropertyChanged(nameof(CanSortSelectedAlphabetically));
        OnPropertyChanged(nameof(IsFavoritesSelected));
        OnPropertyChanged(nameof(IsRecentSelected));
        OnPropertyChanged(nameof(FilterLabel));
        await RefreshAsync();
    }

    private void BuildNodes()
    {
        Guid? selectedId = SelectedDevice?.Id;
        RememberExpansion(Nodes);
        _selectedNodes = [];
        Nodes.Clear();
        if (_filter != DeviceCatalogFilter.All || !string.IsNullOrWhiteSpace(_searchText))
        {
            foreach (DeviceProfile device in _snapshot.Devices)
                Nodes.Add(CreateDeviceNode(device));
        }
        else
        {
            foreach (Site site in _snapshot.Sites)
            {
                DeviceExplorerNodeViewModel[] children = BuildFolderChildren(site.Id, null);
                Nodes.Add(new DeviceExplorerNodeViewModel(
                    site.Name, $"{CountDevices(children)} devices", string.Empty,
                    children: children, siteId: site.Id, isSite: true)
                {
                    IsExpanded = WasExpanded(DeviceExplorerNodeKind.Site, site.Id),
                });
            }

            DeviceExplorerNodeViewModel[] unassigned = BuildFolderChildren(null, null);
            DeviceExplorerNodeViewModel[] orderedSites = SortNodes(Nodes).ToArray();
            Nodes.Clear();
            foreach (DeviceExplorerNodeViewModel site in orderedSites)
                Nodes.Add(site);
            Nodes.Add(new DeviceExplorerNodeViewModel(
                "Unassigned", $"{CountDevices(unassigned)} devices", string.Empty,
                children: unassigned, isSite: true)
            {
                IsExpanded = WasExpanded(DeviceExplorerNodeKind.Site, null),
            });
        }

        SelectedNode = FindDeviceNode(Nodes, selectedId);
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(HasSelectedDevices));
        NotifyCollectionState();
    }

    private DeviceExplorerNodeViewModel[] BuildFolderChildren(Guid? siteId, Guid? parentFolderId)
    {
        IEnumerable<DeviceExplorerNodeViewModel> folders = _snapshot.Groups
            .Where(group => group.SiteId == siteId && group.ParentGroupId == parentFolderId)
            .Select(group =>
            {
                DeviceExplorerNodeViewModel[] children = BuildFolderChildren(siteId, group.Id);
                return new DeviceExplorerNodeViewModel(
                    group.Name, $"{CountDevices(children)} devices", string.Empty,
                    children: children, siteId: siteId, folderId: group.Id,
                    parentFolderId: parentFolderId)
                {
                    IsExpanded = WasExpanded(DeviceExplorerNodeKind.Folder, group.Id),
                };
            });
        IEnumerable<DeviceExplorerNodeViewModel> devices = _snapshot.Devices
            .Where(device => device.SiteId == siteId && device.GroupId == parentFolderId)
            .Select(CreateDeviceNode);
        return SortNodes(folders.Concat(devices)).ToArray();
    }

    private void RememberExpansion(IEnumerable<DeviceExplorerNodeViewModel> nodes)
    {
        foreach (DeviceExplorerNodeViewModel node in nodes)
        {
            if (node.Kind != DeviceExplorerNodeKind.Device)
                _expandedNodes[(node.Kind, node.OrderId)] = node.IsExpanded;
            RememberExpansion(node.Children);
        }
    }

    private bool WasExpanded(DeviceExplorerNodeKind kind, Guid? id)
        => _expandedNodes.TryGetValue((kind, id), out bool expanded) && expanded;

    private IOrderedEnumerable<DeviceExplorerNodeViewModel> SortNodes(
        IEnumerable<DeviceExplorerNodeViewModel> nodes)
    {
        IReadOnlyDictionary<Guid, int>? positions = _snapshot.ExplorerOrder;
        return nodes.OrderBy(node => node.OrderId is { } id && positions is not null
                    && positions.TryGetValue(id, out int position) ? position : int.MaxValue)
            .ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase);
    }

    private static int CountDevices(IEnumerable<DeviceExplorerNodeViewModel> nodes)
        => nodes.Sum(node => node.Device is not null ? 1 : CountDevices(node.Children));

    private string GetFolderPath(Guid? folderId)
    {
        var names = new Stack<string>();
        var visited = new HashSet<Guid>();
        while (folderId is { } id && visited.Add(id))
        {
            DeviceGroup? folder = _snapshot.Groups.FirstOrDefault(group => group.Id == id);
            if (folder is null)
                break;
            names.Push(folder.Name);
            folderId = folder.ParentGroupId;
        }

        return string.Join(" / ", names);
    }

    public async Task CreateSiteAsync(string name, CancellationToken cancellationToken = default)
    {
        await _catalogService.CreateSiteAsync(name, cancellationToken);
        await RefreshAsync(null, cancellationToken);
    }

    public async Task CreateFolderAsync(
        string name, DeviceExplorerNodeViewModel? parent, CancellationToken cancellationToken = default)
    {
        Guid? siteId = parent?.SiteId;
        await _catalogService.CreateFolderAsync(name, siteId, null, cancellationToken);
        await RefreshAsync(null, cancellationToken);
    }

    public async Task DeleteEmptyNodeAsync(
        DeviceExplorerNodeViewModel node, CancellationToken cancellationToken = default)
    {
        if (node.Kind == DeviceExplorerNodeKind.Folder && node.FolderId is { } folderId)
            await _catalogService.DeleteEmptyFolderAsync(folderId, cancellationToken);
        else if (node.Kind == DeviceExplorerNodeKind.Site && node.SiteId is { } siteId)
            await _catalogService.DeleteEmptySiteAsync(siteId, cancellationToken);
        else
            throw new ArgumentException("Select a saved site or folder.", nameof(node));
        await RefreshAsync(null, cancellationToken);
    }

    public IReadOnlyList<DeviceExplorerNodeViewModel> GetDragSources(
        DeviceExplorerNodeViewModel pressed,
        IReadOnlyList<DeviceExplorerNodeViewModel> selected)
    {
        if (pressed.Device is null || !selected.Contains(pressed))
            return [pressed];

        HashSet<Guid> selectedIds = selected
            .Where(node => node.Device is not null)
            .Select(node => node.Device!.Id)
            .ToHashSet();
        if (selectedIds.Count < 2)
            return [pressed];

        var sources = new List<DeviceExplorerNodeViewModel>();
        CollectSelectedDeviceNodes(Nodes, selectedIds, sources);
        return sources.Count > 1 ? sources : [pressed];
    }

    private static void CollectSelectedDeviceNodes(
        IEnumerable<DeviceExplorerNodeViewModel> nodes,
        IReadOnlySet<Guid> selectedIds,
        ICollection<DeviceExplorerNodeViewModel> sources)
    {
        foreach (DeviceExplorerNodeViewModel node in nodes)
        {
            if (node.Device is { } device && selectedIds.Contains(device.Id))
                sources.Add(node);
            CollectSelectedDeviceNodes(node.Children, selectedIds, sources);
        }
    }

    public async Task SortSelectedAlphabeticallyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSortSelectedAlphabetically || SelectedNode is not { } parent)
            return;

        Guid[] childIds = parent.Children.Select(child => child.OrderId)
            .OfType<Guid>().ToArray();
        await _catalogService.ResetExplorerOrderAsync(childIds, cancellationToken);
        await RefreshAsync(null, cancellationToken);
        SelectedNode = FindNodeByOrderId(Nodes, parent.OrderId);
    }

    private static DeviceExplorerNodeViewModel? FindNodeByOrderId(
        IEnumerable<DeviceExplorerNodeViewModel> nodes, Guid? orderId)
    {
        foreach (DeviceExplorerNodeViewModel node in nodes)
        {
            if (node.Kind != DeviceExplorerNodeKind.Device && node.OrderId == orderId)
                return node;
            DeviceExplorerNodeViewModel? child = FindNodeByOrderId(node.Children, orderId);
            if (child is not null)
                return child;
        }
        return null;
    }

    public bool CanDrop(
        IReadOnlyList<DeviceExplorerNodeViewModel> sources,
        DeviceExplorerNodeViewModel target)
    {
        if (sources.Count == 1)
            return CanDrop(sources[0], target);
        return CanOrganize && sources.Count > 1
            && target.Kind is DeviceExplorerNodeKind.Site or DeviceExplorerNodeKind.Folder
            && sources.All(source => source.Device is not null)
            && sources.Select(source => source.Device!.Id).Distinct().Count() == sources.Count;
    }

    public async Task MoveOrReorderAsync(
        IReadOnlyList<DeviceExplorerNodeViewModel> sources,
        DeviceExplorerNodeViewModel target,
        bool insertAfter, CancellationToken cancellationToken = default)
    {
        if (!CanDrop(sources, target))
            return;
        if (sources.Count == 1)
        {
            await MoveOrReorderAsync(sources[0], target, insertAfter, cancellationToken);
            return;
        }

        Guid? siteId = target.SiteId;
        Guid? folderId = target.Kind == DeviceExplorerNodeKind.Folder
            ? target.FolderId : null;
        Guid[] sourceIds = sources.Select(source => source.Device!.Id).ToArray();
        HashSet<Guid> movedIds = sourceIds.ToHashSet();
        List<Guid> order = target.Children
            .Select(node => node.OrderId)
            .OfType<Guid>()
            .Where(id => !movedIds.Contains(id))
            .ToList();
        order.AddRange(sourceIds);

        foreach (DeviceExplorerNodeViewModel source in sources)
        {
            DeviceProfile device = source.Device!;
            if (device.SiteId != siteId || device.GroupId != folderId)
                await _catalogService.MoveDeviceAsync(device.Id, siteId, folderId, cancellationToken);
        }

        await _catalogService.SaveExplorerOrderAsync(order, cancellationToken);
        await RefreshAsync(null, cancellationToken);
    }

    public bool CanDrop(DeviceExplorerNodeViewModel source, DeviceExplorerNodeViewModel target)
    {
        if (_filter != DeviceCatalogFilter.All || !string.IsNullOrWhiteSpace(_searchText)
            || ReferenceEquals(source, target))
            return false;
        if (source.Kind == DeviceExplorerNodeKind.Device)
            return true;
        if (source.Kind == DeviceExplorerNodeKind.Site)
            return target.Kind == DeviceExplorerNodeKind.Site
                && source.SiteId is not null && target.SiteId is not null;
        return source.Kind == DeviceExplorerNodeKind.Folder
            && target.Kind is DeviceExplorerNodeKind.Folder or DeviceExplorerNodeKind.Device
            && source.SiteId == target.SiteId
            && source.ParentFolderId == (target.Kind == DeviceExplorerNodeKind.Folder
                ? target.ParentFolderId : target.FolderId);
    }

    public async Task MoveOrReorderAsync(
        DeviceExplorerNodeViewModel source, DeviceExplorerNodeViewModel target,
        bool insertAfter, CancellationToken cancellationToken = default)
    {
        if (!CanDrop(source, target))
            return;

        IReadOnlyList<DeviceExplorerNodeViewModel> siblings;
        if (source.Kind == DeviceExplorerNodeKind.Site)
        {
            siblings = Nodes.Where(node => node.SiteId is not null).ToArray();
        }
        else if (target.Kind == DeviceExplorerNodeKind.Site)
        {
            siblings = target.Children.ToArray();
        }
        else if (target.Kind == DeviceExplorerNodeKind.Folder
            && source.Kind == DeviceExplorerNodeKind.Device)
        {
            siblings = target.Children.ToArray();
        }
        else
        {
            siblings = FindSiblings(Nodes, target) ?? [];
        }

        List<Guid> ids = siblings.Select(node => node.OrderId)
            .Where(id => id is not null && id != source.OrderId)
            .Select(id => id!.Value).ToList();
        if (source.OrderId is not { } sourceId)
            return;
        int index = target.OrderId is { } targetId ? ids.IndexOf(targetId) : -1;
        if (index >= 0 && insertAfter)
            index++;
        if (index < 0)
            index = ids.Count;
        ids.Insert(index, sourceId);

        if (source.Device is { } device)
        {
            Guid? siteId = target.SiteId;
            Guid? groupId = target.Kind switch
            {
                DeviceExplorerNodeKind.Site => null,
                DeviceExplorerNodeKind.Folder => target.FolderId,
                _ => target.FolderId,
            };
            if (device.SiteId != siteId || device.GroupId != groupId)
                await _catalogService.MoveDeviceAsync(device.Id, siteId, groupId, cancellationToken);
        }

        await _catalogService.SaveExplorerOrderAsync(ids, cancellationToken);
        await RefreshAsync(null, cancellationToken);
    }

    private static IReadOnlyList<DeviceExplorerNodeViewModel>? FindSiblings(
        IReadOnlyList<DeviceExplorerNodeViewModel> nodes, DeviceExplorerNodeViewModel target)
    {
        if (nodes.Contains(target))
            return nodes;
        foreach (DeviceExplorerNodeViewModel node in nodes)
        {
            IReadOnlyList<DeviceExplorerNodeViewModel>? children = FindSiblings(
                node.Children.ToArray(), target);
            if (children is not null)
                return children;
        }

        return null;
    }

    private DeviceExplorerNodeViewModel CreateDeviceNode(DeviceProfile device)
    {
        return new DeviceExplorerNodeViewModel(
            device.Name,
            device.AccessMode == DeviceAccessMode.WebOnly
                ? $"WEB · {new Uri(device.WebAccess!.Address).Authority}"
                : device.ConnectionType == Core.Connections.ConnectionType.Serial
                ? $"SERIAL \u00B7 {device.Connection.Host} \u00B7 {device.Connection.Port} baud"
                : device.ConnectionType == Core.Connections.ConnectionType.Ssh && device.Connection.Port == 22
                    ? $"SSH \u00B7 {device.Connection.Host}"
                    : $"{(device.ConnectionType == Core.Connections.ConnectionType.RawTcp ? "RAW TCP" : device.ConnectionType.ToString().ToUpperInvariant())} \u00B7 {device.Connection.Host}:{device.Connection.Port}",
            string.Empty,
            device);
    }

    private string ResolveSiteName(Guid? id)
    {
        return id is null
            ? "Unassigned"
            : _snapshot.Sites.FirstOrDefault(site => site.Id == id)?.Name ?? "Unknown site";
    }

    private string ResolveGroupName(Guid? id)
    {
        return id is null
            ? "Ungrouped"
            : _snapshot.Groups.FirstOrDefault(group => group.Id == id)?.Name ?? "Unknown group";
    }

    private static DeviceExplorerNodeViewModel? FindDeviceNode(
        IEnumerable<DeviceExplorerNodeViewModel> nodes,
        Guid? deviceId)
    {
        if (deviceId is null)
        {
            return null;
        }

        foreach (DeviceExplorerNodeViewModel node in nodes)
        {
            if (node.Device?.Id == deviceId)
            {
                return node;
            }

            DeviceExplorerNodeViewModel? child = FindDeviceNode(node.Children, deviceId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static void CollectDeviceIds(DeviceExplorerNodeViewModel node, ICollection<Guid> ids)
    {
        if (node.Device is { } device)
        {
            ids.Add(device.Id);
        }

        foreach (DeviceExplorerNodeViewModel child in node.Children)
        {
            CollectDeviceIds(child, ids);
        }
    }

    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(Credentials));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(RecentCount));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(HasNoNodes));
    }
}
