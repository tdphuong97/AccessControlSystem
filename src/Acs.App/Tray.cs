using Acs.Core.Host;
using Forms = System.Windows.Forms;

namespace Acs.App;

/// <summary>Tray icon with the Mở / Thoát menu, and the few balloons the app shows (transitions only).</summary>
sealed class Tray : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    bool _hinted;

    // State kept between devices.changed events so only changes produce a balloon.
    readonly Dictionary<int, bool> _online = new();
    readonly Dictionary<int, bool> _paused = new();
    int? _pending;
    int? _failed;

    public Tray(Action open, Action quit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mở", null, (_, _) => open());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => quit());
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/acs.ico")).Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Access Control System",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
        _icon.BalloonTipClicked += (_, _) => open();
    }

    public void HintTray()
    {
        if (_hinted) return;
        _hinted = true;
        Balloon("Access Control System", "Phần mềm vẫn chạy ở khay hệ thống để gửi dữ liệu còn chờ.");
    }

    public void Balloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text.Length > 200 ? text[..200] : text;
        _icon.ShowBalloonTip(4000);
    }

    /// <summary>Called on the UI thread for every host event.</summary>
    public void OnEmitted(string name, object? data)
    {
        if (name != "devices.changed" || data is not IEnumerable<DeviceView> devices) return;
        var list = devices.ToList();

        string? message = null;
        foreach (var d in list)
        {
            var known = _online.TryGetValue(d.Id, out var was);
            var wasPaused = _paused.TryGetValue(d.Id, out var p) && p;
            // A wrong password also reads as "offline" (Online is false while paused): say what it really is.
            if (d.AuthPaused)
            {
                if (known && !wasPaused) message = AuthPausedText(d.Name);
            }
            else if (known && was != d.Online)
                message = d.Online ? $"{d.Name} đã kết nối lại." : $"{d.Name} mất kết nối. Dữ liệu sẽ được gửi khi máy online lại.";
            _online[d.Id] = d.Online;
            _paused[d.Id] = d.AuthPaused;
        }

        var pending = list.Sum(d => d.Pending);
        var failed = list.Sum(d => d.Failed);
        if (_failed is { } f && failed > f)
            message = "Có dữ liệu gửi bị lỗi. Mở phần mềm để xem và thử lại.";
        else if (_pending is > 0 && pending == 0 && failed == 0)
            message = "Đã gửi xong mọi dữ liệu còn chờ.";
        _pending = pending;
        _failed = failed;

        if (message is not null) Balloon("Access Control System", message);
    }

    internal static string AuthPausedText(string device) =>
        $"{device}: sai mật khẩu hoặc máy đang khoá tạm. Đã dừng gửi; mở phần mềm, vào tab \"Máy chấm công & cập nhật\" để sửa mật khẩu.";

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
