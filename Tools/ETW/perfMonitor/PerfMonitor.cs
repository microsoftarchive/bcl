// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Diagnostics.Tracing;
using Stacks;
using Stats;
using Symbols;
using Utilities;
using Diagnostics.Tracing.StackSources;
using PerfView;
using Diagnostics.Tracing.Parsers;

// See code:PerfMonitor.PerfMonitor to get started. 
namespace PerfMonitor
{
    /// <summary>
    /// A utility for profiling the system using Event Tracing for Windows (ETW)
    /// 
    /// Basically reads the command line and based on that does one of the commands in code:CommandProcessor
    /// </summary>
    sealed class PerfMonitor
    {
        public static int Main()
        {
            // We want to make deployment 'trivial' which means that EVERYTHING is in a single EXE.
            // WE support this by unpacking all the other DLLS needed from the EXE if necessary when
            // we launch.   However to do this we can't refernce the DLLs until we have unpacked them
            // We do this doing the real work (which happens to reference TraceEvent.dll) in 'DoMain'
            // and insure that we unpacked all dlls (including TraceEvent.dll) before calling it. 
            SupportFiles.UnpackResourcesIfNeeded();

            // Preemptively LoadLibrary it so that it is guarenteed find it.  
            // TODO do this more lazily close to actual use.  
            SupportFiles.LoadNative("msdia100.dll");
            SupportFiles.LoadNative("dbghelp.dll");
            SupportFiles.LoadNative("KernelTraceControl.dll");
            return DoMain();
        }
        #region private
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int DoMain()
        {
            Stopwatch sw = Stopwatch.StartNew();

            var commandProcessor = new PerfMonitorCommandProcessor(Console.Out);
            var commandLineArgs = new CommandLineArgs(commandProcessor);
            if (commandLineArgs.DoCommand == null)
            {
                // No command was given, this is an error, but help them out and show them the users guide. 
                Console.Error.WriteLine("Error: No PerfMonitor command given.   Launching Users guide.");
                commandProcessor.UsersGuide(commandLineArgs);
                return 1;
            }

            int ret = commandProcessor.ExecuteCommand(commandLineArgs);

            sw.Stop();
            Console.Error.WriteLine("PerfMonitor processing time: " + (sw.ElapsedMilliseconds / 1000.0).ToString("f3") + " secs.");
            return ret;
        }
        #endregion
    }

    /// <summary>
    /// CommandProcessor knows how to take a CommandLineArgs and do basic operations 
    /// 
    /// There is a method for every perfMonitor command.   To add a new command add 
    /// a method here as well as update code:CommandLineArgs.CommandLineArgs to actually
    /// call the method (and provide the help for it).  
    /// </summary>
    public class PerfMonitorCommandProcessor
    {
        public PerfMonitorCommandProcessor() { }
        public PerfMonitorCommandProcessor(TextWriter logFile) { m_logFile = logFile; }
        public int ExecuteCommand(CommandLineArgs parsedArgs)
        {
            try
            {
                parsedArgs.DoCommand(parsedArgs);
                return 0;
            }
            catch (Exception ex)
            {
                if (ex is ApplicationException || ex is FileNotFoundException || ex is DirectoryNotFoundException
                 || ex is IOException || ex is UnauthorizedAccessException || ex is CommandLineParserException
                 || ex is FastSerialization.SerializationException)
                {
                    // The above execeptions are meaningful to a human user, so we can just print them.
                    LogFile.WriteLine("Error: " + ex.Message);
                }
                else if (ex is ThreadAbortException)
                {
                    LogFile.WriteLine("Command Aborted by user.");
                }
                else
                {
                    // This are really internal programmer exceptions, but printing them is useful for debugging.  
                    LogFile.WriteLine("Exception: " + ex.ToString());
                    LogFile.WriteLine("An exceptional condition occured.");
                }
                return 1;
            }
        }
        public TextWriter LogFile
        {
            get
            {
                PollAbort();
                return m_logFile;
            }
            set { m_logFile = value; }
        }

        private CommandProcessor PerfViewCommandProcessor
        {
            get
            {
                if (m_PerfViewCommandProcessor == null)
                {
                    m_PerfViewCommandProcessor = new CommandProcessor();
                    m_PerfViewCommandProcessor.LogFile = LogFile;
                }
                return m_PerfViewCommandProcessor;
            }
        }
        PerfView.CommandProcessor m_PerfViewCommandProcessor;

        // Command line commands. 
        // This first group uses the PerfView logic.  
        public void Run(CommandLineArgs parsedArgs)
        {
            PerfViewCommandProcessor.Run(parsedArgs);
        }
        public void Collect(CommandLineArgs parsedArgs)
        {
            PerfViewCommandProcessor.Collect(parsedArgs);
        }
        public void Start(CommandLineArgs parsedArgs)
        {
            PerfViewCommandProcessor.Start(parsedArgs);
        }
        public void Stop(CommandLineArgs parsedArgs)
        {
            PerfViewCommandProcessor.Stop(parsedArgs);
        }
        public void Abort(CommandLineArgs parsedArgs)
        {
            PerfViewCommandProcessor.Abort(parsedArgs);
        }
        public void Merge(CommandLineArgs parsedArgs)
        {
            PerfViewCommandProcessor.Merge(parsedArgs);
        }
        public void ListSessions(CommandLineArgs parsedArgs)
        {
            PerfViewCommandProcessor.ListSessions(parsedArgs);
        }

        public void Analyze(CommandLineArgs parsedArgs) { ReportGenerator.Analyze(parsedArgs); }
        public void CpuTime(CommandLineArgs parsedArgs) { ReportGenerator.CpuTime(parsedArgs); }
        public void GCTime(CommandLineArgs parsedArgs) { ReportGenerator.GCTime(parsedArgs); }
        public void JitTime(CommandLineArgs parsedArgs) { ReportGenerator.JitTime(parsedArgs); }

        public void RunAnalyze(CommandLineArgs parsedArgs)
        {
            Run(parsedArgs);
            parsedArgs.DataFile = Path.ChangeExtension(parsedArgs.DataFile, ".etlx");
            Analyze(parsedArgs);
        }
        public void RunDump(CommandLineArgs parsedArgs)
        {
            var process = parsedArgs.Process;
            Run(parsedArgs);
            parsedArgs.Process = process;
            parsedArgs.DataFile = Path.ChangeExtension(parsedArgs.DataFile, ".etlx");
            Dump(parsedArgs);
        }

        public void ListSources(CommandLineArgs parsedArgs)
        {
            bool anySources = false;
            foreach (Type eventSource in EventSourceFinder.GetEventSourcesInFile(parsedArgs.ExeFile, true))
            {
                if (!anySources)
                {
                    Console.WriteLine("Event sources in " + parsedArgs.ExeFile + ".");
                    Console.WriteLine();
                    Console.WriteLine("EventSource Name                 EventSource Guid");
                    Console.WriteLine("-------------------------------------------------------------------------");
                }

                Console.WriteLine("{0,-30}: {1}", EventSourceFinder.GetName(eventSource), EventSourceFinder.GetGuid(eventSource));

                if (parsedArgs.DumpManifests != null)
                {
                    var manifest = EventSourceFinder.GetManifest(eventSource);
                    File.WriteAllText(Path.Combine(parsedArgs.DumpManifests, eventSource.Name + ".manifest.xml"), manifest);
                }
                anySources = true;
            }
            if (!anySources)
                Console.WriteLine("No event sources in " + parsedArgs.ExeFile + ".");
            else
            {
                Console.WriteLine();
                if (parsedArgs.DumpManifests != null)
                    Console.WriteLine("Manifests dumped in {0}", parsedArgs.DumpManifests);
                Console.WriteLine("You can these on with the /provider:@FileName#EventSourceName syntax.");
                Console.WriteLine("You can all of these on with the /provider:@FileName syntax.");
            }
        }
        public void Procs(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";

            Console.WriteLine("Processes that started within the trace:");
            TraceEventDispatcher dispatcher = ReportGenerator.GetSource(ref parsedArgs.DataFile);
            dispatcher.Kernel.ProcessStart += delegate(ProcessTraceData data)
            {
                Console.WriteLine("Process {0,5} Name: {1,-15} Start: {2:f3} msec", data.ProcessID, data.ProcessName, data.TimeStampRelativeMSec);
                Console.WriteLine("     CmdLine: {0}", data.CommandLine);
            };
            dispatcher.Process();
        }
        public void Stats(CommandLineArgs parsedArgs)
        {
            using (TraceEventDispatcher source = ReportGenerator.GetSource(ref parsedArgs.DataFile))
            {
                Console.WriteLine("Computing Stats for " + parsedArgs.DataFile);

                TextWriter output = Console.Out;
                var statsFileName = Path.ChangeExtension(parsedArgs.DataFile, "stats.xml");
                output = System.IO.File.CreateText(statsFileName);

                Console.WriteLine("Trace duration : " + source.SessionDuration);

                // Maps task X opcode -> count 
                EventStats eventStats = new EventStats(source);
                output.Write(eventStats.ToString());
                if (output != Console.Out)
                {
                    output.Close();
                    Console.WriteLine("Output in " + statsFileName);
                }
            }
        }

        public void Monitor(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.ClrEvents == ClrTraceEventParser.Keywords.Default)
                parsedArgs.ClrEvents = ClrTraceEventParser.Keywords.None;
            if (parsedArgs.KernelEvents == KernelTraceEventParser.Keywords.Default)
                parsedArgs.KernelEvents = KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ProcessCounters;
            if (parsedArgs.Providers == null)
                parsedArgs.Providers = new string[] { "@" };

            var process = parsedArgs.Process;
            Run(parsedArgs);
            parsedArgs.Process = process;
        }
        public void MonitorDump(CommandLineArgs parsedArgs)
        {
            Monitor(parsedArgs);
            parsedArgs.DataFile = Path.ChangeExtension(parsedArgs.DataFile, ".etlx");
            Dump(parsedArgs);
        }
        public void MonitorPrint(CommandLineArgs parsedArgs)
        {
            Monitor(parsedArgs);
            parsedArgs.DataFile = Path.ChangeExtension(parsedArgs.DataFile, ".etlx");
            PrintSources(parsedArgs);
        }

        private void SetEventsForMonitor(CommandLineArgs parsedArgs)
        {

        }

        /// <summary>
        /// Dumps a ETL or ETLX file as a XML stream of events.  Unlike 'Dump' this can work on
        /// an ETL file and only does little processing of the events and no symbolic lookup.  As
        /// a result it mostly used for debuggin PerfMonitor itself.  
        /// </summary>
        public void RawDump(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null && File.Exists("PerfMonitorOutput.etl"))
                parsedArgs.DataFile = "PerfMonitorOutput.etl";

