using Microsoft.Extensions.Logging;
using PMC.Data.DF.CustomerPortal;

namespace FileConversionTool.Services;

public class ThumbnailMigrationResult
{
    public int UpsertedCount { get; init; }
    public IReadOnlyDictionary<int, int> ThumbnailIdMap { get; init; } = new Dictionary<int, int>();
}

/// <summary>
/// Synchronizes ResourceThumbnail rows into the production database and returns the
/// resulting test-to-production ID mapping.
/// </summary>
public class ThumbnailMigrator
{
    private readonly CustomerPortalContext _prodCtx;
    private readonly ILogger _logger;

    public ThumbnailMigrator(CustomerPortalContext prodCtx, ILogger logger)
    {
        _prodCtx = prodCtx;
        _logger = logger;
    }

    public async Task<ThumbnailMigrationResult> UpsertRecordsAsync(MigrationPlan plan)
    {
        int upserted = 0;
        Dictionary<int, int> thumbnailIdMap = new();

        foreach (ThumbnailMigrationItem item in plan.ThumbnailItems)
        {
            try
            {
                ResourceThumbnail record;

                if (item.RecordExistsInProd)
                {
                    int prodRecordId = item.MatchedProdRecordId
                        ?? throw new InvalidOperationException($"A matched production thumbnail ID is required for test thumbnail ID={item.TestRecord.ID}.");

                    record = await _prodCtx.ResourceThumbnails.FindAsync(prodRecordId)
                        ?? throw new InvalidOperationException($"Thumbnail ID={prodRecordId} was expected but not found.");

                    _logger.LogInformation("Updating thumbnail ID={ID} ({Name})", prodRecordId, item.TestRecord.Name);
                }
                else
                {
                    record = new ResourceThumbnail();
                    _prodCtx.ResourceThumbnails.Add(record);
                    _logger.LogInformation("Inserting thumbnail for test ID={ID} ({Name})", item.TestRecord.ID, item.TestRecord.Name);
                }

                record.Name = item.TestRecord.Name;
                record.FilePath = item.ProdDbFilePath;

                await _prodCtx.SaveChangesAsync();

                thumbnailIdMap[item.TestRecord.ID] = record.ID;
                upserted++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upserting thumbnail ID={ID}: {Message}", item.TestRecord.ID, ex.Message);
            }
        }

        return new ThumbnailMigrationResult
        {
            UpsertedCount = upserted,
            ThumbnailIdMap = thumbnailIdMap,
        };
    }
}
