using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProdContext = PMC.Data.DF.CustomerPortal.CustomerPortalContext;
using ProdResourceDownload = PMC.Data.DF.CustomerPortal.ResourceDownload;
using ProdResourceThumbnail = PMC.Data.DF.CustomerPortal.ResourceThumbnail;
using TestContext = PMC.Data.DF.CustomerPortalOrchTest.CustomerPortalOrchTestContext;
using TestResourceDownload = PMC.Data.DF.CustomerPortalOrchTest.ResourceDownload;
using TestResourceThumbnail = PMC.Data.DF.CustomerPortalOrchTest.ResourceThumbnail;

namespace FileConversionTool.Services;

/// <summary>
/// Reads the current state of both databases and the test file system to produce a
/// <see cref="MigrationPlan"/> without making any changes.
/// </summary>
public class PreCopyAnalyzer
{
    private readonly TestContext _testCtx;
    private readonly ProdContext _prodCtx;
    private readonly PathHelper _pathHelper;
    private readonly ILogger _logger;

    public PreCopyAnalyzer(
        TestContext testCtx,
        ProdContext prodCtx,
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
        List<TestResourceDownload> testRecords =
            await _testCtx.ResourceDownloads.AsNoTracking().ToListAsync();
        List<TestResourceThumbnail> testThumbnailRecords =
            await _testCtx.ResourceThumbnails.AsNoTracking().ToListAsync();

        _logger.LogInformation("Pre-copy analysis: found {Count} ResourceDownload record(s) in test database.", testRecords.Count);
        _logger.LogInformation("Pre-copy analysis: found {Count} ResourceThumbnail record(s) in test database.", testThumbnailRecords.Count);

        List<ProdResourceDownload> prodRecords = await _prodCtx.ResourceDownloads
            .AsNoTracking()
            .ToListAsync();
        List<ProdResourceThumbnail> prodThumbnailRecords = await _prodCtx.ResourceThumbnails
            .AsNoTracking()
            .ToListAsync();

        _logger.LogInformation("Pre-copy analysis: found {Count} existing ResourceDownload record(s) in production database.", prodRecords.Count);
        _logger.LogInformation("Pre-copy analysis: found {Count} existing ResourceThumbnail record(s) in production database.", prodThumbnailRecords.Count);

        Dictionary<string, List<ProdResourceDownload>> prodByFileName = prodRecords
            .GroupBy(r => Path.GetFileName(r.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<ProdResourceThumbnail>> prodThumbnailsByName = prodThumbnailRecords
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<ProdResourceThumbnail>> prodThumbnailsByFilePath = prodThumbnailRecords
            .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var items = new List<MigrationItem>(testRecords.Count);
        var thumbnailItems = new List<ThumbnailMigrationItem>(testThumbnailRecords.Count);

        foreach (var testRecord in testRecords)
        {
            string sourceFilePath = _pathHelper.MapToTestFileSystem(testRecord.FilePath);
            string destFilePath = _pathHelper.MapToProdPath(testRecord.FilePath);
            string prodDbFilePath = _pathHelper.MapToProdPath(testRecord.FilePath);
            bool fileExists = File.Exists(sourceFilePath);

            if (!fileExists)
                _logger.LogWarning("Pre-copy analysis: source file not found for record ID={ID}: {Path}", testRecord.ID, sourceFilePath);

            string testFileName = Path.GetFileName(testRecord.FilePath);
            prodByFileName.TryGetValue(testFileName, out List<ProdResourceDownload>? prodCandidates);

            ProdResourceDownload? matchedProdRecord = null;
            if (fileExists && prodCandidates is not null)
            {
                foreach (ProdResourceDownload candidate in prodCandidates)
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

        foreach (var testThumbnailRecord in testThumbnailRecords)
        {
            string sourceFilePath = _pathHelper.MapToTestFileSystem(testThumbnailRecord.FilePath);
            string destFilePath = _pathHelper.MapToProdPath(testThumbnailRecord.FilePath);
            string prodDbFilePath = _pathHelper.MapToProdPath(testThumbnailRecord.FilePath);
            bool fileExists = File.Exists(sourceFilePath);

            if (!fileExists)
                _logger.LogWarning("Pre-copy analysis: source thumbnail file not found for record ID={ID}: {Path}", testThumbnailRecord.ID, sourceFilePath);

            ProdResourceThumbnail? matchedProdThumbnail = await FindMatchingThumbnailRecordAsync(
                testThumbnailRecord,
                sourceFilePath,
                prodDbFilePath,
                fileExists,
                prodThumbnailsByName,
                prodThumbnailsByFilePath);

            thumbnailItems.Add(new ThumbnailMigrationItem
            {
                TestRecord = testThumbnailRecord,
                SourceFilePath = sourceFilePath,
                DestFilePath = destFilePath,
                ProdDbFilePath = prodDbFilePath,
                FileExists = fileExists,
                RecordExistsInProd = matchedProdThumbnail is not null,
                MatchedProdRecordId = matchedProdThumbnail?.ID,
            });
        }

        HashSet<string> expectedProdFiles = items
            .Select(item => item.DestFilePath)
            .Concat(thumbnailItems.Select(item => item.DestFilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> directoriesToScan = expectedProdFiles
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> orphanedProdFiles = FindOrphanedFiles(directoriesToScan, expectedProdFiles);

        var plan = MigrationPlan.From(items, thumbnailItems, orphanedProdFiles);

        _logger.LogInformation(
            "Pre-copy analysis complete. Files to copy: {Copy}, Missing files: {Missing}, " +
            "ResourceDownload inserts: {DownloadInsert}, ResourceDownload updates: {DownloadUpdate}, " +
            "ResourceThumbnail inserts: {ThumbnailInsert}, ResourceThumbnail updates: {ThumbnailUpdate}, " +
            "Orphaned prod files: {Orphaned}.",
            plan.FilesToCopy,
            plan.MissingFiles,
            plan.DownloadRecordsToInsert,
            plan.DownloadRecordsToUpdate,
            plan.ThumbnailRecordsToInsert,
            plan.ThumbnailRecordsToUpdate,
            plan.OrphanedProdFileCount);

        if (plan.OrphanedProdFileCount > 0)
            _logger.LogWarning("Pre-copy analysis: found {Count} orphaned production file(s) in scanned directories.", plan.OrphanedProdFileCount);

        return plan;
    }

    private async Task<ProdResourceThumbnail?> FindMatchingThumbnailRecordAsync(
        TestResourceThumbnail testRecord,
        string sourceFilePath,
        string prodDbFilePath,
        bool fileExists,
        IReadOnlyDictionary<string, List<ProdResourceThumbnail>> prodThumbnailsByName,
        IReadOnlyDictionary<string, List<ProdResourceThumbnail>> prodThumbnailsByFilePath)
    {
        var candidates = new List<ProdResourceThumbnail>();

        if (prodThumbnailsByFilePath.TryGetValue(prodDbFilePath, out List<ProdResourceThumbnail>? filePathCandidates))
            candidates.AddRange(filePathCandidates);

        if (prodThumbnailsByName.TryGetValue(testRecord.Name, out List<ProdResourceThumbnail>? nameCandidates))
        {
            foreach (ProdResourceThumbnail candidate in nameCandidates)
            {
                if (candidates.All(existing => existing.ID != candidate.ID))
                    candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
            return null;

        ProdResourceThumbnail? exactPathMatch = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, prodDbFilePath, StringComparison.OrdinalIgnoreCase));

        if (exactPathMatch is not null)
            return exactPathMatch;

        if (fileExists)
        {
            foreach (ProdResourceThumbnail candidate in candidates)
            {
                string candidateProdPath = _pathHelper.MapToProdPath(candidate.FilePath);
                if (!File.Exists(candidateProdPath))
                    continue;

                if (await FilesHaveSameContentsAsync(sourceFilePath, candidateProdPath))
                    return candidate;
            }
        }

        return candidates.Count == 1 ? candidates[0] : null;
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
