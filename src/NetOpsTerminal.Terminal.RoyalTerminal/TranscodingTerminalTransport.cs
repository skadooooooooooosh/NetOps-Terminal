using System.Text;
using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Terminal;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

internal sealed class TranscodingTerminalTransportProvider : ITerminalTransportProvider
{
    private readonly ITerminalTransportProvider _inner;
    private readonly Encoding _targetEncoding;
    private readonly TerminalLineEnding? _lineEnding;

    public TranscodingTerminalTransportProvider(
        ITerminalTransportProvider inner,
        TerminalTextEncoding encoding,
        TerminalLineEnding? lineEnding = null)
    {
        _inner = inner;
        _targetEncoding = TerminalEncodingPolicy.Resolve(encoding);
        _lineEnding = lineEnding;
    }

    public string TransportId => _inner.TransportId;

    public bool CanHandle(ITerminalTransportOptions options) => _inner.CanHandle(options);

    public ITerminalTransport Create()
    {
        return new TranscodingTerminalTransport(
            _inner.Create(),
            _targetEncoding,
            _lineEnding);
    }
}

internal sealed class TranscodingTerminalTransport : ITerminalTransport, IDisposable
{
    private static readonly Encoding TerminalEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private readonly ITerminalTransport _inner;
    private readonly Encoding _targetEncoding;
    private readonly Decoder _outputDecoder;
    private readonly TerminalLineEnding? _lineEnding;
    private readonly object _decoderLock = new();
    private bool _disposed;

    public TranscodingTerminalTransport(
        ITerminalTransport inner,
        Encoding targetEncoding,
        TerminalLineEnding? lineEnding)
    {
        _inner = inner;
        _targetEncoding = targetEncoding;
        _outputDecoder = targetEncoding.GetDecoder();
        _lineEnding = lineEnding;
        _inner.DataReceived += OnDataReceived;
        _inner.ProcessExited += OnProcessExited;
    }

    public bool IsRunning => _inner.IsRunning;

    public event Action<byte[], int>? DataReceived;

    public event Action<int>? ProcessExited;

    public ValueTask StartAsync(
        ITerminalTransportOptions options,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _inner.StartAsync(options, cancellationToken);
    }

    public void SendInput(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] converted = TerminalEncodingPolicy.ConvertInput(
            data,
            _targetEncoding,
            _lineEnding);
        _inner.SendInput(converted);
    }

    public void Resize(TerminalSessionDimensions dimensions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inner.Resize(dimensions);
    }

    public ValueTask StopAsync()
    {
        return _disposed ? ValueTask.CompletedTask : _inner.StopAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.DataReceived -= OnDataReceived;
        _inner.ProcessExited -= OnProcessExited;
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnDataReceived(byte[] data, int count)
    {
        if (_disposed || count <= 0)
        {
            return;
        }

        count = Math.Min(count, data.Length);
        char[] characters = new char[_targetEncoding.GetMaxCharCount(count)];
        int characterCount;
        lock (_decoderLock)
        {
            characterCount = _outputDecoder.GetChars(
                data.AsSpan(0, count),
                characters.AsSpan(),
                flush: false);
        }

        if (characterCount == 0)
        {
            return;
        }

        byte[] utf8 = TerminalEncoding.GetBytes(
            new string(characters, 0, characterCount));
        DataReceived?.Invoke(utf8, utf8.Length);
    }

    private void OnProcessExited(int exitCode)
    {
        ProcessExited?.Invoke(exitCode);
    }
}

internal static class TerminalEncodingPolicy
{
    private static readonly Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public static Encoding Resolve(TerminalTextEncoding encoding)
    {
        return encoding switch
        {
            TerminalTextEncoding.Utf8 => Utf8,
            TerminalTextEncoding.Ascii => Encoding.ASCII,
            TerminalTextEncoding.Latin1 => Encoding.Latin1,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        };
    }

    public static byte[] ConvertInput(
        ReadOnlySpan<byte> terminalUtf8,
        Encoding targetEncoding,
        TerminalLineEnding? lineEnding)
    {
        string text = Utf8.GetString(terminalUtf8);
        if (lineEnding is not null)
        {
            text = NormalizeLineEndings(text, ResolveLineEnding(lineEnding.Value));
        }

        return targetEncoding.GetBytes(text);
    }

    private static string ResolveLineEnding(TerminalLineEnding lineEnding)
    {
        return lineEnding switch
        {
            TerminalLineEnding.CarriageReturn => "\r",
            TerminalLineEnding.LineFeed => "\n",
            TerminalLineEnding.CarriageReturnLineFeed => "\r\n",
            _ => throw new ArgumentOutOfRangeException(nameof(lineEnding)),
        };
    }

    private static string NormalizeLineEndings(string value, string lineEnding)
    {
        var result = new StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character == '\r')
            {
                if (index + 1 < value.Length && value[index + 1] == '\n')
                {
                    index++;
                }

                result.Append(lineEnding);
            }
            else if (character == '\n')
            {
                result.Append(lineEnding);
            }
            else
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }
}
