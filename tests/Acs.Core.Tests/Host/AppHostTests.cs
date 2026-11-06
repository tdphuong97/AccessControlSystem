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
        await Until(() => { lock (_events) return _events.Any(e => e.Name == "person.changed"); }, () => _time.Advance(TimeSpan.FromSeconds(1)));
        Assert.Contains(_events, e => e.Name == "devices.changed");
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
        Assert.Equal("Ảnh khuôn mặt phải là tệp JPEG.", ex.Message);

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
        Assert.Contains(_events, e => e.Name == "app.restart");
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
        Assert.DoesNotContain(_events, e => e.Name == "update.available");
        _time.Advance(TimeSpan.FromSeconds(31));
        await Until(() => _events.Any(e => e.Name == "update.available"));
        var view = Out(_events.First(e => e.Name == "update.available").Data);
        Assert.Equal("1.1.0", view.GetProperty("version").GetString());
        Assert.Equal(1.9, view.GetProperty("sizeMb").GetDouble());
        await h.StopAsync();

        var off = NewHost(FeedWith("1.1.0", new byte[10]));
        await off.HandleAsync("settings.set", J("""{"autoCheckUpdates":false}"""));
        _events.Clear();
        await off.StartAsync();
        _time.Advance(TimeSpan.FromHours(21));
        await Task.Delay(200);
        Assert.DoesNotContain(_events, e => e.Name == "update.available");
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
        Assert.Contains(_events, e => e.Name == "update.progress");
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
}
