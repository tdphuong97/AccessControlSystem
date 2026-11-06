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
