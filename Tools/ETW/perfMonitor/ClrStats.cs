// Copyright (c) Microsoft Corporation.  All rights reserved
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using Diagnostics.Eventing;

namespace PerfMonitor
{
    /// <summary>
    /// GCProcess holds information about GCs in a particular process. 
    /// </summary>
    class GCProcess : ProcessLookupContract
    {
        public static ProcessLookup<GCProcess> Collect(TraceEventDispatcher source)
        {
            ProcessLookup<GCProcess> perProc = new ProcessLookup<GCProcess>();

            source.Kernel.AddToAllMatching<ProcessCtrTraceData>(delegate(ProcessCtrTraceData data)
            {
                var stats = perProc[data];
                stats.PeakVirtualMB = ((double)data.PeakVirtualSize) / 1000000.0;
                stats.PeakWorkingSetMB = ((double)data.PeakWorkingSetSize) / 1000000.0;
            });

            source.Clr.GCSuspendEEStart += delegate(GCSuspendEETraceData data)
            {
                var stats = perProc[data];
                Debug.WriteLine("EESuspend start at " + data.TimeStampRelativeMSec.ToString("f4"));
                Debug.Assert(stats.suspendThreadID == -1);        // we should not set this twice. 
                stats.suspendThreadID = data.ThreadID;
                stats.GCSuspendTimeRelativeMSec = data.TimeStampRelativeMSec;
            };

            source.Clr.RuntimeStart += delegate(RuntimeInformationTraceData data)
            {
                var stats = perProc[data];
                stats.RuntimeVersion = "V " + data.VMMajorVersion.ToString() + "." + data.VMMinorVersion + "." + data.VMBuildNumber;
                stats.StartupFlags = data.StartupFlags;
                if (stats.CommandLine == null)
                    stats.CommandLine = data.CommandLine;
            };

            source.Kernel.ProcessStart += delegate(ProcessTraceData data)
            {
                var stats = perProc[data];
                var commandLine = data.CommandLine;
                if (!string.IsNullOrEmpty(commandLine))
                    stats.CommandLine = commandLine;
            };

            source.Kernel.PerfInfoSampleProf += delegate(SampledProfileTraceData data)
            {
                var stats = perProc.TryGet(data);
                if (stats != null)
                    stats.ProcessCpuTimeMsec++;
            };

            source.Clr.GCRestartEEStop += delegate(GCNoUserDataTraceData data)
            {
                Debug.WriteLine("EEResume complete at " + data.TimeStampRelativeMSec.ToString("f4"));
                GCProcess stats = perProc[data];
                Debug.Assert(stats.suspendThreadID != -1);

                // For every GC that has not been resumed yet, resume it.  
                for (int i = stats.events.Count - 1; 0 <= i; --i)
                {
                    GCEvent _event = stats.events[i];
                    if (_event.PauseDurationMSec != 0 || _event.SuspendDurationMSec != 0)
                        break;

                    Debug.Assert(_event.StoppedOnThreadId == 0 || _event.StoppedOnThreadId == data.ThreadID);
                    // Set the pause duration.  
                    _event.PauseDurationMSec = data.TimeStampRelativeMSec - _event.PauseStartRelMSec;
                    // Note that for background GCs (which have not had a GCStop), _event.DurationMSec is 0 which is what we want.  
                    _event.SuspendDurationMSec = _event.PauseDurationMSec - _event.GCDurationMSec;

                    // Compute the more global stats that depend on PauseDuration
                    stats.Generations[_event.GCGeneration].MaxPauseDurationMSec = Math.Max(stats.Generations[_event.GCGeneration].MaxPauseDurationMSec, _event.PauseDurationMSec);
                    stats.Generations[_event.GCGeneration].TotalPauseTimeMSec += _event.PauseDurationMSec;
                    stats.Generations[_event.GCGeneration].MaxSuspendDurationMSec = Math.Max(stats.Generations[_event.GCGeneration].MaxSuspendDurationMSec, _event.SuspendDurationMSec);
                    stats.Total.MaxPauseDurationMSec = Math.Max(stats.Total.MaxPauseDurationMSec, _event.PauseDurationMSec);
                    stats.Total.TotalPauseTimeMSec += _event.PauseDurationMSec;
                    stats.Total.MaxSuspendDurationMSec = Math.Max(stats.Total.MaxSuspendDurationMSec, _event.SuspendDurationMSec);
                }

                stats.lastGCEndTimeRelativeMSec = data.TimeStampRelativeMSec;
                // This is just so some asserts work
                stats.suspendThreadID = -1;
            };

            source.Clr.GCAllocationTick += delegate(GCAllocationTickTraceData data)
            {
                GCProcess stats = perProc[data];
                stats.currentAllocatedSizeMB += data.AllocationAmount / 1000000.0;
            };

            source.Clr.GCStart += delegate(GCStartTraceData data)
            {
                GCProcess stats = perProc[data];
                GCEvent _event = new GCEvent();
                _event.StartTime100ns = data.TimeStamp100ns;

                Debug.Assert(stats.suspendThreadID == data.ThreadID);
                _event.PauseStartRelMSec = stats.GCSuspendTimeRelativeMSec;

                _event.GCStartRelMSec = data.TimeStampRelativeMSec;
                Debug.Assert(0 <= data.Depth && data.Depth <= 2);
                _event.GCGeneration = data.Depth;
                _event.Reason = data.Reason;
                _event.GCNumber = data.Count;
                _event.Type = data.Type;
                _event.SizeBeforeMB = stats.currentAllocatedSizeMB;
                _event.AllocedSinceLastGC = stats.currentAllocatedSizeMB - stats.sizeOfHeapAtLastGCMB;
                _event.DurationSinceLastGCMSec = _event.GCStartRelMSec - stats.lastGCEndTimeRelativeMSec;

                stats.events.Add(_event);
            };
            source.Clr.GCStop += delegate(GCEndTraceData data)
            {
                GCProcess stats = perProc[data];
                GCEvent _event = stats.FindGCEventForGCNumber(data.Count);
                if (_event != null)
                {
                    _event.GCDurationMSec = (data.TimeStamp100ns - _event.StartTime100ns) / 10000.0F;
                    _event.StoppedOnThreadId = data.ThreadID;
                }
            };
            source.Clr.GCHeapStats += delegate(GCHeapStatsTraceData data)
            {
                GCProcess stats = perProc[data];
                GCEvent _event = stats.FindGCEventStoppedOnThread(data.ThreadID);
                if (_event != null)
                {
                    _event.SizeAfterMB = (data.GenerationSize1 + data.GenerationSize2 + data.GenerationSize3) / 1000000.0;

                    // Update the per-generation information 
                    stats.Generations[_event.GCGeneration].GCCount++;
                    stats.Generations[_event.GCGeneration].TotalGCDurationMSec += _event.GCDurationMSec;
                    stats.Generations[_event.GCGeneration].TotalSizeAfterMB += _event.SizeAfterMB;
                    stats.Generations[_event.GCGeneration].TotalPauseTimeMSec += _event.PauseDurationMSec;
                    stats.Generations[_event.GCGeneration].TotalReclaimedMB += _event.SizeBeforeMB - _event.SizeAfterMB;
                    stats.Generations[_event.GCGeneration].TotalAllocatedMB += _event.AllocedSinceLastGC;
                    stats.Generations[_event.GCGeneration].MaxPauseDurationMSec = Math.Max(stats.Generations[_event.GCGeneration].MaxPauseDurationMSec, _event.PauseDurationMSec);
                    stats.Generations[_event.GCGeneration].MaxSizeBeforeMB = Math.Max(stats.Generations[_event.GCGeneration].MaxSizeBeforeMB, _event.SizeBeforeMB);
                    stats.Generations[_event.GCGeneration].MaxAllocRateMBSec = Math.Max(stats.Generations[_event.GCGeneration].MaxAllocRateMBSec, _event.AllocRateMBSec);

                    // And the total.  
                    stats.Total.GCCount++;
                    stats.Total.TotalGCDurationMSec += _event.GCDurationMSec;
                    stats.Total.TotalSizeAfterMB += _event.SizeAfterMB;
                    stats.Total.TotalPauseTimeMSec += _event.PauseDurationMSec;
                    stats.Total.TotalReclaimedMB += _event.SizeBeforeMB - _event.SizeAfterMB;
                    stats.Total.TotalAllocatedMB += _event.AllocedSinceLastGC;
                    stats.Total.MaxPauseDurationMSec = Math.Max(stats.Total.MaxPauseDurationMSec, _event.PauseDurationMSec);
                    stats.Total.MaxSizeBeforeMB = Math.Max(stats.Total.MaxSizeBeforeMB, _event.SizeBeforeMB);
                    stats.Total.MaxAllocRateMBSec = Math.Max(stats.Total.MaxAllocRateMBSec, _event.AllocRateMBSec);

                    // reset our allocation count.
                    stats.sizeOfHeapAtLastGCMB = stats.currentAllocatedSizeMB = _event.SizeAfterMB;
                }
            };

            source.Process();

#if DEBUG
            foreach (GCProcess gcProcess in perProc)
                foreach (GCEvent _event in gcProcess.events)
                {
                    Debug.Assert(_event.StoppedOnThreadId != 0, "Missing GC Stop event");
                    Debug.Assert(_event.SizeAfterMB != 0, "Missing GC Heap Stats event");
                    Debug.Assert(_event.PauseStartRelMSec != 0, "Missing GC RestartEE event");
                }
#endif
            return perProc;
        }

