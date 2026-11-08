using Acs.Core.Host;

namespace Acs.Core.Tests.Host;

public sealed class UiScaleTests
{
    [Fact]
    public void The_default_window_keeps_the_designed_size()
        => Assert.Equal(1.0, UiScale.Fit(1164, 721));

    [Fact]
    public void A_bigger_window_scales_up_by_the_tighter_side()
        => Assert.Equal(1.25, UiScale.Fit(1600, 900));   // 1.38 wide, 1.25 tall

    [Fact]
    public void A_smaller_window_scales_down_in_steps_of_five_percent()
        => Assert.Equal(0.9, UiScale.Fit(1050, 660));     // 0.905 wide, 0.917 tall

    [Theory]
    [InlineData(1920, 1137, 1.5)]   // maximized on a big screen: capped
    [InlineData(720, 480, 0.8)]     // the smallest window: text stays readable
    public void The_scale_stays_within_limits(double w, double h, double scale)
        => Assert.Equal(scale, UiScale.Fit(w, h));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 700)]
    public void A_window_without_a_size_yet_keeps_one(double w, double h)
        => Assert.Equal(1.0, UiScale.Fit(w, h));
}
