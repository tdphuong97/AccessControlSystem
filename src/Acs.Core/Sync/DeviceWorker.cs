using Acs.Core.Hik;
using Acs.Core.Model;
using Acs.Core.Storage;

namespace Acs.Core.Sync;

/// <summary>
/// One worker per device: runs its jobs one at a time, verifies each write by reading back, and pings the device
/// every 30 s while idle. All waiting goes through the TimeProvider, so tests can drive it with fake time.
/// </summary>
internal sealed class DeviceWorker
{
    /// <summary>A job outcome that is already decided (shown to the user as is).</summary>
    private sealed class JobFailure(string kind, string message, string? detail = null) : Exception(message)
    {
        public string Kind { get; } = kind;
        public string? Detail { get; } = detail;
    }

    private readonly int _id;
    private readonly Database _db;
    private readonly IHikDeviceFactory _factory;
    private readonly TimeProvider _time;
    private readonly Action<int> _deviceChanged;
    private readonly Action<string> _personChanged;

    private readonly object _gate = new();
    private bool _wake;
    private TaskCompletionSource? _wakeSignal;

    private (string, string, string, int)? _clientKey;
    private IHikDevice? _client;
    private DateTime _nextPing;

    private volatile bool _online;
    private volatile string? _message;

    public DeviceWorker(int id, Database db, IHikDeviceFactory factory, TimeProvider time, Action<int> deviceChanged, Action<string> personChanged)
    {
        _id = id; _db = db; _factory = factory; _time = time; _deviceChanged = deviceChanged; _personChanged = personChanged;
    }

