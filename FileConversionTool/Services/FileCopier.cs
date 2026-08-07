using Microsoft.Extensions.Logging;

namespace FileConversionTool.Services;

/// <summary>
/// Executes the physical file copies described by a <see cref="MigrationPlan"/>.
/// </summary>
public class FileCopier
{
    private readonly ILogger _logger;

    public FileCopier(ILogger logger) => _logger = logger;

    /// <summary>
    /// Copies all files in the plan that have a source file present on disk.
    /// Returns the number of files successfully copied.
    /// </summary>
    public int CopyFiles(MigrationPlan plan)
    {
        int copied = 0;

        foreach (MigrationItem item in plan.Items.Where(i => i.FileExists))
        {
            try
            {
                string? destDir = Path.GetDirectoryName(item.DestFilePath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                    _logger.LogDebug("Created directory: {Dir}", destDir);
                }

                File.Copy(item.SourceFilePath, item.DestFilePath, overwrite: true);
                _logger.LogInformation("Copied file: {Src} -> {Dest}", item.SourceFilePath, item.DestFilePath);
                copied++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy file for record ID={ID}: {Message}", item.TestRecord.ID, ex.Message);
            }
        }

        foreach (ThumbnailMigrationItem item in plan.ThumbnailItems.Where(i => i.FileExists))
        {
            try
            {
                string? destDir = Path.GetDirectoryName(item.DestFilePath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                    _logger.LogDebug("Created directory: {Dir}", destDir);
                }

                File.Copy(item.SourceFilePath, item.DestFilePath, overwrite: true);
                _logger.LogInformation("Copied thumbnail file: {Src} -> {Dest}", item.SourceFilePath, item.DestFilePath);
                copied++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy thumbnail file for record ID={ID}: {Message}", item.TestRecord.ID, ex.Message);
            }
        }

        return copied;
    }
}
