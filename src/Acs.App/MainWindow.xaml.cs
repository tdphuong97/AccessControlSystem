using System.ComponentModel;
using System.Windows;
using Acs.Core.Host;

namespace Acs.App;

public partial class MainWindow : Window
{
    readonly App _app;
    bool _revealed;
    internal WebHost Bridge { get; }

    internal MainWindow(App app, AppHost host, bool startHidden)
    {
        _app = app;
        InitializeComponent();
        Bridge = new WebHost(Web, host.HandleAsync);
        // The whole page grows and shrinks with the window (UiScale: 100% at the default size, 80–150%).
        Web.SizeChanged += (_, e) => Web.ZoomFactor = UiScale.Fit(e.NewSize.Width, e.NewSize.Height);
        if (startHidden)
        {
            // The browser control only starts once the window has been shown, so show it
            // unnoticed (minimized, no taskbar button) and hide it as soon as it has rendered.
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowState = WindowState.Minimized;
            ContentRendered += (_, _) =>
            {
                if (!_revealed) Hide();
            };
        }
        Loaded += async (_, _) => await Bridge.LoadAsync("index.html");
        // Dữ liệu chung: the PC whose window is open sends to the devices.
        IsVisibleChanged += (_, _) => host.SetWindowActive(IsVisible && WindowState != WindowState.Minimized);
        StateChanged += (_, _) => host.SetWindowActive(IsVisible && WindowState != WindowState.Minimized);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing hides to the tray so queued data keeps being sent.
        if (!_app.Exiting)
        {
            e.Cancel = true;
            Hide();
            _app.Tray?.HintTray();
            return;
        }
        base.OnClosing(e);
    }

    public void Reveal()
    {
        _revealed = true;
        ShowInTaskbar = true;
        ShowActivated = true;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;  // bring forward even when another app has focus
        Topmost = false;
        Focus();
    }
}
