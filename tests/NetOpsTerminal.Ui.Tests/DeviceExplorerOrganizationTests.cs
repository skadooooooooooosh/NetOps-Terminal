using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Ui.Tests;

public sealed class DeviceExplorerOrganizationTests
{
    [Fact]
    public async Task EmptySiteFoldersStayVisibleAndMovingUpdatesOrganization()
    {
        var catalog = new OrganizationCatalog();
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);

        DeviceExplorerNodeViewModel site = Assert.Single(explorer.Nodes, node => node.SiteId == catalog.Site.Id);
        DeviceExplorerNodeViewModel root = Assert.Single(site.Children, node => node.FolderId == catalog.Root.Id);
        DeviceExplorerNodeViewModel child = Assert.Single(site.Children, node => node.FolderId == catalog.Child.Id);
        Assert.Empty(child.Children);
        Assert.Equal("DC", explorer.GetFolderLocation(child));

        DeviceExplorerNodeViewModel source = Assert.Single(root.Children, node => node.Device is not null);
        await explorer.MoveOrReorderAsync(source, child, false);

        Assert.Equal(catalog.Child.Id, Assert.Single(catalog.Devices, device => device.Name == "SW-01").GroupId);
        var reopened = new DeviceExplorerViewModel(catalog);
        await reopened.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel reopenedSite = Assert.Single(reopened.Nodes, node => node.SiteId == catalog.Site.Id);
        DeviceExplorerNodeViewModel reopenedChild = Assert.Single(
            reopenedSite.Children, node => node.FolderId == catalog.Child.Id);
        DeviceExplorerNodeViewModel moved = Assert.Single(reopenedChild.Children, node => node.Device?.Name == "SW-01");
        Assert.Equal("Switches", reopened.CreateEditRequest(moved.Device!).GroupName);

