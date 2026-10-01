namespace NetOpsTerminal.Core.Devices;

public sealed record DeviceGroup
{
    public DeviceGroup(Guid id, string name, Guid? siteId, Guid? parentGroupId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A device-group ID is required.", nameof(id));
        }

        Id = id;
        Name = Site.NormalizeName(name, nameof(name));
        SiteId = siteId;
        ParentGroupId = parentGroupId;
    }

    public Guid Id { get; }

    public string Name { get; }

    public Guid? SiteId { get; }

    public Guid? ParentGroupId { get; }
}
