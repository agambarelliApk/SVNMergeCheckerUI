using System;

namespace SVNMergeCheckerUI {
    public partial class Form1 : Form {
        private readonly ISvnService _svnService;
        private readonly IConfigService _configService;
        private readonly IReportParserService _reportParser;
        private readonly IRevisionRenderingService _revisionRenderingService;
        private readonly SvnCheckerHelper _svnCheckerHelper;

        private string _fullReportOutput = string.Empty;
        private CancellationTokenSource? _cts;
        private string? _btnRunOriginalText;
        private IReadOnlyDictionary<int, RevisionDisplayState> _revisionStates = new Dictionary<int, RevisionDisplayState>();
        private IReadOnlyDictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>> _perIssueRevisionStates = new Dictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>();

        public Form1() {
            InitializeComponent();
            _svnService = new SvnService();
            _configService = new JsonConfigService();
            _reportParser = new ReportParserService();
            _revisionRenderingService = new RevisionRenderingService(_reportParser);
            _svnCheckerHelper = new SvnCheckerHelper(_svnService);

            // Mode selector for ResultType (Standard/Debug)
            cmbMode.Items.AddRange(new object[] { "Standard", "Debug" });
            cmbMode.SelectedIndexChanged += cmbMode_SelectedIndexChanged;
            cmbMode.SelectedIndex = 0; // default = Standard
            ApplyModeToResultType();
            UpdateGroupByOptions();

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
            UpdateGroupByOptions();
            if (string.IsNullOrEmpty(_fullReportOutput)) return;
            var resultType = cmbResultType.SelectedItem?.ToString() ?? "Elenco Revisioni";
            RenderOutput(resultType);
        }

        private void cmbGroupBy_SelectedIndexChanged(object sender, EventArgs e) {
            if (string.IsNullOrEmpty(_fullReportOutput)) return;
            RenderOutput(cmbResultType.SelectedItem?.ToString() ?? "Elenco Revisioni");
        }

        private void UpdateGroupByOptions() {
            var resultType = cmbResultType.SelectedItem?.ToString() ?? "Elenco Revisioni";
            var currentGroupBy = cmbGroupBy.SelectedItem?.ToString();

            if (resultType == "Elenco Revisioni") {
                lblGroupBy.Visible = true;
                cmbGroupBy.Visible = true;
                var items = new[] { "Issue", "Merge Suggerito" };
                if (!AreComboItemsEqual(cmbGroupBy.Items, items)) {
                    cmbGroupBy.BeginUpdate();
                    try {
                        cmbGroupBy.Items.Clear();
                        cmbGroupBy.Items.AddRange(items);
                        cmbGroupBy.SelectedIndex = (currentGroupBy != null && items.Contains(currentGroupBy))
                            ? Array.IndexOf(items, currentGroupBy)
                            : 0;
                    } finally {
                        cmbGroupBy.EndUpdate();
                    }
                }
            } else if (resultType == "File Coinvolti") {
                lblGroupBy.Visible = true;
                cmbGroupBy.Visible = true;
                var items = new[] { "Issue/Revisione", "File" };
                if (!AreComboItemsEqual(cmbGroupBy.Items, items)) {
                    cmbGroupBy.BeginUpdate();
                    try {
                        cmbGroupBy.Items.Clear();
                        cmbGroupBy.Items.AddRange(items);
                        cmbGroupBy.SelectedIndex = (currentGroupBy != null && items.Contains(currentGroupBy))
                            ? Array.IndexOf(items, currentGroupBy)
                            : 0;
                    } finally {
                        cmbGroupBy.EndUpdate();
                    }
                }
            } else {
                lblGroupBy.Visible = false;
                cmbGroupBy.Visible = false;
            }
        }

        private static bool AreComboItemsEqual(ComboBox.ObjectCollection currentItems, string[] expectedItems) {
            if (currentItems.Count != expectedItems.Length) return false;
            for (int i = 0; i < expectedItems.Length; i++) {
                if (currentItems[i]?.ToString() != expectedItems[i]) return false;
            }
            return true;
        }

        private void cmbMode_SelectedIndexChanged(object sender, EventArgs e) {
            ApplyModeToResultType();
        }

