using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Acs.Core.Host;
using Acs.Core.Model;
using Acs.Core.Security;
using Acs.Core.Tests.Fakes;
using Acs.Core.Update;
using Microsoft.Extensions.Time.Testing;

namespace Acs.Core.Tests.Host;

public sealed class AppHostTests : IAsyncDisposable
{
    private sealed class PassThrough : IKeyProtector
    {
        public byte[] Protect(byte[] data) => (byte[])data.Clone();
        public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
    }

    /// <summary>Feed and installer served from memory.</summary>
    private sealed class FakeHttp(string feedJson, byte[] installer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.RequestUri!.AbsolutePath.EndsWith("latest.json") ? Encoding.UTF8.GetBytes(feedJson) : installer;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "acs-host-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeDeviceFactory _factory = new();
    private readonly List<AppHost> _hosts = [];
    private readonly List<(string Name, object? Data)> _events = [];
    private List<(string Name, object? Data)> Ev { get { lock (_events) return [.. _events]; } }

    private AppHost NewHost(Updater? updater = null, string version = "1.0.0")
    {
        var h = AppHost.Create(_folder, new PassThrough(), _factory, _time, version, updater, 1000);
        h.Emitted += (n, d) => { lock (_events) _events.Add((n, d)); };
        _hosts.Add(h);
        return h;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var h in _hosts) await h.StopAsync();
        try { Directory.Delete(_folder, true); } catch { }
    }

    private static JsonElement J(string json = "{}") => JsonDocument.Parse(json).RootElement.Clone();

    private static string Ser(object? o) => JsonSerializer.Serialize(o, AppHost.Json);

    private static JsonElement Out(object? o) => J(Ser(o));

