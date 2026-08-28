using System;

namespace SVNMergeCheckerUI {
    public partial class Form1 : Form {
        private readonly ISvnService _svnService;
        private readonly IConfigService _configService;
        private readonly IReportParserService _reportParser;
        private readonly SvnCheckerHelper _svnCheckerHelper;

        private string _fullReportOutput = string.Empty;
        private CancellationTokenSource? _cts;
        private string? _btnRunOriginalText;
        private IReadOnlyDictionary<int, RevisionDisplayState> _revisionStates = new Dictionary<int, RevisionDisplayState>();

        public Form1() {
            InitializeComponent();
            _svnService = new SvnService();
            _configService = new JsonConfigService();
            _reportParser = new ReportParserService();
            _svnCheckerHelper = new SvnCheckerHelper(_svnService);

            cmbResultType.Items.AddRange(new object[]
            {
                "Elenco Revisioni", "Albero Dipendenze", "File Coinvolti", "Log Console", "Script output",
            });
            cmbResultType.SelectedIndex = 0;

            // group by options for File Coinvolti
            cmbGroupBy.Items.AddRange(new object[] { "Issue/Revisione", "File" });
            cmbGroupBy.SelectedIndex = 0;
            lblGroupBy.Visible = false;
            cmbGroupBy.Visible = false;

            // Mode selector for ResultType (Standard/Debug)
            cmbMode.Items.AddRange(new object[] { "Standard", "Debug" });
            cmbMode.SelectedIndexChanged += cmbMode_SelectedIndexChanged;
            cmbMode.SelectedIndex = 0; // default = Standard
            ApplyModeToResultType();

            txtOutFile.Text = @"C:\APSNet\Tempdir\report_svn_checker.txt";

            UpdateWindowTitle(string.Empty);
        }

        // ----------------------------------------------------------------
        // Window Title
        // ----------------------------------------------------------------
        private void UpdateWindowTitle(string label) {
            Text = string.IsNullOrWhiteSpace(label)
                ? "SVN Merge Checker"
                : $"SVN Merge Checker - {label}";
        }

