using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Acs.Core;
using Acs.Core.Hik;
using Acs.Core.Host;
using Acs.Core.Security;
using Microsoft.Win32;

namespace Acs.App;

public partial class App : Application
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "AccessControlSystem";

    Mutex? _single;
    EventWaitHandle? _show;
    AppHost? _host;
    MainWindow? _window;
    bool _stopping;

    internal Tray? Tray { get; private set; }
    internal bool Exiting { get; private set; }

    static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(v)) return "0.9.0";
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // With ACS_DATA (tests, dev) the names carry a suffix so a test copy never collides with the real one.
        var suffix = AppPaths.DataOverride is null ? "" : "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(AppPaths.Data).ToLowerInvariant())), 0, 4);
        _single = new Mutex(true, $@"Local\AccessControlSystem-{Environment.UserName}{suffix}", out var first);
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\AccessControlSystem-show-{Environment.UserName}{suffix}");
        if (!first)
        {
            // Already running: ask that window to come forward, then leave.
            try { _show.Set(); } catch (Exception) { }
            _show.Dispose();
            _show = null;
            _single.Dispose();
            _single = null;
            Exiting = true;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show("Phần mềm gặp lỗi. Bạn có thể tiếp tục dùng; nếu lỗi lặp lại, hãy khởi động lại phần mềm.\n\nChi tiết: " + ex.Exception.Message,
                "Access Control System", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        // Windows shutting down, or the installer replacing the app: save and leave.
        SessionEnding += (_, _) =>
        {
            Exiting = true;
            try { Task.Run(StopHostAsync).Wait(TimeSpan.FromSeconds(4)); } catch (Exception) { }
        };

        var minimized = e.Args.Contains("--minimized");
        var updated = e.Args.Contains("--updated");

        try
        {
            _host = AppHost.Create(AppPaths.Data, new DpapiKeyProtector(), new HikDeviceFactory(), TimeProvider.System, Version);
            _host.RunInstaller = RunInstallerAsync;
            _host.ApplyStartWithWindows = ApplyStartWithWindows;
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không khởi động được phần mềm. Hãy đóng mọi cửa sổ Access Control System rồi mở lại.\n\nChi tiết: " + ex.Message,
                "Access Control System", MessageBoxButton.OK, MessageBoxImage.Error);
            Exiting = true;
            Shutdown();
            return;
        }

        Tray = new Tray(() => _window?.Reveal(), () => _ = QuitAsync());
        _window = new MainWindow(this, _host, startHidden: minimized);
        _host.Emitted += (name, data) => Dispatcher.BeginInvoke(() =>
        {
            Tray?.OnEmitted(name, data);
            _window?.Bridge.Emit(name, data);
            if (name == "app.restart") _ = RestartAsync();
        });
        _window.Show();

        var waiter = new Thread(() =>
        {
            while (_show!.WaitOne())
            {
                if (Exiting) return;
                Dispatcher.BeginInvoke(() => _window?.Reveal());
            }
        }) { IsBackground = true, Name = "show-signal" };
        waiter.Start();

        if (updated) Tray.Balloon("Access Control System", $"Đã cập nhật lên {Version}");
    }

    Task StopHostAsync()
    {
        if (_host is null || _stopping) return Task.CompletedTask;
        _stopping = true;
        return _host.StopAsync();
    }

    async Task QuitAsync()
    {
        Exiting = true;
        try { await StopHostAsync(); } catch (Exception) { }
        Shutdown();
    }

    /// <summary>The single-instance mutex is owned by the UI thread, so it must be released there.</summary>
    void ReleaseOnUiThread(Action after)
    {
        Dispatcher.Invoke(() =>
        {
            ReleaseSingleInstance();
            after();
        });
    }

    /// <summary>After backup.restore the host is already stopped: start a fresh copy of the exe and leave.</summary>
    async Task RestartAsync()
    {
        Exiting = true;
        try { await StopHostAsync(); } catch (Exception) { }
        ReleaseOnUiThread(() =>
        {
            try
            {
                if (Environment.ProcessPath is { } exe)
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception) { }
            Shutdown();
        });
    }

    async Task RunInstallerAsync(string path)
    {
        // Start the installer first: if that fails (UAC cancelled, file missing) the app just keeps running.
        try
        {
            Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            throw new HostException("Không chạy được bộ cài cập nhật. Hãy thử lại hoặc tải bản mới trên trang phát hành.");
        }
        Exiting = true;
        try { await StopHostAsync(); } catch (Exception) { }
        ReleaseOnUiThread(Shutdown);
    }

    static void ApplyStartWithWindows(bool on)
    {
        if (on && Environment.ProcessPath is null)
        {
            Debug.WriteLine("ApplyStartWithWindows: ProcessPath is null, skipped.");
            return;
        }
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --minimized");
        else key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    void ReleaseSingleInstance()
    {
        try { _single?.ReleaseMutex(); } catch (Exception) { }
        _single?.Dispose();
        _single = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();
        ReleaseSingleInstance();
        _show?.Dispose();
        base.OnExit(e);
    }
}