        private void ApplyModeToResultType() {
            var current = cmbResultType.SelectedItem?.ToString();

            var items = new List<string>
            {
                "Elenco Revisioni",                
                "File Coinvolti"
            };

            if (cmbMode.SelectedItem?.ToString() == "Debug") {
                items.Add("Albero Dipendenze");
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
                    _perIssueRevisionStates = result.PerIssueRevisionStates;
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
                var groupBy = cmbGroupBy.SelectedItem?.ToString() ?? "Issue";
                IReadOnlyDictionary<string, IReadOnlyDictionary<int, List<int>>>? perIssueTree = null;
                var effectivePerIssueRevisionStates = _perIssueRevisionStates;

                if (string.Equals(groupBy, "Issue", StringComparison.OrdinalIgnoreCase)) {
                    var alberoSection = _reportParser.Parse(_fullReportOutput, "Albero Dipendenze");
                    perIssueTree = _reportParser.ParseAlberoDipendenze(alberoSection);

                    if (perIssueTree != null && _perIssueRevisionStates.Count > 0) {
                        // Raccoglie per ciascuna issue le relative revisioni dirette (DaMergiareDiretta)
                        var directRevsByIssue = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (issue, states) in _perIssueRevisionStates) {
                            directRevsByIssue[issue] = states
                                .Where(kvp => kvp.Value == RevisionDisplayState.DaMergiareDiretta)
                                .Select(kvp => kvp.Key)
                                .ToHashSet();
                        }

                        // Se una revisione diretta di issueA compare nell'albero di un'altra issueB come DipendenzaPrecedenteDaMergiare,
                        // il suo stato in issueB viene aggiornato a DipendenzaDirettaAltraIssuePrecedenteDaMergiare (colore Blue con simbolo [❗▶]).
                        var modifiedStates = _perIssueRevisionStates
                            .ToDictionary(k => k.Key, k => new Dictionary<int, RevisionDisplayState>(k.Value), StringComparer.OrdinalIgnoreCase);

                        foreach (var (issueA, directRevs) in directRevsByIssue) {
                            foreach (var (issueB, treeB) in perIssueTree) {
                                if (string.Equals(issueA, issueB, StringComparison.OrdinalIgnoreCase)) continue;

                                if (modifiedStates.TryGetValue(issueB, out var statesB)) {
                                    foreach (var rev in directRevs) {
                                        var isChildInTreeB = treeB.Values.Any(children => children.Contains(rev));
                                        if (isChildInTreeB && statesB.TryGetValue(rev, out var stateB) && stateB == RevisionDisplayState.DipendenzaPrecedenteDaMergiare) {
                                            statesB[rev] = RevisionDisplayState.DipendenzaDirettaAltraIssuePrecedenteDaMergiare;
                                        }
                                    }
                                }
                            }
                        }

                        effectivePerIssueRevisionStates = modifiedStates.ToDictionary(
                            k => k.Key,
                            k => (IReadOnlyDictionary<int, RevisionDisplayState>)k.Value,
                            StringComparer.OrdinalIgnoreCase);
                    }
                }
                var lines = _reportParser.ParseRevisioniLines(section, _revisionStates, effectivePerIssueRevisionStates, groupBy, perIssueTree);
                RenderRevisioniColoured(lines);
            } else if (resultType == "File Coinvolti") {
                var raw = _reportParser.Parse(_fullReportOutput, "File Coinvolti - Raw");
                var groupBy = cmbGroupBy.SelectedItem?.ToString() ?? "Issue/Revisione";
                var model = _revisionRenderingService.BuildFileCoinvoltiModel(raw, groupBy, _revisionStates, _perIssueRevisionStates);
                RenderFileCoinvoltiModel(model);
            } else {
                rtbOutput.Text = _reportParser.Parse(_fullReportOutput, resultType);
            }

            rtbOutput.ResumeLayout();
        }

        // Colore e simbolo unico per ciascuno degli stati di visualizzazione delle revisioni.
        private static (Color Color, string Symbol) GetStateVisual(RevisionDisplayState state) => state switch {
            // Revisione già mergiata nella working copy di destinazione
            RevisionDisplayState.Mergiato => (Color.Green, "[\u2714 ]"),
            // Revisione diretta dell'issue da mergiare
            RevisionDisplayState.DaMergiareDiretta => (Color.Blue, "[\u25B6]"),
            // Revisione diretta utente della stessa issue emessa come dipendenza figlia nell'albero
            RevisionDisplayState.DipendenzaUtentePrecedenteDaMergiare => (Color.LightGray, "[\u25B6]"),
            // Dipendenza indiretta ancora da mergiare
            RevisionDisplayState.DaMergiareIndiretta => (Color.Orange, "[\u2757]"),
            // Dipendenza indiretta con numero di revisione più alto delle dirette
            RevisionDisplayState.DaMergiareIndirettaAlta => (Color.Red, "[\u2714]"),
            // Dipendenza temporale successiva già mergiata
            RevisionDisplayState.DipendenzaSuccessivaMergiata => (Color.Red, "[\u2714]"),
            // Dipendenza temporale successiva da mergiare
            RevisionDisplayState.DipendenzaSuccessivaDaMergiare => (Color.Orange, "[\u25B6]"),
            // Dipendenza temporale precedente già mergiata
            RevisionDisplayState.DipendenzaPrecedenteMergiata => (Color.Green, "[\u2714]"),
            // Dipendenza temporale precedente da mergiare (non diretta in altre issue)
            RevisionDisplayState.DipendenzaPrecedenteDaMergiare => (Color.Brown, "[\u2757\u25B6]"),
            // Dipendenza temporale precedente da mergiare che è revisione diretta di un'altra issue (cambia colore da Brown a Blue mantenendo il simbolo)
            RevisionDisplayState.DipendenzaDirettaAltraIssuePrecedenteDaMergiare => (Color.Blue, "[\u2757\u25B6]"),
            _ => (Color.Gray, "[?]")
        };

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

