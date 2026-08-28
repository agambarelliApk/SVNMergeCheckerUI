# SVNMergeCheckerUI.Tests

Progetto di test automatici per la solution `SVNMergeCheckerUI`.

## Obiettivo

Questo progetto contiene i test unitari dedicati alla logica di parsing e, in futuro, ai servizi di business dell'applicazione principale.

## Stack di test

- `.NET 8`
- `xUnit`
- `Microsoft.NET.Test.Sdk`
- `coverlet.collector`

## Progetto principale testato

Il progetto di test referenzia direttamente:

- `..\SVNMergeCheckerUI.csproj`

## Test presenti

### `ReportParserServiceTests`

File: `ReportParserServiceTests.cs`

Scopo: verificare il parsing e la riorganizzazione del report generato da `ReportParserService`.

Copertura attuale:

- `Parse_ElencoRevisioni_UsesGroupedByIssueSection_WhenPresent`
- `Parse_ElencoRevisioni_FallsBackToHeader1_WhenGroupedSectionMissing`
- `Parse_AlberoDipendenze_ReturnsErrorMarker_WhenSectionNotFound`
- `Parse_AlberoDipendenze_ExtractsContentBetweenHeader2AndHeader3`
- `Parse_FileCoinvoltiRaw_ReturnsErrorMarker_WhenHeader3Missing`
- `ParseRevisioniLines_LineWithoutRevisionNumber_HasNullState`
- `ParseRevisioniLines_EmptyLine_HasNullState`
- `ParseRevisioniLines_RevisionNotInDictionary_HasNullState`
- `ParseRevisioniLines_RevisionInDictionary_HasMatchingState`
- `PivotFileCoinvolti_ReturnsInputUnchanged_WhenNoFilesFound`
- `PivotFileCoinvolti_GroupsFilesByRevisionAndIssue`
- `PivotFileCoinvolti_SkipsFilesMarkedAsNoneFound`

## Esecuzione dei test

Da root solution:

```powershell
dotnet test SVNMergeCheckerUI.Tests/SVNMergeCheckerUI.Tests.csproj
```

## Note

- I test devono rimanere veloci, deterministici e senza dipendenze esterne.
- Per i componenti che usano processi esterni o file system, preferire mocking o fixture controllate.
