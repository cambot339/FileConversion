using PMC.Data.DF.CustomerPortalOrchTest;

namespace FileConversionTool.Services;

/// <summary>
/// Represents a single file-copy + database-record operation to be performed during migration.
/// </summary>
public class MigrationItem
{
    /// <summary>The source record from the test database.</summary>
    public required ResourceDownload TestRecord { get; init; }

    /// <summary>Absolute path of the source file for the current copy operation.</summary>
    public required string SourceFilePath { get; init; }

    /// <summary>Absolute destination path for the current copy operation.</summary>
    public required string DestFilePath { get; init; }

    /// <summary>The FilePath value that will be stored in the production database.</summary>
    public required string ProdDbFilePath { get; init; }

    /// <summary>Whether <see cref="SourceFilePath"/> exists and should be physically copied.</summary>
    public required bool FileExists { get; init; }

    /// <summary>Whether the corresponding file already exists in production storage.</summary>
    public required bool ProductionFileExists { get; init; }

    /// <summary>Whether a matching production record (filename + same file content) already exists.</summary>
    public required bool RecordExistsInProd { get; init; }

    /// <summary>The matched production record ID when <see cref="RecordExistsInProd"/> is true.</summary>
    public int? MatchedProdRecordId { get; init; }

    /// <summary>Whether this item can be restored from production storage back into test storage.</summary>
    public bool CanBackCopyFromProd => !FileExists && ProductionFileExists;
}

/// <summary>
/// Summary of what a migration run will do before any changes are made.
/// </summary>
public class MigrationPlan
{
    public IReadOnlyList<MigrationItem> Items { get; init; } = [];
    public IReadOnlyList<string> OrphanedProdFiles { get; init; } = [];

    public int FilesToCopy => Items.Count(item => item.FileExists);
    public int MissingFiles => Items.Count(item => !item.FileExists);
    public int FilesAvailableToBackCopy => Items.Count(item => item.CanBackCopyFromProd);
    public int RecordsToInsert => Items.Count(item => !item.RecordExistsInProd);
    public int RecordsToUpdate => Items.Count(item => item.RecordExistsInProd);
    public int OrphanedProdFileCount => OrphanedProdFiles.Count;

    /// <summary>Builds a <see cref="MigrationPlan"/> from a list of items.</summary>
    public static MigrationPlan From(IReadOnlyList<MigrationItem> items, IReadOnlyList<string>? orphanedProdFiles = null) =>
        new()
        {
            Items = items,
            OrphanedProdFiles = orphanedProdFiles ?? [],
        };

    /// <summary>Builds a copy-only plan that restores missing test files from production storage.</summary>
    public MigrationPlan CreateBackCopyPlan() =>
        From(
            Items
                .Where(item => item.CanBackCopyFromProd)
                .Select(item => new MigrationItem
                {
                    TestRecord = item.TestRecord,
                    SourceFilePath = item.DestFilePath,
                    DestFilePath = item.SourceFilePath,
                    ProdDbFilePath = item.ProdDbFilePath,
                    FileExists = true,
                    ProductionFileExists = item.ProductionFileExists,
                    RecordExistsInProd = item.RecordExistsInProd,
                    MatchedProdRecordId = item.MatchedProdRecordId,
                })
                .ToList(),
            OrphanedProdFiles);
}
