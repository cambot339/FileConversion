using FileConversionTool.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PMC.Data.DF.CustomerPortal;
using PMC.Data.DF.CustomerPortalOrchTest;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.testrun.json", optional: true, reloadOnChange: false)
    .Build();

using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole());
ILogger logger = loggerFactory.CreateLogger("FileConversion");

string testConnStr = config["TestDatabase:ConnectionString"]
    ?? throw new InvalidOperationException("TestDatabase:ConnectionString is required.");
string prodConnStr = config["ProductionDatabase:ConnectionString"]
    ?? throw new InvalidOperationException("ProductionDatabase:ConnectionString is required.");

string testDriveLetter = config["Storage:TestDriveLetter"]
    ?? throw new InvalidOperationException("Storage:TestDriveLetter is required.");
string prodDriveLetter = config["Storage:ProdDriveLetter"]
    ?? throw new InvalidOperationException("Storage:ProdDriveLetter is required.");
string? testRunOutputDirectory = config["Storage:TestRunOutputDirectory"];

// ---------------------------------------------------------------------------
// Build EF contexts using the DF-generated classes
// ---------------------------------------------------------------------------
await using var testCtx = new CustomerPortalOrchTestContext(
    new DbContextOptionsBuilder<CustomerPortalOrchTestContext>()
        .UseSqlServer(testConnStr)
        .Options);

await using var prodCtx = new CustomerPortalContext(
    new DbContextOptionsBuilder<CustomerPortalContext>()
        .UseSqlServer(prodConnStr)
        .Options);

// ---------------------------------------------------------------------------
// Compose services
// ---------------------------------------------------------------------------
var pathHelper = new PathHelper(testDriveLetter, prodDriveLetter);
var analyzer = new PreCopyAnalyzer(testCtx, prodCtx, pathHelper, logger);
var fileCopier = new FileCopier(logger);
var databaseMigrator = new DatabaseMigrator(prodCtx, logger);

Console.WriteLine("Choose run mode:");
Console.WriteLine("  1) Test plan only (read-only analysis)");
Console.WriteLine("  2) Full file copy and database update");
Console.WriteLine("  3) Test file copy to configured directory (no DB updates)");
Console.Write("Selection [1/2/3]: ");

string? selection = Console.ReadLine()?.Trim();

while (selection is not "1" and not "2" and not "3")
{
    Console.Write("Invalid selection. Enter 1, 2, or 3: ");
    selection = Console.ReadLine()?.Trim();
}


logger.LogInformation("Building migration plan from TEST to PRODUCTION.");
MigrationPlan plan = await analyzer.AnalyzeAsync();


if (selection == "1")
{
    if (plan.OrphanedProdFileCount > 0)
    {
        logger.LogWarning("Potential orphaned production files that could be deleted:");
        foreach (string orphanedFile in plan.OrphanedProdFiles)
            logger.LogWarning("  {Path}", orphanedFile);
    }

    return;
}

if (selection == "3")
{
    if (string.IsNullOrWhiteSpace(testRunOutputDirectory))
        throw new InvalidOperationException("Storage:TestRunOutputDirectory is required for run mode 3. Configure it in appsettings.testrun.json or appsettings.json.");

    string outputRoot = Path.GetFullPath(testRunOutputDirectory);
    logger.LogInformation("Starting test file copy run to {OutputRoot}. Production database will not be modified.", outputRoot);

    MigrationPlan testRunPlan = MigrationPlan.From(
        plan.Items.Select(item => new MigrationItem
        {
            TestRecord = item.TestRecord,
            SourceFilePath = item.SourceFilePath,
            DestFilePath = pathHelper.MapToConfiguredRoot(item.TestRecord.FilePath, outputRoot),
            ProdDbFilePath = item.ProdDbFilePath,
            FileExists = item.FileExists,
            RecordExistsInProd = item.RecordExistsInProd,
            MatchedProdRecordId = item.MatchedProdRecordId,
        }).ToList(),
        plan.OrphanedProdFiles);

    int testRunFilesCopied = fileCopier.CopyFiles(testRunPlan);
    int testRunFileCopyErrors = testRunPlan.FilesToCopy - testRunFilesCopied;

    logger.LogInformation(
        "Test file copy run complete. Files copied: {Files}, File copy errors: {FileCopyErrors}. No database changes were made.",
        testRunFilesCopied, testRunFileCopyErrors);

    if (testRunFileCopyErrors > 0)
        Environment.Exit(1);

    return;
}

logger.LogInformation("Starting file/database migration from TEST to PRODUCTION.");
int filesCopied = fileCopier.CopyFiles(plan);
int recordsUpserted = await databaseMigrator.UpsertRecordsAsync(plan);

int fileCopyErrors = plan.FilesToCopy - filesCopied;
int dbErrors = plan.Items.Count - recordsUpserted;
int errors = fileCopyErrors + dbErrors;

logger.LogInformation(
    "Migration complete. Records upserted: {Upserted}, Files copied: {Files}, " +
    "File copy errors: {FileCopyErrors}, DB errors: {DbErrors}",
    recordsUpserted, filesCopied, fileCopyErrors, dbErrors);

if (errors > 0)
{
    Environment.Exit(1);
}
