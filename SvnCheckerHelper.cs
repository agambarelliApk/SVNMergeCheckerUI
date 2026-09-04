using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SVNMergeCheckerUI {
    public class SvnCheckerHelper {
        private readonly ISvnService _svn;

        // Timeout di default (ms) per le chiamate a svn.exe senza timeout esplicito.
        // TODO: rendere configurabile da AppConfig/GUI (vedi TASKS.md).
        private const int DefaultSvnTimeoutMs = 60_000;

        public SvnCheckerHelper(ISvnService svn) => _svn = svn;

        // ----------------------------------------------------------------
        // Entry point principale
        // ----------------------------------------------------------------
        // Aggregato interno prodotto da un'esecuzione per-issue: contiene i dati
        // "grezzi" della singola issue prima della fusione con le altre.
        private sealed record IssueRunResult(
            string Issue,
            Dictionary<int, RevisionInfo> Details,
            List<int> ToProcess,
            Dictionary<int, List<string>> Tree,
            DateTime SearchMinDate);

        public async Task<SvnCheckerResult> RunAsync(
            SvnCheckerParameters p,
            IProgress<string> progress,
            CancellationToken ct = default) {
            var timeoutMs = p.SvnTimeoutSeconds > 0 ? p.SvnTimeoutSeconds * 1000 : DefaultSvnTimeoutMs;

            // 1. Resolve URL repository sorgente
            var repoUrl = await _svn.GetSvnUrlAsync(p.SourceRepository, ct, timeoutMs)
                          ?? throw new InvalidOperationException(
                              $"Impossibile recuperare l'URL dalla sorgente '{p.SourceRepository}'.");

            Dictionary<int, RevisionInfo> details;
            List<int> toProcess;
            Dictionary<int, List<string>> tree;
            DateTime searchMinDate;

            if (p.Issues.Count > 0) {
                // Flusso per-issue: una sola chiamata svn log a monte, poi ciclo per issue.
                progress.Report("[-] Scansione log per identificare le issue...");
                var xml = await RunSvnXmlAsync(new[] { "log", "-l", "500", "--xml", repoUrl }, ct, timeoutMs);

                var perIssueResults = new List<IssueRunResult>();
                foreach (var issue in p.Issues) {
                    ct.ThrowIfCancellationRequested();
                    progress.Report($"\n===== [Issue {issue}] Elaborazione =====");
                    var perIssue = await RunSingleIssueAsync(issue, xml, p, repoUrl, progress, ct, timeoutMs);
                    perIssueResults.Add(perIssue);
                }

                // Fusione risultati per-issue
                (details, toProcess, tree, searchMinDate) = MergeIssueResults(perIssueResults);
            } else {
                // Flusso manuale invariato: single-shot come da comportamento precedente.
                details = new Dictionary<int, RevisionInfo>();
                toProcess = new List<int>();
                await LoadManualRevisionsAsync(p.Revisions, repoUrl, p.MaxNewRevs, details, toProcess, progress, ct, timeoutMs);

                if (toProcess.Count == 0) {
                    progress.Report("[!] Nessuna revisione da elaborare.");
                    return new SvnCheckerResult();
                }

                var earliestUserRevDate = details.Values.Select(r => r.Date)
                                           .DefaultIfEmpty(DateTime.Now).Min();
                searchMinDate = p.SearchMinDateDays > 0
                    ? earliestUserRevDate.AddDays(-p.SearchMinDateDays)
                    : DateTime.MinValue;

                tree = new Dictionary<int, List<string>>();
                var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rev in toProcess) tree[rev] = new List<string>();

                await AnalyzeDependenciesAsync(
                    toProcess, tree, processedFiles, details,
                    repoUrl, searchMinDate, p.MaxNewRevs, progress, ct, timeoutMs);
            }

            if (toProcess.Count == 0) {
                progress.Report("[!] Nessuna revisione da elaborare.");
                return new SvnCheckerResult();
            }

            // Revisioni già mergiate (svn mergeinfo + SkipRevisions)
            progress.Report("\n[-] Confronto con le revisioni già mergiate...");
            var merged = await _svn.GetMergedRevisionsAsync(repoUrl, p.WorkingCopy, ct, timeoutMs);
            var mergedSet = new HashSet<int>(merged);
            foreach (var s in p.SkipRevisions) mergedSet.Add(s);

            // Soglia: massimo numero di revisione tra le revisioni dirette ancora da mergiare.
            var directPendingMax = details.Values
                .Where(info => info.ParentRev == null && !mergedSet.Contains(info.Number))
                .Select(info => (int?)info.Number)
                .DefaultIfEmpty(null)
                .Max();

            foreach (var info in details.Values) {
                info.DisplayState = ComputeDisplayState(info, mergedSet, directPendingMax);

                // Stato calcolato nel contesto di ciascuna issue che referenzia questa revisione:
                // la stessa revisione può essere "diretta" per un'issue e "dipendenza" per un'altra
                // (vedi PerIssueRoles), quindi il simbolo mostrato in sezione 1/3 del report deve
                // poter differire da un'issue all'altra invece di essere sempre lo stesso.
                foreach (var (issueKey, role) in info.PerIssueRoles) {
                    info.PerIssueDisplayStates[issueKey] = ComputeDisplayState(
                        info.Number, role.Direction, role.ParentRevNumber, mergedSet, directPendingMax);
                }
            }

            var revisionStates = details.Values.ToDictionary(info => info.Number, info => info.DisplayState);
            var perIssueRevisionStates = new Dictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>(StringComparer.OrdinalIgnoreCase);
            foreach (var issue in p.Issues) {
                var issueDict = new Dictionary<int, RevisionDisplayState>();
                foreach (var info in details.Values) {
                    if (info.PerIssueDisplayStates.TryGetValue(issue, out var st)) {
                        issueDict[info.Number] = st;
                    }
                }
                perIssueRevisionStates[issue] = issueDict;
            }

            progress.Report($"##MERGED_REVISIONS:{string.Join(",", mergedSet)}");
            progress.Report($"##REVISION_STATES:{string.Join(",", revisionStates.Select(kv => $"{kv.Key}={StateCode(kv.Value)}"))}");

            // Build testo report (stesso formato atteso da ReportParserService)
            var reportText = BuildReportText(p, repoUrl, details, toProcess, tree, searchMinDate);

            if (!string.IsNullOrWhiteSpace(p.OutFile)) {
                try {
                    await File.WriteAllTextAsync(p.OutFile, reportText, Encoding.UTF8, ct);
                    progress.Report("\n\n\n\n" +                                               "===============================================================================================================================================================" +
                        $"\n[OK] Report salvato in: {p.OutFile}");
                } catch {
                    progress.Report("Non è stato possible salvare il file di report./nVerificare l'esistenza e l'accessibilità del percorso ");
                }
            }

            return new SvnCheckerResult {
                Revisions = toProcess.Order().Select(r => details[r]).ToList(),
                DependencyTree = tree,
                MergedRevisions = mergedSet,
                RevisionStates = revisionStates,
                PerIssueRevisionStates = perIssueRevisionStates,
                ReportText = reportText
            };
        }

        // ----------------------------------------------------------------
        // Esecuzione di una singola issue (usa svn log pre-caricato)
        // ----------------------------------------------------------------
        private async Task<IssueRunResult> RunSingleIssueAsync(
            string issue,
            XmlDocument? preFetchedLog,
            SvnCheckerParameters p,
            string repoUrl,
            IProgress<string> progress,
            CancellationToken ct,
            int timeoutMs) {
            var details = new Dictionary<int, RevisionInfo>();
            var toProcess = new List<int>();

            if (preFetchedLog is not null) {
                foreach (XmlElement entry in preFetchedLog.SelectNodes("/log/logentry")!) {
                    ct.ThrowIfCancellationRequested();
                    if (!int.TryParse(entry.GetAttribute("revision"), out var rev)) continue;
                    if (!DateTime.TryParse(entry.SelectSingleNode("date")?.InnerText, out var date)) date = DateTime.MinValue;
                    var auth = entry.SelectSingleNode("author")?.InnerText.Trim() ?? "Unknown";
                    var msg = entry.SelectSingleNode("msg")?.InnerText.Trim() ?? string.Empty;

                    if (!msg.Contains(issue, StringComparison.OrdinalIgnoreCase)) continue;

                    TryAddRevision(rev, date, auth, msg, p.MaxNewRevs, details, toProcess);
                    if (details.ContainsKey(rev)) {
                        details[rev].MatchedIssues.Add(issue);
                        details[rev].issues = string.Join(", ", details[rev].MatchedIssues);
                    }
                }
            }

            if (toProcess.Count == 0) {
                progress.Report($"  [!] Issue '{issue}': nessuna revisione trovata.");
                return new IssueRunResult(issue, details, toProcess,
                    new Dictionary<int, List<string>>(), DateTime.Now);
            }

            var earliestUserRevDate = details.Values.Select(r => r.Date)
                                       .DefaultIfEmpty(DateTime.Now).Min();
            var searchMinDate = p.SearchMinDateDays > 0
                ? earliestUserRevDate.AddDays(-p.SearchMinDateDays)
                : DateTime.MinValue;

            var tree = new Dictionary<int, List<string>>();
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rev in toProcess) tree[rev] = new List<string>();

            await AnalyzeDependenciesAsync(
                toProcess, tree, processedFiles, details,
                repoUrl, searchMinDate, p.MaxNewRevs, progress, ct, timeoutMs);

            // Registra, per ciascuna revisione trovata in questa issue, il ruolo (diretta o
            // dipendente + direzione) che ha nel contesto di questa specifica issue. Necessario
            // perché la stessa revisione può avere ruoli diversi in issue diverse e la fusione
            // successiva (MergeIssueResults) collassa Direction/ParentRev su un unico valore.
            foreach (var (rev, info) in details) {
                info.PerIssueRoles[issue] = new PerIssueRole(info.ParentRev?.Number, info.Direction);
            }

            return new IssueRunResult(issue, details, toProcess, tree, searchMinDate);
        }

        // ----------------------------------------------------------------
        // Fusione dei risultati per-issue in strutture globali unificate
        // ----------------------------------------------------------------
        private static (Dictionary<int, RevisionInfo> details,
                        List<int> toProcess,
                        Dictionary<int, List<string>> tree,
                        DateTime searchMinDate)
            MergeIssueResults(List<IssueRunResult> perIssueResults) {
            var details = new Dictionary<int, RevisionInfo>();
            var tree = new Dictionary<int, List<string>>();
            // Legami "dipendente -> genitore" scoperti durante l'analisi di una specifica issue,
            // da risolvere in un secondo momento (a dizionario globale ormai completo) per
            // individuare i casi in cui la stessa relazione di file coinvolge due issue diverse
            // (es. una revisione dipendenza di file per l'issue A è anche revisione diretta
            // dell'issue B): vedi RegisterCrossIssueLinks.
            var pendingCrossLinks = new List<(int DependentRev, int ParentRevNumber, string SourceIssue)>();

            foreach (var res in perIssueResults) {
                foreach (var (rev, info) in res.Details) {
                    if (info.ParentRev is not null)
                        pendingCrossLinks.Add((rev, info.ParentRev.Number, res.Issue));

                    if (!details.TryGetValue(rev, out var existing)) {
                        details[rev] = info;
                    } else {
                        existing.IsUserCollision = existing.IsUserCollision || info.IsUserCollision;
                        foreach (var mi in info.MatchedIssues) existing.MatchedIssues.Add(mi);
                        existing.issues = string.Join(", ", existing.MatchedIssues);

                        foreach (var f in info.Files) {
                            if (!existing.Files.Contains(f, StringComparer.OrdinalIgnoreCase))
                                existing.Files.Add(f);
                        }

                        // Riporta sull'istanza "canonica" i ruoli per-issue registrati sull'istanza
                        // scartata: senza questo passaggio, le informazioni di ruolo raccolte in
                        // RunSingleIssueAsync per issue diverse dalla prima incontrata andrebbero perse.
                        foreach (var (issueKey, role) in info.PerIssueRoles) {
                            existing.PerIssueRoles[issueKey] = role;
                        }

                        // Se in un'altra issue la revisione è "diretta" (None) prevale sul dipendente
                        // come stato GLOBALE (usato per sezione 2/UI), ma il ruolo per-issue specifico
                        // resta comunque tracciato in PerIssueRoles per la sezione 1/3 del report.
                        if (existing.Direction != DependencyDirection.None &&
                            info.Direction == DependencyDirection.None) {
                            existing.Direction = DependencyDirection.None;
                            existing.ParentRev = null;
                        }
                    }
                }

                foreach (var (key, children) in res.Tree) {
                    if (!tree.TryGetValue(key, out var list)) {
                        list = new List<string>();
                        tree[key] = list;
                    }
                    foreach (var c in children) {
                        if (!list.Contains(c)) list.Add(c);
                    }
                }
            }

            // Ri-collega ParentRev alle istanze "canoniche" presenti in 'details': durante la
            // fusione la prima istanza incontrata per una revisione diventa quella canonica, ma i
            // suoi ParentRev possono ancora puntare a istanze scartate (di un'altra issue) prive
            // delle fusioni successive (MatchedIssues/Files aggiornati). Senza questo passaggio,
            // camminare la catena ParentRev (es. in ResolveOwningIssues) può "perdere" revisioni.
            foreach (var info in details.Values) {
                if (info.ParentRev is not null && details.TryGetValue(info.ParentRev.Number, out var canonicalParent)) {
                    info.ParentRev = canonicalParent;
                }
            }

            RegisterCrossIssueLinks(details, pendingCrossLinks);

            var toProcess = details.Keys.Order().ToList();
            var searchMinDate = perIssueResults
                .Where(r => r.ToProcess.Count > 0)
                .Select(r => r.SearchMinDate)
                .DefaultIfEmpty(DateTime.Now)
                .Min();

            return (details, toProcess, tree, searchMinDate);
        }

        // ----------------------------------------------------------------
        // Segnala i legami "stesso file coinvolto" tra revisioni dirette di issue diverse, anche
        // quando una delle due è stata riclassificata come "diretta" (ParentRev azzerato) durante
        // la fusione dei risultati per-issue. Va eseguita dopo che 'details' contiene già tutte le
        // revisioni di tutte le issue, per non dipendere dall'ordine di elaborazione delle issue.
        // ----------------------------------------------------------------
        private static void RegisterCrossIssueLinks(
            Dictionary<int, RevisionInfo> details,
            List<(int DependentRev, int ParentRevNumber, string SourceIssue)> pendingCrossLinks) {
            foreach (var (dependentRevNum, parentRevNum, sourceIssue) in pendingCrossLinks) {
                if (dependentRevNum == parentRevNum) continue;
                if (!details.TryGetValue(dependentRevNum, out var dependentRev)) continue;
                if (!details.TryGetValue(parentRevNum, out var parentRev)) continue;

                foreach (var otherIssue in dependentRev.MatchedIssues) {
                    if (string.Equals(otherIssue, sourceIssue, StringComparison.OrdinalIgnoreCase)) continue;
                    parentRev.CrossIssueDependencies.Add(otherIssue);
                    dependentRev.CrossIssueDependencies.Add(sourceIssue);
                }
            }
        }

        // ----------------------------------------------------------------
        // Calcolo dello stato di visualizzazione di una revisione
        // ----------------------------------------------------------------
        private static RevisionDisplayState ComputeDisplayState(
            RevisionInfo info, HashSet<int> mergedSet, int? directPendingMax) =>
            ComputeDisplayState(info.Number, info.Direction, info.ParentRev?.Number, mergedSet, directPendingMax);

        // Overload esplicito su Direction/ParentRevNumber, usato sia per lo stato globale
        // (Direction/ParentRev "collassati" da MergeIssueResults) sia per lo stato calcolato nel
        // contesto di una singola issue (a partire da RevisionInfo.PerIssueRoles), dato che la
        // stessa revisione può avere ruoli differenti in issue differenti.
        private static RevisionDisplayState ComputeDisplayState(
            int revisionNumber, DependencyDirection direction, int? parentRevNumber,
            HashSet<int> mergedSet, int? directPendingMax) {
            var isMerged = mergedSet.Contains(revisionNumber);

            if (direction == DependencyDirection.Next) {
                return isMerged
                    ? RevisionDisplayState.DipendenzaSuccessivaMergiata
                    : RevisionDisplayState.DipendenzaSuccessivaDaMergiare;
            }
            if (direction == DependencyDirection.Previous) {
                return isMerged
                    ? RevisionDisplayState.DipendenzaPrecedenteMergiata
                    : RevisionDisplayState.DipendenzaPrecedenteDaMergiare;
            }
            if (isMerged) return RevisionDisplayState.Mergiato;
            if (parentRevNumber == null) return RevisionDisplayState.DaMergiareDiretta;
            if (directPendingMax.HasValue && revisionNumber > directPendingMax.Value)
                return RevisionDisplayState.DaMergiareIndirettaAlta;
            return RevisionDisplayState.DaMergiareIndiretta;
        }

        // ----------------------------------------------------------------
        // Sezione 2-b: caricamento revisioni manuali
        // ----------------------------------------------------------------
        private async Task LoadManualRevisionsAsync(
            IReadOnlyList<int> revisions,
            string repoUrl,
            int maxRevs,
            Dictionary<int, RevisionInfo> details,
            List<int> toProcess,
            IProgress<string> progress,
            CancellationToken ct,
            int timeoutMs) {
            progress.Report("[-] Recupero dettagli revisioni manuali...");
            foreach (var rev in revisions) {
                ct.ThrowIfCancellationRequested();
                var xml = await RunSvnXmlAsync(new[] { "log", "-c", rev.ToString(), "--xml", repoUrl }, ct, timeoutMs);
                if (xml is null) continue;

                var entry = xml.SelectSingleNode("/log/logentry") as XmlElement;
                if (entry is null) continue;

                if (!DateTime.TryParse(entry.SelectSingleNode("date")?.InnerText, out var date)) date = DateTime.MinValue;
                var auth = entry.SelectSingleNode("author")?.InnerText.Trim() ?? "Unknown";
                var msg = entry.SelectSingleNode("msg")?.InnerText.Trim() ?? string.Empty;

                TryAddRevision(rev, date, auth, msg, maxRevs, details, toProcess);
            }
        }

        // ----------------------------------------------------------------
        // Sezione 3: analisi ciclica dipendenze
        // ----------------------------------------------------------------
        private static readonly Regex HunkHeaderRegex = new(
            @"@@\s+-(?<oldStart>\d+)(?:,(?<oldCount>\d+))?\s+\+(?<newStart>\d+)(?:,(?<newCount>\d+))?\s+@@",
            RegexOptions.Compiled);

        public static List<(int Start, int End)> ParseDiffHunkRanges(string? diffText) {
            var ranges = new List<(int Start, int End)>();
            if (string.IsNullOrWhiteSpace(diffText)) return ranges;

            foreach (var line in diffText.Split('\n')) {
                var m = HunkHeaderRegex.Match(line);
                if (!m.Success) continue;

                var oldStart = int.Parse(m.Groups["oldStart"].Value);
                var oldCount = m.Groups["oldCount"].Success ? int.Parse(m.Groups["oldCount"].Value) : 1;
                var newStart = int.Parse(m.Groups["newStart"].Value);
                var newCount = m.Groups["newCount"].Success ? int.Parse(m.Groups["newCount"].Value) : 1;

                var start = Math.Min(oldStart, newStart);
                var end = Math.Max(oldStart + Math.Max(0, oldCount - 1), newStart + Math.Max(0, newCount - 1));
                ranges.Add((start, end));
            }

            return ranges;
        }

        public static bool CheckRangeCollision(
            IReadOnlyList<(int Start, int End)> rangesA,
            IReadOnlyList<(int Start, int End)> rangesB,
            int margin = 3) {
            // Se uno dei due non ha hunk parsabili (es. diff binario o modifica file-level), fallback conservativo su collisione
            if (rangesA.Count == 0 || rangesB.Count == 0)
                return true;

            foreach (var (startA, endA) in rangesA) {
                foreach (var (startB, endB) in rangesB) {
                    if (startA <= endB + margin && startB <= endA + margin)
                        return true;
                }
            }

            return false;
        }

        private async Task AnalyzeDependenciesAsync(
            List<int> toProcess,
            Dictionary<int, List<string>> tree,
            HashSet<string> processedFiles,
            Dictionary<int, RevisionInfo> details,
            string repoUrl,
            DateTime searchMinDate,
            int maxRevs,
            IProgress<string> progress,
            CancellationToken ct,
            int timeoutMs) {
            var directRevs = new HashSet<int>(toProcess);
            var diffSummaryRe = new Regex(@"^[ADMR\s]{1,3}\s+(.+)$", RegexOptions.Compiled);
            progress.Report($"  [DEBUG] repoUrl usato per diff: '{repoUrl}'");

            var diffRangeCache = new Dictionary<(int Rev, string FileUrl), List<(int Start, int End)>>();
            var fileLogCache = new Dictionary<string, XmlDocument?>(StringComparer.OrdinalIgnoreCase);
            var processedRevFiles = new HashSet<(int Rev, string File)>();

            async Task<List<(int Start, int End)>> GetHunkRangesAsync(int targetRev, string fileUrl) {
                var key = (targetRev, fileUrl);
                if (diffRangeCache.TryGetValue(key, out var cached))
                    return cached;

                var diffText = await RunSvnRawAsync(
                    new[] { "diff", "-c", targetRev.ToString(), fileUrl }, ct, timeoutMs);
                var ranges = ParseDiffHunkRanges(diffText);
                diffRangeCache[key] = ranges;
                return ranges;
            }

            async Task<XmlDocument?> GetFileLogXmlAsync(string targetFileUrl) {
                if (fileLogCache.TryGetValue(targetFileUrl, out var cachedDoc))
                    return cachedDoc;

                var doc = await RunSvnXmlAsync(
                    new[] { "log", "--xml", "-r", "1:HEAD", targetFileUrl }, ct, timeoutMs);
                fileLogCache[targetFileUrl] = doc;
                return doc;
            }

            for (var idx = 0; idx < toProcess.Count; idx++) {
                ct.ThrowIfCancellationRequested();
                var rev = toProcess[idx];
                progress.Report($"[-] Analisi file modificati nella revisione {rev}...");

                var diffRaw = await RunSvnRawAsync(
                    new[] { "diff", "--summarize", "-c", rev.ToString(), repoUrl }, ct, timeoutMs);

                var filesInRev = new List<string>();
                foreach (var line in (diffRaw ?? string.Empty).Split('\n')) {
                    var m = diffSummaryRe.Match(line);
                    if (!m.Success) continue;
                    var rawPath = m.Groups[1].Value.Trim();
                    var relativePath = rawPath.StartsWith(repoUrl, StringComparison.OrdinalIgnoreCase)
                        ? rawPath.Substring(repoUrl.Length).TrimStart('/')
                        : rawPath;
                    progress.Report($"  [DEB] {rev} rawPath='{rawPath}' -> relative='{relativePath}'");
                    filesInRev.Add(relativePath);
                }

                if (filesInRev.Count == 0)
                    progress.Report($"  [DEB] {rev} diff vuoto o non parsato. Prima riga raw: '{(diffRaw ?? string.Empty).Split('\n').FirstOrDefault()}'");
                else
                    progress.Report($"  [DEB] Trovati {filesInRev.Count} file per {rev}");

                details[rev].Files.AddRange(filesInRev);

                foreach (var file in filesInRev) {
                    if (!processedRevFiles.Add((rev, file))) continue;

                    var fileUrl = file.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? file
                        : $"{repoUrl.TrimEnd('/', '\\')}/{file.TrimStart('/', '\\')}";

                    var fileLogXml = await GetFileLogXmlAsync(fileUrl);
                    if (fileLogXml is null) continue;

                    var revHunkRanges = await GetHunkRangesAsync(rev, fileUrl);

                    var truncated = false;
                    foreach (XmlElement entry in fileLogXml.SelectNodes("/log/logentry")!) {
                        if (!int.TryParse(entry.GetAttribute("revision"), out var fRev)) continue;
                        if (fRev >= rev) continue; // Solo revisioni precedenti
                        if (!DateTime.TryParse(entry.SelectSingleNode("date")?.InnerText, out var fDate)) fDate = DateTime.MinValue;
                        if (fDate < searchMinDate) continue;

                        var fRevHunkRanges = await GetHunkRangesAsync(fRev, fileUrl);
                        if (!CheckRangeCollision(revHunkRanges, fRevHunkRanges)) {
                            progress.Report($"  [NESSUNA COLLISIONE] '{file}' rev {fRev} non collide con {rev} (righe disgiunte)");
                            continue;
                        }

                        var fAuth = entry.SelectSingleNode("author")?.InnerText.Trim() ?? "Unknown";
                        var fMsg = entry.SelectSingleNode("msg")?.InnerText.Trim() ?? string.Empty;

                        progress.Report($"  [COLLISIONE TROVATA] '{file}' richiede {fRev} (Autore: {fAuth}, direzione: Previous)");

                        if (!tree.ContainsKey(fRev)) tree[fRev] = new List<string>();
                        if (!tree.ContainsKey(rev)) tree[rev] = new List<string>();
                        var depText = $"revisione precedente derivata da {rev} da file in {fRev}";
                        if (!tree[rev].Contains(depText))
                            tree[rev].Add(depText);

                        if (directRevs.Contains(fRev) && details.TryGetValue(fRev, out var fRevInfo)) {
                            fRevInfo.IsUserCollision = true;
                        }

                        if (toProcess.Contains(fRev))
                            continue;

                        if (toProcess.Count >= maxRevs) {
                            if (!truncated) {
                                progress.Report($"  [WARN] Limite massimo di revisioni ({maxRevs}) raggiunto.");
                                truncated = true;
                            }
                            continue;
                        }

                        TryAddRevision(fRev, fDate, fAuth, fMsg, maxRevs, details, toProcess, parent: details[rev], direction: DependencyDirection.Previous);
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // Sezione 4: costruzione testo report (stesso formato PS)
        // ----------------------------------------------------------------
        private static string BuildReportText(
            SvnCheckerParameters p,
            string repoUrl,
            Dictionary<int, RevisionInfo> details,
            List<int> toProcess,
            Dictionary<int, List<string>> tree,
            DateTime searchMinDate) {
            var sb = new StringBuilder();
            sb.AppendLine("====================================================");
            sb.AppendLine(" REPORT DI CONSISTENZA FINALE PER MERGE");
            sb.AppendLine("====================================================");
            sb.AppendLine($"Working Copy (Destinazione): {p.WorkingCopy}");
            sb.AppendLine($"Source Repo (Sorgente)     : {p.SourceRepository}");
            sb.AppendLine($"URL Repository SVN         : {repoUrl}");
            sb.AppendLine($"Flusso Utilizzato          : {(p.Issues.Count > 0 ? "Ricerca per Issue" : "Inserimento Manuale Revisions")}");
            if (p.Issues.Count > 0)
                sb.AppendLine($"Issue Inserite             : {string.Join(", ", p.Issues)}");
            sb.AppendLine($"Revisioni Rilevate         : {string.Join(", ", toProcess.Order())}");
            sb.AppendLine($"Data Limite Ricerca        : {searchMinDate:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine("====================================================\n");

            // Costruisce il dizionario issue?revisioni (usato sia in sezione 1 che 3)
            var issueToRevisions = BuildIssueToRevisionsMap(p.Issues, toProcess, details);
            // Orfane per issue: una revisione è orfana per l'issue X se X non la matcha direttamente
            // (MatchedIssues) ma appartiene comunque al grafo di dipendenze scoperto durante
            // l'elaborazione di X (PerIssueRoles contiene X). Non si può usare un unico insieme
            // "globale" di orfane (MatchedIssues.Count == 0), perché la stessa revisione può essere
            // diretta per un'issue e dipendenza per un'altra nella stessa esecuzione multi-issue.
            var orphansByIssue = BuildOrphansByIssueMap(p.Issues, toProcess, details);
            var assignedOrphans = new HashSet<int>(orphansByIssue.Values.SelectMany(x => x));
            var unresolvedOrphans = toProcess
                .Where(r => details[r].MatchedIssues.Count == 0 && !assignedOrphans.Contains(r))
                .Order().ToList();

            // Sezione 1: dinamica in base alla presenza di Issue
            if (p.Issues.Count > 0) {
                sb.Append("1. REVISIONI RAGGRUPPATE PER ISSUE:");

                foreach (var issue in p.Issues) {
                    var hasDirect = issueToRevisions.ContainsKey(issue);
                    var hasOrphans = orphansByIssue.TryGetValue(issue, out var issueOrphans) && issueOrphans.Count > 0;
                    if (!hasDirect && !hasOrphans) continue;

                    sb.AppendLine($"\n=== [{issue}] ===");
                    if (hasDirect) {
                        foreach (var rev in issueToRevisions[issue].Order()) {
                            var d = details[rev];
                            var desc = d.Message.Replace("\n", " ").Replace("\r", "");
                            if (desc.Length > 70) desc = desc[..67] + "...";
                            sb.AppendLine($"     => {MergeStateLabel(StateForIssue(d, issue))} {rev} del {d.Date:dd/MM/yyyy HH:mm} [{d.Author}] : {desc}{CrossIssueNote(d)}");
                        }
                    }

                    if (hasOrphans) {
                        sb.AppendLine("  --- Revisioni senza issue diretta ---");
                        foreach (var rev in issueOrphans!.Order()) {
                            var d = details[rev];
                            var desc = d.Message.Replace("\n", " ").Replace("\r", "");
                            if (desc.Length > 70) desc = desc[..67] + "...";
                            sb.AppendLine($"   => {MergeStateLabel(StateForIssue(d, issue))} {rev} del {d.Date:dd/MM/yyyy HH:mm} [{d.Author}] : {desc}{CrossIssueNote(d)}");
                        }
                    }
                }

                if (unresolvedOrphans.Count > 0) {
                    sb.AppendLine("\n=== [Revisioni di dipendenza senza issue diretta] ===");
                    foreach (var rev in unresolvedOrphans) {
                        var d = details[rev];
                        var desc = d.Message.Replace("\n", " ").Replace("\r", "");
                        if (desc.Length > 70) desc = desc[..67] + "...";
                        sb.AppendLine($"    => {MergeStateLabel(d.DisplayState)} {rev} del {d.Date:dd/MM/yyyy HH:mm} [{d.Author}] : {desc}{CrossIssueNote(d)}");
                    }
                }
            } else {
                sb.Append("1. ELENCO COMPLETO REVISIONI ORDINATO (Crescente):");
                sb.AppendLine("\n  [Elaborazione senza Issue]");
                foreach (var rev in toProcess.Order()) {
                    var d = details[rev];
                    var desc = d.Message.Replace("\n", " ").Replace("\r", "");
                    if (desc.Length > 70) desc = desc[..67] + "...";
                    sb.AppendLine($"    => {MergeStateLabel(d.DisplayState)} {rev} del {d.Date:dd/MM/yyyy HH:mm} [{d.Author}] : {desc}");
                }
            }
            sb.AppendLine();

            if (p.Issues.Count > 0) {                
                sb.Append("2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE RAGRUPPATE PER ISSUE:");
                // Sezione 2: dinamica in base alla presenza di Issue (usa dizionario issue?revisioni)
                foreach (var issue in p.Issues) {
                    var hasDirect = issueToRevisions.ContainsKey(issue);
                    var hasOrphans = orphansByIssue.TryGetValue(issue, out var issueOrphans) && issueOrphans.Count > 0;
                    if (!hasDirect && !hasOrphans) continue;

                    sb.AppendLine($"\n=== [{issue}] ===");
                    var issueRevs = new List<int>();
                    if (hasDirect) issueRevs.AddRange(issueToRevisions[issue]);
                    if (hasOrphans) issueRevs.AddRange(issueOrphans!);

                    foreach (var rev in issueRevs.Distinct().Order()) {
                        sb.AppendLine($"  {rev}");
                        if (tree.TryGetValue(rev, out var children) && children.Count > 0)
                            foreach (var c in children) sb.AppendLine($"    > {c}");
                        else
                            sb.AppendLine("    > [Nessuna dipendenza trovata]");
                    }
                }

            } else {
                sb.Append("2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE:");
                sb.AppendLine("\n[Elaborazione senza Issue]");
                foreach (var (key, children) in tree) {
                    sb.AppendLine($"  {key}");
                    if (children.Count > 0)
                        foreach (var c in children) sb.AppendLine($"    > {c}");
                    else
                        sb.AppendLine("    > [Nessuna dipendenza trovata]");
                }
            }
            sb.AppendLine();

            // Sezione 3: dinamica in base alla presenza di Issue (usa stesso dizionario)
            sb.AppendLine("3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE:");

            if (p.Issues.Count > 0) {
                foreach (var issue in p.Issues) {
                    var hasDirect = issueToRevisions.ContainsKey(issue);
                    var hasOrphans = orphansByIssue.TryGetValue(issue, out var issueOrphans) && issueOrphans.Count > 0;
                    if (!hasDirect && !hasOrphans) continue;

                    sb.AppendLine($"=== [{issue}] ===");
                    if (hasDirect) {
                        foreach (var rev in issueToRevisions[issue].Order()) {
                            var d = details[rev];
                            sb.AppendLine($"> REVISIONE {rev}:{CrossIssueNote(d)}");
                            var files = d.Files;
                            if (files.Count > 0)
                                foreach (var f in files) sb.AppendLine($"  - {f}");
                            else
                                sb.AppendLine("  - Nessun file rilevato o operazione di sola proprietà.");
                            sb.AppendLine("----------------------------------------------------");
                        }
                    }

                    if (hasOrphans) {
                        sb.AppendLine("--- Revisioni senza issue diretta ---");
                        foreach (var rev in issueOrphans!.Order()) {
                            var d = details[rev];
                            sb.AppendLine($"> REVISIONE {rev}:{CrossIssueNote(d)}");
                            var files = d.Files;
                            if (files.Count > 0)
                                foreach (var f in files) sb.AppendLine($"  - {f}");
                            else
                                sb.AppendLine("  - Nessun file rilevato o operazione di sola proprietà.");
                            sb.AppendLine("----------------------------------------------------");
                        }
                    }
                }

                if (unresolvedOrphans.Count > 0) {
                    sb.AppendLine("\n=== [Revisioni al di fuori delle issue inserite dall'utente] ===");
                    foreach (var rev in unresolvedOrphans) {
                        sb.AppendLine($"> REVISIONE {rev}:");
                        var files = details[rev].Files;
                        if (files.Count > 0)
                            foreach (var f in files) sb.AppendLine($"  - {f}");
                        else
                            sb.AppendLine("  - Nessun file rilevato o operazione di sola proprietà.");
                        sb.AppendLine("----------------------------------------------------");
                    }
                }
            } else {
                foreach (var rev in toProcess.Order()) {
                    sb.AppendLine($"> REVISIONE {rev}:");
                    var files = details[rev].Files;
                    if (files.Count > 0)
                        foreach (var f in files) sb.AppendLine($"  - {f}");
                    else
                        sb.AppendLine("  - Nessun file rilevato o operazione di sola proprietà.");
                    sb.AppendLine("----------------------------------------------------");
                }
            }
            sb.AppendLine();

            var toMerge = toProcess.Order().Where(r => details[r].DisplayState != RevisionDisplayState.Mergiato);
            //sb.AppendLine("[INFO] Comando consigliato per eseguire il merge sulla Working Copy (Solo da mergiare):");
            //sb.AppendLine($"svn merge -c {string.Join(",", toMerge)} {repoUrl} \"{p.WorkingCopy}\"");

            return sb.ToString();
        }

        // ----------------------------------------------------------------
        // Helpers report
        // ----------------------------------------------------------------
        private static string CrossIssueNote(RevisionInfo d) =>
            d.CrossIssueDependencies.Count > 0
                ? $" (correlata anche a: {string.Join(", ", d.CrossIssueDependencies.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))})"
                : string.Empty;

        // Stato di visualizzazione da usare per una revisione nel contesto di una specifica
        // issue: se è stato calcolato un ruolo per-issue (PerIssueDisplayStates) lo si usa,
        // altrimenti si ricade sullo stato "globale" (DisplayState) calcolato da MergeIssueResults.
        private static RevisionDisplayState StateForIssue(RevisionInfo d, string issue) =>
            d.PerIssueDisplayStates.TryGetValue(issue, out var state) ? state : d.DisplayState;

        private static string MergeStateLabel(RevisionDisplayState state) => state switch {
            RevisionDisplayState.Mergiato => "[\u2714]",
            RevisionDisplayState.DaMergiareDiretta => "[\u25B6]",
            RevisionDisplayState.DipendenzaUtentePrecedenteDaMergiare => "[\u25B6]",
            RevisionDisplayState.DaMergiareIndiretta => "[\u2757]",
            RevisionDisplayState.DaMergiareIndirettaAlta => "[\u274C]",
            RevisionDisplayState.DipendenzaSuccessivaMergiata => "[\u274C]",
            RevisionDisplayState.DipendenzaSuccessivaDaMergiare => "[\u2757]",
            RevisionDisplayState.DipendenzaPrecedenteMergiata => "[\u25B6]",
            RevisionDisplayState.DipendenzaPrecedenteDaMergiare => "[\u2757]",
            RevisionDisplayState.DipendenzaDirettaAltraIssuePrecedenteDaMergiare => "[\u2757]",
            _ => "[?]"
        };

        private static string StateCode(RevisionDisplayState state) => state switch {
            RevisionDisplayState.Mergiato => "M",
            RevisionDisplayState.DaMergiareDiretta => "D",
            RevisionDisplayState.DipendenzaUtentePrecedenteDaMergiare => "UD",
            RevisionDisplayState.DaMergiareIndiretta => "I",
            RevisionDisplayState.DaMergiareIndirettaAlta => "X",
            RevisionDisplayState.DipendenzaSuccessivaMergiata => "SM",
            RevisionDisplayState.DipendenzaSuccessivaDaMergiare => "SD",
            RevisionDisplayState.DipendenzaPrecedenteMergiata => "PM",
            RevisionDisplayState.DipendenzaPrecedenteDaMergiare => "PD",
            RevisionDisplayState.DipendenzaDirettaAltraIssuePrecedenteDaMergiare => "PDD",
            _ => "?"
        };

        private static Dictionary<string, List<int>> BuildIssueToRevisionsMap(
            IReadOnlyList<string> issues,
            List<int> toProcess,
            Dictionary<int, RevisionInfo> details) {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var rev in toProcess) {
                var info = details[rev];
                if (info.MatchedIssues.Count == 0) continue;
                foreach (var issue in info.MatchedIssues) {
                    if (!map.ContainsKey(issue)) map[issue] = new List<int>();
                    if (!map[issue].Contains(rev)) map[issue].Add(rev); // Fix 2: evita duplicati
                }
            }
            return map;
        }

        // Per ciascuna issue X, una revisione è "orfana" (senza issue diretta) se X non la matcha
        // direttamente (MatchedIssues) ma la revisione appartiene comunque al grafo di dipendenze
        // scoperto durante l'elaborazione di X (PerIssueRoles contiene la chiave X, popolata in
        // RunSingleIssueAsync per ogni revisione trovata analizzando quella specifica issue).
        // Non si usa MatchedIssues.Count == 0 come pre-filtro globale perché la stessa revisione può
        // essere diretta per un'issue e dipendenza per un'altra nella stessa esecuzione multi-issue.
        private static Dictionary<string, List<int>> BuildOrphansByIssueMap(
            IReadOnlyList<string> issues,
            List<int> toProcess,
            Dictionary<int, RevisionInfo> details) {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var issue in issues) {
                foreach (var rev in toProcess) {
                    var info = details[rev];
                    if (info.MatchedIssues.Contains(issue)) continue;
                    if (!info.PerIssueRoles.ContainsKey(issue)) continue;

                    if (!map.TryGetValue(issue, out var list)) {
                        list = new List<int>();
                        map[issue] = list;
                    }
                    if (!list.Contains(rev)) list.Add(rev);
                }
            }
            return map;
        }

        // ----------------------------------------------------------------
        // SVN helpers privati
        // ----------------------------------------------------------------
        private static void TryAddRevision(
            int rev, DateTime date, string auth, string msg,
            int maxRevs,
            Dictionary<int, RevisionInfo> details,
            List<int> toProcess,
            RevisionInfo? parent = null,
            DependencyDirection direction = DependencyDirection.None) {
            if (toProcess.Count >= maxRevs || details.ContainsKey(rev)) return;
            details[rev] = new RevisionInfo { Number = rev, Date = date, Author = auth, Message = msg, ParentRev = parent, Direction = direction };
            toProcess.Add(rev);
        }

        private static async Task<XmlDocument?> RunSvnXmlAsync(string[] args, CancellationToken ct, int timeoutMs = DefaultSvnTimeoutMs) {
            var raw = await RunSvnRawAsync(args, ct, timeoutMs);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try {
                var doc = new XmlDocument();
                doc.LoadXml(raw);
                return doc;
            } catch { return null; }
        }

        private static async Task<string?> RunSvnRawAsync(string[] args, CancellationToken ct, int timeoutMs = DefaultSvnTimeoutMs) {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "svn",
                string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))) {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;

            var outputTask = proc.StandardOutput.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            try {
                await proc.WaitForExitAsync(timeoutCts.Token);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignored */ }
                throw;
            } catch (OperationCanceledException) {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignored */ }
                throw new TimeoutException($"Il comando 'svn {string.Join(" ", args)}' non ha risposto entro {timeoutMs} ms.");
            }

            var output = await outputTask;
            return proc.ExitCode == 0 ? output : null;
        }
    }
}
