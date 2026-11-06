using System.Text;
using Acs.Core.Hik;
using Acs.Core.Probe;

Console.OutputEncoding = Encoding.UTF8;

const string Usage = """
AccessControlSystem-Probe - công cụ kiểm tra máy chấm công Hikvision tại chỗ.

Cách dùng:
  AccessControlSystem-Probe.exe --host URL --user TÀI_KHOẢN --pass MẬT_KHẨU
      [--peer URL --peer-user TÀI_KHOẢN --peer-pass MẬT_KHẨU] [--out THƯ_MỤC]

Ví dụ:
  AccessControlSystem-Probe.exe --host http://192.168.1.50 --user admin --pass ******

Công cụ chỉ tạo, sửa và xoá duy nhất người thử mã 99999 (TEST ACS), rồi tự xoá khi xong.
Kết quả ghi vào probe-report-yyyyMMdd-HHmm.txt (mật khẩu được che bằng ***). Gửi file này lại cho người hỗ trợ.
Bấm Ctrl+C để dừng; công cụ vẫn xoá người thử trước khi thoát.
""";

var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("--")) { opts[args[i][2..]] = args[i + 1]; i++; }
    else if (args[i] is "-h" or "--help" or "/?") { Console.WriteLine(Usage); return 0; }
    else { Console.WriteLine($"Tham số không hợp lệ: {args[i]}\n"); Console.WriteLine(Usage); return 2; }
}
if (!opts.TryGetValue("host", out var host) || !opts.TryGetValue("user", out var user) || !opts.TryGetValue("pass", out var pass))
{
    Console.WriteLine(Usage);
    return 2;
}
opts.TryGetValue("peer", out var peerHost);
opts.TryGetValue("peer-user", out var peerUser);
opts.TryGetValue("peer-pass", out var peerPass);
if (peerHost is not null && (peerUser is null || peerPass is null))
{
    Console.WriteLine("Có --peer thì cần cả --peer-user và --peer-pass.\n");
    Console.WriteLine(Usage);
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Console.WriteLine("Đang dừng và dọn dẹp..."); };

var report = new ProbeReport([pass, peerPass ?? ""]);
using var main = new HikClient(host, user, pass);
using var peer = peerHost is null ? null : new HikClient(peerHost, peerUser!, peerPass!);

Task Wait(string message)
{
    Console.WriteLine(message);
    Console.ReadLine();
    return Task.CompletedTask;
}

var ok = await new ProbeRunner(main, peer, Wait, report).RunAsync(cts.Token);
Console.WriteLine();
Console.WriteLine(report.Text);
var path = report.Save(opts.TryGetValue("out", out var outDir) ? outDir : Directory.GetCurrentDirectory(), DateTime.Now);
Console.WriteLine($"Đã ghi báo cáo: {Path.GetFullPath(path)}");
Console.WriteLine(ok ? "Tất cả các bước đều OK." : "Có bước LỖI. Hãy gửi file báo cáo cho người hỗ trợ.");
return ok ? 0 : 1;
