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
                OnPropertyChanged(nameof(HasSelectedDevice));
            }
        }
    }

    public DeviceProfile? SelectedDevice => SelectedNode?.Device;

    public IReadOnlyList<Guid> GetSelectedDeviceIds()
    {
        if (SelectedNode is null)
        {
            return [];
        }

        var ids = new List<Guid>();
        CollectDeviceIds(SelectedNode, ids);
        return ids;
    }

    public bool HasSelectedDevice => SelectedDevice is not null;

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

    public async Task DeleteSelectedAsync(CancellationToken cancellationToken = default)
    {
        DeviceProfile? device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        await _catalogService.DeleteAsync(device.Id, cancellationToken);
        SelectedNode = null;
        await RefreshAsync(null, cancellationToken);
    }

    public DeviceEditRequest CreateEditRequest(DeviceProfile device)
    {
        ArgumentNullException.ThrowIfNull(device);
        string siteName = device.SiteId is { } siteId
            ? _snapshot.Sites.FirstOrDefault(site => site.Id == siteId)?.Name ?? string.Empty
            : string.Empty;
        string groupName = device.GroupId is { } groupId
            ? _snapshot.Groups.FirstOrDefault(group => group.Id == groupId)?.Name ?? string.Empty
            : string.Empty;
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
            TerminalType = device.Connection.TerminalType,
            Encoding = device.Connection.Encoding,
            ConnectTimeoutSeconds = device.Connection.ConnectTimeoutSeconds,
            SerialDataBits = device.Connection.SerialDataBits,
            SerialParity = device.Connection.SerialParity,
            SerialStopBits = device.Connection.SerialStopBits,
            SerialFlowControl = device.Connection.SerialFlowControl,
            RawLineEnding = device.Connection.RawLineEnding,
            TelnetAcknowledged = device.Connection.TelnetAcknowledged,
        };
    }

    private async Task SetFilterAsync(DeviceCatalogFilter filter)
    {
        _filter = filter;
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsFavoritesSelected));
        OnPropertyChanged(nameof(IsRecentSelected));
        OnPropertyChanged(nameof(FilterLabel));
        await RefreshAsync();
    }

    private void BuildNodes()
    {
        Guid? selectedId = SelectedDevice?.Id;
        Nodes.Clear();
        if (_filter != DeviceCatalogFilter.All || !string.IsNullOrWhiteSpace(_searchText))
        {
            foreach (DeviceProfile device in _snapshot.Devices)
            {
                Nodes.Add(CreateDeviceNode(device));
            }
        }
        else
        {
            var devicesBySite = _snapshot.Devices
                .GroupBy(device => device.SiteId)
                .OrderBy(group => group.Key is null ? 1 : 0)
                .ThenBy(group => ResolveSiteName(group.Key), StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<Guid?, DeviceProfile> siteDevices in devicesBySite)
            {
                var children = new List<DeviceExplorerNodeViewModel>();
                foreach (IGrouping<Guid?, DeviceProfile> groupDevices in siteDevices
                    .GroupBy(device => device.GroupId)
                    .OrderBy(group => group.Key is null ? 1 : 0)
                    .ThenBy(group => ResolveGroupName(group.Key), StringComparer.OrdinalIgnoreCase))
                {
                    DeviceExplorerNodeViewModel[] deviceNodes = groupDevices
                        .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(CreateDeviceNode)
                        .ToArray();
                    if (groupDevices.Key is null)
                    {
                        children.AddRange(deviceNodes);
                    }
                    else
                    {
                        children.Add(new DeviceExplorerNodeViewModel(
                            ResolveGroupName(groupDevices.Key),
                            $"{deviceNodes.Length} devices",
                            "▤",
                            children: deviceNodes));
                    }
                }

                Nodes.Add(new DeviceExplorerNodeViewModel(
                    ResolveSiteName(siteDevices.Key),
                    $"{siteDevices.Count()} devices",
                    "▣",
                    children: children));
            }
        }

        SelectedNode = FindDeviceNode(Nodes, selectedId);
        NotifyCollectionState();
    }

    private DeviceExplorerNodeViewModel CreateDeviceNode(DeviceProfile device)
    {
        return new DeviceExplorerNodeViewModel(
            device.Name,
            device.ConnectionType == Core.Connections.ConnectionType.Serial
                ? $"Serial \u00B7 {device.Connection.Host} \u00B7 {device.Connection.Port} baud"
                : device.ConnectionType == Core.Connections.ConnectionType.Ssh && device.Connection.Port == 22
                    ? $"SSH \u00B7 {device.Connection.Host}"
                    : $"{device.ConnectionType} \u00B7 {device.Connection.Host}:{device.Connection.Port}",
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
