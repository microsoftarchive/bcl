// Copyright (c) Microsoft Corporation.  All rights reserved
using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics.Eventing;
using System.IO;
using System.Diagnostics;
using Diagnostics.Eventing;

namespace SystemManagement
{
    /// <summary>
    /// GCProcess holds information about GCs in a particular process. 
    /// </summary>
    class GCProcess : ProcessLookupContract
    {
        public static ProcessLookup<GCProcess> Collect(TraceEventDispatcher source)
        {
            ProcessLookup<GCProcess> perProc = new ProcessLookup<GCProcess>();


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
                stats.CommandLine = data.CommandLine;
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
                    stats.Generations[_event.GCGeneration].TotalAllocatedMB += stats.currentAllocatedSizeMB - stats.sizeOfHeapAtLastGCMB;
                    stats.Generations[_event.GCGeneration].MaxPauseDurationMSec = Math.Max(stats.Generations[_event.GCGeneration].MaxPauseDurationMSec, _event.PauseDurationMSec);
                    stats.Generations[_event.GCGeneration].MaxSizeBeforeMB = Math.Max(stats.Generations[_event.GCGeneration].MaxSizeBeforeMB, _event.SizeBeforeMB);


                    // And the total.  
                    stats.Total.GCCount++;
                    stats.Total.TotalGCDurationMSec += _event.GCDurationMSec;
                    stats.Total.TotalSizeAfterMB += _event.SizeAfterMB;
                    stats.Total.TotalPauseTimeMSec += _event.PauseDurationMSec;
                    stats.Total.TotalReclaimedMB += _event.SizeBeforeMB - _event.SizeAfterMB;
                    stats.Total.TotalAllocatedMB += stats.currentAllocatedSizeMB - stats.sizeOfHeapAtLastGCMB;
                    stats.Total.MaxPauseDurationMSec = Math.Max(stats.Total.MaxPauseDurationMSec, _event.PauseDurationMSec);
                    stats.Total.MaxSizeBeforeMB = Math.Max(stats.Total.MaxSizeBeforeMB, _event.SizeBeforeMB);

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

        public int ProcessID;
        public string ProcessName;
        public GCInfo[] Generations = new GCInfo[3];
        public GCInfo Total;
        public int ProcessCpuTimeMsec;     // Total CPU time used in process (approximate)
        public StartupFlags StartupFlags;
        public string RuntimeVersion;
        public string CommandLine;

        public virtual void ToXml(TextWriter writer)
        {
            writer.Write(" <GCProcess");
            writer.Write(" Process="); GCEvent.QuotePadLeft(writer, ProcessName, 10);
            writer.Write(" ProcessID="); GCEvent.QuotePadLeft(writer, ProcessID.ToString(), 5);
            if (ProcessCpuTimeMsec != 0)
                writer.Write(" ProcessCpuTimeMsec="); GCEvent.QuotePadLeft(writer, ProcessCpuTimeMsec.ToString(), 5);
            Total.ToXmlAttribs(writer);
            if (RuntimeVersion != null)
            {
                writer.Write(" RuntimeVersion="); GCEvent.QuotePadLeft(writer, RuntimeVersion, 8);
                writer.Write(" StartupFlags="); GCEvent.QuotePadLeft(writer, StartupFlags.ToString(), 10);
                writer.Write(" CommandLine="); writer.Write(XmlUtilities.XmlQuote(CommandLine));
            }

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
        public double PauseStartRelMSec;    //  Set in SuspendGCStart
        public double PauseDurationMSec;    //  Total time EE is suspended (can be less than GC time for background)
        public double SizeBeforeMB;         //  Set in Start
        public double SizeAfterMB;          //  Set in HeapStats
        public double SizeReclaimed { get { return SizeBeforeMB - SizeAfterMB; } }
        public double RatioBeforeAfter { get { if (SizeAfterMB == 0) return 0; return SizeBeforeMB / SizeAfterMB; } }
        public int GCNumber;                //  Set in Start (starts at 1, unique for process)
        public int GCGeneration;            //  Set in Start (Generation 0, 1 or 2)
        public double GCDurationMSec;       //  Set in Stop This is JUST the GC time (not including suspension)
        public double SuspendDurationMSec;  //  Time taken before and after GC to suspend and resume EE.  
        public double GCStartRelMSec;       //  Set in Start
        public GCType Type;                 //  Set in Start
        public GCReason Reason;             //  Set in Start

        public void ToXml(TextWriter writer)
        {
            writer.Write("   <GCEvent");
            writer.Write(" PauseStartRelMSec="); QuotePadLeft(writer, PauseStartRelMSec.ToString("f3").ToString(), 10);
            writer.Write(" PauseDurationMSec="); QuotePadLeft(writer, PauseDurationMSec.ToString("f3").ToString(), 10);
            writer.Write(" SizeBeforeMB="); QuotePadLeft(writer, SizeBeforeMB.ToString("f3"), 10);
            writer.Write(" SizeAfterMB="); QuotePadLeft(writer, SizeAfterMB.ToString("f3"), 10);
            writer.Write(" SizeReclaimed="); QuotePadLeft(writer, SizeReclaimed.ToString("f3"), 10);
            writer.Write(" RatioBeforeAfter="); QuotePadLeft(writer, RatioBeforeAfter.ToString("f3"), 5);
            writer.Write(" GCNumber="); QuotePadLeft(writer, GCNumber.ToString(), 10);
            writer.Write(" GCGeneration="); QuotePadLeft(writer, GCGeneration.ToString(), 3);
            writer.Write(" GCDurationMSec="); QuotePadLeft(writer, GCDurationMSec.ToString("f3").ToString(), 10);
            writer.Write(" SuspendDurationMSec="); QuotePadLeft(writer, SuspendDurationMSec.ToString("f3").ToString(), 10);
            writer.Write(" GCStartRelMSec="); QuotePadLeft(writer, GCStartRelMSec.ToString("f3"), 10);
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
                JitEvent _event = new JitEvent();
                _event.StartTime100ns = data.TimeStamp100ns;
                _event.StartTimeMSec = data.TimeStampRelativeMSec;
                _event.ILSize = data.MethodILSize;
                _event.MethodName = GetMethodName(data);
                _event.ThreadID = data.ThreadID;
                stats.moduleNamesFromID.TryGetValue(data.ModuleID, out _event.ModuleILPath);
                stats.events.Add(_event);
            };
            source.Clr.LoaderModuleLoad += delegate(ModuleLoadUnloadTraceData data)
            {
                JitProcess stats = perProc[data];
                stats.moduleNamesFromID[data.ModuleID] = data.ModuleILPath;
            };
            source.Clr.MethodLoadVerbose += delegate(MethodLoadUnloadVerboseTraceData data)
            {
                if (data.IsJitted)
                    MethodComplete(perProc, data, data.MethodSize);
            };

            source.Clr.MethodLoad += delegate(MethodLoadUnloadTraceData data)
            {
                if (data.IsJitted)
                    MethodComplete(perProc, data, data.MethodSize);
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

        public int ProcessID;
        public string ProcessName;
        public JitInfo Total = new JitInfo();

        public virtual void ToXml(TextWriter writer)
        {
            writer.WriteLine(" <JitProcess Process=\"{0}\" ProcessID=\"{1}\" JitTimeMSec=\"{3:f3}\" Count=\"{2}\" ILSize=\"{4}\" NativeSize=\"{5}\">",
                ProcessName, ProcessID, Total.JitTimeMSec, Total.Count, Total.ILSize, Total.NativeSize);

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
        private static void MethodComplete(ProcessLookup<JitProcess> perProc, TraceEvent data, int methodNativeSize)
        {
            JitProcess stats = perProc[data];
            JitEvent _event = stats.FindJitEventOnThread(data.ThreadID);
            if (_event != null)
            {
                _event.NativeSize = methodNativeSize;
                _event.JitTimeMSec = (data.TimeStamp100ns - _event.StartTime100ns) / 10000.0;

                if (_event.ModuleILPath != null)
                    stats.moduleStats.GetOrCreate(_event.ModuleILPath).Update(_event);
                stats.Total.Update(_event);
            }
            else
            {
                Console.WriteLine("Warning: MethodComplete at {0:f3} process {1} thread {2} without JIT Start.",
                    data.TimeStampRelativeMSec, data.ProcessName, data.ThreadID);
            }
        }
        private static string GetMethodName(MethodJittingStartedTraceData data)
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
        private JitEvent FindJitEventOnThread(int threadID)
        {
            for (int i = events.Count - 1; 0 <= i; --i)
            {
                JitEvent ret = events[i];
                if (ret.ThreadID == threadID)
                    return ret;
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
    }
}
