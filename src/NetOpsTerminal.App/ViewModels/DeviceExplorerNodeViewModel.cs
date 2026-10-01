using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NetOpsTerminal.Core.Devices;

namespace NetOpsTerminal.App.ViewModels;

public enum DeviceExplorerNodeKind { Device, Site, Folder }

public sealed class DeviceExplorerNodeViewModel : ObservableObject
{
    private bool _isDropBefore;
    private bool _isDropAfter;
    private bool _isDropInto;
    private bool _isExpanded;

    public DeviceExplorerNodeViewModel(
        string label,
        string details,
        string glyph,
        DeviceProfile? device = null,
        IEnumerable<DeviceExplorerNodeViewModel>? children = null,
        Guid? siteId = null,
        Guid? folderId = null,
        Guid? parentFolderId = null,
        bool isSite = false)
    {
        Label = label;
        Details = details;
        Glyph = glyph;
        Device = device;
        SiteId = device?.SiteId ?? siteId;
        FolderId = device?.GroupId ?? folderId;
        ParentFolderId = parentFolderId;
        Kind = device is not null ? DeviceExplorerNodeKind.Device
            : isSite ? DeviceExplorerNodeKind.Site : DeviceExplorerNodeKind.Folder;
        Children = children is null
            ? []
            : new ObservableCollection<DeviceExplorerNodeViewModel>(children);
    }

    public string Label { get; }

    public string Details { get; }

    public string Glyph { get; }

    public DeviceProfile? Device { get; }

    public DeviceExplorerNodeKind Kind { get; }

    public Guid? SiteId { get; }

    public Guid? FolderId { get; }

    public Guid? ParentFolderId { get; }

    public Guid? OrderId => Device?.Id ?? (Kind == DeviceExplorerNodeKind.Site ? SiteId : FolderId);

    public ObservableCollection<DeviceExplorerNodeViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsDropBefore
    {
        get => _isDropBefore;
        set => SetProperty(ref _isDropBefore, value);
    }

    public bool IsDropAfter
    {
        get => _isDropAfter;
        set => SetProperty(ref _isDropAfter, value);
    }

    public bool IsDropInto
    {
        get => _isDropInto;
        set => SetProperty(ref _isDropInto, value);
    }

    public bool IsDevice => Device is not null;

    public bool IsWebOnly => Device?.AccessMode == DeviceAccessMode.WebOnly;

    public bool IsTerminalAndWeb => Device?.AccessMode == DeviceAccessMode.TerminalAndWeb;

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
