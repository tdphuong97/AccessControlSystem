namespace Acs.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Data_uses_ACS_DATA_when_set()
    {
        var old = Environment.GetEnvironmentVariable("ACS_DATA");
        var tmp = Path.Combine(Path.GetTempPath(), "acs-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("ACS_DATA", tmp);
            Assert.Equal(tmp, AppPaths.Data);
            Assert.Equal(Path.Combine(tmp, "acs.db"), AppPaths.DbFile);
            Assert.Equal(Path.Combine(tmp, "backups"), AppPaths.Backups);
            Assert.Equal(Path.Combine(tmp, "updates"), AppPaths.Updates);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ACS_DATA", old);
        }
    }

    [Fact]
    public void Data_defaults_to_LocalAppData_when_unset()
    {
        var old = Environment.GetEnvironmentVariable("ACS_DATA");
        try
        {
            Environment.SetEnvironmentVariable("ACS_DATA", null);
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessControlSystem");
            Assert.Equal(expected, AppPaths.Data);
            Assert.Null(AppPaths.DataOverride);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ACS_DATA", old);
        }
    }

    [Fact]
    public void Ui_is_under_exe_dir()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "ui"), AppPaths.Ui);
    }
}
