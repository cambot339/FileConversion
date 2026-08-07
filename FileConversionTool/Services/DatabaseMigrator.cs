using Microsoft.Extensions.Logging;
using PMC.Data.DF.CustomerPortal;

namespace FileConversionTool.Services;

/// <summary>
/// Executes the database upserts described by a <see cref="MigrationPlan"/>.
/// </summary>
public class DatabaseMigrator
{
    private readonly CustomerPortalContext _prodCtx;
    private readonly ILogger _logger;

    public DatabaseMigrator(CustomerPortalContext prodCtx, ILogger logger)
    {
        _prodCtx = prodCtx;
        _logger = logger;
    }

    /// <summary>
    /// Upserts every record in the plan into the production database.
    /// Each record is saved individually to isolate failures.
    /// Returns the number of records successfully upserted.
    /// </summary>
    public async Task<int> UpsertRecordsAsync(MigrationPlan plan)
    {
        int upserted = 0;

        foreach (MigrationItem item in plan.Items)
        {
            try
            {
                if (item.RecordExistsInProd)
                {
                    ResourceDownload existing = await _prodCtx.ResourceDownloads.FindAsync(item.TestRecord.ID)
                        ?? throw new InvalidOperationException($"Record ID={item.TestRecord.ID} was expected but not found.");

                    existing.Name        = item.TestRecord.Name;
                    existing.Description = item.TestRecord.Description;
                    existing.FilePath    = item.ProdDbFilePath;
                    existing.CategoryID  = item.TestRecord.CategoryID;
                    existing.ThumbnailID = item.TestRecord.ThumbnailID;
                    existing.FolderId    = item.TestRecord.FolderId;

                    _logger.LogInformation("Updating record ID={ID} ({Name})", item.TestRecord.ID, item.TestRecord.Name);
                }
                else
                {
                    _prodCtx.ResourceDownloads.Add(new ResourceDownload
                    {
                        ID          = item.TestRecord.ID,
                        Name        = item.TestRecord.Name,
                        Description = item.TestRecord.Description,
                        FilePath    = item.ProdDbFilePath,
                        CategoryID  = item.TestRecord.CategoryID,
                        ThumbnailID = item.TestRecord.ThumbnailID,
                        FolderId    = item.TestRecord.FolderId,
                    });

                    _logger.LogInformation("Inserting record ID={ID} ({Name})", item.TestRecord.ID, item.TestRecord.Name);
                }

                await _prodCtx.SaveChangesAsync();
                upserted++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upserting record ID={ID}: {Message}", item.TestRecord.ID, ex.Message);
            }
        }

        return upserted;
    }
}
