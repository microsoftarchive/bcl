using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Diagnostics.Tracing;
using Microsoft.Win32;
using PerfMonitor;
using Symbols;
using Utilities;
using Address = System.UInt64;
using Diagnostics.Tracing.Parsers;

// This is subsetted from the PerfView code.   TODO factor this properly...
// I have saved the baseline that I used to generat this file (by subsetting) in CommandProcessor.baseline.
// Thus you can do a 3 way merge of changes to PerfView's version of this file and thus pick up updates.  
namespace PerfView
{
    /// <summary>
    /// CommandProcessor knows how to take a CommandLineArgs and do basic operations 
    /// that are NOT gui dependent.  
    /// </summary>
    public class CommandProcessor
    {
        public CommandProcessor() { }
        public int ExecuteCommand(CommandLineArgs parsedArgs)
        {
            try
            {
                parsedArgs.DoCommand(parsedArgs);
                return 0;
            }
            catch (ThreadInterruptedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                bool userLevel;
                var message = ExceptionMessage.GetUserMessage(ex, out userLevel);
                LogFile.WriteLine("[{0}]", message);
                return 1;
            }
        }
        public TextWriter LogFile
        {
            get { return m_logFile; }
            set { m_logFile = value; }
        }

        // Command line commands.  
        public void Run(CommandLineArgs parsedArgs)
        {
            // TODO can we simpify?
            // Find the command from the command line and see if we need to wrap it in cmd /c 
            var exeName = GetExeName(parsedArgs.CommandLine);

            // Add the support directory to the path so you get the tutorial examples. 
            if (!s_addedSupportDirToPath)
            {
                Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + ";" + SupportFiles.SupportFileDir);
                s_addedSupportDirToPath = true;
            }
            var exeFullPath = Command.FindOnPath(exeName);

            if (string.Compare(Path.GetExtension(exeFullPath), ".exe", StringComparison.OrdinalIgnoreCase) == 0)
                parsedArgs.Process = Path.GetFileNameWithoutExtension(exeFullPath);

            if (exeFullPath == null && string.Compare(exeName, "start", StringComparison.OrdinalIgnoreCase) != 0)
                throw new FileNotFoundException("Could not find command " + exeName + " on path.");

            var fullCmdLine = parsedArgs.CommandLine;
            if (string.Compare(Path.GetExtension(exeFullPath), ".exe", StringComparison.OrdinalIgnoreCase) != 0 ||
                fullCmdLine.IndexOfAny(new char[] { '<', '>', '&' }) >= 0)   // File redirection ...
                fullCmdLine = "cmd /c call " + parsedArgs.CommandLine;

            // OK actually do the work.
            parsedArgs.NoRundown = true;
            bool success = false;
            Command cmd = null;
            try
            {
                Start(parsedArgs);
                Thread.Sleep(100);          // Allow time for the start rundown events OS events to happen.  
                DateTime startTime = DateTime.Now;
                LogFile.WriteLine("Starting at {0}", startTime);
                // TODO allow users to specify the launch directory
                LogFile.WriteLine("Current Directory {0}", Environment.CurrentDirectory);
                LogFile.WriteLine("Executing: {0} {{", fullCmdLine);

                // Options:  add support dir to path so that tutorial examples work.  
                var options = new CommandOptions().AddNoThrow().AddTimeout(CommandOptions.Infinite).AddOutputStream(LogFile);
                cmd = new Command(fullCmdLine, options);

                // We break this up so that on thread interrrupted exceptions can happen
                while (!cmd.HasExited)
                {
                    if (parsedArgs.MaxCollectSec != 0 && (DateTime.Now - startTime).TotalSeconds > parsedArgs.MaxCollectSec)
                    {
                        LogFile.WriteLine("Exceeded the maximum collection time of {0} sec.", parsedArgs.MaxCollectSec);
                        parsedArgs.NoRundown = false;
                        break;
                    }
                    Thread.Sleep(200);
                }

                DateTime stopTime = DateTime.Now;
                LogFile.WriteLine("}} Stopping at {0} = {1:f3} sec", stopTime, (stopTime - startTime).TotalSeconds);
                if (cmd.ExitCode != 0)
                    LogFile.WriteLine("Warning: Command exited with non-success error code 0x{0:x}", cmd.ExitCode);
                Stop(parsedArgs);

                if (!cmd.HasExited)
                    cmd.Kill();
                success = true;
            }
            finally
            {
                if (!success)
                {
                    if (cmd != null)
                        cmd.Kill();
                    Abort(parsedArgs);
                }
            }
        }
        public void Collect(CommandLineArgs parsedArgs)
        {
            parsedArgs.Process = null;

            // When you collect we ALWAYS use circular buffer mode (too dangerous otherwise).
            // Users can use a very large number if they want 'infinity'.  
            if (parsedArgs.Circular == 0)
            {
                LogFile.WriteLine("Circular buffer size = 0, setting to 500.");
                parsedArgs.Circular = 500;
            }

            bool success = false;
            try
            {
                Start(parsedArgs);
                DateTime startTime = DateTime.Now;
                LogFile.WriteLine("Starting at {0}", DateTime.Now);

                WaitForStopNoGui(parsedArgs, startTime);

                DateTime stopTime = DateTime.Now;
                LogFile.WriteLine("Stopping at {0} = {1:f3} sec", stopTime, (stopTime - startTime).TotalSeconds);
                Stop(parsedArgs);
                success = true;
            }
            finally
            {
                if (!success)
                    Abort(parsedArgs);
            }
        }

