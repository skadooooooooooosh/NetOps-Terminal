namespace NetOpsTerminal.Core.Credentials;

public sealed record CredentialProfile
{
    public CredentialProfile(
        Guid id,
        string name,
        string username,
        string secretReference,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A credential-profile ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 120
            || name.Any(char.IsControl))
        {
            throw new ArgumentException("Enter a valid credential name.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(username)
            || username.Length > 255
            || username.Any(char.IsControl))
        {
            throw new ArgumentException("Enter a valid username.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(secretReference)
            || secretReference.Length > 200
            || secretReference.Any(char.IsControl))
        {
            throw new ArgumentException("A valid secret reference is required.", nameof(secretReference));
        }

        Id = id;
        Name = name.Trim();
        Username = username.Trim();
        SecretReference = secretReference;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Username { get; }

    public string SecretReference { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public override string ToString() => $"{Name} ({Username}, secret [PROTECTED])";
}
