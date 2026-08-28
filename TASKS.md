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

- [x] **`SvnService.IsSvnAvailable()` (righe 19-39) reso asincrono** — ora `IsSvnAvailableAsync()` usa
  `WaitForExitAsync()` con timeout e termina il processo in caso di timeout. Aggiornati gli handler in
  `Form1.cs` per `await`-are la chiamata. (Completato: 2026-08-24 00:00:00)
- [x] **`SvnService.RunSvnAsync` e `SvnCheckerHelper.RunSvnRawAsync` allineati al pattern corretto** —
  riverifica puntuale sul codice sorgente attuale: entrambi i metodi usano già `await p.WaitForExitAsync(ct)`
  con `catch (OperationCanceledException) { p.Kill(entireProcessTree: true); throw; }` (nessun `Task.Run`
  con `WaitForExit()` sincrono residuo). Pattern coerente con `PowerShellRunnerService.RunAsync`.
  (Completato: 2026-08-24, nessuna modifica di codice necessaria)
- [x] **`Process.WaitForExit()`/`ReadToEnd()` sincroni già sostituiti** — entrambi i metodi usano
  `WaitForExitAsync()` + `ReadToEndAsync(ct)`, nessun wrapper `Task.Run` presente.
  (Completato: 2026-08-24)
- [x] **`ct.ThrowIfCancellationRequested()` verificato + fix propagazione `CancellationToken` mancante**
  — verifica end-to-end completata: i cicli di `SvnCheckerHelper` (`FindRevisionsByIssuesAsync`,
  `LoadManualRevisionsAsync`, `AnalyzeDependenciesAsync`) chiamano già `ct.ThrowIfCancellationRequested()`
  e `RunSvnRawAsync` killa correttamente il processo su cancellazione. Trovato però un bug reale: 
  `ISvnService.GetSvnUrlAsync`/`GetMergedRevisionsAsync` non accettavano `CancellationToken` ed erano
  invocati internamente con `CancellationToken.None`, quindi la cancellazione utente non killava il
  processo `svn.exe` se in corso durante queste due chiamate (usate in testa a
  `SvnCheckerHelper.RunAsync`). Fix applicato: aggiunto parametro opzionale `CancellationToken ct = default`
  a entrambi i metodi in `ISvnService`/`SvnService`, propagato fino a `RunSvnAsync`; `SvnCheckerHelper.RunAsync`
  ora passa il proprio `ct` in entrambe le chiamate. Build validata.
  (Completato: 2026-08-24, build validata)
 - [x] **Aggiungere timeout configurabili** per le chiamate a `svn.exe` (oggi solo `IsSvnAvailable` ha un
   timeout esplicito di 5000ms; le altre chiamate in `RunSvnAsync`/`RunSvnRawAsync` non hanno timeout e
   possono bloccare indefinitamente in caso di prompt di autenticazione interattiva, nonostante
   `UseShellExecute = false`).
  - [x] **Codice**: aggiunto `DefaultSvnTimeoutMs` (60000ms) + `CancellationTokenSource.CreateLinkedTokenSource`
    in `SvnService.RunSvnAsync` e `SvnCheckerHelper.RunSvnRawAsync`; allo scadere del timeout il processo
    viene killato e viene lanciata una `TimeoutException` (distinta dalla `OperationCanceledException` di
    cancellazione utente). Build validata.
  - [x] **GUI**: esposto il timeout come impostazione configurabile dall'utente (non solo costante nel
    codice), persistito in `AppConfig`/`svn_config.json` tramite `IConfigService` (nuovo campo
    `SvnTimeoutSeconds`, default 60) e gestito da un nuovo controllo `NumericUpDown numSvnTimeout`
    (5-600s, step 5) in `Form1` (`grpParametri`, `Form1.Designer.cs`). Il valore è propagato come
    parametro opzionale a `ISvnService`/`SvnService` (`GetSvnUrlAsync`/`UpdateDirectoryAsync`/
    `GetMergedRevisionsAsync`) e a `SvnCheckerHelper.RunAsync` (via `SvnCheckerParameters.SvnTimeoutSeconds`)
    invece della costante `DefaultSvnTimeoutMs` fissa; letto/scritto in `BuildConfig`/`ApplyConfig` e usato
    in `btnSvnConnect_Click`/`btnSvnUpdate_Click`/`btnRun_Click`.
    - ? Completed and validated on 2026-08-25

## 2. Separazione della logica di business dalla UI

