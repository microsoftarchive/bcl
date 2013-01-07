using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Diagnostics.Tracing;
using Diagnostics.Tracing.Parsers;

namespace System.Diagnostics.Tracing
{
    [EventSource(Name = "PerfView")]
    class PerfViewLogger : System.Diagnostics.Tracing.EventSource
    {
        [Event(1)]
        public void Mark(string message) { WriteEvent(1, message); }
        [Event(2, Opcode = EventOpcode.Start, Task = Tasks.Tracing)]
        public void StartTracing() { WriteEvent(2); }
        [Event(3, Opcode = EventOpcode.Stop, Task = Tasks.Tracing)]
        public void StopTracing() { WriteEvent(3); }
        [Event(4, Opcode = EventOpcode.Start, Task = Tasks.Rundown)]
        public void StartRundown() { WriteEvent(4); }
        [Event(5, Opcode = EventOpcode.Stop, Task = Tasks.Rundown)]
        public void StopRundown() { WriteEvent(5); }
        [Event(6)]
        public void WaitForIdle() { WriteEvent(6); }

        [Event(10)]
        public void CommandLineParameters(string commandLine, string currentDirectory, string version)
        {
            WriteEvent(10, commandLine, currentDirectory, version);
        }
        [Event(11)]
        public void SessionParameters(string sessionName, string sessionFileName, int bufferSizeMB, int circularBuffSizeMB)
        {
            WriteEvent(11, sessionName, sessionFileName, bufferSizeMB, circularBuffSizeMB);
        }
        [Event(12)]
        public void KernelEnableParameters(KernelTraceEventParser.Keywords keywords, KernelTraceEventParser.Keywords stacks)
        {
            WriteEvent(12, (int)keywords, (int)stacks);
        }
        [Event(13)]
        public void ClrEnableParameters(ClrTraceEventParser.Keywords keywords, TraceEventLevel level)
        {
            WriteEvent(13, (long)keywords, (int)level);
        }
        [Event(14)]
        public void ProviderEnableParameters(string providerName, Guid providerGuid, TraceEventLevel level, ulong keywords, TraceEventOptions options)
        {
            WriteEvent(14, providerName, providerGuid, (int)level, keywords, (int)options);
        }
        [Event(15)]
        private void StartAndStopTimes(int startTimeRelMSec, int stopTimeRelMSec)
        {
            WriteEvent(15, startTimeRelMSec, stopTimeRelMSec);
        }
        /// <summary>
        /// Logs the time (relative to this event firing) when the trace was started and stop.
        /// This is useful for circular buffer situations where that may not be known.  
        /// </summary>
        [NonEvent]
        public void StartAndStopTimes()
        {
            var now = DateTime.UtcNow;
            int startTimeRelMSec = 0;
            if (StartTime.Ticks != 0)
                startTimeRelMSec = (int) (now - StartTime).TotalMilliseconds;
            int stopTimeRelMSec = 0;
            if (StopTime.Ticks != 0)
                stopTimeRelMSec = (int) (now - StopTime).TotalMilliseconds;
            StartAndStopTimes(startTimeRelMSec, stopTimeRelMSec);
        }
        [Event(16)]
        public void PerformanceCounterTriggered(string trigger, double value) { WriteEvent(16, trigger, value); }

        public class Tasks {
            public const EventTask Tracing = (EventTask) 1;
            public const EventTask Rundown = (EventTask)2;
        };

        public static PerfViewLogger Log = new PerfViewLogger();

        // Remember the real time where we started and stopped the trace so they are there event 
        // If the Start and Stop events get lost (because of circular buffering)
        public static DateTime StartTime;
        public static DateTime StopTime;
    }
}
