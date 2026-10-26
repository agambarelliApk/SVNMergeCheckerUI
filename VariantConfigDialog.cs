namespace SVNMergeCheckerUI {
    // Dialog di configurazione di parallel_modules.json (voci della combo "Includi").
    public class VariantConfigDialog : Form {
        private readonly ISvnService _svn;
        private readonly List<VariantDefinition> _variants;

        private readonly ListBox _lstVariants = new();
        private readonly TextBox _txtLabel = new();
        private readonly TextBox _txtFolder = new();
        private readonly Button _btnBrowse = new();
        private readonly Button _btnAdd = new();
        private readonly Button _btnRemove = new();
        private readonly Button _btnOk = new();
        private readonly Button _btnCancel = new();

        private bool _folderVerified;

        public IReadOnlyList<VariantDefinition> Variants => _variants;

        public VariantConfigDialog(ISvnService svn, IEnumerable<VariantDefinition> current) {
            _svn = svn;
            _variants = JsonVariantConfigService.Normalize(current).ToList();
            BuildUi();
            RefreshList();
            UpdateAddState();
        }

        // "Aggiungi" è abilitato solo con etichetta non vuota e cartella SVN verificata.
        private void UpdateAddState() =>
            _btnAdd.Enabled = _btnBrowse.Enabled &&
                              !string.IsNullOrWhiteSpace(_txtLabel.Text) &&
                              _folderVerified &&
                              !string.IsNullOrWhiteSpace(_txtFolder.Text);

        private void BuildUi() {
            Text = "Configura moduli paralleli";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(480, 340);

            var lblList = new Label { Text = "Voci attuali:", Location = new Point(12, 10), AutoSize = true };
            _lstVariants.Location = new Point(12, 32);
            _lstVariants.Size = new Size(456, 120);
            _lstVariants.SelectedIndexChanged += (_, _) => UpdateRemoveState();

            _btnRemove.Text = "Rimuovi";
            _btnRemove.Location = new Point(368, 156);
            _btnRemove.Size = new Size(100, 28);
            _btnRemove.Click += btnRemove_Click;

            var lblLabel = new Label { Text = "Etichetta (Label):", Location = new Point(12, 196), AutoSize = true };
            _txtLabel.Location = new Point(150, 193);
            _txtLabel.Size = new Size(318, 26);
            _txtLabel.TextChanged += (_, _) => UpdateAddState();

            var lblFolder = new Label { Text = "Cartella URL (FolderName):", Location = new Point(12, 230), AutoSize = true };
            _txtFolder.Location = new Point(190, 227);
            _txtFolder.Size = new Size(240, 26);
            _txtFolder.ReadOnly = true;

            _btnBrowse.Text = "...";
            _btnBrowse.Location = new Point(436, 226);
            _btnBrowse.Size = new Size(32, 28);
            _btnBrowse.Click += btnBrowse_Click;

            _btnAdd.Text = "Aggiungi";
            _btnAdd.Location = new Point(368, 264);
            _btnAdd.Size = new Size(100, 28);
            _btnAdd.Click += btnAdd_Click;

            _btnOk.Text = "OK";
            _btnOk.Location = new Point(258, 302);
            _btnOk.Size = new Size(100, 28);
            _btnOk.DialogResult = DialogResult.OK;

            _btnCancel.Text = "Annulla";
            _btnCancel.Location = new Point(368, 302);
            _btnCancel.Size = new Size(100, 28);
            _btnCancel.DialogResult = DialogResult.Cancel;

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Controls.AddRange(new Control[] {
                lblList, _lstVariants, _btnRemove, lblLabel, _txtLabel,
                lblFolder, _txtFolder, _btnBrowse, _btnAdd, _btnOk, _btnCancel
            });
        }

        private void RefreshList() {
            _lstVariants.Items.Clear();
            foreach (var v in _variants)
                _lstVariants.Items.Add($"{v.Label}  ->  {v.FolderName}");
            UpdateRemoveState();
        }

        // "Generale" non è mai rimovibile.
        private void UpdateRemoveState() {
            var idx = _lstVariants.SelectedIndex;
            _btnRemove.Enabled = idx >= 0 &&
                !string.Equals(_variants[idx].Label, JsonVariantConfigService.GeneraleLabel, StringComparison.OrdinalIgnoreCase);
        }

        private async void btnBrowse_Click(object? sender, EventArgs e) {
            try {
                using var dlg = new FolderBrowserDialog {
                    Description = "Seleziona la cartella (working copy SVN) della variante",
                    UseDescriptionForTitle = true
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                _btnBrowse.Enabled = false;
                _btnAdd.Enabled = false;
                string? url;
                try {
                    url = await _svn.GetSvnUrlAsync(dlg.SelectedPath);
                } finally {
                    _btnBrowse.Enabled = true;
                    UpdateAddState();
                }

                var folder = JsonVariantConfigService.ExtractFolderName(url);
                if (folder is null) {
                    _folderVerified = false;
                    _txtFolder.Text = string.Empty;
                    UpdateAddState();
                    MessageBox.Show(this,
                        "Nessun URL SVN associato alla cartella selezionata: impossibile aggiungerla.",
                        "Cartella non valida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _folderVerified = true;
                _txtFolder.Text = folder;
                if (string.IsNullOrWhiteSpace(_txtLabel.Text))
                    _txtLabel.Text = folder;
                UpdateAddState();
            } catch (Exception ex) {
                MessageBox.Show(this, $"Errore durante la verifica della cartella: {ex.Message}",
                    "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnAdd_Click(object? sender, EventArgs e) {
            var label = JsonVariantConfigService.Capitalize(_txtLabel.Text.Trim());
            if (label.Length == 0) {
                Warn("Inserire un'etichetta.");
                return;
            }
            if (string.Equals(label, JsonVariantConfigService.TuttoLabel, StringComparison.OrdinalIgnoreCase)) {
                Warn("L'etichetta 'Tutto' è riservata.");
                return;
            }
            if (_variants.Any(v => string.Equals(v.Label, label, StringComparison.OrdinalIgnoreCase))) {
                Warn("Esiste già una voce con questa etichetta.");
                return;
            }
            if (!_folderVerified || string.IsNullOrWhiteSpace(_txtFolder.Text)) {
                Warn("Selezionare una cartella con un URL SVN valido (pulsante '...').");
                return;
            }

            // "Generale" resta sempre in ultima posizione.
            var generaleIdx = _variants.FindIndex(v =>
                string.Equals(v.Label, JsonVariantConfigService.GeneraleLabel, StringComparison.OrdinalIgnoreCase));
            var newItem = new VariantDefinition(label, _txtFolder.Text.Trim());
            if (generaleIdx >= 0) _variants.Insert(generaleIdx, newItem);
            else _variants.Add(newItem);

            _txtLabel.Clear();
            _txtFolder.Clear();
            _folderVerified = false;
            RefreshList();
            UpdateAddState();
        }

        private void btnRemove_Click(object? sender, EventArgs e) {
            var idx = _lstVariants.SelectedIndex;
            if (idx < 0) return;
            if (string.Equals(_variants[idx].Label, JsonVariantConfigService.GeneraleLabel, StringComparison.OrdinalIgnoreCase))
                return;
            _variants.RemoveAt(idx);
            RefreshList();
        }

        private void Warn(string message) =>
            MessageBox.Show(this, message, "Attenzione", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
