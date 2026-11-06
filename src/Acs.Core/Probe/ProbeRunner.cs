using System.Diagnostics;
using Acs.Core.Hik;

namespace Acs.Core.Probe;

/// <summary>
/// Runs the on-site probe. Writes ONLY employeeNo 99999 (the test person) and always deletes it afterwards.
/// </summary>
public sealed class ProbeRunner(IHikDevice main, IHikDevice? peer, Func<string, Task> waitForPerson, ProbeReport report)
{
    public const string TestNo = "99999";
    public const string TestName = "TEST ACS";

    private static readonly HikUserWrite TestUser = new(TestNo, TestName, "2026-01-01T00:00:00", "2030-12-31T23:59:59", true, 1);

    private byte[]? _face;
    private string? _template;

    /// <summary>Returns true when every step succeeded.</summary>
    public async Task<bool> RunAsync(CancellationToken ct)
    {
        var started = false;
        try
        {
            // Safety: refuse to touch an existing 99999 that is not our test person.
            foreach (var (label, dev) in Devices())
            {
                try
                {
                    var existing = await dev.FindUserAsync(TestNo, ct);
                    if (existing is not null && existing.Name != TestName)
                    {
                        report.Step($"{label}: kiểm tra mã 99999 còn trống", false, 0,
                            $"Mã 99999 đã được dùng cho người khác (\"{existing.Name}\"). Dừng để không ghi đè.");
                        return false;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    report.Step($"{label}: kiểm tra mã 99999 còn trống", false, 0, Describe(ex));
                    return false;
                }
            }
            started = true;
            try
            {
                await RunStepsAsync(ct);
            }
            catch (OperationCanceledException)
            {
                report.Step("Huỷ bởi người dùng (Ctrl+C)", false, 0, "Đang dọn dẹp người thử 99999.");
            }
        }
        finally
        {
            if (started)
                foreach (var (label, dev) in Devices())
                    await TimedVoid($"{label}: xoá người thử 99999 (dọn dẹp)", () => dev.DeleteUserAsync(TestNo, CancellationToken.None), "đã xoá");
        }
        return report.Failed == 0;
    }

    private IEnumerable<(string Label, IHikDevice Dev)> Devices()
    {
        yield return ("Máy chính", main);
        if (peer is not null) yield return ("Máy phụ", peer);
    }

    private async Task RunStepsAsync(CancellationToken ct)
    {
        // 1. device info + capabilities
        foreach (var (label, dev) in Devices())
        {
            await Timed($"{label}: thông tin thiết bị", () => dev.GetDeviceInfoAsync(ct), i => $"model={i.Model} serial={i.Serial} firmware={i.Firmware}");
            await Timed($"{label}: khả năng thiết bị", () => dev.GetCapabilitiesAsync(ct),
                c => $"chụp mặt={c.CaptureFace?.ToString() ?? "?"} chụp vân tay={c.CaptureFingerprint?.ToString() ?? "?"} tối đa người={c.MaxUsers?.ToString() ?? "?"}");
        }

        // 2. user count (count only; other people are never recorded)
        await Timed("Máy chính: đếm số người", async () =>
        {
            var n = 0;
            await foreach (var _ in main.ListUsersAsync(ct)) n++;
            return n;
        }, n => $"{n} người");

        // 3. create test user
        var created = await TimedVoid("Máy chính: tạo người thử 99999 \"TEST ACS\"", () => main.UpsertUserAsync(TestUser, ct), "đã gửi");
        if (!created) { report.Note("Bỏ qua các bước sau vì không tạo được người thử."); return; }
        await Timed("Máy chính: đọc lại người thử", () => main.FindUserAsync(TestNo, ct),
            u => u is null ? "KHÔNG thấy người 99999 sau khi tạo" : $"thấy: {u.Name}, enabled={u.Enabled}, mặt={u.FaceCount}, vân tay={u.FingerCount}");

        // 4. face
        await waitForPerson("Mời người thử đứng trước máy rồi bấm Enter để chụp khuôn mặt.");
        var face = await Timed("Máy chính: chụp khuôn mặt", () => main.CaptureFaceAsync(ct), FaceSummary);
        if (face is not null)
        {
            _face = face;
            await TimedVoid("Máy chính: ghi khuôn mặt", () => main.SetFaceAsync(TestNo, face, ct), $"đã gửi {face.Length} byte");
            await Timed("Máy chính: kiểm tra có khuôn mặt", () => main.HasFaceAsync(TestNo, ct), h => h ? "có" : "KHÔNG có sau khi ghi");
            await Timed("Máy chính: tải khuôn mặt về", () => main.DownloadFaceAsync(TestNo, ct), b => b is null ? "thiết bị không giữ ảnh (null)" : FaceSummary(b));
        }

        // 5. fingerprint
        await waitForPerson("Mời người thử đứng trước máy rồi bấm Enter để lấy vân tay (ngón 1).");
        var finger = await Timed("Máy chính: lấy vân tay ngón 1", () => main.CaptureFingerprintAsync(1, ct),
            f => $"chất lượng={f.Quality} độ dài mẫu={f.TemplateBase64.Length}");
        if (finger is not null)
        {
            _template = finger.TemplateBase64;
            await TimedVoid("Máy chính: ghi vân tay ngón 1", () => main.SetFingerprintAsync(TestNo, 1, finger.TemplateBase64, 1, ct), "đã gửi");
            await Timed("Máy chính: đọc lại vân tay", () => main.ReadFingerprintsAsync(TestNo, ct),
                l => $"{l.Count} vân tay: " + string.Join(", ", l.Select(x => $"ngón {x.FingerId} độ dài {x.TemplateBase64.Length}"))
                     + (l.Any(x => x.FingerId == 1 && x.TemplateBase64 == finger.TemplateBase64) ? " (khớp mẫu đã ghi)" : " (KHÔNG khớp mẫu đã ghi)"));
        }

        // 6. peer
        if (peer is not null)
        {
            var ok = await TimedVoid("Máy phụ: tạo người thử 99999", () => peer.UpsertUserAsync(TestUser, ct), "đã gửi");
            if (!ok) return;
            if (_face is not null)
                await TimedVoid("Máy phụ: ghi khuôn mặt (từ máy chính)", () => peer.SetFaceAsync(TestNo, _face, ct), "đã gửi");
            else report.Note("Máy phụ: bỏ qua ghi khuôn mặt vì máy chính chưa có ảnh.");
            if (_template is not null)
                await TimedVoid("Máy phụ: ghi mẫu vân tay (từ máy chính)", () => peer.SetFingerprintAsync(TestNo, 1, _template, 1, ct), "đã gửi");
            else report.Note("Máy phụ: bỏ qua ghi vân tay vì máy chính chưa có mẫu.");
            await Timed("Máy phụ: kiểm tra lại", async () =>
            {
                var u = await peer.FindUserAsync(TestNo, ct);
                var f = await peer.ReadFingerprintsAsync(TestNo, ct);
                var h = await peer.HasFaceAsync(TestNo, ct);
                return (u, f, h);
            }, r => r.u is null ? "KHÔNG thấy người 99999" : $"thấy {r.u.Name}; mặt={(r.h ? "có" : "không")}; vân tay={r.f.Count}");
        }
    }

    private static string FaceSummary(byte[] b) =>
        $"{b.Length} byte, đầu={Convert.ToHexString(b.AsSpan(0, Math.Min(8, b.Length)))}";

    private Task<bool> TimedVoid(string name, Func<Task> action, string okText) =>
        RunStep(name, async () => { await action(); return true; }, _ => okText).ContinueWith(t => t.Result.ok);

    private async Task<T?> Timed<T>(string name, Func<Task<T>> action, Func<T, string> summary) =>
        (await RunStep(name, action, summary)).value;

    private async Task<(bool ok, T? value)> RunStep<T>(string name, Func<Task<T>> action, Func<T, string> summary)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await action();
            report.Step(name, true, sw.ElapsedMilliseconds, summary(r));
            return (true, r);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            report.Step(name, false, sw.ElapsedMilliseconds, Describe(ex));
            return (false, default);
        }
    }

    private static string Describe(Exception ex) =>
        ex is HikException h ? $"{ex.GetType().Name}: {h.Message}" + (h.Detail is null ? "" : $" | {h.Detail}") : $"{ex.GetType().Name}: {ex.Message}";
}
