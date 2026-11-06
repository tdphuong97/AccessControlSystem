using System.Text.Encodings.Web;
using System.Text.Json;

namespace Acs.Core.Hik;

/// <summary>Shared serializer settings for everything sent to a device. Vietnamese goes out as raw UTF-8, not \uXXXX.</summary>
internal static class HikJson
{
    internal static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
