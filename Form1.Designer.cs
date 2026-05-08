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
            this.lblHandle = new System.Windows.Forms.Label();
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
            this.btnSendMouseUp.Text = "发送鼠标上移 100";
            this.btnSendMouseUp.UseVisualStyleBackColor = true;
            this.btnSendMouseUp.Click += new System.EventHandler(this.btnSendMouseUp_Click);
            // 
            // lblHandle
            // 
            this.lblHandle.AutoSize = true;
            this.lblHandle.Location = new System.Drawing.Point(12, 54);
            this.lblHandle.Name = "lblHandle";
            this.lblHandle.Size = new System.Drawing.Size(120, 15);
            this.lblHandle.TabIndex = 2;
            this.lblHandle.Text = "选中窗口句柄: (无)";
            // 
            // Form1
            // 
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(this.btnSendMouseUp);
            Controls.Add(this.btnSelectWindow);
            Controls.Add(this.lblHandle);
            Name = "Form1";
            Text = "Form1";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Button btnSelectWindow;
        private System.Windows.Forms.Button btnSendMouseUp;
        private System.Windows.Forms.Label lblHandle;

        #endregion
    }
}
