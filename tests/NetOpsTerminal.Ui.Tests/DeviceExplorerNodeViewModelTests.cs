using NetOpsTerminal.App.ViewModels;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.Ui.Tests;

public sealed class DeviceExplorerNodeViewModelTests
{
    [Theory]
    [InlineData(DeviceType.Switch, true, false, false, false)]
    [InlineData(DeviceType.Router, false, true, false, false)]
    [InlineData(DeviceType.Firewall, false, false, true, false)]
    [InlineData(DeviceType.Server, false, false, false, true)]
    public void DeviceTypeFlagsDescribeTheSavedDevice(
        DeviceType deviceType,
        bool isSwitch,
        bool isRouter,
        bool isFirewall,
        bool isServer)
    {
        var node = new DeviceExplorerNodeViewModel(
            "device-01",
            "192.0.2.10",
            string.Empty,
            CreateDevice(deviceType, DeviceEnvironment.Production));

        Assert.True(node.IsDevice);
        Assert.False(node.IsGroup);
        Assert.Equal(isSwitch, node.IsSwitch);
        Assert.Equal(isRouter, node.IsRouter);
        Assert.Equal(isFirewall, node.IsFirewall);
        Assert.Equal(isServer, node.IsServer);
        Assert.True(node.IsProduction);
    }

    [Theory]
    [InlineData(DeviceType.AccessPoint, true, false, false)]
    [InlineData(DeviceType.Storage, false, true, false)]
    [InlineData(DeviceType.Other, false, false, true)]
    [InlineData(DeviceType.Appliance, false, false, true)]
    [InlineData(DeviceType.ConsoleServer, false, false, true)]
    public void RemainingAndLegacyDeviceTypesSelectOneExplorerIcon(
        DeviceType deviceType,
        bool isAccessPoint,
        bool isStorage,
        bool isGeneric)
    {
        var node = new DeviceExplorerNodeViewModel(
            "device-01",
            "192.0.2.10",
            string.Empty,
            CreateDevice(deviceType, DeviceEnvironment.Unknown));

        Assert.Equal(isAccessPoint, node.IsAccessPoint);
        Assert.Equal(isStorage, node.IsStorage);
        Assert.Equal(isGeneric, node.IsGenericDevice);
    }

    [Theory]
    [InlineData(DeviceEnvironment.Production, true, false, false, false)]
    [InlineData(DeviceEnvironment.Test, false, true, false, false)]
    [InlineData(DeviceEnvironment.Lab, false, false, true, false)]
    [InlineData(DeviceEnvironment.Development, false, false, false, true)]
    [InlineData(DeviceEnvironment.Unknown, false, false, false, false)]
    public void EnvironmentFlagsSelectOnlyTheMatchingBadge(
        DeviceEnvironment environment,
        bool isProduction,
        bool isTest,
        bool isLab,
        bool isDevelopment)
    {
        var node = new DeviceExplorerNodeViewModel(
            "device-01",
            "192.0.2.10",
            string.Empty,
            CreateDevice(DeviceType.Switch, environment));

        Assert.Equal(isProduction, node.IsProduction);
        Assert.Equal(isTest, node.IsTest);
        Assert.Equal(isLab, node.IsLab);
        Assert.Equal(isDevelopment, node.IsDevelopment);
    }

    [Fact]
    public void GroupNodeHasNoDeviceOrEnvironmentBadge()
    {
        var node = new DeviceExplorerNodeViewModel("Core", "4 devices", string.Empty);

        Assert.True(node.IsGroup);
        Assert.False(node.IsDevice);
        Assert.False(node.IsProduction);
        Assert.False(node.IsTest);
        Assert.False(node.IsLab);
        Assert.False(node.IsDevelopment);
    }

    private static DeviceProfile CreateDevice(DeviceType type, DeviceEnvironment environment)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new DeviceProfile(
            Guid.NewGuid(), "device-01", string.Empty,
            new SshDeviceConnectionSettings("192.0.2.10"), type,
            string.Empty, string.Empty, null, null, environment,
            Guid.NewGuid(), null, false, string.Empty, now, now, null);
    }
}