                // Replace any leading '=>' or '==', più l'eventuale simbolo di stato testuale già
                // presente nel report (scritto da MergeStateLabel, es. "[▶]"), con il marcatore ">"
                // seguito dal simbolo colorato calcolato da GetStateVisual. Questo evita di avere
                // il simbolo duplicato (uno testuale dal report, uno colorato dalla UI).
                var displayed = System.Text.RegularExpressions.Regex.Replace(
                                line, @"^(\s*)(=>|==)\s*(\[[^\]]*\]\s*)?", "$1> " + symbol + " ");

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

        private void RenderFileCoinvoltiModel(FileCoinvoltiModel model) {
            if (model.RootNodes.Count == 0) {
                rtbOutput.Text = string.Empty;
                return;
            }

            foreach (var node in model.RootNodes) {
                RenderFileCoinvoltiNode(node);
                if (node.TrailingBlankLine)
                    rtbOutput.AppendText(Environment.NewLine);
            }

            rtbOutput.SelectionColor = rtbOutput.ForeColor;
        }

        private void RenderFileCoinvoltiNode(FileCoinvoltiNode node) {
            switch (node.Kind) {
                case FileCoinvoltiNodeKind.FileHeader:
                    rtbOutput.SelectionFont = new Font(rtbOutput.Font, FontStyle.Bold);
                    rtbOutput.SelectionColor = rtbOutput.ForeColor;
                    rtbOutput.AppendText("> " + node.Label + Environment.NewLine);
                    rtbOutput.SelectionFont = rtbOutput.Font;
                    foreach (var child in node.Children)
                        RenderFileCoinvoltiNode(child);
                    break;

                case FileCoinvoltiNodeKind.IssueHeaderNested:
                    rtbOutput.SelectionFont = new Font(rtbOutput.Font, FontStyle.Bold);
                    rtbOutput.SelectionColor = Color.DarkBlue;
                    rtbOutput.AppendText(node.Label + Environment.NewLine);
                    rtbOutput.SelectionFont = rtbOutput.Font;
                    foreach (var child in node.Children)
                        RenderFileCoinvoltiNode(child);
                    break;

                case FileCoinvoltiNodeKind.IssueHeaderTop:
                    rtbOutput.SelectionFont = new Font(rtbOutput.Font, FontStyle.Bold);
                    rtbOutput.SelectionColor = Color.DarkBlue;
                    rtbOutput.AppendText(node.Label + Environment.NewLine + Environment.NewLine);
                    rtbOutput.SelectionFont = rtbOutput.Font;
                    foreach (var child in node.Children)
                        RenderFileCoinvoltiNode(child);
                    break;

                case FileCoinvoltiNodeKind.RevisionMarker:
                    AppendRevisionMarkerNode(node);
                    break;
            }
        }

        private void AppendRevisionMarkerNode(FileCoinvoltiNode node) {
            var prefix = node.IsMarkerHeader ? ">" : "  -";
            string marker = string.Empty;
            var color = rtbOutput.ForeColor;
            if (node.State is RevisionDisplayState state) {
                var visual = GetStateVisual(state);
                color = visual.Color;
                marker = " " + visual.Symbol;
            }
            rtbOutput.SelectionColor = color;
            rtbOutput.AppendText($"{node.Indent}{prefix}{marker} {node.Label}" + Environment.NewLine);

            foreach (var file in node.Files) {
                rtbOutput.SelectionColor = rtbOutput.ForeColor;
                rtbOutput.AppendText("  - " + file + Environment.NewLine);
            }
        }

    }
}

