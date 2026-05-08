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
            this.lblScoreThreshold = new System.Windows.Forms.Label();
            this.numScoreThreshold = new System.Windows.Forms.NumericUpDown();
            this.numPreviewInterval = new System.Windows.Forms.NumericUpDown();
            this.lblPreviewInterval = new System.Windows.Forms.Label();
            this.lblAimHeightPercent = new System.Windows.Forms.Label();
            this.numAimHeightPercent = new System.Windows.Forms.NumericUpDown();
            this.lblAimDeadzone = new System.Windows.Forms.Label();
            this.numAimDeadzone = new System.Windows.Forms.NumericUpDown();
            this.lblAimSmoothing = new System.Windows.Forms.Label();
            this.numAimSmoothing = new System.Windows.Forms.NumericUpDown();
            this.lblAimSpeedMultiplier = new System.Windows.Forms.Label();
            this.numAimSpeedMultiplier = new System.Windows.Forms.NumericUpDown();
            this.lblAimMaxStep = new System.Windows.Forms.Label();
            this.numAimMaxStep = new System.Windows.Forms.NumericUpDown();
            this.lblAimSwitchDistance = new System.Windows.Forms.Label();
            this.numAimSwitchDistance = new System.Windows.Forms.NumericUpDown();
            this.lblAimMaxMissedFrames = new System.Windows.Forms.Label();
            this.numAimMaxMissedFrames = new System.Windows.Forms.NumericUpDown();
            this.lblHandle = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.pictureBoxPreview = new System.Windows.Forms.PictureBox();
            this.txtDiagnostics = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.numScoreThreshold)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numRoiSize)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPreviewInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimHeightPercent)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimDeadzone)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimSmoothing)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimSpeedMultiplier)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimMaxStep)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimSwitchDistance)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimMaxMissedFrames)).BeginInit();
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
            // lblScoreThreshold
            // 
            this.lblScoreThreshold.AutoSize = true;
            this.lblScoreThreshold.Location = new System.Drawing.Point(368, 82);
            this.lblScoreThreshold.Name = "lblScoreThreshold";
            this.lblScoreThreshold.Size = new System.Drawing.Size(83, 15);
            this.lblScoreThreshold.TabIndex = 7;
            this.lblScoreThreshold.Text = "检测阈值(%)";
            // 
            // numScoreThreshold
            // 
            this.numScoreThreshold.Location = new System.Drawing.Point(457, 80);
            this.numScoreThreshold.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numScoreThreshold.Name = "numScoreThreshold";
            this.numScoreThreshold.Size = new System.Drawing.Size(54, 23);
            this.numScoreThreshold.TabIndex = 8;
            this.numScoreThreshold.Value = new decimal(new int[] {
            35,
            0,
            0,
            0});
            // 
            // numPreviewInterval
            // 
            this.numPreviewInterval.Location = new System.Drawing.Point(734, 80);
            this.numPreviewInterval.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numPreviewInterval.Name = "numPreviewInterval";
            this.numPreviewInterval.Size = new System.Drawing.Size(54, 23);
            this.numPreviewInterval.TabIndex = 9;
            this.numPreviewInterval.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // lblPreviewInterval
            // 
            this.lblPreviewInterval.AutoSize = true;
            this.lblPreviewInterval.Location = new System.Drawing.Point(624, 82);
            this.lblPreviewInterval.Name = "lblPreviewInterval";
            this.lblPreviewInterval.Size = new System.Drawing.Size(104, 15);
            this.lblPreviewInterval.TabIndex = 10;
            this.lblPreviewInterval.Text = "预览刷新间隔(帧)";
            // 
            // lblAimHeightPercent
            // 
            this.lblAimHeightPercent.AutoSize = true;
            this.lblAimHeightPercent.Location = new System.Drawing.Point(12, 110);
            this.lblAimHeightPercent.Name = "lblAimHeightPercent";
            this.lblAimHeightPercent.Size = new System.Drawing.Size(95, 15);
            this.lblAimHeightPercent.TabIndex = 9;
            this.lblAimHeightPercent.Text = "瞄准高度(%)";
            // 
            // numAimHeightPercent
            // 
            this.numAimHeightPercent.Location = new System.Drawing.Point(113, 108);
            this.numAimHeightPercent.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numAimHeightPercent.Name = "numAimHeightPercent";
            this.numAimHeightPercent.Size = new System.Drawing.Size(56, 23);
            this.numAimHeightPercent.TabIndex = 10;
            this.numAimHeightPercent.Value = new decimal(new int[] {
            20,
            0,
            0,
            0});
            // 
            // lblAimDeadzone
            // 
            this.lblAimDeadzone.AutoSize = true;
            this.lblAimDeadzone.Location = new System.Drawing.Point(181, 110);
            this.lblAimDeadzone.Name = "lblAimDeadzone";
            this.lblAimDeadzone.Size = new System.Drawing.Size(71, 15);
            this.lblAimDeadzone.TabIndex = 11;
            this.lblAimDeadzone.Text = "停止范围";
            // 
            // numAimDeadzone
            // 
            this.numAimDeadzone.Location = new System.Drawing.Point(258, 108);
            this.numAimDeadzone.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numAimDeadzone.Name = "numAimDeadzone";
            this.numAimDeadzone.Size = new System.Drawing.Size(56, 23);
            this.numAimDeadzone.TabIndex = 12;
            this.numAimDeadzone.Value = new decimal(new int[] {
            12,
            0,
            0,
            0});
            // 
            // lblAimSmoothing
            // 
            this.lblAimSmoothing.AutoSize = true;
            this.lblAimSmoothing.Location = new System.Drawing.Point(326, 110);
            this.lblAimSmoothing.Name = "lblAimSmoothing";
            this.lblAimSmoothing.Size = new System.Drawing.Size(83, 15);
            this.lblAimSmoothing.TabIndex = 13;
            this.lblAimSmoothing.Text = "平滑强度(%)";
            // 
            // numAimSmoothing
            // 
            this.numAimSmoothing.Location = new System.Drawing.Point(415, 108);
            this.numAimSmoothing.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numAimSmoothing.Name = "numAimSmoothing";
            this.numAimSmoothing.Size = new System.Drawing.Size(56, 23);
            this.numAimSmoothing.TabIndex = 14;
            this.numAimSmoothing.Value = new decimal(new int[] {
            35,
            0,
            0,
            0});
            // 
            // lblAimSpeedMultiplier
            // 
            this.lblAimSpeedMultiplier.AutoSize = true;
            this.lblAimSpeedMultiplier.Location = new System.Drawing.Point(483, 110);
            this.lblAimSpeedMultiplier.Name = "lblAimSpeedMultiplier";
            this.lblAimSpeedMultiplier.Size = new System.Drawing.Size(83, 15);
            this.lblAimSpeedMultiplier.TabIndex = 15;
            this.lblAimSpeedMultiplier.Text = "速度倍率(%)";
            // 
            // numAimSpeedMultiplier
            // 
            this.numAimSpeedMultiplier.Location = new System.Drawing.Point(572, 108);
            this.numAimSpeedMultiplier.Maximum = new decimal(new int[] {
            500,
            0,
            0,
            0});
            this.numAimSpeedMultiplier.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numAimSpeedMultiplier.Name = "numAimSpeedMultiplier";
            this.numAimSpeedMultiplier.Size = new System.Drawing.Size(56, 23);
            this.numAimSpeedMultiplier.TabIndex = 16;
            this.numAimSpeedMultiplier.Value = new decimal(new int[] {
            100,
            0,
            0,
            0});
            // 
            // lblAimMaxStep
            // 
            this.lblAimMaxStep.AutoSize = true;
            this.lblAimMaxStep.Location = new System.Drawing.Point(640, 110);
            this.lblAimMaxStep.Name = "lblAimMaxStep";
            this.lblAimMaxStep.Size = new System.Drawing.Size(71, 15);
            this.lblAimMaxStep.TabIndex = 17;
            this.lblAimMaxStep.Text = "单次上限";
            // 
            // numAimMaxStep
            // 
            this.numAimMaxStep.Location = new System.Drawing.Point(717, 108);
            this.numAimMaxStep.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numAimMaxStep.Name = "numAimMaxStep";
            this.numAimMaxStep.Size = new System.Drawing.Size(56, 23);
            this.numAimMaxStep.TabIndex = 18;
            this.numAimMaxStep.Value = new decimal(new int[] {
            36,
            0,
            0,
            0});
            // 
            // lblAimSwitchDistance
            // 
            this.lblAimSwitchDistance.AutoSize = true;
            this.lblAimSwitchDistance.Location = new System.Drawing.Point(12, 139);
            this.lblAimSwitchDistance.Name = "lblAimSwitchDistance";
            this.lblAimSwitchDistance.Size = new System.Drawing.Size(95, 15);
            this.lblAimSwitchDistance.TabIndex = 19;
            this.lblAimSwitchDistance.Text = "切换距离阈值";
            // 
            // numAimSwitchDistance
            // 
            this.numAimSwitchDistance.Location = new System.Drawing.Point(113, 137);
            this.numAimSwitchDistance.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.numAimSwitchDistance.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numAimSwitchDistance.Name = "numAimSwitchDistance";
            this.numAimSwitchDistance.Size = new System.Drawing.Size(56, 23);
            this.numAimSwitchDistance.TabIndex = 20;
            this.numAimSwitchDistance.Value = new decimal(new int[] {
            140,
            0,
            0,
            0});
            // 
            // lblAimMaxMissedFrames
            // 
            this.lblAimMaxMissedFrames.AutoSize = true;
            this.lblAimMaxMissedFrames.Location = new System.Drawing.Point(181, 139);
            this.lblAimMaxMissedFrames.Name = "lblAimMaxMissedFrames";
            this.lblAimMaxMissedFrames.Size = new System.Drawing.Size(71, 15);
            this.lblAimMaxMissedFrames.TabIndex = 21;
            this.lblAimMaxMissedFrames.Text = "容忍丢帧";
            // 
            // numAimMaxMissedFrames
            // 
            this.numAimMaxMissedFrames.Location = new System.Drawing.Point(258, 137);
            this.numAimMaxMissedFrames.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numAimMaxMissedFrames.Name = "numAimMaxMissedFrames";
            this.numAimMaxMissedFrames.Size = new System.Drawing.Size(56, 23);
            this.numAimMaxMissedFrames.TabIndex = 22;
            this.numAimMaxMissedFrames.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            // 
            // lblHandle
            // 
            this.lblHandle.AutoSize = true;
            this.lblHandle.Location = new System.Drawing.Point(12, 51);
            this.lblHandle.Name = "lblHandle";
            this.lblHandle.Size = new System.Drawing.Size(120, 15);
            this.lblHandle.TabIndex = 21;
            this.lblHandle.Text = "选中窗口句柄: (无)";
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(12, 169);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(43, 15);
            this.lblStatus.TabIndex = 22;
            this.lblStatus.Text = "未启动";
            // 
            // pictureBoxPreview
            // 
            this.pictureBoxPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pictureBoxPreview.Location = new System.Drawing.Point(12, 196);
            this.pictureBoxPreview.Name = "pictureBoxPreview";
            this.pictureBoxPreview.Size = new System.Drawing.Size(776, 330);
            this.pictureBoxPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxPreview.TabIndex = 23;
            this.pictureBoxPreview.TabStop = false;
            // 
            // txtDiagnostics
            // 
            this.txtDiagnostics.Location = new System.Drawing.Point(12, 532);
            this.txtDiagnostics.Multiline = true;
            this.txtDiagnostics.Name = "txtDiagnostics";
            this.txtDiagnostics.ReadOnly = true;
            this.txtDiagnostics.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtDiagnostics.Size = new System.Drawing.Size(776, 154);
            this.txtDiagnostics.TabIndex = 24;
            // 
            // Form1
            // 
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 700);
            Controls.Add(this.txtDiagnostics);
            Controls.Add(this.pictureBoxPreview);
            Controls.Add(this.lblStatus);
            Controls.Add(this.numScoreThreshold);
            Controls.Add(this.lblScoreThreshold);
            Controls.Add(this.numAimSpeedMultiplier);
            Controls.Add(this.lblAimSpeedMultiplier);
            Controls.Add(this.numAimMaxMissedFrames);
            Controls.Add(this.lblAimMaxMissedFrames);
            Controls.Add(this.numAimSwitchDistance);
            Controls.Add(this.lblAimSwitchDistance);
            Controls.Add(this.numAimMaxStep);
            Controls.Add(this.lblAimMaxStep);
            Controls.Add(this.numAimSmoothing);
            Controls.Add(this.lblAimSmoothing);
            Controls.Add(this.numAimDeadzone);
            Controls.Add(this.lblAimDeadzone);
            Controls.Add(this.numAimHeightPercent);
            Controls.Add(this.lblAimHeightPercent);
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
            ((System.ComponentModel.ISupportInitialize)(this.numScoreThreshold)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numRoiSize)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPreviewInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimHeightPercent)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimDeadzone)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimSmoothing)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimSpeedMultiplier)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimMaxStep)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimSwitchDistance)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAimMaxMissedFrames)).EndInit();
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
        private System.Windows.Forms.Label lblScoreThreshold;
        private System.Windows.Forms.NumericUpDown numScoreThreshold;
        private System.Windows.Forms.NumericUpDown numPreviewInterval;
        private System.Windows.Forms.Label lblPreviewInterval;
        private System.Windows.Forms.Label lblAimHeightPercent;
        private System.Windows.Forms.NumericUpDown numAimHeightPercent;
        private System.Windows.Forms.Label lblAimDeadzone;
        private System.Windows.Forms.NumericUpDown numAimDeadzone;
        private System.Windows.Forms.Label lblAimSmoothing;
        private System.Windows.Forms.NumericUpDown numAimSmoothing;
        private System.Windows.Forms.Label lblAimSpeedMultiplier;
        private System.Windows.Forms.NumericUpDown numAimSpeedMultiplier;
        private System.Windows.Forms.Label lblAimMaxStep;
        private System.Windows.Forms.NumericUpDown numAimMaxStep;
        private System.Windows.Forms.Label lblAimSwitchDistance;
        private System.Windows.Forms.NumericUpDown numAimSwitchDistance;
        private System.Windows.Forms.Label lblAimMaxMissedFrames;
        private System.Windows.Forms.NumericUpDown numAimMaxMissedFrames;
        private System.Windows.Forms.Label lblHandle;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.PictureBox pictureBoxPreview;
        private System.Windows.Forms.TextBox txtDiagnostics;

        #endregion
    }
}
