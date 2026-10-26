# SVN Merge Checker UI

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/license-Da%20definire-lightgrey)

## Sommario

- [Descrizione](#descrizione)
- [Caratteristiche principali](#caratteristiche-principali)
- [Prerequisiti](#prerequisiti)
- [Installazione](#installazione)
- [Avvio rapido / Utilizzo](#avvio-rapido--utilizzo)
- [Configurazione](#configurazione)
- [Esecuzione dei test](#esecuzione-dei-test)
- [Documentazione test](#documentazione-test)
- [Come contribuire](#come-contribuire)
- [Licenza](#licenza)

## Descrizione

**SVN Merge Checker UI** è un'applicazione desktop Windows (WinForms, .NET 8) che assiste sviluppatori e team di
rilascio nell'analisi predittiva dei merge tra repository Subversion (SVN). L'applicazione individua le revisioni
associate a issue o inserite manualmente, ne analizza le dipendenze incrociate sui file modificati e produce un
report di consistenza che evidenzia quali revisioni sono già state mergiate e quali richiedono ancora un merge,
distinguendo tra dipendenze dirette e indirette.

Il progetto è pensato per team che gestiscono flussi di merge complessi tra rami/repository SVN e necessitano di
uno strumento visuale per prevenire merge incompleti o inconsistenti prima di eseguire il comando `svn merge`.

## Caratteristiche principali

- Ricerca delle revisioni SVN per **codice Issue** oppure per **inserimento manuale** dei numeri di revisione.
- Analisi ciclica delle **dipendenze tra revisioni** basata sui file effettivamente modificati, estesa sia alle revisioni **precedenti** che a quelle **successive** nella cronologia SVN.
- Controllo capillare delle **collisioni di codice** analizzando i range di righe degli hunk nei diff SVN (`svn diff`) con margine di tolleranza di 3 righe, per rilevare reali sovrapposizioni tra modifiche.
- Ispezione dello storico con opzione `--stop-on-copy` nelle interrogazioni `svn log` per non trascinare le revisioni dei branch antenati durante l'analisi tra rami candidate e release.
- Verifica parallela opzionale (**Includi**) su varianti/rami collegati per verificare lo stato di merge delle issue anche sui repository correlati. Le voci della combo sono **configurabili** tramite il file `parallel_modules.json` e la dialog dedicata (pulsante ⚙); di default è presente solo *Generale*.
- Rilevamento automatico delle revisioni **già mergiate** tramite `svn mergeinfo`.
- Classificazione visiva dettagliata tramite simboli e colori:
  - 🟢 **Verde (`[✔ ]` / `[✔]`)** — `Mergiato` (revisione già mergiata) oppure `DipendenzaPrecedenteMergiata` (dipendenza precedente già mergiata).
  - 🔵 **Blu (`[▶]`)** — `DaMergiareDiretta` (revisione direttamente associata all'issue o inserita manualmente, ancora da mergiare).
  - 🔵 **Blu (`[❗▶]`)** — `DipendenzaDirettaAltraIssuePrecedenteDaMergiare` (dipendenza temporale precedente da mergiare che è contemporaneamente revisione diretta di un'altra issue).
  - ⚪ **Grigio chiaro (`[▶]`)** — `DipendenzaUtentePrecedenteDaMergiare` (revisione diretta utente emessa come dipendenza figlia all'interno della stessa issue).
  - 🟠 **Arancione (`[❗]`)** — `DaMergiareIndiretta` (dipendenza indiretta ancora da mergiare).
  - 🟠 **Arancione (`[▶]`)** — `DipendenzaSuccessivaDaMergiare` (dipendenza temporale successiva ancora da mergiare).
  - 🔴 **Rosso (`[✔]`)** — `DaMergiareIndirettaAlta` (dipendenza indiretta più recente di tutte le dirette) oppure `DipendenzaSuccessivaMergiata` (dipendenza temporale successiva già mergiata).
  - 🟤 **Marrone (`[❗▶]`)** — `DipendenzaPrecedenteDaMergiare` (dipendenza temporale precedente ancora da mergiare, non diretta per altre issue).
- Viste multiple del report: elenco revisioni (raggruppabile per Issue o per Merge con ordine crescente ed esclusione dei già mergiati), albero delle dipendenze, file coinvolti (raggruppabili per
  issue/revisione o per file), log di console.
- Generazione automatica del comando `svn merge` consigliato, limitato alle sole revisioni ancora da mergiare.
- Salvataggio e caricamento di **profili di configurazione** (working copy, repository sorgente, parametri) in un
  file JSON locale.
- Esportazione del report completo su file di testo.
- Raggruppamento delle revisioni di dipendenza (senza issue diretta) direttamente sotto l'header dell'issue di provenienza tramite il separatore `--- Revisioni senza issue diretta ---`.
- Posizionamento uniforme del simbolo di stato visivo in testa alla riga di ciascuna revisione (subito dopo il marcatore `>` o `=>`).
- Gestione corretta **per-issue** delle revisioni condivise: la stessa revisione può restare diretta per un'issue e comparire come dipendenza (con stato dedicato) in un'altra nella stessa esecuzione multi-issue.

## Prerequisiti

- **Sistema operativo:** Windows 7 SP1 o superiore (applicazione WinForms).
- **.NET SDK:** 8.0 o superiore ([download](https://dotnet.microsoft.com/download/dotnet/8.0)).
- **Subversion (SVN) CLI:** eseguibile `svn.exe` disponibile nel `PATH` di sistema.
- **Visual Studio 2022** (opzionale, per lo sviluppo tramite IDE) con carico di lavoro *.NET Desktop Development*.

## Installazione

1. Clonare il repository:

   ```powershell
   git clone  https://github.com/agambarelliApk/SVNMergeCheckerUI.git
   cd SVNMergeCheckerUI
   ```

2. Ripristinare le dipendenze NuGet e compilare il progetto:

   ```powershell
   dotnet restore SVNMergeCheckerUI.csproj
   dotnet build SVNMergeCheckerUI.csproj
   ```

3. In alternativa, aprire `SVNMergeCheckerUI.sln` (o il file di progetto) direttamente in Visual Studio 2022 e
   compilare tramite l'IDE.

## Avvio rapido / Utilizzo

Avviare l'applicazione in modalità Debug da riga di comando:

```powershell
dotnet run --project SVNMergeCheckerUI.csproj
```

Oppure eseguire l'eseguibile compilato:

```powershell
dotnet build -c Release SVNMergeCheckerUI.csproj
.\bin\Release\net8.0-windows7.0\SVNMergeCheckerUI.exe
```

Flusso tipico di utilizzo dalla UI:

1. Selezionare la cartella della **Working Copy** (destinazione del merge) e del **Source Repository** locale.
2. Inserire uno o più **codici Issue** oppure una lista di **numeri di revisione** manuali.
3. (Opzionale) Specificare le revisioni da escludere nel campo *Skip Revisions*.
4. (Opzionale) Scegliere nella combo **Includi** un modulo parallelo da verificare (o *Tutto*). Per aggiungere o
   rimuovere moduli, cliccare il pulsante **⚙** accanto alla combo (vedi sezione *Configurazione*).
5. Avviare l'analisi tramite il pulsante di esecuzione.
6. Consultare il report generato nelle diverse viste disponibili (Elenco Revisioni, Albero Dipendenze, File
   Coinvolti, Log Console) e, se necessario, esportarlo su file.

## Configurazione

- **Percorso `svn.exe`:** deve essere raggiungibile dal `PATH` di sistema; l'applicazione verifica la disponibilità
  del comando prima di ogni operazione SVN.
- **Profili di configurazione:** i parametri di working copy, repository sorgente e opzioni di analisi possono
  essere salvati/caricati tramite un file JSON (`svn_config.json`), generato automaticamente accanto
  all'eseguibile e gestito tramite l'interfaccia utente (pulsanti *Salva Configurazione* / *Carica
  Configurazione*).
- **File di output:** il percorso del report testuale esportabile è configurabile dalla UI (campo *Out File*).
- **Limite massimo revisioni:** il numero massimo di nuove revisioni analizzabili in un singolo ciclo è
  configurabile tramite l'apposito controllo numerico nell'interfaccia.
- **Timeout SVN:** configurabile dall'utente (5-600 secondi, default 60s) tramite controllo numerico dedicato, persistito nel profilo e applicato con kill automatico del processo in caso di scadenza.
- **Verifiche parallele (Includi):** configurabile tramite combobox (voce vuota = nessuna verifica, i moduli configurati e *Tutto*) per interrogare in parallelo i rami correlati preservando l'isolamento della working copy locale.
- **Moduli paralleli (`parallel_modules.json`):** file JSON accanto all'eseguibile che elenca le voci della combo *Includi*
  (esclusi la voce vuota e *Tutto*, che sono fissi). Ogni voce ha due campi: `Label` (testo mostrato in UI e
  nell'header del report `***** Label *****`) e `FolderName` (ultimo segmento dell'URL SVN della variante).

  ```json
  [
    { "Label": "TributiNet", "FolderName": "tributinet" },
    { "Label": "Generale", "FolderName": "generale" }
  ]
  ```

  Regole applicate in lettura e in salvataggio:
  - se il file manca o non è valido, la combo contiene solo la voce vuota e *Generale*;
  - *Generale* viene sempre garantito e collocato in ultima posizione (non è rimovibile);
  - la prima lettera di ogni `Label` viene portata in maiuscolo;
  - le voci con `Label` vuota o *Tutto* vengono scartate e i duplicati (senza distinzione tra maiuscole e minuscole) rimossi;
  - *Tutto* compare solo se le varianti sono almeno due.
- **Configurazione dei moduli dalla UI:** il pulsante **⚙** accanto alla combo *Includi* apre la dialog *Configura moduli
  paralleli*, che permette di aggiungere e rimuovere voci (tranne *Generale*):
  1. indicare l'**Etichetta** (`Label`);
  2. premere **...** e selezionare la cartella (working copy SVN) della variante: l'applicazione verifica con
     `svn info` che la cartella sia collegata a un URL SVN e ne ricava il `FolderName` dall'ultimo segmento dell'URL;
     se non viene trovato alcun URL compare un avviso e la cartella non può essere aggiunta;
  3. premere **Aggiungi** (abilitato solo con etichetta e cartella verificata) e infine **OK** per salvare il file e
     aggiornare la combo; **Annulla** non scrive nulla.

## Esecuzione dei test

(73 casi di test automatici xUnit, tutti verdi)

Per eseguire i test automatici:

```powershell
dotnet test SVNMergeCheckerUI.Tests/SVNMergeCheckerUI.Tests.csproj
```

La suite copre:
- Parsing e pivoting dei report (`ReportParserServiceTests`)
- Golden-file test di non-regressione formato output (`ReportParserServiceGoldenFileTests`)
- Algoritmi di collisione hunk e orchestrazione (`SvnCheckerHelperTests`)
- Persistenza e validazione profili JSON (`JsonConfigServiceTests`)
- Validazione argomenti e percorsi SVN (`SvnServiceTests`)
- Configurazione dei moduli paralleli `parallel_modules.json` (`VariantConfigServiceTests`)

## Documentazione test

Per l'elenco aggiornato dei test presenti nel workspace e il loro scopo, consultare `TESTS.md`.

## Come contribuire

1. Effettuare un **fork** del repository.
2. Creare un branch dedicato per la modifica:

   ```powershell
   git checkout -b feature/nome-della-modifica
   ```

3. Sviluppare la modifica seguendo le convenzioni di stile esistenti nel codice (interfacce `I*` con
   implementazione dedicata, servizi iniettati via costruttore, commenti in italiano coerenti con lo stile del
   progetto).
4. Verificare che il progetto compili correttamente:

   ```powershell
   dotnet build SVNMergeCheckerUI.csproj
   ```

5. Aprire una **Pull Request** descrivendo chiaramente lo scopo della modifica e i test manuali effettuati.

## Licenza

Da definire.
