#=====================
# ESEGUIRE LO SCRIPT
# 1 recarti nella repository della tua WorkingCopy (es C:\APSNet\lib\src.rsunet)
# 1 esegui pawer shell come amministratore nella directory
# 2 sblocca l'esecuzione pawer shell(AD OGNI SESSIONE) : Set-ExecutionPolicy Bypass -Scope Process
# 3 lancia l'elalaborazione dicitando
#		(C:\<percorso_del_file_ps1>\svn_predictive_merge_checker.ps1
#====================================================================
#====================================================================
# ANALISI PREDITTIVA E CONSISTENZA MERGE SVN (WINDOWS - POWERSHELL)
#====================================================================

param(
    [string]$WorkingCopy = (Get-Item .).FullName,
    [string]$SourceRepository,
    [string[]]$Issues = @(),
    [int[]]$Revisions = @(),
    [int[]]$SkipRevisions = @(284490, 284715, 285241, 285772, 286388, 287834, 288900, 289118),
    [switch]$NonInteractive,
    [int]$MaxNewRevs = 500,
    [switch]$DryRun,
    [string]$OutFile
)

# Helper: wrapper sicuro per invocare svn e riportare errori
function Invoke-Svn {
    param([string[]]$SvnArgs)
    $cmdLine = "svn " + ($SvnArgs -join ' ')
    try {
        $output = & svn @SvnArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            if ($output -notmatch "E160013|path not found") {
                Write-Error "[SVN ERROR] comando: $cmdLine`n$output"
            }
            return $null
        }
        return $output
    } catch {
        Write-Error "[SVN EXCEPTION] $_"
        return $null
    }
}

