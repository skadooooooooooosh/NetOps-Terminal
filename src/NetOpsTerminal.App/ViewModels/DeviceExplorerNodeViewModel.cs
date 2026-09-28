using System.Collections.ObjectModel;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.App.ViewModels;

public sealed class DeviceExplorerNodeViewModel
{
    public DeviceExplorerNodeViewModel(
        string label,
        string details,
        string glyph,
        DeviceProfile? device = null,
        IEnumerable<DeviceExplorerNodeViewModel>? children = null)
    {
        Label = label;
        Details = details;
        Glyph = glyph;
        Device = device;
        Children = children is null
            ? []
            : new ObservableCollection<DeviceExplorerNodeViewModel>(children);
    }

    public string Label { get; }

    public string Details { get; }

    public string Glyph { get; }

    public DeviceProfile? Device { get; }

    public ObservableCollection<DeviceExplorerNodeViewModel> Children { get; }

    public bool IsDevice => Device is not null;

    public bool IsGroup => Device is null;

    public bool IsProduction => Device?.Environment == DeviceEnvironment.Production;

    public bool IsTest => Device?.Environment == DeviceEnvironment.Test;

    public bool IsLab => Device?.Environment == DeviceEnvironment.Lab;

    public bool IsDevelopment => Device?.Environment == DeviceEnvironment.Development;

    public bool IsSwitch => Device?.DeviceType == DeviceType.Switch;

    public bool IsRouter => Device?.DeviceType == DeviceType.Router;

    public bool IsFirewall => Device?.DeviceType == DeviceType.Firewall;

    public bool IsServer => Device?.DeviceType == DeviceType.Server;

    public bool IsAccessPoint => Device?.DeviceType == DeviceType.AccessPoint;

    public bool IsStorage => Device?.DeviceType == DeviceType.Storage;

    public bool IsGenericDevice => IsDevice &&
        !IsSwitch && !IsRouter && !IsFirewall && !IsServer &&
        !IsAccessPoint && !IsStorage;
}
