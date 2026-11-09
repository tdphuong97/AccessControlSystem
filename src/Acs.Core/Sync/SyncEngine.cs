using Acs.Core.Hik;
using Acs.Core.Storage;

namespace Acs.Core.Sync;

public sealed record DeviceStatus(int DeviceId, bool Online, bool AuthPaused, DateTime? LastSeen, int Pending, int Failed, string? Message);

/// <summary>Owns one <see cref="DeviceWorker"/> per device and exposes status and change events.</summary>
public sealed class SyncEngine(Database db, IHikDeviceFactory factory, TimeProvider time) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (DeviceWorker Worker, Task Run)> _workers = new();
    private CancellationTokenSource? _cts;

    public const string OtherSenderMessage = "Máy tính khác đang gửi xuống máy chấm công.";
    private volatile bool _notSender;

    /// <summary>False while another PC holds the Dữ liệu chung sender lease (this PC's workers are stopped).</summary>
    public bool IsSender => !_notSender;

    public event Action<int>? DeviceChanged;
    public event Action<string>? PersonChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            db.Jobs.ResetRunning();
            _cts = new CancellationTokenSource();
            EnsureWorkers();
        }
    }

    public async Task StopAsync()
    {
        List<Task> runs;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            runs = _workers.Values.Select(w => w.Run).ToList();
            _workers.Clear();
        }
        if (cts is null) return;
        await cts.CancelAsync();
        try { await Task.WhenAll(runs); } catch (Exception) { /* workers log their own faults; stopping must not throw */ }
        cts.Dispose();
    }

    /// <summary>
    /// Dữ liệu chung: only the sender lease holder runs the send queue. False stops the workers (devices show
    /// <see cref="OtherSenderMessage"/>); true starts them again, which puts jobs left "running" back to pending.
    /// </summary>
    public async Task SetSenderAsync(bool sender)
    {
        if (sender == !_notSender && (sender == (_cts is not null))) return;
        _notSender = !sender;
        if (sender) Start();
        else await StopAsync();
        foreach (var d in db.Devices.List()) RaiseDevice(d.Id);
    }

    /// <summary>Wakes one device's worker (or all). Workers for devices added since Start are created here.</summary>
    public void Wake(int? deviceId = null)
    {
        List<DeviceWorker> targets;
        lock (_gate)
        {
            if (_cts is null) return;
            EnsureWorkers();
            targets = _workers.Where(w => deviceId is null || w.Key == deviceId).Select(w => w.Value.Worker).ToList();
        }
        foreach (var w in targets) w.Wake();
    }

    public IReadOnlyList<DeviceStatus> Status()
    {
        Dictionary<int, DeviceWorker> workers;
        lock (_gate) workers = _workers.ToDictionary(w => w.Key, w => w.Value.Worker);
        return db.Devices.List().Select(d =>
        {
            var (pending, failed) = db.Jobs.Counts(d.Id);
            workers.TryGetValue(d.Id, out var w);
            if (_notSender) return new DeviceStatus(d.Id, false, d.AuthPaused, d.LastSeen, pending, failed, OtherSenderMessage);
            var message = d.AuthPaused ? JobRules.AuthMessage : w?.Message;
            return new DeviceStatus(d.Id, w?.Online == true && !d.AuthPaused, d.AuthPaused, d.LastSeen, pending, failed, message);
        }).ToList();
    }

    private void EnsureWorkers()   // caller holds _gate
    {
        if (_cts is null) return;
        var ids = db.Devices.List().Select(d => d.Id).ToHashSet();
        foreach (var gone in _workers.Where(w => w.Value.Run.IsCompleted && !ids.Contains(w.Key)).Select(w => w.Key).ToList())
            _workers.Remove(gone);
        foreach (var id in ids)
        {
            if (_workers.TryGetValue(id, out var existing) && !existing.Run.IsCompleted) continue;
            var worker = new DeviceWorker(id, db, factory, time, RaiseDevice, RaisePerson);
            var token = _cts.Token;
            _workers[id] = (worker, Task.Run(() => worker.RunAsync(token), token));
        }
    }

    private void RaiseDevice(int id)
    {
        try { DeviceChanged?.Invoke(id); } catch { /* a listener must not stop the queue */ }
    }

    private void RaisePerson(string no)
    {
        try { PersonChanged?.Invoke(no); } catch { /* a listener must not stop the queue */ }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
