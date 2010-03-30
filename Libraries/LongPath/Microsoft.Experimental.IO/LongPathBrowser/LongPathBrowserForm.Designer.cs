//     Copyright (c) Microsoft Corporation.  All rights reserved.
namespace LongPathSamples {

    partial class LongPathBrowserForm {
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            this.selectButton = new System.Windows.Forms.Button();
            this.deleteButton = new System.Windows.Forms.Button();
            this.createDirButton = new System.Windows.Forms.Button();
            this.createFileButton = new System.Windows.Forms.Button();
            this.fileNameTextBox = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.fileSystemEntriesListBox = new System.Windows.Forms.ListBox();
            this.label1 = new System.Windows.Forms.Label();
            this.currentPath = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // selectButton
            // 
            this.selectButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.selectButton.Location = new System.Drawing.Point(480, 593);
            this.selectButton.Name = "selectButton";
            this.selectButton.Size = new System.Drawing.Size(97, 23);
            this.selectButton.TabIndex = 2;
            this.selectButton.Text = "Select Folder";
            this.selectButton.UseVisualStyleBackColor = true;
            this.selectButton.Click += new System.EventHandler(this.OnSelectButtonClick);
            // 
            // deleteButton
            // 
            this.deleteButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.deleteButton.Location = new System.Drawing.Point(766, 593);
            this.deleteButton.Name = "deleteButton";
            this.deleteButton.Size = new System.Drawing.Size(75, 23);
            this.deleteButton.TabIndex = 3;
            this.deleteButton.Text = "Delete";
            this.deleteButton.UseVisualStyleBackColor = true;
            this.deleteButton.Click += new System.EventHandler(this.OnDeleteButtonClick);
            // 
            // createDirButton
            // 
            this.createDirButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.createDirButton.Location = new System.Drawing.Point(583, 593);
            this.createDirButton.Name = "createDirButton";
            this.createDirButton.Size = new System.Drawing.Size(97, 23);
            this.createDirButton.TabIndex = 6;
            this.createDirButton.Text = "Create Folder";
            this.createDirButton.UseVisualStyleBackColor = true;
            this.createDirButton.Click += new System.EventHandler(this.OnCreateDirectoryButtonClick);
            // 
            // createFileButton
            // 
            this.createFileButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.createFileButton.Location = new System.Drawing.Point(686, 593);
            this.createFileButton.Name = "createFileButton";
            this.createFileButton.Size = new System.Drawing.Size(75, 23);
            this.createFileButton.TabIndex = 7;
            this.createFileButton.Text = "Create File";
            this.createFileButton.UseVisualStyleBackColor = true;
            this.createFileButton.Click += new System.EventHandler(this.OnCreateFileButtonClick);
            // 
            // fileNameTextBox
            // 
            this.fileNameTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.fileNameTextBox.Location = new System.Drawing.Point(126, 509);
            this.fileNameTextBox.Name = "fileNameTextBox";
            this.fileNameTextBox.Size = new System.Drawing.Size(715, 20);
            this.fileNameTextBox.TabIndex = 8;
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 512);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(105, 13);
            this.label2.TabIndex = 9;
            this.label2.Text = "File/Directory name: ";
            // 
            // fileSystemEntriesListBox
            // 
            this.fileSystemEntriesListBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.fileSystemEntriesListBox.FormattingEnabled = true;
            this.fileSystemEntriesListBox.HorizontalScrollbar = true;
            this.fileSystemEntriesListBox.Location = new System.Drawing.Point(12, 31);
            this.fileSystemEntriesListBox.Name = "fileSystemEntriesListBox";
            this.fileSystemEntriesListBox.Size = new System.Drawing.Size(829, 459);
            this.fileSystemEntriesListBox.TabIndex = 4;
            this.fileSystemEntriesListBox.SelectedIndexChanged += new System.EventHandler(this.OnFileSystemEntriesListBoxSelectedIndexChanged);
            this.fileSystemEntriesListBox.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.OnFileSystemEntriesListBoxMouseDoubleClick);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(15, 12);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(89, 13);
            this.label1.TabIndex = 10;
            this.label1.Text = "Current Directory:";
            // 
            // currentPath
            // 
            this.currentPath.AutoSize = true;
            this.currentPath.Location = new System.Drawing.Point(110, 12);
            this.currentPath.Name = "currentPath";
            this.currentPath.Size = new System.Drawing.Size(0, 13);
            this.currentPath.TabIndex = 11;
            // 
            // LongPathBrowserForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(853, 625);
            this.Controls.Add(this.currentPath);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.fileNameTextBox);
            this.Controls.Add(this.createFileButton);
            this.Controls.Add(this.createDirButton);
            this.Controls.Add(this.fileSystemEntriesListBox);
            this.Controls.Add(this.deleteButton);
            this.Controls.Add(this.selectButton);
            this.Name = "LongPathBrowserForm";
            this.ShowIcon = false;
            this.Text = "Long Path Browser";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Button selectButton;
        private System.Windows.Forms.Button deleteButton;
        private System.Windows.Forms.Button createDirButton;
        private System.Windows.Forms.Button createFileButton;
        private System.Windows.Forms.TextBox fileNameTextBox;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ListBox fileSystemEntriesListBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label currentPath;
    }
}

