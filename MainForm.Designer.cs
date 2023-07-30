namespace GrzBeamProfiler
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose( bool disposing )
        {
            if ( disposing && ( components != null ) )
            {
                components.Dispose( );
            }
            base.Dispose( disposing );
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent( )
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.devicesCombo = new System.Windows.Forms.ComboBox();
            this.videoResolutionsCombo = new System.Windows.Forms.ComboBox();
            this.connectButton = new System.Windows.Forms.Button();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.hScrollBarBrightness = new System.Windows.Forms.HScrollBar();
            this.hScrollBarExposure = new System.Windows.Forms.HScrollBar();
            this.snapshotButton = new System.Windows.Forms.Button();
            this.buttonSettings = new System.Windows.Forms.Button();
            this.buttonProperties = new System.Windows.Forms.Button();
            this.timerUpdateHeadline = new System.Windows.Forms.Timer(this.components);
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.pictureBoxPseudo = new System.Windows.Forms.PictureBox();
            this.panel4 = new System.Windows.Forms.Panel();
            this.buttonCaptureBackground = new System.Windows.Forms.Button();
            this.panelGraphs = new System.Windows.Forms.Panel();
            this.tableLayoutPanelGraphs = new System.Windows.Forms.TableLayoutPanel();
            this.labelCameraExposure = new System.Windows.Forms.Label();
            this.labelImageBrightness = new System.Windows.Forms.Label();
            this.timerStillImage = new System.Windows.Forms.Timer(this.components);
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPseudo)).BeginInit();
            this.panel4.SuspendLayout();
            this.panelGraphs.SuspendLayout();
            this.SuspendLayout();
            // 
            // devicesCombo
            // 
            this.devicesCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.devicesCombo.FormattingEnabled = true;
            this.devicesCombo.Location = new System.Drawing.Point(62, 5);
            this.devicesCombo.Margin = new System.Windows.Forms.Padding(3, 5, 3, 3);
            this.devicesCombo.Name = "devicesCombo";
            this.devicesCombo.Size = new System.Drawing.Size(132, 21);
            this.devicesCombo.TabIndex = 1;
            this.devicesCombo.SelectedIndexChanged += new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
            this.devicesCombo.Click += new System.EventHandler(this.devicesCombo_Click);
            // 
            // videoResolutionsCombo
            // 
            this.videoResolutionsCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.videoResolutionsCombo.FormattingEnabled = true;
            this.videoResolutionsCombo.Location = new System.Drawing.Point(62, 35);
            this.videoResolutionsCombo.Margin = new System.Windows.Forms.Padding(3, 5, 3, 3);
            this.videoResolutionsCombo.Name = "videoResolutionsCombo";
            this.videoResolutionsCombo.Size = new System.Drawing.Size(132, 21);
            this.videoResolutionsCombo.TabIndex = 3;
            this.videoResolutionsCombo.SelectedIndexChanged += new System.EventHandler(this.videoResolutionsCombo_SelectedIndexChanged);
            this.videoResolutionsCombo.Click += new System.EventHandler(this.videoResolutionsCombo_Click);
            // 
            // connectButton
            // 
            this.connectButton.Location = new System.Drawing.Point(448, 3);
            this.connectButton.Name = "connectButton";
            this.tableLayoutPanel1.SetRowSpan(this.connectButton, 2);
            this.connectButton.Size = new System.Drawing.Size(74, 54);
            this.connectButton.TabIndex = 6;
            this.connectButton.Text = "&connect";
            this.toolTip.SetToolTip(this.connectButton, "connect to camera");
            this.connectButton.UseVisualStyleBackColor = true;
            this.connectButton.Click += new System.EventHandler(this.connectButton_Click);
            // 
            // toolTip
            // 
            this.toolTip.AutoPopDelay = 5000;
            this.toolTip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(255)))), ((int)(((byte)(192)))));
            this.toolTip.InitialDelay = 100;
            this.toolTip.ReshowDelay = 100;
            // 
            // hScrollBarBrightness
            // 
            this.hScrollBarBrightness.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.hScrollBarBrightness.LargeChange = 1;
            this.hScrollBarBrightness.Location = new System.Drawing.Point(312, 35);
            this.hScrollBarBrightness.Margin = new System.Windows.Forms.Padding(0, 5, 7, 0);
            this.hScrollBarBrightness.Maximum = 10;
            this.hScrollBarBrightness.Minimum = -10;
            this.hScrollBarBrightness.Name = "hScrollBarBrightness";
            this.hScrollBarBrightness.Size = new System.Drawing.Size(126, 17);
            this.hScrollBarBrightness.TabIndex = 13;
            this.toolTip.SetToolTip(this.hScrollBarBrightness, "camera brightness");
            this.hScrollBarBrightness.Value = -6;
            this.hScrollBarBrightness.Scroll += new System.Windows.Forms.ScrollEventHandler(this.hScrollBarBrightness_Scroll);
            // 
            // hScrollBarExposure
            // 
            this.hScrollBarExposure.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.hScrollBarExposure.LargeChange = 1;
            this.hScrollBarExposure.Location = new System.Drawing.Point(312, 5);
            this.hScrollBarExposure.Margin = new System.Windows.Forms.Padding(0, 5, 7, 0);
            this.hScrollBarExposure.Maximum = 1;
            this.hScrollBarExposure.Minimum = -10;
            this.hScrollBarExposure.Name = "hScrollBarExposure";
            this.hScrollBarExposure.Size = new System.Drawing.Size(126, 17);
            this.hScrollBarExposure.TabIndex = 11;
            this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure");
            this.hScrollBarExposure.Value = -5;
            this.hScrollBarExposure.Scroll += new System.Windows.Forms.ScrollEventHandler(this.hScrollBarExposure_Scroll);
            // 
            // snapshotButton
            // 
            this.snapshotButton.Location = new System.Drawing.Point(16, 1);
            this.snapshotButton.Margin = new System.Windows.Forms.Padding(10, 3, 3, 3);
            this.snapshotButton.Name = "snapshotButton";
            this.snapshotButton.Size = new System.Drawing.Size(91, 27);
            this.snapshotButton.TabIndex = 9;
            this.snapshotButton.Text = "snapshot";
            this.toolTip.SetToolTip(this.snapshotButton, "capture a snapshot image");
            this.snapshotButton.UseVisualStyleBackColor = true;
            this.snapshotButton.Click += new System.EventHandler(this.snapshotButton_Click);
            // 
            // buttonSettings
            // 
            this.buttonSettings.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonSettings.BackgroundImage")));
            this.buttonSettings.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.buttonSettings.Location = new System.Drawing.Point(525, 30);
            this.buttonSettings.Margin = new System.Windows.Forms.Padding(0);
            this.buttonSettings.Name = "buttonSettings";
            this.buttonSettings.Size = new System.Drawing.Size(35, 27);
            this.buttonSettings.TabIndex = 0;
            this.toolTip.SetToolTip(this.buttonSettings, "app settings");
            this.buttonSettings.UseVisualStyleBackColor = true;
            this.buttonSettings.Click += new System.EventHandler(this.buttonSettings_Click);
            // 
            // buttonProperties
            // 
            this.buttonProperties.Location = new System.Drawing.Point(3, 3);
            this.buttonProperties.Name = "buttonProperties";
            this.tableLayoutPanel1.SetRowSpan(this.buttonProperties, 2);
            this.buttonProperties.Size = new System.Drawing.Size(53, 53);
            this.buttonProperties.TabIndex = 10;
            this.buttonProperties.Text = "camera settings";
            this.buttonProperties.UseVisualStyleBackColor = true;
            this.buttonProperties.Click += new System.EventHandler(this.buttonProperties_Click);
            // 
            // timerUpdateHeadline
            // 
            this.timerUpdateHeadline.Enabled = true;
            this.timerUpdateHeadline.Interval = 1000;
            this.timerUpdateHeadline.Tick += new System.EventHandler(this.timerUpdateHeadline_Tick);
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 7;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 59F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 138F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 115F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 133F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.hScrollBarBrightness, 3, 1);
            this.tableLayoutPanel1.Controls.Add(this.buttonSettings, 5, 1);
            this.tableLayoutPanel1.Controls.Add(this.buttonProperties, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.hScrollBarExposure, 3, 0);
            this.tableLayoutPanel1.Controls.Add(this.pictureBoxPseudo, 5, 0);
            this.tableLayoutPanel1.Controls.Add(this.panel4, 6, 1);
            this.tableLayoutPanel1.Controls.Add(this.videoResolutionsCombo, 1, 1);
            this.tableLayoutPanel1.Controls.Add(this.devicesCombo, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.panelGraphs, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.connectButton, 4, 0);
            this.tableLayoutPanel1.Controls.Add(this.labelCameraExposure, 2, 0);
            this.tableLayoutPanel1.Controls.Add(this.labelImageBrightness, 2, 1);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 3;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(900, 656);
            this.tableLayoutPanel1.TabIndex = 11;
            // 
            // pictureBoxPseudo
            // 
            this.tableLayoutPanel1.SetColumnSpan(this.pictureBoxPseudo, 2);
            this.pictureBoxPseudo.Location = new System.Drawing.Point(528, 3);
            this.pictureBoxPseudo.Name = "pictureBoxPseudo";
            this.pictureBoxPseudo.Size = new System.Drawing.Size(248, 24);
            this.pictureBoxPseudo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxPseudo.TabIndex = 12;
            this.pictureBoxPseudo.TabStop = false;
            this.pictureBoxPseudo.Paint += new System.Windows.Forms.PaintEventHandler(this.pictureBoxPseudo_Paint);
            // 
            // panel4
            // 
            this.panel4.Controls.Add(this.buttonCaptureBackground);
            this.panel4.Controls.Add(this.snapshotButton);
            this.panel4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel4.Location = new System.Drawing.Point(565, 30);
            this.panel4.Margin = new System.Windows.Forms.Padding(0);
            this.panel4.Name = "panel4";
            this.panel4.Size = new System.Drawing.Size(335, 30);
            this.panel4.TabIndex = 19;
            // 
            // buttonCaptureBackground
            // 
            this.buttonCaptureBackground.Location = new System.Drawing.Point(121, 1);
            this.buttonCaptureBackground.Name = "buttonCaptureBackground";
            this.buttonCaptureBackground.Size = new System.Drawing.Size(91, 27);
            this.buttonCaptureBackground.TabIndex = 1;
            this.buttonCaptureBackground.Text = "get background";
            this.buttonCaptureBackground.UseVisualStyleBackColor = true;
            this.buttonCaptureBackground.Click += new System.EventHandler(this.buttonCaptureBackground_Click);
            // 
            // panelGraphs
            // 
            this.tableLayoutPanel1.SetColumnSpan(this.panelGraphs, 7);
            this.panelGraphs.Controls.Add(this.tableLayoutPanelGraphs);
            this.panelGraphs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelGraphs.Location = new System.Drawing.Point(0, 60);
            this.panelGraphs.Margin = new System.Windows.Forms.Padding(0);
            this.panelGraphs.Name = "panelGraphs";
            this.panelGraphs.Size = new System.Drawing.Size(900, 596);
            this.panelGraphs.TabIndex = 20;
            // 
            // tableLayoutPanelGraphs
            // 
            this.tableLayoutPanelGraphs.ColumnCount = 2;
            this.tableLayoutPanelGraphs.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 104F));
            this.tableLayoutPanelGraphs.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelGraphs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanelGraphs.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanelGraphs.Margin = new System.Windows.Forms.Padding(0);
            this.tableLayoutPanelGraphs.Name = "tableLayoutPanelGraphs";
            this.tableLayoutPanelGraphs.RowCount = 2;
            this.tableLayoutPanelGraphs.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelGraphs.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 104F));
            this.tableLayoutPanelGraphs.Size = new System.Drawing.Size(900, 596);
            this.tableLayoutPanelGraphs.TabIndex = 13;
            this.tableLayoutPanelGraphs.Paint += new System.Windows.Forms.PaintEventHandler(this.tableLayoutPanelGraphs_Paint);
            // 
            // labelCameraExposure
            // 
            this.labelCameraExposure.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labelCameraExposure.AutoSize = true;
            this.labelCameraExposure.Location = new System.Drawing.Point(200, 8);
            this.labelCameraExposure.Margin = new System.Windows.Forms.Padding(3, 8, 3, 0);
            this.labelCameraExposure.Name = "labelCameraExposure";
            this.labelCameraExposure.Size = new System.Drawing.Size(109, 13);
            this.labelCameraExposure.TabIndex = 21;
            this.labelCameraExposure.Text = "camera exposure: -10";
            // 
            // labelImageBrightness
            // 
            this.labelImageBrightness.AutoSize = true;
            this.labelImageBrightness.Location = new System.Drawing.Point(200, 36);
            this.labelImageBrightness.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.labelImageBrightness.Name = "labelImageBrightness";
            this.labelImageBrightness.Size = new System.Drawing.Size(107, 13);
            this.labelImageBrightness.TabIndex = 22;
            this.labelImageBrightness.Text = "image brightness: -10";
            // 
            // timerStillImage
            // 
            this.timerStillImage.Interval = 300;
            this.timerStillImage.Tick += new System.EventHandler(this.timerStillImage_Tick);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 656);
            this.Controls.Add(this.tableLayoutPanel1);
            this.DoubleBuffered = true;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(689, 578);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.Resize += new System.EventHandler(this.MainForm_Resize);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPseudo)).EndInit();
            this.panel4.ResumeLayout(false);
            this.panelGraphs.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox devicesCombo;
        private System.Windows.Forms.ComboBox videoResolutionsCombo;
        private System.Windows.Forms.Button connectButton;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Button snapshotButton;
        private System.Windows.Forms.Button buttonProperties;
        private System.Windows.Forms.Timer timerUpdateHeadline;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.HScrollBar hScrollBarExposure;
        private System.Windows.Forms.HScrollBar hScrollBarBrightness;
        private System.Windows.Forms.PictureBox pictureBoxPseudo;
        private System.Windows.Forms.Panel panel4;
        private System.Windows.Forms.Button buttonSettings;
        private System.Windows.Forms.Panel panelGraphs;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelGraphs;
        private System.Windows.Forms.Timer timerStillImage;
        private System.Windows.Forms.Button buttonCaptureBackground;
        private System.Windows.Forms.Label labelCameraExposure;
        private System.Windows.Forms.Label labelImageBrightness;
        //        private System.Windows.Forms.PictureBox pictureBox;
    }
}

