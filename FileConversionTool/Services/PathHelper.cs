namespace FileConversionTool.Services;

/// <summary>
/// Converts database file paths between the test and production drive-letter conventions.
/// </summary>
public class PathHelper
{
    private readonly string _testDriveLetter;
    private readonly string _prodDriveLetter;

    public PathHelper(string testDriveLetter, string prodDriveLetter)
    {
        _testDriveLetter = testDriveLetter.TrimEnd(':');
        _prodDriveLetter = prodDriveLetter.TrimEnd(':');
    }

    /// <summary>
    /// Extracts the relative portion of a path stored in the test database.
    /// </summary>
    public string GetRelativePart(string dbPath)
    {
        string normalized = dbPath.Replace('/', '\\');
        string drivePrefix = _testDriveLetter + ":";

        if (normalized.StartsWith(drivePrefix + "\\", StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(drivePrefix.Length).TrimStart('\\');

        // Unknown prefix – use the file name only to avoid unintended path traversal.
        return Path.GetFileName(normalized);
    }

    /// <summary>Maps a DB file path to an absolute path on the test file system.</summary>
    public string MapToTestFileSystem(string dbPath) =>
        _testDriveLetter + ":\\" + GetRelativePart(dbPath);

    /// <summary>
    /// Maps a test DB file path to the production drive-letter path.
    /// Used for both the physical destination on disk and the path stored in the production database.
    /// </summary>
    public string MapToProdPath(string dbPath) =>
        _prodDriveLetter + ":\\" + GetRelativePart(dbPath);
}
