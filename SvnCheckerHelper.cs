using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SVNMergeCheckerUI {
    public class SvnCheckerHelper {
        private readonly ISvnService _svn;

        public SvnCheckerHelper(ISvnService svn) => _svn = svn;

        // ----------------------------------------------------------------
        // Entry point principale
        // ----------------------------------------------------------------
        public async Task<SvnCheckerResult> RunAsync(
            SvnCheckerParameters p,
            IProgress<string> progress,
            CancellationToken ct = default) {
            // 1. Resolve URL repository sorgente
            var repoUrl = await _svn.GetSvnUrlAsync(p.SourceRepository, ct)
                          ?? throw new InvalidOperationException(
                              $"Impossibile recuperare l'URL dalla sorgente '{p.SourceRepository}'.");

            // 2. Recupero revisioni iniziali
            var details = new Dictionary<int, RevisionInfo>();
            var toProcess = new List<int>();

            if (p.Issues.Count > 0)
                await FindRevisionsByIssuesAsync(p.Issues, repoUrl, p.MaxNewRevs, details, toProcess, progress, ct);
            else
                await LoadManualRevisionsAsync(p.Revisions, repoUrl, p.MaxNewRevs, details, toProcess, progress, ct);

            if (toProcess.Count == 0) {
                progress.Report("[!] Nessuna revisione da elaborare.");
                return new SvnCheckerResult();
            }

            var searchMinDate = details.Values.Select(r => r.Date)
                                       .DefaultIfEmpty(DateTime.Now.AddYears(-1)).Min();

            // 3. Analisi ciclica dipendenze
            var tree = new Dictionary<int, List<string>>();
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rev in toProcess) tree[rev] = new List<string>();

            await AnalyzeDependenciesAsync(
                toProcess, tree, processedFiles, details,
                repoUrl, searchMinDate, p.MaxNewRevs, progress, ct);

            // 4. Revisioni già mergiate (svn mergeinfo + SkipRevisions)
            progress.Report("\n[-] Confronto con le revisioni già mergiate...");
            var merged = await _svn.GetMergedRevisionsAsync(repoUrl, p.WorkingCopy, ct);
            var mergedSet = new HashSet<int>(merged);
            foreach (var s in p.SkipRevisions) mergedSet.Add(s);

            // Soglia: massimo numero di revisione tra le revisioni dirette ancora da mergiare.
            // Le dipendenze indirette con numero superiore a questa soglia sono evidenziate come "Alta".
            var directPendingMax = details.Values
                .Where(info => info.ParentRev == null && !mergedSet.Contains(info.Number))
                .Select(info => (int?)info.Number)
                .DefaultIfEmpty(null)
                .Max();

            foreach (var info in details.Values) {
                if (mergedSet.Contains(info.Number)) {
                    info.DisplayState = RevisionDisplayState.Mergiato;
                } else if (info.ParentRev == null) {
                    info.DisplayState = RevisionDisplayState.DaMergiareDiretta;
                } else if (directPendingMax.HasValue && info.Number > directPendingMax.Value) {
                    info.DisplayState = RevisionDisplayState.DaMergiareIndirettaAlta;
                } else {
                    info.DisplayState = RevisionDisplayState.DaMergiareIndiretta;
                }
            }

            var revisionStates = details.Values.ToDictionary(info => info.Number, info => info.DisplayState);

            // Emette il marker noto alla GUI per popolare _mergedRevisions
            progress.Report($"##MERGED_REVISIONS:{string.Join(",", mergedSet)}");
            // Emette il marker con lo stato a 4 valori di ogni revisione (M=Mergiato, D=Diretta, I=Indiretta, X=IndirettaAlta)
            progress.Report($"##REVISION_STATES:{string.Join(",", revisionStates.Select(kv => $"{kv.Key}={StateCode(kv.Value)}"))}");

            // 5. Build testo report (stesso formato atteso da ReportParserService)
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
                ReportText = reportText
            };
        }

        // ----------------------------------------------------------------
        // Sezione 2-a: scansione log per Issue
        // ----------------------------------------------------------------
        private async Task FindRevisionsByIssuesAsync(
            IReadOnlyList<string> issues,
            string repoUrl,
            int maxRevs,
            Dictionary<int, RevisionInfo> details,
            List<int> toProcess,
            IProgress<string> progress,
            CancellationToken ct) {
            progress.Report("[-] Scansione log per identificare le issue...");
            var xml = await RunSvnXmlAsync(new[] { "log", "-l", "500", "--xml", repoUrl }, ct);
            if (xml is null) return;

            foreach (XmlElement entry in xml.SelectNodes("/log/logentry")!) {
                ct.ThrowIfCancellationRequested();
                if (!int.TryParse(entry.GetAttribute("revision"), out var rev)) continue;
                if (!DateTime.TryParse(entry.SelectSingleNode("date")?.InnerText, out var date)) date = DateTime.MinValue;
                var auth = entry.SelectSingleNode("author")?.InnerText.Trim() ?? "Unknown";
                var msg = entry.SelectSingleNode("msg")?.InnerText.Trim() ?? string.Empty;

                foreach (var issue in issues) {
                    if (msg.Contains(issue, StringComparison.OrdinalIgnoreCase)) {
                        TryAddRevision(rev, date, auth, msg, maxRevs, details, toProcess);
                        if (details.ContainsKey(rev)) {
                            details[rev].MatchedIssues.Add(issue);
                            details[rev].issues = string.Join(", ", details[rev].MatchedIssues);
                        }
                    }
                }
            }
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
            CancellationToken ct) {
            progress.Report("[-] Recupero dettagli revisioni manuali...");
            foreach (var rev in revisions) {
                ct.ThrowIfCancellationRequested();
                var xml = await RunSvnXmlAsync(new[] { "log", "-c", rev.ToString(), "--xml", repoUrl }, ct);
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
        private async Task AnalyzeDependenciesAsync(
            List<int> toProcess,
            Dictionary<int, List<string>> tree,
            HashSet<string> processedFiles,
            Dictionary<int, RevisionInfo> details,
            string repoUrl,
            DateTime searchMinDate,
            int maxRevs,
            IProgress<string> progress,
            CancellationToken ct) {
            var diffSummaryRe = new Regex(@"^[ADMR]\s+(.+)$", RegexOptions.Compiled);
            progress.Report($"  [DEBUG] repoUrl usato per diff: '{repoUrl}'");

            for (var idx = 0; idx < toProcess.Count; idx++) {
                ct.ThrowIfCancellationRequested();
                var rev = toProcess[idx];
                progress.Report($"[-] Analisi file modificati nella revisione {rev}...");

                var diffRaw = await RunSvnRawAsync(
                    new[] { "diff", "--summarize", "-c", rev.ToString(), repoUrl }, ct);

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
                    if (!processedFiles.Add(file)) continue;

                    var fileUrl = file.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? file
                        : $"{repoUrl.TrimEnd('/')}/{file.TrimStart('/')}";

                    var fileLogXml = await RunSvnXmlAsync(
                        new[] { "log", "--xml", $"{fileUrl}@{rev}" }, ct);
                    if (fileLogXml is null) continue;

                    foreach (XmlElement entry in fileLogXml.SelectNodes("/log/logentry")!) {
                        if (!int.TryParse(entry.GetAttribute("revision"), out var fRev)) continue;
                        if (!DateTime.TryParse(entry.SelectSingleNode("date")?.InnerText, out var fDate)) fDate = DateTime.MinValue;
                        if (fDate < searchMinDate) continue;
                        if (toProcess.Contains(fRev)) continue;

                        var fAuth = entry.SelectSingleNode("author")?.InnerText.Trim() ?? "Unknown";
                        var fMsg = entry.SelectSingleNode("msg")?.InnerText.Trim() ?? string.Empty;

                        progress.Report($"  [DIPENDENZA TROVATA] '{file}' richiede {fRev} (Autore: {fAuth})");

                        if (toProcess.Count >= maxRevs) {
                            progress.Report($"  [WARN] Limite massimo di revisioni ({maxRevs}) raggiunto.");
                            continue;
                        }

                        TryAddRevision(fRev, fDate, fAuth, fMsg, maxRevs, details, toProcess, parent: details[rev]);
                        if (!tree.ContainsKey(fRev)) tree[fRev] = new List<string>();
                        if (!tree.ContainsKey(rev)) tree[rev] = new List<string>();
                        tree[rev].Add($"revisione derivata da {rev} da file in {fRev}");
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
            var orphanRevisions = toProcess.Where(r => details[r].MatchedIssues.Count == 0).Order().ToList();

            // Sezione 1: dinamica in base alla presenza di Issue
            if (p.Issues.Count > 0) {
                sb.Append("1. REVISIONI RAGGRUPPATE PER ISSUE:");

                foreach (var issue in p.Issues) {
                    if (!issueToRevisions.ContainsKey(issue)) continue; // Fix 1: header solo se ci sono revisioni

                    sb.AppendLine($"\n  [{issue}]");
                    foreach (var rev in issueToRevisions[issue].Order()) {
                        var d = details[rev];
                        var desc = d.Message.Replace("\n", " ").Replace("\r", "");
                        if (desc.Length > 70) desc = desc[..67] + "...";
                        sb.AppendLine($"     => {rev} del {d.Date:dd/MM/yyyy HH:mm} [{d.Author}] : {desc} {MergeStateLabel(d.DisplayState)}");
                    }
                }

                if (orphanRevisions.Count > 0) {
                    sb.AppendLine("\n  [Revisioni di dipendenza senza issue diretta]");
                    foreach (var rev in orphanRevisions) {
                        var d = details[rev];
                        var desc = d.Message.Replace("\n", " ").Replace("\r", "");
                        if (desc.Length > 70) desc = desc[..67] + "...";
                        sb.AppendLine($"    => {rev} del {d.Date:dd/MM/yyyy HH:mm} [{d.Author}] : {desc} {MergeStateLabel(d.DisplayState)}");
                    }
                }
            } else {
                sb.Append("1. ELENCO COMPLETO REVISIONI ORDINATO (Crescente):");
                sb.AppendLine("\n  [Elaborazione senza Issue]");
                foreach (var rev in toProcess.Order()) {
                    var d = details[rev];
                    var desc = d.Message.Replace("\n", " ").Replace("\r", "");
                    if (desc.Length > 70) desc = desc[..67] + "...";
                    sb.AppendLine($"    => {rev} del {d.Date:dd/MM/yyyy HH:mm} [{d.Author}] : {desc} {MergeStateLabel(d.DisplayState)}");
                }
            }
            sb.AppendLine();

            if (p.Issues.Count > 0) {                
                sb.Append("2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE RAGRUPPATE PER ISSUE:");
                // Sezione 2: dinamica in base alla presenza di Issue (usa dizionario issue?revisioni)
                foreach (var issue in p.Issues) {
                    if (!issueToRevisions.ContainsKey(issue)) continue; // Fix 1: header solo se ci sono revisioni
                    sb.AppendLine($"\n[{issue}]");
                    foreach (var rev in issueToRevisions[issue].Order()) {
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
                bool isFirtsIssue = true;
                foreach (var issue in p.Issues) {
                    if (!issueToRevisions.ContainsKey(issue)) continue; // Fix 1: header solo se ci sono revisioni
                    
                    sb.AppendLine($"[{issue}]");
                    foreach (var rev in issueToRevisions[issue].Order()) {
                        sb.AppendLine($"> REVISIONE {rev}:");
                        var files = details[rev].Files;
                        if (files.Count > 0)
                            foreach (var f in files) sb.AppendLine($"  - {f}");
                        else
                            sb.AppendLine("  - Nessun file rilevato o operazione di sola proprietà.");
                        sb.AppendLine("----------------------------------------------------");
                    }
                }

                if (orphanRevisions.Count > 0) {
                    sb.AppendLine("\n[Revisioni al di fuori delle issue inserite dall'utente]");
                    foreach (var rev in orphanRevisions) {
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
        private static string MergeStateLabel(RevisionDisplayState state) => state switch {
            RevisionDisplayState.Mergiato => "[\u2713]",
            RevisionDisplayState.DaMergiareDiretta => "[>]",
            RevisionDisplayState.DaMergiareIndiretta => "[!]",
            RevisionDisplayState.DaMergiareIndirettaAlta => "[X]",
            _ => "[?]"
        };

        private static string StateCode(RevisionDisplayState state) => state switch {
            RevisionDisplayState.Mergiato => "M",
            RevisionDisplayState.DaMergiareDiretta => "D",
            RevisionDisplayState.DaMergiareIndiretta => "I",
            RevisionDisplayState.DaMergiareIndirettaAlta => "X",
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

        // ----------------------------------------------------------------
        // SVN helpers privati
        // ----------------------------------------------------------------
        private static void TryAddRevision(
            int rev, DateTime date, string auth, string msg,
            int maxRevs,
            Dictionary<int, RevisionInfo> details,
            List<int> toProcess,
            RevisionInfo? parent = null) {
            if (toProcess.Count >= maxRevs || details.ContainsKey(rev)) return;
            details[rev] = new RevisionInfo { Number = rev, Date = date, Author = auth, Message = msg, ParentRev = parent };
            toProcess.Add(rev);
        }

        private static async Task<XmlDocument?> RunSvnXmlAsync(string[] args, CancellationToken ct) {
            var raw = await RunSvnRawAsync(args, ct);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try {
                var doc = new XmlDocument();
                doc.LoadXml(raw);
                return doc;
            } catch { return null; }
        }

        private static async Task<string?> RunSvnRawAsync(string[] args, CancellationToken ct) {
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

            try {
                await proc.WaitForExitAsync(ct);
            } catch (OperationCanceledException) {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignored */ }
                throw;
            }

            var output = await outputTask;
            return proc.ExitCode == 0 ? output : null;
        }
    }
}
