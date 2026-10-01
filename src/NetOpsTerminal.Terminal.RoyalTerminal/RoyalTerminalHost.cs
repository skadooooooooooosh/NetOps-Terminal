using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NetOpsTerminal.Terminal.Abstractions;
using RoyalTerminal.Avalonia.Controls;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

public sealed class RoyalTerminalHost : ContentControl
{
    private TerminalControl? _subscribedControl;
    private bool _pasteInProgress;

    public RoyalTerminalHost()
    {
        AddHandler(
            PointerPressedEvent,
            TerminalPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            KeyDownEvent,
            BlockReservedOrLockedKeyInput,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            KeyUpEvent,
            BlockReservedOrLockedKeyInput,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            TextInputEvent,
            BlockLockedTextInput,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public static readonly StyledProperty<ITerminalSession?> SessionProperty =
        AvaloniaProperty.Register<RoyalTerminalHost, ITerminalSession?>(nameof(Session));

    public ITerminalSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public static readonly StyledProperty<bool> BackspaceSendsControlHProperty =
        AvaloniaProperty.Register<RoyalTerminalHost, bool>(nameof(BackspaceSendsControlH));

    public bool BackspaceSendsControlH
    {
        get => GetValue(BackspaceSendsControlHProperty);
        set => SetValue(BackspaceSendsControlHProperty, value);
    }

    public static readonly StyledProperty<bool> BackspaceSendsDeleteProperty =
        AvaloniaProperty.Register<RoyalTerminalHost, bool>(nameof(BackspaceSendsDelete));

    public bool BackspaceSendsDelete
    {
        get => GetValue(BackspaceSendsDeleteProperty);
        set => SetValue(BackspaceSendsDeleteProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty)
        {
            DetachTerminalControl();
            Content = Session switch
            {
                IRoyalTerminalSession session => session.Control,
                null => null,
                _ => new TextBlock
                {
                    Text = "This terminal session has no compatible renderer.",
                    Margin = new Thickness(16),
                },
            };

            AttachTerminalControl();
            RequestTerminalFocus();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachTerminalControl();
        RequestTerminalFocus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachTerminalControl();
        base.OnDetachedFromVisualTree(e);
    }

    public void RequestTerminalFocus()
    {
        Dispatcher.UIThread.Post(
            () => _ = FocusTerminal(),
            DispatcherPriority.Loaded);
    }

    internal bool FocusTerminal()
    {
        bool hasTerminalControl = Content is TerminalControl;
        bool isWindowActive = TopLevel.GetTopLevel(this) is Window { IsActive: true };
        if (!TerminalFocusPolicy.CanFocus(
                this.IsAttachedToVisualTree(),
                IsEffectivelyVisible,
                isWindowActive,
                hasTerminalControl))
        {
            return false;
        }

        return ((TerminalControl)Content!).Focus(
            NavigationMethod.Unspecified,
            KeyModifiers.None);
    }

    private void AttachTerminalControl()
    {
        if (Content is not TerminalControl control
            || ReferenceEquals(_subscribedControl, control))
        {
            return;
        }

        DetachTerminalControl();
        _subscribedControl = control;
        control.SelectionFinalized += TerminalSelectionFinalized;
    }

    private void DetachTerminalControl()
    {
        if (_subscribedControl is null)
        {
            return;
        }

        _subscribedControl.SelectionFinalized -= TerminalSelectionFinalized;
        _subscribedControl = null;
    }

    private void TerminalSelectionFinalized(object? sender, EventArgs e)
    {
        if (sender is TerminalControl { HasSelection: true } control)
        {
            _ = CopySelectionSafelyAsync(control);
        }
    }

    private async void TerminalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_pasteInProgress
            || Content is not TerminalControl control
            || !e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
        {
            return;
        }

        e.Handled = true;
        if (Session?.IsInputLocked == true)
        {
            return;
        }

        _pasteInProgress = true;
        try
        {
            _ = control.Focus(NavigationMethod.Pointer, e.KeyModifiers);
            await control.PasteAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Terminal clipboard paste failed: {exception.GetType().Name}");
        }
        finally
        {
            _pasteInProgress = false;
        }
    }

    private void BlockReservedOrLockedKeyInput(object? sender, KeyEventArgs e)
    {
        if (TerminalKeyRoutingPolicy.ShouldBlock(
                e.Key,
                e.KeyModifiers,
                Session?.IsInputLocked == true))
        {
            e.Handled = true;
            return;
        }

        string? backspaceInput = TerminalKeyRoutingPolicy.GetBackspaceInput(
            e.Key, e.KeyModifiers, Session?.IsInputLocked == true,
            BackspaceSendsControlH, BackspaceSendsDelete);
        if (Content is TerminalControl control && backspaceInput is not null)
        {
            if (e.RoutedEvent == KeyDownEvent)
            {
                control.SendInput(backspaceInput);
            }

            e.Handled = true;
        }
    }

    private void BlockLockedTextInput(object? sender, TextInputEventArgs e)
    {
        if (Session?.IsInputLocked == true)
        {
            e.Handled = true;
        }
    }

    private static async Task CopySelectionSafelyAsync(TerminalControl control)
    {
        try
        {
            await control.CopySelectionAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Terminal selection copy failed: {exception.GetType().Name}");
        }
    }
}