        public int ProcessID { get; set; }
        public string ProcessName { get; set; }

        public GCInfo[] Generations = new GCInfo[3];
        public GCInfo Total;
        public int ProcessCpuTimeMsec;     // Total CPU time used in process (approximate)
        public StartupFlags StartupFlags;
        public string RuntimeVersion;
        public string CommandLine { get; set; }

        public double PeakWorkingSetMB { get; set; }
        public double PeakVirtualMB { get; set; }

        public void ToHtml(TextWriter writer, string fileName)
        {
            writer.WriteLine("<H3><A Name=\"Stats_{0}\"><font color=\"blue\">GC Stats for for Process {1,5}: {2}</font><A></H3>", ProcessID, ProcessID, ProcessName);
            writer.WriteLine("<UL>");
            if (!string.IsNullOrEmpty(CommandLine))
                writer.WriteLine("<LI>CommandLine: {0}</LI>", CommandLine);
            writer.WriteLine("<LI>Runtime Version: {0}</LI>", RuntimeVersion);
            writer.WriteLine("<LI>CLR Startup Flags: {0}</LI>", StartupFlags);
            writer.WriteLine("<LI>Total CPU Time: {0:f3} msec</LI>", ProcessCpuTimeMsec);
            writer.WriteLine("<LI>Total GC  Time: {0:f3} msec</LI>", Total.TotalGCDurationMSec);
            writer.WriteLine("<LI>Total GC Pause: {0:f3} msec</LI>", Total.TotalPauseTimeMSec);
            if (Total.TotalGCDurationMSec != 0)
                writer.WriteLine("<LI>% CPU Time spent Garbage Collecting: {0:f1}%</LI>", Total.TotalGCDurationMSec * 100.0 / ProcessCpuTimeMsec);

            writer.WriteLine("<LI>Max GC Heap Size: {0:f3} MB</LI>", Total.MaxSizeBeforeMB);
            if (PeakWorkingSetMB != 0)
                writer.WriteLine("<LI>Peak Process Working Set: {0:f3} MB</LI>", PeakWorkingSetMB);
            if (PeakWorkingSetMB != 0)
                writer.WriteLine("<LI>Peak Virtual Memory Usage: {0:f3} MB</LI>", PeakVirtualMB);

            writer.WriteLine("<LI><A HREF=\"#Events_{0}\">Individual GC Events</A></LI>", ProcessID);
            var usersGuideFile = ReportGenerator.WriteUsersGuide(fileName);
            writer.WriteLine("<LI> <A HREF=\"{0}#UnderstandingGCPerf\">GC Perf Users Guide</A></LI>", usersGuideFile);

            writer.WriteLine("</UL>");

            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TH colspan=\"10\" Align=\"Center\">GC Rollup By Generation</TH></TR>");
            writer.WriteLine("<TR><TH colspan=\"10\" Align=\"Center\">All times are in msec.</TH></TR>");
            writer.WriteLine("<TR>" +
                             "<TH>Gen</TH>" +
                             "<TH>Count</TH>" +
                             "<TH>Max<BR/>Pause</TH>" +
                             "<TH>Max<BR/>Before MB</TH>" +
                             "<TH>Max Alloc<BR/>MB/sec</TH>" +
                             "<TH>Total<BR/>Pause</TH>" +
                             "<TH>Total<BR/>Alloc MB</TH>" +
                             "<TH>Total<BR/>Reclaimed MB</TH>" +
                             "<TH>Mean<BR/>Pause</TH>" +
                             "<TH>Mean<BR/>Reclaim MB</TH>" +
                             "</TR>");
            writer.WriteLine("<TR>" +
                             "<TD Align=\"Center\">{0}</TD>" +
                             "<TD Align=\"Center\">{1}</TD>" +
                             "<TD Align=\"Center\">{2:f1}</TD>" +
                             "<TD Align=\"Center\">{3:f1}</TD>" +
                             "<TD Align=\"Center\">{4:f3}</TD>" +
                             "<TD Align=\"Center\">{5:f1}</TD>" +
                             "<TD Align=\"Center\">{6:f1}</TD>" +
                             "<TD Align=\"Center\">{7:f1}</TD>" +
                             "<TD Align=\"Center\">{8:f1}</TD>" +
                             "<TD Align=\"Center\">{9:f1}</TD>" +
                             "</TR>",
                            "ALL",
                            Total.GCCount,
                            Total.MaxPauseDurationMSec,
                            Total.MaxSizeBeforeMB,
                            Total.MaxAllocRateMBSec,
                            Total.TotalPauseTimeMSec,
                            Total.TotalAllocatedMB,
                            Total.TotalReclaimedMB,
                            Total.MeanPauseDurationMSec,
                            Total.MeanReclaimedMB);

