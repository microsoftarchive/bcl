//     Copyright (c) Microsoft Corporation.  All rights reserved.
using System;
using System.Windows.Forms;

namespace LongPathSamples {
    internal static class LongPathBrowser {
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LongPathBrowserForm());
        }
    }
}