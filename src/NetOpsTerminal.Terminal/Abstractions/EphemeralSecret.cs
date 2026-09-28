using System.Security.Cryptography;

namespace NetOpsTerminal.Terminal.Abstractions;

public sealed class EphemeralSecret : IDisposable
{
    private readonly object _gate = new();
    private char[]? _value;

    private EphemeralSecret(char[] value)
    {
        _value = value;
    }

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _value is null;
            }
        }
    }

    public static EphemeralSecret Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            throw new ArgumentException("A password is required.", nameof(value));
        }

        return new EphemeralSecret(value.ToCharArray());
    }

    public string Reveal()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_value is null, this);
            return new string(_value);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_value is null)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_value.AsSpan()));
            _value = null;
        }
    }

    public override string ToString()
    {
        return "[REDACTED]";
    }
}
