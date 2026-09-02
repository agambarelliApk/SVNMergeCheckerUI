# Elenco Test - SVNMergeCheckerUI

## Stato attuale

Nella solution è presente un progetto di test:

- `SVNMergeCheckerUI.Tests/SVNMergeCheckerUI.Tests.csproj`

Il progetto è compatibile con il progetto principale `SVNMergeCheckerUI.csproj`:

- target `net8.0-windows7.0`
- `UseWindowsForms=true`
- riferimento diretto al progetto principale
- pacchetti di test già configurati (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`)

## Test presenti

### `ReportParserServiceTests`

File: `SVNMergeCheckerUI.Tests/ReportParserServiceTests.cs`

Scopo: verificare il parsing e la riorganizzazione del report generato dall'applicazione.

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
- `ParseRevisioniLines_UsesPerIssueState_WhenAvailable`
- `ParseRevisioniLines_GroupByMerge_ReturnsHeaderExcludesMergedAndDeduplicatesAscending`
- `ParseRevisioniLines_GroupByMerge_PrioritizesStatesCorrectlyOnDuplicates`
- `ParseRevisioniLines_GroupByMerge_RevisionNotInDictionary_IncludedWithNullState`
- `PivotFileCoinvolti_ReturnsInputUnchanged_WhenNoFilesFound`
- `PivotFileCoinvolti_GroupsFilesByRevisionAndIssue`
- `PivotFileCoinvolti_SkipsFilesMarkedAsNoneFound`

### `ReportParserServiceGoldenFileTests`

File: `SVNMergeCheckerUI.Tests/ReportParserServiceGoldenFileTests.cs`

Scopo: golden-file test che confronta l'output di `ReportParserService.Parse` con file di riferimento,
usando come fixture un report nello stesso formato prodotto da `SvnCheckerHelper.BuildReportText`
(`SVNMergeCheckerUI.Tests/Fixtures/SampleReport.txt`), per intercettare regressioni nel formato del
report.

Copertura attuale:

- `Parse_ElencoRevisioni_MatchesGoldenFile`
- `Parse_AlberoDipendenze_MatchesGoldenFile`
- `Parse_FileCoinvoltiRaw_MatchesGoldenFile`
- `Parse_FileCoinvolti_Pivoted_MatchesGoldenFile`

### `SvnCheckerHelperTests`

File: `SVNMergeCheckerUI.Tests/SvnCheckerHelperTests.cs`

Scopo: verificare `SvnCheckerHelper.RunAsync` usando un `ISvnService` finto, senza invocare mai
`svn.exe`.

Copertura attuale:

- `RunAsync_Throws_WhenRepoUrlCannotBeResolved`
- `RunAsync_ReturnsEmptyResult_WhenNoIssuesAndNoRevisionsProvided`
- `RunAsync_UsesResolvedRepoUrl_EvenWhenSourceRepositoryIsALocalPath`

Nota: i flussi che richiedono `svn log`/`svn diff` reali (calcolo dei 4 stati con issue/revisioni
effettive) non sono ancora testabili in isolamento, perché `SvnCheckerHelper` invoca direttamente
`Process.Start` per queste chiamate. Vedi `TASKS.md` per l'astrazione `IProcessRunner` proposta.

### `JsonConfigServiceTests`

File: `SVNMergeCheckerUI.Tests/JsonConfigServiceTests.cs`

Scopo: verificare la persistenza dei profili di configurazione su file JSON, isolata su directory
temporanee.

Copertura attuale:

- `Save_Throws_WhenLabelIsEmpty`
- `Save_Throws_WhenLabelAlreadyExists_AndOverwriteIsFalse`
- `Save_Overwrites_WhenLabelAlreadyExists_AndOverwriteIsTrue`
- `LabelExists_ReturnsTrue_OnlyAfterSaving`
- `Save_And_LoadAll_RoundTrips_AllFields`
- `LoadAll_ReturnsEmpty_WhenStoreFileDoesNotExist`
- `LoadAll_ReturnsEmpty_WhenStoreFileContainsInvalidJson`

### `SvnServiceTests`

File: `SVNMergeCheckerUI.Tests/SvnServiceTests.cs`

Scopo: verificare `SvnService` sui percorsi di codice che non richiedono l'esecuzione reale di
`svn.exe` (short-circuit su URL già risolti, percorsi remoti o percorsi non esistenti).

Copertura attuale:

- `GetSvnUrlAsync_ReturnsInputUnchanged_WhenAlreadyAUrl` (http/https/svn/svn+ssh)
- `GetSvnUrlAsync_ReturnsNull_WhenPathIsEmpty`
- `UpdateDirectoryAsync_Skips_WhenPathIsRemoteUrl` (http/https)
- `UpdateDirectoryAsync_Skips_WhenPathDoesNotExist`
- `UpdateDirectoryAsync_Skips_WhenPathIsEmpty`
- `GetMergedRevisionsAsync_ReturnsEmptyList_WhenSourceUrlIsEmpty`
- `GetMergedRevisionsAsync_ReturnsEmptyList_WhenTargetPathIsEmpty`

## Conclusione

La suite di test esistente è compatibile con il progetto principale e il build della solution risulta
corretto. Al momento sono presenti 41 test automatici, tutti verdi (`dotnet test`).

Attività future possibili: introdurre un'astrazione `IProcessRunner` iniettabile per isolare
completamente `Process.Start` in `SvnService`/`SvnCheckerHelper` e testare così anche il flusso
completo di `RunAsync` (calcolo dei 4 stati) e la costruzione esatta degli argomenti CLI, senza
dipendere da `svn.exe` installato sulla macchina di build.
