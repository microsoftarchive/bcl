// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using Utilities;
using System.Threading;
using System.Diagnostics.Eventing;
using System.Security;
using Diagnostics.Eventing;


// TODO this is all experimental...

public static class ColdStartup
{
    #region native Methods

    [DllImport("psapi.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
    [return: MarshalAs(UnmanagedType.Bool)]

    private static extern bool
    EmptyWorkingSet(IntPtr processHandle);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
    internal static extern IntPtr LoadLibraryW(string lpszLib);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
    static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [Flags]
    enum FileMapAccessType : uint
    {
        Copy = 0x01,
        Write = 0x02,
        Read = 0x04,
        AllAccess = 0x08,
        Execute = 0x20,
    }

    [Flags]
    enum PageProtection : uint
    {
        NoAccess = 0x01,
        Readonly = 0x02,
        ReadWrite = 0x04,
        WriteCopy = 0x08,
        Execute = 0x10,
        ExecuteRead = 0x20,
        ExecuteReadWrite = 0x40,
        ExecuteWriteCopy = 0x80,
        Guard = 0x100,
        NoCache = 0x200,
        WriteCombine = 0x400,
    }

    [DllImport("kernel32.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
    static extern SafeHandle CreateFileMapping(SafeHandle hFile,
       IntPtr lpSecurityAttributes, PageProtection flProtect, uint dwMaximumSizeHigh,
       uint dwMaximumSizeLow, string lpName);

    [DllImport("kernel32.dll"), SuppressUnmanagedCodeSecurityAttribute]
    static extern IntPtr MapViewOfFileEx(SafeHandle hFileMappingObject,
       FileMapAccessType dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow,
       UIntPtr dwNumberOfBytesToMap, IntPtr lpBaseAddress);

    [DllImport("kernel32.dll"), SuppressUnmanagedCodeSecurityAttribute]
    static extern bool UnmapViewOfFile(IntPtr address);

    #endregion

    public static void ReloadSystemDlls()
    {
        ReloadDll("kerenel32.dll");
        ReloadDll("Advapi32.dll");
    }

    public unsafe static void ReloadDll(string dllName)
    {
        IntPtr handle = LoadLibraryW(dllName);
        if (handle == null)
            throw new System.ComponentModel.Win32Exception("Error loading module " + dllName, new System.ComponentModel.Win32Exception());

        PEFile.PEHeader header = new PEFile.PEHeader(handle);
        byte* dllBase = (byte*)handle;
        for (uint i = 0; i < header.SizeOfCode; i += 4096)
            dummy += dllBase[i];
    }

    private volatile static byte dummy;
    unsafe public static void FlushFileSystemCache()
    {
        // Make a really big file. 
        string fileName = "test";
        FileStream file = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite);
        uint fileLength = 2U * 1024 * 1024 * 1024;
        file.SetLength(fileLength);

        SafeHandle mapping = CreateFileMapping(file.SafeFileHandle, IntPtr.Zero, PageProtection.Readonly, 0, fileLength, null);
        IntPtr basePtr = MapViewOfFileEx(mapping, FileMapAccessType.Read, 0, 0, UIntPtr.Zero, IntPtr.Zero);

        // Touch the file to bring it into memory.  
        byte* ptr = (byte*)basePtr;
        for (uint i = 0; i < fileLength; i += 1024)
            dummy = ptr[i];

        UnmapViewOfFile(basePtr);
        mapping.Close();
        file.Close();

#if false
        hHandle = OpenFileMapping(FILE_MAP_ALL_ACCESS, false, SharedMemoryName);
        pBuffer = MapViewOfFile(hHandle, FILE_MAP_ALL_ACCESS, 0, 0, &NumBytes);


        byte[] buffer = new byte[1024 * 256];
        long bytesRead = 0;
        for (; ; )
        {
            int count = file.Read(buffer, 0, buffer.Length);
            if (count == 0)
                break;
            bytesRead += count;
        }
        file.Close();
        File.Delete(fileName);
        Console.WriteLine("Read 0x" + bytesRead.ToString("x") + " bytes");
#endif
    }

}

public class StartupAnalysis
{
    StartupAnalysis(TraceEvents events)
    {
    }

}


class ProcessTasks
{
    [DllImport("advapi32.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
    internal extern static int ProcessIdleTasks();

    static void Main1()
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();
        ProcessIdleTasks();
        sw.Stop();
        Console.WriteLine("ProcessIdleTasks took " + sw.ElapsedMilliseconds + " milliseconds");
    }
}




namespace ResetPF
{
    class Program
    {
        static string m_appName;
        const int TIMESTAMP_OFFSET_XP = 120;
        const int TIMESTAMP_OFFSET_VISTA = 128;

        static System.Collections.IEnumerable OpenPrefetchFile()
        {
            string directorypath = Environment.GetEnvironmentVariable("windir") + "\\Prefetch";
            if (!Directory.Exists(directorypath))
            {
                Console.WriteLine("Prefetch folder could not be found. Tried {0}", directorypath);
                yield break;
            }

            string[] matches = Directory.GetFiles(directorypath, m_appName + "*");

            if (matches.Length == 0)
            {
                Console.WriteLine("No matching prefetch file found");
                yield break;
            }
            foreach (string s in matches)
            {

                Console.WriteLine("Found Prefetch file - {0}", s);
                FileStream fs = new FileStream(s, FileMode.Open, FileAccess.ReadWrite);

                yield return fs;
            }
        }

        static void Main2(string[] args)
        {
            if (args.Length == 0)
                throw new ArgumentException("No application name specified");

            m_appName = args[0];
            int offset = 0;
            bool OnlyDisplay = false;

            if (args.Length > 1)
                OnlyDisplay = true;

            OperatingSystem os = Environment.OSVersion;
            if (os.Version.Major == 5)
                offset = TIMESTAMP_OFFSET_XP;
            else if (os.Version.Major == 6)
                offset = TIMESTAMP_OFFSET_VISTA;
            else
            {
                throw new NotSupportedException("This tool will only work on XP or Vista. To make it work on newer OSes, find the offset of the timestamp inside the prefetch file header and update the test");
            }

            foreach (FileStream fs in OpenPrefetchFile())
            {
                using (fs)
                {
                    if (fs == null)
                    {
                        Console.WriteLine("No prefetch file found");
                        return;
                    }

                    fs.Position = offset;
                    byte[] buffer = new byte[8];
                    BinaryReader br = new BinaryReader(fs);
                    long timestamp = br.ReadInt64();

                    Console.WriteLine("Previous timestamp - {0}", timestamp);

                    if (OnlyDisplay)
                        Console.WriteLine("Not resetting timestamp");
                    else
                    {
                        fs.Position = offset;
                        fs.Write(buffer, 0, 8);

                        Console.WriteLine("Successfully reset timestamp");
                    }
                }
            }
        }
    }
}

#if false

// PurgeStandbyList
// Derived from pfpurge.c from Cenk Ergan, NT Performance
ULONG PurgeStandbyList ()
{
	volatile ULONG DummyValue = 0;
	const DWORD FileSize = 32 * 1024;	// All units are in KB, so this is 32 MB

	// Get system information to determine page size
	HMODULE hNtDll = LoadLibrary("ntdll.dll");
	if (!hNtDll)
	{
		throw Error("LoadLibrary on ntdll.dll failed", GetLastError(), __FILE__, __LINE__);
	}

	typedef int (*NQSI)(SYSTEM_INFORMATION_CLASS,PVOID,ULONG,PULONG);
	NQSI pfnNtQuerySystemInformation;
    pfnNtQuerySystemInformation = (NQSI) GetProcAddress(hNtDll, "NtQuerySystemInformation");
	if (!pfnNtQuerySystemInformation)
	{
		throw Error("Unable to get NtQuerySystemInformation proc address",GetLastError(),__FILE__,__LINE__);
	}

	SYSTEM_BASIC_INFORMATION BasicInfo;
	NTSTATUS Status = pfnNtQuerySystemInformation(SystemBasicInformation,
		&BasicInfo,
		sizeof(BasicInfo),
		NULL);
	if (!NT_SUCCESS(Status))
	{
		throw Error("NtQuerySystemInformation failed",Status,__FILE__,__LINE__);
	}

	DWORD FileCount = (BasicInfo.PageSize / 1024 * (ULONG)BasicInfo.NumberOfPhysicalPages)/FileSize + 1;
	FreeLibrary(hNtDll);

	char FileDir[MAX_PATH];
	if (!GetTempPath(MAX_PATH, FileDir))
	{
		throw Error("GetTempPath failed, error 0x%x", GetLastError(),__FILE__,__LINE__);
	}
	FileDir[MAX_PATH - 1] = 0;

	ObjectArray<FileHandle> FileHandles(FileCount);

	// Create and walk the files.
	for (ULONG FileIndex = 0; FileIndex < FileCount; FileIndex++)
	{
		// Cook up a name
		char FilePath[MAX_PATH];
		if (sprintf_s(FilePath, MAX_PATH-1, "%s\\slpurge_%d.tmp", FileDir, FileIndex) < 0)
		{
			throw Error("Temp pathname too long", __FILE__, __LINE__);
		}
		FilePath[MAX_PATH - 1] = 0;

		// Create the file.
		HANDLE fh = CreateFile(FilePath,
			GENERIC_READ | GENERIC_WRITE,
			FILE_SHARE_READ | FILE_SHARE_DELETE,
			NULL,
			CREATE_ALWAYS,
			FILE_FLAG_DELETE_ON_CLOSE,
			NULL);
		FileHandles[FileIndex] = fh;
        if (FileHandles[FileIndex] == INVALID_HANDLE_VALUE)
		{
			throw Error("CreateFile failed, error 0x%x", GetLastError(),__FILE__,__LINE__);
		}

		// Grow the file.
		LARGE_INTEGER DistanceToMove;
		DistanceToMove.QuadPart = FileSize * 1024;
		if (!SetFilePointerEx(FileHandles[FileIndex],
			DistanceToMove,
			NULL,
			FILE_BEGIN)) 
		{
			throw Error("Failed to set file pointer, error 0x%x", GetLastError(),__FILE__,__LINE__);
		}

		if (!SetEndOfFile(FileHandles[FileIndex]))
		{
			throw Error("Failed to extend file, error 0x%x", GetLastError(),__FILE__,__LINE__);
		}

		// Create mapping so we can walk the file
		Handle FileMapping (CreateFileMapping(FileHandles[FileIndex],
			NULL,
			PAGE_READONLY,
			0,
			0,
			NULL) );
		if (!FileMapping)
		{
			throw Error("Unable to create file mapping, error 0x%x", GetLastError(),__FILE__,__LINE__);
		}

		// Map the whole file.
		FileView fv = (PCHAR) MapViewOfFile(FileMapping,
			FILE_MAP_READ,
			0,
			0,
			0);
		if (!fv)
		{
			throw Error("Unable to map view of file, error 0x%x", GetLastError(),__FILE__,__LINE__);
		}

		// Walk and touch all pages.
		for (PCHAR Page = fv; 
			Page < fv + FileSize * 1024; 
			Page += BasicInfo.PageSize)
		{
			DummyValue += *(ULONG *)Page;
		}
    
	}	// End of loop over files
    return DummyValue;
}

#endif

public class Program
{
    public class Qualifiers
    {
        //
        // QUALIFIERS FOR ANALYZING RUNS 
        public string analyze;          // don't run program just anlalyze an existing etl file
        public string exeName;          // The name of the exe to analyze. 
        public bool dllInfo;            // details on dlls
        public bool readInfo;           // details on reads
        public bool blockedInfo;        // details on blocked 
        public bool afterPrefetch;        // Show stats after prefetching is complete
        // The verbosity level (Basic,DllInfo,ReadInfo)
        //
        // QUALIFIERS FOR RUNING SCENARIOS 
        public bool vista;              // args for vista (noDefrag, noServiceStop, assume superfetch)
        public bool noDefrag;           // don't defrag the disk after priming prefetch 
        public bool noFlush;              // don't flush file system before cold start test
        public bool noPrefetchPrime;    // don't delete and recreate the prefetch cache data
        public bool noServiceStop;      // don't bother stopping services
        public string outName;          // output name, if directory, name of exe is prepended.
        public string mainWindowName;   // main window name, used for overriding default wincmd behavior
        public bool warm;               // do warm startup (default is cold)
        public bool both;               // do warm and cold startup
        public bool noPrefetch;         // remove prefetch data
        public int count = 1;           // repeat count times. 
        public int maxStartup = 25;        // second to wait for startup to complete
        //
        // SPECIALIZED QUALIFIERS
        public bool debug;              // print advanced diagnostic information
    }
    static public Qualifiers quals;

    /// <summary>
    /// Measure warm or cold startup.  By default the 'args' qualifer
    /// is the command line that should be run,collecting startup information.
    /// 
    /// The script does the necessary prep work to get a good run, turns
    /// on ETW tracing and takes the run.  It then post-processes the
    /// data to find the startup time.
    /// 
    /// Using the /analyze qualifier you can get detailed information on
    /// the disk usage and page faults that happened during the run, broken
    /// down by DLL if requested. 
    /// </summary>
    public static void Main1(Qualifiers quals, string[] args)
    {
        Program.quals = quals;
        if (!String.IsNullOrEmpty(quals.analyze))
        {
            Console.WriteLine("Analyzing " + quals.analyze);
            if (quals.exeName == null)
            {
                quals.exeName = Path.GetFileName(quals.analyze);
                int firstDot = quals.exeName.IndexOf('.');
                if (firstDot > 0)
                    quals.exeName = quals.exeName.Substring(0, firstDot);
                Console.WriteLine("Using " + quals.exeName + " as the process name pattern");
            }
            ProcessTrace trace = new ProcessTrace(ProcessTrace.GetCsvFile(quals.analyze), quals.exeName);

            string outName = Path.ChangeExtension(quals.analyze, ".trace.csv");
            Console.WriteLine("Writing processed file " + outName);
            trace.WriteCsvFile(outName);

            Console.WriteLine();
            Console.WriteLine("Statistics from Process Start to Idle 0 to {0:f0}ms", (trace.IdleUsec - trace.ProcessStartUsec) / 1000.0);
            AnalysisInterval startup = new AnalysisInterval(trace, trace.ProcessStartUsec, trace.IdleUsec);
            startup.PrintSummaryStats(GetVerbosity(quals));

            if (quals.afterPrefetch)
            {
                Console.WriteLine();
                Console.WriteLine("*********************************************************************");
                Console.WriteLine("Statistics from Kernel32 load (after Prefetch) {0:f0}ms onward ", (trace.Kernel32LoadUsec - trace.ProcessStartUsec) / 1000.0);
                AnalysisInterval afterPrefetch = new AnalysisInterval(trace, trace.Kernel32LoadUsec, trace.IdleUsec);
                afterPrefetch.PrintSummaryStats(GetVerbosity(quals));
            }
            return;
        }

        if (args.Length == 0)
            throw new Exception("Error: must specify command line of scenario to run");
        string exe = args[0];
        string commandLine = CommandLineUtilities.FormCommandLineFromArguments(args, 0);
        try
        {
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { startServices(); Environment.Exit(1); };
            stopServices();

            if (quals.outName == null)
                quals.outName = Path.GetFileName(exe);
            else if (Directory.Exists(quals.outName))
                quals.outName = Path.Combine(quals.outName, Path.GetFileName(exe));

            if (quals.both)
                quals.warm = false;

            // Command.Run("cmd /c xperfInfo -StopAll");
            string outNameBase = Path.ChangeExtension(quals.outName, null);
            int i = 0;
            Stats stats = new Stats(quals.count, delegate()
            {
                i++;
                Console.WriteLine("******* Iteration " + i);
                return StartupTime(outNameBase + "." + i.ToString(), exe, commandLine, quals);
            });

            string kind = quals.warm ? "warm" : "cold";
            Console.WriteLine("Summary Process Duration (" + kind + ") for " + exe + " " + String.Join(" ", args));
            Console.WriteLine("    " + stats.ToString());

            if (quals.both)
            {
                quals.both = false;
                quals.warm = true;
                Main1(quals, args);
            }
        }
        finally
        {
            startServices();
        }
    }

    /// <summary>
    /// Runs a scenaro (exe) and returns the sartup time in msec.
    /// It also runs xperfinfo while the run active and generates .etl file, 
    /// a .csv file (the text dump of the ETL file), and a .trace.csv file
    /// (a massaged version that only contains the events for the process 
    /// being measured, and addition annotations. 
    /// </summary>
    /// <param name="outputBase">The name to use to create output files (.etl, .csv, .trace.csv)</param>
    /// <param name="exe">The executable program to run</param>
    /// <param name="argsString">Aguments to pass to the program</param>
    /// <param name="quals">command line qualifiers (/warm, /noprefetch)</param>
    /// <returns>startup time (in msec) </returns>
    public static int StartupTime(string outputBase, string exe, string commandLine, Qualifiers quals)
    {
        string etlName = outputBase + (quals.warm ? ".warm.etl" : ".etl");
        string exeFileName = Path.GetFileNameWithoutExtension(exe) + ".exe";
        CommandOptions scenarioOptions = new CommandOptions().
            AddEnvironmentVariable("COMPLUS_Version", null).
            AddEnvironmentVariable("COMPLUS_InstallRoot", null);

        if (quals.warm)
        {
            Console.WriteLine("Priming disk buffers for warm startup");
            RunScenario(exe, commandLine, quals.mainWindowName, scenarioOptions);
        }
        else
        {
            if (quals.noPrefetch)
            {
                foreach (string prefetchFile in Directory.GetFiles(Environment.GetEnvironmentVariable("WINDIR") + @"\Prefetch", exeFileName + "*"))
                {
                    Console.WriteLine("Deleting prefetch file " + prefetchFile);
                    File.Delete(prefetchFile);
                }
            }
            else
            {
                if (!quals.noPrefetchPrime)
                {
                    Command cmd;
                    if (quals.vista)
                    {
                        Console.WriteLine("Starting Superfetch");
                        cmd = Command.Run("net start sysMain", new CommandOptions().AddNoThrow());
                    }

                    string prefetchDir = Environment.GetEnvironmentVariable("SystemRoot") + @"\Prefetch";
                    foreach (string prefetchFile in Directory.GetFiles(prefetchDir, Path.GetFileName(exe) + "*"))
                    {
                        Console.WriteLine("Deleting prefetch file " + prefetchFile);
                        FileUtilities.ForceDelete(prefetchFile);
                    }

                    Console.WriteLine("Running the scenario to prime prefetch data.");
                    RunScenario(exe, commandLine, quals.mainWindowName, scenarioOptions);

                    for (int i = 0; Directory.GetFiles(prefetchDir, Path.GetFileName(exe) + "*").Length == 0; i++)
                    {
                        System.Threading.Thread.Sleep(100);
                        if (i > 500)
                            throw new Exception("New prefetch data not created");
                    }

                    if (!quals.noDefrag && !quals.vista)
                    {
                        // TODO use rundll32.exe advapi32.dll,ProcessIdleTasks
                        // Defrag -b does not seem to work properly
                        Console.WriteLine("Insuring optimized disk layout includes prefetch data...");
                        cmd = Command.Run("defrag -b " + Environment.GetEnvironmentVariable("SystemDrive"), new CommandOptions().AddNoThrow());
                        if (cmd.ExitCode != 0)
                            Console.WriteLine("WARNING defrag failed exit code " + cmd.ExitCode + " startup may be affected");
                    }

                    if (quals.vista)
                    {
                        Console.WriteLine("Stopping Superfetch");
                        cmd = Command.Run("net stop sysMain", new CommandOptions().AddNoThrow());
                    }
                }
            }
            if (!quals.noFlush)
            {
                Console.WriteLine("Flushing disk buffers to simulate cold startup...");
                Command.Run("ready /t5");
            }
        }

        Command.Run("cmd /c xperfInfo -on base+ALL_FAULTS+CSWITCH -f \"" + etlName + "\"");
        // RunScenario("sleep.exe", "sleep 1", scenarioOptions);  // run something so that Run.exe code paths for waiting get hit.
        GC.Collect();                           // page in GC (timed based GC might go off).  
        GC.WaitForPendingFinalizers();          // Make certain the finalizer thread does not kick in.
        System.Threading.Thread.Sleep(2000);    // Make some space in the xperf trace (TODO how much?)
        Console.WriteLine("Running scenario");
        RunScenario(exe, commandLine, quals.mainWindowName, scenarioOptions);
        System.Threading.Thread.Sleep(2000);
        Command.Run("cmd /c xperfInfo -stop");

        ProcessTrace trace = new ProcessTrace(ProcessTrace.GetCsvFile(etlName), exeFileName);
        AnalysisInterval startup = new AnalysisInterval(trace, trace.ProcessStartUsec, trace.IdleUsec);
        Console.WriteLine("Statistics from Process Start to Idle 0 to {0:f0}ms", (trace.IdleUsec - trace.ProcessStartUsec) / 1000.0);
        startup.PrintSummaryStats(GetVerbosity(quals));

        return (trace.IdleUsec - trace.ProcessStartUsec) / 1000;
    }

    /// <summary>
    /// A helper that knows how to run a a program for a set amount 
    /// </summary>
    private static void RunScenario(string exe, string commandLine, string windowName, CommandOptions scenarioOptions)
    {
        Console.WriteLine("Running: " + commandLine);
        CommandHelpers.Run(commandLine, Path.GetFileName(exe), windowName, 0x10, -1, true, false);
        // CommandHelpers.Run(exe + " " + args, exe, 0x10, quals.maxStartup, false, false); 

        /***
                Command cmd = new Command(exe + " " + args, scenarioOptions.Clone().AddOutputStream(Console.Out));

                cmd.Process.WaitForExit(quals.maxStartup * 1000);
                if (!cmd.HasExited)
                {
                    Console.WriteLine("Waited " + quals.maxStartup + " seconds, killing");
                    CommandHelpers.SignalWinformsToExit(exe, 0x10, false); 
            
                    cmd.Kill();
                }
        ***/
    }

    private static AnalysisIntervalFlags GetVerbosity(Qualifiers quals)
    {
        AnalysisIntervalFlags verbosity = AnalysisIntervalFlags.Basic;
        if (quals.blockedInfo)
            verbosity |= AnalysisIntervalFlags.BlockedInfo;
        if (quals.readInfo)
            verbosity |= AnalysisIntervalFlags.ReadInfo;
        if (quals.dllInfo)
            verbosity |= AnalysisIntervalFlags.DllInfo;

        return verbosity;
    }

    // Stop services that get in the way and purturb results, that shouldn't be turned off for long
    // periods of time
    private static void stopServices()
    {
        if (quals.noServiceStop || quals.vista)
            return;

        // TODO: work better when serices are not present, or are already off
        Console.WriteLine("Stopping Etrust anti-virus and System Managment Service (they interfere)");
        Command.Run("net stop InoTask", new CommandOptions().AddNoThrow());
        Command.Run("net stop InoRT", new CommandOptions().AddNoThrow());
        Command.Run("net stop ccmExec", new CommandOptions().AddNoThrow());
        Command.Run("taskkill /f /im ctfmon.exe", new CommandOptions().AddNoThrow());        // A service started by office for speech, handwriting ... 

        // Command.Run("net stop MSSQL$SQLEXPRESS", new CommandOptions().AddNoThrow());
        // Command.Run("net stop cftmon", new CommandOptions().AddNoThrow());
    }
    private static void startServices()
    {
        if (quals.noServiceStop || quals.vista)
            return;
        Console.WriteLine("Starting Etrust and System Management Service");
        Command.Run("net start InoTask", new CommandOptions().AddNoThrow());
        Command.Run("net start InoRT", new CommandOptions().AddNoThrow());
        Command.Run("net start ccmExec", new CommandOptions().AddNoThrow());
    }
};

// TODO current we dont use these Running Commands. 
#region Helpers for Running Commands

static class Output
{
    static int stepNum = 1;
    public static void WriteStep(string str)
    {
        Console.WriteLine();
        Console.WriteLine(stepNum.ToString() + ") " + str);
        stepNum++;
    }

    public static void WriteInformation(string str)
    {
        Console.WriteLine("\t" + str);
    }
}

static class CommandHelpers
{
    public static void WaitTillIdle(string exeName, int idleTime /*seconds*/)
    {
        try
        {
            // give the process a chance to start
            Thread.Sleep(500);
            string processName = Path.ChangeExtension(exeName, null);
            Process[] procs = Process.GetProcessesByName(processName);
            if (procs.Length == 0)
                return;
            Process p = procs[0];

            const int sleepPeriod = 200;
            int minIdleCount = (idleTime * 1000) / sleepPeriod;

            int count = 0;
            long totalTime = p.TotalProcessorTime.Ticks;
            while (true)
            {
                long currentTime = p.TotalProcessorTime.Ticks;
                if (currentTime == totalTime)
                {
                    count++;

                    if (count > minIdleCount)
                    {
                        // it was idle for the minimum time needed
                        return;
                    }
                }
                else
                {
                    count = 0;
                }
                totalTime = currentTime;
                Thread.Sleep(sleepPeriod);
            }
        }
        catch (Exception e)
        {
            Output.WriteInformation("Exception thrown while waiting for process to exit: " + e.Message);
        }
    }

    public static void SignalWinformsToExit(string exeName, string windowName, int wm, bool privateDesktop)
    {
        // TODO: think about using /X 1 here to restrict the WM to only a single thing, in PDN for 
        //       instance after using wincmd.exe to shut it down all of the little toolbar windows will
        //       be closed because they remember their state, however that could result in bad training
        //       data.

        Output.WriteInformation("Sending window message (0x" + wm.ToString("x") + ") to application: " + exeName);
        string wincmdCmdLine = "wincmd.exe /p" + exeName + " /m0x" + wm.ToString("x");
        if (windowName != null)
        {
            wincmdCmdLine += " /W\"" + windowName + "\"";
        }
        if (privateDesktop)
        {
            wincmdCmdLine = "privatedesktop /n " + wincmdCmdLine;
        }
        Command wincmd = Command.Run(wincmdCmdLine, new CommandOptions().AddNoThrow());
        if (wincmd.ExitCode != 0)
        {
            Output.WriteInformation("wincmd failed with exit code: 0x" + wincmd.ExitCode.ToString("x"));
        }
    }

    // if wm == -1 this will run normally, if wm != -1 this will use wincmd to close the application
    public static string RunToIdle(string commandLine, string exeName, string windowName, int wm, bool privateDesktop)
    {
        int idleTime = 5;
        Output.WriteInformation("Running: '" + commandLine + "' and waiting until it is idle for " + idleTime + " seconds before sending window message");
        Command cmd = new Command(commandLine, new CommandOptions().
        AddNoThrow().AddTimeout(Int32.MaxValue).
        AddEnvironmentVariable("COMPLUS_Version", null).
            AddEnvironmentVariable("COMPLUS_InstallRoot", null));

        Output.WriteInformation("Waiting for process to idle for " + idleTime + " seconds");
        WaitTillIdle(exeName, idleTime);

        if (cmd.HasExited)
        {
            return cmd.Output;
        }

        if (wm != -1)
        {
            SignalWinformsToExit(exeName, windowName, wm, privateDesktop);
        }
        else
        {
            cmd.Kill();
        }

        return cmd.Wait().Output;
    }

    public static string RunToTimeout(string commandLine, string exeName, string windowName, int wm, int timeout, bool privateDesktop)
    {
        Output.WriteInformation("Running: '" + commandLine + "' with timeout");
        Command cmd = new Command(commandLine, new CommandOptions().
        AddNoThrow().AddTimeout(Int32.MaxValue).
        AddEnvironmentVariable("COMPLUS_Version", null).
            AddEnvironmentVariable("COMPLUS_InstallRoot", null));

        Output.WriteInformation("Waiting for timeout: " + timeout + " seconds");
        for (int i = 0; i < timeout; i++)
        {
            if (cmd.HasExited)
                break;
            System.Threading.Thread.Sleep(1000);
        }

        if (cmd.HasExited)
        {
            return cmd.Output;
        }

        if (wm != -1)
        {
            SignalWinformsToExit(exeName, windowName, wm, privateDesktop);
        }
        else
        {
            cmd.Kill();
        }

        return cmd.Wait().Output;
    }

    public static string Run(string commandLine, string exeName, string windowName, int wm, int timeout, bool toIdle, bool privateDesktop)
    {
        if (privateDesktop)
        {
            Output.WriteInformation("Running on private desktop");
            commandLine = "privatedesktop /n " + commandLine;
        }

        if (toIdle)
        {
            return RunToIdle(commandLine, exeName, windowName, wm, privateDesktop);
        }
        else if (timeout != -1)
        {
            return RunToTimeout(commandLine, exeName, windowName, wm, timeout, privateDesktop);
        }
        else
        {
            Output.WriteInformation("Running: '" + commandLine + "'");
            return Command.Run(commandLine).Output;
        }
    }

    static int pulseCounter = 1;
    static void PulseCallback(object state)
    {
        int heartBeatTime = (int)state;
        Output.WriteInformation("Still running... (" + ((pulseCounter++) * heartBeatTime) + " seconds)");
    }

    public static string RunWithPulse(string commandLine, int heartBeatTime)
    {
        Command cmd = null;
        pulseCounter = 1;
        using (Timer t = new Timer(PulseCallback, heartBeatTime, heartBeatTime * 1000, heartBeatTime * 1000))
        {
            cmd = Command.Run(commandLine, new CommandOptions().AddNoThrow().AddTimeout(Int32.MaxValue));
        }
        return cmd.Output;
    }
}

#endregion

#region Simple statistics logic

public delegate int Sampler();
public class Stats
{
    public Stats(int count, Sampler sample)
    {
        data = new List<int>();
        min = Int32.MaxValue;
        max = Int32.MinValue;
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            int dataPoint = sample();
            data.Add(dataPoint);
            if (dataPoint < min)
                min = dataPoint;
            if (dataPoint > max)
                max = dataPoint;
            total += dataPoint;
        }

        if (count > 0)
        {
            data.Sort();
            if (count % 2 == 1)
                median = data[count / 2];
            else
                median = (data[(count / 2) - 1] + data[count / 2]) / 2;
            average = total / count;
        }
    }

    public override string ToString()
    {
        return "Count: " + data.Count + " Min: " + min + " Median: " + median + " Mean: " + average + " Max: " + max;
    }

    public List<int> data;
    public int min;
    public int max;
    public int median;
    public int average;
}

#endregion // simple statistics logic

#region ETW file (.etl) processing logic

public class ProcessTrace
{
    /// <summary>
    /// Process a xperfInfo comma separated value file and generate 
    /// a ProcessTrace structure internally which holds the parsed values
    /// for a single process which matches 'processName'.  
    /// </summary>
    /// <param name="csvFile">The xperfinfo data file</param>
    /// <param name="processName">
    /// Asubstring matching the file name (not the path) of the image.
    /// The substring test is case-insensitive.  </param>
    public ProcessTrace(string csvFile, string processPattern)
    {
        // Process the CSV file.  
        char[] comma = new char[] { ',' };
        StreamReader csvFileHandle = File.OpenText(csvFile);
        bool isStarted = false;
        int lastSampleUsec = Int32.MinValue;
        int lastSampleIdx = 0;
        int lastEventTime = 0;

        if (processPattern.Length > 15)        // XperfInfo truncates the name
            processPattern = processPattern.Substring(0, 15);
        while (!csvFileHandle.EndOfStream)
        {
            string line = csvFileHandle.ReadLine();
            string[] fields = line.Split(comma);
            if (fields.Length < 2)
                continue;
            int timeUsec;
            if (!Int32.TryParse(fields[1], out timeUsec))
                continue;
            lastEventTime = timeUsec;

            string eventType = fields[0].Trim();

            if (!isStarted && eventType != "P-Start")
                continue;

            string processExe = "";
            int processId = 0;
            int threadId = 0;
            if (fields.Length > 2)
            {
                GetPidAndName(fields[2], out processExe, out processId);
                if (fields.Length > 3)                // TODO Fix when ThreadId parsing fails
                    Int32.TryParse(fields[3], out threadId);
            }

            switch (eventType)
            {
                case "P-Start":
                    {
                        // int timeUsec = Int32.Parse(fields[1]);
                        // string exeName = fields[2];
                        // int parentPID = Int32.Parse(fields[3]);
                        if (String.IsNullOrEmpty(processPattern))
                            processPattern = processExe;
                        if (processExe.IndexOf(processPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (isStarted)
                                Console.WriteLine("WARNING: detected multiple starts for " + processPattern);
                            else
                            {
                                ProcessStartUsec = timeUsec;
                                ProcessExe = processExe;
                                ProcessId = processId;
                                Console.WriteLine("Process {0} PID {1} started at {2} msec", ProcessExe, ProcessId, ProcessStartUsec / 1000);
                            }
                            isStarted = true;
                        }
                    } break;
                case "P-End":
                    {
                        if (processId == ProcessId)
                        {
                            ProcessEndUsec = timeUsec;
                            Console.WriteLine("Process {0} PID {1} ended at {2} msec delta {3} msec ", ProcessExe, ProcessId,
                                 (ProcessEndUsec / 1000), (ProcessEndUsec - ProcessStartUsec) / 1000);
                            goto done;
                        }
                        // TODO put in Event list.
                    } break;
                case "I-Start":
                    {
                        // int timeUsec = Int32.Parse(fields[1]);
                        // string exeName = fields[2];
                        long imageBase = ToHexLong(fields[3]);
                        long imageEnd = ToHexLong(fields[4]);
                        string fileName = StripQuotes(fields[8]);

                        if (processId == ProcessId && fileName.EndsWith(@"\kernel32.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            Kernel32LoadUsec = timeUsec;
                            Console.WriteLine("Kernel32 load (after prefetch) at {0} msec delta {1} msec",
                                (Kernel32LoadUsec / 1000), (timeUsec - ProcessStartUsec) / 1000);
                        }
                        int imageSize = (int)(imageEnd - imageBase);
                        loadedImages.Add(new LoadedImage(fileName, imageBase, imageSize));
                        Events.Add(new ImageLoadEvent(timeUsec, processId, processExe, imageBase, imageSize, fileName));
                    } break;

                case "DiskWrite":
                case "DiskRead":
                    {
                        // int timeUsec = Int32.Parse(fields[1]);
                        // string exeName = fields[2];
                        // int threadId = Int32.Parse(fields[3]);
                        long offset = ToHexLong(fields[5]);
                        int size = ToHexInt(fields[6]);
                        int elapsedUsec = Int32.Parse(fields[7]);
                        int diskNum = Int32.Parse(fields[8]);
                        int diskUsec = Int32.Parse(fields[10]);
                        string fileName = StripQuotes(fields[14]);

                        // Insert the disk start event in the right place.  
                        InsertEvent(new DiskIOStartEvent(timeUsec - elapsedUsec, processId, processExe, threadId, eventType == "DiskRead", diskNum));
                        // Add the end normal 'end of I/O event too
                        Events.Add(new DiskIOEndEvent(timeUsec, processId, processExe, threadId, eventType == "DiskRead", diskNum, offset, size, diskUsec, elapsedUsec, fileName));
                    } break;
                case "SampledProfile":
                    {
                        // int timeUsec = Int32.Parse(fields[1]);
                        // string exeName = fields[2];
                        // int threadId = Int32.Parse(fields[3]);
                        int instructionPoitner = ToHexInt(fields[4]);
                        // int cpu = Int32.Parse(fields[5]);
                        // string threadStart = fields[6];
                        string imageFunction = fields[7].Trim();
                        // int count = Int32.Parse(fields[8]);
                        // string type = fields[9];

                        // Throw away idle samples
                        if (processExe == "Idle")
                            continue;

                        // To make it easy to track CPUs on a multproc, insure that
                        // the samples from different CPUS all look like they are taken at the same time 
                        // and no events can come between the samples 
                        Event newEvent = new SampleEvent(timeUsec, processId, processExe, threadId, instructionPoitner, imageFunction);
                        if (timeUsec < lastSampleUsec + 10)
                        {
                            timeUsec = lastSampleUsec;
                            Events.Insert(lastSampleIdx, newEvent);
                        }
                        else
                            Events.Add(newEvent);
                        lastSampleIdx = Events.Count;
                        lastSampleUsec = timeUsec;
                    } break;
                case "HardFault":
                    {
                        // int timeUsec = Int32.Parse(fields[1]);
                        // string exeName = fields[2];
                        // int threadId = Int32.Parse(fields[3]);
                        // int virtualAddr = ToHexInt(fields[4]);
                        // long byteOffset = ToHexLong(fields[5]);
                        // int size = ToHexInt(fields[6]);
                        // int elapsedTimeUsec = Int32.Parse(fields[7]);
                        // int fileObject = ToHexInt(fields[8]);
                        // string fileName = StripQuotes(fields[9]);
                        // string info = fields[10];

                        // Currently ignore since hard faults also cause PageFault events.  
                    } break;
                case "PageFault":
                    {
                        // int timeUsec = Int32.Parse(fields[1]);
                        // string exeName = fields[2];
                        // int threadId = Int32.Parse(fields[3]);
                        int virtualAddress = ToHexInt(fields[4]);
                        int instructionPointer = ToHexInt(fields[5]);
                        string type = fields[6].Trim();
                        //string imageFunction = fields[7];
                        Events.Add(new PageFaultEvent(timeUsec, processId, processExe, threadId, virtualAddress, instructionPointer, type, GetImageNameForVirtualAddress(virtualAddress)));
                    } break;
                case "CSwitch":
                    {
                        //CSwitch,  TimeStamp, New Process Name ( PID),    New TID, NPri, NQnt, TmSinceLast, WaitTime, Old Process Name ( PID),    Old TID, OPri, OQnt,        OldState,      Wait Reason, Swapable, InSwitchTime, CPU, IdealProc
                        // int timeUsec = Int32.Parse(fields[1]);
                        // string exeName = fields[2];
                        // int threadId = Int32.Parse(fields[3]);
                        int fromProc;
                        string fromProcName;
                        GetPidAndName(fields[8], out fromProcName, out fromProc);
                        int fromThread = Int32.Parse(fields[9]);
                        string fromState = fields[12];
                        string reason = fields[13];
                        Events.Add(new ContextSwitchEvent(timeUsec, processId, processExe, threadId, fromProc, fromProcName, fromThread, fromState, reason));
                        HasContextSwitches = true;
                    } break;
                case "T-Start":
                case "T-End":
                case "I-End":
                case "I-DCStart":
                case "I-DCEnd":
                case "T-DCStart":
                case "T-DCEnd":
                case "P-DCStart":
                case "P-DCEnd":
                case "DiskFlushInit":
                case "DiskReadInit":
                case "DiskWriteInit":
                case "UnknownEvent/Classic":
                case "ImageId":
                case "DiskFlush":
                case "FileNameCreate":
                case "FileNameDelete":
                case "Mark":
                    break;
                default:
                    Console.WriteLine("Warning Unknown type " + eventType);
                    break;
            }
        }
    done:
        if (!isStarted)
            throw new Exception("Error Could not find process start in log");

        if (ProcessEndUsec == 0)
        {
            Console.WriteLine("Process {0} did not end using {1:f1} msec as end point", ProcessExe, lastEventTime / 1000.0);
            ProcessEndUsec = lastEventTime;
        }

        /**
        ComputeCPURanges(ProcessId);
        ComputeBlockedRanges(ProcessId);
        **/

        foreach (int processId in ProcessesActiveInRange(0, Int32.MaxValue))
        {
            ComputeCPURanges(processId);
            ComputeBlockedRanges(processId);
        }
        IdleUsec = ComputeIdleUsec();
    }

    private void GetPidAndName(string procNameAndPid, out string procName, out int procId)
    {
        procId = 0;
        procName = "";

        int paren = procNameAndPid.IndexOf('(');
        if (paren < 0)
            return;
        int afterParen = paren + 1;

        int endParen = procNameAndPid.IndexOf(')', afterParen);
        if (endParen < 0)
            return;

        string justNum = procNameAndPid.Substring(afterParen, endParen - afterParen);

        procName = procNameAndPid.Substring(0, paren).Trim();
        if (Int32.TryParse(justNum, out procId))
            ProcessExes[procId] = procName;
    }

    /// <summary>
    /// </summary>
    /// <param name="fileName">The name of the output file</param>
    public static string GetCsvFile(string inputFile)
    {
        string extention = Path.GetExtension(inputFile);
        string csvFile = inputFile;
        if (String.Compare(extention, ".etl", StringComparison.OrdinalIgnoreCase) == 0)
        {
            csvFile = Path.ChangeExtension(inputFile, ".csv");
            Console.WriteLine("Creating " + csvFile);
            Command.Run("cmd /c xperfinfo -i \"" + inputFile + "\"", new CommandOptions().AddOutputFile(csvFile));
            if (!File.Exists(csvFile))
                throw new Exception("Error generating " + csvFile + " from " + inputFile);
        }
        return csvFile;
    }

    /// <summary>
    /// Write out the process trace as a CSV file.  This is different from the input CSV file
    /// because it only contains information about the process, it removes fields that are
    /// not useful, and addes regions that make interpreting the file easier.  It is intended 
    /// that it be useful to launch into excel usefully.  
    /// </summary>
    /// <param name="fileName">The name of the output file</param>
    public void WriteCsvFile(string fileName)
    {
        StreamWriter output = File.CreateText(fileName);
        output.WriteLine("EventType,      EventMsec, ProcessExe,      Pid,  Tid, Data ...");
        foreach (Event e in Events)
        {
            WriteCsv(e, output);
        }
        output.Close();
    }

    public int FindIndexForTime(int timeUsec)
    {
        // TODO do a binary search
        for (int i = 0; i < Events.Count; i++)
        {
            if (timeUsec <= Events[i].TimeUsec)
                return i;
        }
        return Events.Count;        // an illegal index.  Could not find. 
    }

    public List<int> ProcessesActiveInRange(int startUsec, int stopUsec)
    {
        List<int> processIds = new List<int>();
        for (int i = FindIndexForTime(startUsec); i < Events.Count; i++)
        {
            Event e = Events[i];
            if (e.TimeUsec >= stopUsec)
                break;
            if (e.ProcessId != 0 && !processIds.Contains(e.ProcessId))
                processIds.Add(e.ProcessId);
        }
        return processIds;
    }

    const int ExpectedEventCount = 50000;
    public List<Event> Events = new List<Event>(ExpectedEventCount);
    public Dictionary<int, string> ProcessExes = new Dictionary<int, string>();
    public string ProcessExe;
    public int ProcessId;
    public int ProcessStartUsec;
    public int ProcessEndUsec;
    public int Kernel32LoadUsec;        // when Kernel32 loads (transition to warm time)
    public int IdleUsec;                // The time the process goes idle 
    public bool HasContextSwitches;     // Does the trace have context switch information?

    /// <summary>
    /// Insert the event in its sorted position (since e may have a time that is 
    /// not the latest
    /// </summary>
    /// <param name="e"></param>
    private int InsertEvent(Event e)
    {
        int idx;
        for (idx = Events.Count - 1; idx >= 0; --idx)
        {
            if (Events[idx].TimeUsec <= e.TimeUsec)
                break;
        }
        idx++;
        Events.Insert(idx, e);
        return idx;
    }
    private static void WriteCsv(Event e, StreamWriter output)
    {

        double eventMsec = e.TimeUsec / 1000.0;
        output.Write("{0,-14} {1,10:f3}, {2,-15} {3,4}, {4,4}",
            Enum.GetName(typeof(EventEnum), e.Kind) + ",",
            eventMsec,
            e.ProcessExe + ",",
            e.ProcessId,
            e.ThreadId);
        switch (e.Kind)
        {
            case EventEnum.DiskReadEnd:
            case EventEnum.DiskWriteEnd:
                output.WriteLine(", {0,18}, {1,10}, {2,10:f3}, {3,8:f3}, \"{4}\"",
                    PrintHexLong(e.DiskIOEnd.Offset),
                    PrintHexInt(e.DiskIOEnd.Size),
                    e.DiskIOEnd.DiskUsec / 1000.0,
                    e.DiskIOEnd.ElapsedUsec / 1000.0,
                    e.DiskIOEnd.FileName);
                break;
            case EventEnum.ImageLoad:
                output.WriteLine(", {0,18}, {1,10}, \"{2}\"",
                    PrintHexLong(e.ImageLoad.ImageBase),
                    PrintHexInt(e.ImageLoad.Size),
                    e.ImageLoad.FileName);
                break;
            case EventEnum.PageFault:
                output.WriteLine(", {0,18}, {1,10}, {2,10},,,,,,, \"{3}\"",
                    PrintHexLong(e.PageFault.VirtualAddress),
                    PrintHexLong(e.PageFault.InstructionPointer),
                    e.PageFault.Type,
                    e.PageFault.FileName);
                break;
            case EventEnum.CPUEnd:
                output.WriteLine(", {0,8:f3}", e.CPU.DurationUsec / 1000.0);
                break;
            case EventEnum.BlockedEnd:
                output.WriteLine(", {0,8:f3}, {1}, {2}", e.Blocked.DurationUsec / 1000.0, e.Blocked.BlockedOnProcessExe, e.Blocked.BlockedOnProcessId);
                break;
            case EventEnum.Sample:
                output.WriteLine(", {0,18}, \"{1}\"", eventMsec,
                    PrintHexLong(e.Sample.InstructionPointer),
                    e.Sample.SymbolicName);
                break;
            case EventEnum.ContextSwitch:
                output.WriteLine(", {0,15}, {1,4}, {2,4}, {3,12}, {4,12}",
                    e.ContextSwitch.FromProcExe,
                    e.ContextSwitch.FromProcId,
                    e.ContextSwitch.FromThreadId,
                    e.ContextSwitch.FromState,
                    e.ContextSwitch.Reason);
                break;
            default:
                output.WriteLine();
                break;
        }
    }

    /// <summary>
    ///  Add 'CpuStart' 'CpuEnd' events when the processor is active. 
    /// </summary>
    private void ComputeCPURanges(int processId)
    {
        if (Events.Count == 0)
            return;

        // When you do a lot of inserting, it is faster to copy as you insert to avoid O(n*n) behavior
        List<Event> oldEvents = Events;
        Events = new List<Event>(oldEvents.Count * 5 / 4 + 100);

        Event newEvent;
        int i = 0;
        while (i < oldEvents.Count)
        {
            Event e = oldEvents[i];
            if (e.Kind == EventEnum.Sample && e.ProcessId == processId)
            {
                // look backward as much as 1000 msec for the best place to start. 
                // don't go into I/O operation completions or context switches, but subsume any other events. 
                int insertPos = Events.Count;
                int cpuStartTime = e.TimeUsec;
                while (insertPos > 0)
                {
                    Event prevEvent = Events[insertPos - 1];
                    if (e.TimeUsec <= e.TimeUsec - 1000)
                        break;
                    cpuStartTime = prevEvent.TimeUsec;
                    if (prevEvent.ProcessId == e.ProcessId && (prevEvent.Kind == EventEnum.DiskReadEnd || prevEvent.Kind == EventEnum.DiskWriteEnd || prevEvent.Kind == EventEnum.ContextSwitch))
                        break;
                    --insertPos;
                }
                Events.Insert(insertPos, newEvent = new CPUStartEvent(cpuStartTime, e.ProcessId, e.ProcessExe));

                // Go forward as long as you keep seeing Sample events.  
                Event scan = e;
                Event lastSampleEvent = e;
                int cpuEndTime = lastSampleEvent.TimeUsec + 1000;
                for (; ; )
                {
                    Events.Add(scan);
                    i++;
                    if (i >= oldEvents.Count)
                        break;

                    // a better guess is that the CPU ends right after disk IO starts or context switch happens.
                    if ((scan.Kind == EventEnum.ContextSwitch && scan.ContextSwitch.FromProcId == e.ProcessId) ||
                        (!HasContextSwitches && scan.ProcessId == e.ProcessId && scan.Kind == EventEnum.DiskIOStart))
                    {
                        cpuEndTime = scan.TimeUsec;
                        break;
                    }

                    Event nextEvent = oldEvents[i];
                    if (nextEvent.TimeUsec > lastSampleEvent.TimeUsec + 1200)
                        break;
                    scan = nextEvent;
                    if (scan.ProcessId == e.ProcessId && scan.Kind == EventEnum.Sample)
                    {
                        lastSampleEvent = scan;
                        cpuEndTime = lastSampleEvent.TimeUsec + 1000;
                    }
                }
                Events.Add(newEvent = new CPUEndEvent(cpuEndTime, cpuEndTime - cpuStartTime, e.ProcessId, e.ProcessExe));
            }
            else
            {
                Events.Add(e);
                i++;
            }
        }
    }

    // Adds 'BlockedStart' 'BlockedEnd' events that tell when the processor is blocked
    private void ComputeBlockedRanges(int processId)
    {
        // When you do a lot of inserting, it is faster to copy as you insert to avoid O(n*n) behavior
        List<Event> oldEvents = Events;
        Events = new List<Event>(ExpectedEventCount);

        int startBlocked = ProcessStartUsec;
        bool cpuInUse = false;
        int diskQueueLength = 0;
        string processExe = "";
        for (int i = 0; i < oldEvents.Count; i++)
        {
            Event e = oldEvents[i];
            if (e.ProcessId == processId)
            {
                processExe = e.ProcessExe;
                switch (e.Kind)
                {
                    case EventEnum.CPUEnd:
                        cpuInUse = false;
                        break;
                    case EventEnum.CPUStart:
                        cpuInUse = true;
                        break;
                    case EventEnum.DiskIOStart:
                        diskQueueLength++;
                        break;
                    case EventEnum.DiskWriteEnd:
                    case EventEnum.DiskReadEnd:
                        --diskQueueLength;
                        break;
                }
                if (startBlocked < 0)   // we were not Blocked before
                {
                    if (diskQueueLength == 0 && !cpuInUse)  // and now we are
                        startBlocked = e.TimeUsec;          // remember the time
                }
                else                    // we were Blocked before
                {
                    if (diskQueueLength > 0 || cpuInUse)   // and not Blocked now. 
                    {
                        if (e.TimeUsec - startBlocked >= 1000)      // We will discount times less then 1ms. 
                        {
                            InsertEvent(new Event(EventEnum.BlockedStart, startBlocked, processId, processExe));
                            Events.Add(new BlockedEndEvent(e.TimeUsec, e.TimeUsec - startBlocked, processId, processExe));
                        }
                        startBlocked = Int32.MinValue;
                    }
                }
            }
            Events.Add(e);
        }

        if (startBlocked > 0)
        {
            InsertEvent(new Event(EventEnum.BlockedStart, startBlocked, processId, processExe));
            Events.Add(new BlockedEndEvent(ProcessEndUsec, ProcessEndUsec - startBlocked, processId, processExe));
        }

        // Add the 'FollowingWorkDuration to each of the BlockedEndEvents
        int lastIdleStart = ProcessEndUsec;

        for (int i = Events.Count - 1; i > 0; --i)
        {
            Event e = Events[i];
            if (e.ProcessId != processId)
                continue;
            if (e.Kind == EventEnum.BlockedStart)
                lastIdleStart = e.TimeUsec;
            else if (e.Kind == EventEnum.BlockedEnd)
                e.Blocked.FollowingWorkDurationUsec = lastIdleStart - e.TimeUsec;
        }
    }

    // Computes the the time at which the processor goes idle for 1 sec)
    private int ComputeIdleUsec()
    {
        int idleWindowUsec = 1000000;       // what window we look for idle in
        int shutdownWindowUsec = 1000000;   // Ignore this many usec before process end. 

        /*****
        int lastLoadIndex = -1;
        for (int i = 0; i < Events.Count; i++)
        {
            Event e = Events[i];
            if (e.Kind == EventEnum.ImageLoad)
                lastLoadIndex = i;
        }

        if (lastLoadIndex < 0)
            throw new Exception("Error: No images loaded by the scenario!");

         * Console.WriteLine("Found last image load at {0} msec", Events[lastLoadIndex].TimeUsec / 1000);
        ****/

        int idleStartCandidate = -1;
        int blockedStartIdx = -1;
        for (int i = 0; i < Events.Count; i++)
        {
            Event e = Events[i];
            if (e.ProcessId != ProcessId)
                continue;

            if (e.Kind == EventEnum.BlockedStart)
                blockedStartIdx = i;
            else if (blockedStartIdx > 0 && e.Kind == EventEnum.BlockedEnd)
            {
                int blockedStartUsec = Events[blockedStartIdx].TimeUsec;
                if (blockedStartUsec + idleWindowUsec + shutdownWindowUsec > ProcessEndUsec)
                    break;

                // You can get of one sample misses by bad luck (sample happen to be taken when 
                // in the kernel where it is not attributed to your process).  Skip these
                if (e.Blocked.DurationUsec < 1500)
                    continue;

                if (Program.quals.debug)
                    Console.WriteLine("Idle Region: {0:f3} len {1:f3} msec. NextWork: {2:f3} msec",
                        blockedStartUsec / 1000.0,
                        e.Blocked.DurationUsec / 1000.0,
                        e.Blocked.FollowingWorkDurationUsec / 1000.0
                        );

                // If we just had a N msec blocking time, see if the next 3N 
                // msec after it makes up for it (you do at 2N msec of work).  
                // If so, you are clearly not idle 
                int nextWorkUsec = WorkOverRange(i, e.Blocked.DurationUsec * 3);
                if (nextWorkUsec < 2 * e.Blocked.DurationUsec)
                {
                    // OK, we seem to have found a candidate for idle.  Let's
                    // see how much time we spent over a larger window. 
                    int workUsec = WorkOverRange(blockedStartIdx, idleWindowUsec);

                    idleStartCandidate = blockedStartUsec;

                    int workUsecHalf = WorkOverRange(blockedStartIdx, idleWindowUsec / 2);

                    if (workUsec > idleWindowUsec / 50)    // less then 2 % time is active
                    {
                        Console.WriteLine("WARNING: did not get crisp idle, startup percentages will be suspect");
                    }
                    Console.WriteLine("Idle Candidate At {0:f0}ms, Len {1:f1} Did {2:f1}ms work {3:f0}ms {4:f1}ms in {5:f0}ms",
                        idleStartCandidate / 1000.0,
                        e.Blocked.DurationUsec / 1000.0,
                        workUsecHalf / 1000.0,
                        idleWindowUsec / 2 / 1000.0,
                        workUsec / 1000.0,
                        idleWindowUsec / 1000.0);

                    if (workUsec < idleWindowUsec / 20)
                    {
                        Console.WriteLine("Idle At {0:f0}ms, from process start", (idleStartCandidate - ProcessStartUsec) / 1000.0);
                        break;
                    }
                }
                else
                {
                    if (Program.quals.debug)
                        Console.WriteLine("At {0:f3} idle {1:f3} msec however we do at least {2:f3} msec work",
                            blockedStartUsec / 1000.0, e.Blocked.DurationUsec / 1000.0, nextWorkUsec / 1000.0);
                }
            }
        }

        if (idleStartCandidate < 0)
        {
            if (ProcessEndUsec > 0)
            {
                Console.WriteLine("Process ended before idle found, using process end as idle time");
                idleStartCandidate = ProcessEndUsec;
            }
            else
            {
                Console.WriteLine("Process never died, and no idle candidate");
                idleStartCandidate = Int32.MaxValue;
            }
        }
        return idleStartCandidate;
    }

    // helper for ComputeIdleUsec
    private int WorkOverRange(int startIdx, int durationUsec)
    {
        Debug.Assert(Events[startIdx].Kind == EventEnum.BlockedStart || Events[startIdx].Kind == EventEnum.BlockedEnd);
        int endUsec = Events[startIdx].TimeUsec + durationUsec;
        int workUsec = 0;
        for (int i = startIdx; i < Events.Count; i++)
        {
            Event e = Events[i];
            if (e.ProcessId != ProcessId)
                continue;
            if (e.Kind == EventEnum.BlockedEnd)
                workUsec += e.Blocked.FollowingWorkDurationUsec;
            if (e.TimeUsec > endUsec)
                break;
        }
        return workUsec;
    }

    #region Page Lookup Functionality

    private string GetImageNameForVirtualAddress(int virtualAddress)
    {
        foreach (LoadedImage image in loadedImages)
        {
            if (image.ImageBase <= virtualAddress && virtualAddress < image.ImageBase + image.Size)
                return image.FileName;
        }
        return "";
    }
    struct LoadedImage
    {
        public LoadedImage(string fileName, long imageBase, int size)
        {
            FileName = fileName;
            ImageBase = imageBase;
            Size = size;
        }
        public string FileName;
        public long ImageBase;
        public long Size;
    }
    private List<LoadedImage> loadedImages = new List<LoadedImage>();
    #endregion

    #region String parsing utilites
    private static string StripQuotes(string str)
    {
        int start = str.IndexOf("\"") + 1;
        int end = str.LastIndexOf("\"");
        return str.Substring(start, end - start);
    }
    private static int ToHexInt(string str)
    {
        str = str.Trim();
        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            str = str.Substring(2);
        return Int32.Parse(str, System.Globalization.NumberStyles.HexNumber);
    }
    private static long ToHexLong(string str)
    {
        str = str.Trim();
        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            str = str.Substring(2);
        return Int64.Parse(str, System.Globalization.NumberStyles.HexNumber);
    }
    internal static string PrintHexInt(int num)
    {
        return "0x" + num.ToString("x");
    }
    internal static string PrintHexLong(long num)
    {
        int asInt = (int)num;
        if (num == asInt)
            return PrintHexInt(asInt);      // avoids lots of FFFFF being printed
        return "0x" + num.ToString("x");
    }
    #endregion
}

#region Discriminated Union Event

public enum EventEnum
{
    PageFault,
    ImageLoad,
    DiskIOStart,
    DiskReadEnd,
    DiskWriteEnd,
    CPUStart,
    CPUEnd,
    BlockedStart,
    BlockedEnd,
    Sample,
    ContextSwitch,
    // NetworkIO,
}

public class PageFaultEvent : Event
{
    public PageFaultEvent(int timeUsec, int processId, string processExe, int threadId, long virtualAddress, long instructionPointer, string type, string fileName)
        : base(EventEnum.PageFault, timeUsec, processId, processExe, threadId)
    {
        VirtualAddress = virtualAddress;
        InstructionPointer = instructionPointer;
        Type = type;
        FileName = fileName;
    }
    public long VirtualAddress;
    public long InstructionPointer;
    public string Type;
    public string FileName;
}

public class ImageLoadEvent : Event
{
    public ImageLoadEvent(int timeUsec, int processId, string processExe, long imageBase, int size, string fileName)
        : base(EventEnum.ImageLoad, timeUsec, processId, processExe)
    {
        ImageBase = imageBase;
        Size = size;
        FileName = fileName;
    }
    public long ImageBase;
    public int Size;
    public string FileName;
}

public class ContextSwitchEvent : Event
{
    public ContextSwitchEvent(int timeUsec, int processId, string processExe, int threadId, int fromProc, string fromProcName, int fromThread, string fromState, string reason)
        : base(EventEnum.ContextSwitch, timeUsec, processId, processExe, threadId)
    {
        FromProcId = fromProc;
        FromProcExe = fromProcName;
        FromThreadId = fromThread;
        FromState = fromState;
        Reason = reason;
    }
    public int FromProcId;
    public string FromProcExe;
    public int FromThreadId;
    public string FromState;
    public string Reason;
}

public class DiskIOStartEvent : Event
{
    public DiskIOStartEvent(int timeUsec, int processId, string processExe, int threadId, bool isRead, int diskNum)
        : base(EventEnum.DiskIOStart, timeUsec, processId, processExe, threadId)
    {
        DiskNum = diskNum;
    }
    public int DiskNum;
}

public class DiskIOEndEvent : Event
{
    public DiskIOEndEvent(int timeUsec, int processId, string processExe, int threadId, bool isRead, int diskNum, long offset, int size, int diskUsec, int elapsedUsec, string fileName)
        : base(isRead ? EventEnum.DiskReadEnd : EventEnum.DiskWriteEnd, timeUsec, processId, processExe, threadId)
    {
        DiskNum = diskNum;
        Offset = offset;
        Size = size;
        DiskUsec = diskUsec;
        ElapsedUsec = elapsedUsec;
        FileName = fileName;
    }
    public int DiskNum;
    public long Offset;
    public int Size;
    public int DiskUsec;        // Time that the disk took.
    public int ElapsedUsec;     // disk time + queue wait time.  
    public string FileName;
}

public class CPUStartEvent : Event
{
    public CPUStartEvent(int timeUsec, int processId, string processExe)
        : base(EventEnum.CPUStart, timeUsec, processId, processExe)
    {
    }
}

public class CPUEndEvent : Event
{
    public CPUEndEvent(int timeUsec, int durationUsec, int processId, string processExe)
        : base(EventEnum.CPUEnd, timeUsec, processId, processExe)
    {
        DurationUsec = durationUsec;
    }
    public int DurationUsec;
}

public class BlockedEndEvent : Event
{
    public BlockedEndEvent(int timeUsec, int durationUsec, int processId, string processExe)
        : base(EventEnum.BlockedEnd, timeUsec, processId, processExe)
    {
        DurationUsec = durationUsec;
        BlockedOnProcessExe = "Unknown";
    }
    public int DurationUsec;
    public int FollowingWorkDurationUsec;
    public string BlockedOnProcessExe;
    public int BlockedOnProcessId;
}

public class SampleEvent : Event
{
    public SampleEvent(int timeUsec, int processId, string processExe, int threadId, long instructionPointer, string symbolicName)
        : base(EventEnum.Sample, timeUsec, processId, processExe, threadId)
    {
        InstructionPointer = instructionPointer;
        SymbolicName = symbolicName;
    }
    public long InstructionPointer;
    public string SymbolicName;
}

public class Event
{
    public Event(EventEnum kind, int timeUsec, int processId, string processExe, int threadId)
    {
        Kind = kind;
        TimeUsec = timeUsec;
        ProcessId = processId;
        ProcessExe = processExe;
        ThreadId = threadId;
    }

    public Event(EventEnum kind, int timeUsec, int processId, string processExe)
        : this(kind, timeUsec, processId, processExe, -1)
    {
    }

    public EventEnum Kind;
    public int TimeUsec;
    public string ProcessExe;
    public int ProcessId;
    public int ThreadId;

    public PageFaultEvent PageFault { get { return (PageFaultEvent)this; } }
    public ImageLoadEvent ImageLoad { get { return (ImageLoadEvent)this; } }
    public DiskIOEndEvent DiskIOEnd { get { return (DiskIOEndEvent)this; } }
    public DiskIOStartEvent DiskIOStart { get { return (DiskIOStartEvent)this; } }
    public CPUEndEvent CPU { get { return (CPUEndEvent)this; } }
    public BlockedEndEvent Blocked { get { return (BlockedEndEvent)this; } }
    public SampleEvent Sample { get { return (SampleEvent)this; } }
    public ContextSwitchEvent ContextSwitch { get { return (ContextSwitchEvent)this; } }
};

#endregion


#region AnalysisInterval Support




[Flags]
public enum AnalysisIntervalFlags
{
    Basic = 1,
    DllInfo = 2,
    ReadInfo = 4,
    BlockedInfo = 8,
};

/// <summary>
/// Represents a table of FileInfo's indexed by name.    
/// </summary>
public class AnalysisInterval
{
    public const int PageSize = 4096;

    public AnalysisInterval(ProcessTrace trace) : this(trace, 0, Int32.MaxValue, trace.ProcessId) { }
    public AnalysisInterval(ProcessTrace trace, int startUsec, int stopUsec) : this(trace, startUsec, stopUsec, trace.ProcessId) { }
    public AnalysisInterval(ProcessTrace trace, int startUsec, int stopUsec, int processId)
    {
        startupUsec = trace.IdleUsec - trace.ProcessStartUsec;
        this.trace = trace;
        this.processId = processId;
        this.startUsec = startUsec;
        this.stopUsec = stopUsec;

        int diskQueueDepth = 0;
        int diskStarted = startUsec;
        foreach (Event e in trace.Events)
        {
            if (e.TimeUsec >= stopUsec)
                break;
            if (e.TimeUsec < startUsec)
                continue;
            if (e.ProcessId != processId)
                continue;

            switch (e.Kind)
            {
                case EventEnum.CPUEnd:
                    totalCPUUsec += e.CPU.DurationUsec;
                    break;
                case EventEnum.BlockedEnd:
                    totalBlockedUsec += e.Blocked.DurationUsec;
                    blockedEvents.Add(e.Blocked);
                    break;
                case EventEnum.ImageLoad:
                    ImageLoad(e.ImageLoad);
                    break;
                case EventEnum.PageFault:
                    PageFault(e.PageFault);
                    break;
                case EventEnum.DiskIOStart:
                    if (diskQueueDepth == 0)
                        diskStarted = e.TimeUsec;
                    diskQueueDepth++;
                    break;
                case EventEnum.DiskReadEnd:
                case EventEnum.DiskWriteEnd:
                    DiskIOEnd(e.DiskIOEnd);
                    if (diskQueueDepth > 0)
                        --diskQueueDepth;
                    if (diskQueueDepth == 0)
                        totalDiskUsec += (e.TimeUsec - diskStarted);

                    totalDiskServiceUsec += e.DiskIOEnd.DiskUsec;
                    break;
            }
        }
        blockedEvents.Sort(delegate(BlockedEndEvent e1, BlockedEndEvent e2)
        {
            return e2.DurationUsec - e1.DurationUsec;
        });
        totalActiveUsec = totalCPUUsec + totalDiskUsec;
    }

    public void PrintSummaryStats(AnalysisIntervalFlags verbosity)
    {

        FileInfo mscorjit;
        if (fileInfo.TryGetValue("mscorjit.dll", out mscorjit))
            Console.WriteLine("WARNING: ******* mscorjit.dll was loaded by the process (You are not completely NGENED)");

        FileInfo mscorsec;
        if (fileInfo.TryGetValue("mscorsec.dll", out mscorsec))
            Console.WriteLine("WARNING: ******* mscorsec.dll was loaded (Are validating Authenticode signatures on managed code?)");

        foreach (string fileName in fileInfo.Keys)
        {
            if (fileName.EndsWith(".ni.dll", StringComparison.OrdinalIgnoreCase))
            {
                string ILImageName = fileName.Substring(0, fileName.Length - 6) + "dll";
                FileInfo ILImageInfo = null;
                if (fileInfo.TryGetValue(ILImageName, out ILImageInfo))
                    Console.WriteLine("WARNING: ***** both IL image {0} and NGEN image {1} loaded", ILImageName, fileName);
            }
        }

        FileInfo mscorwks;
        if (fileInfo.TryGetValue("mscorwks.dll", out mscorwks))
        {
            Console.WriteLine("Runtime Loaded: " + mscorwks.Name);
            Console.WriteLine();
        }

        double readSizeMB = totalReads.Sum / (1024.0 * 1024.0);
        double estimatedTimeMSec = EstDiskTimeMSec(totalReads);

        Console.WriteLine("Total Disk Reads       = {0,8}", totalReads.Count);
        Console.WriteLine("Total Disk Data        = {0,8:f1} MB = {1,5} Pages",
            readSizeMB, totalReads.Sum / PageSize);

        Console.WriteLine();
        Console.WriteLine("Prefetched Disk Reads  = {0,8}    ({1,3:f0}%)",
            prefetchReads.Count, 100.0 * prefetchReads.Count / totalReads.Count);
        Console.WriteLine("Prefetched Disk Data   = {0,8:f1} MB ({1,3:f0}%)",
            prefetchReads.Sum / (1024.0 * 1024.0), 100.0 * prefetchReads.Sum / totalReads.Sum);

        Console.WriteLine();
        Console.WriteLine("Estimated Disk time    = {0,8:f1} msec = {1,4:f1}%",
            estimatedTimeMSec,
            100.0 * estimatedTimeMSec * 1000 / startupUsec);

        Console.WriteLine();
        Console.WriteLine("Total Startup Time     = {0,8:f1} msec", startupUsec / 1000.0);
        Console.WriteLine("Real Disk Service time = {0,8:f1} msec = {1,4:f1}%",
            totalDiskServiceUsec / 1000.0,
            100.0 * totalDiskServiceUsec / startupUsec);
        Console.WriteLine("Total Disk Elapsed Time= {0,8:f1} msec = {1,4:f1}%",
            totalDiskUsec / 1000.0,
            100.0 * totalDiskUsec / startupUsec);

        Console.WriteLine();
        Console.WriteLine("Time CPU is busy       = {0,8:f1} msec = {1,4:f1}%",
            totalCPUUsec / 1000.0,
            100.0 * totalCPUUsec / startupUsec);
        Console.WriteLine("Time Blocked (non-disk)= {0,8:f1} msec = {1,4:f1}%",
            totalBlockedUsec / 1000.0,
            100.0 * totalBlockedUsec / startupUsec);

        Console.WriteLine();
        Console.WriteLine("Time Active (disk+cpu) = {0,8:f1} msec = {1,4:f1}%",
            totalActiveUsec / 1000.0,
            100.0 * totalActiveUsec / startupUsec);
        int totalWarmStartup = totalCPUUsec + totalBlockedUsec;
        Console.WriteLine("Blocked ms + CPU ms    = {0,8:f1} msec = {1,4:f1}%",
            totalWarmStartup / 1000.0,
            100.0 * totalWarmStartup / startupUsec);

        Console.WriteLine();
        int totalTime = totalDiskUsec + totalCPUUsec + totalBlockedUsec;
        int error = startupUsec - totalTime;
        Console.WriteLine("Error                  = {0,8:f1} msec = {1,4:f1}%",
            error / 1000.0,
            100.0 * error / startupUsec);

        Console.WriteLine();
        double NGENreadSizeMB = ngenReads.Sum / (1024.0 * 1024.0);
        double NGENestimatedTimeMSec = EstDiskTimeMSec(ngenReads);

        int ngenSize = 0;
        int ngenPageFaults = 0;
        foreach (FileInfo file in fileInfo.Values)
        {
            if (file.Name.EndsWith(".ni.dll", StringComparison.OrdinalIgnoreCase) ||
                file.Name.EndsWith(".ni.exe", StringComparison.OrdinalIgnoreCase))
            {
                ngenSize += file.ImageSize;
                ngenPageFaults += file.TotalPageFaults;
            }
        }
        Console.WriteLine("NGEN Image Size        = {0:f1} MB = {1} Pages",
            ngenSize / (1024.0 * 1024.0), ngenSize / PageSize);
        Console.WriteLine("NGEN Disk Reads        = {0}                    ({1:f1}% total)",
            ngenReads.Count, 100.0 * ngenReads.Count / totalReads.Count);
        Console.WriteLine("NGEN Disk Data         = {0:f1} MB = {1} Pages  ({2:f1}% total)",
            NGENreadSizeMB, ngenReads.Sum / PageSize, 100.0 * ngenReads.Sum / totalReads.Sum);
        Console.WriteLine("NGEN Est Disk time     = {0:f1} msec            ({1:f1}% total)",
            NGENestimatedTimeMSec, 100.0 * NGENestimatedTimeMSec / estimatedTimeMSec);
        Console.WriteLine("NGEN Page Faults       = {0} Pages ({1:f1}% total image faults)",
            ngenPageFaults, 100.0 * ngenPageFaults / (totalPageFaults - nonImagePageFaults));

        Console.WriteLine();
        double averageSizeKB = readSizeMB * 1000.0 / totalReads.Count;
        Console.WriteLine("Average Read Size      = {0:f2} KB", averageSizeKB.ToString("f2"));

        Console.WriteLine();
        Console.WriteLine("Total files Read = {0} .dll = {1} .ni.dll = {2}", fileInfo.Count, dlls, ngenCount);

        Console.WriteLine();
        Console.WriteLine("PageFault Total {0,-8}  Private {1,-8}  Image {2,-8}  Non-Image {2,-8}",
            totalPageFaults, totalPrivatePageFaults, totalPageFaults - nonImagePageFaults, nonImagePageFaults);

        if ((verbosity & (AnalysisIntervalFlags.DllInfo | AnalysisIntervalFlags.ReadInfo)) != 0)
            PrintFileStats(verbosity);

        Console.WriteLine();
        Console.WriteLine("Blocked time {0:f1} msec occured in {1} intervals", totalBlockedUsec / 1000.0, blockedEvents.Count);
        if ((verbosity & AnalysisIntervalFlags.BlockedInfo) != 0)
        {
            int bigBlocksTotal = 0;
            foreach (BlockedEndEvent e in blockedEvents)
            {
                if (e.DurationUsec * 100 < totalBlockedUsec)    // clip at 1%
                    break;

                int beginUsec = e.TimeUsec - e.DurationUsec;
                bigBlocksTotal += e.DurationUsec;
                Console.WriteLine("  Interval {0,7:f1} - {1,7:f1}: {2,5:f1} msec ({3,5:f1}% of total blocked time)",
                   beginUsec / 1000.0, e.TimeUsec / 1000.0,
                   e.DurationUsec / 1000.0,
                   100.0 * e.DurationUsec / totalBlockedUsec);

                int processActiveUsec = 0;
                List<AnalysisInterval> processAnalysises = new List<AnalysisInterval>();
                foreach (int procId in trace.ProcessesActiveInRange(beginUsec, e.TimeUsec))
                {
                    if (procId == processId)
                        continue;

                    AnalysisInterval interval = new AnalysisInterval(trace, beginUsec, e.TimeUsec, procId);
                    if (interval.totalActiveUsec == 0)
                        continue;

                    processActiveUsec += interval.totalActiveUsec;
                    processAnalysises.Add(interval);
                }
                processAnalysises.Sort(delegate(AnalysisInterval x, AnalysisInterval y)
                {
                    return y.totalActiveUsec - x.totalActiveUsec;
                });

                if (processAnalysises.Count > 0)
                {
                    Console.WriteLine("    Other Processes Active {0,7:f1} msec ({1,3:f0}% of blocked interval)",
                        processActiveUsec / 1000.0,
                        100 * processActiveUsec / e.DurationUsec);

                    foreach (AnalysisInterval interval in processAnalysises)
                    {
                        Console.WriteLine("      {0,-15} PID {1,4} Active {2,6:f1} msec {3,2:f0}% ({4,5:f1}% CPU, {5,5:f1}% Disk)",
                            trace.ProcessExes[interval.processId], interval.processId,
                            interval.totalActiveUsec / 1000.0, 100.0 * interval.totalActiveUsec / e.DurationUsec,
                            100.0 * interval.totalCPUUsec / interval.totalActiveUsec,
                            100.0 * interval.totalDiskUsec / interval.totalActiveUsec);
                    }
                }
                else
                    Console.WriteLine("  No other process was active for more than 1msec during this time");
            }
            Console.WriteLine("  All other small intervals:  {0,5:f1} msec ({1,5:f1}% of total blocked time)",
                (totalBlockedUsec - bigBlocksTotal) / 1000.0,
                100.0 * (totalBlockedUsec - bigBlocksTotal) / totalBlockedUsec);
        }

    }

    public void PrintFileStats(AnalysisIntervalFlags verbosity)
    {
        Console.WriteLine();
        Console.WriteLine("Breakdown by file");
        string[] fileNames = new string[fileInfo.Count];
        fileInfo.Keys.CopyTo(fileNames, 0);
        Array.Sort<string>(fileNames, delegate(string name1, string name2)
        {
            return (int)(1000000 * (EstDiskTimeMSec(fileInfo[name2].ReadStats) - EstDiskTimeMSec(fileInfo[name1].ReadStats)));
        });

        foreach (string fileName in fileNames)
        {
            FileInfo info = fileInfo[fileName];
            if (info.ReadStats.Count == 0)          // This happens on system Dlls that are already in memory
                continue;

            Console.WriteLine("EstTime= {0,7:f1} msec **** {1}    ({2})",
                EstDiskTimeMSec(info.ReadStats), fileName, info.Name);
            Console.WriteLine("    DllSize: {0,5:f2}, MB = {1,5} pages, Read {2,6:f1}%",
                info.ImageSize / (1024.0 * 1024.0),
                info.ImageSize / PageSize,
                (info.ImageSize > 0) ? (100.0 * info.ReadStats.Sum / info.ImageSize) : 0);
            Console.WriteLine("    ReadCount={0,3}, ReadData={1,5:f2} MB = {2,5} Pages, AverageRead={3,5:f1} Pages",
                info.ReadStats.Count,
                info.ReadStats.Sum / (1024.0 * 1024.0),
                info.ReadStats.Sum / PageSize,
                (double)info.ReadStats.Sum / info.ReadStats.Count / PageSize);
            Console.WriteLine("    PageFault Total {0,8}  Private {1,8} Density {2:f1}%",
                info.TotalPageFaults, info.PrivatePageFaults, (100.0 * PageSize * info.TotalPageFaults) / info.ReadStats.Sum);

            if (fileName.StartsWith("$") || fileName == "pagefile.sys")
                continue;

            if ((verbosity & AnalysisIntervalFlags.ReadInfo) != 0)
            {
                Console.WriteLine("    ImageBase = 0x{0:x}", info.ImageBase);
                Console.WriteLine("    MinTouch = 0x{0:x}", info.MinTouchOffset);
                foreach (Range read in info.Reads)
                {
                    Console.WriteLine("     0x{0:x} Length {1,-4} Pages",
                        read.Offset - info.MinTouchOffset + info.ImageBase, read.Size / PageSize);
                }
            }
        }
    }

    private void ImageLoad(ImageLoadEvent e)
    {
        FileInfo info = FindInfoForFile(e.FileName);
        if (info.ImageBase != 0)
        {
            /** FIX NOW turn back on 
            if (String.Compare(info.Name, e.FileName, StringComparison.OrdinalIgnoreCase) == 0)
                Console.WriteLine("Dll loaded twice (happens when things not in gac): " + Path.GetFileName(e.FileName));
            else
                Console.WriteLine("Warning Loading two dlls with filename " + Path.GetFileName(e.FileName));
            ***/
        }
        info.ImageBase = e.ImageBase;
        info.ImageSize = e.Size;
    }

    private void DiskIOEnd(DiskIOEndEvent e)
    {
        FileInfo info = FindInfoForFile(e.FileName);

        info.ReadStats.Count++;
        info.ReadStats.Sum += e.Size;

        if (e.FileName.EndsWith(".ni.dll", StringComparison.OrdinalIgnoreCase))
        {
            ngenReads.Count++;
            ngenReads.Sum += e.Size;
        }

        if (e.TimeUsec < trace.Kernel32LoadUsec)
        {
            prefetchReads.Count++;
            prefetchReads.Sum += e.Size;
        }

        totalReads.Count++;
        totalReads.Sum += e.Size;

        info.Reads.Add(new Range(e.Offset, e.Size));
        if (e.Offset < info.MinTouchOffset)
            info.MinTouchOffset = e.Offset;
    }


    private void PageFault(PageFaultEvent e)
    {
        FileInfo image = FindInfoForFile(e.FileName);
        totalPageFaults++;
        switch (e.Type)
        {
            case "Transition":      // soft shareable
            case "HardFault":       // hard presumed shareable 
            case "CopyOnWrite":
                if (image != null)
                {
                    image.TotalPageFaults++;
                    if (e.Type == "CopyOnWrite")
                    {
                        image.PrivatePageFaults++;
                        totalPrivatePageFaults++;
                    }
                }
                else
                {
                    nonImagePageFaults++;
                    if (e.Type == "CopyOnWrite")
                        totalPrivatePageFaults++;
                    // FIX NOW  Console.WriteLine("Warning: could not find image for fault " + ProcessTrace.PrintHexLong(e.VirtualAddress) + " of type " + e.Type);
                }
                break;
            case "DemandZero":
                if (image != null)
                {
                    // FIX NOW Console.WriteLine("Warning: unexpected Demand zero at " + ProcessTrace.PrintHexLong(e.VirtualAddress) + " for " + image.Name);
                }
                nonImagePageFaults++;
                totalPrivatePageFaults++;
                break;
        }
    }
    private FileInfo FindInfoForFile(string path)
    {
        if (String.IsNullOrEmpty(path))
            return null;

        string shortName = Path.GetFileName(path);
        FileInfo info;
        if (!fileInfo.TryGetValue(shortName, out info))
        {
            info = fileInfo[shortName] = new FileInfo(path);
            if (shortName.EndsWith(".ni.dll", StringComparison.OrdinalIgnoreCase))
                ngenCount++;
            if (shortName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                dlls++;
        }
        return info;
    }
    /* Private Stuff */
    private double EstDiskTimeMSec(SumAndCount stats)
    {
        return EstDiskTimeMSec(stats.Count, stats.Sum);
    }
    private double EstDiskTimeMSec(int readCount, int readSize)
    {
        double readSizeMB = readSize / (1024.0 * 1024.0);
        return 20 * readSizeMB + 4 * readCount;
    }

    private Dictionary<string, FileInfo> fileInfo = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
    private List<BlockedEndEvent> blockedEvents = new List<BlockedEndEvent>();
    private int ngenCount;
    private int dlls;
    private ProcessTrace trace;

    private int totalPageFaults;
    private int totalPrivatePageFaults;
    private int nonImagePageFaults;

    private SumAndCount totalReads;
    private SumAndCount prefetchReads;
    private SumAndCount ngenReads;

    private int processId;
    private int startUsec;
    private int stopUsec;

    private int totalActiveUsec;
    private int totalCPUUsec;
    private int totalDiskUsec;
    private int totalDiskServiceUsec;
    private int totalBlockedUsec;

    private int startupUsec;
}


/// <summary>
/// Keep information on a single file
/// </summary>
public class FileInfo
{
    public FileInfo(string name)
    {
        Reads = new List<Range>();
        MinTouchOffset = Int64.MaxValue;
        Name = name;
    }
    public SumAndCount ReadStats;
    public List<Range> Reads;

    public string Name;

    public int TotalPageFaults;
    public int PrivatePageFaults;

    public long MinTouchOffset;
    public long ImageBase;
    public int ImageSize;
}

/// <summary>
/// Trivial (offset, size) tuple. 
/// </summary>
public struct Range
{
    public Range(long offset, int size)
    {
        this.Offset = offset;
        this.Size = size;
    }
    public long Offset;
    public int Size;
};

/// <summary>
/// Trival (name, sum, count) tuple for agregating values. 
/// </summary>
public struct SumAndCount
{
    public void Add(int value)
    {
        Sum += value;
        Count++;
    }
    public int Sum;
    public int Count;
};

#endregion  // AnalysisInterval Support

#endregion  // ETW file (.etl) processing logic



