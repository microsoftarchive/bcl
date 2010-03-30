//     Copyright (c) Microsoft Corporation.  All rights reserved.
using System.Diagnostics;
using Utilities;

// Welcome to the PerfMonitor code base. This _README.cs file is your table of contents.
// 
// You will notice that the code is littered with code: qualifiers.  If you install the 'hyperAddin' for
// Visual Studio, these qualifers turn into hyperlinks that allow easy cross references.  The hyperAddin is
// available on http://www.codeplex.com/hyperAddin
// 
// -------------------------------------------------------------------------------------
// Overview of files
// 
// PerfMonitor Specific:
// 
// * file:PerfMonitor.cs - The main program. This is a relatively thin wrapper over code:TraceEventSession (for
//     conrolling ETW data collection and code:ETWTraceEventSource (for processing the data).
//     
// * file:UsersGuide.htm - The file that is displayed when you run perfMonitor usersGuide
// 
// * file:ColdStartup.cs - currently experimental.  Utilities for collecting cold startup performance
//     information.  
//
//--------------------------------------------------------------------------------------
//
// see also 
// 
// * file:utilities\_Readme.cs for more on general purpose utilities 
// * file:..\TraceEvent\_Readme.cs for more on the TraceEvent project.  
// 
