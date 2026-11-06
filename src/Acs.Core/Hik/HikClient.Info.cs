using System.Text.Json;
using System.Xml.Linq;

namespace Acs.Core.Hik;

public sealed partial class HikClient
{
    public async Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct)
    {
        var body = await GetAsync("/ISAPI/System/deviceInfo?format=json", ct);
        return new DeviceInfo(
            Find(body, "model") ?? "",
            Find(body, "serialNumber") ?? "",
            Find(body, "firmwareVersion") ?? "");
    }

    public async Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct)
    {
        var body = await GetAsync("/ISAPI/AccessControl/capabilities?format=json", ct);
        return new DeviceCapabilities(
            FindBool(body, "isSupportCaptureFace"),
            FindBool(body, "isSupportCaptureFingerPrint"),
            FindInt(body, "maxPersonNum"));
    }

    // Reads a scalar by key name anywhere in a JSON or XML body; null when absent.
    internal static string? Find(string body, string key)
    {
        var t = HikClient.StripBom(body);
        try
        {
            if (t.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(t);
                return FindJson(doc.RootElement, key);
            }
            if (t.StartsWith('<'))
                return XDocument.Parse(t).Descendants().FirstOrDefault(e => e.Name.LocalName == key && !e.HasElements)?.Value;
        }
        catch (Exception e) when (e is JsonException or System.Xml.XmlException) { }
        return null;
    }

    private static string? FindJson(JsonElement e, string key)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == key && p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null))
                    return p.Value.ToString();
                var r = FindJson(p.Value, key);
                if (r is not null) return r;
            }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var i in e.EnumerateArray()) { var r = FindJson(i, key); if (r is not null) return r; }
        return null;
    }

    internal static bool? FindBool(string body, string key)
        => Find(body, key) is { } s && bool.TryParse(s, out var b) ? b : null;

    internal static int? FindInt(string body, string key)
        => Find(body, key) is { } s && int.TryParse(s, out var n) ? n : null;
}
