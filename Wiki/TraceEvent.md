## Superseded by the TraceEvent Nuget Package

**This respository has been superseded by the TraceEvent Nuget Package.**   

Please see [My getting Started Blog](http://blogs.msdn.com/b/vancem/archive/2014/03/15/walk-through-getting-started-with-etw-traceevent-nuget-samples-package.aspx)  or my [TraceEvent Blog Entries](http://blogs.msdn.com/b/vancem/archive/tags/traceevent/) for more information on getting started with the Nuget package.

The Nuget package does not have the source code associated with it.  However we are likely to provide a GIT repository for the source code in the near future. 

## TraceEvent
TraceEvent is an library that greatly simplifies reading [Event Tracing for Windows](http://msdn.microsoft.com/en-us/library/bb968803(VS.85).aspx) (ETW) events.   ETW is the power behind the [Windows Performance Analyzer](http://msdn.microsoft.com/en-us/performance/default.aspx) (also known as the XPerf Tool).    The Windows OS has events for just about anything that you could be interested in from a performance standpoint (CPU usage, Context switched Disk I/O DLL Loads, Blocking, all with stack traces).    In addition the .NET Runtime has events for garbage collection, Just In time Compilation, Assembly loading and more.    TraceEvent was built for people who understand the power of the data that XPERF lets you get at, but also needs the capability to programatically maniputate that data.    It is the foundation of truly powerful and flexible performance analysis on windows.     

### Getting started

First, see [TraceEvent Class Overview](TraceEvent-Class-Overview) to see if the functionality this download provides piques your interest.   

If it does, the best way to understand how TraceEvent can be used in 'real life' scenarios is by exploring a sample.  That is exactly what [PerfMonitor](PerfMonitor) is.   It is a simple console-based application that can collect ETW data as ETL files and display them in various ways as XML.    It is recommended that you simply download that application and learn see how it uses TraceEvent APIs to learn how to use it.   PerfMonitor includes the TraceEvent library as part of its download, so you get both a sample and the library in one download.    If you wish to just download TraceEvent without PerfMonitor you can do so by visiting the page.  

### See Also
	* [TraceEvent Class Overview](TraceEvent-Class-Overview) for more information on the classes that this download provides.  
	* [release:Download](99984) for downloading either the source or the binary distribution of TraceEvent.dll
	* [PerfMonitor](PerfMonitor) the simple command line ETW controller and printer based on TraceEvent. 

### Having problems?

	* [Ask a question](http://bcl.codeplex.com/Thread/List.aspx)
	* [File a bug](http://bcl.codeplex.com/workitem/list/basic)