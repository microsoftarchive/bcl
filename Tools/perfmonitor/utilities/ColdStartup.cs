using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections;
using System.Diagnostics;

// TODO this is all experimental...

public static class ColdStartup
{
    #region native Methods

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool
    EmptyWorkingSet(IntPtr processHandle);

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
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    const UInt32 STANDARD_RIGHTS_REQUIRED = 0x000F0000;
    const UInt32 SECTION_QUERY = 0x0001;
    const UInt32 SECTION_MAP_WRITE = 0x0002;
    const UInt32 SECTION_MAP_READ = 0x0004;
    const UInt32 SECTION_MAP_EXECUTE = 0x0008;
    const UInt32 SECTION_EXTEND_SIZE = 0x0010;
    const UInt32 SECTION_ALL_ACCESS = (
        STANDARD_RIGHTS_REQUIRED | 
        SECTION_QUERY |
        SECTION_MAP_WRITE |
        SECTION_MAP_READ |
        SECTION_MAP_EXECUTE |
        SECTION_EXTEND_SIZE);
    const UInt32 FILE_MAP_ALL_ACCESS = SECTION_ALL_ACCESS;

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

    [DllImport("kernel32.dll")]
    static extern IntPtr MapViewOfFileEx(IntPtr hFileMappingObject,
       FileMapAccessType dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow,
       UIntPtr dwNumberOfBytesToMap, IntPtr lpBaseAddress);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateFileMapping(IntPtr hFile,
       IntPtr lpFileMappingAttributes, PageProtection flProtect, uint dwMaximumSizeHigh,
       uint dwMaximumSizeLow, string lpName);

    #endregion

    private static void ReloadSystemFiles()
    {
    }

    public static void FlushFileSystemCache()
    {
#if false
        hHandle = OpenFileMapping(FILE_MAP_ALL_ACCESS, false, SharedMemoryName);
        pBuffer = MapViewOfFile(hHandle, FILE_MAP_ALL_ACCESS, 0, 0, &NumBytes);
#endif

        // Make a really big file. 
        string fileName = "test";
        FileStream file = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite);
        long fileLength = 2L * 1024 * 1024 * 1024;
        file.SetLength(fileLength);

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
    }
}

class ProcessTasks
{
    [DllImport("advapi32.dll", SetLastError = true)]
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

        static IEnumerable OpenPrefetchFile()
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
            foreach(string s in matches)
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

            foreach(FileStream fs in OpenPrefetchFile())
            {
                using(fs)
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
