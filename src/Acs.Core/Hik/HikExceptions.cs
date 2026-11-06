namespace Acs.Core.Hik;

// Contract C2. Message is Vietnamese with the next step; Detail is raw device text (at most 500 chars).
public class HikException(string message, string? detail = null) : Exception(message)
{
    public string? Detail { get; } = detail;
}

/// <summary>Offline, timeout, 5xx, busy.</summary>
public sealed class HikTransientException(string m, string? d = null) : HikException(m, d);

/// <summary>401 after one session rebuild.</summary>
public sealed class HikAuthException(string m, string? d = null) : HikException(m, d);

public sealed class HikRejectedException(string code, string m, string? d = null) : HikException(m, d)
{
    public string Code { get; } = code;
}

public sealed class HikNotSupportedException(string m, string? d = null) : HikException(m, d);
