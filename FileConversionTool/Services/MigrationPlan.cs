using PMC.Data.DF.CustomerPortalOrchTest;

namespace FileConversionTool.Services;

/// <summary>
/// Represents a single file-copy + database-record operation to be performed during migration.
/// </summary>
public class MigrationItem
{
    /// <summary>The source record from the test database.</summary>
    public required ResourceDownload TestRecord { get; init; }

    /// <summary>Absolute path of the source file on the test file system. Null means the file was not found.</summary>
    public required string SourceFilePath { get; init; }

    /// <summary>Absolute path where the file should be written on the production file system.</summary>
    public required string DestFilePath { get; init; }

    /// <summary>The FilePath value that will be stored in the production database.</summary>
    public required string ProdDbFilePath { get; init; }

    /// <summary>Whether the source file exists and should be physically copied.</summary>
    public required bool FileExists { get; init; }

    /// <summary>Whether a record with this ID already exists in the production database.</summary>
    public required bool RecordExistsInProd { get; init; }
}

/// <summary>
/// Summary of what a migration run will do before any changes are made.
/// </summary>
public class MigrationPlan
{
    public IReadOnlyList<MigrationItem> Items { get; init; } = [];

    public int FilesToCopy { get; init; }
    public int MissingFiles { get; init; }
    public int RecordsToInsert { get; init; }
    public int RecordsToUpdate { get; init; }

    /// <summary>Builds a <see cref="MigrationPlan"/> from a list of items, computing all counts in a single pass.</summary>
    public static MigrationPlan From(IReadOnlyList<MigrationItem> items)
    {
        int filesToCopy = 0, missingFiles = 0, toInsert = 0, toUpdate = 0;
        foreach (var item in items)
        {
            if (item.FileExists) filesToCopy++; else missingFiles++;
            if (item.RecordExistsInProd) toUpdate++; else toInsert++;
        }
        return new MigrationPlan
        {
            Items = items,
            FilesToCopy = filesToCopy,
            MissingFiles = missingFiles,
            RecordsToInsert = toInsert,
            RecordsToUpdate = toUpdate,
        };
    }
}