- [x] **Spostare la logica di rendering/parsing oggi in `Form1.cs`** in un servizio dedicato
  (`IRevisionRenderingService`/`RevisionRenderingService`, nuovo file `RevisionRenderingService.cs`):
  - `RenderFileCoinvoltiGrouped`/`RenderFileCoinvoltiGroupedLegacy`/`AppendRevisionEntry` rimossi da
    `Form1.cs`; la logica di parsing dei blocchi issue/revisione/file (con fallback legacy) è stata
    spostata in `RevisionRenderingService.BuildFileCoinvoltiModel`, che produce un modello dati
    strutturato (`FileCoinvoltiModel`/`FileCoinvoltiNode`) invece di stringhe grezze da ri-analizzare.
  - `NormalizeRevisionText` ed `ExtractRevisionNumber` centralizzati come metodi pubblici di
    `ReportParserService` (unica fonte di verità), riusati sia dal nuovo servizio che in precedenza
    duplicati localmente in `Form1`.
  - `GetStateVisual` resta in `Form1` (mapping stato?colore, responsabilità della UI) ma ora riceve
    dati già pre-parsati/strutturati dal modello (`FileCoinvoltiNode.State`) invece di stringhe grezze;
    `Form1` si limita a iterare il modello (`RenderFileCoinvoltiModel`/`RenderFileCoinvoltiNode`/
    `AppendRevisionMarkerNode`) scrivendo su `rtbOutput` (font/colore/testo), senza più logica di
    parsing/regex propria. Build validata.
  - ? Completed and validated on 2026-08-29
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
- [x] **Pulizia codice morto (sessione di verifica dedicata)** — riverifica puntuale confermata,
  rimosso in un'unica passata (Completato: 2026-08-24, build validata):
  - `Form1.cs`: rimossi i metodi `ParseMergedRevisionsFromScriptOutput`, `FindScriptPath`,
    `BuildEffectiveSkipRevisionsAsync`, `ParseSkipRevisionSet` (nessuna chiamata residua nel progetto).
  - `RevisionMergeInfo.cs`: file rimosso interamente (`IPowerShellRunnerService` +
    `PowerShellRunnerService`), non iniettato né istanziato.
  - `ReportParserService.cs`: rimossa la riga duplicata `using System.Text;`.
  - ? Completed and validated on 2026-08-24
- ?? **Introdurre un layer di validazione input centralizzato**: oggi `ValidateInputs()` (righe
  267-275) valida solo issue/revisioni; la validazione di percorsi (`txtWorkingCopy`, `txtSourceRepo`)
  e del campo numerico avviene implicitamente altrove o per nulla. Centralizzare in un servizio/metodo
  statico dedicato in `SvnCheckerModels.cs`.
- [x] **`Form1` istanzia direttamente `new SvnCheckerHelper(_svnService)` dentro `btnRun_Click`**
  — risolto: aggiunto il campo `_svnCheckerHelper`, istanziato una sola volta nel costruttore di
  `Form1` come gli altri servizi (`_svnService`, `_configService`, `_reportParser`); `btnRun_Click`
  ora riusa l'istanza condivisa invece di crearne una nuova ad ogni click. Build validata.
  - ? Completed and validated on 2026-08-29
- ?? **Estrarre in un piccolo helper/ViewModel la costruzione di `SvnCheckerParameters`**
  (righe 208-234), oggi costruita inline nel click handler leggendo direttamente i controlli WinForms.

 - [x] **Modalità Config (`cmbMode`) per il caricamento di `cmbResultType` (Standard/Debug)** —
   riverifica puntuale: `cmbMode` è inizializzato in `Form1()` (righe 32-35) con voci `Standard`/`Debug`,
   `cmbMode_SelectedIndexChanged` (riga 196) chiama `ApplyModeToResultType()` (righe 200-225) che
   ricostruisce gli items di `cmbResultType` mostrando `Log Console`/`Script output` solo in modalità
   Debug. Default = `Standard`. (Verificato: 2026-08-24, già implementato)

## 3. Unit test per il parser e per i servizi

- [x] **Creare il progetto di test** `SVNMergeCheckerUI.Tests` (xUnit + `coverlet.collector`): il progetto
  esiste già nella solution, è referenziato correttamente e compila con il resto dell'applicazione. Build
  validata.
  - ? Completed and validated on 2026-08-28
