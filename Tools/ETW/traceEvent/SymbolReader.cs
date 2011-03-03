//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
// 
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// #define DEBUG_SERIALIZE
using System;
using System.Collections.Generic;
using System.ComponentModel; // For Win32Excption;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Diagnostics.Eventing;
using System.Text.RegularExpressions;

namespace Symbols
{
    public unsafe class SymbolReader : IDisposable
    {
        /// <summary>
        /// Opens a new SymbolReader.   All diagnostics messages about symbol lookup go to 'log'.  
        /// If 'localSymbolsOnly' is true, then network symbol servers are not consulted (but cached
        /// PDBs that are local to the machine are checked).
        /// </summary>
        public SymbolReader(TextWriter log)
        {
            // Load the necessary native DLLs
            var curAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            var assemDir = Path.GetDirectoryName(curAssembly.ManifestModule.FullyQualifiedName);

            this.log = log;
            SymbolReaderNativeMethods.SymOptions options = SymbolReaderNativeMethods.SymGetOptions();
            SymbolReaderNativeMethods.SymSetOptions(
                SymbolReaderNativeMethods.SymOptions.SYMOPT_DEBUG |
                // SymbolReaderNativeMethods.SymOptions.SYMOPT_DEFERRED_LOADS |
                SymbolReaderNativeMethods.SymOptions.SYMOPT_LOAD_LINES |
                SymbolReaderNativeMethods.SymOptions.SYMOPT_EXACT_SYMBOLS |
                SymbolReaderNativeMethods.SymOptions.SYMOPT_UNDNAME
                );
            SymbolReaderNativeMethods.SymOptions options1 = SymbolReaderNativeMethods.SymGetOptions();

            currentProcess = Process.GetCurrentProcess();  // Only here to insure processHandle does not die.  TODO get on safeHandles. 
            currentProcessHandle = currentProcess.Handle;

            bool success = SymbolReaderNativeMethods.SymInitializeW(currentProcessHandle, null, false);
            if (!success)
            {
                // This captures the GetLastEvent (and has to happen before calling CloseHandle()
                currentProcessHandle = IntPtr.Zero;
                throw new Win32Exception();
            }
            callback = new SymbolReaderNativeMethods.SymRegisterCallbackProc(this.StatusCallback);
            success = SymbolReaderNativeMethods.SymRegisterCallbackW64(currentProcessHandle, callback, 0);

            Debug.Assert(success);

            // TODO remove when we are sure we won't use SymFromAddr
            // const int maxNameLen = 512;
            // int bufferSize = sizeof(SymbolReaderNativeMethods.SYMBOL_INFO) + maxNameLen*2;
            // buffer = (byte*)Marshal.AllocHGlobal(bufferSize);
            // SymbolReaderNativeMethods.ZeroMemory((IntPtr)buffer, (uint)bufferSize);
            // symbolInfo = (SymbolReaderNativeMethods.SYMBOL_INFO*)buffer;
            // symbolInfo->SizeOfStruct = (uint)sizeof(SymbolReaderNativeMethods.SYMBOL_INFO);
            // symbolInfo->MaxNameLen = maxNameLen - 1;

            lineInfo.SizeOfStruct = (uint)sizeof(SymbolReaderNativeMethods.IMAGEHLP_LINE64);
            messages = new StringBuilder();
        }
        /// <summary>
        /// Loads the PDB file associated with 'moduleFilePath' loaded at at moduleImageBase.  Uses
        /// the _NT_SYMBOL_PATH to look up the PDB.   'moduleFilePath' must exist, as it needs to be opened
        /// to get the necessary information to look up the PDB.  
        /// </summary>
        public SymbolReaderModule OpenSymbolsForModule(string moduleFilepath, Address moduleImageBase)
        {
            return new SymbolReaderModule(this, moduleFilepath, moduleImageBase);
        }
        /// <summary>
        /// Finds the symbol file for 'exeFilePath' that exists on the current machine (we open
        /// it to find the needed info).   Will fetch the file from the symbol server if necessary.
        /// </summary>
        public string FindSymbolFilePathForModule(string exeFilePath)
        {
            messages.Length = 0;
            string ret = null;
            string message = null;
            try
            {
                if (File.Exists(exeFilePath))
                {
                    using (var peFile = new PEFile.PEFile(exeFilePath))
                    {
                        string pdbName = null;
                        Guid pdbGuid = Guid.Empty;
                        int pdbAge = 0;
                        if (peFile.GetPdbSignature(ref pdbName, ref pdbGuid, ref pdbAge))
                            return FindSymbolFilePath(pdbName, pdbGuid, pdbAge);
                        else
                            message = "File {0} does not have a codeview debug signature.";
                    }
                }
                else
                    message = "File {0} does not exist.";
            }
            catch (Exception) {
                message = "Failure opening PE file {0}.";
            }
            if (message != null)
            {
                log.WriteLine(" <FindSymbolFilePathForModule Status=\"FAIL\", StatusCode=\"-1\">");
                log.Write("  ");
                log.WriteLine(message, XmlUtilities.XmlEscape(exeFilePath)); 
                log.WriteLine(" </FindSymbolFilePathForModule>");
            }
            return ret;
        }
        /// <summary>
        /// Find the complete PDB path, given just the simple name (filename + pdb extension) as well as its 'signature', 
        /// which uniquely identifies it (on symbol servers).   Uses the _NT_SYMBOL_PATH (including Symbol servers) to 
        /// look up the PDB, and will download the PDB to the local cache if necessary.  
        /// 
        /// </summary>
        /// <param name="pdbSimpleName">The name of the PDB file (we only use the file name part)</param>
        /// <param name="pdbGuid">The GUID that is embedded in the DLL in the debug information that allows matching the DLL and the PDB</param>
        /// <param name="pdbAge">Tools like BBT transform a DLL into another DLL (with the same GUID) the 'pdbAge' is a small integers
        /// that indicates how many transformations were done</param>
        /// <param name="fileVersion">This is an optional string that identifies the file version (the 'Version' resource information.  
        /// Use only for error messages.</param>
        public string FindSymbolFilePath(string pdbSimpleName, Guid pdbGuid, int pdbAge, string fileVersion=null)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            StringBuilder pdbFullPath = new StringBuilder(260);
            messages.Length = 0;
            bool foundPDB = SymbolReaderNativeMethods.SymFindFileInPathW(currentProcessHandle,
                null,                  // Search path
                pdbSimpleName,
                ref pdbGuid,        // ID (&GUID)
                pdbAge,                  // ID 2
                0,                    // ID 3
                SymbolReaderNativeMethods.SSRVOPT_GUIDPTR,  // Flags
                pdbFullPath,    // output FilePath
                null,           // Callback 
                IntPtr.Zero);   // Context for callback 

