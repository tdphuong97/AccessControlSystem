using System.Text.RegularExpressions;

namespace Acs.Core.Enroll;

/// <summary>
/// On site every name on the devices starts with the company code: "ABC. Lan", "XYZ.KT.Minh". The code before the
/// first dot (letters and digits, at most 6) is the person's group; any other name has none.
/// </summary>
public static partial class GroupRules
{
    [GeneratedRegex(@"^\s*([\p{L}\p{Nd}]{1,6})\s*\.")]
    private static partial Regex Prefix();

    public static string? FromName(string? name)
    {
        var m = Prefix().Match(name ?? "");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }
}
