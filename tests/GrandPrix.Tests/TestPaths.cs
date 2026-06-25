namespace GrandPrix.Tests;

internal static class TestPaths
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Levels")) &&
                dir.GetFiles("EntelectGrandPrix.sln*").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root (with Levels/ and the .sln).");
    }

    public static string Level(int n) => Path.Combine(RepoRoot(), "Levels", $"{n}.txt");
}