        public void Start(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";

            // The DataFile does not have the .zip associated with it (it is implied)
            if (parsedArgs.DataFile.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
                parsedArgs.DataFile = parsedArgs.DataFile.Substring(0, parsedArgs.DataFile.Length - 4);

            // Are we on an X86 machine?
            if ((Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "AMD64" ||
                 Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") == "AMD64"))
            {
                if (!IsKernelStacks64Enabled())
                {
                    var ver = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
                    if (ver <= 61)
                    {
                        LogFile.WriteLine("Warning: This trace is being collected on a X64 machine on a Pre Win8 OS");
                        LogFile.WriteLine("         And paging is allowed in the kernel.  This can cause stack breakage");
                        LogFile.WriteLine("         when samples are taken in the kernel and there is memory pressure.");
                        LogFile.WriteLine("         It is recommended that you disable paging in the kernel to decrease");
                        LogFile.WriteLine("         the number of broken stacks.   To do this run the command:");
                        LogFile.WriteLine("");
                        LogFile.WriteLine("         PerfMonitor /EnableKernelStacks ");
                        LogFile.WriteLine("");
                        LogFile.WriteLine("         A reboot will be required for the change to have an effect.");
                        LogFile.WriteLine("");
                    }
                }
            }

            string userFileName = Path.ChangeExtension(parsedArgs.DataFile, ".etl");
            string kernelFileName = Path.ChangeExtension(parsedArgs.DataFile, ".kernel.etl");
            string rundownFileName = Path.ChangeExtension(parsedArgs.DataFile, ".clrRundown.etl");

            // Insure that old data is gone (BASENAME.*.etl?)
            var dirName = Path.GetDirectoryName(parsedArgs.DataFile);
            if (dirName.Length == 0)
                dirName = ".";
            var fileNames = Directory.GetFiles(dirName, Path.GetFileNameWithoutExtension(parsedArgs.DataFile) + "*.etl?");
            try
            {
                foreach (var fileName in fileNames)
                    FileUtilities.ForceDelete(fileName);
            }
            catch (IOException)
            {
                LogFile.WriteLine("Files in use, aborting and trying again.");
                Abort(parsedArgs);
                foreach (var fileName in fileNames)
                    FileUtilities.ForceDelete(fileName);
            }

            // Create the sessions
            TraceEventSession kernelModeSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName, kernelFileName);
            LogFile.WriteLine("Kernel keywords enabled: {0}", parsedArgs.KernelEvents);
            if (parsedArgs.KernelEvents != KernelTraceEventParser.Keywords.None)
            {
                if ((parsedArgs.KernelEvents & (KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad)) == 0 &&
                    (parsedArgs.KernelEvents & (KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.ContextSwitch)) != 0)
                {
                    LogFile.WriteLine("Kernel process and image thread events not present, adding them");
                    parsedArgs.KernelEvents |= (
                        KernelTraceEventParser.Keywords.Process |
                        KernelTraceEventParser.Keywords.ImageLoad |
                        KernelTraceEventParser.Keywords.Thread);
                }

                LogFile.WriteLine("[Kernel Log: {0}]", Path.GetFullPath(kernelFileName));
                kernelModeSession.BufferSizeMB = parsedArgs.BufferSize;
                if (parsedArgs.Circular != 0)
                    kernelModeSession.CircularBufferMB = parsedArgs.Circular;
                kernelModeSession.StopOnDispose = true; // Don't leave running on errors 
                kernelModeSession.EnableKernelProvider(parsedArgs.KernelEvents, parsedArgs.KernelEvents);
            }

            LogFile.WriteLine("[User mode Log: {0}]", Path.GetFullPath(userFileName));
            TraceEventSession userModeSession = new TraceEventSession(s_UserModeSessionName, userFileName);
            userModeSession.BufferSizeMB = parsedArgs.BufferSize;
            // Note that you don't need the rundown 300Meg if you are V4.0.   
            if (parsedArgs.Circular != 0)
            {
                // Typcially you only need less than 1/5 the space + rundown 
                userModeSession.CircularBufferMB = Math.Min(parsedArgs.Circular, parsedArgs.Circular / 5 + 300);
            }
            userModeSession.StopOnDispose = true;   // Don't leave running on errors

            // Turn on PerfViewLogger
            userModeSession.EnableProvider(PerfViewLogger.Log.Guid);
            Thread.Sleep(100);  // Give it at least some time to start, it is not synchronous. 

            PerfViewLogger.Log.StartTracing();
            PerfViewLogger.StartTime = DateTime.UtcNow;

            PerfViewLogger.Log.SessionParameters(KernelTraceEventParser.KernelSessionName, kernelFileName,
                kernelModeSession.BufferSizeMB, kernelModeSession.CircularBufferMB);
            PerfViewLogger.Log.KernelEnableParameters(parsedArgs.KernelEvents, parsedArgs.KernelEvents);
            PerfViewLogger.Log.SessionParameters(s_UserModeSessionName, userFileName,
                userModeSession.BufferSizeMB, userModeSession.CircularBufferMB);

            if (parsedArgs.ClrEvents != ClrTraceEventParser.Keywords.None)
            {
                // If we don't change the core set then we should assume the user wants more stuff.  
                var coreClrEvents = ClrTraceEventParser.Keywords.Default &
                    ~ClrTraceEventParser.Keywords.NGen & ~ClrTraceEventParser.Keywords.SupressNGen;

                if ((parsedArgs.ClrEvents & coreClrEvents) == coreClrEvents)
                {
                    LogFile.WriteLine("Turning on more CLR GC, JScript and ASP.NET Events.");
                    // Default CLR events also means ASP.NET and private events.  
                    // Turn on ASP.NET at informational by default.
                    EnableUserProvider(userModeSession, "ASP.NET", AspNetTraceEventParser.ProviderGuid,
                        TraceEventLevel.Verbose, ulong.MaxValue);

                    // Turn on JScript events too
                    EnableUserProvider(userModeSession, "Microsoft-JScript", JScriptTraceEventParser.ProviderGuid,
                        TraceEventLevel.Verbose, ulong.MaxValue);

                    // Used to determine what is going on with tasks. 
                    EnableUserProvider(userModeSession, ".NETTasks",
                        new Guid(0x2e5dba47, 0xa3d2, 0x4d16, 0x8e, 0xe0, 0x66, 0x71, 0xff, 220, 0xd7, 0xb5),
                        TraceEventLevel.Verbose, ulong.MaxValue, TraceEventOptions.Stacks);
                }

                // If we have turned on CSwitch and ReadyThread events, go ahead and turn on networking stuff too.  
                // It does not increase the volume in a significant way and they can be pretty useful.     
                if ((parsedArgs.KernelEvents & (KernelTraceEventParser.Keywords.Dispatcher | KernelTraceEventParser.Keywords.ContextSwitch))
                    == (KernelTraceEventParser.Keywords.Dispatcher | KernelTraceEventParser.Keywords.ContextSwitch))
                {
                    EnableUserProvider(userModeSession, "Microsoft-Windows-HttpService",
                        new Guid("DD5EF90A-6398-47A4-AD34-4DCECDEF795F"),
                        TraceEventLevel.Verbose, ulong.MaxValue, TraceEventOptions.Stacks);

                    EnableUserProvider(userModeSession, "Microsoft-Windows-TCPIP",
                        new Guid("2F07E2EE-15DB-40F1-90EF-9D7BA282188A"),
                        TraceEventLevel.Verbose, ulong.MaxValue, TraceEventOptions.Stacks);

                    EnableUserProvider(userModeSession, "Microsoft-Windows-Winsock-AFD",
                        new Guid("E53C6823-7BB8-44BB-90DC-3F86090D48A6"),
                        TraceEventLevel.Verbose, ulong.MaxValue);
                }

                // Turn off NGEN if they asked for it.  
                if (parsedArgs.NoNGenRundown)
                    parsedArgs.ClrEvents &= ~ClrTraceEventParser.Keywords.NGen;

                // Force NGEN rundown if they asked for it. 
                if (parsedArgs.ForceNgenRundown)
                    parsedArgs.ClrEvents &= ~ClrTraceEventParser.Keywords.SupressNGen;

                LogFile.WriteLine("Turning on CLR Events: {0}", parsedArgs.ClrEvents);
                EnableUserProvider(userModeSession, "CLR", ClrTraceEventParser.ProviderGuid,
                    TraceEventLevel.Verbose, (ulong)parsedArgs.ClrEvents);
            }

            if (parsedArgs.Providers != null)
                EnableAdditionalProviders(userModeSession, parsedArgs.Providers, parsedArgs.CommandLine);

            // OK at this point, we want to leave both sessions for an indefinite period of time (even past process exit)
            kernelModeSession.StopOnDispose = false;
            userModeSession.StopOnDispose = false;
            PerfViewLogger.Log.CommandLineParameters("", Environment.CurrentDirectory, "PerfMonitor");
        }
        public void Stop(CommandLineArgs parsedArgs)
        {
            LogFile.WriteLine("Stopping tracing for sessions '" + KernelTraceEventParser.KernelSessionName +
                "' and '" + s_UserModeSessionName + "'.");

            PerfViewLogger.Log.CommandLineParameters("", Environment.CurrentDirectory, "PerfMonitor");
            PerfViewLogger.Log.StopTracing();
            PerfViewLogger.StopTime = DateTime.UtcNow;
            PerfViewLogger.Log.StartAndStopTimes();

            // Try to stop the kernel session
            try { new TraceEventSession(KernelTraceEventParser.KernelSessionName).Stop(); }
            catch (FileNotFoundException) { LogFile.WriteLine("Kernel events were active for this trace."); }
            catch (Exception e) { if (!(e is ThreadInterruptedException)) LogFile.WriteLine("Error stopping Kernel session: " + e.Message); throw; }

            string dataFile = null;
            try
            {
                TraceEventSession clrSession = new TraceEventSession(s_UserModeSessionName);
                dataFile = clrSession.FileName;
                clrSession.Stop();

                // Try to force the rundown of CLR method and loader events.  This routine does not fail.  
                if (!parsedArgs.NoRundown)
                    DoClrRundownForSession(clrSession.FileName, clrSession.SessionName, parsedArgs);
            }
            catch (Exception e) { if (!(e is ThreadInterruptedException)) LogFile.WriteLine("Error stopping User session: " + e.Message); throw; }

            if (dataFile == null || !File.Exists(dataFile))
                LogFile.WriteLine("Warning: no data generated.\n");
            else
            {
                parsedArgs.DataFile = dataFile;
                if (parsedArgs.Merge || parsedArgs.Zip)
                    Merge(parsedArgs);
            }

            DateTime stopComplete = DateTime.Now;
            LogFile.WriteLine("Stop Completed at {0}", stopComplete);
        }
        public void Abort(CommandLineArgs parsedArgs)
        {
            lock (s_UserModeSessionName)    // Insure only one thread can be aborting at a time.
            {
                if (s_abortInProgress)
                    return;
                s_abortInProgress = true;
                m_logFile.WriteLine("Aborting tracing for sessions '" +
                    KernelTraceEventParser.KernelSessionName + "' and '" + s_UserModeSessionName + "'.");
                try { new TraceEventSession(KernelTraceEventParser.KernelSessionName).Stop(true); }
                catch (Exception) { }

                try { new TraceEventSession(s_UserModeSessionName).Stop(true); }
                catch (Exception) { }

                // Insure that the rundown session is also stopped. 
                try { new TraceEventSession(s_UserModeSessionName + "Rundown").Stop(true); }
                catch (Exception) { }
            }
        }
        public void Merge(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";

            LogFile.WriteLine("[Merging data files to " + Path.GetFileName(parsedArgs.DataFile) + ".  Can take 10s of seconds...]");
            Stopwatch sw = Stopwatch.StartNew();

            // MergeInPlace does not respond to ThreadInterupted Exceptions, do on another thread and use ThreadAbort.
            Thread mergeWorker = null;
            Thread pdbWorker = null;
            List<string> pdbFileList = null;
            try
            {
                mergeWorker = new Thread(delegate()
                {
                    var startTime = DateTime.UtcNow;
                    LogFile.WriteLine("Starting Merging of {0}", parsedArgs.DataFile);
                    var kernelFile = Path.ChangeExtension(parsedArgs.DataFile, ".kernel.etl");
                    if (!File.Exists(kernelFile))
                        kernelFile = parsedArgs.DataFile;

                    TraceEventSession.MergeInPlace(parsedArgs.DataFile, LogFile);
                    LogFile.WriteLine("Merging took {0:f1} sec", (DateTime.UtcNow - startTime).TotalSeconds);
                });

                pdbWorker = new Thread(delegate()
                {
                    var startTime = DateTime.UtcNow;
                    LogFile.WriteLine("Starting Generating NGEN pdbs for {0}", parsedArgs.DataFile);
                    var symReader = new SymbolReader(LogFile);
                    pdbFileList = GetNGenPdbs(parsedArgs.DataFile, symReader, LogFile);
                    LogFile.WriteLine("Generating NGEN Pdbs took {0:f1} sec", (DateTime.UtcNow - startTime).TotalSeconds);
                });

                mergeWorker.Start();
                pdbWorker.Start();
                while (!mergeWorker.Join(100) || !pdbWorker.Join(100))
                    ;
            }
            catch (Exception)
            {
                mergeWorker.Abort();
                pdbWorker.Abort();
                throw;
            }

            sw.Stop();
            LogFile.WriteLine("Merge took {0:f3} sec.", sw.Elapsed.TotalSeconds);
            LogFile.WriteLine("Merge output file {0}", parsedArgs.DataFile);

            if (parsedArgs.Zip)
                parsedArgs.DataFile = ZipEtlFile(parsedArgs.DataFile, pdbFileList);
        }

        public void ListSessions(CommandLineArgs parsedArgs)
        {
            LogFile.WriteLine("Active Session Names");
            foreach (string activeSessionName in TraceEventSession.GetActiveSessionNames())
                LogFile.WriteLine("    " + activeSessionName);
        }

        public void EnableKernelStacks(CommandLineArgs parsedArgs)
        {
            SetKernelStacks64(true, LogFile);
        }
        public void DisableKernelStacks(CommandLineArgs parsedArgs)
        {
            SetKernelStacks64(false, LogFile);
        }

        #region private

        private void WaitForStopNoGui(CommandLineArgs parsedArgs, DateTime startTime)
        {
            Console.WriteLine("");
            if (parsedArgs.NoNGenRundown)
                Console.WriteLine("Pre V4.0 .NET Rundown disabled, Type 'E' to enable symbols for V3.5 processes.");
            else
                Console.WriteLine("Pre V4.0 .NET Rundown enabled, Type 'D' to disable and speed up .NET Rundown.");

            Console.WriteLine("Do NOT close this console window.   It will leave collection on!");
            Console.WriteLine("Type S to stop collection, 'A' will abort.  (Also consider /MaxCollectSec:N)");
            bool done = false;
            DateTime lastStatusTime = startTime;
            string startedDropping = "";

            PerformanceCounterTrigger stopTrigger = null;
            if (parsedArgs.StopTrigger != null)
            {
                LogFile.WriteLine("Enabling Performance Counter Trigger {0}.", parsedArgs.StopTrigger);
                stopTrigger = new PerformanceCounterTrigger(parsedArgs.StopTrigger);
                stopTrigger.Update();
            }

            DateTime lastPerfCounterUpdate = startTime;
            while (!done)
            {
                // Do we have a performance counter trigger? 
                if (CheckForPerfCounterTrigger(stopTrigger, parsedArgs.StopTrigger, ref lastPerfCounterUpdate))
                    break;
                if (parsedArgs.MaxCollectSec != 0 && (DateTime.Now - startTime).TotalSeconds > parsedArgs.MaxCollectSec)
                {
                    LogFile.WriteLine("Exceeded the maximum collection time of {0} sec.", parsedArgs.MaxCollectSec);
                    break;
                }
                if (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(true);
                    var keyChar = char.ToLower(keyInfo.KeyChar);
                    if (keyInfo.KeyChar == 's')
                    {
                        Console.WriteLine("Stopping collection.");
                        done = true;
                    }
                    else if (keyInfo.KeyChar == 'e')
                    {
                        parsedArgs.NoNGenRundown = false;
                        Console.WriteLine("Pre V4.0 .NET Rundown enabled.");
                    }
                    else if (keyInfo.KeyChar == 'd')
                    {
                        parsedArgs.NoNGenRundown = true;
                        Console.WriteLine("Pre V4.0 .NET Rundown disabled.");
                    }
                    else if (keyInfo.KeyChar == 'a')
                    {
                        Console.WriteLine("Aborting collection...");
                        throw new ThreadInterruptedException();
                    }
                }

                // periodically send out status.  
                DateTime now = DateTime.Now;
                if ((now - lastStatusTime).TotalSeconds > 10)
                {
                    Console.WriteLine(GetStatusLine(parsedArgs, startTime, ref startedDropping));
                    lastStatusTime = now;
                }

                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Returns true if the 'stopTrigger' has indicated that the collection should stop
        /// </summary>
        private bool CheckForPerfCounterTrigger(PerformanceCounterTrigger stopTrigger, string triggerString, ref DateTime lastUpdateTime)
        {
            if (stopTrigger == null)
                return false;

            var isTriggered = stopTrigger.IsCurrentlyTrue();
            if (isTriggered)
            {
                // Perf counters are noisy, only trigger if we get 3 consective counts that trigger.  
                stopTrigger.Count++;
                if (stopTrigger.Count > 3)
                {
                    PerfViewLogger.Log.PerformanceCounterTriggered(triggerString, stopTrigger.CurrentValue);
                    LogFile.WriteLine("Performance Counter Trigger {0} FIRED, value = {1:n3} time = {2}",
                        triggerString, stopTrigger.CurrentValue, DateTime.Now);
                    LogFile.WriteLine("[Taking 10 more seconds of data.]");
                    LogFile.Flush();
                    // TODO FIX NOW.  this is a bad experience. The GUI locks up.   
                    System.Threading.Thread.Sleep(10000);        // Wait 10 seconds more . 
                    return true;
                }
            }
            else
                stopTrigger.Count = 0;

            var now = DateTime.Now;
            if ((now - lastUpdateTime).TotalSeconds > 5)
            {
                LogFile.WriteLine("Performance Counter Trigger {0} WAITING, value = {1:n3} time = {2}",
                    triggerString, stopTrigger.CurrentValue, DateTime.Now);
                lastUpdateTime = now;
            }
            return false;
        }

        /// <summary>
        /// Returns a status line for the collection that indicates how much data we have collected.  
        /// TODO review, I don't really like this.  
        /// </summary>
        internal static string GetStatusLine(CommandLineArgs parsedArgs, DateTime startTime, ref string startedDropping)
        {
            var durationSec = (DateTime.Now - startTime).TotalSeconds;
            var fileSizeMB = 0.0;
            if (File.Exists(parsedArgs.DataFile))
            {
                bool droppingData = startedDropping.Length != 0;

                fileSizeMB = new FileInfo(parsedArgs.DataFile).Length / 1048576.0;      // MB here are defined as 2^20 
                if (!droppingData && parsedArgs.Circular != 0 && parsedArgs.Circular <= fileSizeMB)
                    droppingData = true;
                var kernelName = Path.ChangeExtension(parsedArgs.DataFile, ".kernel.etl");
                if (File.Exists(kernelName))
                {
                    var kernelFileSizeMB = new FileInfo(kernelName).Length / 1048576.0;
                    if (!droppingData && parsedArgs.Circular != 0 && parsedArgs.Circular <= kernelFileSizeMB)
                        droppingData = true;
                    fileSizeMB += kernelFileSizeMB;
                }

                if (droppingData && startedDropping.Length == 0)

                    startedDropping = "  Recycling started at " + TimeStr(durationSec) + ".";
            }

            return string.Format("Collecting {0,8}: Size={1,5:n1} MB.{2}", TimeStr(durationSec), fileSizeMB, startedDropping);
        }

        internal static string TimeStr(double durationSec)
        {
            string ret;
            if (durationSec < 60)
                ret = durationSec.ToString("f0") + " sec";
            else if (durationSec < 3600)
                ret = (durationSec / 60).ToString("f1") + " min";
            else if (durationSec < 86400)
                ret = (durationSec / 3600).ToString("f1") + " hr";
            else
                ret = (durationSec / 86400).ToString("f1") + " days";
            return ret;
        }

        private static RegistryKey GetMemManagementKey(bool writable)
        {
            // Open this computer's registry hive remotely even though we are in th WOW we 
            // should have access to the 64 bit registry, which is what we want.
            RegistryKey hklm = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, Environment.MachineName);
            if (hklm == null)
            {
                Debug.Assert(false, "Could not get HKLM key");
                return null;
            }
            RegistryKey memManagment = hklm.OpenSubKey(@"System\CurrentControlSet\Control\Session Manager\Memory Management", writable);
            hklm.Close();
            return memManagment;
        }
        private static void SetKernelStacks64(bool crawlable, TextWriter writer)
        {
            // Are we on a 64 bit system? 
            if (!(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "AMD64" ||
                  Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") == "AMD64"))
            {
                writer.WriteLine("Disabling kernel paging is only necessary on X64 machines");
                return;
            }

            if (IsKernelStacks64Enabled() == crawlable)
            {
                writer.WriteLine(@"HLKM\" + @"System\CurrentControlSet\Control\Session Manager\Memory Management" + "DisablePagingExecutive" + " already {0}",
                    crawlable ? "set" : "unset");
                return;
            }

            // This is not needed on Windows 8 (mostly)
            var ver = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
            if (ver > 61)
            {
                writer.WriteLine("Disabling kernel paging is not necessary on Win8 machines.");
                return;
            }

            try
            {
                RegistryKey memKey = GetMemManagementKey(true);
                if (memKey != null)
                {
                    memKey.SetValue("DisablePagingExecutive", crawlable ? 1 : 0, RegistryValueKind.DWord);
                    memKey.Close();
                    writer.WriteLine();
                    writer.WriteLine("The memory management configuration has been {0} for stack crawling.", crawlable ? "enabled" : "disabled");
                    writer.WriteLine("However a reboot is needed for it to take effect.  You can reboot by executing");
                    writer.WriteLine("     shutdown /r /t 1 /f");
                    writer.WriteLine();
                }
                else
                    writer.WriteLine("Error: Could not access Kernel memory management registry keys.");
            }
            catch (Exception e)
            {
                writer.WriteLine("Error: Failure setting registry keys: {0}", e.Message);
            }
        }
        private static bool IsKernelStacks64Enabled()
        {
            bool ret = false;
            RegistryKey memKey = GetMemManagementKey(false);
            if (memKey != null)
            {
                object valueObj = memKey.GetValue("DisablePagingExecutive", null);
                if (valueObj != null && valueObj is int)
                {
                    ret = ((int)valueObj) != 0;
                }
                memKey.Close();
            }
            return ret;
        }

        private string ZipEtlFile(string dataFileName, List<string> pdbList)
        {
            var sw = Stopwatch.StartNew();

            var zipFileName = dataFileName + ".zip";
            var newFileName = dataFileName + ".zip.new";
            FileUtilities.ForceDelete(newFileName);
            try
            {
                LogFile.WriteLine("[Zipping ETL file {0}]", dataFileName);
                using (var zipArchive = new ZipArchive(newFileName, ZipArchiveMode.Create))
                {
                    zipArchive.CreateEntryFromFile(dataFileName, Path.GetFileName(dataFileName));
                    LogFile.WriteLine("[Writing {0} PDBS to Zip file]", pdbList.Count);
                    AddPdbsToZip(zipArchive, pdbList, LogFile);
                }
                FileUtilities.ForceMove(newFileName, zipFileName);

                // We make the ZIP the same time as the original file.  TODO is this the best option?   
                File.SetLastWriteTimeUtc(zipFileName, File.GetLastWriteTimeUtc(dataFileName));
                FileUtilities.ForceDelete(dataFileName);
            }
            finally
            {
                FileUtilities.ForceDelete(newFileName);
            }
            sw.Stop();
            LogFile.WriteLine("ZIP generation took {0:f3} sec", sw.Elapsed.TotalSeconds);
            LogFile.WriteLine("ZIP output file {0}", zipFileName);


            return zipFileName;
        }

        /// <summary>
        /// Returns the list of path names to the NGEN pdbs for any NGEN image in 'etlFile' that has
        /// any samples in it.   
        /// </summary>
        static internal List<string> GetNGenPdbs(string etlFile, SymbolReader symbolReader, TextWriter log)
        {
            var images = new List<ImageData>(300);
            var addressCounts = new Dictionary<Address, int>();

            // Get the name of all DLLS (in the file, and the set of all address-process pairs in the file.   
            using (var source = new ETWTraceEventSource(etlFile))
            {
                source.Kernel.ImageGroup += delegate(ImageLoadTraceData data)
                {
                    var fileName = data.FileName;
                    if (fileName.IndexOf(".ni.", StringComparison.OrdinalIgnoreCase) < 0)
                        return;

                    var processId = data.ProcessID;
                    images.Add(new ImageData(processId, fileName, data.ImageBase, data.ImageSize));
                };

                source.Kernel.StackWalk += delegate(StackWalkTraceData data)
                {
                    if (data.ProcessID == 0)
                        return;
                    var processId = data.ProcessID;
                    for (int i = 0; i < data.FrameCount; i++)
                    {
                        var address = (data.InstructionPointer(i) & 0xFFFFFFFFFFFF0000L) + ((Address)(processId & 0xFFFF));
                        addressCounts[address] = 1;
                    }
                };
                source.Process();
            }

            // imageNames is a set of names that we want symbols for.  
            var imageNames = new Dictionary<string, string>(100);
            foreach (var image in images)
            {
                if (!imageNames.ContainsKey(image.DllName))
                {
                    for (uint offset = 0; offset < (uint)image.Size; offset += 0x10000)
                    {
                        var key = image.BaseAddress + offset + (uint)(image.ProcessID & 0xFFFF);
                        if (addressCounts.ContainsKey(key))
                        {
                            imageNames[image.DllName] = image.DllName;
                            break;
                        }
                    }
                }
            }

            // Find the PDBS for the given images. 
            var pdbNames = new List<string>(100);
            foreach (var imageName in imageNames.Keys)
            {
                Debug.Assert(0 <= imageName.IndexOf(".ni.", StringComparison.OrdinalIgnoreCase));
                var pdbName = TraceCodeAddresses.GenerateNGenPdb(symbolReader, imageName);
                if (pdbName != null)
                {
                    pdbNames.Add(pdbName);
                    log.WriteLine("Found NGEN pdb {0}", pdbName);
                }
            }
            return pdbNames;
        }

        /// <summary>
        /// Image data is a trivial record for image data, where it is keyed by the base address, processID and name.  
        /// </summary>
        class ImageData : IComparable<ImageData>
        {
            public int CompareTo(ImageData other)
            {
                var ret = BaseAddress.CompareTo(other.BaseAddress);
                if (ret != 0)
                    return ret;
                ret = ProcessID - other.ProcessID;
                if (ret != 0)
                    return ret;
                return DllName.CompareTo(other.DllName);
            }

            public ImageData(int ProcessID, string DllName, Address BaseAddress, int Size)
            {
                this.ProcessID = ProcessID;
                this.DllName = DllName;
                this.BaseAddress = BaseAddress;
                this.Size = Size;
            }
            public int ProcessID;
            public string DllName;
            public Address BaseAddress;
            public int Size;
        }

        static void AddPdbsToZip(ZipArchive zipArchive, List<string> pdbs, TextWriter log)
        {
            // Add the Pdbs to the archive 
            foreach (var pdb in pdbs)
            {
                // If the path looks like a sym server cache path, grab that chunk, otherwise just copy the file name part of the path.  
                string relativePath;
                var m = Regex.Match(pdb, @"\\([^\\]+.pdb\\\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\w\d+\\[^\\]+)$");
                if (m.Success)
                    relativePath = m.Groups[1].Value;
                else
                    relativePath = Path.GetFileName(pdb);

                var archivePath = Path.Combine("symbols", relativePath);

                // log.WriteLine("Writing PDB {0} to archive.", archivePath);
                zipArchive.CreateEntryFromFile(pdb, archivePath);
            }
        }

        /// <summary>
        /// Enable any additional providers specified by 'providerSpecs'.  
        /// </summary>
        public void EnableAdditionalProviders(TraceEventSession userModeSession, string[] providerSpecs, string commandLine = null)
        {
            string wildCardFileName = null;
            if (commandLine != null)
            {
                wildCardFileName = Command.FindOnPath(GetExeName(commandLine));
            }

            var parsedProviders = ProviderParser.ParseProviderSpecs(providerSpecs, wildCardFileName, LogFile);
            foreach (var parsedProvider in parsedProviders)
            {
                CheckAndWarnAboutAspNet(parsedProvider.Guid, true);
                EnableUserProvider(userModeSession, parsedProvider.Name, parsedProvider.Guid, parsedProvider.Level,
                    (ulong)parsedProvider.MatchAnyKeywords, parsedProvider.Options, parsedProvider.Values);
            }
        }

        private void CheckAndWarnAboutAspNet(Guid guid, bool displayDialog)
        {
            if (guid != AspNetTraceEventParser.ProviderGuid)
                return;

            // We turned on the ASP.NET provider, make sure ASP.NET tracing is enabled in the registry.
            var tracing = Registry.LocalMachine.GetValue(@"SOFTWARE\Microsoft\InetStp\Components\HttpTracing", null);
            if (tracing != null && (tracing is int) && (int)tracing != 0)
                return;

            var message = "ASP.NET provider activated but ASP.NET Tracing not installed.\r\n" +
                          "    No ASP.NET events may be created.\r\n";
            LogFile.WriteLine(message);
        }

        void EnableUserProvider(TraceEventSession userModeSession, string providerName, Guid providerGuid,
            TraceEventLevel providerLevel, ulong matchAnyKeywords, TraceEventOptions options = TraceEventOptions.None,
            IEnumerable<KeyValuePair<string, string>> values = null)
        {
            if (providerGuid == ClrTraceEventParser.ProviderGuid)
                PerfViewLogger.Log.ClrEnableParameters((ClrTraceEventParser.Keywords)matchAnyKeywords, providerLevel);
            else
                PerfViewLogger.Log.ProviderEnableParameters(providerName, providerGuid, providerLevel, matchAnyKeywords, options);

            LogFile.WriteLine("Enabling Provider {0} Level {1} Keywords = 0x{2:x} Options = {3} Guid = {4}",
                providerName, providerLevel, matchAnyKeywords, options, providerGuid);
            userModeSession.EnableProvider(providerGuid, providerLevel, matchAnyKeywords, 0, options, values);
        }

        private static string GetExeName(string commandLine)
        {
            Match m = Regex.Match(commandLine, "^\\s*\"(.*?)\"");    // Is it quoted?
            if (!m.Success)
                m = Regex.Match(commandLine, @"\s*(\S*)");           // Nope, then whatever is before the first space.
            return m.Groups[1].Value;
        }

        /// <summary>
        /// Activates the CLR rundown for the user session 'sessionName' with logFile 'fileName'  
        /// </summary>
        private void DoClrRundownForSession(string fileName, string sessionName, CommandLineArgs parsedArgs)
        {
            // TODO FIX NOW: use the fact the file has stopped growing, not the CPU to determine if the rundown is complete. 
            if (string.IsNullOrEmpty(fileName))
                return;
            LogFile.WriteLine("[Sending rundown command to CLR providers...]");
            if (!parsedArgs.NoNGenRundown)
                LogFile.WriteLine("[Use /NoNGenRundown if you don't care about pre V4.0 runtimes]");
            Stopwatch sw = Stopwatch.StartNew();
            TraceEventSession clrRundownSession = null;
            try
            {
                try
                {
                    var rundownFile = Path.ChangeExtension(fileName, ".clrRundown.etl");
                    clrRundownSession = new TraceEventSession(sessionName + "Rundown", rundownFile);

                    clrRundownSession.BufferSizeMB = Math.Max(parsedArgs.BufferSize, 256);

                    clrRundownSession.EnableProvider(PerfViewLogger.Log.Guid);
                    Thread.Sleep(20);       // Give it time to startup 
                    PerfViewLogger.Log.StartRundown();

                    var rundownKeywords = ClrRundownTraceEventParser.Keywords.Default;
                    if (parsedArgs.ForceNgenRundown)
                        rundownKeywords &= ~ClrRundownTraceEventParser.Keywords.SupressNGen;

                    if (parsedArgs.NoNGenRundown)
                        rundownKeywords &= ~ClrRundownTraceEventParser.Keywords.NGen;

                    EnableUserProvider(clrRundownSession, "CLRRundown", ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                        (ulong)rundownKeywords);

                    // For V2.0 runtimes you activate the main provider so we do that too.  
                    EnableUserProvider(clrRundownSession, "Clr", ClrTraceEventParser.ProviderGuid,
                        TraceEventLevel.Verbose, (ulong)rundownKeywords);

                    PerfViewLogger.Log.WaitForIdle();
                    WaitForRundownIdle(parsedArgs.MinRundownTime, parsedArgs.RundownTimeout, rundownFile);

                    PerfViewLogger.Log.CommandLineParameters("", Environment.CurrentDirectory, "PerfMonitor");
                    PerfViewLogger.Log.StartAndStopTimes();
                    PerfViewLogger.Log.StopRundown();
                    clrRundownSession.Stop();
                    clrRundownSession = null;
                    sw.Stop();
                    LogFile.WriteLine("CLR Rundown took {0:f3} sec.", sw.Elapsed.TotalSeconds);
                }
                finally
                {
                    if (clrRundownSession != null)
                        clrRundownSession.Stop();
                }
            }
            catch (Exception e)
            {
                if (!(e is ThreadInterruptedException))
                    LogFile.WriteLine("Warning: failure during CLR Rundown " + e.Message);
                throw;
            }
        }
        /// <summary>
        /// Currently there is no good way to know when rundown is finished.  We basically wait as long as
        /// the rundown file is growing.  
        /// </summary>
        private void WaitForRundownIdle(int minSeconds, int maxSeconds, string rundownFilePath)
        {
            LogFile.WriteLine("Waiting up to {0} sec for rundown events.  Use /RundownTimeout to change.", maxSeconds);
            LogFile.WriteLine("If you know your process has exited, use /noRundown qualifer to skip this step.");

            long rundownFileLen = 0;
            for (int i = 0; i < maxSeconds; i++)
            {
                Thread.Sleep(1000);
                var newRundownFileLen = new FileInfo(rundownFilePath).Length;
                var delta = newRundownFileLen - rundownFileLen;
                LogFile.WriteLine("Rundown File Length: {0:n1}MB delta: {1:n1}MB", newRundownFileLen / 1000000.0, delta / 1000000.0);
                rundownFileLen = newRundownFileLen;

                if (i >= minSeconds)
                {
                    if (delta == 0 && newRundownFileLen != 0)
                    {
                        LogFile.WriteLine("Rundown file has stopped growing, assuming rundown complete.");
                        break;
                    }
                }
            }
        }

        private bool s_abortInProgress;      // We are currently in Abort()
        private static string s_UserModeSessionName = "PerfMonitorSession";
        static bool s_addedSupportDirToPath;

        TextWriter m_logFile;
        #endregion
    }

    /// <summary>
    /// PerformanceCounterTrigger is a class that knows how to determine if a particular performance counter has
    /// exceeded a particular threashold.   
    /// </summary>
    class PerformanceCounterTrigger
    {
        /// <summary>
        /// Creates a new PerformanceCounterTrigger based on a specification.   Basically this specification is 
        /// a condition which is either true or false at any particular time.  Once the PerformanceCounterTrigger
        /// has been created, you can call 'IsCurrentlyTrue()' to see if the condition holds.  
        /// </summary>
        /// <param name="spec">This is of the form CATEGORY:COUNTERNAME:INSTANCE OP NUM  where OP is either a 
        /// greater than or less than sign, NUM is a floating point number and CATEGORY:COUNTERNAME:INSTANCE
        /// identify the performance counter to use (same as PerfMon).  For example 
        /// 
        /// .NET CLR Memory:% Time in GC:_Global_>20  
        ///    
        /// Will trigger when the % Time in GC for the _Global_ instance (which represents all processes) is
        /// greater than 20. 
        /// 
        /// Processor:% Processor Time:_Total>90
        /// 
        /// Will trigger when the % processor time exceeeds 90%.  
        /// </param>
        public PerformanceCounterTrigger(string spec)
        {
            var m = Regex.Match(spec, @"(.*?):(.*?):(.*?)\s*([<>])\s*(\d+.\d*)");
            if (!m.Success)
                throw new ApplicationException(
                    "Performance monitor specification does not match syntax CATEGORY:COUNTER:INSTANCE [<>] NUM");
            var category = m.Groups[1].Value;
            var counter = m.Groups[2].Value;
            var instance = m.Groups[3].Value;
            var op = m.Groups[4].Value;
            var threashold = m.Groups[5].Value;

            IsGreaterThan = (op == ">");
            Threshold = float.Parse(threashold);
            try
            {
                Counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            }
            catch (Exception)
            {
                throw new ApplicationException("Cound not find the performance counter " + category + ":" + counter + ":" + instance);
            }
        }
        public PerformanceCounter Counter { get; private set; }
        public float Threshold { get; set; }
        public bool IsGreaterThan { get; set; }

        /// <summary>
        /// This is the value of the performance counter since the last tie 'Update()' was called.  
        /// </summary>
        public float CurrentValue { get; private set; }
        /// <summary>
        /// Update 'CurrentValue' to the live value of the performance counter. 
        /// </summary>
        /// <returns></returns>
        public float Update()
        {
            CurrentValue = Counter.NextValue();
            return CurrentValue;
        }
        public bool IsCurrentlyTrue()
        {
            Update();
            if (IsGreaterThan)
                return CurrentValue > Threshold;
            else
                return CurrentValue < Threshold;
        }

        // Perf Counters can be noisy, be default we require 3 consecutive samples that succeed.  
        // This variable keeps track of this. 
        public int Count;
    }

    /// <summary>
    /// ProviderParser knows how to take a string provider specification and parse it.  
    /// </summary>
    static class ProviderParser
    {
        public class ParsedProvider
        {
            public string Name;
            public Guid Guid;
            public TraceEventLevel Level;
            public TraceEventKeyword MatchAnyKeywords;
            public TraceEventOptions Options;
            public List<KeyValuePair<string, string>> Values;       // Pass addition information to EventSource.   TODO FIX NOW currently unused.
        }

        /// <summary>
        /// TODO FIX NOW document
        /// </summary>
        public static List<ParsedProvider> ParseProviderSpecs(string[] providerSpecs, string wildCardFileName, TextWriter log = null)
        {
            var ret = new List<ParsedProvider>();

            foreach (var providerSpec in providerSpecs)
            {
                if (log != null)
                    log.WriteLine("Parsing Spec {0}", providerSpec);

                // "^\s* (.*?) (:(0x)?(.*?))?(:(.*?))?(:stack)?(:(.*))\s*$", RegexOptions.IgnoreCase);

                Match m = Regex.Match(providerSpec, @"^\s*(.*?)(:(0x)?(.*?))?(:(.*?))?(:stack)?(:(.*))?\s*$", RegexOptions.IgnoreCase);
                Debug.Assert(m.Success);
                var providerStr = m.Groups[1].Value;
                var matchAnyKeywordsStr = m.Groups[4].Value;
                var levelStr = m.Groups[6].Value;
                var stackStr = m.Groups[7].Value;
                var valuesStr = m.Groups[9].Value;


                if (providerStr == "@" || providerStr.Length == 0 && wildCardFileName != null)
                {
                    if (log != null)
                        log.WriteLine("No file name provided using {0}", wildCardFileName);

                    providerStr = "@" + wildCardFileName;
                }

                ulong matchAnyKeywords = unchecked((ulong)-1);
                if (matchAnyKeywordsStr.Length > 0)
                {
                    // Leaving it empty or using a * will mean everything, which is the default.  
                    if (matchAnyKeywordsStr.Length > 0 && matchAnyKeywordsStr != "*")
                    {
                        if (!ulong.TryParse(matchAnyKeywordsStr, System.Globalization.NumberStyles.HexNumber, null, out matchAnyKeywords))
                            throw new CommandLineParserException("Could not parse as a hexidecimal keyword specification " + matchAnyKeywordsStr);
                    }
                }

                TraceEventLevel level = TraceEventLevel.Verbose;
                if (levelStr.Length > 0)
                {
                    int intLevel;
                    if (int.TryParse(levelStr, out intLevel) && 0 <= intLevel && intLevel < 256)
                        level = (TraceEventLevel)intLevel;
                    else
                    {
                        try { Enum.Parse(typeof(TraceEventLevel), levelStr); }
                        catch { throw new CommandLineParserException("Could not parse level specification " + levelStr); }
                    }
                }

                TraceEventOptions options = TraceEventOptions.None;
                if (stackStr.Length > 0)
                    options |= TraceEventOptions.Stacks;

                // TODO FIX NOW test this.  
                List<KeyValuePair<string, string>> values = null;
                if (valuesStr.Length > 0)
                {
                    values = new List<KeyValuePair<string, string>>();

                    // TODO FIX so that it works with things with commas and colons
                    for (var pos = 0; pos < valuesStr.Length; )
                    {
                        var regex = new Regex(@"^\s*(\w+)=([^:]*)");
                        var match = regex.Match(valuesStr, pos);
                        if (!match.Success)
                            throw new ApplicationException("Could not parse values '" + valuesStr + "'");
                        values.Add(new KeyValuePair<string, string>(match.Groups[1].Value, match.Groups[2].Value));
                        pos += match.Length;
                    }
                }

                ParseProviderSpec(providerStr, level, (TraceEventKeyword)matchAnyKeywords, options, values, ret, log);
            }
            return ret;
        }


        #region private
        // Given a provider specification (guid or name or @filename#eventSource return a list of providerGuids for it.  
        private static void ParseProviderSpec(string providerSpec, TraceEventLevel level, TraceEventKeyword matchAnyKeywords,
            TraceEventOptions options, List<KeyValuePair<string, string>> values, List<ParsedProvider> retList, TextWriter log)
        {
            // Is it a EventSource specification (@path#eventSourceName?)
            if (providerSpec.StartsWith("@"))
            {
                int atIndex = providerSpec.IndexOf('#', 1);
                string eventSourceName = null;
                if (atIndex < 0)
                    atIndex = providerSpec.Length;
                else
                    eventSourceName = providerSpec.Substring(atIndex + 1);

                string fileName = providerSpec.Substring(1, atIndex - 1);
                if (!File.Exists(fileName))
                {
                    var exe = fileName + ".exe";
                    if (File.Exists(exe))
                        fileName = exe;
                    else
                    {
                        var dll = fileName + ".dll";
                        if (File.Exists(dll))
                            fileName = dll;
                    }
                }

                if (log != null)
                    log.WriteLine("Looking for event sources in {0}", fileName);
                foreach (Type eventSource in EventSourceFinder.GetEventSourcesInFile(fileName))
                {
                    string candidateEventSourceName = EventSourceFinder.GetName(eventSource);
                    if (log != null)
                        log.WriteLine("Found EventSource {0} in file.", candidateEventSourceName);

                    bool useProvider = false;
                    if (eventSourceName == null)
                        useProvider = true;
                    else
                    {
                        if (String.Compare(eventSourceName, candidateEventSourceName, StringComparison.OrdinalIgnoreCase) == 0)
                            useProvider = true;
                        else
                        {
                            int dot = candidateEventSourceName.LastIndexOf('.');
                            if (dot >= 0)
                                candidateEventSourceName = candidateEventSourceName.Substring(dot + 1);

                            if (String.Compare(eventSourceName, candidateEventSourceName, StringComparison.OrdinalIgnoreCase) == 0)
                                useProvider = true;
                            else
                            {
                                if (candidateEventSourceName.EndsWith("EventSource", StringComparison.OrdinalIgnoreCase))
                                    candidateEventSourceName = candidateEventSourceName.Substring(0, candidateEventSourceName.Length - 11);

                                if (String.Compare(eventSourceName, candidateEventSourceName, StringComparison.OrdinalIgnoreCase) == 0)
                                    useProvider = true;

                            }
                        }
                    }
                    if (useProvider)
                    {
                        retList.Add(new ParsedProvider()
                        {
                            Name = EventSourceFinder.GetName(eventSource),
                            Guid = EventSourceFinder.GetGuid(eventSource),
                            Level = level,
                            MatchAnyKeywords = matchAnyKeywords,
                            Options = options,
                            Values = values
                        });
                    }
                }
                if (retList.Count == 0)
                {
                    if (eventSourceName != null)
                        throw new ApplicationException("EventSource " + eventSourceName + " not found in " + fileName);
                    else
                        throw new ApplicationException("No types deriving from EventSource found in " + fileName);
                }
            }
            else
            {
                // Is it a normal GUID 
                Guid providerGuid;
                if (Regex.IsMatch(providerSpec, "........-....-....-....-............"))
                {
                    try
                    {
                        providerGuid = new Guid(providerSpec);
                    }
                    catch
                    {
                        throw new ApplicationException("Could not parse Guid '" + providerSpec + "'");
                    }
                }
                else if (providerSpec.StartsWith("*"))
                {
                    // We allow you to specify EventSources without knowing where they came from with the * syntax.  
                    providerGuid = EventSourceFinder.GenerateGuidFromName(providerSpec.Substring(1).ToUpperInvariant());
                }
                // Is it specially known.  TODO should we remove some of these?
                else if (string.Compare(providerSpec, "Clr", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = ClrTraceEventParser.ProviderGuid;
                else if (string.Compare(providerSpec, "ClrRundown", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = ClrRundownTraceEventParser.ProviderGuid;
                else if (string.Compare(providerSpec, "ClrStress", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = ClrStressTraceEventParser.ProviderGuid;
                else if (string.Compare(providerSpec, "ASP.Net", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = new Guid("AFF081FE-0247-4275-9C4E-021F3DC1DA35");
                else if (string.Compare(providerSpec, ".NetTasks", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = new Guid(0x2e5dba47, 0xa3d2, 0x4d16, 0x8e, 0xe0, 0x66, 0x71, 0xff, 220, 0xd7, 0xb5);
                else if (string.Compare(providerSpec, ".NetFramework", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = new Guid(0x8e9f5090, 0x2d75, 0x4d03, 0x8a, 0x81, 0xe5, 0xaf, 0xbf, 0x85, 0xda, 0xf1);
                else if (string.Compare(providerSpec, ".NetPLinq", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = new Guid(0x159eeeec, 0x4a14, 0x4418, 0xa8, 0xfe, 250, 0xab, 0xcd, 0x98, 120, 0x87);
                else if (string.Compare(providerSpec, ".NetConcurrentColections", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = new Guid(0x35167f8e, 0x49b2, 0x4b96, 0xab, 0x86, 0x43, 0x5b, 0x59, 0x33, 0x6b, 0x5e);
                else if (string.Compare(providerSpec, ".NetSync", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = new Guid(0xec631d38, 0x466b, 0x4290, 0x93, 6, 0x83, 0x49, 0x71, 0xba, 2, 0x17);
                else if (string.Compare(providerSpec, "MeasurementBlock", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = new Guid("143A31DB-0372-40B6-B8F1-B4B16ADB5F54");
                else if (string.Compare(providerSpec, "CodeMarkers", StringComparison.OrdinalIgnoreCase) == 0)
                    providerGuid = new Guid("641D7F6C-481C-42E8-AB7E-D18DC5E5CB9E");
                else
                {
                    providerGuid = TraceEventSession.GetProviderByName(providerSpec);
                    // Look it up by name 
                    if (providerGuid == Guid.Empty)
                        throw new ApplicationException("Could not find provider name '" + providerSpec + "'");
                }
                retList.Add(new ParsedProvider()
                {
                    Name = providerSpec,
                    Guid = providerGuid,
                    Level = level,
                    MatchAnyKeywords = matchAnyKeywords,
                    Options = options,
                });
            }
        }

        #endregion
    }

    /// <summary>
    /// EventSourceFinder is a class that can find all the EventSources in a file
    /// </summary>
    static class EventSourceFinder
    {
        // TODO remove and depend on framework for these instead.  
        public static Guid GetGuid(Type eventSource)
        {
            foreach (var attrib in CustomAttributeData.GetCustomAttributes(eventSource))
            {
                foreach (var arg in attrib.NamedArguments)
                {
                    if (arg.MemberInfo.Name == "Guid")
                    {
                        var value = (string)arg.TypedValue.Value;
                        return new Guid(value);
                    }
                }
            }

            return GenerateGuidFromName(GetName(eventSource).ToUpperInvariant());
        }
        public static string GetName(Type eventSource)
        {
            foreach (var attrib in CustomAttributeData.GetCustomAttributes(eventSource))
            {
                foreach (var arg in attrib.NamedArguments)
                {
                    if (arg.MemberInfo.Name == "Name")
                    {
                        var value = (string)arg.TypedValue.Value;
                        return value;
                    }
                }
            }
            return eventSource.Name;
        }
        public static string GetManifest(Type eventSource)
        {
            // Invoke GenerateManifest
            string manifest = (string)eventSource.BaseType.InvokeMember("GenerateManifest",
                BindingFlags.InvokeMethod | BindingFlags.Static | BindingFlags.Public,
                null, null, new object[] { eventSource, "" });

            return manifest;
        }
        public static Guid GenerateGuidFromName(string name)
        {
            // The algorithm below is following the guidance of http://www.ietf.org/rfc/rfc4122.txt
            // Create a blob containing a 16 byte number representing the namespace
            // followed by the unicode bytes in the name.  
            var bytes = new byte[name.Length * 2 + 16];
            uint namespace1 = 0x482C2DB2;
            uint namespace2 = 0xC39047c8;
            uint namespace3 = 0x87F81A15;
            uint namespace4 = 0xBFC130FB;
            // Write the bytes most-significant byte first.  
            for (int i = 3; 0 <= i; --i)
            {
                bytes[i] = (byte)namespace1;
                namespace1 >>= 8;
                bytes[i + 4] = (byte)namespace2;
                namespace2 >>= 8;
                bytes[i + 8] = (byte)namespace3;
                namespace3 >>= 8;
                bytes[i + 12] = (byte)namespace4;
                namespace4 >>= 8;
            }
            // Write out  the name, most significant byte first
            for (int i = 0; i < name.Length; i++)
            {
                bytes[2 * i + 16 + 1] = (byte)name[i];
                bytes[2 * i + 16] = (byte)(name[i] >> 8);
            }

            // Compute the Sha1 hash 
            var sha1 = System.Security.Cryptography.SHA1.Create();
            byte[] hash = sha1.ComputeHash(bytes);

            // Create a GUID out of the first 16 bytes of the hash (SHA-1 create a 20 byte hash)
            int a = (((((hash[3] << 8) + hash[2]) << 8) + hash[1]) << 8) + hash[0];
            short b = (short)((hash[5] << 8) + hash[4]);
            short c = (short)((hash[7] << 8) + hash[6]);

            c = (short)((c & 0x0FFF) | 0x5000);   // Set high 4 bits of octet 7 to 5, as per RFC 4122
            Guid guid = new Guid(a, b, c, hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]);
            return guid;
        }

        // TODO load it its own appdomain so we can unload them properly.
        public static IEnumerable<Type> GetEventSourcesInFile(string fileName, bool allowInvoke = false)
        {
            System.Reflection.Assembly assembly;
            try
            {
                if (allowInvoke)
                    assembly = System.Reflection.Assembly.LoadFrom(fileName);
                else
                    assembly = System.Reflection.Assembly.ReflectionOnlyLoadFrom(fileName);
            }
            catch (Exception e)
            {
                // Convert to an application exception TODO is this a good idea?
                throw new ApplicationException(e.Message);
            }

            Dictionary<Assembly, Assembly> soFar = new Dictionary<Assembly, Assembly>();
            GetStaticReferencedAssemblies(assembly, soFar);

            List<Type> eventSources = new List<Type>();
            foreach (Assembly subAssembly in soFar.Keys)
            {
                try
                {
                    foreach (Type type in subAssembly.GetTypes())
                    {
                        if (type.BaseType != null && type.BaseType.Name == "EventSource")
                            eventSources.Add(type);
                    }
                }
                catch (Exception)
                {
                    Debug.WriteLine("Problem loading {0} module, skipping.", subAssembly.GetName().Name);
                }
            }
            return eventSources;
        }
        #region private

        private static void GetStaticReferencedAssemblies(Assembly assembly, Dictionary<Assembly, Assembly> soFar)
        {
            soFar[assembly] = assembly;
            string assemblyDirectory = Path.GetDirectoryName(assembly.ManifestModule.FullyQualifiedName);
            foreach (AssemblyName childAssemblyName in assembly.GetReferencedAssemblies())
            {
                try
                {
                    // TODO is this is at best heuristic.  
                    string childPath = Path.Combine(assemblyDirectory, childAssemblyName.Name + ".dll");
                    Assembly childAssembly = null;
                    if (File.Exists(childPath))
                        childAssembly = Assembly.ReflectionOnlyLoadFrom(childPath);

                    //TODO do we care about things in the GAC?   it expands the search quite a bit. 
                    //else
                    //    childAssembly = Assembly.Load(childAssemblyName);

                    if (childAssembly != null && !soFar.ContainsKey(childAssembly))
                        GetStaticReferencedAssemblies(childAssembly, soFar);
                }
                catch (Exception)
                {
                    Console.WriteLine("Could not load assembly " + childAssemblyName + " skipping.");
                }
            }
        }
        #endregion
    }
}
