using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PMC.Data.DF.CustomerPortal;
using PMC.Data.DF.CustomerPortalOrchTest;

namespace FileConversionTool.Services;

/// <summary>
/// Reads the current state of both databases and the test file system to produce a
/// <see cref="MigrationPlan"/> without making any changes.
/// </summary>
public class PreCopyAnalyzer
{
    private readonly CustomerPortalOrchTestContext _testCtx;
    private readonly CustomerPortalContext _prodCtx;
    private readonly PathHelper _pathHelper;
    private readonly ILogger _logger;

    public PreCopyAnalyzer(
        CustomerPortalOrchTestContext testCtx,
        CustomerPortalContext prodCtx,
        PathHelper pathHelper,
        ILogger logger)
    {
        _testCtx = testCtx;
        _prodCtx = prodCtx;
        _pathHelper = pathHelper;
        _logger = logger;
    }

    /// <summary>
    /// Analyses both databases and the test file system.
    /// Returns a plan describing every file copy and database record change that will be needed.
    /// No files or database rows are modified.
    /// </summary>
    public async Task<MigrationPlan> AnalyzeAsync()
    {
        _logger.LogInformation("Pre-copy analysis: reading test database...");
        List<PMC.Data.DF.CustomerPortalOrchTest.ResourceDownload> testRecords =
            await _testCtx.ResourceDownloads.AsNoTracking().ToListAsync();

        _logger.LogInformation("Pre-copy analysis: found {Count} ResourceDownload record(s) in test database.", testRecords.Count);

        // Load all existing prod IDs in one query.
        HashSet<int> prodIds = (await _prodCtx.ResourceDownloads
            .AsNoTracking()
            .Select(r => r.ID)
            .ToListAsync())
            .ToHashSet();

        _logger.LogInformation("Pre-copy analysis: found {Count} existing ResourceDownload record(s) in production database.", prodIds.Count);

        var items = new List<MigrationItem>(testRecords.Count);

        foreach (var testRecord in testRecords)
        {
            string sourceFilePath = _pathHelper.MapToTestFileSystem(testRecord.FilePath);
            string destFilePath   = _pathHelper.MapToProdPath(testRecord.FilePath);
            string prodDbFilePath = _pathHelper.MapToProdPath(testRecord.FilePath);
            bool fileExists       = File.Exists(sourceFilePath);

            if (!fileExists)
                _logger.LogWarning("Pre-copy analysis: source file not found for record ID={ID}: {Path}", testRecord.ID, sourceFilePath);

            items.Add(new MigrationItem
            {
                TestRecord         = testRecord,
                SourceFilePath     = sourceFilePath,
                DestFilePath       = destFilePath,
                ProdDbFilePath     = prodDbFilePath,
                FileExists         = fileExists,
                RecordExistsInProd = prodIds.Contains(testRecord.ID),
            });
        }

        var plan = MigrationPlan.From(items);

        _logger.LogInformation(
            "Pre-copy analysis complete. Files to copy: {Copy}, Missing files: {Missing}, " +
            "Records to insert: {Insert}, Records to update: {Update}.",
            plan.FilesToCopy, plan.MissingFiles, plan.RecordsToInsert, plan.RecordsToUpdate);

        return plan;
    }
}