            using (TraceEventDispatcher source = ReportGenerator.GetSource(ref parsedArgs.DataFile))
            {
                Console.WriteLine("Generating XML for " + parsedArgs.DataFile);
                Console.WriteLine("Trace duration : " + source.SessionDuration);

                var xmlFileName = Path.ChangeExtension(parsedArgs.DataFile, ".rawDump.xml");
                using (TextWriter output = System.IO.File.CreateText(xmlFileName))
                {
                    KernelTraceEventParser kernelSource = source.Kernel;
                    ClrTraceEventParser clrSource = source.Clr;
                    ClrRundownTraceEventParser clrRundownSource = new ClrRundownTraceEventParser(source);
                    ClrStressTraceEventParser clrStress = new ClrStressTraceEventParser(source);
                    SymbolTraceEventParser symbolSource = new SymbolTraceEventParser(source);
                    DynamicTraceEventParser dyamicSource = source.Dynamic;

                    if (source is ETWTraceEventSource)
                    {
                        // file names associated with disk I/O activity are logged at the end of the stream.  Thus to
                        // get file names, you need two passes
                        Console.WriteLine("PrePass to collect symbolic file name information.");
                        source.Process();
                    }

                    Console.WriteLine("Producing the XML output.");

                    output.WriteLine("<EventData>");
                    output.WriteLine(" <Header ");
                    output.WriteLine("   LogFileName=" + XmlUtilities.XmlQuote(parsedArgs.DataFile));
                    output.WriteLine("   EventsLost=" + XmlUtilities.XmlQuote(source.EventsLost));
                    output.WriteLine("   SessionStartTime=" + XmlUtilities.XmlQuote(source.SessionStartTime));
                    output.WriteLine("   SessionEndTime=" + XmlUtilities.XmlQuote(source.SessionEndTime));
                    output.WriteLine("   SessionDuration=" + XmlUtilities.XmlQuote((source.SessionDuration).ToString()));
                    output.WriteLine("   CpuSpeedMHz=" + XmlUtilities.XmlQuote(source.CpuSpeedMHz));
                    output.WriteLine("   NumberOfProcessors=" + XmlUtilities.XmlQuote(source.NumberOfProcessors));
                    output.WriteLine(" />");

                    output.WriteLine("<Events>");
                    bool dumpUnknown = true;        // TODO allow people to see this?
                    StringBuilder sb = new StringBuilder(1024);

                    long startTime = source.RelativeTimeMSecTo100ns(parsedArgs.StartTimeRelMsec);
                    long endTime = source.RelativeTimeMSecTo100ns(parsedArgs.EndTimeRelMsec);
                    bool timeFilter = (parsedArgs.StartTimeRelMsec > 0 || parsedArgs.EndTimeRelMsec < double.MaxValue);

                    Action<TraceEvent> Dumper = delegate(TraceEvent data)
                    {
                        if (timeFilter)
                        {
                            var timeStamp = data.TimeStamp100ns;
                            if (timeStamp < startTime)
                                return;
                            if (timeStamp > endTime)
                            {
                                source.StopProcessing();
                                return;
                            }
                        }
                        if (dumpUnknown && data is UnhandledTraceEvent)
                            output.WriteLine(data.Dump());
                        else
                            output.WriteLine(data.ToXml(sb).ToString());
                        sb.Length = 0;
                    };
                    var tdhParser = new RegisteredTraceEventParser(source);
                    tdhParser.All += Dumper;
                    kernelSource.All += Dumper;
                    clrRundownSource.All += Dumper;
                    clrSource.All += Dumper;
                    clrStress.All += Dumper;
                    symbolSource.All += Dumper;
                    source.Dynamic.All += Dumper;
                    source.UnhandledEvent += Dumper;
                    source.Process();

                    output.WriteLine("</Events>");
                    output.WriteLine("</EventData>");
                    Console.WriteLine("Output in " + xmlFileName);
                }
            }
        }
        public void Dump(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etlx";

            var xmlFileName = Path.ChangeExtension(parsedArgs.DataFile, ".dump.xml");

            using (StreamWriter writer = System.IO.File.CreateText(xmlFileName))
            {
                TraceLog log = TraceLog.OpenOrConvert(parsedArgs.DataFile, null);
                TraceEvents events = log.Events;

                if (parsedArgs.Process != null)
                {
                    TraceProcess process = null;
                    int pid;
                    if (int.TryParse(parsedArgs.Process, out pid))
                        process = log.Processes.LastProcessWithID(pid);
                    else
                        process = log.Processes.FirstProcessWithName(parsedArgs.Process, log.SessionStartTime100ns + 1);
                    if (process == null)
                        throw new ApplicationException("Could not find a process named " + parsedArgs.Process);
                    Console.WriteLine("Filtering to process {0} ({1}).  Started at {1:f3} msec.", process.Name,
                        process.ProcessID, process.StartTimeRelativeMsec);
                    events = process.EventsInProcess;
                }

                Console.WriteLine("Converting " + log.FilePath + " to an XML file.");
                writer.WriteLine("<TraceLog>");
                writer.WriteLine(log.ToString());
                writer.WriteLine("<Events>");
                EventStats eventStats = new EventStats();
                StringBuilder sb = new StringBuilder();
                foreach (TraceEvent anEvent in events)
                {
                    eventStats.Increment(anEvent);
                    string eventXml = anEvent.ToString();
                    TraceCallStack callStack = anEvent.CallStack();
                    bool opened = false;
                    if (callStack != null)
                    {
                        sb.Length = 0;
                        writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                        writer.WriteLine("  <StackTrace>");
                        writer.Write(callStack.ToString(sb).ToString());
                        writer.WriteLine("  </StackTrace>");
                        opened = true;
                    }
                    else
                    {
                        SampledProfileTraceData sample = anEvent as SampledProfileTraceData;
                        if (sample != null)
                        {
                            if (!opened)
                                writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                            opened = true;
                            writer.WriteLine(sample.IntructionPointerCodeAddressString());
                        }
                        PageFaultTraceData pageFault = anEvent as PageFaultTraceData;
                        if (pageFault != null)
                        {
                            if (!opened)
                                writer.WriteLine(XmlUtilities.OpenXmlElement(eventXml));
                            opened = true;
                            writer.WriteLine(pageFault.ProgramCounterAddressString());
                        }
                    }

                    if (opened)
                        writer.WriteLine("</Event>");
                    else
                        writer.WriteLine(eventXml);
                }

                writer.WriteLine("</Events>");

                // Write the event statistics. 
                writer.Write(eventStats.ToString());

                // Dump the summary information in the log
                DumpLogData(log, writer);
                writer.WriteLine("</TraceLog>");
            }
            Console.WriteLine("Output in " + xmlFileName);
        }
        public void Etlx(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";

            LogFile.WriteLine("Converting data files to ETLX format.\n");
            Stopwatch sw = Stopwatch.StartNew();
            var etlxFileName = TraceLog.CreateFromETL(parsedArgs.DataFile);
            sw.Stop();
            LogFile.WriteLine("ETLX convertion took {0:f3} sec.", sw.Elapsed.TotalSeconds);
            LogFile.WriteLine("ETLX output in {0}.\n", Path.GetFileName(etlxFileName));
        }
        public void PrintSources(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null && File.Exists("PerfMonitorOutput.etl"))
                parsedArgs.DataFile = "PerfMonitorOutput.etl";

            TextWriter writer = Console.Out;
            if (parsedArgs.OutputFile != null)
            {
                Console.WriteLine("Output written to {0}", parsedArgs.OutputFile);
                writer = File.CreateText(parsedArgs.OutputFile);
            }

            using (TraceEventDispatcher source = ReportGenerator.GetSource(ref parsedArgs.DataFile))
                PrintEventSourcesAsText(source, source, writer);

            if (writer != Console.Out)
                writer.Close();
        }
        public void Listen(CommandLineArgs parsedArgs)
        {
            // Assume you are only doing EventSources, so you don't have to specify the @ if you don't want to.  
            for (int i = 0; i < parsedArgs.Providers.Length; i++)
            {
                if (!parsedArgs.Providers[i].StartsWith("@") && !parsedArgs.Providers[i].StartsWith("*"))
                    parsedArgs.Providers[i] = "@" + parsedArgs.Providers[i];
            }

            TextWriter writer = Console.Out;
            if (parsedArgs.OutputFile != null)
            {
                Console.WriteLine("Output written to {0}", parsedArgs.OutputFile);
                writer = File.CreateText(parsedArgs.OutputFile);
            }

            TraceEventSession session = null;
            TraceEventSession kernelSession = null;

            Console.WriteLine("Monitoring Event source, Ctrl-C to stop");
            // Use Control-C to stop things.  
            Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate
            {
                if (session != null)
                    session.Stop();
                if (kernelSession != null)
                    kernelSession.Stop();
                if (writer != Console.Out)
                    writer.Close();
                Environment.Exit(0);
            });

            try
            {
                // Get kernel source (for process starts)
                kernelSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName, null);
                kernelSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ProcessCounters);
                var kernelSource = new ETWTraceEventSource(KernelTraceEventParser.KernelSessionName, TraceEventSourceType.Session);

                // Get source for user events. 
                session = new TraceEventSession(s_UserModeSessionName, null);
                session.StopOnDispose = true;

                // TODO Instantiating a new CommandProcessor is a bit of a hack.  
                var commandProcessor = new CommandProcessor();
                commandProcessor.LogFile = writer;
                commandProcessor.EnableAdditionalProviders(session, parsedArgs.Providers, null);
                var source = new ETWTraceEventSource(session.SessionName, TraceEventSourceType.Session);

                PrintEventSourcesAsText(source, kernelSource, Console.Out);
            }
            finally
            {
                if (session != null)
                    session.Dispose();
            }
        }

        public void CpuTest(CommandLineArgs parsedArgs)
        {
            Console.WriteLine("Calling DateTime.Now until 5 seconds elapses.");
            int count = Spinner.RecSpin(5);
            Console.WriteLine("Executed {0:f3} Meg iterations in 5 seconds", count / 1000000.0);
        }
        public void UsersGuide(CommandLineArgs parsedArgs)
        {
            string tempDir = Environment.GetEnvironmentVariable("TEMP");
            if (tempDir == null)
                tempDir = ".";
            string helpHtmlFileName = Path.Combine(tempDir, "PerfMonitorUsersGuide.htm");
            if (!ResourceUtilities.UnpackResourceAsFile(@".\UsersGuide.htm", helpHtmlFileName))
                Console.WriteLine("No Users guide available");
            else
                new Command("iexplore \"" + Path.GetFullPath(helpHtmlFileName) + "\"", new CommandOptions().AddStart());
        }

        /// <summary>
        /// Dump the EventSource messages from 'source' to 'writer' as human readable text (like printf)
        /// 
        /// kernelSource is also passed for real time sources, for ETL files set kernelSource==soruce.  
        /// </summary>
        private void PrintEventSourcesAsText(TraceEventDispatcher source, TraceEventDispatcher kernelSource, TextWriter writer)
        {
            // This gets adjusted to be relative to process start.  
            var zeroTime = source.SessionStartTime100ns;
            // Remember where processes start
            var processStarts = new Dictionary<int, ProcessTraceData>();
            // link up start and stop events.  
            var startEvents = new Dictionary<TraceEvent, long>(new StartEventComparer());

            var sequencer = new Sequencer(source.SessionStartTime100ns);

            StringBuilder sb = new StringBuilder();

            Action<TraceEvent> dumper = delegate(TraceEvent data)
            {
                try
                {
                    // Insure that the kernel and non-kernel callbacks are in time order and execute one at a time.  
                    sequencer.WaitForTurnAndLock(0, data.TimeStamp100ns);

                    ProcessTraceData processStart;
                    if (processStarts.TryGetValue(data.ProcessID, out processStart) && processStart != null)
                    {
                        // Times are from process start 
                        zeroTime = processStart.TimeStamp100ns;

                        var processStartMessage = processStart.CommandLine;
                        if (processStartMessage.Length == 0)
                            processStartMessage = processStart.ProcessName;
                        writer.WriteLine("{0,12:f3} {1,4} {2,-16} {3}", 0.0, data.ProcessID, "ProcessStart", processStartMessage);
                    }
                    processStarts[data.ProcessID] = null;       // mark that it is a process of interest

                    var message = data.FormattedMessage;
                    if (message == null)
                    {
                        if (data.EventName == "ManifestDump")
                            return;
                        sb.Length = 0;
                        var fieldNames = data.PayloadNames;
                        for (int i = 0; i < fieldNames.Length; i++)
                            sb.Append(fieldNames[i]).Append('=').Append(data.PayloadString(i)).Append(' ');
                        message = sb.ToString();
                    }

                    // Compute duration if possible 
                    if (data.Opcode == TraceEventOpcode.Start)
                        startEvents[data.Clone()] = data.TimeStamp100ns;
                    else if (data.Opcode == TraceEventOpcode.Stop)
                    {
                        long startTime;
                        if (startEvents.TryGetValue(data, out startTime))
                        {
                            startEvents.Remove(data);
                            var durationMsec = (data.TimeStamp100ns - startTime) / 10000.0;
                            message = string.Format("{0} Duration={1:f3} MSec", message, durationMsec);
                        }
                    }
                    var timeRelMsec = (data.TimeStamp100ns - zeroTime) / 10000.0;   // convert 100ns to Msec
                    writer.WriteLine("{0,12:f3} {1,4} {2,-16} {3}", timeRelMsec, data.ProcessID, data.EventName, message);
                }
                finally { sequencer.Unlock(); }
            };

            // Add the dumper to all providers whose manifest is emedded in the data stream
            source.Dynamic.All += dumper;
            // And all 'OS declared manifests'
            var tdhParser = new RegisteredTraceEventParser(source);
            tdhParser.All += dumper;

            kernelSource.Kernel.ProcessStart += delegate(ProcessTraceData data)
            {
                try
                {
                    // Insure that the kernel and non-kernel callbacks are in time order and execute one at a time.  
                    sequencer.WaitForTurnAndLock(1, data.TimeStamp100ns);

                    var me = (ProcessTraceData)data.Clone();
                    processStarts[data.ProcessID] = me;
                    startEvents[me] = data.TimeStamp100ns;
                }
                finally { sequencer.Unlock(); }
            };
            kernelSource.Kernel.ProcessEnd += delegate(ProcessTraceData data)
            {
                try
                {
                    // Insure that the kernel and non-kernel callbacks are in time order and execute one at a time.  
                    sequencer.WaitForTurnAndLock(1, data.TimeStamp100ns);

                    // Is this a process of interest (we published some events)
                    ProcessTraceData processStart;
                    if (processStarts.TryGetValue(data.ProcessID, out processStart) && processStart == null)
                    {
                        var message = "";
                        long startTime;
                        if (startEvents.TryGetValue(data, out startTime))
                        {
                            startEvents.Remove(data);
                            var durationMsec = (data.TimeStamp100ns - startTime) / 10000.0;
                            message = string.Format("Duration={0:f3}", durationMsec);
                        }
                        var timeRelMsec = (data.TimeStamp100ns - zeroTime) / 10000.0;   // convert 100ns to Msec
                        writer.WriteLine("{0,12:f3} {1,4} {2,-16} {3}", timeRelMsec, data.ProcessID, data.EventName, message);
                    }
                }
                finally { sequencer.Unlock(); }
            };
            kernelSource.Kernel.ProcessPerfCtr += delegate(ProcessCtrTraceData data)
            {
                try
                {
                    // Insure that the kernel and non-kernel callbacks are in time order and execute one at a time.  
                    sequencer.WaitForTurnAndLock(1, data.TimeStamp100ns);

                    // Is this a process of interest (we published some events
                    ProcessTraceData processStart;
                    if (processStarts.TryGetValue(data.ProcessID, out processStart) && processStart == null)
                    {
                        var timeRelMsec = (data.TimeStamp100ns - zeroTime) / 10000.0;   // convert 100ns to Msec
                        var message = string.Format("PeakWS={0:f1}MB", data.PeakWorkingSetSize / 1000000.0);
                        writer.WriteLine("{0,12:f3} {1,4} {2,-16} {3}", timeRelMsec, data.ProcessID, data.EventName, message);
                    }
                }
                finally { sequencer.Unlock(); }
            };

            if (kernelSource != source)
                writer.WriteLine("Listening for events.  6 second delay because of buffering...");
            writer.WriteLine();
            writer.WriteLine("  Time Msec  PID  Event Name       Message");
            writer.WriteLine("-----------------------------------------------------------------------------");

            if (kernelSource != source)
            {
                // Wait for kernel events 
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    kernelSource.Process();
                });
            }
            // Wait for user events
            source.Process();
        }

        /// <summary>
        /// Sequencer allows us to interleave more than one real time TraceEventSource and keep the time order chronological.  
        /// </summary>
        class Sequencer
        {
            const long maxFlushDelay100ns = 60000000;   // Wait 6 seconds
            // long sessionStart100ns;

            public Sequencer(long sessionStart100ns)
            {
                // this.sessionStart100ns = sessionStart100ns;
                // Console.WriteLine("Seqencer: unblocked up to {0:f3}", (DateTime.Now.ToFileTime() - sessionStart100ns - maxFlushDelay100ns) / 10000.0);
            }
            public void WaitForTurnAndLock(int streamNumber, long fileTime100nsec)
            {
                System.Threading.Monitor.Enter(this);
                if (streamNumber == 0)
                {
                    //Console.WriteLine("Seqencer: {0} ENTER {1:f3}", 0, (fileTime100nsec - sessionStart100ns) / 10000.0);
                    stream0NextEventTime100ns = fileTime100nsec;
                    for (; ; )
                    {
                        if (fileTime100nsec < stream1NextEventTime100ns)
                        {
                            //Console.WriteLine("Seqencer: {0} LEAVE {1:f3}", 0, (fileTime100nsec - sessionStart100ns) / 10000.0);
                            return;
                        }
                        Wait(stream1NextEventTime100ns < blockUntilTime100ns);
                    }
                }
                else
                {
                    //Console.WriteLine("Seqencer: {0} ENTER {1:f3}", 1, (fileTime100nsec - sessionStart100ns) / 10000.0);
                    Debug.Assert(streamNumber == 1);    // Current we only handle 2 streams
                    stream1NextEventTime100ns = fileTime100nsec;
                    for (; ; )
                    {
                        if (fileTime100nsec < stream0NextEventTime100ns)
                        {
                            //Console.WriteLine("Seqencer: {0} LEAVE {1:f3}", 1, (fileTime100nsec - sessionStart100ns) / 10000.0);
                            return;
                        }
                        Wait(stream0NextEventTime100ns < blockUntilTime100ns);
                    }
                }
            }
            public void Unlock()
            {
                System.Threading.Monitor.Exit(this);
            }

            #region private
            private void Wait(bool shortWait)
            {
                System.Threading.Monitor.Exit(this);
                if (shortWait)
                {
                    //Console.WriteLine("Seqencer: YIELD");
                    Thread.Sleep(0);
                    System.Threading.Monitor.Enter(this);
                }
                else
                {
                    //Console.WriteLine("Seqencer: WAIT START");
                    Thread.Sleep(100);
                    System.Threading.Monitor.Enter(this);
                    long blockUntilTime100ns = DateTime.Now.ToFileTime() - maxFlushDelay100ns;
                    //Console.WriteLine("Seqencer: WAIT DONE Unblocked up to {0:f3}", (blockUntilTime100ns - sessionStart100ns) / 10000.0);

                    if (stream0NextEventTime100ns < blockUntilTime100ns)
                        stream0NextEventTime100ns = blockUntilTime100ns;
                    if (stream1NextEventTime100ns < blockUntilTime100ns)
                        stream1NextEventTime100ns = blockUntilTime100ns;
                }
            }

            long blockUntilTime100ns;
            long stream0NextEventTime100ns;
            long stream1NextEventTime100ns;
            #endregion
        }

        /// <summary>
        /// A comparer used to identify a Start and Stop event (basically if the tasks match and the
        /// work item ID matches (defaults to thread ID)
        /// </summary>
        class StartEventComparer : IEqualityComparer<TraceEvent>
        {
            public bool Equals(TraceEvent x, TraceEvent y)
            {
                if (x.ProviderGuid != y.ProviderGuid && x.Task != y.Task || x.ProcessID != y.ProcessID)
                    return false;
                var xID = x.PayloadByName("ID");
                var yID = y.PayloadByName("ID");
                if (xID == null && yID == null)
                    return x.ThreadID == y.ThreadID;
                return xID.Equals(yID);
            }
            public int GetHashCode(TraceEvent obj)
            {
                var ret = (int)obj.Task;
                var id = obj.PayloadByName("ID");
                if (id != null)
                    ret += id.GetHashCode();
                else
                    ret += obj.ThreadID;
                return ret;
            }
        }

        // Experimental 
        public void MonitorProcs(CommandLineArgs parsedArgs)
        {
            Console.WriteLine("Monitoring Processes, Ctrl-C to stop");

            // Start the session as a Real time monitoring session
            TraceEventSession session = new TraceEventSession(KernelTraceEventParser.KernelSessionName, null);

            // Use Control-C to stop things.  
            Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate
            {
                Console.WriteLine("Stoping tracing");
                session.Stop();
                Console.WriteLine("Done");
                Environment.Exit(0);
            });

            // OK offset collecting
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

            // Start monitoring.  
            Dictionary<int, ProcessTraceData> liveProcesses = new Dictionary<int, ProcessTraceData>();
            DateTime start = DateTime.Now;

            ETWTraceEventSource source = new ETWTraceEventSource(KernelTraceEventParser.KernelSessionName, TraceEventSourceType.Session);

            Action<ProcessTraceData> onPStart = delegate(ProcessTraceData data)
            {
                Console.WriteLine("********** IN PSTART ****************");
                TimeSpan relativeTime = data.TimeStamp - start;
                liveProcesses[data.ProcessID] = (ProcessTraceData)data.Clone();
                Console.WriteLine(@"{0}{1}: {{ At({2}) Parent({3}) Cmd: {4}",
                    Indent(liveProcesses, data.ProcessID),
                    data.ProcessID, relativeTime, data.ParentID, data.CommandLine);

                Trace.WriteLine("Got PStart");
            };
            source.Kernel.ProcessStart += onPStart;

            source.Kernel.ProcessDCStart += delegate(ProcessTraceData data)
            {
                TimeSpan relativeTime = data.TimeStamp - start;
                // liveProcesses[data.ProcessID] = (ProcessTraceData)data.Clone();

                Console.WriteLine(@"{0}{1}: {{ At({2}) Parent({3}) Cmd: {4}",
                    Indent(liveProcesses, data.ProcessID),
                    data.ProcessID, relativeTime, data.ParentID, data.CommandLine);

                Trace.WriteLine("Got Process Data Collect Start");
            };

            source.Kernel.ProcessEnd += delegate(ProcessTraceData data)
            {
                TimeSpan relativeTime = data.TimeStamp - start;
                ProcessTraceData startData;
                if (liveProcesses.TryGetValue(data.ProcessID, out startData))
                {
                    TimeSpan processDuration = data.TimeStamp - startData.TimeStamp;
                    Console.WriteLine("{0}{1}: }} At({2}) Exit(0x{3:x}) Duration({4}) ",
                        Indent(liveProcesses, data.ProcessID), data.ProcessID, relativeTime, data.ExitStatus, processDuration);
                }
            };  

            source.UnhandledEvent += delegate(TraceEvent data)
            {
                Console.WriteLine("Got unhandled event: " + data.ToString());
            };

            // session.CaptureKernelState(KernelTraceEventParser.Keywords.Process);

            source.Process();
            Console.WriteLine("Processing Complete");
        }
        private static string Indent(Dictionary<int, ProcessTraceData> liveProcesses, int processId)
        {
            int indent = 0;
            ProcessTraceData startData;
            while (liveProcesses.TryGetValue(processId, out startData))
            {
                processId = startData.ParentID;
                indent++;
            }
            if (indent > 0)
                --indent;

            return new string(' ', indent * 2);
        }

        #region private
        private static void DumpLogData(TraceLog log, TextWriter stream)
        {
            stream.WriteLine("  <TraceModuleFiles>");
            foreach (TraceModuleFile moduleFile in log.ModuleFiles)
            {
                stream.WriteLine("      " + moduleFile);
            }
            stream.WriteLine("  </TraceModuleFiles>");
            stream.WriteLine("<TraceProcesses>");
            foreach (TraceProcess process in log.Processes)
            {
                stream.WriteLine("  " + XmlUtilities.OpenXmlElement(process.ToString()));

                stream.WriteLine("    <TraceThreads>");
                foreach (TraceThread thread in process.Threads)
                {
                    stream.WriteLine("      " + XmlUtilities.OpenXmlElement(thread.ToString()));
                    stream.WriteLine("        </TraceThread>");
                }
                stream.WriteLine("    </TraceThreads>");

                stream.WriteLine("    <TraceLoadedModules>");
                foreach (TraceLoadedModule module in process.LoadedModules)
                {
                    TraceManagedModule asManaged = module as TraceManagedModule;
                    if (asManaged != null && asManaged.NativeModule == null)
                    {
                        stream.WriteLine("      " + XmlUtilities.OpenXmlElement(module.ToString()));
                        stream.WriteLine("      </TraceManagedModule>");
                    }
                    else
                        stream.WriteLine("      " + module);
                }
                stream.WriteLine("    </TraceLoadedModules>");
                stream.WriteLine("  </TraceProcess>");

            }
            stream.WriteLine("</TraceProcesses>");
        }

        // TODO decide what to do with this. 
        /// <summary>
        /// Sets up what to do when Ctrl-C entered
        /// </summary>
        private void AbortOnCtrlCSetup()
        {
            Thread mainThread = Thread.CurrentThread;
            int controlCPressed = 0;
            Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate(object sender, ConsoleCancelEventArgs e)
            {
                m_logFile.WriteLine("Ctrl-C entered...");
                s_abortRequest = true;
                for (; ; )
                {
                    if (Interlocked.CompareExchange(ref controlCPressed, 1, 0) == 0)
                    {
                        if (s_forceAbortOnCtrlC)
                            Abort(null);
                        Environment.Exit(1);
                    }
                    Thread.Sleep(100);
                }
            });
        }
        /// <summary>
        /// Check if a Ctrl-C has been entered, and if so raise an ThreadInterruptedException. 
        /// </summary>
        void PollAbort() { if (s_abortRequest) throw new ThreadInterruptedException(); }

        TextWriter m_logFile = Console.Out;      // Where to send outputStream to the user

        private bool s_abortRequest;         // A ctrl-C was hit 
        private bool s_forceAbortOnCtrlC;    // We definately need an abort on a Ctrl-C

        private static string s_UserModeSessionName = "PerfMonitorSession";
        #endregion
    }

    /// <summary>
    /// The code:CommandLine class holds the parsed form of all the commandLine line arguments.  It is
    /// intialized by handing it the 'args' array for main, and it has a public field for each named argument.
    /// code:#CommandLineDefinitions for the code that defines the arguments (and the help strings associated 
    /// with them). 
    /// 
    /// See code:CommandLineParser for more on parser itself.   
    /// </summary>
    public class CommandLineArgs
    {
        public CommandLineArgs(PerfMonitorCommandProcessor commandProcessor)
        {
            CommandProcessor = commandProcessor;
            CommandLineParser.ParseForConsoleApplication(delegate(CommandLineParser parser)
            {
                // #CommandLineDefinitions
                parser.ParameterSetsWhereQualifiersMustBeFirst = new string[] { "run", "runAnalyze" };
                parser.NoDashOnParameterSets = true;

                // These apply to start, collect and run
                parser.DefineOptionalQualifier("BufferSize", ref BufferSize,
                    "The size the buffers (in MB) the OS should use.  Increase this if PerfMonitor warns you that the log lost events.");
                parser.DefineOptionalQualifier("Circular", ref Circular, "Do Circular logging with a file size in MB.  Zero means non-circular.");

                // These apply to Stop Collect and Run 
                parser.DefineOptionalQualifier("Merge", ref Merge, "Do a merge after stopping collection (/merge:false turns off).");
                parser.DefineOptionalQualifier("Etlx", ref Etlx, "Convert to etlx (containes symbolic information) after stopping collection.");
                parser.DefineOptionalQualifier("NoRundown", ref NoRundown, "Don't request CLR Rundown events.");
                parser.DefineOptionalQualifier("NoNGenRundown", ref NoNGenRundown, "Don't request CLR Rundown events for NGEN images.");
                parser.DefineOptionalQualifier("ForceNgenRundown", ref ForceNgenRundown,
                    "By default on V4.0 runtimes NGEN rundown is supressed, because NGEN pdbs are a less expensive way of getting symbolic " +
                    "information for NGEN images.  This option forces NGEN rundown, so NGEN pdbs are not needed.  This can be useful " +
                    "in some scenarios where NGEN Pdbs are not working properly.");
                parser.DefineOptionalQualifier("RundownTimeout", ref RundownTimeout,
                    "Maximum number of seconds to wait for CLR rundown to complete.");
                parser.DefineOptionalQualifier("MinRundownTime", ref MinRundownTime,
                    "Minimum number of seconds to wait for CLR rundown to complete.");
                parser.DefineOptionalQualifier("SymbolsForDlls", ref SymbolsForDlls,
                    "A comma separated list of DLL names (without extension) for which to look up symbolic informaotion (PDBs).");
                parser.DefineOptionalQualifier("DataFile", ref DataFile, "FileName of the profile data to generate.");
                parser.DefineOptionalQualifier("Providers", ref Providers,
                    "Providers in ADDITION to the kernel and CLR providers.  This is comma separated list of ProviderGuid:Keywords:Level specs.");
                parser.DefineOptionalQualifier("ClrEvents", ref ClrEvents,
                    "A comma separated list of .NET CLR events to turn on.  See Users guide for details.");
                parser.DefineOptionalQualifier("KernelEvents", ref KernelEvents,
                    "A comma separated list of windows OS kernel events to turn on.  See Users guide for details.");

                parser.DefineOptionalQualifier("NoBrowser", ref NoBrowser, "Don't launch a browser on an HTML report.");
                parser.DefineOptionalQualifier("Process", ref Process,
                    "Filter events to just one process with the given name (exe without extension) or numeric process ID.");
                parser.DefineOptionalQualifier("StartTime", ref StartTimeRelMsec,
                    "Filter events happening before this time (msec from start of trace.");
                parser.DefineOptionalQualifier("EndTime", ref EndTimeRelMsec,
                    "Filter events happening after this time (msec from start of trace.");
                parser.DefineOptionalQualifier("Threshold", ref ThresholdPercent,
                    "Stack items less than this % threashold are folded into their parent node.");
                parser.DefineOptionalQualifier("NoOSGrouping", ref NoOSGrouping,
                    "Turn off the grouping of OS functions in stack traces.");
                parser.DefineOptionalQualifier("NoBrowser", ref NoBrowser, "Don't launch a browser on an HTML report.");
                parser.DefineOptionalQualifier("StopTrigger", ref StopTrigger,
                    "This is of the form CATEGORY:COUNTERNAME:INSTANCE OP NUM  where CATEGORY:COUNTERNAME:INSTANCE, identify " +
                    "a performance counter (same as PerfMon), OP is either < or >, and NUM is a number.  " +
                    "When that condition is true then collection will stop.");
                parser.DefineOptionalQualifier("MaxCollectSec", ref MaxCollectSec,
                    "Turn off collection (and kill the program if perfView started it) after this many seconds. Zero means no timeout.");
                parser.DefineOptionalQualifier("Zip", ref Zip, "Zip the ETL file (implies /Merge).");

                parser.DefineOptionalQualifier("WriteXml", ref WriteXml, "Write an XML report as well as the HTML report.");

                parser.DefineParameterSet("runAnalyze", ref DoCommand, CommandProcessor.RunAnalyze,
                    "Performs a run command then the analyze command.");
                parser.DefineParameter("CommandAndArgs", ref CommandAndArgs, "Command to run and arguments.");

                parser.DefineParameterSet("runDump", ref DoCommand, CommandProcessor.RunDump,
                    "Performs a run command then the Dump command.");
                parser.DefineParameter("CommandAndArgs", ref CommandAndArgs, "Command to run and arguments.");

                parser.DefineParameterSet("monitor", ref DoCommand, CommandProcessor.Monitor,
                    "Turns on all event sources, performs a run command.");
                parser.DefineParameter("CommandAndArgs", ref CommandAndArgs, "Command to run and arguments.");

                parser.DefineParameterSet("monitorDump", ref DoCommand, CommandProcessor.MonitorDump,
                    "Turns on all event sources, performs a run command then the Dump command.");
                parser.DefineParameter("CommandAndArgs", ref CommandAndArgs, "Command to run and arguments.");

                parser.DefineParameterSet("monitorPrint", ref DoCommand, CommandProcessor.MonitorPrint,
                    "Turns on all event sources, performs a run command then the PrintSources command.");
                parser.DefineParameter("CommandAndArgs", ref CommandAndArgs, "Command to run and arguments.");

                parser.DefineParameterSet("run", ref DoCommand, CommandProcessor.Run, "Starts data collection, runs a command and stops.");
                parser.DefineParameter("CommandAndArgs", ref CommandAndArgs, "Command to run and arguments.");

                parser.DefineParameterSet("analyze", ref DoCommand, CommandProcessor.Analyze,
                    "Creates a general performance report report (CPU, GC, JIT ...).");
                parser.DefineOptionalQualifier("DataFile", ref DataFile, "FileName of the profile data to analyze.");

                parser.DefineParameterSet("GCTime", ref DoCommand, CommandProcessor.GCTime, "Creates a Report on .NET Just In Time compilation..");
                parser.DefineOptionalQualifier("DataFile", ref DataFile, "FileName of the profile data to analyze.");

                parser.DefineParameterSet("CpuTime", ref DoCommand, CommandProcessor.CpuTime, "Creates a Report on CPU usage time.");
                parser.DefineOptionalQualifier("DataFile", ref DataFile, "FileName of the profile data to analyze.");

                parser.DefineParameterSet("JitTime", ref DoCommand, CommandProcessor.JitTime, "Creates a Report on .NET Just In Time compilation.");
                parser.DefineOptionalQualifier("DataFile", ref DataFile, "FileName of the profile data to analyze.");

                parser.DefineParameterSet("collect", ref DoCommand, CommandProcessor.Collect,
                    "Starts data logging, then displayes a messagebox.   When dismissed, it then stops collection.");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

                parser.DefineParameterSet("start", ref DoCommand, CommandProcessor.Start,
                    "Starts machine wide profile data collection.");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

                parser.DefineParameterSet("stop", ref DoCommand, CommandProcessor.Stop,
                    "Stop collecting profile data (machine wide).");

                parser.DefineParameterSet("abort", ref DoCommand, CommandProcessor.Abort,
                    "Insures that any active PerfMonitor sessions are stopped.");

                parser.DefineParameterSet("merge", ref DoCommand, CommandProcessor.Merge,
                    "Combine separate ETL files into a single ETL file (that can be decoded on another machine).");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

                parser.DefineParameterSet("stats", ref DoCommand, CommandProcessor.Stats,
                    "Produce an XML report of what events are in an ETL file.");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

                parser.DefineParameterSet("procs", ref DoCommand, CommandProcessor.Procs,
                    "Display the processes that started in the trace.");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

                parser.DefineParameterSet("dump", ref DoCommand, CommandProcessor.Dump,
                    "Convert the events to an XML file.");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

                parser.DefineParameterSet("rawDump", ref DoCommand, CommandProcessor.RawDump,
                    "Convert the events with no preprocessing (thus stacks are individual events, and rundown events are shown.");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

                parser.DefineParameterSet("listSessions", ref DoCommand, CommandProcessor.ListSessions,
                    "Lists active ETW sessions.");

                parser.DefineParameterSet("listSources", ref DoCommand, CommandProcessor.ListSources,
                    "Lists all System.Diagnostics.EventSources in a executable (or its static dependencies)");
                parser.DefineOptionalQualifier("DumpManifests", ref DumpManifests,
                    "Specifies the directory where dump eventSource manifests files will be place.");
                parser.DefineParameter("ExeFile", ref ExeFile, "The EXE (or DLL) in which to look for event sources.");

                parser.DefineParameterSet("monitorProcs", ref DoCommand, CommandProcessor.MonitorProcs,
                    "EXPERIMENTAL: Monitor process creation in real time.");

                parser.DefineParameterSet("etlx", ref DoCommand, CommandProcessor.Etlx,
                    "Create a ETLX file from the ETL files.");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

                parser.DefineParameterSet("cpuTest", ref DoCommand, CommandProcessor.CpuTest,
                    "Run a simple, CPU bound routine.   Useful as a tutorial example.");

                parser.DefineParameterSet("PrintSources", ref DoCommand, CommandProcessor.PrintSources,
                    "Takes an ETL file and generates a text file that prints EventSource events as formatted strings.");
                parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");
                parser.DefineOptionalQualifier("OutputFile", ref OutputFile, "The file name to write the text output.  Stdout is the default.");

                parser.DefineParameterSet("Listen", ref DoCommand, CommandProcessor.Listen,
                    "Turns on ETW logging for EventSources (providers), and then writes text file (by default to stdout).");
                parser.DefineOptionalQualifier("OutputFile", ref OutputFile, "The file name to write the text output.  Stdout is the default.");
                parser.DefineParameter("Providers", ref Providers, "The Providers (EventSources) to turn on before listening");

                parser.DefineParameterSet("UsersGuide", ref DoCommand, CommandProcessor.UsersGuide, "Displays the users Guide.");

                parser.DefineDefaultParameterSet(
                    "PerfMonitor is a tool for quickly and easily collecting ETW performance data and generating useful reports.  " +
                    "Please use 'PerfMonitor usersGuide' command for more details.  ");
            });

            if (CommandAndArgs != null)
                CommandLine = CommandLineUtilities.FormCommandLineFromArguments(CommandAndArgs, 0);
        }
        public PerfMonitorCommandProcessor CommandProcessor { get; private set; }

        // The command to execute (determined by the parameter set)
        public Action<CommandLineArgs> DoCommand;

        // options common to multiple commands
        public string DataFile;
        public string LogFile;
        public string Process;              // A process name to focus on.  

        // ListSources options 
        public string ExeFile;
        public string DumpManifests;

        // Listen options
        public string OutputFile;

        // run options 
        public string[] CommandAndArgs;     // This is broken up into words
        public string CommandLine;          // This is a one long string.  

        // Start options.
        public int BufferSize = 64;         // Megabytes of buffer size (this is a good default). 
        public int Circular;
        public KernelTraceEventParser.Keywords KernelEvents = KernelTraceEventParser.Keywords.Default;
        public ClrTraceEventParser.Keywords ClrEvents = ClrTraceEventParser.Keywords.Default;
        public string[] Providers;          // Additional providers to turn on.   

        // Stop options.  
        public bool NoRundown;
        public bool NoNGenRundown;
        public bool ForceNgenRundown;
        public bool Merge = true;
        public bool Etlx;
        public int RundownTimeout = 30;
        public int MinRundownTime;
        public string[] SymbolsForDlls;
        public bool Zip = true;
        public string StopTrigger;
        public int MaxCollectSec;

        // options for cpuTime
        public double StartTimeRelMsec;
        public double EndTimeRelMsec = double.PositiveInfinity;
        public float ThresholdPercent = 5;
        public bool NoOSGrouping;

        // options for A, CPUStats, GCStats JitStats
        public bool NoBrowser;    // Don't launch IE on the report. 

        // TODO review these are currently not connected.  
        public bool WriteXml;         // (also write out XML) // TODO decide if we want to expose this, or rip it out.  
        public bool LineNumbers;
        public bool SymDebug;
    };

    /// <summary>
    /// This class is simply a example of a CPU bound computation.   It does it recurisively to 
    /// make the example more interesting.  
    /// </summary>
    class Spinner
    {
        public static int aStatic = 0;

        // Spin for 'timeSec' seconds.   We do only 1 second in this
        // method, doing the rest in the helper.   
        public static int RecSpin(int timeSec)
        {
            if (timeSec <= 0)
                return 0;
            --timeSec;
            return SpinForASecond() + RecSpinHelper(timeSec);
        }

        // RecSpinHelper is a clone of RecSpin.   It is repeated 
        // to simulate mutual recursion (more interesting example)
        static int RecSpinHelper(int timeSec)
        {
            if (timeSec <= 0)
                return 0;
            --timeSec;
            return SpinForASecond() + RecSpin(timeSec);
        }

        // SpingForASecond repeatedly calls DateTime.Now until for
        // 1 second.  It also does some work of its own in this
        // methods so we get some exclusive time to look at.  
        static int SpinForASecond()
        {
            DateTime start = DateTime.Now;
            for (int j = 0; ; j++)
            {
                if ((DateTime.Now - start).TotalSeconds > 1)
                    return j;

                // Do some work in this routine as well.   
                for (int i = 0; i < 10; i++)
                    aStatic += i;
            }
        }
    }

    /// <summary>
    /// ReportGenerator is a class that knows how to make the HTML reports associated with the 'Analyze' command.  
    /// </summary>
    class ReportGenerator
    {
        public static void Analyze(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";

            var htmlFile = Path.ChangeExtension(parsedArgs.DataFile, "analyze.html");
            var usersGuideFile = UsersGuide.WriteUsersGuide(htmlFile);

            using (TraceLog log = TraceLog.OpenOrConvert(parsedArgs.DataFile, GetConvertOptions(parsedArgs)))
                GetProcessFromCommandRun(parsedArgs, log);

            Console.WriteLine("Analyzing data in {0}", parsedArgs.DataFile);
            var noBrowser = parsedArgs.NoBrowser;
            parsedArgs.NoBrowser = true;
            var gcInfo = GCTime(parsedArgs);
            var jitInfo = JitTime(parsedArgs);
            // var dllInfo = DllLoads(parsedArgs);     
            var callTree = CpuTime(parsedArgs);

            using (TraceLog log = TraceLog.OpenOrConvert(parsedArgs.DataFile, GetConvertOptions(parsedArgs)))
            {
                using (TextWriter writer = System.IO.File.CreateText(htmlFile))
                {
                    writer.WriteLine("<html>");
                    writer.WriteLine("<body>");
                    writer.WriteLine("<H2><A Name=\"Top\">Perf Analysis for {0}</A></H2>", parsedArgs.Process != null ? "process " + parsedArgs.Process : "All Processes");

                    TraceProcess process = null;
                    if (!string.IsNullOrEmpty(parsedArgs.Process))
                    {
                        int pid;
                        if (int.TryParse(parsedArgs.Process, out pid))
                            process = log.Processes.LastProcessWithID(pid);
                        else
                            process = log.Processes.FirstProcessWithName(parsedArgs.Process, log.SessionStartTime100ns + 1);
                        if (process == null)
                            throw new ApplicationException("Could not find a process named " + parsedArgs.Process);
                        Console.WriteLine("Filtering to process {0} ({1}).  Started at {1:f3} msec.", process.Name, process.ProcessID,
                            process.StartTimeRelativeMsec);
                    }

                    double duration = log.SessionDuration.TotalMilliseconds;

                    if (process != null)
                    {
                        duration = process.EndTimeRelativeMsec - process.StartTimeRelativeMsec;

                        writer.WriteLine("<UL>");
                        writer.WriteLine("<LI> Process ID: {0}</LI>", process.ProcessID);
                        writer.WriteLine("<LI> Command Line: {0}</LI>", XmlUtilities.XmlEscape(process.CommandLine));
                        writer.WriteLine("<LI> Start Time: {0}</LI>", process.StartTime);
                        writer.WriteLine("<LI> Total Duration: {0:f0} msec</LI>", duration);
                        writer.WriteLine("<LI> <A HREF=\"{0}#UnderstandingTheAnalysisReport\">Perf Analysis Users Guide</A></LI>",
                            usersGuideFile);
                        writer.WriteLine("</UL>");
                        writer.WriteLine("<P>" +
                                        "This report contains useful general purpose, performance analysis of .NET programs.  \r\n" +
                                        "Currently there is an analysis of <A HREF=\"#CPUStats\">CPU Time</A>, " +
                                        "<A HREF=\"#GCStats\">.NET Garbage Collection</A>, and <A HREF=\"#JITStats\">Just In Time Compilation</A>.  \r\n" +
                                        "</P>");
                        writer.WriteLine("<P>" +
                                        "See the <A HREF=\"{0}#UnderstandingTheAnalysisReport\">Perf Analysis Users Guide</A> for more information on this report and what PerfMonitor can do in general.  \r\n" +
                                        "</P>", usersGuideFile);
                        double cpuRatio = callTree.Root.InclusiveMetric / duration;
                        string cpuReport = Path.ChangeExtension(parsedArgs.DataFile, "cpuTime.html");

                        writer.WriteLine("<HR/>");
                        writer.WriteLine("<H3><A Name=\"CPUStats\">CPU Statistics</A></H3>");
                        writer.WriteLine("<UL>");
                        writer.WriteLine("<LI> CPU Time Consumed: {0:f0} msec</LI>", callTree.Root.InclusiveMetric);
                        writer.WriteLine("<LI> Average CPU Utilization for the process: {0:f1}%</LI>", cpuRatio * 100);
                        writer.WriteLine("<LI> <A href=\"{0}\">Detailed CPU Analysis</A></LI>", cpuReport);
                        writer.WriteLine("</UL>");

                        writer.WriteLine("<P>" +
                        "This process spent {0:f1}% of its total time using the CPU.   The rest of the time was spent waiting\r\n" +
                        "on the disk, network, user input or other results.   At most {1:f1} msec can be saved by optimizing CPU.\r\n" +
                        "</P>", cpuRatio * 100, callTree.Root.InclusiveMetric);
                        if (cpuRatio > .7)
                        {
                            writer.WriteLine("<P><font color=\"red\">" +
                                "Optimizing CPU is likely to be an important part improving the speed of this application.  " +
                                "See <A href=\"{1}\">Detailed CPU Analysis</A> to determine exactly where the CPU time is spent.  " +
                                "</font></P>", cpuRatio * 10, cpuReport);
                        }

                        GCProcess gcForProcess;
                        if (gcInfo.TryGetByID(process.ProcessID, out gcForProcess))
                        {
                            writer.WriteLine("<HR/>");
                            writer.WriteLine("<H3><A Name=\"GCStats\">GC Statistics</A></H3>");
                            writer.WriteLine("<UL>");
                            var gcTime = gcForProcess.Total.TotalGCDurationMSec;
                            writer.WriteLine("<LI> Total GC time: {0:f0} msec</LI>", gcTime);
                            var gcTimeToCPUTime = gcTime / callTree.Root.InclusiveMetric;

                            writer.WriteLine("<LI> Ratio of GC to CPU time: {0:f1}%</LI>", gcTimeToCPUTime * 100);
                            writer.WriteLine("<LI> Max GC Heap Size: {0:f1} MB</LI>", gcForProcess.Total.MaxSizeBeforeMB);
                            writer.WriteLine("<LI> Max Working Set: {0:f1} MB</LI>", gcForProcess.PeakWorkingSetMB);
                            var gcToTotalWS = gcForProcess.Total.MaxSizeBeforeMB / gcForProcess.PeakWorkingSetMB;
                            writer.WriteLine("<LI> Ratio of GC to Total Working Set: {0:f1}%</LI>", gcToTotalWS * 100.0);

                            writer.WriteLine("<LI> Max GC Allcation Rate: {0:f1} MB/Sec</LI>", gcForProcess.Total.MaxAllocRateMBSec);
                            writer.WriteLine("<LI> Max GC Pause: {0:f1} Msec</LI>", gcForProcess.Total.MaxPauseDurationMSec);
                            writer.WriteLine("<LI> Average GC Pause: {0:f1} Msec</LI>", gcForProcess.Total.MeanPauseDurationMSec);
                            writer.WriteLine("<LI> <A href=\"{0}\">Detailed GC Time Analysis</A></LI>", Path.ChangeExtension(parsedArgs.DataFile, "gcTime.html"));
                            writer.WriteLine("</UL>");

                            writer.WriteLine("<P>" +
                                "The <A href=\"http://msdn.microsoft.com/en-us/library/0xy59wtx.aspx\">.NET Runtime garbage collected (GC) heap</A> at its largest was {0:f1}% of the total memory (working set) used by the process." +
                                "The rest is consumed by code loaded from your appliation (the EXE and DLLs), and memory allocated by the " +
                                "CLR, operating system, and other unmanaged code.  " +
                                "</P>", gcToTotalWS * 100);

                            if (gcToTotalWS > .30)
                            {
                                writer.WriteLine("<P>" +
                                    "The GC heap uses over 30% of the total memory used.  Thus it is likely to be useful to optimized GC Heap allocations" +
                                    "If you wish to optmized GC Heap allocations you should use a tool like " +
                                    "<A href=\"http://msdn.microsoft.com/en-us/library/ms979205.aspx\">ClrProfiler<A> " +
                                    "to do further investigation.  " +
                                    "<br/>See also <A href=\"http://msdn.microsoft.com/en-us/magazine/dd882521.aspx\">Memory Usage Auditing For .NET Applications<A>." +
                                    "</P>");
                            }
                            else
                            {
                                writer.WriteLine("<P>" +
                                    "The GC heap used only {0:f1}% of the total memory.  Thus most memory was consumed by things other than the GC heap." +
                                    "To get a breakdown of that memory, a tool like " +
                                    "<A href=\"http://technet.microsoft.com/en-us/sysinternals/dd535533.aspx\">VMMap<A> is most appropriate.  " +
                                    "See also <A href=\"http://msdn.microsoft.com/en-us/magazine/dd882521.aspx\">Memory Usage Auditing For .NET Applications<A>." +
                                    "</P>", gcToTotalWS * 100);
                            }

                            if (gcTimeToCPUTime > .10)
                            {
                                writer.WriteLine("<P>" +
                                    "Over 10% of the total CPU time was spent in the garbage collector.   Most well tuned applications are in the 0-10% range.  " +
                                    "This is typically caused by an allocation pattern that allows objects to live just long enough to require an expensive " +
                                    "Gen 2 collection.   Finalizable objects tend to be part of the problem.   " +
                                    "<br/>See also <A href=\"http://msdn.microsoft.com/en-us/library/0xy59wtx.aspx\">.NET  garbage collection</A>" +
                                    "<br/>See also <A href=\"http://blogs.msdn.com/b/ricom/archive/2003/12/04/41281.aspx\">GC Mid Life Crisis</A>" +
                                     "</P>");
                            }

                            if (gcForProcess.Total.MaxAllocRateMBSec > 10)
                            {
                                writer.WriteLine("<P>" +
                                     "<Font color=\"Red\">This program had a peak GC heap allocation rate of over 10 MB/sec.   This is quite high.  " +
                                     "It is not uncommon that this is simply a performance bug.</font>  " +
                                     "This will often show up in the <A href=\"{0}\">CPU Analysis</A> because allocations consume CPU so tune for CPU first.  If the app is already tuned for CPU, " +
                                     "an investigation using <A href=\"http://msdn.microsoft.com/en-us/library/ms979205.aspx\">ClrProfiler<A> would be useful.  " +
                                     "See also <A href=\"http://msdn.microsoft.com/en-us/magazine/dd882521.aspx\">Memory Usage Auditing For .NET Applications<A>." +
                                     "</P>", cpuReport);
                            }

                            if (gcForProcess.Total.MaxPauseDurationMSec > 200)
                            {
                                writer.WriteLine("<P>" +
                                    "The maximum GC pause time is greater than 200 msec.   During this time the program will appear to freeze.  " +
                                    "This is clearly undesirable, and is indicative of doing a Gen 2 collection on a large GC heap with many GC pointers.  " +
                                    "If this application is not running on V4.0 of the .NET runtime, it is likely to benefit from upgrading. " +
                                    "See <A href=\"{0}\">Detailed GC Statistics</A> for more information on how often and how long these pause times are.  " +
                                    "See also <A href=\"http://blogs.msdn.com/b/maoni/archive/2008/11/19/so-what-s-new-in-the-clr-4-0-gc.aspx\">Background GC</A>.  " +
                                     "</P>", Path.ChangeExtension(parsedArgs.DataFile, "gcTime.html"));
                            }
                        }

                        JitProcess jitForProcess;
                        if (jitInfo.TryGetByID(process.ProcessID, out jitForProcess))
                        {
                            writer.WriteLine("<HR/>");
                            writer.WriteLine("<H3><A Name=\"JITStats\">JIT Compilation Statistics</A></H3>");
                            writer.WriteLine("<UL>");
                            writer.WriteLine("<LI> Number of methods JIT compiled: {0} </LI>", jitForProcess.Total.Count);
                            writer.WriteLine("<LI> Total Size of Native code created: {0} bytes</LI>", jitForProcess.Total.NativeSize);
                            writer.WriteLine("<LI> Average Native method size compiled: {0:f1} bytes</LI>",
                                (double)jitForProcess.Total.NativeSize / jitForProcess.Total.Count);
                            if (jitForProcess.isClr4)
                            {
                                var jitTime = jitForProcess.Total.JitTimeMSec;
                                writer.WriteLine("<LI> Total JIT compilation time: {0:f0} msec</LI>", jitTime);
                                writer.WriteLine("<LI> Ratio of JIT compilation to CPU time: {0:f1}%</LI>", jitTime / callTree.Root.InclusiveMetric * 100);
                            }
                            writer.WriteLine("<LI> <A href=\"{0}\">Detailed JIT Time Analysis</A></LI>", Path.ChangeExtension(parsedArgs.DataFile, "jitTime.html"));
                            writer.WriteLine("</UL>");

                            if (jitForProcess.isClr4)
                            {
                                writer.WriteLine("<P>" +
                                    "The <A href=\"http://msdn.microsoft.com/en-us/library/ht8ecch6(VS.71).aspx\">.NET Just in time (JIT) compiler</A> compiled {0} methods taking {0:f1} msec during the execution of the program." +
                                    "Thus at most {1:f1} msec might be saved at startup by precompiling your application with the " +
                                    "<A href=\"http://msdn.microsoft.com/en-us/magazine/cc163808.aspx\">NGen Tool</A>. " +
                                    "</P>", jitForProcess.Total.Count, jitForProcess.Total.JitTimeMSec);
                            }
                            else
                            {
                                writer.WriteLine("<P>" +
                                    "The <A href=\"http://msdn.microsoft.com/en-us/library/ht8ecch6(VS.71).aspx\">.NET Just in time (JIT) compiler</A> compiled {0} methods with a total native size of {1} bytes during the execution of the program." +
                                    "</P>", jitForProcess.Total.Count, jitForProcess.Total.NativeSize);

                                if (jitForProcess.Total.NativeSize > 100000)
                                {
                                    writer.WriteLine("<P>" +
                                        "The application compiles more than 100K of Native code.  The JIT can compile about This is enough that using the " +
                                        "<A href=\"http://msdn.microsoft.com/en-us/magazine/cc163808.aspx\">NGen Tool</A> is likely to be helpful to improve startup time.  " +
                                        "</P>");
                                }
                            }
                        }
                    }
                    else
                    {
                        writer.WriteLine("<UL>");
                        writer.WriteLine("<LI> Total Duration: {0:f0} msec</LI>", duration);
                        writer.WriteLine("<LI> <A href=\"{0}\">Detailed CPU Analysis</A></LI>", Path.ChangeExtension(parsedArgs.DataFile, "cpuTime.html"));
                        writer.WriteLine("<LI> <A href=\"{0}\">GC Time Analysis</A></LI>", Path.ChangeExtension(parsedArgs.DataFile, "gcTime.html"));
                        writer.WriteLine("<LI> <A href=\"{0}\">JIT Time Analysis</A></LI>", Path.ChangeExtension(parsedArgs.DataFile, "jitTime.html"));
                        writer.WriteLine("</UL>");
                    }
                    writer.WriteLine("<BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/>");
                    writer.WriteLine("</body>");
                    writer.WriteLine("</html>");
                }
            }
            Console.WriteLine("Perf Analysis HTML report in {0}", htmlFile);
            if (!noBrowser)
                Command.Run("\"" + htmlFile + "\"", new CommandOptions().AddStart());
        }
        public static ProcessLookup<GCProcess> GCTime(CommandLineArgs parsedArgs)
        {
            ProcessLookup<GCProcess> gcProcess;

            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";
            var xmlFile = Path.ChangeExtension(parsedArgs.DataFile, "GCTime.xml");

            using (TraceEventDispatcher dispatcher = GetSource(ref parsedArgs.DataFile))
                gcProcess = GCProcess.Collect(dispatcher);

            if (parsedArgs.WriteXml)
            {
                using (TextWriter writer = System.IO.File.CreateText(xmlFile))
                {
                    writer.WriteLine("<Data>");
                    gcProcess.ToXml(writer, "GCProcesses");
                    writer.WriteLine("</Data>");
                }
                Console.WriteLine("GC Time Xml data in " + xmlFile);
            }

            Predicate<GCProcess> filter = GetFilter<GCProcess>(parsedArgs.Process);
            if (filter == null)
                filter = delegate(GCProcess data) { return data.Total.GCCount > 0; };

            var htmlFile = Path.ChangeExtension(xmlFile, ".html");
            using (TextWriter writer = System.IO.File.CreateText(htmlFile))
            {
                writer.WriteLine("<html><body>");
                gcProcess.ToHtml(writer, htmlFile, "Processes with CLR Garbage Collection time", filter);
                writer.WriteLine("</body></html>");
            }

            Console.WriteLine("GC Time HTML Report in " + htmlFile);
            // Launching iexplore directly makes it come up in a new window.  
            if (!parsedArgs.NoBrowser)
                Command.Run("iexplore \"" + Path.GetFullPath(htmlFile) + "\"", new CommandOptions().AddStart());

            return gcProcess;
        }
        public static ProcessLookup<JitProcess> JitTime(CommandLineArgs parsedArgs)
        {
            ProcessLookup<JitProcess> jitProcess;

            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";
            var xmlFile = Path.ChangeExtension(parsedArgs.DataFile, "jitTime.xml");

            using (TraceEventDispatcher dispatcher = GetSource(ref parsedArgs.DataFile))
                jitProcess = JitProcess.Collect(dispatcher);

            if (parsedArgs.WriteXml)
            {
                using (TextWriter writer = System.IO.File.CreateText(xmlFile))
                {
                    writer.WriteLine("<Data>");
                    jitProcess.ToXml(writer, "JitProcesses");
                    writer.WriteLine("</Data>");
                }
                Console.WriteLine("JIT Time Xml data in " + xmlFile);
            }

            Predicate<JitProcess> filter = GetFilter<JitProcess>(parsedArgs.Process);
            if (filter == null)
                filter = delegate(JitProcess data) { return data.Total.Count > 0; };

            var htmlFile = Path.ChangeExtension(xmlFile, ".html");
            using (TextWriter writer = System.IO.File.CreateText(htmlFile))
            {
                writer.WriteLine("<html><body>");
                jitProcess.ToHtml(writer, htmlFile, "Processes with JIT compilation time", filter);
                writer.WriteLine("</body></html>");
            }
            Console.WriteLine("JIT Time HTML Report in " + htmlFile);
            // Launching iexplore directly makes it come up in a new window.  
            if (!parsedArgs.NoBrowser)
                Command.Run("iexplore \"" + Path.GetFullPath(htmlFile) + "\"", new CommandOptions().AddStart());
            return jitProcess;
        }
        public static ProcessLookup<DllProcess> DllLoads(CommandLineArgs parsedArgs)
        {
            ProcessLookup<DllProcess> dllProcess;

            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";
            var xmlFile = Path.ChangeExtension(parsedArgs.DataFile, "DllLoad.xml");

            using (TraceLog log = TraceLog.OpenOrConvert(parsedArgs.DataFile, GetConvertOptions(parsedArgs)))
                dllProcess = DllProcess.Collect(log.Events.GetSource());

            using (TextWriter writer = System.IO.File.CreateText(xmlFile))
            {
                writer.WriteLine("<Data>");
                dllProcess.ToXml(writer, "DllProcess");
                writer.WriteLine("</Data>");
            }
            Console.WriteLine("DLL Xml data in " + xmlFile);
            return dllProcess;
        }
        public static CallTree CpuTime(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
                parsedArgs.DataFile = "PerfMonitorOutput.etl";
            var xmlFile = Path.ChangeExtension(parsedArgs.DataFile, "cpuTime.xml");

            using (TraceLog log = TraceLog.OpenOrConvert(parsedArgs.DataFile, GetConvertOptions(parsedArgs)))
            {
                GetProcessFromCommandRun(parsedArgs, log);
                double rundownStart = double.PositiveInfinity;
                TraceEvents events = log.Events;
                if (!string.IsNullOrEmpty(parsedArgs.Process))
                {
                    int pid;
                    TraceProcess process;
                    if (int.TryParse(parsedArgs.Process, out pid))
                        process = log.Processes.LastProcessWithID(pid);
                    else
                        process = log.Processes.FirstProcessWithName(parsedArgs.Process, log.SessionStartTime100ns + 1);
                    if (process == null)
                        throw new ApplicationException("Could not find a process named " + parsedArgs.Process);
                    Console.WriteLine("Filtering to process {0} ({1}).  Started at {1:f3} msec.", process.Name, process.ProcessID,
                        process.StartTimeRelativeMsec);
                    events = process.EventsInProcess;

                    if (parsedArgs.EndTimeRelMsec == double.PositiveInfinity)
                    {
                        rundownStart = GetRundownStart(events);
                        parsedArgs.EndTimeRelMsec = rundownStart;
                    }
                }

                CallTree tree = new CallTree(ScalingPolicyKind.TimeMetric);
                double startTimeMsec = 0;
                double endTimeMSec = log.RelativeTimeMSec(log.SessionEndTime100ns);
                if (parsedArgs.StartTimeRelMsec != 0 || parsedArgs.EndTimeRelMsec != double.PositiveInfinity)
                {
                    Console.WriteLine("Filtering to Time region [{0:f3}, {1:f3}] msec",
                        parsedArgs.StartTimeRelMsec, parsedArgs.EndTimeRelMsec);
                    events = events.FilterByTime(
                        log.SessionStartTime100ns + (long)parsedArgs.StartTimeRelMsec * 10000,
                        log.SessionStartTime100ns + (long)parsedArgs.EndTimeRelMsec * 10000);

                    startTimeMsec = parsedArgs.StartTimeRelMsec;
                    if (parsedArgs.EndTimeRelMsec < endTimeMSec)
                        endTimeMSec = parsedArgs.EndTimeRelMsec;
                }
                TraceEvents cpuEvents = events.Filter(delegate(TraceEvent x) { return x is SampledProfileTraceData && x.ProcessID > 0; });

                // Round to the nearest half second
                endTimeMSec = Math.Ceiling((endTimeMSec - startTimeMsec) / 500) * 500 + startTimeMsec;
                tree.TimeHistogramController = new TimeHistogramController(tree, startTimeMsec, endTimeMSec);

                tree.StackSource = new OSGroupingStackSource(new TraceEventStackSource(cpuEvents));

                var foldThresholdRatio = (parsedArgs.ThresholdPercent / 100.0F) * .10F;        // we fold nodes 1/10 of the tree fold threshold
                tree.FoldNodesUnder(foldThresholdRatio * tree.Root.InclusiveMetric, false);

                if (parsedArgs.WriteXml)
                {
                    using (TextWriter writer = System.IO.File.CreateText(xmlFile))
                        WriteAsXml(tree, writer, parsedArgs.ThresholdPercent, parsedArgs);
                    Console.WriteLine("CPU Time Xml data in " + xmlFile);
                }

                var htmlFile = Path.ChangeExtension(xmlFile, ".html");
                using (TextWriter writer = System.IO.File.CreateText(htmlFile))
                    WriteAsHtml(tree, writer, htmlFile, parsedArgs.ThresholdPercent, rundownStart, parsedArgs);
                Console.WriteLine("CPU Time HTML report in " + htmlFile);
                // Launching iexplore directly makes it come up in a new window.  
                if (!parsedArgs.NoBrowser)
                    Command.Run("iexplore \"" + Path.GetFullPath(htmlFile) + "\"", new CommandOptions().AddStart());
                return tree;
            }
        }
        /// <summary>
        /// Given a input file name (which can be an ETL or ETLX file, return the TraceEventDispatcher
        /// needed for it.  'filePath' can be null, in which case defaults are used.  
        /// </summary>
        public static TraceEventDispatcher GetSource(ref string filePath)
        {
            if (filePath == null)
            {
                filePath = "PerfMonitorOutput.etlx";
                if (!File.Exists(filePath))
                    filePath = "PerfMonitorOutput.etl";
            }

            bool isETLXFile = string.Compare(Path.GetExtension(filePath), ".etlx", StringComparison.OrdinalIgnoreCase) == 0;
            if (!File.Exists(filePath) && isETLXFile)
            {
                string etlFile = Path.ChangeExtension(filePath, ".etl");
                if (File.Exists(etlFile))
                {
                    filePath = etlFile;
                    isETLXFile = false;
                }
            }

            TraceEventDispatcher ret;
            if (isETLXFile)
                ret = new TraceLog(filePath).Events.GetSource();
            else
                ret = new ETWTraceEventSource(filePath);

            if (ret.EventsLost != 0)
                Console.WriteLine("WARNING: events were lost during data collection! Any anaysis is suspect!");
            return ret;
        }

        #region private
        private static double GetRundownStart(TraceEvents events)
        {
            double start = double.PositiveInfinity;
            var source = events.GetSource();
            source.Clr.MethodUnloadVerbose += delegate(MethodLoadUnloadVerboseTraceData data)
            {
                if ((data.MethodFlags & MethodFlags.Jitted) == 0)
                {
                    start = data.TimeStampRelativeMSec;
                    source.StopProcessing();
                }
            };
            source.Process();
            return start;
        }

        /// <summary>
        /// If the parsedArgs.command is present, deduce the parsedArgs.processName argument from it 
        /// </summary>
        /// <param name="parsedArgs"></param>
        private static void GetProcessFromCommandRun(CommandLineArgs parsedArgs, TraceLog log)
        {
            // This is part of a 'run' command and the user did not specify a process of interest, then deduce one. 
            if (parsedArgs.CommandLine != null && parsedArgs.Process == null)
            {
                TraceProcess bestProcessMatch = null;
                foreach (TraceProcess process in log.Processes)
                {
                    if (ProcessNameMatchCommandLine(process.Name, parsedArgs.CommandLine))
                    {
                        if (bestProcessMatch == null || bestProcessMatch.StartTime100ns < process.StartTime100ns)
                            bestProcessMatch = process;
                    }
                }
                if (bestProcessMatch != null)
                    parsedArgs.Process = bestProcessMatch.Name;
            }
        }
        private static bool ProcessNameMatchCommandLine(string processName, string commandLine)
        {
            if (commandLine.StartsWith(processName, StringComparison.OrdinalIgnoreCase))
            {
                if (commandLine.Length == processName.Length)
                    return true;

                char nextChar = commandLine[processName.Length];
                if (nextChar == ' ' || nextChar == '.')
                    return true;
            }
            return false;
        }
        private static Predicate<T> GetFilter<T>(string processNameSpec) where T : ProcessLookupContract
        {
            if (!string.IsNullOrEmpty(processNameSpec))
            {
                return delegate(T stats)
                {
                    return stats.ProcessID.ToString() == processNameSpec || string.Compare(stats.ProcessName, processNameSpec, StringComparison.OrdinalIgnoreCase) == 0;
                };
            }
            return null;
        }
        private static TraceLogOptions GetConvertOptions(CommandLineArgs parsedArgs)
        {
            TraceLogOptions options = new TraceLogOptions();
            options.SourceLineNumbers = parsedArgs.LineNumbers;
            options.SymbolDebug = parsedArgs.SymDebug;
            Dictionary<string, string> dllSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (parsedArgs.SymbolsForDlls != null)
            {
                options.AlwaysResolveSymbols = true;
                foreach (string dll in parsedArgs.SymbolsForDlls)
                    dllSet[dll] = null;
            }
            options.ShouldResolveSymbols = delegate(string moduleFilePath)
            {
                string moduleName = Path.GetFileNameWithoutExtension(moduleFilePath);
                return dllSet.ContainsKey(moduleName);
            };

            string convertLog = Path.ChangeExtension(parsedArgs.DataFile, ".convertlog.txt");
            options.ConversionLogName = convertLog;
            return options;
        }

        #region HtmlFile Creation
        private static void WriteAsXml(CallTree tree, TextWriter writer, float thresholdPercent, CommandLineArgs parsedArgs)
        {
            writer.Write("<Data thresholdPercent=\"{0:f1}\"", thresholdPercent);
            if (parsedArgs.Process != null)
                writer.WriteLine(" processFilter=\"{0}\"", parsedArgs.Process);
            writer.WriteLine(">");
            tree.ToXml(writer);

            // Dump the byname (exclusive) statistics
            writer.WriteLine(" <ByName>");
            List<CallTreeNodeBase> allMethodStats = tree.ByIDSortedExclusiveMetric();
            foreach (CallTreeNodeBase methodStats in allMethodStats)
            {
                // We fix the exclusive threashold to be 20% of the inclusive threashold 
                if (methodStats.ExclusiveMetricPercent < thresholdPercent * .20)
                    break;
                writer.Write("  <method");
                methodStats.ToXmlAttribs(writer);
                writer.WriteLine("/>");
            }
            writer.WriteLine(" </ByName>");
            writer.WriteLine("</Data>");
        }
        private static void WriteAsHtml(CallTree tree, TextWriter writer, string htmlFile, float thresholdPercent, double rundownStart, CommandLineArgs parsedArgs)
        {
            // We fix the exclusive threshold to be 20% of the inclusive threshold 
            var excThresholdPercent = thresholdPercent * .20F;
            var callerCalleeNodes = new Dictionary<string, int>();

            writer.WriteLine("<html>");
            writer.WriteLine("<body>");
            writer.WriteLine("<H2><A Name=\"Top\">CPU Report for process {0}</A></H2>", parsedArgs.Process != null ? parsedArgs.Process : "All Processes");
            writer.WriteLine("<UL>");
            double duration = tree.Root.LastTimeRelMSec - tree.Root.FirstTimeRelMSec;
            if (parsedArgs.StartTimeRelMsec != 0 || parsedArgs.EndTimeRelMsec != double.PositiveInfinity)
            {
                if (parsedArgs.StartTimeRelMsec == 0 && parsedArgs.EndTimeRelMsec == rundownStart)
                {
                    writer.WriteLine("<LI>CPU Samples after {0:f3} msec are excluded to avoid capturing ETW symbolic information dumping overhead.</LI>", rundownStart);
                }
                else
                {
                    writer.WriteLine("<LI><font color=\"red\"> Filter to time region : [{0:f3}, {1:f3}] msec</font></LI>",
                        parsedArgs.StartTimeRelMsec, parsedArgs.EndTimeRelMsec);
                }
            }
            var percentBroken = tree.Root.GetBrokenStackCount() * 100.0 / tree.Root.InclusiveCount;

            writer.WriteLine("<LI> Total Duration: {0:f0} msec</LI>", duration);
            writer.WriteLine("<LI> Total CPU: {0:f0} msec</LI>", tree.Root.InclusiveMetric);
            writer.WriteLine("<LI> Average CPU Utilization (1 CPU): {0:f1}%</LI>", tree.Root.InclusiveMetric * 100.0 / duration);
            writer.WriteLine("<LI> Interval for each Time Bucket in CPU utilization: {0:f1} msec</LI>", tree.TimeHistogramController.BucketDuration);
            writer.WriteLine("<LI> Percent of samples with broken stacks: {0:f1}%</LI>", percentBroken);
            writer.WriteLine("<LI> <A href=\"#TopDownAnalysis\">Top Down Analysis</A>  (Breaking CPU down by call tree)</LI>");
            writer.WriteLine("<LI> <A href=\"#BottomUpAnalysis\">Bottom up Analysis</A> (Looking at individual methods that use alot of CPU).</LI>");
            var usersGuideFile = UsersGuide.WriteUsersGuide(htmlFile);
            writer.WriteLine("<LI> <A HREF=\"{0}#UnderstandingCPUPerf\">CPU Perf Users Guide</A></LI>",
                usersGuideFile);
            writer.WriteLine("</UL>");

            if (duration < 2000 && parsedArgs.StartTimeRelMsec == 0)
            {
                writer.WriteLine("<P>" +
                    "<font color=\"red\">WARNING:The total duration process is less than 2 seconds.   This is really too small to do accurate CPU analysis.</font>" +
                    "The recommended duration is 3-5 seconds. \r\n" +
                    "Ideally the program or inputs shoudl be modified until the duration is in this range." +
                    "</P>");

            }

            if (percentBroken > 20)
            {
                writer.Write("<P>" +
                "<font color=\"red\">WARNING:In {0:f1}% of the samples, fetching a stack trace failed.\r\n</font>" +
                "This means that the 'top-down' and caller-callee views are likely to be misleading since many samples cannot be attributed to the correct place in the call tree.\r\n" +
                "However the bottom up (method view), can still be useful.\r\n", percentBroken);

                if (percentBroken > 80)
                {
                    writer.Write("The most likely cause of high broken stack percentages is running JIT compiled code on a 64 bit machine.  \r\n" +
                                 "Forcing the application to run as a 32 bit app or NGENing the application can fix this issue.   \r\n");
                }
                writer.WriteLine("(See users guide for details).  </P>");
            }

            if (tree.Root.InclusiveMetric / duration < .5)
            {
                writer.WriteLine("<P>" +
                "<font color=\"red\">WARNING: only {0:f1}% of the total time was spent using the CPU.\r\n</font>" +
                "Optimizing CPU is not likely to be have a large effect.\r\n" +
                "If this is a startup scenario, it is likely that the time is dominated by Disk I/O.\r\n" +
                "If it is a client-server app, network latencies could be consuming the time.\r\n" +
                "</P>", tree.Root.InclusiveMetric * 100.0 / duration);

                writer.WriteLine("<P>" +
                "It may still be useful to look at CPU if there are pockets of high CPU use and that response time is important, " +
                "but keep in mind it will not have a strong effect on the end-to-end time." +
                "</P>");
            }
            writer.WriteLine("<P>" +
                "To start an analysis you will want to look at both the <A href=\"#BottomUpAnalysis\">Bottom up</A> and <A href=\"#TopDownAnalysis\">Top Down</A> for your program.\r\n" +
                "Typically looking at the bottom-up analysis is the fastest way to find individual functions which if improved will have impact.\r\n" +
                "The top-down analysis is useful in understanding the performance of your program as a whole and is useful for determining if larger structural changes (e.g. new representation or data structures) are needed to get good performance.\r\n" +
                "</P>");

            writer.WriteLine("<P>" +
                "To avoid information overload only those methods that use a significant amount of CPU time ({0:f1}% for bottom-up and {1:f1}% for top-down) are displayed.\r\n" +
                "You can use the /threshold:N qualifier to adust this if it is inappropriate.\r\n" +
                "You can filter to a particular region of time by using the /startTime:<bold>MsecFromStart</bold> and /endTime:<bold>MsecFromStart</bold> qualifiers.\r\n" +
                "</P>", excThresholdPercent, thresholdPercent);


            // START BOTTOM UP
            writer.WriteLine("<H3><A name=\"BottomUpAnalysis\">Bottom Up Analysis (CPU time broken down method)</A></H3>");
            writer.WriteLine("<UL>");
            writer.WriteLine("<LI> Relevance Threshold {0:f1}%</LI>", excThresholdPercent);
            writer.WriteLine("<LI> <A href=\"#Top\">Goto Top</A></LI>");
            writer.WriteLine("</UL>");

            writer.WriteLine("<P>" +
                "This is a listing of methods where CPU time was spent in that method alone but not its callers.\r\n" +
                "Routines that show up here either have loops in them which execute alot or are called alot.\r\n" +
                "Hover over the column headings for a description of the column.\r\n" +
                "Clicking on a name will go to a caller-callee view of the that method.\r\n" +
                "</P>");

            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");

            WriteColumnHeaders(writer, null, true);
            List<CallTreeNodeBase> allMethodStats = tree.ByIDSortedExclusiveMetric();
            foreach (CallTreeNodeBase methodStats in allMethodStats)
            {
                if (methodStats.ExclusiveMetricPercent < excThresholdPercent)
                    break;
                writer.WriteLine("<TR> <TD>{0}</TD> <TD align=\"center\">{1:f1}</TD> <TD align=\"center\">{2:f0}</TD> <TD align=\"center\">{3:f1}</TD> <TD align=\"center\">{4:f0}</TD> <TD><font face=\"Courier New\" size=\"-2\">{5}</font></TD> <TD align=\"center\">{6:f3}</TD> <TD align=\"center\">{7:f3}</TD></TR>",
                    AnchorForName(methodStats.Name, callerCalleeNodes), methodStats.ExclusiveMetricPercent, methodStats.ExclusiveMetric,
                    methodStats.InclusiveMetricPercent, methodStats.InclusiveMetric, methodStats.InclusiveMetricByTimeString,
                    methodStats.FirstTimeRelMSec, methodStats.LastTimeRelMSec);
            }
            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");
            // END BOTTOM UP 

            // START TOP DOWN
            writer.WriteLine("<H3><A name=\"TopDownAnalysis\">Top Down Analysis (CPU time broken down by Call Tree)</A></H3>");
            writer.WriteLine("<UL>");
            writer.WriteLine("<LI> Relevance Threshold: {0:f1}% (Methods with less than this % will not be displayed)</LI>", thresholdPercent);
            writer.WriteLine("<LI> <A href=\"#Top\">Goto Top</A></LI>");
            writer.WriteLine("</UL>");

            writer.WriteLine("<P>" +
                "This is a breakdown where CPU time for the process was spent broken down by call tree.\r\n" +
                "Hover over the column headings for a description of the column.\r\n" +
                "Clicking on a name will go to a caller-callee view of the that method.\r\n" +
                "</P>");

            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"-2\">");
            WriteColumnHeaders(writer);
            WriteHtmlRowsForTree(writer, tree.Root, thresholdPercent, callerCalleeNodes);
            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");
            // END  TOP DOWN

            // CALLER-CALLEE Views
            writer.WriteLine("<HR/><HR/>Caller-Callee views<BR/><BR/><BR/><BR/><BR/><BR/><BR/>");
            WriteCallerCalleeNodes(writer, tree.Root, callerCalleeNodes);

            writer.WriteLine("</body>");
            writer.WriteLine("</html>");
        }
        private static void WriteHtmlRowsForTree(TextWriter writer, CallTreeNode callTreeNode, float thresholdPercent, Dictionary<string, int> callerCalleeNodes)
        {
            if (callTreeNode.InclusiveMetricPercent < thresholdPercent)
                return;

            string indent = "<font face=\"Courier New\" size=\"-1\">" + callTreeNode.IndentString(true).Replace(" ", "&nbsp;") + "</font>";
            string nameValue = "<font size=\"-1\">" + indent + AnchorForName(callTreeNode.Name, callerCalleeNodes) + "</font>";

            writer.WriteLine("<TR> <TD>{0}</TD> <TD align=\"center\">{1:f1}</TD> <TD align=\"center\">{2:f0}</TD> <TD align=\"center\">{3:f1}</TD> <TD align=\"center\">{4:f0}</TD> <TD><font face=\"Courier New\" size=\"-2\">{5}</font></TD> <TD align=\"center\">{6:f3}</TD> <TD align=\"center\">{7:f3}</TD></TR>",
                nameValue, callTreeNode.InclusiveMetricPercent, callTreeNode.InclusiveMetric,
                callTreeNode.ExclusiveMetricPercent, callTreeNode.ExclusiveMetric, callTreeNode.InclusiveMetricByTimeString,
                callTreeNode.FirstTimeRelMSec, callTreeNode.LastTimeRelMSec);

            if (callTreeNode.Callees != null)
            {
                foreach (var callee in callTreeNode.Callees)
                    WriteHtmlRowsForTree(writer, callee, thresholdPercent, callerCalleeNodes);
            }
        }
        private static void WriteCallerCalleeNodes(TextWriter writer, CallTreeNode callTreeNode, Dictionary<string, int> callerCalleeNodes)
        {
            // Have we already done this node?
            int index;
            bool found = false;
            if (!callerCalleeNodes.TryGetValue(callTreeNode.Name, out index))
                index = callerCalleeNodes.Count + 1;
            else if (index < 0)
                index = -index;
            else
                found = true;

            if (!found)
            {
                callerCalleeNodes[callTreeNode.Name] = index;
                var callerCallee = new CallerCalleeNode(callTreeNode.Name, callTreeNode.CallTree);

                writer.WriteLine("<Center>");
                writer.WriteLine("<A name=\"callerCallee{0}\">", index);
                writer.WriteLine("<Table Border=\"1\">");
                writer.WriteLine("<TR bgcolor=\"lightBlue\"><TH colspan=\"10\">Callers of {0}</TH></TR>", XmlName(callTreeNode.Name));
                if (callerCallee.Callers.Count > 0)
                {
                    WriteColumnHeaders(writer, "lightBlue");
                    foreach (CallTreeNodeBase methodStats in callerCallee.Callers)
                    {
                        writer.WriteLine("<TR bgcolor=\"lightBlue\"> <TD>{0}</TD> <TD align=\"center\">{1:f1}</TD> <TD align=\"center\">{2:f0}</TD> <TD align=\"center\">{3:f1}</TD> <TD align=\"center\">{4:f0}</TD> <TD><font face=\"Courier New\" size=\"-2\">{5}</font></TD> <TD align=\"center\">{6:f3}</TD> <TD align=\"center\">{7:f3}</TD></TR>",
                                    AnchorForName(methodStats.Name, callerCalleeNodes), methodStats.InclusiveMetricPercent, methodStats.InclusiveMetric,
                            methodStats.ExclusiveMetricPercent, methodStats.ExclusiveMetric, methodStats.InclusiveMetricByTimeString,
                            methodStats.FirstTimeRelMSec, methodStats.LastTimeRelMSec);
                    }
                }
                writer.WriteLine("<TR><TH colspan=\"10\">&nbsp;</TH></TR>", callTreeNode.Name);
                writer.WriteLine("<TR bgcolor=\"lightPink\"><TH colspan=\"10\">Method {0}</TH></TR>", XmlName(callTreeNode.Name));
                WriteColumnHeaders(writer, "lightPink");

                writer.WriteLine("<TR bgcolor=\"lightPink\"> <TD>{0}</TD> <TD align=\"center\">{1:f1}</TD> <TD align=\"center\">{2:f0}</TD> <TD align=\"center\">{3:f1}</TD> <TD align=\"center\">{4:f0}</TD> <TD><font face=\"Courier New\" size=\"-2\">{5}</font></TD> <TD align=\"center\">{6:f3}</TD> <TD align=\"center\">{7:f3}</TD></TR>",
                    AnchorForName(callerCallee.Name, callerCalleeNodes), callerCallee.InclusiveMetricPercent, callerCallee.InclusiveMetric,
                    callerCallee.ExclusiveMetricPercent, callerCallee.ExclusiveMetric, callerCallee.InclusiveMetricByTimeString,
                    callerCallee.FirstTimeRelMSec, callerCallee.LastTimeRelMSec);

                writer.WriteLine("<TR><TH colspan=\"10\">&nbsp;</TH></TR>", callTreeNode.Name);
                writer.WriteLine("<TR bgcolor=\"lightGreen\"><TH colspan=\"10\">Callees of {0}</TH></TR>", XmlName(callTreeNode.Name));
                if (callerCallee.Callees.Count > 0)
                {
                    WriteColumnHeaders(writer, "lightGreen");
                    foreach (CallTreeNodeBase methodStats in callerCallee.Callees)
                    {
                        writer.WriteLine("<TR bgcolor=\"lightGreen\"> <TD>{0}</TD> <TD align=\"center\">{1:f1}</TD> <TD align=\"center\">{2:f0}</TD> <TD align=\"center\">{3:f1}</TD> <TD align=\"center\">{4:f0}</TD> <TD><font face=\"Courier New\" size=\"-2\">{5}</font></TD> <TD align=\"center\">{6:f3}</TD> <TD align=\"center\">{7:f3}</TD></TR>",
                                  AnchorForName(methodStats.Name, callerCalleeNodes), methodStats.InclusiveMetricPercent, methodStats.InclusiveMetric,
                            methodStats.ExclusiveMetricPercent, methodStats.ExclusiveMetric, methodStats.InclusiveMetricByTimeString,
                            methodStats.FirstTimeRelMSec, methodStats.LastTimeRelMSec);
                    }
                }
                writer.WriteLine("</Table>");
                writer.WriteLine("</A>");
                writer.WriteLine("</Center>");
                writer.WriteLine("<HR/><HR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/>");
            }
            if (callTreeNode.Callees != null)
            {
                foreach (var callee in callTreeNode.Callees)
                    WriteCallerCalleeNodes(writer, callee, callerCalleeNodes);
            }
        }
        private static void WriteColumnHeaders(TextWriter writer, string backGroundColor = null, bool excFirst = false)
        {
            // Write out headers 
            if (backGroundColor != null)
                writer.Write("<TR bgcolor=\"{0}\">", backGroundColor);
            else
                writer.Write("<TR>");
            writer.Write("<TH Title=\"Name of method\">Name</TH>");
            if (excFirst)
            {
                writer.Write("<TH Title=\"CPU useage in this method but NOT callees.\r\n(% of total samples).\">Exc %</TH>");
                writer.Write("<TH Title=\"CPU useage in this method but NOT callees.\r\n(MSec used).\">Exc MSec</TH>");
            }
            writer.Write("<TH Title=\"CPU useage in this method and any callees.\r\n(% of total samples).\">Inc %</TH>");
            writer.Write("<TH Title=\"CPU useage in this method and any callees.\r\n(MSec used).\">Inc MSec</TH>");
            if (!excFirst)
            {
                writer.Write("<TH Title=\"CPU useage in this method but NOT callees.\r\n(% of total samples).\">Exc %</TH>");
                writer.Write("<TH Title=\"CPU useage in this method but NOT callees.\r\n(MSec used).\">Exc MSec</TH>");
            }

            writer.Write("<TH Title=\"CPU Utilization over time.\r\n" +
                   "Total time is broken into 32 buckets and each digit represents its CPU\r\n" +
                   "    _ = no CPU used  \r\n" +
                   "    0 =  0-10% CPU use\r\n" +
                   "    1 = 10-20% use\r\n" +
                   "    ...\r\n" +
                   "    9 = 90-100% use\r\n" +
                   "    A = 100-110% use.\r\n" +
                   "    ...\r\n" +
                   "    Z = 340-350% use.\r\n" +
                   "    * = greater than 350% use.\r\n" +
                   "\">CPU Utilization</TH>");
            writer.Write("<TH Title=\"The first time CPU was used (MSec from start of trace).\">First</TH>");
            writer.Write("<TH Title=\"The last time CPU was used (MSec from start of trace).\">Last</TH>");
            writer.WriteLine("</TR>");
        }


        private static int GetIndex(string name, Dictionary<string, int> callerCalleeNodes)
        {
            int index;
            if (!callerCalleeNodes.TryGetValue(name, out index))
            {
                index = (callerCalleeNodes.Count + 1);
                callerCalleeNodes[name] = -index;       // Negative means it has not be emitted yet.  
            }
            else if (index < 0)
                index = -index;
            return index;
        }
        private static string AnchorForName(string name, Dictionary<string, int> callerCalleeNodes)
        {
            return "<A href=\"#callerCallee" + GetIndex(name, callerCalleeNodes) + "\">" + XmlName(name) + "</A>";
        }
        private static string XmlName(string name)
        {
            // Shorten types in the signature.  
            Match m = Regex.Match(name, @"^(.*?)\((.+)\)$");
            if (m.Success)
            {
                var method = m.Groups[1].Value;
                var signature = m.Groups[2].Value;
                signature = Regex.Replace(signature, @"\w+\.", "");        // Remove namespaces
                signature = signature.Replace("value class ", "");
                signature = signature.Replace("class ", "");
                name = method + "(" + signature + ")";
            }
            return XmlUtilities.XmlEscape(name);
        }
        #endregion
        #endregion
    }
}
