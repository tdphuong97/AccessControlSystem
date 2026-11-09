using System.Text.Json;
using Acs.Core.Cloud;
using Acs.Core.Host;
using Acs.Core.Security;
using Acs.Core.Tests.Cloud;
using Acs.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace Acs.Core.Tests.Host;

public sealed class AppHostCloudTests : IAsyncDisposable
{
    private sealed class PassThrough : IKeyProtector
    {
        public byte[] Protect(byte[] data) => data;
        public byte[] Unprotect(byte[] data) => data;
    }

    private const string Code = "acs1:acs.test:" + FakeCloud.Key;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "acs-hc-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCloud _cloud = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));
    private readonly List<AppHost> _hosts = [];

    private AppHost Host(string name)
    {
        var h = AppHost.Create(Path.Combine(_root, name), new PassThrough(), new FakeDeviceFactory(), _time, "0.9.15", null, 1000, _cloud.Client());
        _hosts.Add(h);
        return h;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var h in _hosts) await h.StopAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static JsonElement J(string json = "{}") => JsonDocument.Parse(json).RootElement.Clone();
    private static JsonElement Out(object? o) => J(JsonSerializer.Serialize(o, AppHost.Json));

    private static async Task<AppHost> Ready(AppHost h, bool backupPassword = true)
    {
        await h.StartAsync();
        if (backupPassword) await h.HandleAsync("backup.setPassword", J("""{"password":"saoluu123"}"""));
        return h;
    }

    private static async Task AddPerson(AppHost h, string no, string name)
    {
        await h.HandleAsync("people.create", J($$"""{"fullName":"{{name}}","employeeNo":"{{no}}"}"""));
        await h.HandleAsync("face.save", J($$"""{"employeeNo":"{{no}}","imageDataUrl":"data:image/jpeg;base64,/9j/2Q=="}"""));
    }

    private static async Task<int> PeopleCount(AppHost h) => Out(await h.HandleAsync("people.list", J("""{"filter":"all"}"""))).GetArrayLength();

