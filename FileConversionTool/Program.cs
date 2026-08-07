using FileConversionTool.Data;
using FileConversionTool.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole());
ILogger logger = loggerFactory.CreateLogger("FileConversion");

string testConnStr = config["TestDatabase:ConnectionString"]
    ?? throw new InvalidOperationException("TestDatabase:ConnectionString is required.");
string prodConnStr = config["ProductionDatabase:ConnectionString"]
    ?? throw new InvalidOperationException("ProductionDatabase:ConnectionString is required.");

string testDriveLetter = config["Storage:TestDriveLetter"]     ?? "T";
string testNetworkBase = config["Storage:TestNetworkBasePath"] ?? "";
string prodDriveLetter = config["Storage:ProdDriveLetter"]     ?? "P";
string prodNetworkBase = config["Storage:ProdNetworkBasePath"] ?? "";

// ---------------------------------------------------------------------------
// Path helpers
// ---------------------------------------------------------------------------

/// <summary>
/// Extracts the relative portion of a path that was stored in the test DB.
/// The test DB stores paths with a mapped drive letter (e.g. T:\folder\file.pdf)
/// or optionally already as UNC paths.
/// </summary>
string GetRelativePart(string dbPath)
{
    string normalized = dbPath.Replace('/', '\\');
    string drivePrefix = testDriveLetter.TrimEnd(':') + ":";

    if (normalized.StartsWith(drivePrefix + "\\", StringComparison.OrdinalIgnoreCase))
        return normalized.Substring(drivePrefix.Length).TrimStart('\\');

    if (!string.IsNullOrEmpty(testNetworkBase)
        && normalized.StartsWith(testNetworkBase, StringComparison.OrdinalIgnoreCase))
        return normalized.Substring(testNetworkBase.Length).TrimStart('\\');

    // Unknown prefix – use the file name only to avoid unintended path traversal.
    return Path.GetFileName(normalized);
}

/// <summary>
/// Maps a test DB file path to its actual file system path on the test network share.
/// </summary>
string MapToTestFileSystem(string dbPath) =>
    Path.Combine(testNetworkBase, GetRelativePart(dbPath));

/// <summary>
/// Maps a test DB file path to its destination path on the production network share.
/// </summary>
string MapToProdFileSystem(string dbPath) =>
    Path.Combine(prodNetworkBase, GetRelativePart(dbPath));

/// <summary>
/// Maps a test DB file path to the drive-letter path that should be stored in
/// the production database (e.g. P:\folder\file.pdf).
/// </summary>
string MapToProdDbPath(string dbPath) =>
    prodDriveLetter.TrimEnd(':') + ":\\" + GetRelativePart(dbPath);

// ---------------------------------------------------------------------------
// Build EF contexts
// ---------------------------------------------------------------------------
DbContextOptions<AppDbContext> testOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(testConnStr)
    .Options;

DbContextOptions<AppDbContext> prodOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(prodConnStr)
    .Options;

// ---------------------------------------------------------------------------
// Main migration logic
// ---------------------------------------------------------------------------
logger.LogInformation("Starting file/database migration from TEST to PRODUCTION.");

await using var testCtx = new AppDbContext(testOptions);
await using var prodCtx = new AppDbContext(prodOptions);

List<ResourceDownload> testRecords = await testCtx.ResourceDownloads.AsNoTracking().ToListAsync();
logger.LogInformation("Found {Count} ResourceDownload record(s) in the test database.", testRecords.Count);

int filesCopied = 0;
int recordsUpserted = 0;
int errors = 0;

foreach (ResourceDownload testRecord in testRecords)
{
    try
    {
        // --- File copy -------------------------------------------------------
        string sourceFilePath = MapToTestFileSystem(testRecord.FilePath);
        string destFilePath   = MapToProdFileSystem(testRecord.FilePath);

        string? destDir = Path.GetDirectoryName(destFilePath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
            logger.LogDebug("Created directory: {Dir}", destDir);
        }

        if (File.Exists(sourceFilePath))
        {
            File.Copy(sourceFilePath, destFilePath, overwrite: true);
            logger.LogInformation("Copied file: {Src} -> {Dest}", sourceFilePath, destFilePath);
            filesCopied++;
        }
        else
        {
            logger.LogWarning("Source file not found, skipping copy: {Src}", sourceFilePath);
        }

        // --- Database upsert -------------------------------------------------
        string prodFilePath = MapToProdDbPath(testRecord.FilePath);

        ResourceDownload? existing = await prodCtx.ResourceDownloads
            .FirstOrDefaultAsync(r => r.ID == testRecord.ID);

        if (existing is null)
        {
            prodCtx.ResourceDownloads.Add(new ResourceDownload
            {
                ID          = testRecord.ID,
                Name        = testRecord.Name,
                Description = testRecord.Description,
                FilePath    = prodFilePath,
                CategoryID  = testRecord.CategoryID,
                ThumbnailID = testRecord.ThumbnailID,
                FolderId    = testRecord.FolderId,
            });
            logger.LogInformation("Inserting record ID={ID} ({Name})", testRecord.ID, testRecord.Name);
        }
        else
        {
            existing.Name        = testRecord.Name;
            existing.Description = testRecord.Description;
            existing.FilePath    = prodFilePath;
            existing.CategoryID  = testRecord.CategoryID;
            existing.ThumbnailID = testRecord.ThumbnailID;
            existing.FolderId    = testRecord.FolderId;
            logger.LogInformation("Updating record ID={ID} ({Name})", testRecord.ID, testRecord.Name);
        }

        // Save per-record to isolate failures and avoid partial batches.
        await prodCtx.SaveChangesAsync();
        recordsUpserted++;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing record ID={ID}: {Message}", testRecord.ID, ex.Message);
        errors++;
    }
}

logger.LogInformation(
    "Migration complete. Records upserted: {Upserted}, Files copied: {Files}, Errors: {Errors}",
    recordsUpserted, filesCopied, errors);

if (errors > 0)
{
    Environment.Exit(1);
}
