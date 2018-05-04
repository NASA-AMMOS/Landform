namespace PairViewer
{
    partial class PairViewerForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openModelMenu = new System.Windows.Forms.ToolStripMenuItem();
            this.openDataMenu = new System.Windows.Forms.ToolStripMenuItem();
            this.exitMenu = new System.Windows.Forms.ToolStripMenuItem();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.ModelPictureBox = new System.Windows.Forms.PictureBox();
            this.DataPictureBox = new System.Windows.Forms.PictureBox();
            this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.menuStrip1.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ModelPictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.DataPictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(928, 24);
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openModelMenu,
            this.openDataMenu,
            this.exitMenu});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "File";
            // 
            // openModelMenu
            // 
            this.openModelMenu.Name = "openModelMenu";
            this.openModelMenu.Size = new System.Drawing.Size(140, 22);
            this.openModelMenu.Text = "Open Model";
            this.openModelMenu.Click += new System.EventHandler(this.openModelMenu_Click);
            // 
            // openDataMenu
            // 
            this.openDataMenu.Name = "openDataMenu";
            this.openDataMenu.Size = new System.Drawing.Size(140, 22);
            this.openDataMenu.Text = "Open Data";
            this.openDataMenu.Click += new System.EventHandler(this.openDataMenu_Click);
            // 
            // exitMenu
            // 
            this.exitMenu.Name = "exitMenu";
            this.exitMenu.Size = new System.Drawing.Size(140, 22);
            this.exitMenu.Text = "Exit";
            this.exitMenu.Click += new System.EventHandler(this.exitMenu_Click);
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel1});
            this.statusStrip1.Location = new System.Drawing.Point(0, 653);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(928, 22);
            this.statusStrip1.TabIndex = 1;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 24);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.ModelPictureBox);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.DataPictureBox);
            this.splitContainer1.Size = new System.Drawing.Size(928, 629);
            this.splitContainer1.SplitterDistance = 467;
            this.splitContainer1.TabIndex = 2;
            // 
            // ModelPictureBox
            // 
            this.ModelPictureBox.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.ModelPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ModelPictureBox.Location = new System.Drawing.Point(0, 0);
            this.ModelPictureBox.Name = "ModelPictureBox";
            this.ModelPictureBox.Size = new System.Drawing.Size(467, 629);
            this.ModelPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.ModelPictureBox.TabIndex = 0;
            this.ModelPictureBox.TabStop = false;
            this.ModelPictureBox.Click += new System.EventHandler(this.ModelPictureBox_Click);
            // 
            // DataPictureBox
            // 
            this.DataPictureBox.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.DataPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DataPictureBox.Location = new System.Drawing.Point(0, 0);
            this.DataPictureBox.Name = "DataPictureBox";
            this.DataPictureBox.Size = new System.Drawing.Size(457, 629);
            this.DataPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.DataPictureBox.TabIndex = 0;
            this.DataPictureBox.TabStop = false;
            this.DataPictureBox.Click += new System.EventHandler(this.DataPictureBox_Click);
            // 
            // toolStripStatusLabel1
            // 
            this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            this.toolStripStatusLabel1.Size = new System.Drawing.Size(118, 17);
            this.toolStripStatusLabel1.Text = "toolStripStatusLabel1";
            // 
            // PairViewerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(928, 675);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "PairViewerForm";
            this.Text = "PairViewer";
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ModelPictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.DataPictureBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openModelMenu;
        private System.Windows.Forms.ToolStripMenuItem exitMenu;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.PictureBox ModelPictureBox;
        private System.Windows.Forms.PictureBox DataPictureBox;
        private System.Windows.Forms.ToolStripMenuItem openDataMenu;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
    }
}

