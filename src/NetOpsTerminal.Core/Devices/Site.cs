namespace NetOpsTerminal.Core.Devices;

public sealed record Site
{
    public Site(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A site ID is required.", nameof(id));
        }

        Name = NormalizeName(name, nameof(name));
        Id = id;
    }

    public Guid Id { get; }

    public string Name { get; }

    internal static string NormalizeName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 120
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("Enter a valid name of 120 characters or fewer.", parameterName);
        }

        return value.Trim();
    }
}
