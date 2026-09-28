using System.Text;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class TerminalEncodingPolicyTests
{
    [Theory]
    [InlineData(TerminalLineEnding.CarriageReturn, "show version\r")]
    [InlineData(TerminalLineEnding.LineFeed, "show version\n")]
    [InlineData(TerminalLineEnding.CarriageReturnLineFeed, "show version\r\n")]
    public void ConvertInputNormalizesMixedLineEndings(
        TerminalLineEnding lineEnding,
        string expected)
    {
        byte[] converted = TerminalEncodingPolicy.ConvertInput(
            Encoding.UTF8.GetBytes("show version\r\n"),
            Encoding.UTF8,
            lineEnding);

        Assert.Equal(expected, Encoding.UTF8.GetString(converted));
    }

    [Fact]
    public void ConvertInputTranscodesLatin1WithoutChangingAnsiBytes()
    {
        byte[] converted = TerminalEncodingPolicy.ConvertInput(
            Encoding.UTF8.GetBytes("café\u001b[32m"),
            Encoding.Latin1,
            lineEnding: null);

        Assert.Equal("café\u001b[32m", Encoding.Latin1.GetString(converted));
        Assert.Equal(0xE9, converted[3]);
    }

    [Theory]
    [InlineData(TerminalTextEncoding.Utf8, "utf-8")]
    [InlineData(TerminalTextEncoding.Ascii, "us-ascii")]
    [InlineData(TerminalTextEncoding.Latin1, "iso-8859-1")]
    public void ResolveReturnsRequestedEncoding(TerminalTextEncoding value, string expectedWebName)
    {
        Assert.Equal(expectedWebName, TerminalEncodingPolicy.Resolve(value).WebName);
    }

    [Fact]
    public void SerialPortCatalogReturnsSafeDistinctNames()
    {
        IReadOnlyList<string> ports = new SystemSerialPortCatalog().GetAvailablePortNames();

        Assert.All(ports, port => Assert.False(string.IsNullOrWhiteSpace(port)));
        Assert.Equal(ports.Count, ports.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