            sw.Stop();
            int statusCode = 0;
            if (!foundPDB)
                statusCode = Marshal.GetLastWin32Error();

            var status = statusCode == 0 ? "SUCCESS" : "FAIL";
            log.Write(" <FindSymbolFilePath Status=\"{0}\" Name=\"{1}\"", status,  XmlUtilities.XmlEscape(pdbSimpleName));
            if (!string.IsNullOrEmpty(fileVersion))
                log.Write("   FileVersion=\"{0}\"", fileVersion);
            log.WriteLine();
            log.WriteLine("  Guid=\"{0}\" PdbAge=\"{1}\" DurationSec=\"{2:f2}\" StatusCode=\"{3}\">", 
                pdbGuid, pdbAge, sw.Elapsed.TotalSeconds, statusCode);
            var msg = XmlUtilities.XmlEscape(messages.ToString().TrimEnd().Replace("\n", "\n    "));
            log.WriteLine("    " + msg);
            log.WriteLine(" </FindSymbolFilePath>");

            if (statusCode != 0)
                return null;
            return pdbFullPath.ToString();
        }
        /// <summary>
        /// Given the path name to a particular PDB file, load it so that you can resolve symbols in it.  
        /// </summary>
        /// <param name="symbolFilePath">The name of the PDB file to open.</param>
        /// <param name="moduleImageBase">The image base that the cooresponding DLL is expected to be loaded at.  Must be unique for all load PDBs. </param>
        /// <returns>The SymbolReaderModule that represents the information in the symbol file (PDB)</returns>
        public SymbolReaderModule OpenSymbolFile(string symbolFilePath, Address moduleImageBase)
        {
            return new SymbolReaderModule(this, symbolFilePath, moduleImageBase);
        }
        public void Dispose()
        {
            if (currentProcessHandle != IntPtr.Zero)
            {
                // Can't do this in the finalizer as the handle may not be valid then.  
                SymbolReaderNativeMethods.SymCleanup(currentProcessHandle);
                currentProcessHandle = IntPtr.Zero;
                currentProcess.Close();
                currentProcess = null;
                callback = null;
                messages = null;
            }
        }
        #region private
        ~SymbolReader()
        {
            /*** TODO remove
            if (buffer != null)
            {
                Marshal.FreeHGlobal((IntPtr)buffer);
                buffer = null;
            }
             * ***/
        }

