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
var pathHelper       = new PathHelper(testDriveLetter, prodDriveLetter);
var analyzer         = new PreCopyAnalyzer(testCtx, prodCtx, pathHelper, logger);
var fileCopier       = new FileCopier(logger);
var databaseMigrator = new DatabaseMigrator(prodCtx, logger);

// ---------------------------------------------------------------------------
// Step 1 – Pre-copy analysis (read-only, no changes made)
// ---------------------------------------------------------------------------
logger.LogInformation("Starting file/database migration from TEST to PRODUCTION.");

MigrationPlan plan = await analyzer.AnalyzeAsync();

// ---------------------------------------------------------------------------
// Step 2 – Copy files
// ---------------------------------------------------------------------------
int filesCopied = fileCopier.CopyFiles(plan);

// ---------------------------------------------------------------------------
// Step 3 – Upsert database records
// ---------------------------------------------------------------------------
int recordsUpserted = await databaseMigrator.UpsertRecordsAsync(plan);

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------
int fileCopyErrors = plan.FilesToCopy - filesCopied;
int dbErrors       = plan.Items.Count - recordsUpserted;
int errors         = fileCopyErrors + dbErrors;

logger.LogInformation(
    "Migration complete. Records upserted: {Upserted}, Files copied: {Files}, " +
    "File copy errors: {FileCopyErrors}, DB errors: {DbErrors}",
    recordsUpserted, filesCopied, fileCopyErrors, dbErrors);

if (errors > 0)
{
    Environment.Exit(1);
}
