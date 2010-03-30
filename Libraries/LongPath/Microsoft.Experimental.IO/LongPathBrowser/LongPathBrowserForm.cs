//     Copyright (c) Microsoft Corporation.  All rights reserved.
using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Experimental.IO;

namespace LongPathSamples {
    internal partial class LongPathBrowserForm : Form {

        public LongPathBrowserForm() {
            InitializeComponent();
        }

        private void OnSelectButtonClick(object sender, EventArgs e) {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog()) {

                if (dialog.ShowDialog() == DialogResult.OK) {

                    this.currentPath.Text = dialog.SelectedPath;

                    RefreshFileList();
                }

            }
        }

        private void OnDeleteButtonClick(object sender, EventArgs e) {

            string selectedPath = (string)fileSystemEntriesListBox.SelectedItem;
            try {
                if (LongPathDirectory.Exists(selectedPath)) {
                    LongPathDirectory.Delete(selectedPath);
                }
                else {
                    LongPathFile.Delete(selectedPath);
                }
            }
            catch (IOException ex) {
                ShowError(ex);
            }
            catch (UnauthorizedAccessException ex) {
                ShowError(ex);
            }

            RefreshFileList();
        }

        private void RefreshFileList() {
            var paths = LongPathDirectory.EnumerateFileSystemEntries(this.currentPath.Text);

            fileSystemEntriesListBox.Items.Clear();
            fileSystemEntriesListBox.Items.AddRange(paths.ToArray());
        }

        private void OnCreateDirectoryButtonClick(object sender, EventArgs e) {

            try {
                string path = Path.Combine(this.currentPath.Text, fileNameTextBox.Text);
                LongPathDirectory.Create(path);
            }
            catch (ArgumentException ex) {
                ShowError(ex);
            }
            catch (IOException ex) {
                ShowError(ex);
            }
            catch (UnauthorizedAccessException ex) {
                ShowError(ex);
            }

            RefreshFileList();
        }

        private void OnCreateFileButtonClick(object sender, EventArgs e) {
            
            try {

                string path = Path.Combine(this.currentPath.Text, fileNameTextBox.Text);
                using (FileStream fs = LongPathFile.Open(path, FileMode.Create, FileAccess.Read)) {
                }
            }
            catch (ArgumentException ex) {
                ShowError(ex);
            }
            catch (IOException ex) {
                ShowError(ex);
            }
            catch (UnauthorizedAccessException ex) {
                ShowError(ex);
            }

            RefreshFileList();
        }

        private void OnFileSystemEntriesListBoxSelectedIndexChanged(object sender, EventArgs e) {

            if (fileSystemEntriesListBox.SelectedItem != null) {
                Clipboard.SetText((string)fileSystemEntriesListBox.SelectedItem);
            }
        }

        private static void ShowError(Exception ex) {
            MessageBox.Show(ex.Message);
        }

        private void OnFileSystemEntriesListBoxMouseDoubleClick(object sender, MouseEventArgs e) {
            if (fileSystemEntriesListBox.SelectedItem != null) {

                string path = (string)fileSystemEntriesListBox.SelectedItem;

                if (LongPathDirectory.Exists(path)) {
                    this.currentPath.Text = path;
                }

                RefreshFileList();
            }
        }
    }
}