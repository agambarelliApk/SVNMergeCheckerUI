namespace SVNMergeCheckerUI {
    partial class Form1 {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            grpDirectory = new GroupBox();
            lblWorkingCopy = new Label();
            txtWorkingCopy = new TextBox();
            btnBrowseWorkingCopy = new Button();
            lblSourceRepo = new Label();
            txtSourceRepo = new TextBox();
            btnBrowseSourceRepo = new Button();
            btnSvnConnect = new Button();
            btnSvnUpdate = new Button();
            grpConfig = new GroupBox();
            lblConfigLabel = new Label();
            txtConfigLabel = new TextBox();
            btnSaveConfig = new Button();
            btnLoadConfig = new Button();
            lblMode = new Label();
            cmbMode = new ComboBox();
            grpElaborazioni = new GroupBox();
            grpParametri = new GroupBox();
            lblSkipRevisions = new Label();
            txtSkipRevisions = new TextBox();
            lblIssues = new Label();
            txtIssues = new TextBox();
            lblRevisions = new Label();
            txtRevisions = new TextBox();
            lblMaxNewRevs = new Label();
            numMaxNewRevs = new NumericUpDown();
            grpOutputCfg = new GroupBox();
            lblOutFile = new Label();
            txtOutFile = new TextBox();
            btnBrowseOutFile = new Button();
            lblResultType = new Label();
            cmbResultType = new ComboBox();
            lblGroupBy = new Label();
            cmbGroupBy = new ComboBox();
            grpRun = new GroupBox();
            btnRun = new Button();
            progressBar = new ProgressBar();
            grpResult = new GroupBox();
            rtbOutput = new RichTextBox();
            grpDirectory.SuspendLayout();
            grpConfig.SuspendLayout();
            grpElaborazioni.SuspendLayout();
            grpParametri.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numMaxNewRevs).BeginInit();
            grpOutputCfg.SuspendLayout();
            grpRun.SuspendLayout();
            grpResult.SuspendLayout();
            SuspendLayout();
            // 
            // grpDirectory
            // 
            grpDirectory.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpDirectory.Controls.Add(lblWorkingCopy);
            grpDirectory.Controls.Add(txtWorkingCopy);
            grpDirectory.Controls.Add(btnBrowseWorkingCopy);
            grpDirectory.Controls.Add(lblSourceRepo);
            grpDirectory.Controls.Add(txtSourceRepo);
            grpDirectory.Controls.Add(btnBrowseSourceRepo);
            grpDirectory.Controls.Add(btnSvnConnect);
            grpDirectory.Controls.Add(btnSvnUpdate);
            grpDirectory.Location = new Point(8, 8);
            grpDirectory.Name = "grpDirectory";
            grpDirectory.Size = new Size(500, 140);
            grpDirectory.TabIndex = 0;
            grpDirectory.TabStop = false;
            grpDirectory.Text = "📁 Directory";
            // 
            // lblWorkingCopy
            // 
            lblWorkingCopy.Location = new Point(8, 24);
            lblWorkingCopy.Name = "lblWorkingCopy";
            lblWorkingCopy.Size = new Size(100, 20);
            lblWorkingCopy.TabIndex = 0;
            lblWorkingCopy.Text = "Working Copy:";
            // 
            // txtWorkingCopy
            // 
            txtWorkingCopy.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtWorkingCopy.Location = new Point(112, 22);
            txtWorkingCopy.Name = "txtWorkingCopy";
            txtWorkingCopy.Size = new Size(340, 26);
            txtWorkingCopy.TabIndex = 1;
            // 
            // btnBrowseWorkingCopy
            // 
            btnBrowseWorkingCopy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseWorkingCopy.Location = new Point(456, 21);
            btnBrowseWorkingCopy.Name = "btnBrowseWorkingCopy";
            btnBrowseWorkingCopy.Size = new Size(33, 25);
            btnBrowseWorkingCopy.TabIndex = 2;
            btnBrowseWorkingCopy.Text = "...";
            btnBrowseWorkingCopy.Click += btnBrowseWorkingCopy_Click;
            // 
            // lblSourceRepo
            // 
            lblSourceRepo.Location = new Point(8, 56);
            lblSourceRepo.Name = "lblSourceRepo";
            lblSourceRepo.Size = new Size(100, 20);
            lblSourceRepo.TabIndex = 3;
            lblSourceRepo.Text = "Source Repo:";
            // 
            // txtSourceRepo
            // 
            txtSourceRepo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSourceRepo.Location = new Point(112, 54);
            txtSourceRepo.Name = "txtSourceRepo";
            txtSourceRepo.Size = new Size(340, 26);
            txtSourceRepo.TabIndex = 4;
            // 
            // btnBrowseSourceRepo
            // 
            btnBrowseSourceRepo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseSourceRepo.Location = new Point(456, 53);
            btnBrowseSourceRepo.Name = "btnBrowseSourceRepo";
            btnBrowseSourceRepo.Size = new Size(33, 25);
            btnBrowseSourceRepo.TabIndex = 5;
            btnBrowseSourceRepo.Text = "...";
            btnBrowseSourceRepo.Click += btnBrowseSourceRepo_Click;
            // 
            // btnSvnConnect
            // 
            btnSvnConnect.Location = new Point(112, 90);
            btnSvnConnect.Name = "btnSvnConnect";
            btnSvnConnect.Size = new Size(130, 28);
            btnSvnConnect.TabIndex = 6;
            btnSvnConnect.Text = "SVN Connect";
            btnSvnConnect.Click += btnSvnConnect_Click;
            // 
            // btnSvnUpdate
            // 
            btnSvnUpdate.Location = new Point(250, 90);
            btnSvnUpdate.Name = "btnSvnUpdate";
            btnSvnUpdate.Size = new Size(130, 28);
            btnSvnUpdate.TabIndex = 7;
            btnSvnUpdate.Text = "SVN Update";
            btnSvnUpdate.Click += btnSvnUpdate_Click;
            // 
            // grpConfig
            // 
            grpConfig.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            grpConfig.Controls.Add(lblConfigLabel);
            grpConfig.Controls.Add(txtConfigLabel);
            grpConfig.Controls.Add(btnSaveConfig);
            grpConfig.Controls.Add(btnLoadConfig);
            grpConfig.Controls.Add(lblMode);
            grpConfig.Controls.Add(cmbMode);
            grpConfig.Location = new Point(514, 12);
            grpConfig.Name = "grpConfig";
            grpConfig.Size = new Size(358, 136);
            grpConfig.TabIndex = 1;
            grpConfig.TabStop = false;
            grpConfig.Text = "⚙️ Config";
            // 
            // lblConfigLabel
            // 
            lblConfigLabel.Location = new Point(8, 24);
            lblConfigLabel.Name = "lblConfigLabel";
            lblConfigLabel.Size = new Size(70, 20);
            lblConfigLabel.TabIndex = 0;
            lblConfigLabel.Text = "Etichetta:";
            // 
            // txtConfigLabel
            // 
            txtConfigLabel.Location = new Point(82, 22);
            txtConfigLabel.Name = "txtConfigLabel";
            txtConfigLabel.Size = new Size(270, 26);
            txtConfigLabel.TabIndex = 1;
            // 
            // btnSaveConfig
            // 
            btnSaveConfig.Location = new Point(82, 60);
            btnSaveConfig.Name = "btnSaveConfig";
            btnSaveConfig.Size = new Size(126, 28);
            btnSaveConfig.TabIndex = 2;
            btnSaveConfig.Text = "Salva Config";
            btnSaveConfig.Click += btnSaveConfig_Click;
            // 
            // btnLoadConfig
            // 
            btnLoadConfig.Location = new Point(214, 60);
            btnLoadConfig.Name = "btnLoadConfig";
            btnLoadConfig.Size = new Size(138, 28);
            btnLoadConfig.TabIndex = 3;
            btnLoadConfig.Text = "Carica Config";
            btnLoadConfig.Click += btnLoadConfig_Click;
            // 
            // lblMode
            // 
            lblMode.Location = new Point(8, 105);
            lblMode.Name = "lblMode";
            lblMode.Size = new Size(70, 20);
            lblMode.TabIndex = 4;
            lblMode.Text = "Modalità:";
            // 
            // cmbMode
            // 
            cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMode.Location = new Point(82, 101);
            cmbMode.Name = "cmbMode";
            cmbMode.Size = new Size(120, 27);
            cmbMode.TabIndex = 5;
            // 
            // grpElaborazioni
            // 
            grpElaborazioni.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpElaborazioni.Controls.Add(grpParametri);
            grpElaborazioni.Controls.Add(grpOutputCfg);
            grpElaborazioni.Location = new Point(8, 154);
            grpElaborazioni.Name = "grpElaborazioni";
            grpElaborazioni.Size = new Size(882, 177);
            grpElaborazioni.TabIndex = 2;
            grpElaborazioni.TabStop = false;
            grpElaborazioni.Text = "🔄 Elaborazioni";
            // 
            // grpParametri
            // 
            grpParametri.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpParametri.Controls.Add(lblSkipRevisions);
            grpParametri.Controls.Add(txtSkipRevisions);
            grpParametri.Controls.Add(lblIssues);
            grpParametri.Controls.Add(txtIssues);
            grpParametri.Controls.Add(lblRevisions);
            grpParametri.Controls.Add(txtRevisions);
            grpParametri.Controls.Add(lblMaxNewRevs);
            grpParametri.Controls.Add(numMaxNewRevs);
            grpParametri.Location = new Point(8, 16);
            grpParametri.Name = "grpParametri";
            grpParametri.Size = new Size(440, 157);
            grpParametri.TabIndex = 0;
            grpParametri.TabStop = false;
            grpParametri.Text = "Parametri";
            // 
            // lblSkipRevisions
            // 
            lblSkipRevisions.Font = new Font("Segoe UI", 9F);
            lblSkipRevisions.Location = new Point(5, 124);
            lblSkipRevisions.Name = "lblSkipRevisions";
            lblSkipRevisions.Size = new Size(72, 20);
            lblSkipRevisions.TabIndex = 10;
            lblSkipRevisions.Text = "Revs  skip:";
            // 
            // txtSkipRevisions
            // 
            txtSkipRevisions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSkipRevisions.Location = new Point(80, 121);
            txtSkipRevisions.Name = "txtSkipRevisions";
            txtSkipRevisions.PlaceholderText = "(Opzionale) es. 284490, 284715";
            txtSkipRevisions.Size = new Size(344, 26);
            txtSkipRevisions.TabIndex = 11;
            // 
            // lblIssues
            // 
            lblIssues.Location = new Point(8, 24);
            lblIssues.Name = "lblIssues";
            lblIssues.Size = new Size(70, 20);
            lblIssues.TabIndex = 0;
            lblIssues.Text = "Issues:";
            // 
            // txtIssues
            // 
            txtIssues.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtIssues.Location = new Point(80, 22);
            txtIssues.Name = "txtIssues";
            txtIssues.PlaceholderText = "es. APKHTRSU-693, APKHTIMU-203";
            txtIssues.Size = new Size(344, 26);
            txtIssues.TabIndex = 1;
            // 
            // lblRevisions
            // 
            lblRevisions.Location = new Point(8, 56);
            lblRevisions.Name = "lblRevisions";
            lblRevisions.Size = new Size(70, 20);
            lblRevisions.TabIndex = 2;
            lblRevisions.Text = "Revisioni:";
            // 
            // txtRevisions
            // 
            txtRevisions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtRevisions.Location = new Point(80, 54);
            txtRevisions.Name = "txtRevisions";
            txtRevisions.PlaceholderText = "es. 12345, 12348";
            txtRevisions.Size = new Size(344, 26);
            txtRevisions.TabIndex = 3;
            // 
            // lblMaxNewRevs
            // 
            lblMaxNewRevs.Location = new Point(8, 90);
            lblMaxNewRevs.Name = "lblMaxNewRevs";
            lblMaxNewRevs.Size = new Size(70, 20);
            lblMaxNewRevs.TabIndex = 4;
            lblMaxNewRevs.Text = "Max Revs:";
            // 
            // numMaxNewRevs
            // 
            numMaxNewRevs.Location = new Point(80, 88);
            numMaxNewRevs.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            numMaxNewRevs.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numMaxNewRevs.Name = "numMaxNewRevs";
            numMaxNewRevs.Size = new Size(100, 26);
            numMaxNewRevs.TabIndex = 5;
            numMaxNewRevs.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // grpOutputCfg
            // 
            grpOutputCfg.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            grpOutputCfg.Controls.Add(lblOutFile);
            grpOutputCfg.Controls.Add(txtOutFile);
            grpOutputCfg.Controls.Add(btnBrowseOutFile);
            grpOutputCfg.Controls.Add(lblResultType);
            grpOutputCfg.Controls.Add(cmbResultType);
            grpOutputCfg.Controls.Add(lblGroupBy);
            grpOutputCfg.Controls.Add(cmbGroupBy);
            grpOutputCfg.Location = new Point(456, 16);
            grpOutputCfg.Name = "grpOutputCfg";
            grpOutputCfg.Size = new Size(418, 157);
            grpOutputCfg.TabIndex = 1;
            grpOutputCfg.TabStop = false;
            grpOutputCfg.Text = "Output";
            // 
            // lblOutFile
            // 
            lblOutFile.Location = new Point(8, 24);
            lblOutFile.Name = "lblOutFile";
            lblOutFile.Size = new Size(80, 20);
            lblOutFile.TabIndex = 0;
            lblOutFile.Text = "File output:";
            // 
            // txtOutFile
            // 
            txtOutFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtOutFile.Location = new Point(92, 22);
            txtOutFile.Name = "txtOutFile";
            txtOutFile.Size = new Size(230, 26);
            txtOutFile.TabIndex = 1;
            // 
            // btnBrowseOutFile
            // 
            btnBrowseOutFile.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseOutFile.Location = new Point(328, 21);
            btnBrowseOutFile.Name = "btnBrowseOutFile";
            btnBrowseOutFile.Size = new Size(80, 25);
            btnBrowseOutFile.TabIndex = 2;
            btnBrowseOutFile.Text = "Salva in...";
            btnBrowseOutFile.Click += btnBrowseOutFile_Click;
            // 
            // lblResultType
            // 
            lblResultType.Location = new Point(8, 58);
            lblResultType.Name = "lblResultType";
            lblResultType.Size = new Size(80, 20);
            lblResultType.TabIndex = 3;
            lblResultType.Text = "Visualizza:";
            // 
            // cmbResultType
            // 
            cmbResultType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbResultType.Location = new Point(92, 57);
            cmbResultType.Name = "cmbResultType";
            cmbResultType.Size = new Size(200, 27);
            cmbResultType.TabIndex = 4;
            cmbResultType.SelectedIndexChanged += cmbResultType_SelectedIndexChanged;
            // 
            // lblGroupBy
            // 
            lblGroupBy.Location = new Point(8, 95);
            lblGroupBy.Name = "lblGroupBy";
            lblGroupBy.Size = new Size(80, 20);
            lblGroupBy.TabIndex = 5;
            lblGroupBy.Text = "  Vedi per:";
            lblGroupBy.Visible = false;
            // 
            // cmbGroupBy
            // 
            cmbGroupBy.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbGroupBy.Location = new Point(92, 92);
            cmbGroupBy.Name = "cmbGroupBy";
            cmbGroupBy.Size = new Size(160, 27);
            cmbGroupBy.TabIndex = 6;
            cmbGroupBy.Visible = false;
            cmbGroupBy.SelectedIndexChanged += cmbGroupBy_SelectedIndexChanged;
            // 
            // grpRun
            // 
            grpRun.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpRun.Controls.Add(btnRun);
            grpRun.Controls.Add(progressBar);
            grpRun.Location = new Point(8, 337);
            grpRun.Name = "grpRun";
            grpRun.Size = new Size(882, 56);
            grpRun.TabIndex = 3;
            grpRun.TabStop = false;
            grpRun.Text = "⚡ Esegui";
            // 
            // btnRun
            // 
            btnRun.Font = new Font("Segoe UI", 9.163636F, FontStyle.Bold);
            btnRun.Location = new Point(8, 18);
            btnRun.Name = "btnRun";
            btnRun.Size = new Size(180, 28);
            btnRun.TabIndex = 0;
            btnRun.Text = "▶  Avvia Analisi SVN";
            btnRun.Click += btnRun_Click;
            // 
            // progressBar
            // 
            progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Location = new Point(200, 20);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(670, 24);
            progressBar.TabIndex = 1;
            progressBar.Visible = false;
            // 
            // grpResult
            // 
            grpResult.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grpResult.Controls.Add(rtbOutput);
            grpResult.Location = new Point(8, 399);
            grpResult.Name = "grpResult";
            grpResult.Size = new Size(882, 357);
            grpResult.TabIndex = 4;
            grpResult.TabStop = false;
            grpResult.Text = "📊 Risultato";
            // 
            // rtbOutput
            // 
            rtbOutput.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            rtbOutput.Font = new Font("Consolas", 9F);
            rtbOutput.Location = new Point(8, 18);
            rtbOutput.Name = "rtbOutput";
            rtbOutput.ReadOnly = true;
            rtbOutput.Size = new Size(862, 327);
            rtbOutput.TabIndex = 0;
            rtbOutput.Text = "";
            rtbOutput.WordWrap = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 19F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(906, 768);
            Controls.Add(grpDirectory);
            Controls.Add(grpConfig);
            Controls.Add(grpElaborazioni);
            Controls.Add(grpRun);
            Controls.Add(grpResult);
            MinimumSize = new Size(920, 720);
            Name = "Form1";
            Text = "SVN Merge Checker";
            grpDirectory.ResumeLayout(false);
            grpDirectory.PerformLayout();
            grpConfig.ResumeLayout(false);
            grpConfig.PerformLayout();
            grpElaborazioni.ResumeLayout(false);
            grpParametri.ResumeLayout(false);
            grpParametri.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numMaxNewRevs).EndInit();
            grpOutputCfg.ResumeLayout(false);
            grpOutputCfg.PerformLayout();
            grpRun.ResumeLayout(false);
            grpResult.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        // Directory section
        private GroupBox grpDirectory;
        private Label lblWorkingCopy;
        private TextBox txtWorkingCopy;
        private Button btnBrowseWorkingCopy;
        private Label lblSourceRepo;
        private TextBox txtSourceRepo;
        private Button btnBrowseSourceRepo;
        private Button btnSvnConnect;
        private Button btnSvnUpdate;

        // Config section
        private GroupBox grpConfig;
        private Label lblConfigLabel;
        private TextBox txtConfigLabel;
        private Button btnSaveConfig;
        private Button btnLoadConfig;
        private Label lblMode;

        // Elaborazioni section
        private GroupBox grpElaborazioni;
        private GroupBox grpParametri;
        private Label lblIssues;
        private TextBox txtIssues;
        private Label lblRevisions;
        private TextBox txtRevisions;
        private Label lblMaxNewRevs;
        private NumericUpDown numMaxNewRevs;
        private GroupBox grpOutputCfg;
        private Label lblOutFile;
        private TextBox txtOutFile;
        private Button btnBrowseOutFile;
        private Label lblResultType;
        private ComboBox cmbResultType;
        private Label lblGroupBy;
        private ComboBox cmbGroupBy;

        // Run section
        private GroupBox grpRun;
        private Button btnRun;
        private ComboBox cmbMode;
        private ProgressBar progressBar;

        // Result section
        private GroupBox grpResult;
        private RichTextBox rtbOutput;
        private Label lblSkipRevisions;
        private TextBox txtSkipRevisions;
    }
}
