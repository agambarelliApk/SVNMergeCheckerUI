# Flow - `RunAsync` e dipendenze

## Scopo

Questo documento riassume il flusso attuale di esecuzione di `SvnCheckerHelper.RunAsync` e delle
funzioni collegate, con particolare attenzione alla gestione multi-issue e alle revisioni condivise.

## Flusso principale

1. `Form1.btnRun_Click`
   - Legge i parametri dalla UI.
   - Costruisce `SvnCheckerParameters`.
   - Invoca `SvnCheckerHelper.RunAsync(...)`.

2. `SvnCheckerHelper.RunAsync`
   - Risolve l'URL SVN della sorgente tramite `ISvnService.GetSvnUrlAsync`.
   - Se sono presenti issue:
     - recupera un `svn log` XML condiviso;
     - esegue `RunSingleIssueAsync` per ogni issue;
     - fonde i risultati con `MergeIssueResults`.
   - Se non sono presenti issue:
     - carica le revisioni manuali;
     - esegue `AnalyzeDependenciesAsync` in modalità singola.
   - Calcola gli stati di visualizzazione globali e per-issue.
   - Costruisce il testo del report con `BuildReportText`.
   - Restituisce `SvnCheckerResult` a `Form1`.

## Flusso per-issue

### `RunSingleIssueAsync`

- Filtra le revisioni trovate nel log XML per la issue corrente.
- Popola `MatchedIssues`.
- Costruisce la `DependencyTree` della singola elaborazione.
- Esegue `AnalyzeDependenciesAsync` sui file delle revisioni trovate.
- Registra `PerIssueRoles[issue]` per ogni revisione scoperta.

### `MergeIssueResults`

- Unifica le revisioni canoniche in un solo dizionario globale.
- Mantiene `MatchedIssues` e `Files` consolidati.
- Recollega `ParentRev` verso l'istanza canonica.
- Registra i legami cross-issue tra revisioni correlate.
- Conserva i ruoli per-issue in `PerIssueRoles`.

## Regole di stato

- `RevisionInfo.DisplayState` è lo stato globale usato come fallback.
- `RevisionInfo.PerIssueDisplayStates[issue]` è lo stato corretto da usare nel contesto della issue.
- `RevisionDisplayState.DipendenzaUtentePrecedenteDaMergiare` (grigio chiaro `[?]`) rappresenta la dipendenza di una revisione utente che collide con un'altra revisione utente.
- `ReportParserService` e `RevisionRenderingService` devono sempre preferire lo stato per-issue quando disponibile.

## Sezioni report

### Sezione 1 - elenco revisioni

- Modalità **GroupBy = "Issue"**: raggruppa le revisioni per issue e le struttura ad albero; le revisioni utente dirette vengono mostrate come radici in blu (`DaMergiareDiretta`) e come sotto-dipendenze in grigio chiaro (`DipendenzaUtentePrecedenteDaMergiare`), senza espandere ulteriormente sotto-nodi dal nodo grigio chiaro. Le combinazioni duplicate `(revisione, stato)` vengono filtrate.
- Modalità **GroupBy = "Merge"**: genera l'elenco deduplicato e ordinato in modo crescente di tutte le revisioni da mergiare con intestazione `REVISIONI ORDINATE PER MERGE`, esclude quelle già mergiate e risolve le collisioni secondo la gerarchia di priorità degli stati.

### Sezione 2 - albero dipendenze

- Mostra la gerarchia per issue.
- Le issue senza revisioni dirette ma con revisioni di dipendenza devono comunque essere renderizzate.

### Sezione 3 - file coinvolti

- Raggruppa i file per revisione.
- Mantiene il contesto issue-specifico e il note cross-issue.

## Punto critico risolto

In multi-issue, una revisione può essere:

- diretta per una issue;
- dipendenza per un'altra;
- orfana rispetto a una terza.

Per questo motivo non bisogna usare solo strutture globali come `ParentRev`, `Direction` o `MatchedIssues.Count == 0` per decidere la visibilità nell'output. La fonte corretta è la combinazione di:

- `MatchedIssues`
- `PerIssueRoles`
- `PerIssueDisplayStates`

## Riferimenti utili

- `SvnCheckerHelper.cs`
- `SvnCheckerModels.cs`
- `ReportParserService.cs`
- `RevisionRenderingService.cs`
- `Form1.cs`
