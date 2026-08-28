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
- Analisi ciclica delle **dipendenze tra revisioni** basata sui file effettivamente modificati.
- Rilevamento automatico delle revisioni **già mergiate** tramite `svn mergeinfo`.
- Classificazione visiva a colori delle revisioni:
  - 🟢 **Verde** — già mergiata.
  - 🔵 **Blu** — da mergiare, direttamente coinvolta nell'issue/revisione richiesta.
  - 🟠 **Arancione** — da mergiare, dipendenza indiretta.
  - 🔴 **Rosso** — da mergiare, dipendenza indiretta con numero di revisione superiore alle dirette pendenti.
- Viste multiple del report: elenco revisioni, albero delle dipendenze, file coinvolti (raggruppabili per
  issue/revisione o per file), log di console.
- Generazione automatica del comando `svn merge` consigliato, limitato alle sole revisioni ancora da mergiare.
- Salvataggio e caricamento di **profili di configurazione** (working copy, repository sorgente, parametri) in un
  file JSON locale.
- Esportazione del report completo su file di testo.

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
4. Avviare l'analisi tramite il pulsante di esecuzione.
5. Consultare il report generato nelle diverse viste disponibili (Elenco Revisioni, Albero Dipendenze, File
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

## Esecuzione dei test

La solution include un progetto di test dedicato: `SVNMergeCheckerUI.Tests`.

Per eseguire i test automatici:

```powershell
dotnet test SVNMergeCheckerUI.Tests/SVNMergeCheckerUI.Tests.csproj
```

Il progetto di test copre attualmente il parsing dei report tramite `ReportParserServiceTests`.
Per validare le modifiche al comportamento complessivo dell'applicazione, resta utile affiancare anche una verifica manuale tramite l'interfaccia grafica su un repository SVN di prova.

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