        // ----------------------------------------------------------------
        // Browse buttons
        // ----------------------------------------------------------------
        private void btnBrowseWorkingCopy_Click(object sender, EventArgs e) {
            using var dlg = new FolderBrowserDialog { Description = "Seleziona la Working Copy" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                txtWorkingCopy.Text = dlg.SelectedPath;
        }

        private void btnBrowseSourceRepo_Click(object sender, EventArgs e) {
            using var dlg = new FolderBrowserDialog { Description = "Seleziona il Source Repository locale" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                txtSourceRepo.Text = dlg.SelectedPath;
        }

        private void btnBrowseOutFile_Click(object sender, EventArgs e) {
            using var dlg = new SaveFileDialog {
                Title = "Salva output come...",
                Filter = "File di testo (*.txt)|*.txt|Tutti i file (*.*)|*.*",
                DefaultExt = "txt"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                txtOutFile.Text = dlg.FileName;
        }

        // ----------------------------------------------------------------
        // SVN Connect
        // ----------------------------------------------------------------
        private async void btnSvnConnect_Click(object sender, EventArgs e) {
            if (!await AssertSvnAvailable()) return;

            SetActionButtons(false);
            rtbOutput.Clear();
            try {
                var timeoutMs = (int)numSvnTimeout.Value * 1000;
                var wcUrl = await _svnService.GetSvnUrlAsync(txtWorkingCopy.Text.Trim(), timeoutMs: timeoutMs);
                var srcUrl = await _svnService.GetSvnUrlAsync(txtSourceRepo.Text.Trim(), timeoutMs: timeoutMs);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[SVN Connect]");
                sb.AppendLine($"Working Copy URL  : {wcUrl ?? "(non rilevabile)"}");
                sb.AppendLine($"Source Repo URL   : {srcUrl ?? "(non rilevabile)"}");
                AppendOutput(sb.ToString());
            } catch (Exception ex) {
                AppendOutput($"[ERRORE] {ex.Message}");
            } finally {
                SetActionButtons(true);
            }
        }

        // ----------------------------------------------------------------
        // SVN Update
        // ----------------------------------------------------------------
        private async void btnSvnUpdate_Click(object sender, EventArgs e) {
            if (!await AssertSvnAvailable()) return;

            SetActionButtons(false);
            rtbOutput.Clear();
            try {
                var timeoutMs = (int)numSvnTimeout.Value * 1000;
                var wcResult = await _svnService.UpdateDirectoryAsync(txtWorkingCopy.Text.Trim(), timeoutMs);
                var srcResult = await _svnService.UpdateDirectoryAsync(txtSourceRepo.Text.Trim(), timeoutMs);

                AppendOutput("[SVN Update - Working Copy]\n" + wcResult);
                AppendOutput("\n[SVN Update - Source Repo]\n" + srcResult);
            } catch (Exception ex) {
                AppendOutput($"[ERRORE] {ex.Message}");
            } finally {
                SetActionButtons(true);
            }
        }

        // ----------------------------------------------------------------
        // Config Save / Load
        // ----------------------------------------------------------------
        private void btnSaveConfig_Click(object sender, EventArgs e) {
            var label = txtConfigLabel.Text.Trim();
            if (string.IsNullOrWhiteSpace(label)) {
                MessageBox.Show("Inserisci un'etichetta prima di salvare.",
                    "Etichetta mancante", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try {
                if (_configService.LabelExists(label)) {
                    var confirm = MessageBox.Show(
                        $"Una configurazione con l'etichetta \"{label}\" esiste già.\nVuoi sovrascriverla?",
                        "Conferma sovrascrittura",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (confirm != DialogResult.Yes) return;

                    _configService.Save(BuildConfig(), overwrite: true);
                } else {
                    _configService.Save(BuildConfig(), overwrite: false);
                }

                UpdateWindowTitle(label);
                MessageBox.Show($"Configurazione \"{label}\" salvata in:\n{_configService.StorePath}",
                    "Salvataggio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) {
                MessageBox.Show($"Errore durante il salvataggio:\n{ex.Message}",
                    "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnLoadConfig_Click(object sender, EventArgs e) {
            try {
                var all = _configService.LoadAll();
                if (all.Count == 0) {
                    MessageBox.Show($"Nessuna configurazione trovata in:\n{_configService.StorePath}",
                        "Caricamento", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using var dlg = new ConfigSelectDialog(all.Keys.ToArray());
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedLabel is null) return;

                var cfg = all[dlg.SelectedLabel];
                ApplyConfig(cfg);
                UpdateWindowTitle(cfg.ConfigLabel);
                MessageBox.Show($"Configurazione \"{cfg.ConfigLabel}\" caricata con successo.",
                    "Caricamento", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) {
                MessageBox.Show($"Errore durante il caricamento:\n{ex.Message}",
                    "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ----------------------------------------------------------------
        // Result Type Changed
        // ----------------------------------------------------------------
        private void cmbResultType_SelectedIndexChanged(object sender, EventArgs e) {
            if (string.IsNullOrEmpty(_fullReportOutput)) return;
            var resultType = cmbResultType.SelectedItem?.ToString() ?? "Elenco Revisioni";
            var isFileCoinvolti = resultType == "File Coinvolti";
            lblGroupBy.Visible = isFileCoinvolti;
            cmbGroupBy.Visible = isFileCoinvolti;
            RenderOutput(resultType);
        }

        private void cmbGroupBy_SelectedIndexChanged(object sender, EventArgs e) {
            if (string.IsNullOrEmpty(_fullReportOutput)) return;
            RenderOutput(cmbResultType.SelectedItem?.ToString() ?? "Elenco Revisioni");
        }

        private void cmbMode_SelectedIndexChanged(object sender, EventArgs e) {
            ApplyModeToResultType();
        }

        private void ApplyModeToResultType() {
            var current = cmbResultType.SelectedItem?.ToString();

            var items = new List<string>
            {
                "Elenco Revisioni",
                "Albero Dipendenze",
                "File Coinvolti"
            };

            if (cmbMode.SelectedItem?.ToString() == "Debug") {
                items.Add("Log Console");
                items.Add("Script output");
            }

            cmbResultType.BeginUpdate();
            try {
                cmbResultType.Items.Clear();
                foreach (var it in items) cmbResultType.Items.Add(it);

                if (current != null && items.Contains(current))
                    cmbResultType.SelectedItem = current;
                else
                    cmbResultType.SelectedIndex = 0;
            } finally { cmbResultType.EndUpdate(); }
        }

        // ----------------------------------------------------------------
        // Run Analysis
        // ----------------------------------------------------------------
        private async void btnRun_Click(object sender, EventArgs e) {
            if (!ValidateInputs()) return;
            if (!await AssertSvnAvailable()) return;

            SetActionButtons(false);
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;
            rtbOutput.Clear();
            _fullReportOutput = string.Empty;

            _cts = new CancellationTokenSource();

            var progress = new Progress<string>(line => AppendOutput(line));

            // Trasforma temporaneamente il pulsante Avvia in pulsante Annulla
            try {
                _btnRunOriginalText = btnRun.Text;
                // SetActionButtons(false) ha disabilitato anche btnRun: riabilitiamolo
                btnRun.Enabled = true;
                btnRun.Text = "✖ Annulla";
                btnRun.Click -= btnRun_Click;
                btnRun.Click += btnCancel_Click;
            } catch { }

            // Costruisce l'insieme di revisioni da saltare:
            // revisioni digitate dall'utente nel campo SkipRevisions.
            var skipSet = txtSkipRevisions.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => int.TryParse(t, out _))
                .Select(int.Parse)
                .ToHashSet();

            // Revisioni manuali
            var manualRevisions = txtRevisions.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => int.TryParse(t, out _))
                .Select(int.Parse)
                .ToArray();

            // Issue
            var issues = txtIssues.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            var checkerParams = new SvnCheckerParameters(
                WorkingCopy: txtWorkingCopy.Text.Trim(),
                SourceRepository: txtSourceRepo.Text.Trim(),
                Issues: issues,
                Revisions: manualRevisions,
                SkipRevisions: skipSet,
                MaxNewRevs: (int)numMaxNewRevs.Value,
                OutFile: txtOutFile.Text.Trim(),
                SvnTimeoutSeconds: (int)numSvnTimeout.Value
            );

            try {
                var result = await _svnCheckerHelper.RunAsync(checkerParams, progress, _cts.Token);

                _fullReportOutput = result.ReportText;

                if (result.RevisionStates.Count > 0) {
                    _revisionStates = result.RevisionStates;
                    var mergedCount = result.RevisionStates.Values.Count(s => s == RevisionDisplayState.Mergiato);
                    ((IProgress<string>)progress).Report(
                        $"[INFO] {mergedCount} revisioni già mergiate rilevate.");
                }

                var resultType = cmbResultType.SelectedItem?.ToString() ?? "Elenco Revisioni";
                RenderOutput(resultType);
            } catch (OperationCanceledException) {
                AppendOutput("[ANALISI ANNULLATA]");
            } catch (Exception ex) {
                MessageBox.Show($"Errore durante l'esecuzione:\n{ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } finally {
                _cts?.Dispose();
                _cts = null;
                progressBar.Visible = false;
                progressBar.Style = ProgressBarStyle.Blocks;
                SetActionButtons(true);

                // Ripristina il pulsante Run al comportamento originale se necessario
                try {
                    if (_btnRunOriginalText is not null) {
                        btnRun.Click -= btnCancel_Click;
                        btnRun.Click += btnRun_Click;
                        btnRun.Text = _btnRunOriginalText;
                        _btnRunOriginalText = null;
                    }
                } catch { }
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------
        private bool ValidateInputs() {
            if (string.IsNullOrWhiteSpace(txtIssues.Text) && string.IsNullOrWhiteSpace(txtRevisions.Text)) {
                MessageBox.Show(
                    "È necessario specificare almeno un codice Issue oppure un numero di Revisione.",
                    "Validazione", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private async Task<bool> AssertSvnAvailable() {
            if (await _svnService.IsSvnAvailableAsync()) return true;
            MessageBox.Show(
                "svn.exe non trovato nel PATH.\nInstalla Subversion e assicurati che sia incluso nel PATH di sistema.",
                "SVN non disponibile", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        private void SetActionButtons(bool enabled) {
            btnRun.Enabled = enabled;
            btnSvnUpdate.Enabled = enabled;
            btnLoadConfig.Enabled = enabled;
            btnSvnConnect.Enabled = enabled;
        }

        private void btnCancel_Click(object? sender, EventArgs e) {
            rtbOutput.SuspendLayout();
            rtbOutput.Clear();

            AppendOutput("[ANNULLAMENTO AVVIATO...]");
            try { _cts?.Cancel(); } catch { }

            // Ripristina immediatamente il pulsante Run
            try {
                if (_btnRunOriginalText is not null) {
                    btnRun.Click -= btnCancel_Click;
                    btnRun.Click += btnRun_Click;
                    btnRun.Text = _btnRunOriginalText;
                    _btnRunOriginalText = null;
                }
            } catch { }

            try {
                SetActionButtons(true);
                progressBar.Visible = false;
                progressBar.Style = ProgressBarStyle.Blocks;
            } catch { }
        }

        private void AppendOutput(string text) {
            if (rtbOutput.InvokeRequired) {
                rtbOutput.Invoke(() => AppendOutput(text));
                return;
            }
            rtbOutput.AppendText(text + Environment.NewLine);
            rtbOutput.ScrollToCaret();
        }

        private AppConfig BuildConfig() => new AppConfig {
            ConfigLabel = txtConfigLabel.Text.Trim(),
            WorkingCopy = txtWorkingCopy.Text.Trim(),
            SourceRepository = txtSourceRepo.Text.Trim(),
            //Issues           = txtIssues.Text.Trim(),
            //Revisions        = txtRevisions.Text.Trim(),
            SkipRevisions = txtSkipRevisions.Text.Trim(),
            MaxNewRevs = (int)numMaxNewRevs.Value,
            OutFile = txtOutFile.Text.Trim(),
            ResultType = cmbResultType.SelectedItem?.ToString() ?? "Elenco Revisioni",
            SvnTimeoutSeconds = (int)numSvnTimeout.Value
        };

        private void ApplyConfig(AppConfig cfg) {
            txtConfigLabel.Text = cfg.ConfigLabel;
            txtWorkingCopy.Text = cfg.WorkingCopy;
            txtSourceRepo.Text = cfg.SourceRepository;
            //txtIssues.Text         = cfg.Issues;
            //txtRevisions.Text      = cfg.Revisions;
            txtSkipRevisions.Text = cfg.SkipRevisions;
            numMaxNewRevs.Value = Math.Clamp(cfg.MaxNewRevs, (int)numMaxNewRevs.Minimum, (int)numMaxNewRevs.Maximum);
            numSvnTimeout.Value = Math.Clamp(cfg.SvnTimeoutSeconds > 0 ? cfg.SvnTimeoutSeconds : 60, (int)numSvnTimeout.Minimum, (int)numSvnTimeout.Maximum);
            txtOutFile.Text = cfg.OutFile;
            var idx = cmbResultType.Items.IndexOf(cfg.ResultType);
            cmbResultType.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void RenderOutput(string resultType) {
            rtbOutput.SuspendLayout();
            rtbOutput.Clear();

            if (resultType == "Elenco Revisioni") {
                var section = _reportParser.Parse(_fullReportOutput, resultType);
                var lines = _reportParser.ParseRevisioniLines(section, _revisionStates);
                RenderRevisioniColoured(lines);
            } else if (resultType == "File Coinvolti") {
                var raw = _reportParser.Parse(_fullReportOutput, "File Coinvolti - Raw");
                var groupBy = cmbGroupBy.SelectedItem?.ToString() ?? "Revisione";
                RenderFileCoinvoltiGrouped(raw, groupBy);
            } else {
                rtbOutput.Text = _reportParser.Parse(_fullReportOutput, resultType);
            }

            rtbOutput.ResumeLayout();
        }

        // Colore e simbolo unico per ciascuno dei 4 stati di visualizzazione delle revisioni.
        private static (Color Color, string Symbol) GetStateVisual(RevisionDisplayState state) => state switch {
            RevisionDisplayState.Mergiato => (Color.Green, "[\u2713]"),
            RevisionDisplayState.DaMergiareDiretta => (Color.Blue, "[>]"),
            RevisionDisplayState.DaMergiareIndiretta => (Color.Orange, "[!]"),
            RevisionDisplayState.DaMergiareIndirettaAlta => (Color.Red, "[X]"),
            _ => (Color.Gray, "[?]")
        };

        private static string NormalizeRevisionText(string text) =>
            System.Text.RegularExpressions.Regex.Replace(text, @"(?i)^\s*r(?=\d)", string.Empty);

        private void RenderRevisioniColoured(IReadOnlyList<(string line, RevisionDisplayState? state)> lines) {
            foreach (var (line, state) in lines) {
                // Righe senza uno stato di revisione riconosciuto (titoli sezione, intestazioni
                // issue "[ISSUE-123]", righe vuote) restano invariate col colore di default.
                if (state is null) {
                    rtbOutput.SelectionColor = rtbOutput.ForeColor;
                    rtbOutput.AppendText(line + Environment.NewLine);
                    continue;
                }

                var (color, symbol) = GetStateVisual(state.Value);

                // Replace any leading '=>' or '==' with the state symbol in square brackets
                var displayed = System.Text.RegularExpressions.Regex.Replace(
                                line, @"^(\s*)(=>|==)\s*", "$1> " + symbol + " ");

                // If there was no leading marker, prepend the symbol
                if (!displayed.Trim().StartsWith("> " + symbol + " "))
                    displayed = "> " + symbol + " " + displayed.TrimStart();

                // Remove the leading 'r' from the revision number right after the symbol (es. "r12345" -> "12345")
                displayed = System.Text.RegularExpressions.Regex.Replace(
                    displayed, "^(\\[.\\]\\s+)r(?=\\d)", "$1");

                rtbOutput.SelectionColor = color;
                rtbOutput.AppendText(displayed + Environment.NewLine);
            }
            rtbOutput.SelectionColor = rtbOutput.ForeColor;
        }

        private void RenderFileCoinvoltiGrouped(string rawSection, string groupBy) {
            if (string.IsNullOrWhiteSpace(rawSection)) {
                rtbOutput.Text = rawSection;
                return;
            }

            var revRegex = new System.Text.RegularExpressions.Regex(@"(?<!\d)r?(?<rev>\d{1,7})(?!\d)", System.Text.RegularExpressions.RegexOptions.Compiled);

            // Parsing: identifica blocchi issue e revisioni
            var issueBlocks = new List<(string issueLabel, List<(string revLabel, List<string> files)> revisions)>();
            string? currentIssue = null;
            string? currentRev = null;
            List<(string revLabel, List<string> files)>? currentIssueRevs = null;
            List<string>? currentRevFiles = null;

            foreach (var rawLine in rawSection.Split('\n')) {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) {
                    // Salva la revisione pendente prima di chiudere il blocco issue corrente
                    if (currentRev != null && currentRevFiles != null && currentIssueRevs != null)
                        currentIssueRevs.Add((currentRev, currentRevFiles));

                    // Salva il blocco issue corrente
                    if (currentIssue != null && currentIssueRevs != null)
                        issueBlocks.Add((currentIssue, currentIssueRevs));

                    currentIssue = trimmed;
                    currentIssueRevs = new List<(string, List<string>)>();
                    currentRev = null;
                    currentRevFiles = null;
                } else if (trimmed.StartsWith(">") && trimmed.Contains("REVISIONE")) {
                    // Nuova revisione
                    if (currentRev != null && currentRevFiles != null && currentIssueRevs != null)
                        currentIssueRevs.Add((currentRev, currentRevFiles));

                    var revText = System.Text.RegularExpressions.Regex.Replace(
                        trimmed.TrimStart('>', ' ').Trim(),
                        "(?i)\\bREVISIONE\\b\\s*",
                        string.Empty).TrimEnd(':', ' ').Trim();
                    currentRev = NormalizeRevisionText(revText);
                    currentRevFiles = new List<string>();
                } else if (trimmed.StartsWith("-") && currentRevFiles != null) {
                    var file = trimmed.TrimStart('-').Trim();
                    if (!string.IsNullOrWhiteSpace(file) && !file.StartsWith("Nessun file"))
                        currentRevFiles.Add(file);
                }
            }

            // Chiudi ultimo blocco
            if (currentRev != null && currentRevFiles != null && currentIssueRevs != null)
                currentIssueRevs.Add((currentRev, currentRevFiles));
            if (currentIssue != null && currentIssueRevs != null)
                issueBlocks.Add((currentIssue, currentIssueRevs));

            // Se non ci sono blocchi issue, fallback al comportamento legacy
            if (issueBlocks.Count == 0) {
                RenderFileCoinvoltiGroupedLegacy(rawSection, groupBy, revRegex);
                return;
            }

            // Rendering raggruppato per issue
            if (groupBy == "File") {
                // Pivot globale: file → (issue → lista revisioni)
                // Struttura: fileOrder, poi per ogni file: dict issue → list revLabel
                var fileOrder = new List<string>();
                // file → lista di (issueLabel, revLabel)
                var fileToIssueRevs = new Dictionary<string, List<(string issueLabel, string revLabel)>>(StringComparer.OrdinalIgnoreCase);

                foreach (var (issueLabel, revisions) in issueBlocks) {
                    foreach (var (revLabel, files) in revisions) {
                        foreach (var file in files) {
                            if (!fileToIssueRevs.ContainsKey(file)) {
                                fileToIssueRevs[file] = new List<(string, string)>();
                                fileOrder.Add(file);
                            }
                            fileToIssueRevs[file].Add((issueLabel, revLabel));
                        }
                    }
                }

                foreach (var file in fileOrder) {
                    // Intestazione documento: "> doc1"
                    rtbOutput.SelectionFont = new Font(rtbOutput.Font, FontStyle.Bold);
                    rtbOutput.SelectionColor = rtbOutput.ForeColor;
                    rtbOutput.AppendText("> " + file + Environment.NewLine);
                    rtbOutput.SelectionFont = rtbOutput.Font;

                    // Raggruppa per issue mantenendo l'ordine di prima occorrenza
                    var issueOrder = new List<string>();
                    var issueToRevs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (issueLabel, revLabel) in fileToIssueRevs[file]) {
                        if (!issueToRevs.ContainsKey(issueLabel)) {
                            issueToRevs[issueLabel] = new List<string>();
                            issueOrder.Add(issueLabel);
                        }
                        issueToRevs[issueLabel].Add(revLabel);
                    }

                    foreach (var issueLabel in issueOrder) {
                        // Etichetta issue: " - [issue1]"
                        rtbOutput.SelectionFont = new Font(rtbOutput.Font, FontStyle.Bold);
                        rtbOutput.SelectionColor = Color.DarkBlue;
                        rtbOutput.AppendText($" {issueLabel}" + Environment.NewLine);
                        rtbOutput.SelectionFont = rtbOutput.Font;
                        foreach (var rev in issueToRevs[issueLabel])
                            AppendRevisionEntry(rev, revRegex, isHeader: true, indent: "    ");
                    }
                    rtbOutput.AppendText(Environment.NewLine);
                }
            } else {
                // Modalità Revisione: mostra issue → revisioni → file
                foreach (var (issueLabel, revisions) in issueBlocks) {
                    rtbOutput.SelectionFont = new Font(rtbOutput.Font, FontStyle.Bold);
                    rtbOutput.SelectionColor = Color.DarkBlue;
                    rtbOutput.AppendText(issueLabel + Environment.NewLine + Environment.NewLine);
                    rtbOutput.SelectionFont = rtbOutput.Font;

                    foreach (var (revLabel, files) in revisions) {
                        AppendRevisionEntry(revLabel, revRegex, isHeader: true);
                        foreach (var file in files) {
                            rtbOutput.SelectionColor = rtbOutput.ForeColor;
                            rtbOutput.AppendText("  - " + file + Environment.NewLine);
                        }
                    }
                    rtbOutput.AppendText(Environment.NewLine);
                }
            }

            rtbOutput.SelectionColor = rtbOutput.ForeColor;
        }

        private void RenderFileCoinvoltiGroupedLegacy(string rawSection, string groupBy, System.Text.RegularExpressions.Regex revRegex) {
            var revToFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var fileToRevs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var revOrder = new List<string>();
            var fileOrder = new List<string>();

            string? currentRev = null;
            foreach (var rawLine in rawSection.Split('\n')) {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(">")) {
                    var revText = System.Text.RegularExpressions.Regex.Replace(trimmed.TrimStart('>', ' ').Trim(), "(?i)\\bREVISIONE\\b\\s*", string.Empty).TrimEnd(':', ' ').Trim();
                    currentRev = NormalizeRevisionText(revText);
                    if (!revToFiles.ContainsKey(currentRev)) { revToFiles[currentRev] = new List<string>(); revOrder.Add(currentRev); }
                } else if (trimmed.StartsWith("-") && currentRev is not null) {
                    var file = trimmed.TrimStart('-').Trim();
                    if (string.IsNullOrWhiteSpace(file)) continue;
                    revToFiles[currentRev].Add(file);
                    if (!fileToRevs.ContainsKey(file)) { fileToRevs[file] = new List<string>(); fileOrder.Add(file); }
                    fileToRevs[file].Add(currentRev);
                }
            }

            if (groupBy == "Documento") {
                foreach (var file in fileOrder) {
                    rtbOutput.SelectionFont = new Font(rtbOutput.Font, FontStyle.Bold);
                    rtbOutput.AppendText("> " + file + Environment.NewLine);
                    rtbOutput.SelectionFont = rtbOutput.Font;
                    foreach (var rev in fileToRevs[file])
                        AppendRevisionEntry(rev, revRegex, isHeader: false);
                }
            } else {
                foreach (var rev in revOrder) {
                    AppendRevisionEntry(rev, revRegex, isHeader: true);
                    foreach (var file in revToFiles[rev]) {
                        rtbOutput.SelectionColor = rtbOutput.ForeColor;
                        rtbOutput.AppendText("  - " + file + Environment.NewLine);
                    }
                }
            }
            rtbOutput.SelectionColor = rtbOutput.ForeColor;
        }

        private void AppendRevisionEntry(string revText, System.Text.RegularExpressions.Regex revRegex, bool isHeader = true, string indent = "") {
            var m = revRegex.Match(revText);
            var prefix = isHeader ? ">" : "  -";
            string marker = string.Empty;
            var color = rtbOutput.ForeColor;
            if (m.Success && int.TryParse(m.Groups["rev"].Value, out var rev) && _revisionStates.TryGetValue(rev, out var state)) {
                var visual = GetStateVisual(state);
                color = visual.Color;
                marker = " " + visual.Symbol;
            }
            rtbOutput.SelectionColor = color;
            rtbOutput.AppendText($"{indent}{prefix}{marker} {NormalizeRevisionText(revText)}" + Environment.NewLine);
        }
        
    }
}