        await reopened.MoveOrReorderAsync(moved, reopenedSite, false);
        Assert.Null(Assert.Single(catalog.Devices).GroupId);
        DeviceExplorerNodeViewModel siteAfterMove = Assert.Single(
            reopened.Nodes, node => node.SiteId == catalog.Site.Id);
        DeviceExplorerNodeViewModel looseDevice = Assert.Single(
            siteAfterMove.Children, node => node.Device?.Name == "SW-01");
        DeviceExplorerNodeViewModel folderAfterMove = Assert.Single(
            siteAfterMove.Children, node => node.FolderId == catalog.Child.Id);
        await reopened.MoveOrReorderAsync(looseDevice, folderAfterMove, false);
        Assert.Equal(catalog.Child.Id, Assert.Single(catalog.Devices).GroupId);
    }

    [Fact]
    public async Task OrganizationFolderActionRequiresSelectedSite()
    {
        var explorer = new DeviceExplorerViewModel(new OrganizationCatalog());
        await explorer.InitializeAsync(CancellationToken.None);

        Assert.False(explorer.CanCreateFolder);
        DeviceExplorerNodeViewModel site = Assert.Single(
            explorer.Nodes, node => node.SiteId is not null);
        explorer.SelectedNode = site;
        Assert.True(explorer.CanCreateFolder);
        Assert.DoesNotContain(explorer.Nodes, node => node.SiteId is null);
        explorer.SelectedNode = null;
        Assert.False(explorer.CanCreateFolder);
    }

    [Fact]
    public async Task UnassignedAppearsOnlyWhenItContainsDevices()
    {
        var catalog = new OrganizationCatalog();
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);

        Assert.DoesNotContain(explorer.Nodes, node => node.SiteId is null);

        catalog.Devices.Add(OrganizationCatalog.CreateDevice("Loose switch", "192.0.2.20", null, null));
        await explorer.RefreshAsync();
        DeviceExplorerNodeViewModel unassigned = Assert.Single(
            explorer.Nodes, node => node.SiteId is null);
        Assert.Single(unassigned.Children, node => node.Device?.Name == "Loose switch");

        catalog.Devices.RemoveAll(device => device.SiteId is null);
        await explorer.RefreshAsync();
        Assert.DoesNotContain(explorer.Nodes, node => node.SiteId is null);
    }

    [Fact]
    public async Task CreatingFromSelectedFolderCreatesSiblingWithinSite()
    {
        var catalog = new OrganizationCatalog();
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel site = Assert.Single(
            explorer.Nodes, node => node.SiteId == catalog.Site.Id);
        DeviceExplorerNodeViewModel folder = Assert.Single(
            site.Children, node => node.FolderId == catalog.Root.Id);

        await explorer.CreateFolderAsync("Access", folder);

        Assert.Equal(catalog.Site.Id, catalog.CreatedSiteId);
        Assert.Null(catalog.CreatedParentGroupId);
    }

    [Fact]
    public async Task SelectedDevicesDragTogetherInVisibleOrderBetweenFolderAndSite()
    {
        var catalog = new OrganizationCatalog();
        catalog.Devices.Add(OrganizationCatalog.CreateDevice(
            "SW-02", "192.0.2.11", catalog.Site.Id, catalog.Root.Id));
        catalog.Devices.Add(OrganizationCatalog.CreateDevice(
            "SW-00", "192.0.2.12", catalog.Site.Id, catalog.Child.Id));
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel site = Assert.Single(
            explorer.Nodes, node => node.SiteId == catalog.Site.Id);
        DeviceExplorerNodeViewModel root = Assert.Single(
            site.Children, node => node.FolderId == catalog.Root.Id);
        DeviceExplorerNodeViewModel child = Assert.Single(
            site.Children, node => node.FolderId == catalog.Child.Id);
        DeviceExplorerNodeViewModel first = Assert.Single(
            root.Children, node => node.Device?.Name == "SW-01");
        DeviceExplorerNodeViewModel second = Assert.Single(
            root.Children, node => node.Device?.Name == "SW-02");
        IReadOnlyList<DeviceExplorerNodeViewModel> sources =
            explorer.GetDragSources(second, [second, first]);

        Assert.Equal(["SW-01", "SW-02"], sources.Select(node => node.Label));
        Assert.Single(explorer.GetDragSources(first, [second]));
        Assert.True(explorer.CanDrop(sources, child));
        Assert.False(explorer.CanDrop(sources, first));
        await explorer.MoveOrReorderAsync(sources, child, false);

        DeviceExplorerNodeViewModel movedSite = Assert.Single(
            explorer.Nodes, node => node.SiteId == catalog.Site.Id);
        DeviceExplorerNodeViewModel movedFolder = Assert.Single(
            movedSite.Children, node => node.FolderId == catalog.Child.Id);
        Assert.Equal(["SW-00", "SW-01", "SW-02"],
            movedFolder.Children.Select(node => node.Label));
        Assert.All(catalog.Devices.Where(device => device.Name is "SW-01" or "SW-02"),
            device => Assert.Equal(catalog.Child.Id, device.GroupId));

        IReadOnlyList<DeviceExplorerNodeViewModel> movedSources =
            movedFolder.Children.Where(node => node.Device?.Name is "SW-01" or "SW-02").ToArray();
        await explorer.MoveOrReorderAsync(movedSources, movedSite, false);
        Assert.All(catalog.Devices.Where(device => device.Name is "SW-01" or "SW-02"),
            device =>
            {
                Assert.Equal(catalog.Site.Id, device.SiteId);
                Assert.Null(device.GroupId);
            });
    }

    [Fact]
    public async Task DragOrderSurvivesExplorerReload()
    {
        var catalog = new OrganizationCatalog();
        catalog.Devices.Add(OrganizationCatalog.CreateDevice(
            "SW-02", "192.0.2.11", catalog.Site.Id, catalog.Root.Id));
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel root = Assert.Single(
            Assert.Single(explorer.Nodes, node => node.SiteId == catalog.Site.Id).Children,
            node => node.FolderId == catalog.Root.Id);
        DeviceExplorerNodeViewModel source = Assert.Single(root.Children, node => node.Device?.Name == "SW-02");
        DeviceExplorerNodeViewModel target = Assert.Single(root.Children, node => node.Device?.Name == "SW-01");

        await explorer.MoveOrReorderAsync(source, target, false);

        var reopened = new DeviceExplorerViewModel(catalog);
        await reopened.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel reopenedRoot = Assert.Single(
            Assert.Single(reopened.Nodes, node => node.SiteId == catalog.Site.Id).Children,
            node => node.FolderId == catalog.Root.Id);
        string[] labels = reopenedRoot.Children.Where(node => node.Device is not null)
            .Select(node => node.Label).ToArray();
        Assert.Equal("SW-02", labels[0]);
        Assert.Equal("SW-01", labels[1]);
    }

    [Fact]
    public async Task ExplorerRefreshPreservesManuallyExpandedAndCollapsedBranches()
    {
        var catalog = new OrganizationCatalog();
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel site = Assert.Single(
            explorer.Nodes, node => node.SiteId == catalog.Site.Id);
        Assert.DoesNotContain(explorer.Nodes, node => node.SiteId is null);
        DeviceExplorerNodeViewModel root = Assert.Single(
            site.Children, node => node.FolderId == catalog.Root.Id);
        DeviceExplorerNodeViewModel other = Assert.Single(
            site.Children, node => node.FolderId == catalog.Child.Id);
        site.IsExpanded = true;
        root.IsExpanded = true;
        other.IsExpanded = false;
        await explorer.RefreshAsync();
        site = Assert.Single(explorer.Nodes, node => node.SiteId == catalog.Site.Id);
        root = Assert.Single(site.Children, node => node.FolderId == catalog.Root.Id);
        other = Assert.Single(site.Children, node => node.FolderId == catalog.Child.Id);
        Assert.True(site.IsExpanded);
        Assert.True(root.IsExpanded);
        Assert.False(other.IsExpanded);
        Assert.DoesNotContain(explorer.Nodes, node => node.SiteId is null);

        site.IsExpanded = false;
        await explorer.ShowFavoritesCommand.ExecuteAsync(null);
        await explorer.ShowAllCommand.ExecuteAsync(null);
        site = Assert.Single(explorer.Nodes, node => node.SiteId == catalog.Site.Id);
        root = Assert.Single(site.Children, node => node.FolderId == catalog.Root.Id);
        Assert.False(site.IsExpanded);
        Assert.True(root.IsExpanded);
        Assert.DoesNotContain(explorer.Nodes, node => node.SiteId is null);
    }

    [Fact]
    public async Task SortingSelectedFolderClearsCustomOrderForFutureDevices()
    {
        var catalog = new OrganizationCatalog();
        catalog.Devices.Add(OrganizationCatalog.CreateDevice(
            "SW-03", "192.0.2.13", catalog.Site.Id, catalog.Root.Id));
        catalog.Devices.Add(OrganizationCatalog.CreateDevice(
            "SW-02", "192.0.2.12", catalog.Site.Id, catalog.Root.Id));
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel folder = Assert.Single(
            Assert.Single(explorer.Nodes, node => node.SiteId == catalog.Site.Id).Children,
            node => node.FolderId == catalog.Root.Id);
        Assert.Equal(["SW-01", "SW-02", "SW-03"],
            folder.Children.Select(node => node.Label));

        await explorer.MoveOrReorderAsync(
            Assert.Single(folder.Children, node => node.Label == "SW-03"),
            Assert.Single(folder.Children, node => node.Label == "SW-01"), false);
        folder = Assert.Single(
            Assert.Single(explorer.Nodes, node => node.SiteId == catalog.Site.Id).Children,
            node => node.FolderId == catalog.Root.Id);
        Assert.Equal(["SW-03", "SW-01", "SW-02"],
            folder.Children.Select(node => node.Label));
        explorer.SelectedNode = folder;
        Assert.True(explorer.CanSortSelectedAlphabetically);
        await explorer.SortSelectedAlphabeticallyAsync();
        Assert.Equal(catalog.Root.Id, explorer.SelectedNode?.FolderId);
        Assert.Equal(["SW-01", "SW-02", "SW-03"],
            explorer.SelectedNode!.Children.Select(node => node.Label));

        catalog.Devices.Add(OrganizationCatalog.CreateDevice(
            "SW-00", "192.0.2.14", catalog.Site.Id, catalog.Root.Id));
        var reopened = new DeviceExplorerViewModel(catalog);
        await reopened.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel reopenedFolder = Assert.Single(
            Assert.Single(reopened.Nodes, node => node.SiteId == catalog.Site.Id).Children,
            node => node.FolderId == catalog.Root.Id);
        Assert.Equal(["SW-00", "SW-01", "SW-02", "SW-03"],
            reopenedFolder.Children.Select(node => node.Label));
    }

    private sealed class OrganizationCatalog : IDeviceCatalogService
    {
        public Site Site { get; } = new(Guid.NewGuid(), "DC");
        public DeviceGroup Root { get; }
        public DeviceGroup Child { get; }
        public List<DeviceProfile> Devices { get; } = [];
        public Guid? CreatedSiteId { get; private set; }
        public Guid? CreatedParentGroupId { get; private set; }
        private readonly Dictionary<Guid, int> _order = [];

        public OrganizationCatalog()
        {
            Root = new DeviceGroup(Guid.NewGuid(), "Core", Site.Id);
            Child = new DeviceGroup(Guid.NewGuid(), "Switches", Site.Id);
            Devices.Add(CreateDevice("SW-01", "192.0.2.10", Site.Id, Root.Id));
        }

        public static DeviceProfile CreateDevice(string name, string host, Guid? siteId, Guid? groupId)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new DeviceProfile(Guid.NewGuid(), name, string.Empty,
                new NetOpsTerminal.Core.Devices.SshDeviceConnectionSettings(host),
                DeviceType.Switch, string.Empty, string.Empty, siteId, groupId,
                DeviceEnvironment.Unknown, Guid.NewGuid(), null, false, string.Empty, now, now, null);
        }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DeviceCatalogSnapshot> LoadAsync(
            string? searchText, DeviceCatalogFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(new DeviceCatalogSnapshot(
                Devices.ToArray(), [Site], [Root, Child], [], [], ExplorerOrder: new Dictionary<Guid, int>(_order)));

        public Task SaveAsync(DeviceEditRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteEmptySiteAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteEmptyFolderAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Site> CreateSiteAsync(string name, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DeviceGroup> CreateFolderAsync(
            string name, Guid? siteId, Guid? parentGroupId, CancellationToken cancellationToken)
        {
            CreatedSiteId = siteId;
            CreatedParentGroupId = parentGroupId;
            return Task.FromResult(new DeviceGroup(Guid.NewGuid(), name, siteId, parentGroupId));
        }

        public Task MoveDeviceAsync(
            Guid deviceId, Guid? siteId, Guid? groupId, CancellationToken cancellationToken)
        {
            int index = Devices.FindIndex(device => device.Id == deviceId);
            DeviceProfile device = Devices[index];
            Devices[index] = new DeviceProfile(
                device.Id, device.Name, device.Description, device.Connection, device.DeviceType,
                device.Vendor, device.Model, siteId, groupId, device.Environment,
                device.CredentialProfileId, device.UsernameOverride, device.IsFavorite, device.Notes,
                device.CreatedAtUtc, DateTimeOffset.UtcNow, device.LastConnectedAtUtc);
            return Task.CompletedTask;
        }

        public Task SaveExplorerOrderAsync(
            IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
        {
            for (int index = 0; index < siblingIds.Count; index++)
                _order[siblingIds[index]] = index;
            return Task.CompletedTask;
        }

        public Task ResetExplorerOrderAsync(
            IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
        {
            foreach (Guid id in siblingIds)
                _order.Remove(id);
            return Task.CompletedTask;
        }

        public Task RecordRecentAsync(Guid deviceId, DateTimeOffset connectedAtUtc, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