- [x] **Test per `ReportParserService`** (facile da testare, nessuna dipendenza esterna):
  - `ExtractSection1`/`ExtractBetween`/`ExtractFrom`: verificare i marker esatti (`Header1/2/3`,
    incluso il fallback quando la sezione "1. REVISIONI RAGGRUPPATE PER ISSUE:" non è presente),
    e il messaggio di errore `"[Sezione '...' non trovata nell'output.]"` riprodotto nel bug
    segnalato dall'utente in precedenza ("Albero Dipendenze" non trovata).
  - `ParseRevisioniLines`: verificare che righe senza match di `RevisionPattern` o con revisione non
    presente nel dizionario `revisionStates` producano `state == null` (comportamento introdotto di
    recente per non colorare intestazioni/righe vuote).
  - `PivotFileCoinvolti`: verificare la corretta pivotazione revisione?file in file?issue?revisioni.
  - Stato attuale: presenti `ReportParserServiceTests.cs` con copertura di `Parse`, `ParseRevisioniLines`
    e `PivotFileCoinvolti`.
  - Nota: il `using System.Text;` duplicato nel file sorgente è già stato rimosso.
  - ? Completed and validated on 2026-08-28
- [x] **Test per `SvnCheckerHelper.RunAsync`**, mockando `ISvnService`: aggiunto
  `SvnCheckerHelperTests.cs` con un `ISvnService` finto (nessuna chiamata reale a `svn.exe`). Copertura
  attuale: eccezione quando l'URL del repository sorgente non è risolvibile, risultato vuoto quando
  non sono forniti né issue né revisioni manuali, corretto utilizzo dell'URL risolto anche quando
  `SourceRepository` è un percorso locale.
  - Nota: il calcolo dei 4 stati (`Mergiato`/`DaMergiareDiretta`/`DaMergiareIndiretta`/
    `DaMergiareIndirettaAlta`) e il flusso completo con issue/revisioni reali richiedono di simulare
    anche l'esecuzione di `svn log`/`svn diff` (oggi chiamati direttamente da `SvnCheckerHelper` senza
    un'astrazione iniettabile): resta un'estensione futura, tracciata più sotto insieme al punto
    `IProcessRunner`.
  - ? Completed and validated on 2026-08-28 (build + test verdi)
- [x] **Test per `JsonConfigService`**: aggiunto `JsonConfigServiceTests.cs`. Copertura: label vuota
  (`ArgumentException`), label duplicata senza `overwrite` (`InvalidOperationException`), overwrite
  esplicito che sostituisce il profilo, `LabelExists`, round-trip completo di tutti i campi tramite
  `Save`/`LoadAll`, store assente (`LoadAll` vuoto) e store con JSON non valido (`LoadAll` vuoto,
  nessuna eccezione). Per isolare i test da `AppContext.BaseDirectory` è stato aggiunto un costruttore
  `internal JsonConfigService(string storeDirectory)` (nessuna modifica al comportamento pubblico) ed
  esposto al progetto di test tramite `InternalsVisibleTo` in `SVNMergeCheckerUI.csproj`.
  - ? Completed and validated on 2026-08-28 (build + test verdi)
- [x] **Test per `SvnService`/`SvnCheckerHelper` (parte processo)**: aggiunto `SvnServiceTests.cs`,
  limitato ai percorsi di `SvnService` che non richiedono l'esecuzione reale di `svn.exe`
  (URL già risolti, percorsi remoti, percorsi locali inesistenti, argomenti vuoti). Non è stata
  introdotta l'astrazione `IProcessRunner` iniettabile: resta un'attività separata se si vorrà in
  futuro testare anche la costruzione esatta degli argomenti CLI (`"info", "--show-item", "url"`,
  `"mergeinfo", "--show-revs", "merged"`, ecc.) senza alcuna dipendenza da `svn.exe` installato.
  - ? Completed and validated on 2026-08-28 (build + test verdi)
- [x] **Golden-file test** per `ReportParserService`: aggiunto `ReportParserServiceGoldenFileTests.cs`
  con fixture in `SVNMergeCheckerUI.Tests/Fixtures/` (`SampleReport.txt` nello stesso formato prodotto
  da `SvnCheckerHelper.BuildReportText`, più un file atteso per ciascuna vista: Elenco Revisioni,
  Albero Dipendenze, File Coinvolti - Raw, File Coinvolti pivotato). Le fixture sono copiate in output
  tramite `CopyToOutputDirectory` nel `.csproj` di test.
  - ? Completed and validated on 2026-08-28 (build + test verdi)

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