# Helper: esegue l'update SVN sulla directory/target specificato
function Update-SvnTarget {
    param(
        [string]$Path,
        [string]$Label
    )
    if (Test-Path $Path) {
        Write-Host "[-] Aggiornamento SVN in corso per $Label ($Path)..." -ForegroundColor Cyan
        $updateOut = Invoke-Svn @('update', "`"$Path`"")
        if ($updateOut) {
            Write-Host "  [OK] $Label aggiornato con successo." -ForegroundColor Green
        } else {
            Write-Host "  [WARN] Impossibile aggiornare $Label. Proseguo con la versione locale corrente." -ForegroundColor Yellow
        }
    } else {
        Write-Host "  [SKIP] Impossibile eseguire l'update: il percorso '$Path' non esiste sul file system." -ForegroundColor Gray
    }
}

# --------------------------------------------------------------------
# 1. Validazione Ambiente, Working Copy e Source Repository
# --------------------------------------------------------------------
if (-not (Get-Command svn -ErrorAction SilentlyContinue)) {
    Write-Host "[ERRORE] svn non trovato nel PATH. Installa Subversion o aggiungilo al PATH." -ForegroundColor Red
    return
}

if (-not (Test-Path $WorkingCopy)) {
    Write-Host "[ERRORE] La Working Copy specificata non esiste: $WorkingCopy" -ForegroundColor Red
    return
}

if (-not $SourceRepository -and -not $NonInteractive) {
    $wcInput = Read-Host "Inserisci il path della Working Copy (Destinazione) [Default: $WorkingCopy]"
    if (-not [string]::IsNullOrWhiteSpace($wcInput)) { $WorkingCopy = $wcInput }

    $srcInput = Read-Host "Inserisci il path locale o URL del Source Repository (Sorgente) [Default: $WorkingCopy]"
    if (-not [string]::IsNullOrWhiteSpace($srcInput)) { 
        $SourceRepository = $srcInput 
    } else { 
        $SourceRepository = $WorkingCopy 
    }
}
elseif (-not $SourceRepository) {
    $SourceRepository = $WorkingCopy
}

# --------------------------------------------------------------------
# 1.1 Aggiornamento (svn update) preliminare
# --------------------------------------------------------------------
Write-Host "`n[*] Esecuzione aggiornamento preliminare del codice (svn update)..." -ForegroundColor Green

Update-SvnTarget -Path $SourceRepository -Label "Source Repository"

if ($WorkingCopy -and (Resolve-Path $WorkingCopy).Path -ne (Resolve-Path $SourceRepository).Path) {
    Update-SvnTarget -Path $WorkingCopy -Label "Working Copy"
}

$RepoUrl = (Invoke-Svn @('info', '--show-item', 'url', $SourceRepository))
if ($RepoUrl) { $RepoUrl = $RepoUrl.Trim() }

if (-not $RepoUrl) {
    Write-Host "[ERRORE] Impossibile recuperare l'URL del Repository dalla sorgente '$SourceRepository'." -ForegroundColor Red
    return
}

$IssueList = @()
$ManualRevList = @()

if ($Issues -and $Issues.Count -gt 0) { 
    $IssueList = $Issues 
}
elseif (-not $NonInteractive) {
    $IssueInput = Read-Host "Inserisci i codici delle Issue separati da virgola (es. APKHTRSU-693, APKHTIMU-203)"
    if (-not [string]::IsNullOrWhiteSpace($IssueInput)) { 
        $IssueList = $IssueInput.Split(',') | ForEach-Object { $_.Trim() } 
    } else {
        Write-Host "[INFO] Nessuna Issue inserita. Passaggio all'inserimento manuale delle revisioni..." -ForegroundColor Gray
        $RevInput = Read-Host "Inserisci numeri di revisione specifici separati da virgola (es. 12345, 12348)"
        if (-not [string]::IsNullOrWhiteSpace($RevInput)) {
            $RevInput.Split(',') | ForEach-Object {
                $s = $_.Trim()
                $num = 0
                if ([int]::TryParse($s, [ref]$num)) { $ManualRevList += $num }
            }
        }
    }
}
else {
    if ($Revisions -and $Revisions.Count -gt 0) { 
        $ManualRevList = $Revisions 
    } else { 
        Write-Host "[ERRORE] In modalita non-interattiva serve -Issues o -Revisions." -ForegroundColor Red
        return 
    }
}

if ($IssueList.Count -eq 0 -and $ManualRevList.Count -eq 0) {
    Write-Host "[ERRORE] Operazione annullata. E necessario specificare almeno una Issue o una Revisione." -ForegroundColor Red
    return
}

# --------------------------------------------------------------------
# 2. Inizializzazione Strutture Dati
# --------------------------------------------------------------------
$RevisioniDaElaborare = [System.Collections.Generic.List[int]]::new()
$AlberoDerivazioni = [ordered]@{} 
$FilePerRevisione = @{}          
$DettagliRevisioni = @{}
$DateRevisioniIniziali = [System.Collections.Generic.List[DateTime]]::new()
$ProcessedFiles = [System.Collections.Generic.HashSet[string]]::new()

function Associazioni-Revisione([int]$targetRev, [DateTime]$rDate, [string]$rAuth, [string]$rMsg) {
    if ($RevisioniDaElaborare.Count -ge $MaxNewRevs) {
        Write-Host "[WARN] Limite massimo di revisioni raggiunto ($MaxNewRevs)." -ForegroundColor Yellow
        return
    }
    if (-not $RevisioniDaElaborare.Contains($targetRev)) {
        $RevisioniDaElaborare.Add($targetRev)
        $AlberoDerivazioni["$targetRev"] = [System.Collections.Generic.List[string]]::new()
        $DateRevisioniIniziali.Add($rDate)
        $DettagliRevisioni["$targetRev"] = @{ 
            Autore      = $rAuth; 
            Descrizione = $rMsg; 
            Data        = $rDate;
            IsMerged    = $false
        }
    }
}

if ($IssueList.Count -gt 0) {
    Write-Host "[-] Scansione log remoti sul repository sorgente per identificare le issue..." -ForegroundColor Cyan
    $SvnLogXmlRaw = Invoke-Svn @('log', '-l', '500', '--xml', $RepoUrl)
    if ($SvnLogXmlRaw) {
        try { [xml]$SvnLogXml = $SvnLogXmlRaw } catch { $SvnLogXml = $null }
    }
    if ($SvnLogXml -and $SvnLogXml.log.logentry) {
        foreach ($entry in $SvnLogXml.log.logentry) {
            $currentLogRev = [int]$entry.revision
            $currentLogDate = [DateTime]$entry.date
            $autore = if ($entry.author) { $entry.author.Trim() } else { "Unknown" }
            $msg = if ($entry.msg) { $entry.msg.Trim() } else { "Nessuna descrizione" }

            foreach ($issue in $IssueList) {
                if ($msg -match [regex]::Escape($issue)) {
                    Associazioni-Revisione $currentLogRev $currentLogDate $autore $msg
                }
            }
        }
    }
}

if ($ManualRevList.Count -gt 0) {
    Write-Host "[-] Recupero dettagli per le revisioni inserite manualmente..." -ForegroundColor Cyan
    foreach ($mRev in $ManualRevList) {
        $SvnSingleLogRaw = Invoke-Svn @('log', '-c', $mRev, '--xml', $RepoUrl)
        if ($SvnSingleLogRaw) {
            try { [xml]$SvnSingleLogXml = $SvnSingleLogRaw } catch { $SvnSingleLogXml = $null }
            if ($SvnSingleLogXml -and $SvnSingleLogXml.log.logentry) {
                $entry = $SvnSingleLogXml.log.logentry
                $currentLogDate = [DateTime]$entry.date
                $autore = if ($entry.author) { $entry.author.Trim() } else { "Unknown" }
                $msg = if ($entry.msg) { $entry.msg.Trim() } else { "Nessuna descrizione" }
                
                Associazioni-Revisione $mRev $currentLogDate $autore $msg
            }
        }
    }
}

if ($RevisioniDaElaborare.Count -eq 0) {
    Write-Host "[!] Nessuna revisione da elaborare." -ForegroundColor Yellow
    return
}

# Calcolo SearchMinDate
$SearchMinDate = $null
if ($DateRevisioniIniziali.Count -gt 0) {
    $SearchMinDate = ($DateRevisioniIniziali | Measure-Object -Minimum).Minimum
} else {
    $SearchMinDate = (Get-Date).AddYears(-1)
}

# ====================================================================
# BOX DI RIEPILOGO INIZIALE CONFIGURAZIONE DI ANALISI
# ====================================================================
Clear-Host
Write-Host "====================================================" -ForegroundColor Yellow
Write-Host " RIEPILOGO CONFIGURAZIONE DI ANALISI" -ForegroundColor Yellow
Write-Host "====================================================" -ForegroundColor Yellow
Write-Host "Working Copy (Destinazione): $WorkingCopy" -ForegroundColor White
Write-Host "Source Repo (Sorgente)     : $SourceRepository" -ForegroundColor White
Write-Host "URL Repository SVN         : $RepoUrl" -ForegroundColor White
Write-Host "Flusso Utilizzato          : $(if($IssueList.Count -gt 0){'Ricerca per Issue'}else{'Inserimento Manuale Revisions'})" -ForegroundColor White
if ($IssueList.Count -gt 0) {
    Write-Host "Issue Inserite             : $($IssueList -join ', ')" -ForegroundColor White
}
Write-Host "Revisioni Iniziali Rilevate : $(($RevisioniDaElaborare | Sort-Object) -join ', ')" -ForegroundColor White
Write-Host "Data Limite Ricerca        : $($SearchMinDate.ToString('dd/MM/yyyy HH:mm:ss'))" -ForegroundColor White
Write-Host "====================================================`n" -ForegroundColor Yellow

if (-not $NonInteractive) {
    $Conferma = Read-Host "Vuoi procedere con l'analisi della consistenza? [S/N]"
    if ($Conferma.Trim().ToUpper() -ne "S") {
        Write-Host "[INFO] Operazione annullata dall'utente." -ForegroundColor Yellow
        return
    }
}

# --------------------------------------------------------------------
# 3. Analisi Ciclica delle Dipendenze sui File
# --------------------------------------------------------------------
Write-Host "`n[*] Avvio ciclo di analisi consistenza file..." -ForegroundColor Green
$index = 0

while ($index -lt $RevisioniDaElaborare.Count) {
    $rev = $RevisioniDaElaborare[$index]
    Write-Host "[-] Analisi file modificati nella revisione $rev..." -ForegroundColor Yellow
    
    $DiffLines = Invoke-Svn @('diff', '--summarize', '-c', $rev, $RepoUrl)
    $FilesinRev = @()
    foreach ($line in $DiffLines) {
        if ($line -match '^[ADMR]\s+(.+)$') {
            $filePath = $Matches[1].Trim()
            $FilesinRev += $filePath
        }
    }
    $FilePerRevisione["$rev"] = $FilesinRev

    foreach ($file in $FilesinRev) {
        if ($ProcessedFiles.Contains($file)) { continue }
        [void]$ProcessedFiles.Add($file)

        if ($file -like "http*") {
            $FileFullUrl = $file
        } else {
            $cleanRepo = $RepoUrl.TrimEnd('/')
            $cleanPath = $file.TrimStart('/')
            $FileFullUrl = "$cleanRepo/$cleanPath"
        }

        $FileFullUrlWithPeg = "${FileFullUrl}@$rev"

        $RawLog = Invoke-Svn @('log', '--xml', $FileFullUrlWithPeg)
        if ([string]::IsNullOrWhiteSpace($RawLog)) { 
            Write-Host "  [INFO] Impossibile recuperare il log per '$file' (File rimosso o spostato)." -ForegroundColor Gray
            continue 
        }

        $FileLogXml = $null
        try { [xml]$FileLogXml = $RawLog } catch { continue }

        if ($FileLogXml -and $FileLogXml.log -and $FileLogXml.log.logentry) {
            foreach ($entry in $FileLogXml.log.logentry) {
                $f_rev = [int]$entry.revision
                $f_date = [DateTime]$entry.date
                $f_auth = if ($entry.author) { $entry.author.Trim() } else { "Unknown" }
                $f_msg = if ($entry.msg) { $entry.msg.Trim() } else { "Nessuna descrizione" }
                
                if ($f_date -lt $SearchMinDate) { continue }

                if ($RevisioniDaElaborare -notcontains $f_rev) {
                    Write-Host "  [DIPENDENZA TROVATA] Il file '$file' richiede la $f_rev (Autore: $f_auth)" -ForegroundColor DarkYellow
                    if ($RevisioniDaElaborare.Count -ge $MaxNewRevs) {
                        Write-Host "  [WARN] Skipping new revision ${f_rev}: massimo di revisioni ($MaxNewRevs) raggiunto." -ForegroundColor Yellow
                    } else {
                        $RevisioniDaElaborare.Add($f_rev)
                        $DettagliRevisioni["$f_rev"] = @{ 
                            Autore      = $f_auth; 
                            Descrizione = $f_msg; 
                            Data        = $f_date;
                            IsMerged    = $false 
                        }

                        if (-not $AlberoDerivazioni.Contains("$f_rev")) { $AlberoDerivazioni["$f_rev"] = [System.Collections.Generic.List[string]]::new() }
                        if (-not $AlberoDerivazioni.Contains("$rev")) { $AlberoDerivazioni["$rev"] = [System.Collections.Generic.List[string]]::new() }
                        
                        $AlberoDerivazioni["$rev"].Add("revisione derivata da $rev da file in $f_rev")
                    }
                }
            }
        }
    }
    $index++
}

# --------------------------------------------------------------------
# 3.1 Recupero RevsMergiate e Verifica Stato
# --------------------------------------------------------------------
Write-Host "`n[-] Confronto revisioni estratte con quelle già mergiate (RevsMergiate)..." -ForegroundColor Cyan

$RevsMergiate = [System.Collections.Generic.HashSet[int]]::new()

# Interroga SVN mettendo in relazione il Repository Sorgente ($RepoUrl) e la Working Copy ($WorkingCopy)
$AlreadyMergedRaw = Invoke-Svn @('mergeinfo', '--show-revs=merged', $RepoUrl, "`"$WorkingCopy`"")

if ($AlreadyMergedRaw) {
    foreach ($line in $AlreadyMergedRaw) {
        $cleanLine = $line.Trim().TrimStart('r')
        if ($cleanLine -match '^(\d+)-(\d+)$') {
            $start = [int]$Matches[1]
            $end = [int]$Matches[2]
            for ($r = $start; $r -le $end; $r++) { [void]$RevsMergiate.Add($r) }
        }
        else {
            $n = 0
            if ([int]::TryParse($cleanLine, [ref]$n)) { [void]$RevsMergiate.Add($n) }
        }
    }
}

if ($SkipRevisions) {
    foreach ($skipRev in $SkipRevisions) {
        [void]$RevsMergiate.Add([int]$skipRev)
    }
}

foreach ($revKey in $DettagliRevisioni.Keys) {
    $revNum = [int]$revKey
    if ($RevsMergiate.Contains($revNum)) {
        $DettagliRevisioni[$revKey].IsMerged = $true
    }
}

# Marker leggibile dalla GUI C# per popolare _mergedRevisions
Write-Host "##MERGED_REVISIONS:$($RevsMergiate -join ',')"

# --------------------------------------------------------------------
# 4. Generazione e Stampa Report Finale
# --------------------------------------------------------------------
function Genera-TestoReport {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("====================================================")
    [void]$sb.AppendLine(" REPORT DI CONSISTENZA FINALE PER MERGE")
    [void]$sb.AppendLine("====================================================")
    [void]$sb.AppendLine("Working Copy (Destinazione): $WorkingCopy")
    [void]$sb.AppendLine("Source Repo (Sorgente)     : $SourceRepository")
    [void]$sb.AppendLine("URL Repository SVN         : $RepoUrl")
    [void]$sb.AppendLine("Flusso Utilizzato          : $(if($IssueList.Count -gt 0){'Ricerca per Issue'}else{'Inserimento Manuale Revisions'})")
    if ($IssueList.Count -gt 0) {
        [void]$sb.AppendLine("Issue Inserite             : $($IssueList -join ', ')")
    }
    [void]$sb.AppendLine("Revisioni Rilevate         : $(($RevisioniDaElaborare | Sort-Object) -join ', ')")
    [void]$sb.AppendLine("Data Limite Ricerca        : $($SearchMinDate.ToString('dd/MM/yyyy HH:mm:ss'))")
    [void]$sb.AppendLine("====================================================`n")
    
    [void]$sb.AppendLine("1. ELENCO COMPLETO REVISIONI ORDINATO (Crescente):")
    $RevisioniOrdinate = $RevisioniDaElaborare | Sort-Object
    foreach ($rev in $RevisioniOrdinate) {
        $dati = $DettagliRevisioni["$rev"]
        $autore = if ($dati) { $dati.Autore } else { "Unknown" }
        $dataStr = if ($dati) { $dati.Data.ToString("dd/MM/yyyy HH:mm") } else { "N/D" }
        $descrizione = if ($dati) { $dati.Descrizione.Replace("`n", " ").Replace("`r", "") } else { "Nessuna descrizione" }
        if ($descrizione.Length -gt 70) { $descrizione = $descrizione.Substring(0, 67) + "..." }
        
        $tagStato = if ($dati -and $dati.IsMerged) { "(Mergiato)" } else { "(Da Mergiare)" }

        [void]$sb.AppendLine(" => $rev del $dataStr [$autore] : $descrizione $tagStato")
    }
    [void]$sb.AppendLine("")
    
    [void]$sb.AppendLine("2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE:")
    foreach ($key in $AlberoDerivazioni.Keys) {
        [void]$sb.AppendLine("$key")
        $derivazioni = $AlberoDerivazioni[$key]
        if ($derivazioni.Count -gt 0) {
            foreach ($del in $derivazioni) { [void]$sb.AppendLine("    > $del") }
        } else {
            [void]$sb.AppendLine("    > [Nessuna revisione figlia / Isolato]")
        }
    }
    [void]$sb.AppendLine("")
    
    [void]$sb.AppendLine("3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE:")
    foreach ($rev in $RevisioniOrdinate) {
        [void]$sb.AppendLine("REVISIONE ${rev}:")
        $files = $FilePerRevisione["$rev"]
        if ($files) {
            foreach ($f in $files) { [void]$sb.AppendLine("  - $f") }
        } else {
            [void]$sb.AppendLine("  - Nessun file rilevato o operazione di sola proprieta.")
        }
        [void]$sb.AppendLine("----------------------------------------------------")
    }
    [void]$sb.AppendLine("")
    
    $RevisioniSoloDaMergere = $RevisioniOrdinate | Where-Object { -not $DettagliRevisioni["$_"].IsMerged }
    $CampioneMerge = $RevisioniSoloDaMergere -join ","
    [void]$sb.AppendLine("[INFO] Comando consigliato per eseguire il merge sulla Working Copy (Solo da mergiare):")
    [void]$sb.AppendLine("svn merge -c $CampioneMerge $RepoUrl `"$WorkingCopy`"")
    
    return $sb.ToString()
}

# Stampa Report Finale a Schermo
Write-Host "`n====================================================" -ForegroundColor Green
Write-Host " REPORT DI CONSISTENZA FINALE PER MERGE" -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Green
Write-Host "Working Copy (Destinazione): $WorkingCopy" -ForegroundColor White
Write-Host "Source Repo (Sorgente)     : $SourceRepository" -ForegroundColor White
Write-Host "URL Repository SVN         : $RepoUrl" -ForegroundColor White
Write-Host "Flusso Utilizzato          : $(if($IssueList.Count -gt 0){'Ricerca per Issue'}else{'Inserimento Manuale Revisions'})" -ForegroundColor White
if ($IssueList.Count -gt 0) {
    Write-Host "Issue Inserite             : $($IssueList -join ', ')" -ForegroundColor White
}
Write-Host "Revisioni Rilevate         : $(($RevisioniDaElaborare | Sort-Object) -join ', ')" -ForegroundColor White
Write-Host "Data Limite Ricerca        : $($SearchMinDate.ToString('dd/MM/yyyy HH:mm:ss'))" -ForegroundColor White
Write-Host "====================================================`n" -ForegroundColor Green

Write-Host "1. ELENCO COMPLETO REVISIONI ORDINATO (Crescente):" -ForegroundColor Cyan
$RevisioniOrdinate = $RevisioniDaElaborare | Sort-Object
foreach ($rev in $RevisioniOrdinate) {
    $dati = $DettagliRevisioni["$rev"]
    $autore = if ($dati) { $dati.Autore } else { "Unknown" }
    $dataStr = if ($dati) { $dati.Data.ToString("dd/MM/yyyy HH:mm") } else { "N/D" }
    $descrizione = if ($dati) { $dati.Descrizione.Replace("`n", " ").Replace("`r", "") } else { "Nessuna descrizione" }
    if ($descrizione.Length -gt 70) { $descrizione = $descrizione.Substring(0, 67) + "..." }
    
    if ($dati -and $dati.IsMerged) {
        Write-Host " => $rev del $dataStr [$autore] : $descrizione (Mergiato)" -ForegroundColor Green
    } else {
        Write-Host " => $rev del $dataStr [$autore] : $descrizione (Da Mergiare)" -ForegroundColor Gray
    }
}
Write-Host ""

Write-Host "2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE:" -ForegroundColor Cyan
foreach ($key in $AlberoDerivazioni.Keys) {
    Write-Host " > $key" -ForegroundColor White
    $derivazioni = $AlberoDerivazioni[$key]
    if ($derivazioni.Count -gt 0) {
        foreach ($del in $derivazioni) { Write-Host "      - $del" -ForegroundColor DarkYellow }
    } else {
        Write-Host "      - [Nessuna revisione figlia / Isolato]" -ForegroundColor Gray
    }
}
Write-Host ""

Write-Host "3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE:" -ForegroundColor Cyan
foreach ($rev in $RevisioniOrdinate) {
    Write-Host "> REVISIONE ${rev}:" -ForegroundColor Yellow
    $files = $FilePerRevisione["$rev"]
    if ($files) {
        foreach ($f in $files) { Write-Host "  - $f" -ForegroundColor White }
    } else {
        Write-Host "  - Nessun file rilevato o operazione di sola proprieta." -ForegroundColor Gray
    }
    Write-Host "----------------------------------------------------"
}

# Estrae solo le revisioni escluse quelle (Mergiato) per la composizione del comando di merge finale
$RevisioniSoloDaMergere = $RevisioniOrdinate | Where-Object { -not $DettagliRevisioni["$_"].IsMerged }
$CampioneMerge = $RevisioniSoloDaMergere -join ","

# Salvataggio facoltativo su file
if ($OutFile) {
    (Genera-TestoReport) | Out-File -FilePath $OutFile -Encoding utf8 -Force
    Write-Host "`n[OK] Report salvato in: $OutFile" -ForegroundColor Green
}
elseif (-not $NonInteractive) {
    $SalvaRisposta = Read-Host "`nVuoi salvare il report completo su file? [S/N]"
    if ($SalvaRisposta.Trim().ToUpper() -eq "S") {
        $TargetFolder = Read-Host "Percorso cartella di destinazione [Default: directory corrente]"
        if ([string]::IsNullOrWhiteSpace($TargetFolder)) { $TargetFolder = (Get-Item .).FullName }
        
        if (Test-Path $TargetFolder) {
            $FullFilePath = Join-Path $TargetFolder "svn_merge_report.txt"
            (Genera-TestoReport) | Out-File -FilePath $FullFilePath -Encoding utf8 -Force
            Write-Host "[OK] Report salvato in: $FullFilePath" -ForegroundColor Green
        } else {
            Write-Host "[ERRORE] Percorso non valido." -ForegroundColor Red
        }
    }
}