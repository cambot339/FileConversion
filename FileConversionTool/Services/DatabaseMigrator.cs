using Microsoft.Extensions.Logging;
using PMC.Data.DF.CustomerPortal;

namespace FileConversionTool.Services;

public class DatabaseMigrationResult
{
    public int ResourceDownloadRecordsUpserted { get; init; }
    public int ResourceThumbnailRecordsUpserted { get; init; }
    public int TotalRecordsUpserted => ResourceDownloadRecordsUpserted + ResourceThumbnailRecordsUpserted;
}

/// <summary>
/// Executes the database upserts described by a <see cref="MigrationPlan"/>.
/// </summary>
public class DatabaseMigrator
{
    private readonly CustomerPortalContext _prodCtx;
    private readonly ThumbnailMigrator _thumbnailMigrator;
    private readonly ILogger _logger;

    public DatabaseMigrator(CustomerPortalContext prodCtx, ThumbnailMigrator thumbnailMigrator, ILogger logger)
    {
        _prodCtx = prodCtx;
        _thumbnailMigrator = thumbnailMigrator;
        _logger = logger;
    }

    /// <summary>
    /// Upserts every record in the plan into the production database.
    /// Each record is saved individually to isolate failures.
    /// Returns the number of records successfully upserted.
    /// </summary>
    public async Task<DatabaseMigrationResult> UpsertRecordsAsync(MigrationPlan plan)
    {
        ThumbnailMigrationResult thumbnailResult = await _thumbnailMigrator.UpsertRecordsAsync(plan);
        int upserted = 0;

        foreach (MigrationItem item in plan.Items)
        {
            try
            {
                if (item.RecordExistsInProd)
                {
                    int prodRecordId = item.MatchedProdRecordId
                        ?? throw new InvalidOperationException($"A matched production record ID is required for test record ID={item.TestRecord.ID}.");

                    ResourceDownload existing = await _prodCtx.ResourceDownloads.FindAsync(prodRecordId)
                        ?? throw new InvalidOperationException($"Record ID={prodRecordId} was expected but not found.");

                    existing.Name        = item.TestRecord.Name;
                    existing.Description = item.TestRecord.Description;
                    existing.FilePath    = item.ProdDbFilePath;
                    existing.CategoryID  = item.TestRecord.CategoryID;
                    existing.ThumbnailID = ResolveThumbnailId(item.TestRecord.ThumbnailID, thumbnailResult.ThumbnailIdMap);
                    existing.FolderId    = item.TestRecord.FolderId;

                    _logger.LogInformation("Updating record ID={ID} ({Name})", prodRecordId, item.TestRecord.Name);
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
                        ThumbnailID = ResolveThumbnailId(item.TestRecord.ThumbnailID, thumbnailResult.ThumbnailIdMap),
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

        return new DatabaseMigrationResult
        {
            ResourceDownloadRecordsUpserted = upserted,
            ResourceThumbnailRecordsUpserted = thumbnailResult.UpsertedCount,
        };
    }

    private static int? ResolveThumbnailId(int? testThumbnailId, IReadOnlyDictionary<int, int> thumbnailIdMap)
    {
        if (!testThumbnailId.HasValue)
            return null;

        if (thumbnailIdMap.TryGetValue(testThumbnailId.Value, out int prodThumbnailId))
            return prodThumbnailId;

        throw new InvalidOperationException(
            $"ResourceDownload references thumbnail ID={testThumbnailId.Value}, but no production thumbnail mapping was created.");
    }
}
