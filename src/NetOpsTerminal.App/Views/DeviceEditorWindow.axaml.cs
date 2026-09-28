using Avalonia.Controls;
using Avalonia.Interactivity;
using NetOpsTerminal.Application.Devices;
using NetOpsTerminal.App.Services;
using NetOpsTerminal.Core.Credentials;
using NetOpsTerminal.Core.Devices;
using NetOpsTerminal.Core.Connections;
using NetOpsTerminal.Terminal.Abstractions;

namespace NetOpsTerminal.App.Views;

public sealed partial class DeviceEditorWindow : ThemedWindow
{
    private static readonly DeviceType[] SelectableDeviceTypes =
    [
        DeviceType.Switch,
        DeviceType.Router,
        DeviceType.Firewall,
        DeviceType.Server,
        DeviceType.AccessPoint,
        DeviceType.Storage,
        DeviceType.Other,
    ];

    private readonly Guid? _deviceId;
    private readonly ISerialPortCatalog? _serialPortCatalog;
    private string _vendor = string.Empty;
    private string _model = string.Empty;

    public DeviceEditorWindow() : this(null)
    {
    }

    public DeviceEditorWindow(ISerialPortCatalog? serialPortCatalog)
    {
        _serialPortCatalog = serialPortCatalog;
        InitializeComponent();
        DeviceTypeComboBox.ItemsSource = SelectableDeviceTypes;
        DeviceTypeComboBox.SelectedItem = DeviceType.Switch;
        EnvironmentComboBox.ItemsSource = Enum.GetValues<DeviceEnvironment>();
        EnvironmentComboBox.SelectedItem = DeviceEnvironment.Unknown;
        TerminalTextEncoding[] encodings = Enum.GetValues<TerminalTextEncoding>();
        TelnetEncodingComboBox.ItemsSource = encodings;
        SerialEncodingComboBox.ItemsSource = encodings;
        RawEncodingComboBox.ItemsSource = encodings;
        TelnetEncodingComboBox.SelectedItem = TerminalTextEncoding.Utf8;
        SerialEncodingComboBox.SelectedItem = TerminalTextEncoding.Utf8;
        RawEncodingComboBox.SelectedItem = TerminalTextEncoding.Utf8;
        SerialBaudRateComboBox.ItemsSource = SerialBaudRateOptions.Common;
        SerialBaudRateComboBox.SelectedItem = SerialBaudRateOptions.Default;
        SerialDataBitsComboBox.ItemsSource = new[] { 5, 6, 7, 8 };
        SerialDataBitsComboBox.SelectedItem = 8;
        SerialParityComboBox.ItemsSource = Enum.GetValues<SerialParity>();
        SerialParityComboBox.SelectedItem = SerialParity.None;
        SerialStopBitsComboBox.ItemsSource = Enum.GetValues<SerialStopBits>();
        SerialStopBitsComboBox.SelectedItem = SerialStopBits.One;
        SerialFlowControlComboBox.ItemsSource = Enum.GetValues<SerialFlowControl>();
        SerialFlowControlComboBox.SelectedItem = SerialFlowControl.None;
        RawLineEndingComboBox.ItemsSource = Enum.GetValues<TerminalLineEnding>();
        RawLineEndingComboBox.SelectedItem = TerminalLineEnding.CarriageReturnLineFeed;
        RefreshSerialPorts();
        UpdateProtocolView();
    }

