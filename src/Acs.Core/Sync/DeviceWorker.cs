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

    /// <summary>What to log instead of the job kind when a job finished differently from its name.</summary>
    private sealed record Note(string Action, string Message);

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

    private bool _pingFailLogged;
    private bool _lostConnection;   // set only when connectivity was really lost
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
        string? lastFault = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await StepAsync(ct)) return;
                lastFault = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // Never let the worker fault: log once per distinct fault, wait, carry on.
                var fault = ex.GetType().Name + ": " + ex.Message;
                if (fault != lastFault)
                {
                    lastFault = fault;
                    try { _db.Log.Add(null, _id, "Worker", false, "Có lỗi không rõ ở luồng gửi của máy này. App sẽ tự thử lại sau 30 giây. Chi tiết: " + fault); } catch { /* the database itself may be failing */ }
                }
                try { await WaitAsync(Now + JobRules.PingEvery, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }
    }

    /// <summary>One pass of the loop; false when the device no longer exists.</summary>
    private async Task<bool> StepAsync(CancellationToken ct)
    {
        var dev = _db.Devices.Get(_id);
        if (dev is null) return false;
        if (dev.AuthPaused)
        {
            SetStatus(false, JobRules.AuthMessage);
            await WaitAsync(null, ct);
            return true;
        }
        if (_message == JobRules.AuthMessage) SetStatus(_online, null);   // pause was cleared from outside

        if (_db.Jobs.NextDue(_id, Now) is { } job) { await ProcessAsync(dev, job, ct); return true; }
        if (Now >= _nextPing) { await PingAsync(dev, ct); return true; }

        var until = _nextPing;
        if (_db.Jobs.NextFuture(_id, Now) is { } f && f < until) until = f;
        await WaitAsync(until, ct);
        return true;
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
            var cameBack = _lostConnection;
            _lostConnection = false;
            _pingFailLogged = false;
            SetStatus(true, null);
            if (cameBack) _db.Jobs.ExpediteRetries(_id, Now);   // connectivity really came back
            _deviceChanged(_id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HikAuthException ex) { PauseForAuth(ex, null); }
        catch (Exception ex)
        {
            _lostConnection = true;
            SetStatus(false, JobRules.OfflineMessage);
            if (!_pingFailLogged)   // log only the online -> offline transition, not every 30 s
            {
                _pingFailLogged = true;
                var msg = ex is HikException ? ex.Message : "Không kết nối được máy. Kiểm tra nguồn và mạng; app sẽ tự thử lại.";
                _db.Log.Add(null, _id, "Ping", false, msg);
            }
            _deviceChanged(_id);
        }
    }

    /// <summary>A network-level failure (not a 5xx or "busy" answer from a device that is reachable).</summary>
    private static bool IsConnectionLoss(Exception ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
            if (e is HttpRequestException or System.Net.Sockets.SocketException or TimeoutException or TaskCanceledException or IOException)
                return true;
        return false;
    }

    // ---- jobs ----

    private async Task ProcessAsync(DeviceRecord dev, JobRecord job, CancellationToken ct)
    {
        if (!_db.Jobs.MarkRunning(job, Now)) return;   // superseded since NextDue: do not send
        string action = job.Kind.ToString();
        try
        {
            var note = await ExecuteAsync(dev, job, Client(dev), ct);
            _db.Tx(() =>
            {
                _db.Jobs.MarkDone(job.Id, Now);
                _db.Devices.SetSeen(_id, null, Now);
                _db.Log.Add(job.EmployeeNo, _id, note?.Action ?? action, true, note?.Message);
                return 0;
            });
            _nextPing = Now + JobRules.PingEvery;
            SetStatus(true, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _db.Jobs.Requeue(job.Id, Now);
            throw;
        }
        catch (JobFailure f)
        {
            FailJob(job, action, f.Kind, f.Message, f.Detail);
        }
        catch (HikTransientException ex)
        {
            _db.Tx(() =>
            {
                _db.Jobs.Retry(job.Id, Now + Backoff.After(job.Attempts), "transient", ex.Message, ex.Detail, Now);
                _db.Log.Add(job.EmployeeNo, _id, action, false, ex.Message);
                return 0;
            });
            // A 5xx/busy answer does not mean the device is offline; only a lost connection does.
            // Otherwise probe with a ping right away: if it fails the device is marked offline there.
            _nextPing = Now;
            if (IsConnectionLoss(ex)) { _lostConnection = true; SetStatus(false, JobRules.OfflineMessage); }
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
            FailJob(job, action, "rejected", ex.Message, ex.Detail);
        }
        catch (Exception ex)
        {
            FailJob(job, action, "unexpected", "Có lỗi không rõ khi gửi lên máy. Bấm Thử lại; nếu vẫn lỗi, xem Chi tiết.",
                ex.GetType().Name + ": " + ex.Message);
        }
        _deviceChanged(_id);
        _personChanged(job.EmployeeNo);
    }

    private void FailJob(JobRecord job, string action, string kind, string message, string? detail) => _db.Tx(() =>
    {
        _db.Jobs.Fail(job.Id, kind, message, detail, Now);
        _db.Log.Add(job.EmployeeNo, _id, action, false, message);
        return 0;
    });

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

    private async Task<Note?> ExecuteAsync(DeviceRecord dev, JobRecord job, IHikDevice hik, CancellationToken ct)
    {
        var no = job.EmployeeNo;
        switch (job.Kind)
        {
            case JobKind.UpsertUser:
            {
                if (_db.People.Get(no) is not { } p) return null;
                // Never confirmed here, and the device already has this number under another name: that is someone
                // else (a reused or device-only number). Merging would hand them this person's access, or the other
                // way round, so stop and let the admin decide (So khớp writes the state row once they have looked).
                if (_db.Jobs.GetState(no, _id) is null && await hik.FindUserAsync(no, ct) is { } other && !SameName(other.Name, p.FullName))
                    throw new JobFailure("conflict", JobRules.NumberTakenMessage(other.Name));
                var enabled = p.Status == PersonStatus.Active;
                await WriteUserAsync(hik, p, dev.DoorNo, enabled, ct);
                Save(no, s => s with { UserPresent = true, UserEnabled = enabled });
                break;
            }
            case JobKind.DisableUser:
            {
                if (_db.People.Get(no) is not { } p) return null;
                if (await hik.FindUserAsync(no, ct) is null) { Save(no, s => s with { UserPresent = false, UserEnabled = false }); return null; }
                await WriteUserAsync(hik, p, dev.DoorNo, false, ct);
                Save(no, s => s with { UserPresent = true, UserEnabled = false });
                break;
            }
            case JobKind.DeleteUser:
            {
                // A locked person still in the app (the Lock rule): check the device itself before deleting. Explicit
                // deletes (people.delete, stranger "Gỡ khỏi máy") have no app person and are not affected.
                if (_db.People.Get(no) is { Status: PersonStatus.Locked } locked && await KeepLockedAsync(dev, hik, locked, ct) is { } kept)
                    return kept;
                try { await hik.DeleteUserAsync(no, ct); }
                catch (HikRejectedException ex) when (HikErrorMap.IsNotExist(ex)) { /* already gone: the read-back below confirms it */ }
                await VerifyAsync(async () => await hik.FindUserAsync(no, ct) is null, ct);
                Save(no, s => s with { UserPresent = false, UserEnabled = false, FacePresent = false, Fingers = new HashSet<int>(), FingersKnown = true });
                break;
            }
            case JobKind.SetFace:
            {
                if (_db.Biometrics.GetFace(no) is not { } face || _db.People.Get(no) is null) return null;
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
            {
                await hik.DeleteFingerprintAsync(no, job.FingerId, ct);
                IReadOnlyList<HikFinger> seen = [];
                await VerifyAsync(async () => (seen = await hik.ReadFingerprintsAsync(no, ct)).All(f => f.FingerId != job.FingerId), ct);
                // The read-back is the device's complete list for this person.
                Save(no, s => s with { Fingers = seen.Select(f => f.FingerId).ToHashSet(), FingersKnown = true, NeedsScan = Without(s.NeedsScan, job.FingerId) });
                break;
            }
        }
        return null;
    }

    /// <summary>
    /// Lock rule on the device: when it holds a face the app has no copy of, or a finger the app has no template for
    /// in THIS device's sensor group, the person is only disabled (null = safe to delete). Spec 5.7: never lose the
    /// only copy of a person's biometrics.
    /// </summary>
    private async Task<Note?> KeepLockedAsync(DeviceRecord dev, IHikDevice hik, PersonRecord p, CancellationToken ct)
    {
        var no = p.EmployeeNo;
        var u = await hik.FindUserAsync(no, ct);
        if (u is null) return null;   // already gone: the delete below is verified as usual
        var own = _db.Biometrics.FingerGroups(no).Where(f => f.FpGroup == dev.FpGroup).Select(f => f.FingerId).ToHashSet();
        IReadOnlyList<int>? onDevice = null;
        try { onDevice = (await hik.ReadFingerprintsAsync(no, ct)).Select(f => f.FingerId).ToList(); }
        catch (Exception e) when (e is HikRejectedException or HikNotSupportedException) { /* unknown: judge by the count */ }
        var deviceOnly = (u.FaceCount > 0 && !_db.Biometrics.HasFace(no))
            || u.FingerCount > own.Count
            || (onDevice is not null && onDevice.Any(f => !own.Contains(f)));
        if (!deviceOnly) return null;
        if (u.Enabled) await WriteUserAsync(hik, p, dev.DoorNo, false, ct);
        Save(no, s => s with
        {
            UserPresent = true, UserEnabled = false, FacePresent = u.FaceCount > 0,
            Fingers = onDevice?.ToHashSet() ?? s.Fingers, FingersKnown = onDevice is not null,
        });
        return new Note(nameof(JobKind.DisableUser), JobRules.KeptOnDeviceMessage);
    }

    private async Task WriteUserAsync(IHikDevice hik, PersonRecord p, int doorNo, bool enabled, CancellationToken ct)
    {
        await hik.UpsertUserAsync(JobRules.ToWrite(p, doorNo, enabled), ct);
        var w = JobRules.ToWrite(p, doorNo, enabled);
        await VerifyAsync(async () => await hik.FindUserAsync(p.EmployeeNo, ct) is { } u && u.Name == p.FullName && u.Enabled == enabled
            && JobRules.NormTime(u.BeginTime) == JobRules.NormTime(w.BeginTime) && JobRules.NormTime(u.EndTime) == JobRules.NormTime(w.EndTime), ct);
    }

    private async Task SetFingerAsync(DeviceRecord dev, JobRecord job, IHikDevice hik, CancellationToken ct)
    {
        var no = job.EmployeeNo;
        if (_db.People.Get(no) is null) return;
        var rows = _db.Biometrics.GetFingers(no).Where(f => f.FingerId == job.FingerId).ToList();
        if (rows.Count == 0) return;
        IReadOnlyList<HikFinger> seen = [];

        FingerRecord? copyFrom = null;
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
                // Only "this sensor does not take this template" means: scan the finger on this device. Any other
                // refusal (memory full, not ready, user missing...) fails the job with the device's own message.
                catch (HikRejectedException ex) when (IsSensorMismatch(ex)) { last = ex; }
            }
            if (used is null)
            {
                Save(no, s => s with { NeedsScan = With(s.NeedsScan, job.FingerId) });
                throw new JobFailure("needs_scan",
                    $"Cần quét {JobRules.FingerLabel(job.FingerId)} trực tiếp ở {dev.Name}.", last?.Detail ?? last?.Message);
            }
            copyFrom = used;
        }
        await VerifyAsync(async () => (seen = await hik.ReadFingerprintsAsync(no, ct)).Any(f => f.FingerId == job.FingerId), ct);
        if (copyFrom is not null) _db.Biometrics.SetFinger(copyFrom with { FpGroup = dev.FpGroup });   // only once the device confirmed it
        // The read-back is the device's complete list for this person (device-only fingers included).
        Save(no, s => s with { Fingers = seen.Select(f => f.FingerId).ToHashSet(), FingersKnown = true, NeedsScan = Without(s.NeedsScan, job.FingerId) });
    }

    private static bool IsSensorMismatch(HikRejectedException e) =>
        e.Code.Equals("fpMismatch", StringComparison.OrdinalIgnoreCase) || e.Code.Equals("checkFingerPrintFailed", StringComparison.OrdinalIgnoreCase);

    private static bool SameName(string a, string b) =>
        string.Equals(a.Trim().Normalize(), b.Trim().Normalize(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<int> With(IReadOnlySet<int> s, int id) => new HashSet<int>(s) { id };
    private static IReadOnlySet<int> Without(IReadOnlySet<int> s, int id) { var h = new HashSet<int>(s); h.Remove(id); return h; }
}
