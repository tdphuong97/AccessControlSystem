using System.Text;

namespace Acs.Core.Cloud;

/// <summary>
/// The "sender" lease: only its holder runs the device send queue, so two PCs never log in to the same attendance
/// device at once. The PC whose window is open takes it from a PC that only runs in the tray. Without the cloud for
/// more than 2 minutes a PC sends on its own (sends can be repeated safely). The holder's PC name travels sealed.
/// </summary>
public sealed class LeaseKeeper(CloudClient client, byte[] key, string pcId, string pcName, TimeProvider time)
{
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromMinutes(2);
    /// <summary>After taking the lease from a PC whose lease had not run out: that PC renews every 30 s and stops when
    /// refused, so wait longer than one renewal before sending.</summary>
    public static readonly TimeSpan TakeoverWait = TimeSpan.FromSeconds(45);
    private DateTimeOffset _lastContact = time.GetUtcNow();
    private DateTimeOffset _sendFrom = DateTimeOffset.MinValue;
    private bool _last;

    /// <summary>Name of the PC that sends to the devices now (null until known).</summary>
    public string? HolderName { get; private set; }

    public async Task<bool> TickAsync(bool windowActive, CancellationToken ct)
    {
        try
        {
            var label = CloudCrypto.Seal(key, "lease", "sender", Encoding.UTF8.GetBytes(pcName));
            var l = await client.LeaseAsync(pcId, windowActive, label, ct);
            var now = time.GetUtcNow();
            _lastContact = now;
            HolderName = l.Holder == pcId ? pcName : Open(l.Label);
            if (l.Took) _sendFrom = now + TakeoverWait;
            return _last = l.Granted && now >= _sendFrom;
        }
        catch (CloudOfflineException)
        {
            if (time.GetUtcNow() - _lastContact > OfflineGrace) { HolderName = pcName; return _last = true; }
            return _last;
        }
    }

    public async Task ReleaseAsync(CancellationToken ct)
    {
        try { await client.ReleaseLeaseAsync(pcId, ct); }
        catch (CloudOfflineException) { /* it expires on its own after 90 s */ }
    }

    private string? Open(string? label)
    {
        if (label is null) return null;
        try { return Encoding.UTF8.GetString(CloudCrypto.Open(key, "lease", "sender", label)); }
        catch (CloudException) { return null; }
    }
}
