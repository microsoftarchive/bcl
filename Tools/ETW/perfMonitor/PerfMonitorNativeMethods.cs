//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Security;
using Diagnostics.Tracing;

// This moduleFile contains Internal PINVOKE declarations and has no public API surface. 
namespace Diagnostics.Tracing
{
    #region Private Classes

    /// <summary>
    /// PerfMonitorNativeMethods contains the PINVOKE declarations needed
    /// to get at the Win32 TraceEvent infrastructure.  It is effectively
    /// a port of evntrace.h to C# declarations.  
    /// </summary>
    [SecuritySafeCritical]
    internal unsafe static class PerfMonitorNativeMethods
    {
        [DllImport("KernelTraceControl.dll", CharSet = CharSet.Unicode), SuppressUnmanagedCodeSecurityAttribute]
        internal extern static int CreateMergedTraceFile(
            string wszMergedFileName,
            string[] wszTraceFileNames,
            int cTraceFileNames,
            EVENT_TRACE_MERGE_EXTENDED_DATA dwExtendedDataFlags);

        // Flags to save extended information to the ETW trace file
        [Flags]
        public enum EVENT_TRACE_MERGE_EXTENDED_DATA
        {
            NONE = 0x00,
            IMAGEID = 0x01,
            BUILDINFO = 0x02,
            VOLUME_MAPPING = 0x04,
            WINSAT = 0x08,
            EVENT_METADATA = 0x10,
        }

    } // end class
    #endregion
}