            for (int genNum = 0; genNum < Generations.Length; genNum++)
            {
                GCInfo gen = Generations[genNum];
                writer.WriteLine("<TR>" +
                                 "<TD Align=\"Center\">{0}</TD>" +
                                 "<TD Align=\"Center\">{1}</TD>" +
                                 "<TD Align=\"Center\">{2:f2}</TD>" +
                                 "<TD Align=\"Center\">{3:f2}</TD>" +
                                 "<TD Align=\"Center\">{4:f3}</TD>" +
                                 "<TD Align=\"Center\">{5:f2}</TD>" +
                                 "<TD Align=\"Center\">{6:f2}</TD>" +
                                 "<TD Align=\"Center\">{7:f2}</TD>" +
                                 "<TD Align=\"Center\">{8:f2}</TD>" +
                                 "<TD Align=\"Center\">{9:f2}</TD>" +
                                 "</TR>",
                                genNum,
                                gen.GCCount,
                                gen.MaxPauseDurationMSec,
                                gen.MaxSizeBeforeMB,
                                gen.MaxAllocRateMBSec,
                                gen.TotalPauseTimeMSec,
                                gen.TotalAllocatedMB,
                                gen.TotalReclaimedMB,
                                gen.MeanPauseDurationMSec,
                                gen.MeanReclaimedMB);
            }
            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");

