namespace NetOpsTerminal.Core.Devices;

public enum DeviceAccessMode
{
    TerminalOnly,
    WebOnly,
    TerminalAndWeb,
}

public enum WebBrowserPreference
{
    WindowsDefault,
    MicrosoftEdge,
    GoogleChrome,
}

public enum DeviceOpenAction
{
    Terminal,
    Web,
}

public sealed record DeviceWebAccessSettings
{
    public DeviceWebAccessSettings(string address, WebBrowserPreference browser,
        DeviceOpenAction defaultAction)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 2048
            || address.Any(char.IsControl)
            || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter an HTTP or HTTPS address without sign-in details, query, or fragment.", nameof(address));
        if (!Enum.IsDefined(browser) || !Enum.IsDefined(defaultAction))
            throw new ArgumentException("Select a valid browser and opening action.");

        Address = uri.AbsoluteUri;
        Browser = browser;
        DefaultAction = defaultAction;
    }

    public string Address { get; }
    public WebBrowserPreference Browser { get; }
    public DeviceOpenAction DefaultAction { get; }
}
