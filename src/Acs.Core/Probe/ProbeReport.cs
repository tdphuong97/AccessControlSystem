using System.Text;

namespace Acs.Core.Probe;

/// <summary>Plain-text report of the on-site probe. Any secret (password) is replaced by *** everywhere.</summary>
public sealed class ProbeReport(IEnumerable<string>? secrets = null)
{
    public const int MaxDetail = 500;
    private readonly string[] _secrets = (secrets ?? []).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
    private readonly List<string> _lines = [];
    private readonly object _gate = new();
    private int _failed;

    public int Failed => _failed;
    public string Text { get { lock (_gate) return string.Join(Environment.NewLine, _lines) + Environment.NewLine; } }

    public string Mask(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        foreach (var secret in _secrets) s = s.Replace(secret, "***", StringComparison.Ordinal);
        return s;
    }

    public void Note(string text) { lock (_gate) _lines.Add(Mask(text)); }

    public void Step(string name, bool ok, long elapsedMs, string? detail)
    {
        var d = Mask(detail).Replace('\r', ' ').Replace('\n', ' ');
        if (d.Length > MaxDetail) d = d[..MaxDetail] + "...";
        lock (_gate)
        {
            if (!ok) _failed++;
            _lines.Add($"[{(ok ? "OK" : "LỖI")}] {Mask(name)} | {elapsedMs} ms | {d}");
        }
    }

    public string Save(string folder, DateTime now)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"probe-report-{now:yyyyMMdd-HHmm}.txt");
        File.WriteAllText(path, Text, new UTF8Encoding(true));
        return path;
    }
}