            writer.WriteLine("<HR/>");
            writer.WriteLine("<H4><A Name=\"Events_{0}\">Individual GC Events for Process {1,5}: {2}<A></H4>", ProcessID, ProcessID, ProcessName);
            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TH colspan=\"20\" Align=\"Center\">GC Events by Time</TH></TR>");
            writer.WriteLine("<TR><TH colspan=\"20\" Align=\"Center\">All times are in msec.  Start time is msec from trace start.</TH></TR>");
            writer.WriteLine("<TR>" +
                 "<TH>Start Time</TH>" +
                 "<TH>GC Num</TH>" +
                 "<TH>Gen</TH>" +
                 "<TH>Pause</TH>" +
                 "<TH>Alloc Rate<BR/>MB/sec</TH>" +
                 "<TH>Before<BR/>MB</TH>" +
                 "<TH>After<BR/>MB</TH>" +
                 "<TH>Ratio<BR/>Before/After</TH>" +
                 "<TH>Reclaimed</TH>" +
                 "<TH>Suspend<BR/>Time</TH>" +
                 "<TH>Type</TH>" +
                 "<TH>Reason</TH>" +
                 "</TR>");
            foreach (GCEvent _event in events)
            {
                writer.WriteLine("<TR>" +
                                 "<TD Align=\"Center\">{0:f3}</TD>" +
                                 "<TD Align=\"Center\">{1}</TD>" +
                                 "<TD Align=\"Center\">{2}</TD>" +
                                 "<TD Align=\"Center\">{3:f2}</TD>" +
                                 "<TD Align=\"Center\">{4:f3}</TD>" +
                                 "<TD Align=\"Center\">{5:f2}</TD>" +
                                 "<TD Align=\"Center\">{6:f2}</TD>" +
                                 "<TD Align=\"Center\">{7:f2}</TD>" +
                                 "<TD Align=\"Center\">{8:f2}</TD>" +
                                 "<TD Align=\"Center\">{9:f2}</TD>" +
                                 "<TD Align=\"Center\">{10}</TD>" +
                                 "<TD Align=\"Center\">{11}</TD>" +
                                 "</TR>",
                                 _event.PauseStartRelMSec,
                                 _event.GCNumber,
                                 _event.GCGeneration,
                                _event.PauseDurationMSec,
                                _event.AllocRateMBSec,
                                _event.SizeBeforeMB,
                                _event.SizeAfterMB,
                                _event.RatioBeforeAfter,
                                _event.SizeReclaimed,
                                _event.SuspendDurationMSec,
                                _event.Type,
                                _event.Reason);
            }

            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");
            writer.WriteLine("<HR/><HR/><BR/><BR/>");
        }
        public virtual void ToXml(TextWriter writer)
        {
            writer.Write(" <GCProcess");
            writer.Write(" Process="); GCEvent.QuotePadLeft(writer, ProcessName, 10);
            writer.Write(" ProcessID="); GCEvent.QuotePadLeft(writer, ProcessID.ToString(), 5);
            if (ProcessCpuTimeMsec != 0)
            {
                writer.Write(" ProcessCpuTimeMsec="); GCEvent.QuotePadLeft(writer, ProcessCpuTimeMsec.ToString(), 5);
            }
            Total.ToXmlAttribs(writer);
            if (RuntimeVersion != null)
            {
                writer.Write(" RuntimeVersion="); GCEvent.QuotePadLeft(writer, RuntimeVersion, 8);
                writer.Write(" StartupFlags="); GCEvent.QuotePadLeft(writer, StartupFlags.ToString(), 10);
                writer.Write(" CommandLine="); writer.Write(XmlUtilities.XmlQuote(CommandLine));
            }
            if (PeakVirtualMB != 0)
                writer.Write(" PeakVirtualMB="); GCEvent.QuotePadLeft(writer, PeakVirtualMB.ToString(), 8);
            if (PeakWorkingSetMB != 0)
                writer.Write(" PeakWorkingSetMB="); GCEvent.QuotePadLeft(writer, PeakWorkingSetMB.ToString(), 8);
            writer.WriteLine(">");
            writer.WriteLine("  <Generations Count=\"{0}\" TotalGCCount=\"{1}\" TotalGCDurationMSec=\"{2}\">",
                Generations.Length, Total.GCCount, Total.TotalGCDurationMSec);
            for (int gen = 0; gen < Generations.Length; gen++)
            {
                writer.Write("   <Generation Gen=\"{0}\"", gen);
                Generations[gen].ToXmlAttribs(writer);
                writer.WriteLine("/>");
            }
            writer.WriteLine("  </Generations>");

            writer.WriteLine("  <GCEvents Count=\"{0}\">", events.Count);
            foreach (GCEvent _event in events)
                _event.ToXml(writer);
            writer.WriteLine("  </GCEvents>");

            writer.WriteLine(" </GCProcess>");
        }
        #region private
        public virtual void Init(TraceEvent data)
        {
            ProcessID = data.ProcessID;
            ProcessName = data.ProcessName;
        }
        private GCEvent FindGCEventStoppedOnThread(int threadID)
        {
            for (int i = events.Count - 1; 0 <= i; --i)
            {
                GCEvent ret = events[i];
                if (ret.StoppedOnThreadId == threadID)
                    return ret;
            }
            Debug.Assert(false, "Count not find GC Stop for thread " + threadID);
            return null;
        }
        private GCEvent FindGCEventForGCNumber(int gcNumber)
        {
            for (int i = events.Count - 1; 0 <= i; --i)
            {
                GCEvent ret = events[i];
                if (ret.GCNumber == gcNumber)
                    return ret;
                if (ret.GCNumber < gcNumber)
                    break;
            }
            Debug.Assert(false, "Count not find GC Start event for GC count " + gcNumber);
            return null;
        }

        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            ToXml(sw);
            return sw.ToString();
        }

        List<GCEvent> events = new List<GCEvent>();

        // The amount of live objects (as far we know).  This is the size on the last GC + all allocations to date.  
        double currentAllocatedSizeMB;
        double sizeOfHeapAtLastGCMB;
        double lastGCEndTimeRelativeMSec;

        // Keep track of the last time we started suspending the EE.  Will use in 'Start' to set PauseStartRelMSec
        int suspendThreadID = -1;
        double GCSuspendTimeRelativeMSec = -1;

        #endregion
    }

    /// <summary>
    /// GCInfo are accumulated statistics per generation.  
    /// </summary>    
    struct GCInfo
    {
        public int GCCount;
        public double MaxPauseDurationMSec;
        public double MeanPauseDurationMSec { get { if (GCCount == 0) return 0; return TotalPauseTimeMSec / GCCount; } }
        public double MeanSizeAfterMB { get { if (GCCount == 0) return 0; return TotalSizeAfterMB / GCCount; } }
        public double MeanSizeBeforeMB { get { return MeanSizeAfterMB + MeanReclaimedMB; } }
        public double MeanReclaimedMB { get { if (GCCount == 0) return 0; return TotalReclaimedMB / GCCount; } }
        public double MeanAllocatedMB { get { if (GCCount == 0) return 0; return TotalAllocatedMB / GCCount; } }
        public double RatioMeanBeforeAfter { get { var after = MeanSizeAfterMB; if (after == 0) return 0; return (MeanReclaimedMB + after) / after; } }
        public double MeanGCDurationMSec { get { if (GCCount == 0) return 0; return TotalGCDurationMSec / GCCount; } }
        public double MaxSuspendDurationMSec;
        public double MaxSizeBeforeMB;
        public double MaxAllocRateMBSec;

        public double TotalAllocatedMB;
        public double TotalReclaimedMB;
        public double TotalGCDurationMSec;
        internal double TotalPauseTimeMSec;
        internal double TotalSizeAfterMB;      // this does not have a useful meaning so we hide it. 

        public void ToXmlAttribs(TextWriter writer)
        {
            writer.Write(" GCCount="); GCEvent.QuotePadLeft(writer, GCCount.ToString(), 6);
            writer.Write(" MaxPauseDurationMSec="); GCEvent.QuotePadLeft(writer, MaxPauseDurationMSec.ToString("f3"), 10);
            writer.Write(" MeanPauseDurationMSec="); GCEvent.QuotePadLeft(writer, MeanPauseDurationMSec.ToString("f3"), 10);
            writer.Write(" MeanSizeBeforeMB="); GCEvent.QuotePadLeft(writer, MeanSizeBeforeMB.ToString("f1"), 10);
            writer.Write(" MeanSizeAfterMB="); GCEvent.QuotePadLeft(writer, MeanSizeAfterMB.ToString("f1"), 10);
            writer.Write(" MeanReclaimedMB="); GCEvent.QuotePadLeft(writer, MeanReclaimedMB.ToString("f1"), 10);
            writer.Write(" TotalAllocatedMB="); GCEvent.QuotePadLeft(writer, TotalAllocatedMB.ToString("f1"), 10);
            writer.Write(" TotalReclaimedMB="); GCEvent.QuotePadLeft(writer, TotalReclaimedMB.ToString("f1"), 10);
            writer.Write(" TotalGCDurationMSec="); GCEvent.QuotePadLeft(writer, TotalGCDurationMSec.ToString("f3"), 10);
            writer.Write(" TotalPauseTimeMSec="); GCEvent.QuotePadLeft(writer, TotalPauseTimeMSec.ToString("f3"), 10);
            writer.Write(" MaxAllocRateMBSec="); GCEvent.QuotePadLeft(writer, MaxAllocRateMBSec.ToString("f3"), 10);
            writer.Write(" MeanGCDurationMSec="); GCEvent.QuotePadLeft(writer, MeanGCDurationMSec.ToString("f3"), 10);
            writer.Write(" MaxSuspendDurationMSec="); GCEvent.QuotePadLeft(writer, MaxSuspendDurationMSec.ToString("f3"), 10);
            writer.Write(" MaxSizeBeforeMB="); GCEvent.QuotePadLeft(writer, MaxSizeBeforeMB.ToString("f3"), 10);
        }
    }

    /// <summary>
    /// GCEvent holds information on a particluar GC
    /// </summary>
    class GCEvent
    {
        public double PauseStartRelMSec;        //  Set in SuspendGCStart
        public double PauseDurationMSec;        //  Total time EE is suspended (can be less than GC time for background)
        public double DurationSinceLastGCMSec;  //  Set in Start
        public double AllocedSinceLastGC;       //  Set in Start
        public double SizeBeforeMB;             //  Set in Start
        public double SizeAfterMB;              //  Set in HeapStats
        public double SizeReclaimed { get { return SizeBeforeMB - SizeAfterMB; } }
        public double RatioBeforeAfter { get { if (SizeAfterMB == 0) return 0; return SizeBeforeMB / SizeAfterMB; } }
        public double AllocRateMBSec { get { return AllocedSinceLastGC * 1000.0 / DurationSinceLastGCMSec; } }
        public int GCNumber;                    //  Set in Start (starts at 1, unique for process)
        public int GCGeneration;                //  Set in Start (Generation 0, 1 or 2)
        public double GCDurationMSec;           //  Set in Stop This is JUST the GC time (not including suspension)
        public double SuspendDurationMSec;      //  Time taken before and after GC to suspend and resume EE.  
        public double GCStartRelMSec;           //  Set in Start
        public GCType Type;                     //  Set in Start
        public GCReason Reason;                 //  Set in Start

        public void ToXml(TextWriter writer)
        {
            writer.Write("   <GCEvent");
            writer.Write(" PauseStartRelMSec="); QuotePadLeft(writer, PauseStartRelMSec.ToString("f3").ToString(), 10);
            writer.Write(" PauseDurationMSec="); QuotePadLeft(writer, PauseDurationMSec.ToString("f3").ToString(), 10);
            writer.Write(" SizeBeforeMB="); QuotePadLeft(writer, SizeBeforeMB.ToString("f3"), 10);
            writer.Write(" SizeAfterMB="); QuotePadLeft(writer, SizeAfterMB.ToString("f3"), 10);
            writer.Write(" SizeReclaimed="); QuotePadLeft(writer, SizeReclaimed.ToString("f3"), 10);
            writer.Write(" RatioBeforeAfter="); QuotePadLeft(writer, RatioBeforeAfter.ToString("f3"), 5);
            writer.Write(" AllocRateMBSec="); QuotePadLeft(writer, AllocRateMBSec.ToString("f3"), 5);
            writer.Write(" GCNumber="); QuotePadLeft(writer, GCNumber.ToString(), 10);
            writer.Write(" GCGeneration="); QuotePadLeft(writer, GCGeneration.ToString(), 3);
            writer.Write(" GCDurationMSec="); QuotePadLeft(writer, GCDurationMSec.ToString("f3").ToString(), 10);
            writer.Write(" SuspendDurationMSec="); QuotePadLeft(writer, SuspendDurationMSec.ToString("f3").ToString(), 10);
            writer.Write(" GCStartRelMSec="); QuotePadLeft(writer, GCStartRelMSec.ToString("f3"), 10);
            writer.Write(" DurationSinceLastGCMSec="); QuotePadLeft(writer, DurationSinceLastGCMSec.ToString("f3"), 5);
            writer.Write(" AllocedSinceLastGC="); QuotePadLeft(writer, AllocedSinceLastGC.ToString("f3"), 5);
            writer.Write(" Type="); QuotePadLeft(writer, Type.ToString(), 18);
            writer.Write(" Reason="); QuotePadLeft(writer, Reason.ToString(), 27);
            writer.WriteLine("/>");
        }
        #region private
        internal static void QuotePadLeft(TextWriter writer, string str, int totalSize)
        {
            int spaces = totalSize - 2 - str.Length;
            while (spaces > 0)
            {
                --spaces;
                writer.Write(' ');
            }
            writer.Write('"');
            writer.Write(str);
            writer.Write('"');
        }
        internal double StartTime100ns;     // Set in Start
        internal int StoppedOnThreadId;     // Set in Stop
        #endregion
    }

    /************************************************************************/
    /*  JIT Stats */

    /// <summary>
    /// JitInfo holds statistics on groups of methods. 
    /// </summary>
    class JitInfo
    {
        public int Count;
        public double JitTimeMSec;
        public int ILSize;
        public int NativeSize;
        #region private
        internal void Update(JitEvent _event)
        {
            Count++;
            JitTimeMSec += _event.JitTimeMSec;
            ILSize += _event.ILSize;
            NativeSize += _event.NativeSize;
        }
        #endregion
    };

    /// <summary>
    /// JitEvent holds information on a JIT compile of a particular method 
    /// </summary>
    class JitEvent
    {
        public double StartTimeMSec;
        public string MethodName;
        public string ModuleILPath;
        public double JitTimeMSec;
        public int ILSize;
        public int NativeSize;

        public void ToXml(TextWriter writer)
        {
            writer.Write("   <JitEvent");
            writer.Write(" StartMSec="); GCEvent.QuotePadLeft(writer, StartTimeMSec.ToString("f3"), 10);
            writer.Write(" JitTimeMSec="); GCEvent.QuotePadLeft(writer, JitTimeMSec.ToString("f3"), 8);
            writer.Write(" ILSize="); GCEvent.QuotePadLeft(writer, ILSize.ToString(), 10);
            writer.Write(" NativeSize="); GCEvent.QuotePadLeft(writer, NativeSize.ToString(), 10);
            if (MethodName != null)
            {
                writer.Write(" MethodName="); writer.Write(XmlUtilities.XmlQuote(MethodName));
            }
            if (ModuleILPath != null)
            {
                writer.Write(" ModuleILPath="); writer.Write(XmlUtilities.XmlQuote(ModuleILPath));
            }
            writer.WriteLine("/>");
        }
        #region private
        internal int ThreadID;
        internal long StartTime100ns;
        #endregion
    }

    /// <summary>
    /// JitProcess holds information about Jitting for a particular process.
    /// </summary>
    class JitProcess : ProcessLookupContract
    {
        public static ProcessLookup<JitProcess> Collect(TraceEventDispatcher source)
        {
            ProcessLookup<JitProcess> perProc = new ProcessLookup<JitProcess>();
            source.Clr.MethodJittingStarted += delegate(MethodJittingStartedTraceData data)
            {
                JitProcess stats = perProc[data];
                stats.LogJitStart(data, GetMethodName(data), data.MethodILSize, data.ModuleID);
            };
            source.Clr.LoaderModuleLoad += delegate(ModuleLoadUnloadTraceData data)
            {
                JitProcess stats = perProc[data];
                stats.moduleNamesFromID[data.ModuleID] = data.ModuleILPath;
            };
            source.Clr.MethodLoadVerbose += delegate(MethodLoadUnloadVerboseTraceData data)
            {
                if (data.IsJitted)
                    MethodComplete(perProc, data, data.MethodSize, data.ModuleID, GetMethodName(data));
            };

            source.Clr.MethodLoad += delegate(MethodLoadUnloadTraceData data)
            {
                if (data.IsJitted)
                    MethodComplete(perProc, data, data.MethodSize, data.ModuleID, "");
            };
            source.Clr.RuntimeStart += delegate(RuntimeInformationTraceData data)
            {
                JitProcess stats = perProc[data];
                stats.isClr4 = true;
                if (stats.CommandLine == null)
                    stats.CommandLine = data.CommandLine;
            };

            source.Kernel.ProcessStart += delegate(ProcessTraceData data)
            {
                var stats = perProc[data];
                var commandLine = data.CommandLine;
                if (!string.IsNullOrEmpty(commandLine))
                    stats.CommandLine = commandLine;
            };

            source.Kernel.PerfInfoSampleProf += delegate(SampledProfileTraceData data)
            {
                JitProcess stats = perProc.TryGet(data);
                if (stats != null)
                    stats.ProcessCpuTimeMsec++;
            };

            source.Process();
#if DEBUG
            foreach (JitProcess jitProcess in perProc)
                foreach (JitEvent _event in jitProcess.events)
                {
                }
#endif
            return perProc;
        }

        public int ProcessID { get; set; }
        public string ProcessName { get; set; }
        public string CommandLine { get; set; }

        public bool isClr4;
        public bool warnedUser;
        public int ProcessCpuTimeMsec;     // Total CPU time used in process (approximate)

        public JitInfo Total = new JitInfo();

        public virtual void ToHtml(TextWriter writer, string fileName)
        {
            writer.WriteLine("<H3><A Name=\"Stats_{0}\"><font color=\"blue\">JIT Stats for for Process {1,5}: {2}</font><A></H3>", ProcessID, ProcessID, ProcessName);
            writer.WriteLine("<UL>");
            if (!isClr4)
                writer.WriteLine("<LI><Font color=\"red\">Warning: This process loaded a V2.0 CLR.  Can not compute JitTime or ILSize, These will appear as 0.</font></LI>");
            if (!string.IsNullOrEmpty(CommandLine))
                writer.WriteLine("<LI>CommandLine: {0}</LI>", CommandLine);
            writer.WriteLine("<LI>Process CPU Time: {0} msec</LI>", ProcessCpuTimeMsec);
            if (Total.JitTimeMSec != 0)
                writer.WriteLine("<LI>% CPU Time spent JIT compiling: {0:f1}%</LI>", Total.JitTimeMSec * 100.0 / ProcessCpuTimeMsec);
            writer.WriteLine("<LI><A HREF=\"#Events_{0}\">Individual JIT Events</A></LI>", ProcessID);
            var usersGuideFile = ReportGenerator.WriteUsersGuide(fileName);
            writer.WriteLine("<LI> <A HREF=\"{0}#UnderstandingJITPerf\">JIT Perf Users Guide</A></LI>", usersGuideFile);
            writer.WriteLine("</UL>");

            writer.WriteLine("<P>" +
                "Below is a table of the time taken to JIT compile the methods used in the program, broken down by module.  \r\n" +
                "If this time is significant you can eliminate it by <A href=\"http://msdn.microsoft.com/en-us/magazine/cc163808.aspx\">NGening</A> your application.  \r\n" +
                "This will improve the startup time for your app.  \r\n" +
                "</P>");

            writer.WriteLine("<P>" +
                "The list below is also useful for tuning the startup performance of your application in general.  \r\n" +
                "In general you want as little to be run during startup as possible.  \r\n" +
                "If you have 1000s of methods being compiled on startup " + 
                "you should try to defer some of that computation until absolutely necessary.\r\n" + 
                "</P>");

            // Sort the module list by Jit Time;
            List<string> moduleNames = new List<string>(moduleStats.Keys);
            moduleNames.Sort(delegate(string x, string y)
            {
                double diff = moduleStats[y].JitTimeMSec - moduleStats[x].JitTimeMSec;
                if (diff > 0)
                    return 1;
                else if (diff < 0)
                    return -1;
                return 0;
            });

            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TH>Name</TH><TH>JitTime<BR/>msec</TH><TH>Num Methods</TH><TH>IL Size</TH><TH>Native Size</TH></TR>");
            writer.WriteLine("<TR><TD Align=\"Center\">{0}</TD><TD Align=\"Center\">{1:f1}</TD><TD Align=\"Center\">{2:f1}</TD><TD Align=\"Center\">{3:f1}</TD><TD Align=\"Center\">{4:f1}</TD></TR>",
                "TOTAL", Total.JitTimeMSec, Total.Count, Total.ILSize, Total.NativeSize);
            foreach (string moduleName in moduleNames)
            {
                JitInfo info = moduleStats[moduleName];
                writer.WriteLine("<TR><TD Align=\"Center\">{0}</TD><TD Align=\"Center\">{1:f1}</TD><TD Align=\"Center\">{2:f1}</TD><TD Align=\"Center\">{3:f1}</TD><TD Align=\"Center\">{4:f1}</TD></TR>",
                    moduleName, info.JitTimeMSec, info.Count, info.ILSize, info.NativeSize);
            }
            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");

            writer.WriteLine("<HR/>");
            writer.WriteLine("<H4><A Name=\"Events_{0}\">Individual JIT Events for Process {1,5}: {2}<A></H4>", ProcessID, ProcessID, ProcessName);
            writer.WriteLine("<Center>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TH>Start (msec)</TH><TH>JitTime</BR>msec</TH><TH>IL Size</TH><TH>Native Size</TH><TH>Method Name</TH><TH>Module</TH></TR>");
            foreach (JitEvent _event in events)
            {
                writer.WriteLine("<TR><TD Align=\"Center\">{0:f3}</TD><TD Align=\"Center\">{1:f1}</TD><TD Align=\"Center\">{2:f1}</TD><TD Align=\"Center\">{3:f1}</TD><TD Align=Left>{4}</TD><TD Align=\"Center\">{5}</TD></TR>",
                    _event.StartTimeMSec, _event.JitTimeMSec, _event.ILSize, _event.NativeSize, _event.MethodName ?? "&nbsp;",
                    _event.ModuleILPath != null ? Path.GetFileName(_event.ModuleILPath) : "&nbsp;");
            }
            writer.WriteLine("</Table>");
            writer.WriteLine("</Center>");
            writer.WriteLine("<HR/><HR/><BR/><BR/>");
        }
        public virtual void ToXml(TextWriter writer)
        {
            writer.Write(" <JitProcess Process=\"{0}\" ProcessID=\"{1}\" JitTimeMSec=\"{2:f3}\" Count=\"{3}\" ILSize=\"{4}\" NativeSize=\"{5}\"",
                ProcessName, ProcessID, Total.JitTimeMSec, Total.Count, Total.ILSize, Total.NativeSize);
            if (ProcessCpuTimeMsec != 0)
                writer.Write(" ProcessCpuTimeMsec=\"{0}\"", ProcessCpuTimeMsec);
            if (CommandLine != null)
                writer.Write(" CommandLine=\"{0}\"", XmlUtilities.XmlEscape(CommandLine, false));
            writer.WriteLine(">");
            writer.WriteLine("  <JitEvents>");
            foreach (JitEvent _event in events)
                _event.ToXml(writer);
            writer.WriteLine("  </JitEvents>");

            writer.WriteLine(" <ModuleStats Count=\"{0}\" TotalCount=\"{1}\" TotalJitTimeMSec=\"{2:f3}\" TotalILSize=\"{3}\" TotalNativeSize=\"{4}\">",
                moduleStats.Count, Total.Count, Total.JitTimeMSec, Total.ILSize, Total.NativeSize);

            // Sort the module list by Jit Time;
            List<string> moduleNames = new List<string>(moduleStats.Keys);
            moduleNames.Sort(delegate(string x, string y)
            {
                double diff = moduleStats[y].JitTimeMSec - moduleStats[x].JitTimeMSec;
                if (diff > 0)
                    return 1;
                else if (diff < 0)
                    return -1;
                return 0;
            });

            foreach (string moduleName in moduleNames)
            {
                JitInfo info = moduleStats[moduleName];
                writer.Write("<Module");
                writer.Write(" JitTimeMSec="); GCEvent.QuotePadLeft(writer, info.JitTimeMSec.ToString("f3"), 11);
                writer.Write(" Count="); GCEvent.QuotePadLeft(writer, info.Count.ToString(), 7);
                writer.Write(" ILSize="); GCEvent.QuotePadLeft(writer, info.ILSize.ToString(), 9);
                writer.Write(" NativeSize="); GCEvent.QuotePadLeft(writer, info.NativeSize.ToString(), 9);
                writer.Write(" Name=\"{0}\"", moduleName);
                writer.WriteLine("/>");
            }
            writer.WriteLine("  </ModuleStats>");

            writer.WriteLine(" </JitProcess>");
        }
        #region private
        private static void MethodComplete(ProcessLookup<JitProcess> perProc, TraceEvent data, int methodNativeSize, long moduleID, string methodName)
        {
            JitProcess stats = perProc[data];
            JitEvent _event = stats.FindIncompleteJitEventOnThread(data.ThreadID);
            if (_event == null)
            {
                // We don't have JIT start, do the best we can.  
                _event = stats.LogJitStart(data, methodName, 0, moduleID);
                if (stats.isClr4)
                {
                    Console.WriteLine("Warning: MethodComplete at {0:f3} process {1} thread {2} without JIT Start, assuming 0 JIT time",
                        data.TimeStampRelativeMSec, data.ProcessName, data.ThreadID);
                }
                else if (!stats.warnedUser)
                {
                    // Console.WriteLine("Warning: Process {0} ({1}) is running a V2.0 CLR, no JIT Start events available, so JIT times will all be 0.", stats.ProcessName, stats.ProcessID);
                    stats.warnedUser = true;
                }
            }
            _event.NativeSize = methodNativeSize;
            _event.JitTimeMSec = (data.TimeStamp100ns - _event.StartTime100ns) / 10000.0;

            if (_event.ModuleILPath != null)
                stats.moduleStats.GetOrCreate(_event.ModuleILPath).Update(_event);
            stats.Total.Update(_event);
        }

        private JitEvent LogJitStart(TraceEvent data, string methodName, int ILSize, long moduleID)
        {
            JitEvent _event = new JitEvent();
            _event.StartTime100ns = data.TimeStamp100ns;
            _event.StartTimeMSec = data.TimeStampRelativeMSec;
            _event.ILSize = ILSize;
            _event.MethodName = methodName;
            _event.ThreadID = data.ThreadID;
            moduleNamesFromID.TryGetValue(moduleID, out _event.ModuleILPath);
            events.Add(_event);
            return _event;
        }

        private static string GetMethodName(MethodJittingStartedTraceData data)
        {
            int parenIdx = data.MethodSignature.IndexOf('(');
            if (parenIdx < 0)
                parenIdx = data.MethodSignature.Length;

            return data.MethodNamespace + "." + data.MethodName + data.MethodSignature.Substring(parenIdx);
        }
        private static string GetMethodName(MethodLoadUnloadVerboseTraceData data)
        {
            int parenIdx = data.MethodSignature.IndexOf('(');
            if (parenIdx < 0)
                parenIdx = data.MethodSignature.Length;

            return data.MethodNamespace + "." + data.MethodName + data.MethodSignature.Substring(parenIdx);
        }

        public virtual void Init(TraceEvent data)
        {
            ProcessID = data.ProcessID;
            ProcessName = data.ProcessName;
        }
        private JitEvent FindIncompleteJitEventOnThread(int threadID)
        {
            for (int i = events.Count - 1; 0 <= i; --i)
            {
                JitEvent ret = events[i];
                if (ret.ThreadID == threadID)
                {
                    // This is a completed JIT event, not what we are looking for. 
                    if (ret.NativeSize > 0 || ret.JitTimeMSec > 0)
                        return null;
                    return ret;
                }
            }
            return null;
        }
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            ToXml(sw);
            return sw.ToString();
        }

        List<JitEvent> events = new List<JitEvent>();
        Dictionary<long, string> moduleNamesFromID = new Dictionary<long, string>();
        SortedDictionary<string, JitInfo> moduleStats = new SortedDictionary<string, JitInfo>(StringComparer.OrdinalIgnoreCase);
        #endregion
    }

    class DllProcess : ProcessLookupContract
    {
        public static ProcessLookup<DllProcess> Collect(TraceEventDispatcher source)
        {
            ProcessLookup<DllProcess> perProc = new ProcessLookup<DllProcess>();
            source.Kernel.ImageLoad += delegate(ImageLoadTraceData data)
            {
                DllProcess stats = perProc[data];
                stats.m_images.Add((ImageLoadTraceData)data.Clone());
            };

            /***
            source.Kernel.DiskIoRead += delegate(DiskIoTraceData data)
            {
            };
             ****/

            source.Clr.LoaderModuleLoad += delegate(ModuleLoadUnloadTraceData data)
            {
                DllProcess stats = perProc[data];
                stats.m_modules.Add((ModuleLoadUnloadTraceData)data.Clone());
            };
            source.Clr.RuntimeStart += delegate(RuntimeInformationTraceData data)
            {
                DllProcess stats = perProc[data];
                if (stats.CommandLine == null)
                    stats.CommandLine = data.CommandLine;
            };
            source.Kernel.ProcessStart += delegate(ProcessTraceData data)
            {
                var stats = perProc[data];
                var commandLine = data.CommandLine;
                if (!string.IsNullOrEmpty(commandLine))
                    stats.CommandLine = commandLine;
            };

            /**
            source.Kernel.PageFaultHardFault += delegate(PageFaultHardFaultTraceData data)
            {


            };
            **/

            source.Process();
            return perProc;
        }

        public void Init(TraceEvent data)
        {
            ProcessID = data.ProcessID;
            ProcessName = data.ProcessName;
            m_images = new List<ImageLoadTraceData>();
            m_modules = new List<ModuleLoadUnloadTraceData>();
        }
        public void ToXml(TextWriter writer)
        {
            writer.Write(" <DllLoads Process=\"{0}\" ProcessID=\"{1}\"", ProcessName, ProcessID);
            if (CommandLine != null)
                writer.Write(" CommandLine=\"{0}\"", XmlUtilities.XmlEscape(CommandLine));
            writer.WriteLine(">");

            StringBuilder sb = new StringBuilder();
            if (m_modules.Count > 0)
            {
                writer.WriteLine("   <ModuleLoads>");
                foreach (var module in m_modules)
                {
                    module.ToXml(sb);
                    writer.Write("    ");
                    writer.WriteLine(sb);
                    sb.Length = 0;
                }
                writer.WriteLine("   </ModuleLoads>");
            }

            if (m_images.Count > 0)
            {
                writer.WriteLine("   <ImageLoads>");
                foreach (var image in m_images)
                {
                    image.ToXml(sb);
                    writer.Write("    ");
                    var element = sb.ToString();
                    sb.Length = 0;

                    var callstack = image.CallStack();
                    if (callstack != null)
                    {
                        element = XmlUtilities.OpenXmlElement(element);
                        writer.WriteLine(element);
                        writer.Write("   ");
                        writer.Write(callstack.ToString().Replace("\n", "\n   "));
                        writer.WriteLine("    </Event>");
                    }
                    else 
                        writer.Write(element);
                }
                writer.WriteLine("   </ImageLoads>");
            }

            writer.WriteLine(" </DllLoads>");
        }
        public void ToHtml(TextWriter writer, string fileName)
        {
            throw new NotImplementedException();
        }

        public int ProcessID { get; set; }
        public string ProcessName { get; set; }
        public string CommandLine { get; set; }

        List<ImageLoadTraceData> m_images;
        List<ModuleLoadUnloadTraceData> m_modules;
    }

    /** TODO REMOVE?
    class DllInfo
    {
        public DllInfo(string name, Address baseAddress, int size, double loadTimeRelMSec)
        {
            Name = name;
            BaseAddress = baseAddress;
            Size = size;
            LoadTimeRelMsec = loadTimeRelMSec;
        }

        public double LoadTimeRelMsec;
        public string Name;
        public Address BaseAddress;
        public int Size;
        public TraceCallStack LoadCallStack;

        public void ToXml(StringBuilder sb)
        {
            sb.Append("    <ImageLoad");
            sb.Append(" LoadTimeRelMSec=\"").Append(LoadTimeRelMsec.ToString("f3")).Append("\"");
            sb.Append(" Name=\"").Append(Name).Append("\"");
            sb.Append(" BaseAddress=\"0x").Append(((long)BaseAddress).ToString("x")).Append("\"");
            sb.Append(" Size=\"0x").Append(((long)Size).ToString("x")).Append("\"");
            sb.Append(">").AppendLine();
            if (LoadCallStack != null)
            {
                sb.Append("     <StackTrace>").AppendLine();
                LoadCallStack.ToString(sb);
                sb.Append("     </StackTrace>").AppendLine();
            }
            sb.Append("    </ImageLoad>").AppendLine();
        }
    }
     ****/

    /************************************************************************/
    /*  Reusable stuff */

    static class SortedDictionaryExtentions
    {
        // As it's name implies, it either fetches soemthign from a dictionary
        // or creates it if it does not already exist (using he default
        // constructor)
        public static V GetOrCreate<K, V>(this SortedDictionary<K, V> dict, K key) where V : new()
        {
            V value;
            if (!dict.TryGetValue(key, out value))
            {
                value = new V();
                dict.Add(key, value);
            }
            return value;
        }
    }

    /// <summary>
    /// ProcessLookup is a generic lookup by process.  
    /// </summary>
    class ProcessLookup<T> : IEnumerable<T> where T : ProcessLookupContract, new()
    {
        /// <summary>
        /// Given an event, find the 'T' that cooresponds to that the process 
        /// associated with that event.  
        /// </summary>
        public T this[TraceEvent data]
        {
            get
            {
                T ret;
                if (!perProc.TryGetValue(data.ProcessID, out ret))
                {
                    ret = new T();
                    ret.Init(data);
                    perProc.Add(data.ProcessID, ret);
                }
                return ret;
            }
        }
        public T TryGet(TraceEvent data)
        {
            T ret;
            perProc.TryGetValue(data.ProcessID, out ret);
            return ret;
        }
        public void ToXml(TextWriter writer, string tag)
        {
            writer.WriteLine("<{0} Count=\"{0}\">", tag, perProc.Count);
            foreach (T stats in perProc.Values)
            {
                stats.ToXml(writer);
            }
            writer.WriteLine("</{0}>", tag);
        }
        public void ToHtml(TextWriter writer, string fileName, string title, Predicate<T> filter)
        {
            writer.WriteLine("<H2>{0}</H2>", title);

            int count = perProc.Count;
            if (filter != null)
            {
                count = 0;
                foreach (T stats in perProc.Values)
                    if (filter(stats))
                        count++;

                if (count == 0)
                    writer.WriteLine("<p>No processes match filter.</p>");
            }

            if (count > 1)
            {
                writer.WriteLine("<UL>");
                foreach (int procId in perProc.Keys)
                {
                    if (filter != null && !filter(perProc[procId]))
                        continue;

                    var id = perProc[procId].CommandLine;
                    if (string.IsNullOrEmpty(id))
                        id = perProc[procId].ProcessName;
                    writer.WriteLine("<LI><A HREF=\"#Stats_{0}\">Process {0,5}: {1}</A></LI>", procId, XmlUtilities.XmlEscape(id));
                }
                writer.WriteLine("</UL>");
                writer.WriteLine("<HR/><HR/><BR/><BR/>");
            }
            foreach (T stats in perProc.Values)
            {
                if (filter == null || filter(stats))
                    stats.ToHtml(writer, fileName);
            }

            writer.WriteLine("<BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/><BR/>");
        }

        public bool TryGetByID(int processID, out T ret)
        {
            return perProc.TryGetValue(processID, out ret);
        }

        #region private
        public IEnumerator<T> GetEnumerator() { return perProc.Values.GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { throw new NotImplementedException(); }
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            ToXml(sw, "Processes");
            return sw.ToString();
        }

        SortedDictionary<int, T> perProc = new SortedDictionary<int, T>();
        #endregion
    }

    /// <summary>
    /// ProcessLookupContract is used by code:ProcessLookup.  The type
    /// parameter needs to implement these functions 
    /// </summary>
    interface ProcessLookupContract
    {
        /// <summary>
        /// Init is called after a new 'T' is created, to initialize the new instance
        /// </summary>
        void Init(TraceEvent data);
        /// <summary>
        /// Prints the 'T' as XML, to 'writer'
        /// </summary>
        void ToXml(TextWriter writer);
        void ToHtml(TextWriter writer, string fileName);
        int ProcessID { get; }
        string ProcessName { get; }
        string CommandLine { get; }
    }
}
