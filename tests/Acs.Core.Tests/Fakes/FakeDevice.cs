using System.Runtime.CompilerServices;
using Acs.Core.Hik;

namespace Acs.Core.Tests.Fakes;

/// <summary>
/// Thread-safe in-memory Hikvision terminal. Fingerprint group convention: a template string is
/// "&lt;group&gt;:&lt;data&gt;" (e.g. "ModelA:AAAA"). When <see cref="AcceptOnlyGroup"/> is true and the prefix differs
/// from <see cref="FpGroup"/>, SetFingerprintAsync throws HikRejectedException("fpMismatch").
/// </summary>
public sealed class FakeDevice : IHikDevice
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HikUserWrite> _users = new();
    private readonly Dictionary<string, byte[]> _faces = new();
    private readonly Dictionary<(string No, int Id), string> _fingers = new();
    private readonly List<string> _calls = [];
    private int _running, _maxRunning;

    public volatile bool Offline;
    public volatile bool WrongPassword;
    public volatile bool RejectFace;
    public volatile bool AcceptOnlyGroup;
    public volatile bool ForgetWrites;
    /// <summary>The next N fingerprint reads return only the first finger (a read cut short, as on site).</summary>
    public int ShortReads;
    /// <summary>When set, ListUsersAsync waits for it: keeps an import or a plan running while a test looks.</summary>
    public TaskCompletionSource? HoldList;
    public string FpGroup { get; set; } = "ModelA";
    public byte[] NextFace { get; set; } = [0xFF, 0xD8, 0xFF, 0xD9];
    public CapturedFinger NextFinger { get; set; } = new("ModelA:AAAA", 80);
    public TimeSpan CaptureDelay { get; set; } = TimeSpan.Zero;
    public DeviceInfo Info { get; set; } = new("DS-K1T343", "FAKE0001", "V1.0");
    public DeviceCapabilities Capabilities { get; set; } = new(true, true, 3000);

    public IReadOnlyList<string> Calls { get { lock (_gate) return [.. _calls]; } }
    public int ConcurrentCaptures { get { lock (_gate) return _maxRunning; } }

    private void Enter(string name)
    {
        lock (_gate) _calls.Add(name);
        if (Offline) throw new HikTransientException("Thiết bị không phản hồi. Kiểm tra nguồn và mạng rồi thử lại.");
        if (WrongPassword) throw new HikAuthException("Sai tên đăng nhập hoặc mật khẩu thiết bị. Vào Thiết bị để sửa lại.");
    }

    private async Task Capturing(CancellationToken ct)
    {
        lock (_gate) { _running++; if (_running > _maxRunning) _maxRunning = _running; }
        try { if (CaptureDelay > TimeSpan.Zero) await Task.Delay(CaptureDelay, ct); }
        finally { lock (_gate) _running--; }
    }

    private HikUser ToUser(HikUserWrite w) => new(w.EmployeeNo, w.Name, w.BeginTime, w.EndTime, w.Enabled,
        _faces.ContainsKey(w.EmployeeNo) ? 1 : 0, _fingers.Keys.Count(k => k.No == w.EmployeeNo));

    public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct) { Enter(nameof(GetDeviceInfoAsync)); return Task.FromResult(Info); }
    public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct) { Enter(nameof(GetCapabilitiesAsync)); return Task.FromResult(Capabilities); }

    public async IAsyncEnumerable<HikUser> ListUsersAsync([EnumeratorCancellation] CancellationToken ct)
    {
        Enter(nameof(ListUsersAsync));
        if (HoldList is { } hold) await hold.Task;
        List<HikUser> snap;
        lock (_gate) snap = _users.Values.Select(ToUser).ToList();
        foreach (var u in snap) { yield return u; await Task.Yield(); }
    }

    public Task<HikUser?> FindUserAsync(string employeeNo, CancellationToken ct)
    {
        Enter(nameof(FindUserAsync));
        lock (_gate) return Task.FromResult(_users.TryGetValue(employeeNo, out var w) ? ToUser(w) : null);
    }

    public Task UpsertUserAsync(HikUserWrite user, CancellationToken ct)
    {
        Enter(nameof(UpsertUserAsync));
        lock (_gate) { if (!ForgetWrites) _users[user.EmployeeNo] = user; }
        return Task.CompletedTask;
    }

    public Task DeleteUserAsync(string employeeNo, CancellationToken ct)
    {
        Enter(nameof(DeleteUserAsync));
        lock (_gate)
        {
            _users.Remove(employeeNo);
            _faces.Remove(employeeNo);
            foreach (var k in _fingers.Keys.Where(k => k.No == employeeNo).ToList()) _fingers.Remove(k);
        }
        return Task.CompletedTask;
    }

    public async Task<byte[]> CaptureFaceAsync(CancellationToken ct)
    {
        Enter(nameof(CaptureFaceAsync));
        await Capturing(ct);
        return NextFace;
    }

    public Task SetFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct)
    {
        Enter(nameof(SetFaceAsync));
        if (RejectFace) throw new HikRejectedException("faceModelingFailed", "Thiết bị không nhận ảnh khuôn mặt này. Chụp lại ảnh rõ mặt, đủ sáng.");
        lock (_gate) { if (!ForgetWrites) _faces[employeeNo] = jpeg; }
        return Task.CompletedTask;
    }

    public Task<bool> HasFaceAsync(string employeeNo, CancellationToken ct)
    {
        Enter(nameof(HasFaceAsync));
        lock (_gate) return Task.FromResult(_faces.ContainsKey(employeeNo));
    }

    public Task<byte[]?> DownloadFaceAsync(string employeeNo, CancellationToken ct)
    {
        Enter(nameof(DownloadFaceAsync));
        lock (_gate) return Task.FromResult(_faces.TryGetValue(employeeNo, out var b) ? b : null);
    }

    public Task DeleteFaceAsync(string employeeNo, CancellationToken ct)
    {
        Enter(nameof(DeleteFaceAsync));
        lock (_gate) _faces.Remove(employeeNo);
        return Task.CompletedTask;
    }

    public async Task<CapturedFinger> CaptureFingerprintAsync(int fingerId, CancellationToken ct)
    {
        Enter(nameof(CaptureFingerprintAsync));
        await Capturing(ct);
        return NextFinger;
    }

    public Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct)
    {
        Enter(nameof(SetFingerprintAsync));
        var i = templateBase64.IndexOf(':');
        var group = i >= 0 ? templateBase64[..i] : "";
        if (AcceptOnlyGroup && group != FpGroup)
            throw new HikRejectedException("fpMismatch", "Mẫu vân tay không khớp loại cảm biến của thiết bị này. Đăng ký lại vân tay trên chính thiết bị.");
        lock (_gate) { if (!ForgetWrites) _fingers[(employeeNo, fingerId)] = templateBase64; }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct)
    {
        Enter(nameof(ReadFingerprintsAsync));
        lock (_gate)
        {
            var all = _fingers.Where(kv => kv.Key.No == employeeNo).OrderBy(kv => kv.Key.Id).Select(kv => new HikFinger(kv.Key.Id, kv.Value)).ToList();
            if (ShortReads > 0 && all.Count > 1) { ShortReads--; all = all.Take(1).ToList(); }
            return Task.FromResult<IReadOnlyList<HikFinger>>(all);
        }
    }

    public Task DeleteFingerprintAsync(string employeeNo, int fingerId, CancellationToken ct)
    {
        Enter(nameof(DeleteFingerprintAsync));
        lock (_gate) _fingers.Remove((employeeNo, fingerId));
        return Task.CompletedTask;
    }
}