    [Fact]
    public async Task connect_on_empty_space_publishes()
    {
        var n = await Ready(Host("nhien"));
        await AddPerson(n, "101", "Nguyễn Một");
        var r = Out(await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}""")));
        Assert.Equal("published", r.GetProperty("mode").GetString());
        Assert.Equal(1, r.GetProperty("people").GetInt32());
        Assert.NotNull(_cloud.Space);
        Assert.Contains(_cloud.Rows.Keys, k => k.Item1 == "people");
        var s = Out(await n.HandleAsync("cloud.status", J()));
        Assert.True(s.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, s.GetProperty("pending").GetInt32());
    }

    [Fact]
    public async Task probe_reports_whether_shared_data_exists()
    {
        var n = await Ready(Host("nhien"));
        Assert.False(Out(await n.HandleAsync("cloud.probe", J($$"""{"code":"{{Code}}"}"""))).GetProperty("exists").GetBoolean());
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        Assert.True(Out(await n.HandleAsync("cloud.probe", J($$"""{"code":"{{Code}}"}"""))).GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task short_password_is_refused_on_first_connect()
    {
        var n = await Ready(Host("nhien"));
        var e = await Assert.ThrowsAsync<HostException>(() => n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"123"}""")));
        Assert.Equal("Mật khẩu dữ liệu chung cần ít nhất 8 ký tự.", e.Message);
        Assert.Null(_cloud.Space);
    }

    [Fact]
    public async Task connect_on_existing_space_replaces_local_after_backup()
    {
        var n = await Ready(Host("nhien"));
        await AddPerson(n, "101", "Nguyễn Một");
        await AddPerson(n, "102", "Nguyễn Hai");
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));

        var h = await Ready(Host("huong"));
        await AddPerson(h, "999", "Cũ");
        var r = Out(await h.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}""")));
        Assert.Equal("joined", r.GetProperty("mode").GetString());
        Assert.Equal(2, r.GetProperty("people").GetInt32());
        Assert.Equal(2, await PeopleCount(h));
        var files = Out(await h.HandleAsync("backup.status", J())).GetProperty("files");
        Assert.Contains(files.EnumerateArray(), f => f.GetProperty("name").GetString()!.Contains("truoc-du-lieu-chung"));
        Assert.Equal("1", h.Db.Settings.Get(AppHost.NeedsReconcileKey));   // shown once a device exists
    }

    [Fact]
    public async Task join_without_backup_password_is_refused()
    {
        var n = await Ready(Host("nhien"));
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        var h = await Ready(Host("huong"), backupPassword: false);
        var e = await Assert.ThrowsAsync<HostException>(() => h.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}""")));
        Assert.Contains("mật khẩu sao lưu", e.Message);
    }

    [Fact]
    public async Task wrong_password_changes_nothing()
    {
        var n = await Ready(Host("nhien"));
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        var h = await Ready(Host("huong"));
        await AddPerson(h, "999", "Cũ");
        var e = await Assert.ThrowsAsync<CloudException>(() => h.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"sai-mat-khau"}""")));
        Assert.Equal("Mật khẩu dữ liệu chung chưa đúng.", e.Message);
        Assert.Equal(1, await PeopleCount(h));
        Assert.False(Out(await h.HandleAsync("cloud.status", J())).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task edits_flow_between_hosts_with_syncNow()
    {
        var n = await Ready(Host("nhien"));
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        var h = await Ready(Host("huong"));
        await h.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        await AddPerson(h, "150", "Kim Chi");
        await h.HandleAsync("cloud.syncNow", J());
        await n.HandleAsync("cloud.syncNow", J());
        Assert.Equal(1, await PeopleCount(n));
    }

    [Fact]
    public async Task switch_off_then_on_takes_cloud_data_without_asking_again()
    {
        var n = await Ready(Host("nhien"));
        await AddPerson(n, "101", "Một");
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        var h = await Ready(Host("huong"));
        await h.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        await h.HandleAsync("cloud.setEnabled", J("""{"on":false}"""));
        await AddPerson(h, "500", "Riêng máy Hương");
        Assert.Equal(0, Out(await h.HandleAsync("cloud.status", J())).GetProperty("pending").GetInt32());   // off: nothing queued
        await h.HandleAsync("cloud.setEnabled", J("""{"on":true}"""));
        Assert.Equal(1, await PeopleCount(h));   // back to the shared list (the local-only person is in the backup)
    }

    [Fact]
    public async Task restore_while_enabled_requires_confirmation_flag()
    {
        var n = await Ready(Host("nhien"));
        await n.HandleAsync("backup.now", J());
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        var name = Out(await n.HandleAsync("backup.status", J())).GetProperty("files")[0].GetProperty("name").GetString();
        var e = await Assert.ThrowsAsync<HostException>(() => n.HandleAsync("backup.restore", J($$"""{"name":"{{name}}","password":"saoluu123"}""")));
        Assert.Equal("Đang dùng dữ liệu chung: khôi phục sẽ đổi dữ liệu của cả hai máy. Hãy xác nhận lại.", e.Message);
    }

    [Fact]
    public async Task restore_while_enabled_republishes_after_restart()
    {
        var n = await Ready(Host("nhien"));
        await AddPerson(n, "101", "Một");
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        // The backup taken just before joining (shared data not yet on in it): the harder case.
        var name = Out(await n.HandleAsync("backup.status", J())).GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString()!).Single(x => x.Contains("truoc-du-lieu-chung"));
        await AddPerson(n, "102", "Hai sau sao lưu");
        await n.HandleAsync("cloud.syncNow", J());
        await n.HandleAsync("backup.restore", J($$"""{"name":"{{name}}","password":"saoluu123","affectsShared":true}"""));
        Assert.True(n.RestartRequested);

        var again = await Ready(Host("nhien"), backupPassword: false);   // the app relaunches on the same folder
        Assert.Equal(1, await PeopleCount(again));
        var h = await Ready(Host("huong"));
        await h.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        Assert.Equal(1, await PeopleCount(h));   // 102 became a tombstone in the shared data
        Assert.False(File.Exists(Path.Combine(_root, "nhien", AppHost.RepublishMarkerName)));
    }

    [Fact]
    public async Task backup_inspect_reports_people_count()
    {
        var n = await Ready(Host("nhien"));
        await AddPerson(n, "101", "Một");
        await n.HandleAsync("backup.now", J());
        var name = Out(await n.HandleAsync("backup.status", J())).GetProperty("files")[0].GetProperty("name").GetString();
        var r = Out(await n.HandleAsync("backup.inspect", J($$"""{"name":"{{name}}","password":"saoluu123"}""")));
        Assert.Equal(1, r.GetProperty("people").GetInt32());
    }

    [Fact]
    public async Task window_active_host_becomes_sender()
    {
        var n = await Ready(Host("nhien"));
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        var h = await Ready(Host("huong"));
        await h.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        await n.CloudLeaseTickAsync();
        await h.CloudLeaseTickAsync();
        Assert.True(Out(await n.HandleAsync("cloud.status", J())).GetProperty("isSender").GetBoolean());
        Assert.False(Out(await h.HandleAsync("cloud.status", J())).GetProperty("isSender").GetBoolean());
        h.SetWindowActive(true);
        await h.CloudLeaseTickAsync();
        await n.CloudLeaseTickAsync();
        Assert.False(Out(await h.HandleAsync("cloud.status", J())).GetProperty("isSender").GetBoolean());   // waits for n to stop
        _time.Advance(LeaseKeeper.TakeoverWait);
        await h.CloudLeaseTickAsync();
        Assert.True(Out(await h.HandleAsync("cloud.status", J())).GetProperty("isSender").GetBoolean());
        Assert.False(Out(await n.HandleAsync("cloud.status", J())).GetProperty("isSender").GetBoolean());
        Assert.Equal(Environment.MachineName, Out(await n.HandleAsync("cloud.status", J())).GetProperty("senderName").GetString());
    }

    [Fact]
    public async Task new_device_gets_unique_fp_group_when_enabled()
    {
        var n = await Ready(Host("nhien"));
        await n.HandleAsync("cloud.connect", J($$"""{"code":"{{Code}}","password":"chung1234"}"""));
        await n.HandleAsync("devices.save", J("""{"name":"Tầng 2","host":"10.9.0.86","username":"admin","password":"x","doorNo":1,"isCaptureDefault":false,"isFingerReader":false}"""));
        var group = n.Db.Devices.List().Single().FpGroup;
        Assert.Matches("^device-[0-9a-f]{8}$", group);
    }

    [Fact]
    public async Task disabled_host_behaves_like_before()
    {
        var n = await Ready(Host("nhien"));
        await AddPerson(n, "101", "Một");
        await n.HandleAsync("cloud.status", J());
        Assert.Equal(0, _cloud.Calls);
        Assert.Equal("device-1", (await AddDevice(n)).FpGroup);
    }

    private static async Task<Acs.Core.Model.DeviceRecord> AddDevice(AppHost h)
    {
        await h.HandleAsync("devices.save", J("""{"name":"Trệt","host":"10.9.0.84","username":"admin","password":"x","doorNo":1,"isCaptureDefault":true,"isFingerReader":false}"""));
        return h.Db.Devices.List().Single();
    }
}