        private bool StatusCallback(
            IntPtr hProcess,
            SymbolReaderNativeMethods.SymCallbackActions ActionCode,
            ulong UserData,
            ulong UserContext)
        {
            bool ret = false;
            switch (ActionCode)
            {
                case SymbolReaderNativeMethods.SymCallbackActions.CBA_DEBUG_INFO:
                    lastLine = new String((char*)UserData).Trim();
                    messages.Append(lastLine).AppendLine();
                    ret = true;
                    break;
                default:
                    // messages.Append("STATUS: Code=").Append(ActionCode).AppendLine();
                    break;
            }
            return ret;
        }

        internal Process currentProcess;      // keep to insure currentProcessHandle stays alive
        internal IntPtr currentProcessHandle; // TODO really need to get on safe handle plan 
        internal string lastLine;
        internal StringBuilder messages;
        internal SymbolReaderNativeMethods.SymRegisterCallbackProc callback;
        internal TextWriter log;

        // TODO put these in SymbolReaderModule
        // internal SymbolReaderNativeMethods.SYMBOL_INFO* symbolInfo;
        // internal byte* buffer;
        internal SymbolReaderNativeMethods.IMAGEHLP_LINE64 lineInfo;
        #endregion
    }

    public unsafe class SymbolReaderModule : IDisposable
    {
        public string FindSymbolForAddress(Address address, out Address endOfSymbol)
        {
            int amountLeft;
            int rva = MapToOriginalRva(address, out amountLeft);

            if (syms.Count > 0 && syms[0].StartRVA <= rva)
            {
                int high = syms.Count;
                int low = 0;
                for (; ; )
                {
                    int mid = (low + high) / 2;
                    if (mid == low)
                        break;
                    if (syms[mid].StartRVA <= rva)
                        low = mid;
                    else
                        high = mid;
                }
                Debug.Assert(low < syms.Count);
                Debug.Assert(low + 1 == high);
                Debug.Assert(syms[low].StartRVA <= rva);
                Debug.Assert(low >= syms.Count - 1 || rva < syms[low + 1].StartRVA);
                Sym sym = syms[low];
                if (sym.Size == 0)
                {
                    endOfSymbol = (Address)((long)address + amountLeft);
                    return sym.MethodName;
                }
                if (rva < sym.StartRVA + sym.Size)
                {
                    int symbolLeft = (int)sym.Size - (rva - sym.StartRVA);
                    amountLeft = Math.Min(symbolLeft, amountLeft);
                    Debug.Assert(0 <= amountLeft && amountLeft < 0x10000);      // Not true but useful for unit testing
                    endOfSymbol = (Address)((long)address + amountLeft);
                    return sym.MethodName;
                }
                // log.WriteLine("No match");
            }
            endOfSymbol = Address.Null;
            return "";
        }
        public void FindSourceLineForAddress(Address address, out int lineNum, ref string sourceFile)
        {
            int displacement = 0;
            if (!SymbolReaderNativeMethods.SymGetLineFromAddrW64(reader.currentProcessHandle, (ulong)address, ref displacement, ref reader.lineInfo))
            {
                lineNum = 0;
                sourceFile = "";
                return;
            }
            lineNum = (int)reader.lineInfo.LineNumber;

            // Try to reuse the source file name as much as we can.  Don't create a new string unless we
            // have to. 
            for (int i = 0; ; i++)
            {
                if (reader.lineInfo.FileName[i] == 0 && i == sourceFile.Length)
                    return;
                if (i >= sourceFile.Length)
                    break;
                if (reader.lineInfo.FileName[i] != sourceFile[i])
                    break;
            }
            sourceFile = new String((char*)reader.lineInfo.FileName);
        }
        public void Dispose()
        {
            if (!SymbolReaderNativeMethods.SymUnloadModule64(reader.currentProcessHandle, (ulong)moduleImageBase))
            {
#if DEBUG
                reader.log.WriteLine("Error unloading module with image base " + ((ulong)moduleImageBase).ToString("x"));
#endif
            }
        }

        #region private
        internal SymbolReaderModule(SymbolReader reader, string moduleFilepath, Address imageBase)
        {
            this.reader = reader;
            reader.messages.Length = 0;
            reader.lastLine = "";
            ulong imageBaseRet = SymbolReaderNativeMethods.SymLoadModuleExW(reader.currentProcessHandle, IntPtr.Zero,
                moduleFilepath, null, (ulong)imageBase, 0, null, 0);

            if (imageBaseRet == 0)
                throw new Exception("Fatal error loading symbols for " + moduleFilepath);
            this.moduleImageBase = (Address)imageBaseRet;
            Debug.Assert(moduleImageBase == imageBase);

            if (reader.lastLine.IndexOf(" no symbols") >= 0 || reader.lastLine.IndexOf(" export symbols") >= 0)
                throw new Exception(
                    "   Could not find PDB file for " + moduleFilepath + "\r\n" +
                    "   Detailed Diagnostic information.\r\n" +
                    "      " + reader.messages.ToString().Replace("\n", "\r\n      "));
            if (reader.lastLine.IndexOf(" public symbols") >= 0)
                reader.log.WriteLine("Loaded only public symbols.");

            // See if we have an object file map (created by BBT)
            SymbolReaderNativeMethods.OMAP* fromMap = null;
            toMap = null;
            ulong toMapCount = 0;
            ulong fromMapCount = 0;
            if (!SymbolReaderNativeMethods.SymGetOmaps(reader.currentProcessHandle, (ulong)moduleImageBase, ref toMap, ref toMapCount, ref fromMap, ref fromMapCount))
                reader.log.WriteLine("No Object maps found");
            this.toMapCount = (int)toMapCount;
            if (toMapCount <= 0)
                toMap = null;

            /*
            log.WriteLine("Got ToMap");
            for (int count = 0; count < (int) toMapCount; count++)
                Console.WriteLine("Rva {0:x} -> {1:x}", toMap[count].rva, toMap[count].rvaTo);
            log.WriteLine("Got FromMap");
            for (int count = 0; count < (int) toMapCount; count++)
                log.WriteLine("Rva {0:x} -> {1:x}", toMap[count].rva, toMap[count].rvaTo);
            */

            syms = new List<Sym>(5000);
            SymbolReaderNativeMethods.SymEnumSymbolsW(reader.currentProcessHandle, (ulong)moduleImageBase, "*",
                delegate(SymbolReaderNativeMethods.SYMBOL_INFO* symbolInfo, uint SymbolSize, IntPtr UserContext)
                {
                    int amountLeft;
                    int mappedRVA = MapToOriginalRva((Address)symbolInfo->Address, out amountLeft);
                    if (mappedRVA == 0)
                        return true;
                    Sym sym = new Sym();
                    sym.MethodName = new String((char*)(&symbolInfo->Name));
                    sym.StartRVA = mappedRVA;
                    sym.Size = (int)symbolInfo->Size;
                    syms.Add(sym);
                    return true;
                }, IntPtr.Zero);

            reader.log.WriteLine("Got {0} symbols and {1} mappings ", syms.Count, toMapCount);
            syms.Sort(delegate(Sym x, Sym y) { return x.StartRVA - y.StartRVA; });
        }
        /// <summary>
        /// BBT splits up methods into many chunks.  Map the final rva of a symbol back into its
        /// pre-BBTed rva.  
        /// </summary>
        private int MapToOriginalRva(Address finalAddress, out int amountLeft)
        {
            int rva = (int)(finalAddress - moduleImageBase);
            if (toMap == null || rva < toMap[0].rva)
            {
                amountLeft = 0;
                return rva;
            };
            Debug.Assert(toMapCount > 0);
            int high = toMapCount;
            int low = 0;

            // Invarient toMap[low]rva <= rva < toMap[high].rva (or high == toMapCount)
            for (; ; )
            {
                int mid = (low + high) / 2;
                if (mid == low)
                    break;
                if (toMap[mid].rva <= rva)
                    low = mid;
                else
                    high = mid;
            }
            Debug.Assert(toMap[low].rva <= rva);
            Debug.Assert(low < toMapCount);
            Debug.Assert(low + 1 == high);
            Debug.Assert(toMap[low].rva <= rva && (low >= toMapCount - 1 || rva < toMap[low + 1].rva));

            if (low + 1 < toMapCount)
                amountLeft = toMap[low + 1].rva - rva;
            else
                amountLeft = 0;
            int diff = rva - toMap[low].rva;

            int ret = toMap[low].rvaTo + diff;
#if false
            int slowAmountLeft;
            int slowRet = MapToOriginalRvaSlow(finalAddress, out slowAmountLeft);
            Debug.Assert(slowRet == ret);
            Debug.Assert(slowAmountLeft == amountLeft);
#endif

            return ret;
        }

#if DEBUG
        private int MapToOriginalRvaSlow(Address finalAddress, out int amountLeft)
        {
            int rva = (int)(finalAddress - moduleImageBase);
            Debug.Assert(toMapCount > 0);
            if (toMap == null || rva < toMap[0].rva)
            {
                amountLeft = 0;
                return rva;
            };

            int i = 0;
            for (; ; )
            {
                if (i >= toMapCount)
                {
                    amountLeft = 0;
                    return toMap[i - 1].rvaTo + (rva - toMap[i - 1].rva);
                }
                if (rva < toMap[i].rva)
                {
                    amountLeft = toMap[i].rva - rva;
                    if (i != 0)
                    {
                        --i;
                        rva = toMap[i].rvaTo + (rva - toMap[i].rva);
                    }
                    return rva;
                }
                i++;
            }
        }
#endif
        class Sym
        {
            public int StartRVA;
            public int Size;
            public string MethodName;
        };

        SymbolReader reader;
        Address moduleImageBase;
        SymbolReaderNativeMethods.OMAP* toMap;
        int toMapCount;
        List<Sym> syms;
        #endregion
    }

    /// <summary>
    /// SymPath is a class that knows how to parse _NT_SYMBOL_PATH syntax.  
    /// </summary>
    public class SymPath
    {
        public static string _NT_SYMBOL_PATH
        {
            get
            {
                var ret = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
                if (ret == null)
                    ret = "";
                return ret;
            }
            set
            {
                Environment.SetEnvironmentVariable("_NT_SYMBOL_PATH", value);
            }
        }
        public static SymPath Clean_NT_SYMBOL_PATH(bool localSymbolsOnly, TextWriter log, string defaultSymCache = null)
        {
            string symPathStr = _NT_SYMBOL_PATH;
            if (symPathStr.Length == 0)
            {
                if (ComputerNameExists("symweb"))
                    symPathStr = "SRV*http://symweb";   // Internal Microsoft location.  
                else
                    symPathStr = "SRV*http://msdl.microsoft.com/download/symbols";
            }

            if (defaultSymCache == null)
            {
                string temp = Environment.GetEnvironmentVariable("TEMP");
                if (temp == null)
                    temp = ".";
                defaultSymCache = Path.Combine(temp, "symbols");
            }

            var symPath = new SymPath(symPathStr);
            symPath = symPath.InsureHasCache(defaultSymCache).CacheFirst();
            if (localSymbolsOnly)
                symPath = symPath.LocalOnly();

            var newSymPathStr = symPath.ToString();
            if (newSymPathStr != symPathStr)
                _NT_SYMBOL_PATH = newSymPathStr;
            return symPath;
        }

        public SymPath()
        {
            m_elements = new SortedDictionary<SymPathElement, SymPathElement>();
        }
        public SymPath(string path)
            : this()
        {
            Add(path);
        }
        public ICollection<SymPathElement> Elements
        {
            get { return m_elements.Keys; }
        }

        public void Add(string path)
        {
            var strElems = path.Split(';');
            foreach (var strElem in strElems)
                Add(new SymPathElement(strElem));
        }
        public void Add(SymPath path)
        {
            foreach (var elem in path.Elements)
                Add(elem);
        }
        public void Add(SymPathElement elem)
        {
            if (elem != null)
                m_elements[elem] = elem;
        }

        /// <summary>
        /// People can use symbol servers without a local cache.  This is bad, add one if necessary. 
        /// </summary>
        public SymPath InsureHasCache(string defaultCachePath)
        {
            var ret = new SymPath();
            foreach (var elem in Elements)
                ret.Add(elem.InsureHasCache(defaultCachePath));
            return ret;
        }
        /// <summary>
        /// Removes all references to remote paths.  This insures that network issues don't cause grief.  
        /// </summary>
        public SymPath LocalOnly()
        {
            var ret = new SymPath();
            foreach (var elem in Elements)
                ret.Add(elem.LocalOnly());
            return ret;
        }
        public SymPath CacheFirst()
        {
            var ret = new SymPath();
            foreach (var elem in Elements)
            {
                if (elem.IsSymServer && elem.IsRemote)
                    ret.Add(elem.LocalOnly());
                ret.Add(elem);
            }
            return ret;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            bool first = true;
            foreach (var elem in Elements)
            {
                if (!first)
                    sb.Append(";");
                first = false;
                sb.Append(elem.ToString());
            }
            return sb.ToString();
        }
        public void ToXml(TextWriter log, string indent)
        {
            log.WriteLine("{0}<SymbolPaths>", indent);
            foreach (var elem in Elements)
                log.WriteLine("  <SymbolPath Value=\"{0}\"/>", XmlUtilities.XmlEscape(elem.ToString()));
            log.WriteLine("{0}</SymbolPaths>", indent);
        }
        #region private
        private SortedDictionary<SymPathElement, SymPathElement> m_elements;

        /// <summary>
        /// Checks to see 'computerName' exists (there is a Domain Names Service (DNS) reply to it)
        /// This routine times out quickly (after 700 msec).  
        /// </summary>
        private static bool ComputerNameExists(string computerName, int timeoutMSec=700)
        {
            if (computerName == s_lastComputerNameLookupFailure)
                return false;
            try
            {
                System.Net.IPHostEntry ipEntry = null;
                var result = System.Net.Dns.BeginGetHostEntry(computerName, null, null);
                // var result = System.Net.Dns.BeginGetHostEntry("symweb", null, null);
                if (result.AsyncWaitHandle.WaitOne(timeoutMSec))
                    ipEntry = System.Net.Dns.EndGetHostEntry(result);
                if (ipEntry != null)
                    return true;
            }
            catch (Exception) { }
            s_lastComputerNameLookupFailure = computerName;
            return false;
        }
        private static string s_lastComputerNameLookupFailure = "";

        #endregion
    }
    /// <summary>
    /// SymPathElement is a part of code:SymPath 
    /// </summary>
    public class SymPathElement : IComparable<SymPathElement>
    {
        // SymPathElement follows functional conventions.  After construction everything is read-only. 
        public bool IsSymServer { get; private set; }
        public string Cache { get; private set; }
        public string Target { get; private set; }

        public bool IsRemote
        {
            get
            {
                if (!IsSymServer)
                    return false;
                if (Cache != null)
                    return true;
                if (Target == null)
                    return false;
                if (Target.StartsWith(@"\\"))
                    return true;
                if (Target.StartsWith("http:/", StringComparison.OrdinalIgnoreCase))
                    return true;
                return false;
            }
        }
        public SymPathElement InsureHasCache(string defaultCachePath)
        {
            if (!IsSymServer)
                return this;
            if (Cache != null)
                return this;
            return new SymPathElement(true, defaultCachePath, Target);
        }
        public SymPathElement LocalOnly()
        {
            if (!IsRemote)
                return this;
            if (Cache != null)
                return new SymPathElement(true, Cache, null);
            return null;
        }

        public int CompareTo(SymPathElement other)
        {
            // Non-Remote nodes come first. 
            var ret = IsRemote.CompareTo(other.IsRemote);
            if (ret != 0)
                return ret;

            // Sort alphabetically by target otherwise. 
            if (Target == null)
                return other.Target == null ? 0 : -1;
            else
                if (other.Target == null)
                    return 1;
                else
                    return Target.CompareTo(other.Target);
        }
        public override string ToString()
        {
            if (IsSymServer)
            {
                var ret = "SRV";
                if (Cache != null)
                    ret += "*" + Cache;
                if (Target != null)
                    ret += "*" + Target;
                return ret;
            }
            else
                return Target;
        }
        #region private
        internal SymPathElement(bool isSymServer, string cache, string target)
        {
            IsSymServer = isSymServer;
            Cache = cache;
            Target = target;
        }
        internal SymPathElement(string strElem)
        {
            var m = Regex.Match(strElem, @"^\s*SRV\*((\s*.*?\s*)\*)?\s*(.*?)\s*$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                IsSymServer = true;
                Cache = m.Groups[2].Value;
                Target = m.Groups[3].Value;
                if (Cache.Length == 0)
                    Cache = null;
                if (Target.Length == 0)
                    Target = null;
            }
            else
                Target = strElem.Trim();
        }
        #endregion
    }

    #region private classes
    internal unsafe class SymbolReaderNativeMethods
    {
        #region symbol lookup
        /**
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern int GetCurrentProcessId();

        [DllImport("kernel32.dll",  SetLastError = true)]
        public static extern IntPtr OpenProcess(int access, bool inherit, int processID);
        **/

        internal const int SSRVOPT_DWORD = 0x0002;
        internal const int SSRVOPT_DWORDPTR = 0x004;
        internal const int SSRVOPT_GUIDPTR = 0x0008;

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymFindFileInPathW(
            IntPtr hProcess,
            string searchPath,
            [MarshalAs(UnmanagedType.LPWStr), In] string fileName,
            ref Guid id,
            int two,
            int three,
            int flags,
            [Out]System.Text.StringBuilder filepath,
            SymFindFileInPathProc findCallback,
            IntPtr context // void*
            );

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymFindFileInPathW(
            IntPtr hProcess,
            string searchPath,
            [MarshalAs(UnmanagedType.LPWStr), In] string fileName,
            int id,
            int two,
            int three,
            int flags,
            [Out]System.Text.StringBuilder filepath,
            SymFindFileInPathProc findCallback,
            IntPtr context // void*
            );

        [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymFindFileInPath(
            IntPtr hProcess,
            string searchPath,
            string fileName,
            IntPtr id, //void*
            int two,
            int three,
            int flags,
            out string filepath,
            SymFindFileInPathProc findCallback,
            IntPtr context // void*
            );


        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymInitializeW(
            IntPtr hProcess,
            string UserSearchPath,
            [MarshalAs(UnmanagedType.Bool)] bool fInvadeProcess);

        [DllImport("dbghelp.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymCleanup(
            IntPtr hProcess);


        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymEnumSymbolsW(
            IntPtr hProcess,
            ulong BaseOfDll,
            string Mask,
            SymEnumSymbolsProc EnumSymbolsCallback,
            IntPtr UserContext);

        // TODO: unicode version of this does not work
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        internal static extern ulong SymLoadModuleExW(
            IntPtr hProcess,
            IntPtr hFile,
            string ImageName,
            string ModuleName,
            ulong BaseOfDll,
            uint DllSize,
            void* Data,
            uint Flags
         );

        [DllImport("dbghelp.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymUnloadModule64(
            IntPtr hProcess,
            ulong BaseOfDll);

        /***
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymFromAddrW(
            IntPtr hProcess,
            ulong Address,
            ref ulong Displacement,
            SYMBOL_INFO* Symbol
        );
         ****/

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymGetLineFromAddrW64(
            IntPtr hProcess,
            ulong Address,
            ref Int32 Displacement,
            ref IMAGEHLP_LINE64 Line
        );

        // Some structures used by the callback 
        internal struct IMAGEHLP_CBA_EVENT
        {
            public int Severity;
            public char* pStrDesc;
            public void* pData;

        }
        internal struct IMAGEHLP_DEFERRED_SYMBOL_LOAD64
        {
            public int SizeOfStruct;
            public Int64 BaseOfImage;
            public int CheckSum;
            public int TimeDateStamp;
            public fixed sbyte FileName[MAX_PATH];
            public bool Reparse;
            public void* hFile;
            public int Flags;
        }

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymRegisterCallbackW64(
            IntPtr hProcess,
            SymRegisterCallbackProc callBack,
            ulong UserContext);

        internal delegate bool SymRegisterCallbackProc(
            IntPtr hProcess,
            SymCallbackActions ActionCode,
            ulong UserData,
            ulong UserContext);


        [Flags]
        public enum SymCallbackActions
        {
            CBA_DEBUG_INFO = 0x10000000,
            CBA_DEFERRED_SYMBOL_LOAD_CANCEL = 0x00000007,
            CBA_DEFERRED_SYMBOL_LOAD_COMPLETE = 0x00000002,
            CBA_DEFERRED_SYMBOL_LOAD_FAILURE = 0x00000003,
            CBA_DEFERRED_SYMBOL_LOAD_PARTIAL = 0x00000020,
            CBA_DEFERRED_SYMBOL_LOAD_START = 0x00000001,
            CBA_DUPLICATE_SYMBOL = 0x00000005,
            CBA_EVENT = 0x00000010,
            CBA_READ_MEMORY = 0x00000006,
            CBA_SET_OPTIONS = 0x00000008,
            CBA_SRCSRV_EVENT = 0x40000000,
            CBA_SRCSRV_INFO = 0x20000000,
            CBA_SYMBOLS_UNLOADED = 0x00000004,
        }

        [Flags]
        public enum SymOptions : uint
        {
            SYMOPT_ALLOW_ABSOLUTE_SYMBOLS = 0x00000800,
            SYMOPT_ALLOW_ZERO_ADDRESS = 0x01000000,
            SYMOPT_AUTO_PUBLICS = 0x00010000,
            SYMOPT_CASE_INSENSITIVE = 0x00000001,
            SYMOPT_DEBUG = 0x80000000,
            SYMOPT_DEFERRED_LOADS = 0x00000004,
            SYMOPT_DISABLE_SYMSRV_AUTODETECT = 0x02000000,
            SYMOPT_EXACT_SYMBOLS = 0x00000400,
            SYMOPT_FAIL_CRITICAL_ERRORS = 0x00000200,
            SYMOPT_FAVOR_COMPRESSED = 0x00800000,
            SYMOPT_FLAT_DIRECTORY = 0x00400000,
            SYMOPT_IGNORE_CVREC = 0x00000080,
            SYMOPT_IGNORE_IMAGEDIR = 0x00200000,
            SYMOPT_IGNORE_NT_SYMPATH = 0x00001000,
            SYMOPT_INCLUDE_32BIT_MODULES = 0x00002000,
            SYMOPT_LOAD_ANYTHING = 0x00000040,
            SYMOPT_LOAD_LINES = 0x00000010,
            SYMOPT_NO_CPP = 0x00000008,
            SYMOPT_NO_IMAGE_SEARCH = 0x00020000,
            SYMOPT_NO_PROMPTS = 0x00080000,
            SYMOPT_NO_PUBLICS = 0x00008000,
            SYMOPT_NO_UNQUALIFIED_LOADS = 0x00000100,
            SYMOPT_OVERWRITE = 0x00100000,
            SYMOPT_PUBLICS_ONLY = 0x00004000,
            SYMOPT_SECURE = 0x00040000,
            SYMOPT_UNDNAME = 0x00000002,
        };

        [DllImport("dbghelp.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        internal static extern SymOptions SymSetOptions(
            SymOptions SymOptions
            );

        [DllImport("dbghelp.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        internal static extern SymOptions SymGetOptions();

        internal delegate bool SymEnumSymbolsProc(
            SYMBOL_INFO* pSymInfo,
            uint SymbolSize,
            IntPtr UserContext);
        internal delegate bool SymFindFileInPathProc(string fileName,
                                                       IntPtr context);
        internal delegate bool SymEnumLinesProc(
            SRCCODEINFO* LineInfo,
            IntPtr UserContext);

        internal struct SYMBOL_INFO
        {
            public UInt32 SizeOfStruct;
            public UInt32 TypeIndex;
            public UInt64 Reserved1;
            public UInt64 Reserved2;
            public UInt32 Index;
            public UInt32 Size;
            public UInt64 ModBase;
            public UInt32 Flags;
            public UInt64 Value;
            public UInt64 Address;
            public UInt32 Register;
            public UInt32 Scope;
            public UInt32 Tag;
            public UInt32 NameLen;
            public UInt32 MaxNameLen;
            public byte Name;           // Actually of variable size Unicode string
        };

        internal struct IMAGEHLP_LINE64
        {
            public UInt32 SizeOfStruct;
            public void* Key;
            public UInt32 LineNumber;
            public byte* FileName;             // pointer to character string. 
            public UInt64 Address;
        };

        internal const int MAX_PATH = 260;
        internal const int DSLFLAG_MISMATCHED_DBG = 0x2;
        internal const int DSLFLAG_MISMATCHED_PDB = 0x1;

        internal struct SRCCODEINFO
        {
            public UInt32 SizeOfStruct;
            public void* Key;
            public UInt64 ModBase;
            public InlineAsciiString Obj;
            public InlineAsciiString FileName;
            public UInt32 LineNumber;
            public UInt64 Address;
        };

        // Tools like BBT rearrange the image but not the PDB instead they simply append a mapping
        // structure at the end that allows the reader of the PDB to map old address to the new address
        [DllImport("dbghelp.dll", SetLastError = true), SuppressUnmanagedCodeSecurityAttribute]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SymGetOmaps(
            IntPtr hProcess,
            ulong BaseOfDll,
            ref OMAP* OmapTo,
            ref ulong cOmapTo,
            ref OMAP* OmapFrom,
            ref ulong cOmapFrom);

        internal struct OMAP
        {
            public int rva;
            public int rvaTo;
        };

        [StructLayout(LayoutKind.Explicit, Size = MAX_PATH + 1)]
        internal struct InlineAsciiString { }

        #endregion
    }
    #endregion
}
