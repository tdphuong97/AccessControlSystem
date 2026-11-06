namespace Acs.Core;

/// <summary>Đường dẫn dữ liệu của ứng dụng. Biến môi trường ACS_DATA ghi đè thư mục dữ liệu.</summary>
public static class AppPaths
{
    public const string DataEnvVar = "ACS_DATA";

    public static string? DataOverride
    {
        get
        {
            var v = Environment.GetEnvironmentVariable(DataEnvVar);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
    }

    public static string Data => DataOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessControlSystem");

    public static string Backups => Path.Combine(Data, "backups");
    public static string Updates => Path.Combine(Data, "updates");
    public static string DbFile => Path.Combine(Data, "acs.db");
    public static string Ui => Path.Combine(AppContext.BaseDirectory, "ui");
}
