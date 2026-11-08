namespace Acs.Core.Host;

/// <summary>
/// The whole page grows and shrinks with the window: the default window (its page area is about 1160 x 720) shows
/// it at 100%, a bigger or smaller window by the tighter of the two sides, in 5% steps, between 80% and 150%.
/// </summary>
public static class UiScale
{
    public const double DesignWidth = 1160, DesignHeight = 720, Min = 0.8, Max = 1.5;

    /// <param name="width">Width of the page area in device-independent pixels.</param>
    /// <param name="height">Height of the page area in device-independent pixels.</param>
    public static double Fit(double width, double height)
    {
        if (!(width > 0) || !(height > 0)) return 1.0;
        var f = Math.Min(width / DesignWidth, height / DesignHeight);
        return Math.Round(Math.Clamp(Math.Round(f * 20) / 20, Min, Max), 2);
    }
}
