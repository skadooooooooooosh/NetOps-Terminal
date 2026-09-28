using NetOpsTerminal.App.Services;

namespace NetOpsTerminal.Ui.Tests;

public sealed class SerialBaudRateOptionsTests
{
    [Fact]
    public void CommonRatesMatchSupportedOperatorChoices()
    {
        int[] expected =
        [
            110,
            300,
            600,
            1200,
            2400,
            4800,
            9600,
            14400,
            19200,
            38400,
            57600,
            115200,
            230400,
            460800,
            921600,
        ];

        Assert.Equal(expected, SerialBaudRateOptions.Common);
    }

    [Fact]
    public void DefaultRateIsAvailableAndUnique()
    {
        Assert.Equal(
            1,
            SerialBaudRateOptions.Common.Count(
                rate => rate == SerialBaudRateOptions.Default));
        Assert.Equal(
            SerialBaudRateOptions.Common.Count,
            SerialBaudRateOptions.Common.Distinct().Count());
    }
}
