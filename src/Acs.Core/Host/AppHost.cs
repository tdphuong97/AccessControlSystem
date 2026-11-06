using System.Text.Json;
using System.Text.Json.Serialization;
using Acs.Core.Backup;
using Acs.Core.Enroll;
using Acs.Core.Hik;
using Acs.Core.Import;
using Acs.Core.Reconcile;
using Acs.Core.Security;
using Acs.Core.Storage;
using Acs.Core.Sync;
using Acs.Core.Update;

namespace Acs.Core.Host;

/// <summary>
/// Composition root and the C3 command dispatcher. The WPF shell only forwards {cmd, args} to
/// <see cref="HandleAsync"/> and relays <see cref="Emitted"/> to the page.
/// </summary>
public sealed partial class AppHost
{
    public const string GenericError = "Có lỗi không lường trước. Xem Nhật ký.";
    public const string UpdatePlatform = "windows";

    /// <summary>JSON options the shell must use for results and events (camelCase, enums as camelCase strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IHikDeviceFactory _factory;
    private readonly TimeProvider _time;
    private readonly string _version;
    private readonly KeyStore _keys;
    private readonly string _dbFile;
    private readonly string _backupFolder;
    private readonly string _updatesFolder;
    private readonly int? _backupIterations;
    private readonly HttpClient? _ownHttp;
    private readonly Updater _updater;
    private readonly CancellationTokenSource _cts = new();
    private readonly IKeyProtector _protector;
    private readonly object _gate = new();
    private bool _started, _closed, _stopped, _restoring;
    private int _inflight;
    private int _installing;

    public const string RestoringMessage = "Đang khôi phục dữ liệu, đợi xong rồi thử lại.";
    public const string SlowNetworkMessage = "Mạng chậm hoặc mất kết nối. Hãy thử lại sau.";
    private const string UnknownPrefix = "Lệnh không có: ";

    /// <summary>Everything built on one Database; replaced as a whole (one reference assignment) after a restore.</summary>
    private sealed record Graph(Database Db, SyncEngine Sync, EnrollService Enroll, DeviceImporter Importer,
        Reconciler Reconciler, BackupService Backup);

    private volatile Graph _g = null!;

    public Database Db => _g.Db;
    public SyncEngine Sync => _g.Sync;
    public EnrollService Enroll => _g.Enroll;
    public DeviceImporter Importer => _g.Importer;
    public Reconciler Reconciler => _g.Reconciler;
    public BackupService Backup => _g.Backup;

    /// <summary>How long a feed check may take before it is given up (the installer download has its own, longer limit).</summary>
    public TimeSpan FeedTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Events for the page: devices.changed, person.changed, import.progress, reconcile.progress, update.available, update.progress, app.restart.</summary>
    public event Action<string, object?>? Emitted;

    /// <summary>Set by the App: runs the installer (/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1), then the App exits.</summary>
    public Func<string, Task>? RunInstaller { get; set; }

    /// <summary>Set by the App: applies the "start with Windows" setting (registry Run key).</summary>
    public Action<bool>? ApplyStartWithWindows { get; set; }

    /// <summary>True after a successful backup.restore: the App must relaunch itself (also announced as the app.restart event).</summary>
    public bool RestartRequested { get; private set; }

    private AppHost(string dataFolder, IKeyProtector protector, IHikDeviceFactory factory, TimeProvider time, string version,
        Updater? updater, int? backupIterations)
    {
        _factory = factory; _time = time; _version = version; _backupIterations = backupIterations;
        _keys = new KeyStore(dataFolder, protector);
        _dbFile = Path.Combine(dataFolder, "acs.db");
        _backupFolder = Path.Combine(dataFolder, "backups");
        _updatesFolder = Path.Combine(dataFolder, "updates");
        if (updater is null)
        {
            _ownHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _ownHttp.DefaultRequestHeaders.UserAgent.ParseAdd("AccessControlSystem");
            updater = new Updater(_ownHttp, Updater.ResolveFeed(), time);
        }
        _updater = updater;
        _protector = protector;
        _g = Build(OpenDb());
    }

    /// <param name="updater">Tests pass one that talks to a local server; the App leaves it null.</param>
    /// <param name="backupIterations">Tests lower the PBKDF2 cost; the App leaves it null.</param>
    public static AppHost Create(string dataFolder, IKeyProtector protector, IHikDeviceFactory factory, TimeProvider time,
        string currentVersion, Updater? updater = null, int? backupIterations = null)
    {
        Directory.CreateDirectory(dataFolder);
        return new AppHost(dataFolder, protector, factory, time, currentVersion, updater, backupIterations);
    }

    private Database OpenDb() => new(_dbFile, new DataProtector(_keys.GetOrCreateDataKey()));