    public bool Online => _online;
    public string? Message => _message;
    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public void Wake()
    {
        lock (_gate) { _wake = true; _wakeSignal?.TrySetResult(); }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _nextPing = Now;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var dev = _db.Devices.Get(_id);
                if (dev is null) return;
                if (dev.AuthPaused)
                {
                    SetStatus(false, JobRules.AuthMessage);
                    await WaitAsync(null, ct);
                    continue;
                }
                if (_message == JobRules.AuthMessage) SetStatus(_online, null);   // pause was cleared from outside

                if (_db.Jobs.NextDue(_id, Now) is { } job) { await ProcessAsync(dev, job, ct); continue; }
                if (Now >= _nextPing) { await PingAsync(dev, ct); continue; }

                var until = _nextPing;
                if (_db.Jobs.NextFuture(_id, Now) is { } f && f < until) until = f;
                await WaitAsync(until, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    // ---- waiting ----

    private async Task WaitAsync(DateTime? until, CancellationToken ct)
    {
        Task wakeTask;
        lock (_gate)
        {
            if (_wake) { _wake = false; return; }
            _wakeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wakeTask = _wakeSignal.Task;
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task delay;
        if (until is { } u)
        {
            var d = u - Now;
            delay = Task.Delay(d > TimeSpan.Zero ? d : TimeSpan.Zero, _time, cts.Token);
            if (Now >= u) cts.Cancel();   // fake time may have moved while the timer was being armed
        }
        else delay = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        await Task.WhenAny(wakeTask, delay);
        cts.Cancel();
        lock (_gate) { _wakeSignal = null; _wake = false; }
        ct.ThrowIfCancellationRequested();
    }

    private IHikDevice Client(DeviceRecord d)
    {
        var key = (d.Host, d.Username, d.Password, d.DoorNo);
        if (_client is null || _clientKey != key) { _client = _factory.Create(d); _clientKey = key; }
        return _client;
    }

    // ---- ping ----

    private async Task PingAsync(DeviceRecord dev, CancellationToken ct)
    {
        _nextPing = Now + JobRules.PingEvery;
        try
        {
            var info = await Client(dev).GetDeviceInfoAsync(ct);
            _db.Devices.SetSeen(_id, info, Now);
            var wasOnline = _online;
            SetStatus(true, null);
            if (!wasOnline) _db.Jobs.ExpediteRetries(_id, Now);
            _deviceChanged(_id);
        }
        catch (HikAuthException ex) { PauseForAuth(ex, null); }
        catch (HikException ex)
        {
            SetStatus(false, JobRules.OfflineMessage);
            _db.Log.Add(null, _id, "Ping", false, ex.Message);
            _deviceChanged(_id);
        }
    }

    // ---- jobs ----

    private async Task ProcessAsync(DeviceRecord dev, JobRecord job, CancellationToken ct)
    {
        _db.Jobs.MarkRunning(job.Id, Now);
        string? action = job.Kind.ToString();
        try
        {
            await ExecuteAsync(dev, job, Client(dev), ct);
            _db.Jobs.MarkDone(job.Id, Now);
            _db.Devices.SetSeen(_id, null, Now);
            _nextPing = Now + JobRules.PingEvery;
            SetStatus(true, null);
            _db.Log.Add(job.EmployeeNo, _id, action, true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _db.Jobs.Requeue(job.Id, Now);
            throw;
        }
        catch (JobFailure f)
        {
            _db.Jobs.Fail(job.Id, f.Kind, f.Message, f.Detail, Now);
            _db.Log.Add(job.EmployeeNo, _id, action, false, f.Message);
        }
        catch (HikTransientException ex)
        {
            _db.Jobs.Retry(job.Id, Now + Backoff.After(job.Attempts), "transient", ex.Message, ex.Detail, Now);
            _nextPing = Now + JobRules.PingEvery;
            SetStatus(false, JobRules.OfflineMessage);
            _db.Log.Add(job.EmployeeNo, _id, action, false, ex.Message);
        }
        catch (HikAuthException ex)
        {
            _db.Jobs.Requeue(job.Id, Now);
            PauseForAuth(ex, job.EmployeeNo);
            _personChanged(job.EmployeeNo);
            return;
        }
        catch (HikException ex)   // rejected, not supported
        {
            _db.Jobs.Fail(job.Id, "rejected", ex.Message, ex.Detail, Now);
            _db.Log.Add(job.EmployeeNo, _id, action, false, ex.Message);
        }
        catch (Exception ex)
        {
            _db.Jobs.Fail(job.Id, "unexpected", "Có lỗi không rõ khi gửi lên máy. Bấm Thử lại; nếu vẫn lỗi, xem Chi tiết.", ex.Message, Now);
            _db.Log.Add(job.EmployeeNo, _id, action, false, ex.Message);
        }
        _deviceChanged(_id);
        _personChanged(job.EmployeeNo);
    }

    private void PauseForAuth(HikAuthException ex, string? employeeNo)
    {
        _db.Devices.SetAuthPaused(_id, true);
        SetStatus(false, JobRules.AuthMessage);
        _db.Log.Add(employeeNo, _id, "Auth", false, ex.Message);
        _deviceChanged(_id);
    }

    private void SetStatus(bool online, string? message)
    {
        if (_online == online && _message == message) return;
        _online = online; _message = message;
        _deviceChanged(_id);
    }

    private async Task VerifyAsync(Func<Task<bool>> check, CancellationToken ct)
    {
        for (var i = 0; i < JobRules.VerifyReads; i++)
        {
            if (await check()) return;
            if (i < JobRules.VerifyReads - 1) await Task.Delay(JobRules.VerifyGap, _time, ct);
        }
        throw new JobFailure("not_saved", JobRules.NotSavedMessage);
    }

    private DeviceStateRecord State(string no) =>
        _db.Jobs.GetState(no, _id) ?? new DeviceStateRecord(no, _id, false, false, false, new HashSet<int>(), new HashSet<int>(), null);

    /// <summary>Writes device_state after a verified change (skipped when the person no longer exists in the app).</summary>
    private void Save(string no, Func<DeviceStateRecord, DeviceStateRecord> change)
    {
        if (_db.People.Get(no) is null) { _db.Jobs.DeleteState(no, _id); return; }
        _db.Jobs.SetState(change(State(no)) with { VerifiedAt = Now });
    }

    private async Task ExecuteAsync(DeviceRecord dev, JobRecord job, IHikDevice hik, CancellationToken ct)
    {
        var no = job.EmployeeNo;
        switch (job.Kind)
        {
            case JobKind.UpsertUser:
            {
                if (_db.People.Get(no) is not { } p) return;
                var enabled = p.Status == PersonStatus.Active;
                await WriteUserAsync(hik, p, dev.DoorNo, enabled, ct);
                Save(no, s => s with { UserPresent = true, UserEnabled = enabled });
                break;
            }
            case JobKind.DisableUser:
            {
                if (_db.People.Get(no) is not { } p) return;
                if (await hik.FindUserAsync(no, ct) is null) { Save(no, s => s with { UserPresent = false, UserEnabled = false }); return; }
                await WriteUserAsync(hik, p, dev.DoorNo, false, ct);
                Save(no, s => s with { UserPresent = true, UserEnabled = false });
                break;
            }
            case JobKind.DeleteUser:
                await hik.DeleteUserAsync(no, ct);
                await VerifyAsync(async () => await hik.FindUserAsync(no, ct) is null, ct);
                Save(no, s => s with { UserPresent = false, UserEnabled = false, FacePresent = false, Fingers = new HashSet<int>() });
                break;
            case JobKind.SetFace:
            {
                if (_db.Biometrics.GetFace(no) is not { } face || _db.People.Get(no) is null) return;
                await hik.SetFaceAsync(no, face.Jpeg, ct);
                await VerifyAsync(() => hik.HasFaceAsync(no, ct), ct);
                Save(no, s => s with { FacePresent = true });
                break;
            }
            case JobKind.DeleteFace:
                await hik.DeleteFaceAsync(no, ct);
                await VerifyAsync(async () => !await hik.HasFaceAsync(no, ct), ct);
                Save(no, s => s with { FacePresent = false });
                break;
            case JobKind.SetFinger:
                await SetFingerAsync(dev, job, hik, ct);
                break;
            case JobKind.DeleteFinger:
                await hik.DeleteFingerprintAsync(no, job.FingerId, ct);
                await VerifyAsync(async () => (await hik.ReadFingerprintsAsync(no, ct)).All(f => f.FingerId != job.FingerId), ct);
                Save(no, s => s with { Fingers = Without(s.Fingers, job.FingerId), NeedsScan = Without(s.NeedsScan, job.FingerId) });
                break;
        }
    }

    private async Task WriteUserAsync(IHikDevice hik, PersonRecord p, int doorNo, bool enabled, CancellationToken ct)
    {
        await hik.UpsertUserAsync(JobRules.ToWrite(p, doorNo, enabled), ct);
        await VerifyAsync(async () => await hik.FindUserAsync(p.EmployeeNo, ct) is { } u && u.Name == p.FullName && u.Enabled == enabled, ct);
    }

    private async Task SetFingerAsync(DeviceRecord dev, JobRecord job, IHikDevice hik, CancellationToken ct)
    {
        var no = job.EmployeeNo;
        if (_db.People.Get(no) is null) return;
        var rows = _db.Biometrics.GetFingers(no).Where(f => f.FingerId == job.FingerId).ToList();
        if (rows.Count == 0) return;

        var own = rows.FirstOrDefault(f => f.FpGroup == dev.FpGroup);
        if (own is not null)
            await hik.SetFingerprintAsync(no, job.FingerId, own.TemplateBase64, 1, ct);
        else
        {
            HikRejectedException? last = null;
            FingerRecord? used = null;
            foreach (var other in rows)
            {
                try
                {
                    await hik.SetFingerprintAsync(no, job.FingerId, other.TemplateBase64, 1, ct);
                    used = other;
                    break;
                }
                catch (HikRejectedException ex) { last = ex; }
            }
            if (used is null)
            {
                Save(no, s => s with { NeedsScan = With(s.NeedsScan, job.FingerId) });
                throw new JobFailure("needs_scan",
                    $"Cần quét {JobRules.FingerLabel(job.FingerId)} trực tiếp ở {dev.Name}.", last?.Detail ?? last?.Message);
            }
            _db.Biometrics.SetFinger(used with { FpGroup = dev.FpGroup });
        }
        await VerifyAsync(async () => (await hik.ReadFingerprintsAsync(no, ct)).Any(f => f.FingerId == job.FingerId), ct);
        Save(no, s => s with { Fingers = With(s.Fingers, job.FingerId), NeedsScan = Without(s.NeedsScan, job.FingerId) });
    }

    private static IReadOnlySet<int> With(IReadOnlySet<int> s, int id) => new HashSet<int>(s) { id };
    private static IReadOnlySet<int> Without(IReadOnlySet<int> s, int id) { var h = new HashSet<int>(s); h.Remove(id); return h; }
}
