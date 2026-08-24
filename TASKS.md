# TASKS.md — Piano di Modernizzazione SVNMergeCheckerUI

Elenco dei task tecnici derivati dall'analisi puntuale di `Form1.cs`, `SvnService.cs`,
`RevisionMergeInfo.cs` (`PowerShellRunnerService`), `SvnCheckerHelper.cs` e `ReportParserService.cs`.
Ogni task riporta il file e la riga/metodo interessato, verificati leggendo il codice sorgente attuale.

Legenda priorità:
- ?? **Alta** — bug reale/rischio concreto o blocca altri task
- ?? **Media** — miglioramento strutturale importante ma non bloccante
- ?? **Bassa** — nice-to-have, rifinitura

---

## 1. Refactoring dell'asincronia (chiamate SVN che bloccano la UI)

- ?? **`SvnService.IsSvnAvailable()` (righe 19-39) è completamente sincrono** e usa
  `p.WaitForExit(5000)` bloccante. Viene invocato da `Form1.AssertSvnAvailable()` (righe 277-283),
  chiamato a sua volta in testa a `btnSvnConnect_Click`, `btnSvnUpdate_Click`, `btnRun_Click`
  (tutti `async void`) **prima di qualsiasi `await`**: il thread UI resta bloccato fino a 5 secondi
  ad ogni avvio di operazione se `svn.exe` è lento a rispondere. **Fix**: rendere `IsSvnAvailable`
  asincrono (`Task<bool> IsSvnAvailableAsync()`, `WaitForExitAsync`) e aggiornare i 3 handler in
  `Form1.cs` per usare `await`.
- ?? **`SvnService.RunSvnAsync` (righe 110-128) e `SvnCheckerHelper.RunSvnRawAsync` (righe 449-464)**
  usano il pattern `Task.Run(() => { ...; p.WaitForExit(); ... })`: liberano il thread UI ma occupano
  un thread del pool in blocking-wait e **non rispettano la cancellazione del processo figlio** — il
  `CancellationToken` passato a `Task.Run` interrompe solo l'attesa lato `Task`, non invia `Kill()` al
  processo `svn.exe` reale, che può continuare a girare in background. Confrontare con l'implementazione
  corretta già presente in `PowerShellRunnerService.RunAsync` (righe 73-88: `WaitForExitAsync(ct)` +
  `catch (OperationCanceledException) { process.Kill(entireProcessTree: true); }`) e allineare
  `SvnService`/`SvnCheckerHelper` allo stesso pattern.
- ?? **Sostituire `Process.WaitForExit()`/`ReadToEnd()` sincroni con `WaitForExitAsync()` +
  `ReadToEndAsync()`** in tutti i punti che usano `Task.Run` come wrapper (`SvnService.RunSvnAsync`,
  `SvnCheckerHelper.RunSvnRawAsync`), eliminando la necessità stessa del `Task.Run` e liberando un
  thread del pool per ogni chiamata SVN concorrente.
- ?? **Verificare l'uso di `ct.ThrowIfCancellationRequested()`** nei cicli di `SvnCheckerHelper`
  (es. `FindRevisionsByIssuesAsync` riga 120, `AnalyzeDependenciesAsync`): la cancellazione oggi
  interrompe il ciclo C# ma — per il punto precedente — non il processo SVN già avviato in quel momento.
- ?? **Aggiungere timeout configurabili** per le chiamate a `svn.exe` (oggi solo `IsSvnAvailable` ha un
  timeout esplicito di 5000ms; le altre chiamate in `RunSvnAsync`/`RunSvnRawAsync` non hanno timeout e
  possono bloccare indefinitamente in caso di prompt di autenticazione interattiva, nonostante
  `UseShellExecute = false`).

## 2. Separazione della logica di business dalla UI

