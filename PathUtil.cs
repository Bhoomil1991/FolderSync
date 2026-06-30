namespace FolderSync;

public static class PathUtil
{
    /// <summary>
    /// True if <paramref name="a"/> and <paramref name="b"/> are the same folder, or one is
    /// nested inside the other. Used to stop a mirror sync from targeting (and deleting) the
    /// source — or looping by writing into a subfolder of itself.
    /// </summary>
    public static bool SameOrNested(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        string na = Normalize(a);
        string nb = Normalize(b);
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;
        char sep = Path.DirectorySeparatorChar;

        return na.Equals(nb, ic)
            || na.StartsWith(nb + sep, ic)
            || nb.StartsWith(na + sep, ic);
    }

    private static string Normalize(string p)
    {
        try { p = Path.GetFullPath(p); } catch { /* keep as-is if it can't be resolved */ }
        return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