    public DeviceEditorWindow(
        DeviceEditRequest? existing,
        IReadOnlyList<CredentialProfile> credentials,
        ISerialPortCatalog? serialPortCatalog = null)
        : this(serialPortCatalog)
    {
        CredentialComboBox.ItemsSource = credentials;
        CredentialComboBox.SelectedIndex = credentials.Count > 0 ? 0 : -1;
        if (existing is null)
        {
            return;
        }

        _deviceId = existing.Id;
        HeadingText.Text = "Edit device";
        NameTextBox.Text = existing.Name;
        DescriptionTextBox.Text = existing.Description;
        ProtocolComboBox.SelectedIndex = existing.ConnectionType switch
        {
            ConnectionType.Ssh => 0,
            ConnectionType.Telnet => 1,
            ConnectionType.Serial => 2,
            ConnectionType.RawTcp => 3,
            _ => 0,
        };
        HostTextBox.Text = existing.Host;
        PortTextBox.Text = existing.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (existing.ConnectionType == ConnectionType.Serial)
        {
            SerialPortComboBox.Text = existing.Host;
            SerialBaudRateComboBox.Text = existing.Port.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        TerminalTypeTextBox.Text = existing.TerminalType;
        TelnetTimeoutTextBox.Text = existing.ConnectTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RawTimeoutTextBox.Text = existing.ConnectTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TelnetEncodingComboBox.SelectedItem = Enum.Parse<TerminalTextEncoding>(existing.Encoding);
        SerialEncodingComboBox.SelectedItem = Enum.Parse<TerminalTextEncoding>(existing.Encoding);
        RawEncodingComboBox.SelectedItem = Enum.Parse<TerminalTextEncoding>(existing.Encoding);
        SerialDataBitsComboBox.SelectedItem = existing.SerialDataBits;
        SerialParityComboBox.SelectedItem = Enum.Parse<SerialParity>(existing.SerialParity);
        SerialStopBitsComboBox.SelectedItem = Enum.Parse<SerialStopBits>(existing.SerialStopBits);
        SerialFlowControlComboBox.SelectedItem = Enum.Parse<SerialFlowControl>(existing.SerialFlowControl);
        RawLineEndingComboBox.SelectedItem = Enum.Parse<TerminalLineEnding>(existing.RawLineEnding);
        TelnetAcknowledgementCheckBox.IsChecked = false;
        if (!SelectableDeviceTypes.Contains(existing.DeviceType))
        {
            // Keep an existing legacy type editable without offering it for new devices.
            DeviceTypeComboBox.ItemsSource = SelectableDeviceTypes.Append(existing.DeviceType).ToArray();
        }

        DeviceTypeComboBox.SelectedItem = existing.DeviceType;
        _vendor = existing.Vendor;
        _model = existing.Model;
        SiteTextBox.Text = existing.SiteName;
        GroupTextBox.Text = existing.GroupName;
        EnvironmentComboBox.SelectedItem = existing.Environment;
        CredentialComboBox.SelectedItem = credentials.FirstOrDefault(
            credential => credential.Id == existing.CredentialProfileId);
        UsernameOverrideTextBox.Text = existing.UsernameOverride;
        FavoriteCheckBox.IsChecked = existing.IsFavorite;
        NotesTextBox.Text = existing.Notes;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Protocol_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (HostLabel is null)
            return;

        int defaultPort = ProtocolComboBox.SelectedIndex switch
        {
            1 => 23,
            2 => 9_600,
            3 => 2_001,
            _ => 22,
        };
        PortTextBox.Text = defaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateProtocolView();
    }

    private void UpdateProtocolView()
    {
        if (HostLabel is null)
            return;

        ConnectionType type = SelectedProtocol();
        bool ssh = type == ConnectionType.Ssh;
        NetworkEndpointPanel.IsVisible = type != ConnectionType.Serial;
        SshHint.IsVisible = ssh;
        TelnetOptionsPanel.IsVisible = type == ConnectionType.Telnet;
        SerialOptionsPanel.IsVisible = type == ConnectionType.Serial;
        RawOptionsPanel.IsVisible = type == ConnectionType.RawTcp;
        CredentialsTab.IsEnabled = ssh;
    }

    private void RefreshSerialPorts_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshSerialPorts();
    }

    private void RefreshSerialPorts()
    {
        string? enteredPort = SerialPortComboBox.Text;
        try
        {
            IReadOnlyList<string> ports = _serialPortCatalog?.GetAvailablePortNames() ?? [];
            SerialPortComboBox.ItemsSource = ports;
            SerialPortComboBox.Text = enteredPort;
            SerialPortStatusText.Text = ports.Count == 0
                ? "No COM ports detected. Enter a COM name or attach a cable and select Refresh."
                : $"Detected {ports.Count} COM port(s). You can also type a COM name.";
        }
        catch (Exception exception)
        {
            SerialPortComboBox.ItemsSource = Array.Empty<string>();
            SerialPortComboBox.Text = enteredPort;
            SerialPortStatusText.Text = $"COM ports could not be enumerated ({exception.GetType().Name}). Type a COM name to save it.";
        }
    }

    private ConnectionType SelectedProtocol() => ProtocolComboBox.SelectedIndex switch
    {
        0 => ConnectionType.Ssh,
        1 => ConnectionType.Telnet,
        2 => ConnectionType.Serial,
        3 => ConnectionType.RawTcp,
        _ => throw new ArgumentException("Select a connection protocol."),
    };

