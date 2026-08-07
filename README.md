# FileConversion
Utility to copy changes made on test website to production site and storage.

## Configuration
`FileConversionTool/appsettings.json` must include the main storage settings:

```json
{
  "Storage": {
    "TestDriveLetter": "D",
    "ProdDriveLetter": "E"
  }
}
```

`FileConversionTool/appsettings.testrun.json` configures run mode 3 output:

```json
{
  "Storage": {
    "TestRunOutputDirectory": "D:\\FileConversionTestRun"
  }
}
```

## Run modes
- `1` Test plan only (read-only analysis)
- `2` Full file copy and production database update
- `3` Test file copy to `Storage:TestRunOutputDirectory` (no production database updates)
- `4` Analyze and back-copy missing test files from production storage

Mode `1` also reports how many missing test files can be restored from production storage.
