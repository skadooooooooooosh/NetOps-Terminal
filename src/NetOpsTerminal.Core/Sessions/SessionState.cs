namespace NetOpsTerminal.Core.Sessions;

public enum SessionState
{
    Created,
    Connecting,
    Connected,
    Disconnecting,
    Disconnected,
    Reconnecting,
    Faulted,
    Disposed,
}