    private Graph Build(Database db)
    {
        var sync = new SyncEngine(db, _factory, _time);
        var enroll = new EnrollService(db, _factory, sync, _time);
        var importer = new DeviceImporter(db, _factory, _time);
        var reconciler = new Reconciler(db, _factory, sync, importer, _time);
        var backup = _backupIterations is { } n
            ? new BackupService(db, _keys, _backupFolder, _time, n)
            : new BackupService(db, _keys, _backupFolder, _time);
        sync.DeviceChanged += _ => Emit("devices.changed", DeviceViews());
        sync.PersonChanged += no => Emit("person.changed", new { employeeNo = no });
        return new Graph(db, sync, enroll, importer, reconciler, backup);
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private void Emit(string name, object? data)
    {
        try { Emitted?.Invoke(name, data); } catch { /* a listener must not break the host */ }
    }

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        Sync.Start();
        await Task.Run(DailyBackup);
        StartUpdateTimer();
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            _closed = true;
            _updateTimer?.Dispose();
        }
        _cts.Cancel();
        var g = _g;
        await g.Sync.StopAsync();
        g.Db.Dispose();
        _ownHttp?.Dispose();
    }

    public async Task<object?> HandleAsync(string cmd, JsonElement args)
    {
        lock (_gate)
        {
            if (_closed) throw new HostException("Phần mềm đang đóng hoặc khởi động lại. Hãy đợi một chút.");
            if (_restoring) throw new HostException(RestoringMessage);
            _inflight++;
        }
        try { return await Dispatch(cmd, args); }
        catch (Exception ex) when (ex is EnrollException or HikException or BackupException or HostException or UpdateException)
        { throw; }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { throw; }   // shutdown
        catch (OperationCanceledException) { throw new HostException(SlowNetworkMessage); }   // timeout
        catch (InvalidOperationException ex) when (ex.Message.StartsWith(UnknownPrefix, StringComparison.Ordinal)) { throw; }
        catch (Exception ex)
        {
            try { Db.Log.Add(null, null, "Lỗi", false, $"{cmd}: {ex.GetType().Name}: {ex.Message}"); } catch { /* the log itself may be failing */ }
            throw new HostException(GenericError);
        }
        finally { lock (_gate) _inflight--; }
    }

    private async Task<object?> Dispatch(string cmd, JsonElement a) => cmd switch
    {
        "app.state" => AppState(),
        "people.list" => PeopleList(a),
        "people.nextNo" => Enroll.NextEmployeeNo(),
        "people.create" => PeopleCreate(a),
        "people.get" => PersonDetailOf(Str(a, "employeeNo")),
        "people.update" => PeopleUpdate(a),
        "people.lock" => PeopleAction(a, Enroll.Lock),
        "people.unlock" => PeopleAction(a, Enroll.Unlock),
        "people.delete" => PeopleDelete(a),
        "face.capture" => await FaceCapture(a),
        "face.save" => FaceSave(a),
        "finger.capture" => await FingerCapture(a),
        "finger.delete" => FingerDelete(a),
        "sync.person" => SyncPerson(Str(a, "employeeNo")),
        "sync.retry" => SyncRetry(a),
        "devices.list" => DeviceViews(),
        "devices.save" => DevicesSave(a),
        "devices.test" => await DevicesTest(a),
        "devices.delete" => DevicesDelete(a),
        "reconcile.plan" => await ReconcilePlanAsync(),
        "reconcile.apply" => await ReconcileApplyAsync(a),
        "import.start" => await ImportStart(a),
        "backup.status" => BackupStatusView(),
        "backup.setPassword" => BackupSetPassword(a),
        "backup.now" => BackupNow(),
        "backup.setExtraFolder" => BackupSetExtraFolder(a),
        "backup.restore" => await BackupRestore(a),
        "update.check" => await UpdateCheck(),
        "update.install" => await UpdateInstall(),
        "settings.get" => SettingsGet(),
        "settings.set" => SettingsSet(a),
        "log.list" => LogList(a),
        _ => throw new InvalidOperationException(UnknownPrefix + cmd),
    };

    // ---- argument helpers ----
    private static bool Has(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null;

    private static string? OptStr(JsonElement a, string name) =>
        Has(a, name) && a.GetProperty(name).ValueKind == JsonValueKind.String ? a.GetProperty(name).GetString() : null;

    private static string Str(JsonElement a, string name) =>
        OptStr(a, name) ?? throw new HostException("Thiếu thông tin cần thiết. Hãy thử lại.");

    private static int? OptInt(JsonElement a, string name) =>
        Has(a, name) && a.GetProperty(name).TryGetInt32(out var n) ? n : null;

    private static int Int(JsonElement a, string name) =>
        OptInt(a, name) ?? throw new HostException("Thiếu thông tin cần thiết. Hãy thử lại.");

    private static bool Bool(JsonElement a, string name, bool dflt = false) =>
        Has(a, name) && a.GetProperty(name).ValueKind is JsonValueKind.True or JsonValueKind.False
            ? a.GetProperty(name).GetBoolean() : dflt;
}
