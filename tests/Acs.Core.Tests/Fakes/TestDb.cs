using Acs.Core.Security;
using Acs.Core.Storage;

namespace Acs.Core.Tests.Fakes;

public static class TestDb
{
    private static readonly List<string> Dirs = [];

    static TestDb() => AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupAll();

    /// <summary>A fresh database on a temp file with a fixed 32-byte key. Temp folders are removed at process exit.</summary>
    public static Database Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "acs-testdb-" + Guid.NewGuid().ToString("N"));
        lock (Dirs) Dirs.Add(dir);
        return new Database(Path.Combine(dir, "acs.db"), new DataProtector(Enumerable.Repeat((byte)7, 32).ToArray()));
    }

    /// <summary>Delete every temp folder created so far (dispose the databases first).</summary>
    public static void CleanupAll()
    {
        string[] dirs;
        lock (Dirs) { dirs = [.. Dirs]; Dirs.Clear(); }
        foreach (var d in dirs)
        {
            try { Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