- ?? **Spostare la logica di rendering/parsing oggi in `Form1.cs`** in un servizio dedicato
  (es. `IRevisionRenderingService`), poiché duplica parzialmente `ReportParserService`:
  - `RenderRevisioniColoured` (righe 397-425), `RenderFileCoinvoltiGrouped` (righe 427-558),
    `RenderFileCoinvoltiGroupedLegacy` (righe 560-601), `AppendRevisionEntry` (righe 603-615) —
    contengono regex proprie (`REVISIONE`, estrazione numero revisione tramite `revRegex`,
    `NormalizeRevisionText`) che si sovrappongono a `RevisionPattern` e `PivotFileCoinvolti` già
    presenti in `ReportParserService`. Questo viola il principio DRY e rischia disallineamenti se il
    formato del report cambia (`ReportParserService` andrebbe aggiornato in un solo punto).
  - `GetStateVisual` (righe 386-392) è invece corretto lasciarlo in `Form1` (mapping stato?colore è
    legittimamente responsabilità della UI), ma dovrebbe ricevere righe già pre-parsate/strutturate
    da `ReportParserService` invece di stringhe grezze da ri-analizzare con regex locali.
- ?? **Rimuovere il codice morto identificato in `Form1.cs`**, non referenziato da alcun handler
  attivo (verificato: `btnRun_Click` costruisce `skipSet` inline alle righe 208-212, non usa
  `BuildEffectiveSkipRevisionsAsync`):
  - `ParseMergedRevisionsFromScriptOutput` (righe 617-636)
  - `FindScriptPath` (righe 638-652)
  - `ParseSkipRevisionSet` (righe 377-383)
  - `BuildEffectiveSkipRevisionsAsync` (righe 326-356)
  - `IPowerShellRunnerService`/`PowerShellRunnerService` (`RevisionMergeInfo.cs`) risultano anch'essi
    non istanziati/usati in `Form1`: valutare se il flusso "script PowerShell" sia ancora un requisito
    attivo o se vada rimosso, per evitare di mantenere due implementazioni parallele della stessa
    logica (`SvnCheckerHelper` in C# vs `script/svn_predictive_merge_checker.ps1`).
- ?? **Introdurre un layer di validazione input centralizzato**: oggi `ValidateInputs()` (righe
  267-275) valida solo issue/revisioni; la validazione di percorsi (`txtWorkingCopy`, `txtSourceRepo`)
  e del campo numerico avviene implicitamente altrove o per nulla. Centralizzare in un servizio/metodo
  statico dedicato in `SvnCheckerModels.cs`.
- ?? **`Form1` istanzia direttamente `new SvnCheckerHelper(_svnService)` dentro `btnRun_Click`**
  (riga 237) invece che come dipendenza iniettata nel costruttore come gli altri servizi
  (`_svnService`, `_configService`, `_reportParser`): allineare al pattern già in uso per coerenza e
  testabilità (constructor injection per tutti i collaboratori).
- ?? **Estrarre in un piccolo helper/ViewModel la costruzione di `SvnCheckerParameters`**
  (righe 208-234), oggi costruita inline nel click handler leggendo direttamente i controlli WinForms.

## 3. Unit test per il parser e per i servizi

- ?? **Creare il progetto di test** `SVNMergeCheckerUI.Tests` (xUnit + `Moq`/`NSubstitute`): non esiste
  alcuna infrastruttura di test nel workspace.
- ?? **Test per `ReportParserService`** (facile da testare, nessuna dipendenza esterna):
  - `ExtractSection1`/`ExtractBetween`/`ExtractFrom`: verificare i marker esatti (`Header1/2/3`,
    incluso il fallback quando la sezione "1. REVISIONI RAGGRUPPATE PER ISSUE:" non è presente),
    e il messaggio di errore `"[Sezione '...' non trovata nell'output.]"` riprodotto nel bug
    segnalato dall'utente in precedenza ("Albero Dipendenze" non trovata).
  - `ParseRevisioniLines`: verificare che righe senza match di `RevisionPattern` o con revisione non
    presente nel dizionario `revisionStates` producano `state == null` (comportamento introdotto di
    recente per non colorare intestazioni/righe vuote).
  - `PivotFileCoinvolti`: verificare la corretta pivotazione revisione?file in file?issue?revisioni.
  - Rimuovere prima (o testare che non causi warning) il `using System.Text;` duplicato a inizio file
    (righe 1-2).
- ?? **Test per `SvnCheckerHelper.RunAsync`**, mockando `ISvnService`:
  - calcolo dei 4 stati (`Mergiato`, `DaMergiareDiretta`, `DaMergiareIndiretta`,
    `DaMergiareIndirettaAlta`) con casi limite: nessuna revisione diretta pendente (`directPendingMax`
    nullo ? nessuna indiretta deve risultare "Alta"), revisione indiretta con numero esattamente pari
    alla soglia (non deve essere "Alta"), superiore (deve esserlo).
  - marker emessi (`##MERGED_REVISIONS:...`, `##REVISION_STATES:...`) nel formato atteso.
  - comando `svn merge` generato in coda al report, che deve escludere solo le revisioni `Mergiato`.
- ?? **Test per `JsonConfigService`**: casi di errore (label vuota, label duplicata senza overwrite),
  corretta serializzazione/deserializzazione.
- ?? **Test per `SvnService`/`SvnCheckerHelper` (parte processo)**: introdurre un'astrazione
  `IProcessRunner` iniettabile per isolare le chiamate a `Process.Start` e testare la costruzione degli
  argomenti CLI (`"info", "--show-item", "url"`, `"mergeinfo", "--show-revs", "merged"`, ecc.) senza
  invocare `svn.exe` reale.
- ?? **Golden-file test** per `ReportParserService` usando come fixture un report reale generato da
  `SvnCheckerHelper.BuildReportText`.

## 4. Integrazione CI con GitHub Actions

- ?? **Workflow di build base** (`.github/workflows/ci.yml`):
  - trigger `push`/`pull_request`;
  - `actions/setup-dotnet` con .NET 8 SDK;
  - `dotnet restore` + `dotnet build SVNMergeCheckerUI.csproj -c Release`;
  - runner **obbligatoriamente `windows-latest`** (progetto `net8.0-windows7.0` + `UseWindowsForms`,
    non compila/esegue su Linux).
- ?? **Esecuzione test automatici** (`dotnet test`), dipendente dalla creazione del progetto test
  (sezione 3); pubblicare i risultati come step separato.
- ?? **Step di format/lint check** (`dotnet format --verify-no-changes`): utile soprattutto per
  intercettare problemi come il `using` duplicato trovato in `ReportParserService.cs`.
- ?? **Cache dei pacchetti NuGet** tramite cache integrata di `setup-dotnet`.
- ?? **Pubblicazione artifact** (`dotnet publish`) dell'eseguibile come step opzionale per release.

---

## Note di verifica

- Tutti i riferimenti a righe sono stati controllati sul contenuto attuale dei file (non su versioni
  precedenti nella cronologia della sessione).
- Il bug UI-freeze di `IsSvnAvailable()` è il finding più concreto e ad alta priorità: è invocato in
  testa a **tre** handler (`btnSvnConnect_Click`, `btnSvnUpdate_Click`, `btnRun_Click`) prima di
  qualunque `await`, quindi blocca sempre il thread UI per l'intera durata del timeout/della chiamata.
- `PowerShellRunnerService` (`RevisionMergeInfo.cs`) è implementato correttamente dal punto di vista
  dell'asincronia (unico punto del codebase con gestione corretta di cancellazione + kill del
  processo), ma risulta non collegato al flusso UI attivo: va deciso se è codice da rimuovere o da
  ricollegare, prima di investire ulteriore effort di refactoring su `SvnService`/`SvnCheckerHelper`.