    private void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        ValidationErrorText.IsVisible = false;
        try
        {
            ConnectionType type = SelectedProtocol();
            string? portText = type == ConnectionType.Serial
                ? SerialBaudRateComboBox.Text : PortTextBox.Text;
            if (!int.TryParse(portText, out int port))
                throw new ArgumentException(type == ConnectionType.Serial
                    ? "Baud rate must be a valid number."
                    : "Port must be a number between 1 and 65535.");
            if (type == ConnectionType.Telnet && TelnetAcknowledgementCheckBox.IsChecked != true)
                throw new ArgumentException("Acknowledge that Telnet is plaintext before saving.");
            Guid? credentialId = null;
            if (type == ConnectionType.Ssh)
            {
                if (CredentialComboBox.SelectedItem is not CredentialProfile credential)
                    throw new ArgumentException("Create and select a credential profile for SSH.");
                credentialId = credential.Id;
            }
            string encoding = type switch
            {
                ConnectionType.Telnet => TelnetEncodingComboBox.SelectedItem?.ToString() ?? "Utf8",
                ConnectionType.Serial => SerialEncodingComboBox.SelectedItem?.ToString() ?? "Utf8",
                ConnectionType.RawTcp => RawEncodingComboBox.SelectedItem?.ToString() ?? "Utf8",
                _ => "Utf8",
            };
            string timeoutText = type == ConnectionType.Telnet
                ? TelnetTimeoutTextBox.Text ?? string.Empty
                : RawTimeoutTextBox.Text ?? string.Empty;
            int timeout = type is ConnectionType.Telnet or ConnectionType.RawTcp
                ? int.TryParse(timeoutText, out int parsed) ? parsed
                    : throw new ArgumentException("Connect timeout must be a valid number.")
                : 15;
            var request = new DeviceEditRequest(
                _deviceId,
                NameTextBox.Text ?? string.Empty,
                DescriptionTextBox.Text ?? string.Empty,
                type == ConnectionType.Serial
                    ? SerialPortComboBox.Text ?? string.Empty
                    : HostTextBox.Text ?? string.Empty,
                port,
                DeviceTypeComboBox.SelectedItem is DeviceType deviceType ? deviceType : DeviceType.Other,
                _vendor,
                _model,
                SiteTextBox.Text ?? string.Empty,
                GroupTextBox.Text ?? string.Empty,
                EnvironmentComboBox.SelectedItem is DeviceEnvironment environment
                    ? environment : DeviceEnvironment.Unknown,
                credentialId,
                type == ConnectionType.Ssh ? UsernameOverrideTextBox.Text : null,
                FavoriteCheckBox.IsChecked == true,
                NotesTextBox.Text ?? string.Empty)
            {
                ConnectionType = type,
                TerminalType = TerminalTypeTextBox.Text ?? string.Empty,
                Encoding = encoding,
                ConnectTimeoutSeconds = timeout,
                SerialDataBits = SerialDataBitsComboBox.SelectedItem is int dataBits ? dataBits : 8,
                SerialParity = SerialParityComboBox.SelectedItem?.ToString() ?? "None",
                SerialStopBits = SerialStopBitsComboBox.SelectedItem?.ToString() ?? "One",
                SerialFlowControl = SerialFlowControlComboBox.SelectedItem?.ToString() ?? "None",
                RawLineEnding = RawLineEndingComboBox.SelectedItem?.ToString() ?? "CarriageReturnLineFeed",
                TelnetAcknowledged = type == ConnectionType.Telnet && TelnetAcknowledgementCheckBox.IsChecked == true,
            };
            _ = new DeviceProfile(
                request.Id ?? Guid.NewGuid(),
                request.Name,
                request.Description,
                new SavedDeviceConnectionSettings(type, request.Host, request.Port,
                    request.TerminalType, request.Encoding, request.ConnectTimeoutSeconds,
                    request.SerialDataBits, request.SerialParity, request.SerialStopBits,
                    request.SerialFlowControl, request.RawLineEnding, request.TelnetAcknowledged),
                request.DeviceType, request.Vendor, request.Model, null, null, request.Environment,
                request.CredentialProfileId, request.UsernameOverride, request.IsFavorite,
                request.Notes, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
            TerminalTextEncoding selectedEncoding = Enum.Parse<TerminalTextEncoding>(request.Encoding);
            switch (type)
            {
                case ConnectionType.Telnet:
                    _ = new TelnetConnectionOptions(request.Host, request.Port,
                        request.TerminalType, selectedEncoding, request.ConnectTimeoutSeconds);
                    break;
                case ConnectionType.Serial:
                    _ = new SerialConnectionOptions(request.Host, request.Port,
                        request.SerialDataBits, Enum.Parse<SerialParity>(request.SerialParity),
                        Enum.Parse<SerialStopBits>(request.SerialStopBits),
                        Enum.Parse<SerialFlowControl>(request.SerialFlowControl), selectedEncoding);
                    break;
                case ConnectionType.RawTcp:
                    _ = new RawTcpConnectionOptions(request.Host, request.Port, selectedEncoding,
                        Enum.Parse<TerminalLineEnding>(request.RawLineEnding),
                        request.ConnectTimeoutSeconds);
                    break;
            }
            Close(request);
        }
        catch (ArgumentException exception)
        {
            ValidationErrorText.Text = exception.Message;
            ValidationErrorText.IsVisible = true;
        }
    }
}
