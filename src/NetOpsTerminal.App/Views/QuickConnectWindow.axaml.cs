using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.App.Services;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.App.Views;

public sealed partial class QuickConnectWindow : ThemedWindow
{
    private readonly ISerialPortCatalog? _serialPortCatalog;

    public QuickConnectWindow()
        : this(null)
    {
    }

    public QuickConnectWindow(ISerialPortCatalog? serialPortCatalog)
    {
        _serialPortCatalog = serialPortCatalog;
        InitializeComponent();
        ProtocolComboBox.SelectionChanged += Protocol_OnSelectionChanged;
        TerminalTextEncoding[] encodings = Enum.GetValues<TerminalTextEncoding>();
        TelnetEncodingComboBox.ItemsSource = encodings;
        TelnetEncodingComboBox.SelectedItem = TerminalTextEncoding.Utf8;
        SerialEncodingComboBox.ItemsSource = encodings;
        SerialEncodingComboBox.SelectedItem = TerminalTextEncoding.Utf8;
        RawEncodingComboBox.ItemsSource = encodings;
        RawEncodingComboBox.SelectedItem = TerminalTextEncoding.Utf8;
        RawLineEndingComboBox.ItemsSource = Enum.GetValues<TerminalLineEnding>();
        RawLineEndingComboBox.SelectedItem = TerminalLineEnding.CarriageReturnLineFeed;
        SerialDataBitsComboBox.ItemsSource = new[] { 5, 6, 7, 8 };
        SerialParityComboBox.ItemsSource = Enum.GetValues<SerialParity>();
        SerialStopBitsComboBox.ItemsSource = Enum.GetValues<SerialStopBits>();
        SerialFlowControlComboBox.ItemsSource = Enum.GetValues<SerialFlowControl>();
        SerialBaudRateComboBox.ItemsSource = SerialBaudRateOptions.Common;
        SerialBaudRateComboBox.SelectedItem = SerialBaudRateOptions.Default;
        SerialDataBitsComboBox.SelectedItem = 8;
        SerialParityComboBox.SelectedItem = SerialParity.None;
        SerialStopBitsComboBox.SelectedItem = SerialStopBits.One;
        SerialFlowControlComboBox.SelectedItem = SerialFlowControl.None;
        RefreshSerialPorts();
        UpdateProtocolView();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        ClearPassword();
        Close(null);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ClearPassword();
        base.OnClosing(e);
    }

    private void Connect_OnClick(object? sender, RoutedEventArgs e)
    {
        ValidationErrorText.IsVisible = false;
        try
        {
            TerminalSessionRequest request = QuickConnectProtocolSelection.FromSelectedIndex(
                ProtocolComboBox.SelectedIndex) switch
            {
                QuickConnectProtocol.Ssh => CreateSshRequest(),
                QuickConnectProtocol.Telnet => CreateTelnetRequest(),
                QuickConnectProtocol.Serial => CreateSerialRequest(),
                QuickConnectProtocol.RawTcp => CreateRawTcpRequest(),
                _ => throw new ArgumentException("Select a connection protocol."),
            };
            ClearPassword();
            Close(request);
        }
        catch (ArgumentException exception)
        {
            ValidationErrorText.Text = exception.Message;
            ValidationErrorText.IsVisible = true;
        }
    }

