using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Ui.Tests;

public sealed class DeviceExplorerBulkDeleteTests
{
    [Fact]
    public async Task SelectedDevicesAndGroupAreDeletedOnceEach()
    {
        DeviceProfile[] devices =
        [
            CreateDevice("A", "192.0.2.1"),
            CreateDevice("B", "192.0.2.2"),
            CreateDevice("C", "192.0.2.3"),
        ];
        var catalog = new RecordingCatalog(devices);
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel group = Assert.Single(explorer.Nodes);
        DeviceExplorerNodeViewModel first = group.Children[0];

        explorer.SelectedNode = first;
        explorer.SetSelectedNodes([group, first]);
        IReadOnlyList<Guid> selectedIds = explorer.GetSelectedDeviceIds();

        Assert.Equal(3, selectedIds.Count);
        Assert.False(explorer.HasSelectedDevice);
        Assert.True(explorer.HasSelectedDevices);
        await explorer.DeleteDevicesAsync(selectedIds);

        Assert.Equal(3, catalog.Deleted.Distinct().Count());
        Assert.Equal(3, catalog.Deleted.Count);
        Assert.Empty(Assert.Single(explorer.Nodes).Children);
        Assert.False(explorer.HasSelectedDevices);
    }

    [Fact]
    public async Task DeletingTwoSelectedDevicesLeavesTheOthers()
    {
        DeviceProfile[] devices =
        [
            CreateDevice("A", "192.0.2.1"),
            CreateDevice("B", "192.0.2.2"),
            CreateDevice("C", "192.0.2.3"),
        ];
        var catalog = new RecordingCatalog(devices);
        var explorer = new DeviceExplorerViewModel(catalog);
        await explorer.InitializeAsync(CancellationToken.None);
        DeviceExplorerNodeViewModel[] nodes = Assert.Single(explorer.Nodes).Children.ToArray();

        explorer.SelectedNode = nodes[0];
        explorer.SetSelectedNodes([nodes[0], nodes[1]]);
        IReadOnlyList<Guid> selectedIds = explorer.GetSelectedDeviceIds();
        await explorer.DeleteDevicesAsync(selectedIds);

        Assert.Equal(2, catalog.Deleted.Count);
        Assert.Single(Assert.Single(explorer.Nodes).Children);
        Assert.Equal(devices[2].Id,
            Assert.Single(Assert.Single(explorer.Nodes).Children).Device!.Id);
    }
    private static DeviceProfile CreateDevice(string name, string host)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new DeviceProfile(Guid.NewGuid(), name, string.Empty,
            new SshDeviceConnectionSettings(host), DeviceType.Switch,
            string.Empty, string.Empty, null, null, DeviceEnvironment.Unknown,
            Guid.NewGuid(), null, false, string.Empty, now, now, null);
    }

    private sealed class RecordingCatalog(IEnumerable<DeviceProfile> devices) : IDeviceCatalogService
    {
        private readonly List<DeviceProfile> _devices = [.. devices];

        public List<Guid> Deleted { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DeviceCatalogSnapshot> LoadAsync(
            string? searchText, DeviceCatalogFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(new DeviceCatalogSnapshot(_devices.ToArray(), [], [], [], []));

        public Task SaveAsync(DeviceEditRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DeleteEmptySiteAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteEmptyFolderAsync(Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Site> CreateSiteAsync(string name, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DeviceGroup> CreateFolderAsync(
            string name, Guid? siteId, Guid? parentGroupId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task MoveDeviceAsync(
            Guid deviceId, Guid? siteId, Guid? groupId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SaveExplorerOrderAsync(
            IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ResetExplorerOrderAsync(
            IReadOnlyList<Guid> siblingIds, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RecordRecentAsync(
            Guid deviceId, DateTimeOffset connectedAtUtc, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Deleted.Add(id);
            _devices.RemoveAll(device => device.Id == id);
            return Task.CompletedTask;
        }
    }
}