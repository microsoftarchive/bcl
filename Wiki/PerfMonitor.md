## PerfMonitor
PerfMonitor PerfMonitor is a command-line tool for profiling the system using  [Event Tracing for Windows](http://msdn.microsoft.com/en-us/library/bb968803(VS.85).aspx) (ETW) events.   ETW is the power behind the [Windows Performance Analyzer](http://msdn.microsoft.com/en-us/performance/default.aspx) (also known as the XPerf Tool).    The Windows OS has events for just about anything that you could be interested in from a performance standpoint (CPU usage, Context switched Disk I/O DLL Loads, Blocking, all with stack traces).    In addition the .NET Runtime has events for garbage collection, Just In time Compilation, Assembly loading and more.     PerfMonitor is a gateway to all of this information.   

While PerfMonitor is a useful tool, it was really designed more as sample code for the [TraceEvent](TraceEvent) library.  This library is the 'heart' of PerfMonitor, and PerfMonitor is mostly a command line parser and XML printer layered on top of that.    Nevertheless PerfMonitor has powerful features.  It has  the abilty to turn on and off any ETW provider, and thus can act as a 'light weight' controler even if you decide to use tools like XPERF to actually analyze the data.     PerfMonitor also has better CLR support than XPERF currently has.   PerfMonitor can decode the full managed and unmanaged stacks that ETW kernel events provide, as well as do CLR specific analysis (like GC pause time and JIT compilation statstics).

PerfMonitor has a 'analyze' command that is designed to be a 'Quick Perf Health Check' for managed applications.   In one command you can run a program and get an anaysis of its CPU costs, as well as GC memory, and Just In Time compilation overhead.   

PerfMonitor has a 'monitor' command that is designed to make it easy to use the new System.Diagnositics.Eventing.EventSource class.   Running 'PerfMonitor monitorPrint EXEFILE' will turn on the event sources present in EXEFILE, run the EXEFILE, and then print the resulting loggint messages.   

Please see the [Perfmonitor Guide](Perfmonitor-Guide) some 'quick starts' on what you can do with the tool, if you are interested follow the download link below.  

Finally PerfMonitor was design to be 'easy to deploy'.  It is just a single EXE, which can be fetched from the [release:Download Page](99985).   It is XCOPY deployable (just copy the EXE out of the ZIP file). and you are ready to run it.   Despite what the 'System Requirements window says (which is the max for all code on this site)  PerfMonitor (and TraceEvent) only need [V3.5 of the .NET Framework](http://www.microsoft.com/downloads/details.aspx?FamilyId=333325FD-AE52-4E35-B531-508D977D32A6&displaylang=en) to be installed.   If you are running Windows Vista or above or have Windows Update running you should have this.  If PerfMonitor fails to load simply go to the link above and download it.   

PerfMonitor does need at least windows VISTA.  It will not work on XP or win2K3.  

### See Also 

	*  [release:Download Page](99985) to download
	*  [PerfMonitor Guide](PerfMonitor-Guide) for a quick look at what PerfMonitor can do before downloading.
	*  [TraceEvent](TraceEvent) and [TraceEvent Class Overview](TraceEvent-Class-Overview) which is the programatic interface that was used to implement PerfMonitor. 

### Having problems?

[Ask a question](http://bcl.codeplex.com/Thread/List.aspx)
[File a bug](http://bcl.codeplex.com/workitem/list/basic)