using NetOpsTerminal.Terminal.RoyalTerminal;

namespace NetOpsTerminal.Terminal.RoyalTerminal.Tests;

public sealed class TerminalCopyTextFormatterTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("show clock", "show clock")]
    [InlineData("show clock     ", "show clock")]
    [InlineData("  description Uplink   ", "  description Uplink")]
    [InlineData("  set  value  here  \nexit ", "  set  value  here\nexit")]
    [InlineData("first   \r\n  second  \r\n", "first\r\n  second\r\n")]
    [InlineData("first \rsecond   \nthird  ", "first\rsecond\nthird")]
    [InlineData("first  \n     \n  last  \n", "first\n\n  last\n")]
    [InlineData("     ", "")]
    [InlineData(" \r\n  \n \r", "\r\n\n\r")]
    [InlineData("\tcommand \t  \n\t next  ", "\tcommand \t\n\t next")]
    [InlineData("CJK \u4e2d \ud83d\ude00 e\u0301  ", "CJK \u4e2d \ud83d\ude00 e\u0301")]
    [InlineData("value\u00a0 ", "value\u00a0")]
    [InlineData("command\u001b[31m  \nnext\u007f ", "command\u001b[31m\nnext\u007f")]
    public void RemovesOnlyLineEndAsciiSpaces(string original, string expected)
    {
        string cleaned = TerminalCopyTextFormatter.TrimLineEnds(original);

        Assert.Equal(expected, cleaned);
        Assert.Equal(original.Where(character => character is '\r' or '\n'),
            cleaned.Where(character => character is '\r' or '\n'));
    }
}