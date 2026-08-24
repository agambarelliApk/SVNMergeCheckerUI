namespace SVNMergeCheckerUI
{
    /// <summary>
    /// Simple modal dialog that lets the user pick one configuration label from a list.
    /// </summary>
    internal sealed class ConfigSelectDialog : Form
    {
        public string? SelectedLabel { get; private set; }

        private readonly ListBox _listBox;

        public ConfigSelectDialog(string[] labels)
        {
            Text            = "Seleziona configurazione";
            MinimumSize     = new Size(380, 280);
            Size            = new Size(400, 320);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;

            var lblPrompt = new Label
            {
                Text     = "Seleziona un profilo da caricare:",
                Location = new Point(12, 12),
                Size     = new Size(360, 20)
            };

            _listBox = new ListBox
            {
                Location      = new Point(12, 38),
                Size          = new Size(360, 170),
                SelectionMode = SelectionMode.One
            };
            _listBox.Items.AddRange(labels.Cast<object>().ToArray());
            if (_listBox.Items.Count > 0) _listBox.SelectedIndex = 0;
            _listBox.DoubleClick += (_, _) => AcceptSelection();

            var btnOk = new Button
            {
                Text         = "Carica",
                DialogResult = DialogResult.None,
                Location     = new Point(200, 220),
                Size         = new Size(80, 28)
            };
            btnOk.Click += (_, _) => AcceptSelection();

            var btnCancel = new Button
            {
                Text         = "Annulla",
                DialogResult = DialogResult.Cancel,
                Location     = new Point(292, 220),
                Size         = new Size(80, 28)
            };

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lblPrompt, _listBox, btnOk, btnCancel });
        }

        private void AcceptSelection()
        {
            if (_listBox.SelectedItem is string label)
            {
                SelectedLabel = label;
                DialogResult  = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show("Seleziona un profilo dalla lista.",
                    "Selezione richiesta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
