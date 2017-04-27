## Overview

This is an library that greatly simplifies reading [Event Tracing for Windows](http://msdn.microsoft.com/en-us/library/bb968803(VS.85).aspx) (ETW) events.   ETW is the power behind the [Windows Performance Analyzer](http://msdn.microsoft.com/en-us/performance/default.aspx) (also known as the XPerf Tool).    The Windows OS has events for just about anything that you could be interested in from a performance standpoint (CPU usage, Context switched Disk I/O DLL Loads, Blocking, all with stack traces).    In addition the .NET Runtime has events for garbage collection, Just In time Compilation, Assembly loading and more.    It is very powerful.     See [TraceEvent documentation](TraceEvent) for more information on downloading this code.  

## Classes for Collecting Events 

* **TraceEventSession** – controls turning data collection on and off.   This is how you can tell windows to collect events.   The result is typically a Event Trace Log (ETL) file.   

## Classes for Analyzing Events

The library contains two sets of API.   A low-level APIs for parsing a raw event stream that simply knows how to parse the raw events.   Built on top of this is a higher level API that with more structure, including Processes, Threads, LoadedModules, and symbolic information.    It is more efficient to use the low level APIs, so that is appropriate for simple, targeted tools, however more powerful profilding tools will want to use the high level API.  

### Low-level APIs:

* **TraceEventSource** – represents a file containing ETW events.  This class, along with TraceEvent, know how to parse ETL files and understand the fields of an event that are common to all events (e.g. time, process ID, thread ID opcode, etc.), but do not understand the payload of specific events.
* **TraceEvent** – represents an individual event in a TraceEventSource stream.  

ETW supports a very extensible system of adding new events (3rd parties such as yourself can add new events).    Only the providers know the layout of the data they send in the events, so there needs to be a mechansim for describing the schema of the eventss to code that wishes to consume the events.   The ETW infrastructure encodes this scheme information as an [XML MANIFEST](http://msdn.microsoft.com/en-us/library/aa385201(v=VS.85).aspx).   Using a tool (Not yet up on CodePlex but soon) you use such a manifest to generate C# code that knows how to parse the binary events.   The result is TraceEventParser.   A TraceEventParser contains all the 'per Provider' information that is contained in the manifest.   The TraceEvent.dll comes with two (VERY USEFUL) parsers 

* **KernelTraceEventParser** –  Understands events and their payloads for [kernel events](http://msdn.microsoft.com/en-us/library/aa364083(v=VS.85).aspx).  (e.g. ProcessTraceData, ImageLoadTraceData, etc.)
* **ClrTraceEventParser** –  Understands the events and payloads for the [.NET Runtime (CLR) events](http://msdn.microsoft.com/en-us/library/dd264810(VS.100).aspx).  (e.g. GCStartTraceEvent, AllocationTickTraceEvent, etc.).

### High-level APIs:
* **TraceLog** – While the raw ETW events are valuable, most non-trivial analysis need more functionaly to be useful.  Things like symbolic names for addresses, various links between threads, threads, modules, and eventToStack traces are needed.  This is what TraceLog provides.   This is likely to be the class that most people use.

The high level API often deals with either additional information (eg information gathered from symbol (PDB) files), or dervied information (eg links between threads and the process that created them).   This information is not in the ETL file, and is expensive to generate.   Because of this a new file format (ETLX), was create that can contain all the information that was originally in the ETL file, as well as this additional information.   The **TraceLog** class is effectively the programatic interface to the ETLX file format.  

TraceLog is the entry point for a true object model for event data that are cross linked to each other as well as the raw events. Here are some of the players:

* **TraceLog** – represents the event log as a whole.  It holds ‘global’ things, like a list of TraceProcesses and the list of TraceModuleFiles
* **TraceProcesses** – represents a list of TraceProcess instances that can be looked up by PID + time.
* **TraceProcess** – represents a single process.
* **TraceThread** – represents a thread within a process.
* **TraceLoadedModules** – represents a list of TraceLoadedModule instance that can be looked up by address + time, or filename + time.
* **TraceLoadedModule** – represents a loaded DLL or EXE (it knows its image base and time loaded)
* **TraceModuleFile** – represents a DLL or EXE on the disk (it only contains information that is common to all threads that use it (e.g. its name).  In particular, it holds all the symbolic address to name mappings (extruded from PDBs).  New TraceModuleFiles are generated if a file is loaded in another location (either later in the same process or a different process).  Thus, the image base becomes an attribute of ModuleFile.
* **TraceCallStack** – represents a call stack associated with the event (on Vista+).  It is logically a list of code addresses (from callee to caller).
* **TraceCodeAddress** – represents instruction pointer into the code.  This can be decorated with symbolic information (e.g. methodIndex, source line, source file).
* **TraceMethod** – represents a particular method.  This class allows information that is common to many samples (its method name and source file) to be shared.

The result is a richly interconnected model of performance data.   