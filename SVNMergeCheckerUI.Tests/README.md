# SVNMergeCheckerUI.Tests

Progetto di test automatici per la solution `SVNMergeCheckerUI`.

## Obiettivo

Questo progetto contiene i test automatici (unitari e golden-file) dedicati alla logica di parsing, calcolo delle dipendenze e collisioni, persistenza configurazione e servizi SVN dell'applicazione principale.

## Stack di test

- `.NET 8` (`net8.0-windows7.0`)
- `xUnit`
- `Microsoft.NET.Test.Sdk`
- `coverlet.collector`

## Progetto principale testato

Il progetto di test referenzia direttamente:

- `..\SVNMergeCheckerUI.csproj`

## Test presenti (73 casi di test totali)

### `ReportParserServiceTests`

File: `ReportParserServiceTests.cs`

Scopo: verificare il parsing e la riorganizzazione del report generato da `ReportParserService`.

Copertura attuale (25 test):

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
- `ParseAlberoDipendenze_ExtractsPerIssueTreeCorrectly`
- `ParseRevisioniLines_WithPerIssueDependencyTree_RendersMultiLayerHierarchyWithTwoSpacesIndent`
- `ParseRevisioniLines_FiltersDipendenzaPrecedenteMergiata_AndExcludesEmptyIssueBlock`
- `ParseRevisioniLines_PreservesDirectMergedAndUserCollisionRevisions`
- `ParseRevisioniLines_SplitsUserCollidingRevisions_AsDirectRootAndIndentedDependency`
- `ParseRevisioniLines_UserCollisionNode_DoesNotEmitSubDependenciesUnderLightGrayNode`
- `ParseRevisioniLines_SkipsDuplicateRevisionStateCombinationInTree`
- `ParseRevisioniLines_DipendenzaDirettaAltraIssuePrecedenteDaMergiare_EmitsCorrectState`
- `ParseRevisioniLines_WithVariantSections_RendersVariantHeadersAndRevisionsCorrectly`

### `ReportParserServiceGoldenFileTests`

File: `ReportParserServiceGoldenFileTests.cs`

Scopo: golden-file test che confronta l'output di `ReportParserService.Parse` con file di fixture attesi in `SVNMergeCheckerUI.Tests/Fixtures/`.

Copertura attuale (4 test):

- `Parse_ElencoRevisioni_MatchesGoldenFile`
- `Parse_AlberoDipendenze_MatchesGoldenFile`
- `Parse_FileCoinvoltiRaw_MatchesGoldenFile`
- `Parse_FileCoinvolti_Pivoted_MatchesGoldenFile`

### `SvnCheckerHelperTests`

File: `SvnCheckerHelperTests.cs`

Scopo: verificare i metodi di helper, algoritmi di collisione hunk e orchestrazione di `SvnCheckerHelper` con mock di `ISvnService`.

Copertura attuale (13 casi di test):

- `RunAsync_Throws_WhenRepoUrlCannotBeResolved`
- `RunAsync_ReturnsEmptyResult_WhenNoIssuesAndNoRevisionsProvided`
- `RunAsync_UsesResolvedRepoUrl_EvenWhenSourceRepositoryIsALocalPath`
- `ParseDiffHunkRanges_ExtractsRangesCorrectly`
- `CheckRangeCollision_DetectsCollisionAndDisjointCorrectly`
- `CheckRangeCollision_EmptyRanges_FallbacksToTrue`
- `ResolveVariantUrl_ReturnsExpectedUrlOrNull` (`[Theory]`, 7 casi)

### `JsonConfigServiceTests`

File: `JsonConfigServiceTests.cs`

Scopo: verificare la persistenza e validazione dei profili di configurazione su file JSON (`AppConfig`), isolata su cartelle temporanee.

Copertura attuale (7 test):

- `Save_Throws_WhenLabelIsEmpty`
- `Save_Throws_WhenLabelAlreadyExists_AndOverwriteIsFalse`
- `Save_Overwrites_WhenLabelAlreadyExists_AndOverwriteIsTrue`
- `LabelExists_ReturnsTrue_OnlyAfterSaving`
- `Save_And_LoadAll_RoundTrips_AllFields`
- `LoadAll_ReturnsEmpty_WhenStoreFileDoesNotExist`
- `LoadAll_ReturnsEmpty_WhenStoreFileContainsInvalidJson`

### `SvnServiceTests`

File: `SvnServiceTests.cs`

Scopo: verificare i percorsi e la validazione di `SvnService` senza invocare `svn.exe`.

Copertura attuale (11 casi di test, alcuni `[Theory]`):

- `GetSvnUrlAsync_ReturnsInputUnchanged_WhenAlreadyAUrl`
- `GetSvnUrlAsync_ReturnsNull_WhenPathIsEmpty`
- `UpdateDirectoryAsync_Skips_WhenPathIsRemoteUrl`
- `UpdateDirectoryAsync_Skips_WhenPathDoesNotExist`
- `UpdateDirectoryAsync_Skips_WhenPathIsEmpty`
- `GetMergedRevisionsAsync_ReturnsEmptyList_WhenSourceUrlIsEmpty`
- `GetMergedRevisionsAsync_ReturnsEmptyList_WhenTargetPathIsEmpty`

### `VariantConfigServiceTests`

File: `VariantConfigServiceTests.cs`

Scopo: verificare `JsonVariantConfigService` (file `parallel_modules.json`), isolata su cartelle temporanee.

Copertura attuale (13 casi di test):

- `LoadAll_ReturnsOnlyGenerale_WhenFileMissing`
- `LoadAll_ReturnsOnlyGenerale_WhenJsonInvalid`
- `LoadAll_AddsGenerale_WhenSingleEntryDifferent`
- `LoadAll_DoesNotDuplicateGenerale_WhenSingleEntryIsGenerale`
- `LoadAll_RemovesDuplicates_IgnoringTuttoAndEmpty_AndKeepsGeneraleLast`
- `LoadAll_UsesLowercaseLabel_WhenFolderNameMissing`
- `LoadAll_CapitalizesFirstLetterOfLabel_KeepingRestUnchanged`
- `Save_WritesCapitalizedLabelToFile`
- `Save_Then_LoadAll_RoundTrips_AndKeepsGeneraleLast`
- `ExtractFolderName_ReturnsLastSegmentOrNull` (`[Theory]`, 4 casi)

## Esecuzione dei test

Da root solution:

```powershell
dotnet test SVNMergeCheckerUI.Tests/SVNMergeCheckerUI.Tests.csproj
```

## Note

- I test devono rimanere veloci, deterministici e senza dipendenze esterne.
- Per i componenti che usano processi esterni o file system, preferire mocking o fixture controllate.