    private void Protocol_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateProtocolView();
    }

    private void UpdateProtocolView()
    {
        QuickConnectProtocol protocol = QuickConnectProtocolSelection.FromSelectedIndex(
            ProtocolComboBox.SelectedIndex);
        SshPanel.IsVisible = protocol == QuickConnectProtocol.Ssh;
        TelnetPanel.IsVisible = protocol == QuickConnectProtocol.Telnet;
        SerialPanel.IsVisible = protocol == QuickConnectProtocol.Serial;
        RawTcpPanel.IsVisible = protocol == QuickConnectProtocol.RawTcp;
        ProtocolSummaryText.Text = QuickConnectProtocolSelection.GetSummary(protocol);
        ValidationErrorText.IsVisible = false;
    }

    private TerminalSessionRequest CreateSshRequest()
    {
        if (!int.TryParse(SshPortTextBox.Text, out int port))
        {
            throw new ArgumentException("SSH port must be a number between 1 and 65535.");
        }

        EphemeralSecret? password = null;
        try
        {
            password = EphemeralSecret.Create(SshPasswordTextBox.Text ?? string.Empty);
            var options = new SshConnectionOptions(
                SshHostTextBox.Text ?? string.Empty,
                port,
                SshUsernameTextBox.Text ?? string.Empty,
                password,
                ReadTimeout(SshTimeoutComboBox, SshConnectionOptions.DefaultConnectTimeoutSeconds));
            password = null;
            return new TerminalSessionRequest(
                Guid.NewGuid(),
                $"{options.Username}@{options.Host}",
                options);
        }
        finally
        {
            password?.Dispose();
        }
    }

    private TerminalSessionRequest CreateTelnetRequest()
    {
        if (TelnetAcknowledgementCheckBox.IsChecked != true)
        {
            throw new ArgumentException("Acknowledge that Telnet sends all data without encryption before connecting.");
        }

        if (!int.TryParse(TelnetPortTextBox.Text, out int port))
        {
            throw new ArgumentException("Telnet port must be a number between 1 and 65535.");
        }

        var options = new TelnetConnectionOptions(
            TelnetHostTextBox.Text ?? string.Empty,
            port,
            TelnetTerminalTypeTextBox.Text ?? string.Empty,
            ReadEncoding(TelnetEncodingComboBox),
            ReadTimeout(TelnetTimeoutComboBox, TelnetConnectionOptions.DefaultConnectTimeoutSeconds));
        return new TerminalSessionRequest(
            Guid.NewGuid(),
            $"Telnet · {options.Host}",
            options);
    }

    private TerminalSessionRequest CreateSerialRequest()
    {
        if (SerialPortComboBox.SelectedItem is not string portName)
        {
            throw new ArgumentException("No serial port is selected. Attach the console cable and refresh the COM port list.");
        }

        if (SerialBaudRateComboBox.SelectedItem is not int baudRate)
        {
            throw new ArgumentException("Select a supported baud rate.");
        }

        var options = new SerialConnectionOptions(
            portName,
            baudRate,
            SerialDataBitsComboBox.SelectedItem is int dataBits ? dataBits : 8,
            ReadEnum<SerialParity>(SerialParityComboBox),
            ReadEnum<SerialStopBits>(SerialStopBitsComboBox),
            ReadEnum<SerialFlowControl>(SerialFlowControlComboBox),
            ReadEncoding(SerialEncodingComboBox));
        return new TerminalSessionRequest(
            Guid.NewGuid(),
            $"Serial · {options.PortName}",
            options);
    }

    private TerminalSessionRequest CreateRawTcpRequest()
    {
        if (!int.TryParse(RawPortTextBox.Text, out int port))
        {
            throw new ArgumentException("Raw TCP port must be a number between 1 and 65535.");
        }

        var options = new RawTcpConnectionOptions(
            RawHostTextBox.Text ?? string.Empty,
            port,
            ReadEncoding(RawEncodingComboBox),
            ReadEnum<TerminalLineEnding>(RawLineEndingComboBox),
            ReadTimeout(RawTimeoutComboBox, RawTcpConnectionOptions.DefaultConnectTimeoutSeconds));
        return new TerminalSessionRequest(
            Guid.NewGuid(),
            $"Raw TCP · {options.Host}:{options.Port}",
            options);
    }

    private void RefreshSerialPorts_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshSerialPorts();
    }

    private void RefreshSerialPorts()
    {
        string? selected = SerialPortComboBox.SelectedItem as string;
        try
        {
            IReadOnlyList<string> ports = _serialPortCatalog?.GetAvailablePortNames() ?? [];
            SerialPortComboBox.ItemsSource = ports;
            SerialPortComboBox.SelectedItem = ports.FirstOrDefault(
                port => string.Equals(port, selected, StringComparison.OrdinalIgnoreCase))
                ?? (ports.Count > 0 ? ports[0] : null);
            SerialPortStatusText.Text = ports.Count == 0
                ? "No COM ports detected. Attach the console cable and select Refresh."
                : $"Detected {ports.Count} serial port(s).";
        }
        catch (Exception exception)
        {
            SerialPortComboBox.ItemsSource = Array.Empty<string>();
            SerialPortStatusText.Text = $"COM ports could not be enumerated ({exception.GetType().Name}).";
        }
    }

    private static int ReadTimeout(ComboBox comboBox, int fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string value }
            && int.TryParse(value, out int parsed)
                ? parsed
                : fallback;
    }

    private static TerminalTextEncoding ReadEncoding(ComboBox comboBox)
    {
        return ReadEnum<TerminalTextEncoding>(comboBox);
    }

    private static TEnum ReadEnum<TEnum>(ComboBox comboBox)
        where TEnum : struct, Enum
    {
        return comboBox.SelectedItem is TEnum value
            ? value
            : throw new ArgumentException($"Select a valid {typeof(TEnum).Name} value.");
    }

    private void ClearPassword()
    {
        SshPasswordTextBox.Text = string.Empty;
    }
}
