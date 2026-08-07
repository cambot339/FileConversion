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

        List<PMC.Data.DF.CustomerPortal.ResourceDownload> prodRecords = await _prodCtx.ResourceDownloads
            .AsNoTracking()
            .ToListAsync();

        _logger.LogInformation("Pre-copy analysis: found {Count} existing ResourceDownload record(s) in production database.", prodRecords.Count);

        Dictionary<string, List<PMC.Data.DF.CustomerPortal.ResourceDownload>> prodByFileName = prodRecords
            .GroupBy(r => Path.GetFileName(r.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var items = new List<MigrationItem>(testRecords.Count);

        foreach (var testRecord in testRecords)
        {
            string sourceFilePath = _pathHelper.MapToTestFileSystem(testRecord.FilePath);
            string destFilePath = _pathHelper.MapToProdPath(testRecord.FilePath);
            string prodDbFilePath = _pathHelper.MapToProdPath(testRecord.FilePath);
            bool fileExists = File.Exists(sourceFilePath);

            if (!fileExists)
                _logger.LogWarning("Pre-copy analysis: source file not found for record ID={ID}: {Path}", testRecord.ID, sourceFilePath);

            string testFileName = Path.GetFileName(testRecord.FilePath);
            prodByFileName.TryGetValue(testFileName, out List<PMC.Data.DF.CustomerPortal.ResourceDownload>? prodCandidates);

            PMC.Data.DF.CustomerPortal.ResourceDownload? matchedProdRecord = null;
            if (fileExists && prodCandidates is not null)
            {
                foreach (PMC.Data.DF.CustomerPortal.ResourceDownload candidate in prodCandidates)
                {
                    string candidateProdPath = _pathHelper.MapToProdPath(candidate.FilePath);
                    if (!File.Exists(candidateProdPath))
                        continue;

                    if (await FilesHaveSameContentsAsync(sourceFilePath, candidateProdPath))
                    {
                        matchedProdRecord = candidate;
                        break;
                    }
                }
            }

            items.Add(new MigrationItem
            {
                TestRecord = testRecord,
                SourceFilePath = sourceFilePath,
                DestFilePath = destFilePath,
                ProdDbFilePath = prodDbFilePath,
                FileExists = fileExists,
                RecordExistsInProd = matchedProdRecord is not null,
                MatchedProdRecordId = matchedProdRecord?.ID,
            });
        }

        HashSet<string> expectedProdFiles = items
            .Select(item => item.DestFilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> directoriesToScan = expectedProdFiles
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> orphanedProdFiles = FindOrphanedFiles(directoriesToScan, expectedProdFiles);

        var plan = MigrationPlan.From(items, orphanedProdFiles);

        _logger.LogInformation(
            "Pre-copy analysis complete. Files to copy: {Copy}, Missing files: {Missing}, " +
            "Records to insert: {Insert}, Records to update: {Update}, Orphaned prod files: {Orphaned}.",
            plan.FilesToCopy, plan.MissingFiles, plan.RecordsToInsert, plan.RecordsToUpdate, plan.OrphanedProdFileCount);

        if (plan.OrphanedProdFileCount > 0)
            _logger.LogWarning("Pre-copy analysis: found {Count} orphaned production file(s) in scanned directories.", plan.OrphanedProdFileCount);

        return plan;
    }

    private static List<string> FindOrphanedFiles(IReadOnlyCollection<string> directoriesToScan, IReadOnlySet<string> expectedProdFiles)
    {
        var orphanedFiles = new List<string>();

        foreach (string directory in directoriesToScan)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!expectedProdFiles.Contains(file))
                    orphanedFiles.Add(file);
            }
        }

        orphanedFiles.Sort(StringComparer.OrdinalIgnoreCase);
        return orphanedFiles;
    }

    private static async Task<bool> FilesHaveSameContentsAsync(string sourcePath, string destinationPath)
    {
        FileInfo sourceInfo = new(sourcePath);
        FileInfo destinationInfo = new(destinationPath);

        if (sourceInfo.Length != destinationInfo.Length)
            return false;

        const int bufferSize = 81920;
        byte[] sourceBuffer = new byte[bufferSize];
        byte[] destinationBuffer = new byte[bufferSize];

        await using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using FileStream destinationStream = new(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);

        while (true)
        {
            int sourceRead = await sourceStream.ReadAsync(sourceBuffer.AsMemory(0, sourceBuffer.Length));
            int destinationRead = await destinationStream.ReadAsync(destinationBuffer.AsMemory(0, destinationBuffer.Length));

            if (sourceRead != destinationRead)
                return false;

            if (sourceRead == 0)
                return true;

            if (!sourceBuffer.AsSpan(0, sourceRead).SequenceEqual(destinationBuffer.AsSpan(0, destinationRead)))
                return false;
        }
    }
}
