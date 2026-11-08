using Acs.Core.Enroll;

namespace Acs.Core.Tests.Enroll;

public sealed class GroupRulesTests
{
    [Theory]
    [InlineData("ABC. Lan", "ABC")]
    [InlineData("XYZ.KT.Minh", "XYZ")]
    [InlineData("Kho.Van Tai", "KHO")]
    [InlineData("  deF . Minh", "DEF")]
    [InlineData("QRST.An", "QRST")]
    public void A_short_code_before_the_first_dot_is_the_group(string name, string group)
        => Assert.Equal(group, GroupRules.FromName(name));

    [Theory]
    [InlineData("BAOVE")]
    [InlineData("Tran Thi Mai")]
    [InlineData("Nguyen Van A. B")]      // words with spaces before the dot: not a code
    [InlineData("TOOLONG. An")]          // more than 6 characters
    [InlineData(". An")]
    [InlineData("A-B. An")]
    [InlineData("")]
    public void A_name_without_a_code_has_no_group(string name)
        => Assert.Null(GroupRules.FromName(name));
}