    private static async Task Until(Func<bool> cond, Action? tick = null, int timeoutMs = 10000)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (!cond())
        {
            if (Environment.TickCount64 > end) throw new TimeoutException("Condition not reached.");
            tick?.Invoke();
            await Task.Delay(5);
        }
    }

    private static async Task<int> AddDevice(AppHost h, string name = "Máy tầng trệt", string password = "pw-secret-1")
    {
        var v = Out(await h.HandleAsync("devices.save", J($$"""{"name":"{{name}}","host":"192.168.1.201","username":"admin","password":"{{password}}","doorNo":1,"isCaptureDefault":true,"isFingerReader":false}""")));
        return v.GetProperty("id").GetInt32();
    }

    private static Task<object?> Create(AppHost h, string no, string name) =>
        h.HandleAsync("people.create", J($$"""{"fullName":"{{name}}","employeeNo":"{{no}}"}"""));

    [Fact]
    public async Task people_create_then_list_shows_pending_until_worker_runs()
    {
        var h = NewHost();
        await AddDevice(h);
        var detail = Out(await Create(h, "1001", "Nguyễn Văn An"));
        Assert.Equal("1001", detail.GetProperty("employeeNo").GetString());
        Assert.Equal("pending", detail.GetProperty("devices")[0].GetProperty("state").GetString());

        var rows = Out(await h.HandleAsync("people.list", J("""{"filter":"all","query":""}""")));
        Assert.Equal("pending", rows[0].GetProperty("sync").GetString());
        Assert.Equal("Máy tầng trệt", rows[0].GetProperty("waitingDevices")[0].GetString());
        Assert.Equal("NA", rows[0].GetProperty("initials").GetString());
        Assert.True(rows[0].GetProperty("isNew").GetBoolean());

        await h.StartAsync();
        await Until(() => Out(h.HandleAsync("people.list", J("""{"filter":"all"}""")).Result)[0].GetProperty("sync").GetString() == "ok",
            () => _time.Advance(TimeSpan.FromSeconds(1)));
        await Until(() => { return Ev.Any(e => e.Name == "person.changed"); }, () => _time.Advance(TimeSpan.FromSeconds(1)));
        Assert.Contains(Ev, e => e.Name == "devices.changed");
    }

    private static async Task SeedOnDevice(FakeDevice dev, string no, string name, bool face, params int[] fingers)
    {
        await dev.UpsertUserAsync(new Acs.Core.Hik.HikUserWrite(no, name, "2026-01-01T00:00:00", "2035-12-31T23:59:59", true, 1), default);
        if (face) await dev.SetFaceAsync(no, [0xFF, 0xD8, 0xFF, 0xD9], default);
        foreach (var f in fingers) await dev.SetFingerprintAsync(no, f, Convert.ToBase64String(Encoding.ASCII.GetBytes("T" + f)), 1, default);
    }

    private async Task<JsonElement> Row0(AppHost h) => Out(await h.HandleAsync("people.list", J()))[0];

    private static string[] Names(JsonElement arr) => arr.EnumerateArray().Select(x => x.GetString()!).ToArray();

    private async Task<bool> NeedsReconcile(AppHost h) =>
        Out(await h.HandleAsync("app.state", J())).GetProperty("needsReconcile").GetBoolean();

    [Fact]
    public async Task first_import_shows_the_other_device_pending_until_reconcile_is_verified()
    {
        var h = NewHost();
        var a = await AddDevice(h, "Máy A");
        var b = await AddDevice(h, "Máy B");
        await SeedOnDevice(_factory.For(a), "1001", "Nguyễn Văn An", true, 1, 6);
        await h.HandleAsync("import.start", J($$"""{"deviceId":{{a}}}"""));

        // No job exists for B and B never confirmed anything: never "✓ Đủ 2 máy".
        var row = await Row0(h);
        Assert.Equal("pending", row.GetProperty("sync").GetString());
        Assert.Equal(["Máy B"], Names(row.GetProperty("waitingDevices")));
        Assert.Equal(1, Out(await h.HandleAsync("people.list", J("""{"filter":"notSynced"}"""))).GetArrayLength());
        var devs = Out(await h.HandleAsync("people.get", J("""{"employeeNo":"1001"}"""))).GetProperty("devices");
        Assert.Equal("ok", devs[0].GetProperty("state").GetString());   // A: confirmed by the import read
        Assert.Equal("pending", devs[1].GetProperty("state").GetString());
        Assert.Equal(4, devs[1].GetProperty("items").GetArrayLength());   // info, face, 2 fingers
        Assert.All(devs[1].GetProperty("items").EnumerateArray(),
            i => Assert.Equal(AppHost.NotOnDeviceMessage, i.GetProperty("message").GetString()));
        Assert.True(await NeedsReconcile(h));

        await h.StartAsync();
        await h.HandleAsync("reconcile.plan", J());
        await h.HandleAsync("reconcile.apply", J("""{"strangers":{}}"""));
        Assert.False(await NeedsReconcile(h));
        await Until(() => Row0(h).Result.GetProperty("sync").GetString() == "ok", () => _time.Advance(TimeSpan.FromSeconds(1)));
        var onB = _factory.For(b);
        Assert.NotNull(await onB.FindUserAsync("1001", default));
        Assert.True(await onB.HasFaceAsync("1001", default));
        var fingersOnB = (await onB.ReadFingerprintsAsync("1001", default)).Select(f => f.FingerId).ToArray();
        Assert.Equal(new[] { 1, 6 }, fingersOnB);
    }

    [Fact]
    public async Task import_progress_is_reported_in_order_before_the_result()
    {
        var h = NewHost();
        var a = await AddDevice(h, "Máy A");
        foreach (var no in new[] { "1001", "1002", "1003" }) await SeedOnDevice(_factory.For(a), no, "P" + no, false);
        await h.HandleAsync("import.start", J($$"""{"deviceId":{{a}}}"""));
        var done = Ev.Where(e => e.Name == "import.progress").Select(e => Out(e.Data).GetProperty("done").GetInt32()).ToList();
        Assert.Equal(3, done.Last());   // already there when the command returned
        Assert.Equal(done.OrderBy(x => x), done);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("face")]
    public async Task person_on_both_devices_ends_ok_on_both_after_import_a_write_and_reconcile(string write)
    {
        var h = NewHost();
        var a = await AddDevice(h, "Máy A");
        var b = await AddDevice(h, "Máy B");
        foreach (var d in new[] { a, b }) await SeedOnDevice(_factory.For(d), "1001", "Nguyễn Văn An", true, 1, 6);
        await h.HandleAsync("import.start", J($$"""{"deviceId":{{a}}}"""));
        await h.StartAsync();

        // A write reaches B before anyone read B's fingers: B's fresh state row must not claim "no fingers".
        if (write == "edit")
            await h.HandleAsync("people.update", J("""{"employeeNo":"1001","fullName":"Nguyễn Văn An","groupName":"Kho"}"""));
        else
        {
            var jpeg = Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7, 0xFF, 0xD9]);
            await h.HandleAsync("face.save", J($$"""{"employeeNo":"1001","imageDataUrl":"data:image/jpeg;base64,{{jpeg}}","source":"file"}"""));
        }
        await Until(() => h.Db.Jobs.ListOpen().Count == 0 && h.Db.Jobs.GetState("1001", b) is not null,
            () => _time.Advance(TimeSpan.FromSeconds(1)));
        var fresh = h.Db.Jobs.GetState("1001", b)!;
        Assert.False(fresh.FingersKnown);
        Assert.Empty(fresh.Fingers);

        await h.HandleAsync("reconcile.plan", J());
        await h.HandleAsync("reconcile.apply", J("""{"strangers":{}}"""));
        await Until(() => h.Db.Jobs.ListOpen().Count == 0 && Row0(h).Result.GetProperty("sync").GetString() == "ok",
            () => _time.Advance(TimeSpan.FromSeconds(1)));
        var states = Out(await h.HandleAsync("people.get", J("""{"employeeNo":"1001"}"""))).GetProperty("devices");
        Assert.All(states.EnumerateArray(), d => Assert.Equal("ok", d.GetProperty("state").GetString()));
        var onB = h.Db.Jobs.GetState("1001", b)!;
        Assert.True(onB.FingersKnown);
        Assert.Equal(new HashSet<int> { 1, 6 }, onB.Fingers);   // read from the device, not assumed
        Assert.False(await NeedsReconcile(h));
    }

    [Fact]
    public async Task a_changed_device_address_forgets_what_the_old_unit_confirmed()
    {
        var h = NewHost();
        var a = await AddDevice(h, "Máy A");
        await Create(h, "1001", "An");
        await h.StartAsync();
        await Until(() => Row0(h).Result.GetProperty("sync").GetString() == "ok", () => _time.Advance(TimeSpan.FromSeconds(1)));
        Assert.NotNull(h.Db.Jobs.GetState("1001", a));

        // The unit was replaced: a new, empty device at a new address.
        _factory.Add(a, new FakeDevice());
        await h.HandleAsync("devices.save", J($$"""{"id":{{a}},"name":"Máy A","host":"192.168.1.202","username":"admin","password":"","doorNo":1,"isCaptureDefault":true,"isFingerReader":false}"""));
        Assert.Null(h.Db.Jobs.GetState("1001", a));
        var row = await Row0(h);
        Assert.Equal("pending", row.GetProperty("sync").GetString());   // never ✓ for an empty unit
        Assert.True(await NeedsReconcile(h));

        await h.HandleAsync("reconcile.plan", J());
        await h.HandleAsync("reconcile.apply", J("""{"strangers":{}}"""));
        await Until(() => Row0(h).Result.GetProperty("sync").GetString() == "ok", () => _time.Advance(TimeSpan.FromSeconds(1)));
        Assert.NotNull(await _factory.For(a).FindUserAsync("1001", default));

        // Only an address change does this: renaming keeps what was confirmed.
        await h.HandleAsync("devices.save", J($$"""{"id":{{a}},"name":"Máy A mới","host":"192.168.1.202","username":"admin","password":"","doorNo":1,"isCaptureDefault":true,"isFingerReader":false}"""));
        Assert.NotNull(h.Db.Jobs.GetState("1001", a));
        Assert.False(await NeedsReconcile(h));
    }

    [Fact]
    public async Task an_import_that_never_ran_does_not_ask_for_reconcile()
    {
        var h = NewHost();
        var a = await AddDevice(h, "Máy A");
        await AddDevice(h, "Máy B");
        await h.HandleAsync("reconcile.plan", J());   // empty app and devices: nothing to send, hint cleared
        Assert.False(await NeedsReconcile(h));
        h.Db.Devices.SetAuthPaused(a, true);
        await Assert.ThrowsAsync<Acs.Core.Hik.HikAuthException>(() => h.HandleAsync("import.start", J($$"""{"deviceId":{{a}}}""")));
        Assert.False(await NeedsReconcile(h));
    }

    [Fact]
    public async Task a_device_added_later_is_pending_for_everyone_and_asks_for_reconcile()
    {
        var h = NewHost();
        await AddDevice(h, "Máy A");
        Assert.False(await NeedsReconcile(h));   // first device and nothing in the app: nothing to send
        await Create(h, "1001", "An");
        await h.StartAsync();
        await Until(() => Row0(h).Result.GetProperty("sync").GetString() == "ok", () => _time.Advance(TimeSpan.FromSeconds(1)));

        await AddDevice(h, "Máy B");
        var row = await Row0(h);
        Assert.Equal("pending", row.GetProperty("sync").GetString());
        Assert.Equal(["Máy B"], Names(row.GetProperty("waitingDevices")));
        Assert.True(await NeedsReconcile(h));
    }

    [Fact]
    public async Task locked_person_is_ok_only_where_the_device_confirmed_absent_or_disabled()
    {
        var h = NewHost();
        var a = await AddDevice(h, "Máy A");
        await Create(h, "1001", "An");
        await h.StartAsync();
        await Until(() => Row0(h).Result.GetProperty("sync").GetString() == "ok", () => _time.Advance(TimeSpan.FromSeconds(1)));
        await h.HandleAsync("people.lock", J("""{"employeeNo":"1001"}"""));
        await Until(() => Out(h.HandleAsync("people.get", J("""{"employeeNo":"1001"}""")).Result).GetProperty("devices")[0]
            .GetProperty("state").GetString() == "ok", () => _time.Advance(TimeSpan.FromSeconds(1)));
        Assert.Null(await _factory.For(a).FindUserAsync("1001", default));

        await AddDevice(h, "Máy B");   // knows nothing about this person yet
        var onB = Out(await h.HandleAsync("people.get", J("""{"employeeNo":"1001"}"""))).GetProperty("devices")[1];
        Assert.Equal("pending", onB.GetProperty("state").GetString());
        Assert.Equal(AppHost.LockedUnknownMessage, onB.GetProperty("items")[0].GetProperty("message").GetString());
        Assert.Equal(0, Out(await h.HandleAsync("people.list", J("""{"filter":"notSynced"}"""))).GetArrayLength());
    }

    [Fact]
    public async Task restore_asks_for_reconcile_after_the_restart()
    {
        var h = NewHost();
        await AddDevice(h);
        await h.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        var file = Out(await h.HandleAsync("backup.now", J())).GetProperty("files")[0].GetProperty("name").GetString()!;
        Assert.False(await NeedsReconcile(h));
        Assert.Null(await h.HandleAsync("backup.restore", J($$"""{"name":"{{file}}","password":"mat-khau-sao-luu"}""")));
        await h.StopAsync();

        var h2 = NewHost();
        Assert.True(await NeedsReconcile(h2));
        Assert.False(File.Exists(Path.Combine(_folder, AppHost.RestoreMarkerName)));
        await h2.HandleAsync("reconcile.plan", J());   // the device already matches: nothing to send
        Assert.False(await NeedsReconcile(h2));
    }

    [Fact]
    public async Task restore_on_a_new_pc_from_a_file_in_another_folder()
    {
        var h = NewHost();
        await AddDevice(h);
        await Create(h, "1001", "Nguyễn Văn An");
        await h.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        var name = Out(await h.HandleAsync("backup.now", J())).GetProperty("files")[0].GetProperty("name").GetString()!;
        var usb = Path.Combine(_folder, "usb");
        Directory.CreateDirectory(usb);
        File.Copy(Path.Combine(_folder, "backups", name), Path.Combine(usb, name));

        // A new PC: its own empty data folder, no local backups.
        var other = _folder + "-newpc";
        AppHost NewPc() { var x = AppHost.Create(other, new PassThrough(), _factory, _time, "1.0.0", null, 1000); _hosts.Add(x); return x; }
        try
        {
            var pc = NewPc();
            Assert.Equal(0, Out(await pc.HandleAsync("backup.status", J())).GetProperty("files").GetArrayLength());
            var listed = Out(await pc.HandleAsync("backup.listFolder", J(Ser(new { folder = usb }))));
            Assert.Equal(name, listed[0].GetProperty("name").GetString());
            Assert.True(listed[0].GetProperty("size").GetInt64() > 0);
            Assert.NotEqual(JsonValueKind.Null, listed[0].GetProperty("at").ValueKind);

            var path = Path.Combine(usb, name);
            await Assert.ThrowsAsync<Acs.Core.Backup.BackupException>(() =>
                pc.HandleAsync("backup.restore", J(Ser(new { path, password = "sai-mat-khau-roi" }))));
            Assert.Null(await pc.HandleAsync("backup.restore", J(Ser(new { path, password = "mat-khau-sao-luu" }))));
            Assert.True(pc.RestartRequested);
            await pc.StopAsync();

            var after = NewPc();
            Assert.Equal("Nguyễn Văn An", (await Row0(after)).GetProperty("fullName").GetString());
            Assert.True(await NeedsReconcile(after));
        }
        finally
        {
            foreach (var x in _hosts.ToList()) await x.StopAsync();
            try { Directory.Delete(other, true); } catch { }
        }
    }

    [Fact]
    public async Task backup_list_folder_and_restore_path_are_validated()
    {
        var h = NewHost();
        var e1 = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("backup.listFolder", J(Ser(new { folder = @"relative\dir" }))));
        Assert.Contains("đường dẫn đầy đủ", e1.Message);
        var e2 = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("backup.listFolder", J(Ser(new { folder = Path.Combine(_folder, "khong-co") }))));
        Assert.Contains("Không thấy thư mục", e2.Message);
        Directory.CreateDirectory(Path.Combine(_folder, "trong"));
        Assert.Equal(0, Out(await h.HandleAsync("backup.listFolder", J(Ser(new { folder = Path.Combine(_folder, "trong") })))).GetArrayLength());

        await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("backup.restore", J(Ser(new { path = Path.Combine(_folder, "acs.db"), password = "x" }))));
        await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("backup.restore", J(Ser(new { path = "ACS-x.acsbak", password = "x" }))));
        var e3 = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("backup.restore", J(Ser(new { path = Path.Combine(_folder, "ACS-none.acsbak"), password = "x" }))));
        Assert.Contains("Không tìm thấy file sao lưu", e3.Message);
        Assert.False(h.RestartRequested);
    }

    [Fact]
    public async Task unknown_command_throws_vietnamese_message()
    {
        var h = NewHost();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.HandleAsync("nope.nothing", J()));
        Assert.Equal("Lệnh không có: nope.nothing", ex.Message);
    }

    [Fact]
    public async Task devices_save_keeps_password_when_empty()
    {
        var h = NewHost();
        var id = await AddDevice(h, password: "pw-secret-1");
        var v = Out(await h.HandleAsync("devices.save", J($$"""{"id":{{id}},"name":"Máy mới tên","host":"192.168.1.202","username":"admin","password":"","doorNo":1,"isCaptureDefault":true,"isFingerReader":true}""")));
        Assert.Equal("pw-secret-1", h.Db.Devices.Get(id)!.Password);
        Assert.Equal("Máy mới tên", v.GetProperty("name").GetString());
        Assert.DoesNotContain("pw-secret", Ser(await h.HandleAsync("devices.list", J())));
        Assert.Equal("device-" + id, h.Db.Devices.Get(id)!.FpGroup);
        await h.HandleAsync("devices.save", J($$"""{"id":{{id}},"name":"Máy mới tên","host":"192.168.1.202","username":"admin","password":"đổi-mật-khẩu","doorNo":1,"isCaptureDefault":true,"isFingerReader":true}"""));
        Assert.Equal("đổi-mật-khẩu", h.Db.Devices.Get(id)!.Password);
    }

    [Fact]
    public async Task face_save_rejects_non_jpeg_with_vietnamese_message()
    {
        var h = NewHost();
        await AddDevice(h);
        await Create(h, "1001", "An");
        var png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]);
        var ex = await Assert.ThrowsAsync<Acs.Core.Enroll.EnrollException>(() => h.HandleAsync("face.save",
            J($$"""{"employeeNo":"1001","imageDataUrl":"data:image/png;base64,{{png}}","source":"file"}""")));
        Assert.Equal(Acs.Core.Enroll.EnrollService.NotJpegMessage, ex.Message);

        var jpeg = Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9]);
        var ok = Out(await h.HandleAsync("face.save", J($$"""{"employeeNo":"1001","imageDataUrl":"data:image/jpeg;base64,{{jpeg}}","source":"webcam"}""")));
        Assert.Equal("data:image/jpeg;base64," + jpeg, ok.GetProperty("imageDataUrl").GetString());
        var d = Out(await h.HandleAsync("people.get", J("""{"employeeNo":"1001"}""")));
        Assert.Equal("data:image/jpeg;base64," + jpeg, d.GetProperty("faceDataUrl").GetString());
        Assert.Equal(BiometricSource.Webcam, h.Db.Biometrics.GetFace("1001")!.Source);
    }

    [Fact]
    public async Task sync_retry_clears_auth_pause()
    {
        var h = NewHost();
        var a = await AddDevice(h, "Máy A");
        var b = await AddDevice(h, "Máy B");
        await Create(h, "1001", "An");
        h.Db.Devices.SetAuthPaused(a, true);
        h.Db.Devices.SetAuthPaused(b, true);
        var failedJob = h.Db.Jobs.ListOpen(a).Single(j => j.Kind == JobKind.UpsertUser);
        Assert.True(h.Db.Jobs.MarkRunning(failedJob, _time.GetUtcNow().UtcDateTime));
        h.Db.Jobs.Fail(failedJob.Id, "rejected", "Máy từ chối.", "raw", _time.GetUtcNow().UtcDateTime);

        await h.HandleAsync("sync.retry", J($$"""{"deviceId":{{a}}}"""));
        Assert.False(h.Db.Devices.Get(a)!.AuthPaused);
        Assert.True(h.Db.Devices.Get(b)!.AuthPaused);   // other device untouched
        var job = h.Db.Jobs.Get(failedJob.Id)!;
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Null(job.ErrorMessage);

        await h.HandleAsync("sync.retry", J("""{"deviceId":null}"""));
        Assert.False(h.Db.Devices.Get(b)!.AuthPaused);
    }

    [Fact]
    public async Task people_list_finds_diacritics_by_plain_letters_and_sorts_vietnamese()
    {
        var h = NewHost();
        await Create(h, "3", "Trần Bình");
        await Create(h, "2", "Nguyễn Văn An");
        await Create(h, "1", "Đặng Hoa");
        await Create(h, "4", "Ân Lê");
        var found = Out(await h.HandleAsync("people.list", J("""{"filter":"all","query":"nguyen"}""")));
        Assert.Equal(1, found.GetArrayLength());
        Assert.Equal("Nguyễn Văn An", found[0].GetProperty("fullName").GetString());
        Assert.Equal(1, Out(await h.HandleAsync("people.list", J("""{"query":"DANG hoa"}"""))).GetArrayLength());
        Assert.Equal(1, Out(await h.HandleAsync("people.list", J("""{"query":"3"}"""))).GetArrayLength());

        var all = Out(await h.HandleAsync("people.list", J("""{"filter":"all"}""")));
        var names = all.EnumerateArray().Select(r => r.GetProperty("fullName").GetString()!).ToArray();
        Assert.Equal(["Ân Lê", "Đặng Hoa", "Nguyễn Văn An", "Trần Bình"], names);
    }

    [Fact]
    public async Task finger_labels_run_right_then_left()
    {
        Assert.Equal("Cái phải", AppHost.FingerLabel(1));
        Assert.Equal("Út phải", AppHost.FingerLabel(5));
        Assert.Equal("Cái trái", AppHost.FingerLabel(6));
        Assert.Equal("Út trái", AppHost.FingerLabel(10));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task app_state_counts_stale_locked_and_first_run()
    {
        var h = NewHost();
        var s0 = Out(await h.HandleAsync("app.state", J()));
        Assert.True(s0.GetProperty("firstRun").GetBoolean());
        Assert.Equal("1.0.0", s0.GetProperty("version").GetString());

        var old = _time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(181);
        var recent = _time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(10);
        foreach (var (no, at) in new[] { ("1", old), ("2", recent) })
            h.Db.People.Insert(new PersonRecord(no, "P" + no, null, new DateOnly(2026, 1, 1), null, PersonStatus.Locked, old, old, at));
        h.Db.People.Insert(new PersonRecord("3", "P3", null, new DateOnly(2026, 1, 1), null, PersonStatus.Active, old, old, null));
        var s = Out(await h.HandleAsync("app.state", J()));
        Assert.Equal(1, s.GetProperty("staleLocked").GetInt32());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("update").ValueKind);
    }

    [Fact]
    public async Task unexpected_errors_become_a_generic_message_and_are_logged()
    {
        var h = NewHost();
        var ex = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("import.start", J("""{"deviceId":99}""")));
        Assert.Equal(AppHost.GenericError, ex.Message);
        Assert.Contains(h.Db.Log.List(null, null, 10), l => l.Action == "Lỗi" && l.Message!.Contains("Không tìm thấy thiết bị"));
    }

    [Fact]
    public async Task reconcile_apply_needs_a_plan()
    {
        var h = NewHost();
        await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("reconcile.apply", J("""{"strangers":{}}""")));
        await AddDevice(h);
        var plan = Out(await h.HandleAsync("reconcile.plan", J()));
        Assert.Equal(1, plan.GetProperty("devices").GetArrayLength());
        Assert.Null(await h.HandleAsync("reconcile.apply", J("""{"strangers":{"1:77":"remove"}}""")));
    }

    [Fact]
    public async Task devices_test_reports_failure_as_a_result()
    {
        var h = NewHost();
        var id = await AddDevice(h);
        var ok = Out(await h.HandleAsync("devices.test", J($$"""{"id":{{id}}}""")));
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Equal("DS-K1T343", ok.GetProperty("model").GetString());
        _factory.For(id).Offline = true;
        var bad = Out(await h.HandleAsync("devices.test", J($$"""{"id":{{id}}}""")));
        Assert.False(bad.GetProperty("ok").GetBoolean());
        Assert.Contains("không phản hồi", bad.GetProperty("message").GetString());
    }

    [Fact]
    public async Task devices_test_is_refused_while_paused_until_the_login_changes()
    {
        var h = NewHost();
        var id = await AddDevice(h);
        var fake = _factory.For(id);
        fake.WrongPassword = true;
        var bad = Out(await h.HandleAsync("devices.test", J($$"""{"id":{{id}}}""")));
        Assert.False(bad.GetProperty("ok").GetBoolean());
        Assert.True(h.Db.Devices.Get(id)!.AuthPaused);   // paused at once, like the worker does
        var calls = fake.Calls.Count;

        var again = Out(await h.HandleAsync("devices.test", J($$"""{"id":{{id}}}""")));
        Assert.False(again.GetProperty("ok").GetBoolean());
        Assert.Equal(Acs.Core.Sync.JobRules.AuthMessage, again.GetProperty("message").GetString());
        Assert.Equal(calls, fake.Calls.Count);   // refused locally: no failed login

        fake.WrongPassword = false;
        await h.HandleAsync("devices.save", J($$"""{"id":{{id}},"name":"Máy tầng trệt","host":"192.168.1.201","username":"admin","password":"mat-khau-moi","doorNo":1,"isCaptureDefault":true,"isFingerReader":false}"""));
        Assert.False(h.Db.Devices.Get(id)!.AuthPaused);   // a changed password clears the pause and is tested
        Assert.True(Out(await h.HandleAsync("devices.test", J($$"""{"id":{{id}}}"""))).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task backup_restore_wrong_password_keeps_running_then_right_password_requests_restart()
    {
        var h = NewHost();
        await Create(h, "1001", "Nguyễn Văn An");
        await h.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        var st = Out(await h.HandleAsync("backup.now", J()));
        var file = st.GetProperty("files")[0].GetProperty("name").GetString()!;
        await h.HandleAsync("people.delete", J("""{"employeeNo":"1001"}"""));

        var bad = await Assert.ThrowsAsync<Acs.Core.Backup.BackupException>(() =>
            h.HandleAsync("backup.restore", J($$"""{"name":"{{file}}","password":"sai-mat-khau-roi"}""")));
        Assert.Contains("Mật khẩu sao lưu không đúng", bad.Message);
        Assert.Equal(0, Out(await h.HandleAsync("people.list", J())).GetArrayLength());   // old data, still working

        Assert.Null(await h.HandleAsync("backup.restore", J($$"""{"name":"{{file}}","password":"mat-khau-sao-luu"}""")));
        Assert.True(h.RestartRequested);
        Assert.Contains(Ev, e => e.Name == "app.restart");
        await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("people.list", J()));
        await h.StopAsync();

        var h2 = NewHost();
        Assert.Equal("Nguyễn Văn An", Out(await h2.HandleAsync("people.list", J()))[0].GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task start_makes_a_daily_backup_only_when_password_set_and_last_is_old()
    {
        var h = NewHost();
        await h.StartAsync();
        Assert.Empty(h.Backup.List());   // no password yet
        await h.StopAsync();

        var h2 = NewHost();
        await h2.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        await h2.StartAsync();
        Assert.Single(h2.Backup.List());
        await h2.StopAsync();

        _time.Advance(TimeSpan.FromHours(2));
        var h3 = NewHost();
        await h3.StartAsync();
        Assert.Single(h3.Backup.List());   // under 24 h: no new one
        await h3.StopAsync();

        _time.Advance(TimeSpan.FromHours(25));
        var h4 = NewHost();
        await h4.StartAsync();
        Assert.Equal(2, h4.Backup.List().Count);
    }

    private static Updater FeedWith(string version, byte[] installer)
    {
        var sha = Convert.ToHexString(SHA256.HashData(installer));
        var json = $$$"""{"version":"{{{version}}}","notes":"Sửa lỗi","windows":{"url":"http://x.test/setup.exe","size":{{{installer.Length}}},"sha256":"{{{sha}}}"}}""";
        return new Updater(new HttpClient(new FakeHttp(json, installer)), "http://x.test/latest.json");
    }

    [Fact]
    public async Task update_timer_checks_after_one_minute_and_only_when_enabled()
    {
        var h = NewHost(FeedWith("1.1.0", new byte[2_000_000]));
        await h.StartAsync();
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain(Ev, e => e.Name == "update.available");
        _time.Advance(TimeSpan.FromSeconds(31));
        await Until(() => Ev.Any(e => e.Name == "update.available"));
        var view = Out(Ev.First(e => e.Name == "update.available").Data);
        Assert.Equal("1.1.0", view.GetProperty("version").GetString());
        Assert.Equal(1.9, view.GetProperty("sizeMb").GetDouble());
        await h.StopAsync();

        var off = NewHost(FeedWith("1.1.0", new byte[10]));
        await off.HandleAsync("settings.set", J("""{"autoCheckUpdates":false}"""));
        lock (_events) _events.Clear();
        await off.StartAsync();
        _time.Advance(TimeSpan.FromHours(21));
        await Task.Delay(200);
        Assert.DoesNotContain(Ev, e => e.Name == "update.available");
        Assert.False(Out(await off.HandleAsync("settings.get", J())).GetProperty("autoCheckUpdates").GetBoolean());
    }

    [Fact]
    public async Task update_install_needs_installer_then_downloads_backs_up_and_runs_it()
    {
        var bytes = new byte[500_000];
        var h = NewHost(FeedWith("1.1.0", bytes));
        var ex = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("update.install", J()));
        Assert.Equal("Chưa sẵn sàng cập nhật", ex.Message);

        await h.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        string? ran = null;
        h.RunInstaller = p => { ran = p; return Task.CompletedTask; };
        var v = Out(await h.HandleAsync("update.check", J()));
        Assert.Equal("1.1.0", v.GetProperty("version").GetString());
        Assert.Null(await h.HandleAsync("update.install", J()));
        Assert.NotNull(ran);
        Assert.True(File.Exists(ran));
        Assert.Contains(Ev, e => e.Name == "update.progress");
        Assert.Contains(h.Backup.List(), b => b.Name.EndsWith("-truoc-cap-nhat.acsbak"));
        Assert.Equal("1.1.0", Out(await h.HandleAsync("app.state", J())).GetProperty("update").GetProperty("version").GetString());
    }

    [Fact]
    public async Task backup_status_and_extra_folder()
    {
        var h = NewHost();
        var extra = Path.Combine(_folder, "extra");
        var s = Out(await h.HandleAsync("backup.setExtraFolder", J(Ser(new { folder = extra }))));
        Assert.Equal(extra, s.GetProperty("extraFolder").GetString());
        Assert.False(s.GetProperty("hasPassword").GetBoolean());
        await h.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        await h.HandleAsync("backup.now", J());
        Assert.Single(Directory.GetFiles(extra));
        var s2 = Out(await h.HandleAsync("backup.setExtraFolder", J("""{"folder":null}""")));
        Assert.Equal(JsonValueKind.Null, s2.GetProperty("extraFolder").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, s2.GetProperty("lastAt").ValueKind);
    }

    private sealed class Hang : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        }
    }

    [Fact]
    public async Task update_check_timeout_is_a_vietnamese_message_not_english_text()
    {
        var h = NewHost(new Updater(new HttpClient(new Hang()), "http://x.test/latest.json"));
        h.FeedTimeout = TimeSpan.FromMilliseconds(100);
        var ex = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("update.check", J()));
        Assert.Equal(AppHost.SlowNetworkMessage, ex.Message);
    }

    [Fact]
    public async Task update_install_twice_at_once_refuses_the_second()
    {
        var h = NewHost(FeedWith("1.1.0", new byte[1000]));
        var gate = new TaskCompletionSource();
        h.RunInstaller = _ => gate.Task;
        var first = h.HandleAsync("update.install", J());
        await Task.Delay(300);
        var ex = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("update.install", J()));
        Assert.Equal("Đang cập nhật…", ex.Message);
        gate.SetResult();
        await first;
    }

    [Fact]
    public async Task wrong_restore_password_leaves_the_engine_running()
    {
        var h = NewHost();
        await AddDevice(h);
        await h.StartAsync();
        await h.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        var file = Out(await h.HandleAsync("backup.now", J())).GetProperty("files")[0].GetProperty("name").GetString()!;
        await Assert.ThrowsAsync<Acs.Core.Backup.BackupException>(() =>
            h.HandleAsync("backup.restore", J($$"""{"name":"{{file}}","password":"sai-mat-khau-roi"}""")));
        Assert.False(h.RestartRequested);

        await Create(h, "1001", "An");   // still accepted, and the worker still sends it
        await Until(() => Out(h.HandleAsync("people.list", J()).Result)[0].GetProperty("sync").GetString() == "ok",
            () => _time.Advance(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task command_during_restore_is_refused_while_running_ones_finish()
    {
        var h = NewHost();
        var dev = await AddDevice(h);
        await Create(h, "1001", "An");
        await h.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        var file = Out(await h.HandleAsync("backup.now", J())).GetProperty("files")[0].GetProperty("name").GetString()!;
        _factory.For(dev).CaptureDelay = TimeSpan.FromMilliseconds(1500);

        var capture = h.HandleAsync("face.capture", J($$"""{"employeeNo":"1001","deviceId":{{dev}}}"""));
        await Task.Delay(100);
        var restore = h.HandleAsync("backup.restore", J($$"""{"name":"{{file}}","password":"mat-khau-sao-luu"}"""));
        await Task.Delay(400);
        var ex = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("people.list", J()));
        Assert.Equal(AppHost.RestoringMessage, ex.Message);

        await capture;
        Assert.Null(await restore);
        Assert.True(h.RestartRequested);
    }

    [Fact]
    public async Task log_list_shows_vietnamese_actions_and_keeps_unknown_codes()
    {
        var h = NewHost();
        foreach (var code in new[] { "UpsertUser", "SetFace", "Ping", "Auth", "Worker", "Mystery" })
            h.Db.Log.Add("1001", null, code, true, "x");
        var rows = Out(await h.HandleAsync("log.list", J("""{"limit":200}"""))).EnumerateArray()
            .Select(r => r.GetProperty("action").GetString()).Reverse().ToArray();
        Assert.Equal(["Thông tin", "Khuôn mặt", "Kiểm tra kết nối", "Đăng nhập máy", "Luồng gửi dữ liệu", "Mystery"], rows!);
    }

    [Fact]
    public async Task problem_filters_skip_locked_people_and_inputs_are_validated()
    {
        var h = NewHost();
        await Create(h, "1", "An");
        await Create(h, "2", "Bình");
        await h.HandleAsync("people.lock", J("""{"employeeNo":"2"}"""));
        Assert.Equal(1, Out(await h.HandleAsync("people.list", J("""{"filter":"noFace"}"""))).GetArrayLength());
        Assert.Equal(1, Out(await h.HandleAsync("people.list", J("""{"filter":"noFinger"}"""))).GetArrayLength());
        Assert.Equal(1, Out(await h.HandleAsync("people.list", J("""{"filter":"locked"}"""))).GetArrayLength());

        await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("devices.save",
            J("""{"name":"M","host":"1.2.3.4","username":"a","password":"b","doorNo":0}""")));
        await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("backup.setExtraFolder", J("""{"folder":"relative-dir"}""")));
        Assert.Empty(h.Db.Devices.List());
    }

    [Fact]
    public async Task stop_called_from_inside_a_command_does_not_wait_for_that_command()
    {
        var h = NewHost(FeedWith("1.1.0", new byte[1000]));
        h.RunInstaller = _ => h.StopAsync();   // what the shell does: update.install -> installer -> StopHostAsync
        var run = h.HandleAsync("update.install", J());
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(run, finished);
        Assert.Null(await run);
    }

    private async Task<(AppHost H, string File, int Dev)> Prepared()
    {
        var h = NewHost();
        var dev = await AddDevice(h);
        await h.StartAsync();
        await h.HandleAsync("backup.setPassword", J("""{"password":"mat-khau-sao-luu"}"""));
        var file = Out(await h.HandleAsync("backup.now", J())).GetProperty("files")[0].GetProperty("name").GetString()!;
        return (h, file, dev);
    }

    [Fact]
    public async Task restore_gives_up_when_a_command_never_finishes_and_everything_keeps_working()
    {
        var (h, file, dev) = await Prepared();
        await Create(h, "1001", "An");
        _factory.For(dev).CaptureDelay = TimeSpan.FromHours(1);   // a command that never finishes (ends at StopAsync)
        var stuck = h.HandleAsync("face.capture", J($$"""{"employeeNo":"1001","deviceId":{{dev}}}"""));
        await Task.Delay(100);
        var restore = h.HandleAsync("backup.restore", J($$"""{"name":"{{file}}","password":"mat-khau-sao-luu"}"""));
        await Until(() => restore.IsCompleted, () => _time.Advance(TimeSpan.FromSeconds(5)));
        var ex = await Assert.ThrowsAsync<HostException>(() => restore);
        Assert.Equal(AppHost.BusyMessage, ex.Message);
        Assert.False(h.RestartRequested);

        Assert.Equal(1, Out(await h.HandleAsync("people.list", J())).GetArrayLength());   // commands accepted again
        await Create(h, "1002", "Bình");
        await Until(() => Out(h.HandleAsync("people.list", J()).Result)[1].GetProperty("sync").GetString() == "ok",
            () => _time.Advance(TimeSpan.FromSeconds(1)));   // engine was never stopped
        _factory.For(dev).CaptureDelay = TimeSpan.Zero;
        await h.StopAsync();
        await Task.WhenAny(stuck.ContinueWith(_ => { }), Task.Delay(2000));
    }

    [Fact]
    public async Task failed_swap_recovers_on_the_old_database()
    {
        var (h, file, dev) = await Prepared();
        Directory.CreateDirectory(Path.Combine(_folder, "acs.db.restore.tmp"));   // makes the restore write fail
        await Assert.ThrowsAsync<Acs.Core.Backup.BackupException>(() =>
            h.HandleAsync("backup.restore", J($$"""{"name":"{{file}}","password":"mat-khau-sao-luu"}""")));
        Assert.False(h.RestartRequested);
        await Create(h, "1001", "An");
        await Until(() => Out(h.HandleAsync("people.list", J()).Result)[0].GetProperty("sync").GetString() == "ok",
            () => _time.Advance(TimeSpan.FromSeconds(1)));   // new graph, engine restarted
    }

    [Fact]
    public async Task failed_swap_that_cannot_reopen_refuses_every_command()
    {
        var (h, file, _) = await Prepared();
        Directory.CreateDirectory(Path.Combine(_folder, "acs.db.restore.tmp"));
        h.ReopenDb = () => throw new IOException("disk gone");
        var ex = await Assert.ThrowsAsync<HostException>(() =>
            h.HandleAsync("backup.restore", J($$"""{"name":"{{file}}","password":"mat-khau-sao-luu"}""")));
        Assert.Equal(AppHost.BrokenMessage, ex.Message);
        var next = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("people.list", J()));
        Assert.Equal(AppHost.BrokenMessage, next.Message);
    }
}
