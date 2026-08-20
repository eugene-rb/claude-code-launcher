using System.IO;

namespace ClaudeLauncher.App.Services;

/// <summary>Compares working directories the way "one project = one directory" management requires:
/// case-insensitively and independent of a trailing separator or relative segments.</summary>
public static class WorkingDirectoryComparer
{
    public static bool AreSame(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
