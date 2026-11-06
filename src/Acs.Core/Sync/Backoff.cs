namespace Acs.Core.Sync;

/// <summary>Retry delays after transient failures: 30, 60, 120, 300, 600, 900 s, then 900 s for ever.</summary>
public static class Backoff
{
    private static readonly int[] Seconds = [30, 60, 120, 300, 600, 900];

    /// <param name="attempts">Attempts already recorded for the job (0 on the first failure).</param>
    public static TimeSpan After(int attempts) =>
        TimeSpan.FromSeconds(Seconds[Math.Clamp(attempts, 0, Seconds.Length - 1)]);
}
