# FileConversion
Utility to copy changes made on test website to produciton site and storage 

## Test run mode

Run with `--test-run` to copy files into a safe test output directory and skip all production database writes.

Configure the destination in `FileConversionTool/appsettings.json`:

```json
{
  "Storage": {
    "TestDriveLetter": "D",
    "ProdDriveLetter": "E",
    "TestRunOutputPath": "D:\\FileConversionTestRun"
  }
}
```
