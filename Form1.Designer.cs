namespace YOLOForAim
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            this.btnSelectWindow = new System.Windows.Forms.Button();
            this.btnSendMouseUp = new System.Windows.Forms.Button();
            this.btnStartDetection = new System.Windows.Forms.Button();
            this.btnStopDetection = new System.Windows.Forms.Button();
            this.chkCenterRoi = new System.Windows.Forms.CheckBox();
            this.numRoiSize = new System.Windows.Forms.NumericUpDown();
            this.chkPreferGpu = new System.Windows.Forms.CheckBox();
            this.numPreviewInterval = new System.Windows.Forms.NumericUpDown();
            this.lblPreviewInterval = new System.Windows.Forms.Label();
            this.lblHandle = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.pictureBoxPreview = new System.Windows.Forms.PictureBox();
            this.txtDiagnostics = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.numRoiSize)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPreviewInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPreview)).BeginInit();
            this.SuspendLayout();
            // 
            // btnSelectWindow
            // 
            this.btnSelectWindow.Location = new System.Drawing.Point(12, 12);
            this.btnSelectWindow.Name = "btnSelectWindow";
            this.btnSelectWindow.Size = new System.Drawing.Size(160, 30);
            this.btnSelectWindow.TabIndex = 0;
            this.btnSelectWindow.Text = "选择窗口 (点击/回车确认)";
            this.btnSelectWindow.UseVisualStyleBackColor = true;
            this.btnSelectWindow.Click += new System.EventHandler(this.btnSelectWindow_Click);
            // 
            // btnSendMouseUp
            // 
            this.btnSendMouseUp.Location = new System.Drawing.Point(190, 12);
            this.btnSendMouseUp.Name = "btnSendMouseUp";
            this.btnSendMouseUp.Size = new System.Drawing.Size(180, 30);
            this.btnSendMouseUp.TabIndex = 1;
            this.btnSendMouseUp.Text = "切换检测开关 (Z)";
            this.btnSendMouseUp.UseVisualStyleBackColor = true;
            this.btnSendMouseUp.Click += new System.EventHandler(this.btnSendMouseUp_Click);
            // 
            // btnStartDetection
            // 
            this.btnStartDetection.Location = new System.Drawing.Point(390, 12);
            this.btnStartDetection.Name = "btnStartDetection";
            this.btnStartDetection.Size = new System.Drawing.Size(120, 30);
            this.btnStartDetection.TabIndex = 2;
            this.btnStartDetection.Text = "开始实时检测";
            this.btnStartDetection.UseVisualStyleBackColor = true;
            this.btnStartDetection.Click += new System.EventHandler(this.btnStartDetection_Click);
            // 
            // btnStopDetection
            // 
            this.btnStopDetection.Enabled = false;
            this.btnStopDetection.Location = new System.Drawing.Point(528, 12);
            this.btnStopDetection.Name = "btnStopDetection";
            this.btnStopDetection.Size = new System.Drawing.Size(120, 30);
            this.btnStopDetection.TabIndex = 3;
            this.btnStopDetection.Text = "停止检测";
            this.btnStopDetection.UseVisualStyleBackColor = true;
            this.btnStopDetection.Click += new System.EventHandler(this.btnStopDetection_Click);
            // 
            // chkCenterRoi
            // 
            this.chkCenterRoi.AutoSize = true;
            this.chkCenterRoi.Checked = false;
            this.chkCenterRoi.Location = new System.Drawing.Point(12, 82);
            this.chkCenterRoi.Name = "chkCenterRoi";
            this.chkCenterRoi.Size = new System.Drawing.Size(102, 19);
            this.chkCenterRoi.TabIndex = 4;
            this.chkCenterRoi.Text = "仅截中心 ROI";
            this.chkCenterRoi.UseVisualStyleBackColor = true;
            // 
            // numRoiSize
            // 
            this.numRoiSize.Increment = new decimal(new int[] {
            64,
            0,
            0,
            0});
            this.numRoiSize.Location = new System.Drawing.Point(120, 80);
            this.numRoiSize.Maximum = new decimal(new int[] {
            1600,
            0,
            0,
            0});
            this.numRoiSize.Minimum = new decimal(new int[] {
            128,
            0,
            0,
            0});
            this.numRoiSize.Name = "numRoiSize";
            this.numRoiSize.Size = new System.Drawing.Size(78, 23);
            this.numRoiSize.TabIndex = 5;
            this.numRoiSize.Value = new decimal(new int[] {
            128,
            0,
            0,
            0});
            // 
            // chkPreferGpu
            // 
            this.chkPreferGpu.AutoSize = true;
            this.chkPreferGpu.Location = new System.Drawing.Point(220, 82);
            this.chkPreferGpu.Name = "chkPreferGpu";
            this.chkPreferGpu.Size = new System.Drawing.Size(142, 19);
            this.chkPreferGpu.TabIndex = 6;
            this.chkPreferGpu.Text = "优先使用 GPU(DML)";
            this.chkPreferGpu.UseVisualStyleBackColor = true;
            // 
            // numPreviewInterval
            // 
            this.numPreviewInterval.Location = new System.Drawing.Point(478, 80);
            this.numPreviewInterval.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numPreviewInterval.Name = "numPreviewInterval";
            this.numPreviewInterval.Size = new System.Drawing.Size(54, 23);
            this.numPreviewInterval.TabIndex = 7;
            this.numPreviewInterval.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // lblPreviewInterval
            // 
            this.lblPreviewInterval.AutoSize = true;
            this.lblPreviewInterval.Location = new System.Drawing.Point(368, 82);
            this.lblPreviewInterval.Name = "lblPreviewInterval";
            this.lblPreviewInterval.Size = new System.Drawing.Size(104, 15);
            this.lblPreviewInterval.TabIndex = 8;
            this.lblPreviewInterval.Text = "预览刷新间隔(帧)";
            // 
            // lblHandle
            // 
            this.lblHandle.AutoSize = true;
            this.lblHandle.Location = new System.Drawing.Point(12, 51);
            this.lblHandle.Name = "lblHandle";
            this.lblHandle.Size = new System.Drawing.Size(120, 15);
            this.lblHandle.TabIndex = 9;
            this.lblHandle.Text = "选中窗口句柄: (无)";
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(12, 110);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(43, 15);
            this.lblStatus.TabIndex = 10;
            this.lblStatus.Text = "未启动";
            // 
            // pictureBoxPreview
            // 
            this.pictureBoxPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pictureBoxPreview.Location = new System.Drawing.Point(12, 137);
            this.pictureBoxPreview.Name = "pictureBoxPreview";
            this.pictureBoxPreview.Size = new System.Drawing.Size(776, 330);
            this.pictureBoxPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxPreview.TabIndex = 11;
            this.pictureBoxPreview.TabStop = false;
            // 
            // txtDiagnostics
            // 
            this.txtDiagnostics.Location = new System.Drawing.Point(12, 473);
            this.txtDiagnostics.Multiline = true;
            this.txtDiagnostics.Name = "txtDiagnostics";
            this.txtDiagnostics.ReadOnly = true;
            this.txtDiagnostics.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtDiagnostics.Size = new System.Drawing.Size(776, 154);
            this.txtDiagnostics.TabIndex = 12;
            // 
            // Form1
            // 
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 640);
            Controls.Add(this.txtDiagnostics);
            Controls.Add(this.pictureBoxPreview);
            Controls.Add(this.lblStatus);
            Controls.Add(this.lblPreviewInterval);
            Controls.Add(this.numPreviewInterval);
            Controls.Add(this.chkPreferGpu);
            Controls.Add(this.numRoiSize);
            Controls.Add(this.chkCenterRoi);
            Controls.Add(this.btnStopDetection);
            Controls.Add(this.btnStartDetection);
            Controls.Add(this.btnSendMouseUp);
            Controls.Add(this.btnSelectWindow);
            Controls.Add(this.lblHandle);
            Name = "Form1";
            Text = "YOLO 实时检测";
            ((System.ComponentModel.ISupportInitialize)(this.numRoiSize)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPreviewInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPreview)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Button btnSelectWindow;
        private System.Windows.Forms.Button btnSendMouseUp;
        private System.Windows.Forms.Button btnStartDetection;
        private System.Windows.Forms.Button btnStopDetection;
        private System.Windows.Forms.CheckBox chkCenterRoi;
        private System.Windows.Forms.NumericUpDown numRoiSize;
        private System.Windows.Forms.CheckBox chkPreferGpu;
        private System.Windows.Forms.NumericUpDown numPreviewInterval;
        private System.Windows.Forms.Label lblPreviewInterval;
        private System.Windows.Forms.Label lblHandle;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.PictureBox pictureBoxPreview;
        private System.Windows.Forms.TextBox txtDiagnostics;

        #endregion
    }
}
