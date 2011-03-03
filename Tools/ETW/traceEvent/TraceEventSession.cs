//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.Collections.Generic;
using System.Security;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Diagnostics;
using Diagnostics.Eventing;
using System.IO;

// TraceEventSession defintions See code:#Introduction to get started.
namespace Diagnostics.Eventing
{
    /// <summary>
    /// #Introduction 
    /// 
    /// A TraceEventSession represents a single ETW Tracing Session (something that logs a
    /// single output moduleFile). Every ETL output moduleFile has exactly one session assoicated with it,
    /// although you can have 'real time' sessions that have no output file and you can connect to
    /// 'directly' to get events without ever creating a file. You signify this simply by passing
    /// 'null' as the name of the file. You extract data from these 'real time' sources by specifying
    /// the session name to the constructor of code:ETWTraceEventSource). Sessions are MACHINE WIDE and can
    /// OUTLIVE the process that creates them. This it takes some care to insure that sessions are cleaned up
    /// in all cases.
    /// 
    /// Code that generated ETW events are called Providers. The Kernel has a provider (and it is often the
    /// most intersting) but other components are free to use public OS APIs (eg WriteEvent), to create
    /// user-mode providers. Each Provider is given a GUID that is used to identify it. You can get a list of
    /// all providers on the system as well as their GUIDs by typing the command
    /// 
    ///             logman query providers
    ///             
    /// The basic model is that you start a session (which creates a ETL moduleFile), and then you call
    /// code:TraceEventSession.EnableProvider on it to add all the providers (event sources), that you are
    /// interested in. A session is given a name (which is MACHINE WIDE), so that you can connect back up to
    /// it from another process (since it might outlive the process that created it), so you can modify it or
    /// (more commonly) close the session down later from another process.
    /// 
    /// For implementation reasons, this is only one Kernel provider and it can only be specified in a
    /// special 'Kernel Mode' session. There can be only one kernel mode session (MACHINE WIDE) and it is
    /// distinguished by a special name 'NT Kernel Logger'. The framework allows you to pass flags to the
    /// provider to control it and the Kernel provider uses these bits to indicate which particular events
    /// are of interest. Because of these restrictions, you often need two sessions, one for the kernel
    /// events and one for all user-mode events.
    /// 
    /// Sample use. Enabling the Kernel's DLL image logging to the moduleFile output.etl
    /// 
    ///  * TraceEventSession session = new TraceEventSession(, KernelTraceEventParser.Keywords.ImageLoad); 
    ///  * Run you scenario 
    ///  * session.Close(); 
    /// 
    /// Once the scenario is complete, you use the code:TraceEventSession.Close methodIndex to shut down a
    /// session. You can also use the code:TraceEventSession.GetActiveSessionNames to get a list of all
    /// currently running session on the machine (in case you forgot to close them!).
    /// 
    /// When the sesion is closed, you can use the code:ETWTraceEventSource to parse the events in the ETL
    /// moduleFile.  Alternatively, you can use code:TraceLog.CreateFromETL to convert the ETL file into an ETLX file. 
    /// Once it is an ETLX file you have a much richer set of processing options availabe from code:TraceLog. 
    /// </summary>
    [SecuritySafeCritical]
    unsafe public sealed class TraceEventSession : IDisposable
    {
        /// <summary>
        /// Create a new logging session.
        /// </summary>
        /// <param name="sessionName">
        /// The name of the session. Since session can exist beyond the lifetime of the process this name is
        /// used to refer to the session from other threads.
        /// </param>
        /// <param name="fileName">
        /// The output moduleFile (by convention .ETL) to put the event data. If this parameter is null, it means
        /// that the data is 'real time' (stored in the session memory itself)
        /// </param>
        public TraceEventSession(string sessionName, string fileName)
        {
            this.m_BufferSizeMB = 40;       // The default size.  
            this.m_SessionHandle = TraceEventNativeMethods.INVALID_HANDLE_VALUE;
            this.m_FileName = fileName;               // filename = null means real time session
            this.m_SessionName = sessionName;
            this.m_Create = true;
        }
        /// <summary>
        /// Open an existing Windows Event Tracing Session, with name 'sessionName'. To create a new session,
        /// use TraceEventSession(string, string)
        /// </summary>
        /// <param name="sessionName"> The name of the session to open (see GetActiveSessionNames)</param>
        public TraceEventSession(string sessionName)
        {
            this.m_SessionHandle = TraceEventNativeMethods.INVALID_HANDLE_VALUE;
            this.m_SessionName = sessionName;

            // Get the filename
            var propertiesBuff = stackalloc byte[PropertiesSize];
            var properties = GetProperties(propertiesBuff);
            int hr = TraceEventNativeMethods.ControlTrace(0UL, sessionName, properties, TraceEventNativeMethods.EVENT_TRACE_CONTROL_QUERY);
            if (hr == 4201)     // Instance name not found.  This means we did not start
                throw new FileNotFoundException("The session " + sessionName + " is not active.");  // Not really a file, but not bad. 
            Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
            this.m_FileName = new string((char*)(((byte*)properties) + properties->LogFileNameOffset));
            this.m_BufferSizeMB = (int)properties->MinimumBuffers;
            if ((properties->LogFileMode & TraceEventNativeMethods.EVENT_TRACE_FILE_MODE_CIRCULAR) != 0)
                m_CircularBufferMB = (int)properties->MaximumFileSize;
        }

        /// <summary>
        /// Start a kernel session (name is required to be NT Kernel Logger) and turn on 'eventsToEnable' events 
        /// and 'eventStacksToEnable' stacks.  
        /// </summary>
        public TraceEventSession(string fileName, 
            KernelTraceEventParser.Keywords eventsToEnable, 
            KernelTraceEventParser.Keywords eventStacksToEnable) : this(KernelTraceEventParser.KernelSessionName, fileName)
        {
            EnableKernelProvider(eventsToEnable, eventStacksToEnable);
        }

        public bool EnableKernelProvider(KernelTraceEventParser.Keywords flags)
        {
            return EnableKernelProvider(flags, KernelTraceEventParser.Keywords.None);
        }
        /// <summary>
        /// #EnableKernelProvider
        /// Enable the kernel provider for the session. If the session must be called 'NT Kernel Session'.   
        /// <param name="flags">
        /// Specifies the particular kernel events of interest</param>
        /// <param name="stackCapture">
        /// Specifies which events should have their eventToStack traces captured too (VISTA+ only)</param>
        /// <returns>Returns true if the session had existed before and is now restarted</returns>
        /// </summary>
        public unsafe bool EnableKernelProvider(KernelTraceEventParser.Keywords flags, KernelTraceEventParser.Keywords stackCapture)
        {
            if (m_SessionName != KernelTraceEventParser.KernelSessionName)
                throw new Exception("Cannot enable kernel events to a real time session unless it is named " + KernelTraceEventParser.KernelSessionName);
            if (m_SessionHandle != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                throw new Exception("The kernel provider must be enabled as the only provider.");
            if (Environment.OSVersion.Version.Major < 6)
                throw new NotSupportedException("Kernel Event Tracing is only supported on Windows 6.0 (Vista) and above.");

            var propertiesBuff = stackalloc byte[PropertiesSize];
            var properties = GetProperties(propertiesBuff);
            properties->Wnode.Guid = KernelTraceEventParser.ProviderGuid;

            // Initialize the stack collecting information
            const int stackTracingIdsMax = 96;
            int numIDs = 0;
            var stackTracingIds = stackalloc TraceEventNativeMethods.STACK_TRACING_EVENT_ID[stackTracingIdsMax];
#if DEBUG
            // Try setting all flags, if we overflow an assert in SetStackTraceIds will fire.  
            SetStackTraceIds((KernelTraceEventParser.Keywords)(-1), stackTracingIds, stackTracingIdsMax);
#endif
            if (stackCapture != KernelTraceEventParser.Keywords.None)
                numIDs = SetStackTraceIds(stackCapture, stackTracingIds, stackTracingIdsMax);

            // The Profile event requires the SeSystemProfilePrivilege to succeed, so set it.  
            if ((flags & KernelTraceEventParser.Keywords.Profile) != 0)
                TraceEventNativeMethods.SetSystemProfilePrivilege();

            bool ret = false;
            properties->EnableFlags = (uint)flags;
            int dwErr;
            try
            {
                dwErr = TraceEventNativeMethods.StartKernelTrace(out m_SessionHandle, properties, stackTracingIds, numIDs);
                if (dwErr == 0xB7) // STIERR_HANDLEEXISTS
                {
                    ret = true;
                    Stop();
                    m_Stopped = false;
                    Thread.Sleep(100);  // Give it some time to stop. 
                    dwErr = TraceEventNativeMethods.StartKernelTrace(out m_SessionHandle, properties, stackTracingIds, numIDs);
                }
            }
            catch (BadImageFormatException)
            {
                // We use a small native DLL called KernelTraceControl that needs to be 
                // in the same directory as the EXE that used TraceEvent.dll.  Unlike IL
                // Native DLLs are specific to a processor type (32 or 64 bit) so the easiest
                // way to insure this is that the EXE that uses TraceEvent is built for 32 bit
                // and that you use the 32 bit version of KernelTraceControl.dll
                throw new BadImageFormatException("Could not load KernelTraceControl.dll (likely 32-64 bit process mismatch)");
            }
            catch (DllNotFoundException)
            {
                // In order to start kernel session, we need a support DLL called KernelTraceControl.dll
                // This DLL is available by downloading the XPERF.exe tool (see 
                // http://msdn.microsoft.com/en-us/performance/cc825801.aspx for instructions)
                // It is recommended that you get the 32 bit version of this (it works on 64 bit machines)
                // and build your EXE that uses TraceEvent to launch as a 32 bit application (This is
                // the default for VS 2010 projects).  
                throw new DllNotFoundException("KernelTraceControl.dll missing from distribution.");
            }
            if (dwErr == 5 && Environment.OSVersion.Version.Major > 5)      // On Vista and we get a 'Accessed Denied' message
                throw new UnauthorizedAccessException("Error Starting ETW:  Access Denied (Administrator rights required to start ETW)");
            Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(dwErr));
            return ret;
        }
        public bool EnableProvider(Guid providerGuid, TraceEventLevel providerLevel)
        {
            return EnableProvider(providerGuid, providerLevel, 0, 0, null);
        }
        public bool EnableProvider(Guid providerGuid, TraceEventLevel providerLevel, ulong matchAnyKeywords)
        {
            return EnableProvider(providerGuid, providerLevel, matchAnyKeywords, 0, null);
        }
        /// <summary>
        /// Add an additional USER MODE provider prepresented by 'providerGuid' (a list of
        /// providers is available by using 'logman query providers').
        /// </summary>
        /// <param name="providerGuid">
        /// The GUID that represents the event provider to turn on. Use 'logman query providers' or
        /// for a list of possible providers. Note that additional user mode (but not kernel mode)
        /// providers can be added to the session by using EnableProvider.</param>
        /// <param name="providerLevel">The verbosity to turn on</param>
        /// <param name="matchAnyKeywords">A bitvector representing the areas to turn on. Only the
        /// low 32 bits are used by classic providers and passed as the 'flags' value.  Zero
        /// is a special value which is a provider defined default, which is usuall 'everything'</param>
        /// <param name="matchAllKeywords">A bitvector representing keywords of an event that must
        /// be on for a particular event for the event to be logged.  A value of zero means
        /// that no keyword must be on, which effectively ignores this value.  </param>
        /// <param name="values">This is set of key-value strings that are passed to the provider
        /// for provider-specific interpretation. Can be null if no additional args are needed.</param>
        /// <returns>true if the session already existed and needed to be restarted.</returns>
        public bool EnableProvider(Guid providerGuid, TraceEventLevel providerLevel, ulong matchAnyKeywords, ulong matchAllKeywords, IEnumerable<KeyValuePair<string, string>> values)
        {
            byte[] valueData = null;
            int valueDataSize = 0;
            int valueDataType = 0;
            if (values != null)
            {
                valueDataType = 0; // ControllerCommands.Start   // TODO use enumeration
                valueData = new byte[1024];
                foreach (KeyValuePair<string, string> keyValue in values)
                {
                    valueDataSize += Encoding.UTF8.GetBytes(keyValue.Key, 0, keyValue.Key.Length, valueData, valueDataSize);
                    if (valueDataSize >= 1023)
                        throw new Exception("Too much provider data");  // TODO better message. 
                    valueData[valueDataSize++] = 0;
                    valueDataSize += Encoding.UTF8.GetBytes(keyValue.Value, 0, keyValue.Value.Length, valueData, valueDataSize);
                    if (valueDataSize >= 1023)
                        throw new Exception("Too much provider data");  // TODO better message. 
                    valueData[valueDataSize++] = 0;
                }
            }
            return EnableProvider(providerGuid, providerLevel, matchAnyKeywords, matchAllKeywords, valueDataType, valueData, valueDataSize);
        }
        /// <summary>
        /// Once started, event sessions will persist even after the process that created them dies. They are
        /// only stoped by this explicit Stop() API. 
        /// </summary>
        public bool Stop(bool noThrow = false)
        {
            if (m_Stopped)
                return true;
            m_Stopped = true;
            var propertiesBuff = stackalloc byte[PropertiesSize];
            var properties = GetProperties(propertiesBuff);
            int hr = TraceEventNativeMethods.ControlTrace(0UL, m_SessionName, properties, TraceEventNativeMethods.EVENT_TRACE_CONTROL_STOP);
            if (hr != 4201)     // Instance name not found.  This means we did not start
            {
                if (!noThrow)
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
                return false;   // Stop failed
            }
            return true;
        }

        /// <summary>
        /// Cause the log to be a circular buffer.  The buffer size (in MegaBytes) is the value of this property.
        /// Setting this to 0 will cause it to revert to non-circular mode.  This routine can only be called BEFORE
        /// a provider is enabled.  
        /// </summary>
        public int CircularBufferMB
        {
            get { return m_CircularBufferMB; }
            set
            {
                if (IsActive)
                    throw new InvalidOperationException("Property can't be changed after A provider has started.");
                if (m_FileName == null)
                    throw new InvalidOperationException("Circular buffers only allowed on sessions with files.");
                m_CircularBufferMB = value;
            }

        }
        /// <summary>
        /// Sets the size of the buffer the operating system should reserve to avoid lost packets.   Starts out 
        /// as a very generous 32MB for files.  If events are lost, this can be increased.  
        /// </summary>
        public int BufferSizeMB
        {
            get { return m_BufferSizeMB; }
            set
            {
                if (IsActive)
                    throw new InvalidOperationException("Property can't be changed after A provider has started.");
                m_BufferSizeMB = value;
            }
        }
        /// <summary>
        /// If set then Stop() will be called automatically when this object is Disposed or GCed (which
        /// will happen on program exit unless a unhandled exception occurs.  
        /// </summary>
        public bool StopOnDispose { get { return m_StopOnDispose; } set { m_StopOnDispose = value; } }
        /// <summary>
        /// The name of the session that can be used by other threads to attach to the session. 
        /// </summary>
        public string SessionName
        {
            get { return m_SessionName; }
        }
        /// <summary>
        /// The name of the moduleFile that events are logged to.  Null means the session is real time. 
        /// </summary>
        public string FileName
        {
            get
            {
                return m_FileName;
            }
        }
        /// <summary>
        /// Creating a TraceEventSession does not actually interact with the operating system until a
        /// provider is enabled. At that point the session is considered active (OS state that survives a
        /// process exit has been modified). IsActive returns true if the session is active.
        /// </summary>
        public bool IsActive
        {
            get
            {
                return m_IsActive;
            }
        }

        /// <summary>
        /// ETW trace sessions survive process shutdown. Thus you can attach to existing active sessions.
        /// GetActiveSessionNames() returns a list of currently existing session names.  These can be passed
        /// to the code:TraceEventSession constructor to control it.   
        /// </summary>
        /// <returns>A enumeration of strings, each of which is a name of a session</returns>
        public unsafe static IEnumerable<string> GetActiveSessionNames()
        {
            const int MAX_SESSIONS = 64;
            int sizeOfProperties = sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES) +
                                   sizeof(char) * MaxNameSize +     // For log moduleFile name 
                                   sizeof(char) * MaxNameSize;      // For session name

            byte* sessionsArray = stackalloc byte[MAX_SESSIONS * sizeOfProperties];
            TraceEventNativeMethods.EVENT_TRACE_PROPERTIES** propetiesArray = stackalloc TraceEventNativeMethods.EVENT_TRACE_PROPERTIES*[MAX_SESSIONS];

            for (int i = 0; i < MAX_SESSIONS; i++)
            {
                TraceEventNativeMethods.EVENT_TRACE_PROPERTIES* properties = (TraceEventNativeMethods.EVENT_TRACE_PROPERTIES*)&sessionsArray[sizeOfProperties * i];
                properties->Wnode.BufferSize = (uint)sizeOfProperties;
                properties->LoggerNameOffset = (uint)sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES);
                properties->LogFileNameOffset = (uint)sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES) + sizeof(char) * MaxNameSize;
                propetiesArray[i] = properties;
            }
            int sessionCount = 0;
            int hr = TraceEventNativeMethods.QueryAllTraces((IntPtr)propetiesArray, MAX_SESSIONS, ref sessionCount);
            Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));

            List<string> activeTraceNames = new List<string>();
            for (int i = 0; i < sessionCount; i++)
            {
                byte* propertiesBlob = (byte*)propetiesArray[i];
                string sessionName = new string((char*)(&propertiesBlob[propetiesArray[i]->LoggerNameOffset]));
                activeTraceNames.Add(sessionName);
            }
            return activeTraceNames;
        }
        /// <summary>
        /// It is sometimes useful to merge the contents of several ETL files into a single 
        /// output ETL file.   This routine does that.  It also will attach additional 
        /// information that will allow correct file name and symbolic lookup if the 
        /// ETL file is used on a machine other than the one that the data was collected on.
        /// If you wish to transport the file to another machine you need to merge them.
        /// </summary>
        /// <param name="inputETLFileNames"></param>
        /// <param name="outputETLFileName"></param>
        public static void Merge(string[] inputETLFileNames, string outputETLFileName)
        {
            int retValue = TraceEventNativeMethods.CreateMergedTraceFile(
                outputETLFileName, inputETLFileNames, inputETLFileNames.Length,
                    TraceEventNativeMethods.EVENT_TRACE_MERGE_EXTENDED_DATA.IMAGEID |
                    TraceEventNativeMethods.EVENT_TRACE_MERGE_EXTENDED_DATA.VOLUME_MAPPING);
            if (retValue != 0)
                throw new ApplicationException("Merge operation failed.");
        }
        /// <summary>
        /// This variation of the Merge command takes the 'primary' etl file name (X.etl)
        /// and will merge in any files that match the list of file pattern in 'suffixPats'
        /// By default this list is .clr*.etl .user*.etl. and .kernel.etl.  
        /// </summary>
        public static void MergeInPlace(string etlFileName, List<String> suffixPats = null)
        {
            if (suffixPats == null)
                suffixPats = new List<string>() { ".clr*.etl", "user*.etl", ".kernel.etl" };

            var dir = Path.GetDirectoryName(etlFileName);
            if (dir.Length == 0)
                dir = ".";
            var baseName = Path.GetFileNameWithoutExtension(etlFileName);
            List<string> mergeInputs = new List<string>();
            mergeInputs.Add(etlFileName);

            foreach(var suffixPat in suffixPats)
                mergeInputs.AddRange(Directory.GetFiles(dir, baseName + suffixPat));
                
            string tempName = Path.ChangeExtension(etlFileName, ".etl.new");
            try
            {
                // Do the merge;
                Merge(mergeInputs.ToArray(), tempName);

                // Delete the originals.  
                foreach (var mergeInput in mergeInputs)
                    File.Delete(mergeInput);

                // Place the output in its final resting place.  
                File.Move(tempName, etlFileName);
            }
            finally
            {
                // Insure we clean up.  
                if (File.Exists(tempName))
                    File.Delete(tempName);
            }
        }
        /// <summary>
        /// Is the current process Elevated (allowed to turn on a ETW provider
        /// </summary>
        /// <returns></returns>
        public static bool? IsElevated() { return TraceEventNativeMethods.IsElevated(); }
        #region Private
        private const int maxStackTraceProviders = 256;
        /// <summary>
        /// The 'properties' field is only the header information.  There is 'tail' that is 
        /// required.  'ToUnmangedBuffer' fills in this tail properly. 
        /// </summary>
        ~TraceEventSession()
        {
            Dispose();
        }
        public void Dispose()
        {
            if (m_StopOnDispose)
                Stop(true);

            if (m_SessionHandle != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                TraceEventNativeMethods.CloseTrace(m_SessionHandle);
            m_SessionHandle = TraceEventNativeMethods.INVALID_HANDLE_VALUE;

            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Do intialization common to the contructors.  
        /// </summary>
        private bool EnableProvider(Guid providerGuid, TraceEventLevel providerLevel, ulong matchAnyKeywords, ulong matchAllKeywords, int providerDataType, byte[] providerData, int providerDataSize)
        {
            bool ret = InsureStarted();
            TraceEventNativeMethods.EVENT_FILTER_DESCRIPTOR* dataDescrPtr = null;
            fixed (byte* providerDataPtr = providerData)
            {
                string regKeyName = @"Software\Microsoft\Windows\CurrentVersion\Winevt\Publishers\{" + providerGuid + "}";
                byte[] registryData = null;
                if (providerData != null || providerDataType != 0)
                {
                    TraceEventNativeMethods.EVENT_FILTER_DESCRIPTOR dataDescr = new TraceEventNativeMethods.EVENT_FILTER_DESCRIPTOR();
                    dataDescr.Ptr = null;
                    dataDescr.Size = providerDataSize;
                    dataDescr.Type = providerDataType;
                    dataDescrPtr = &dataDescr;

                    if (providerData == null)
                        providerData = new byte[0];
                    else
                        dataDescr.Ptr = providerDataPtr;

                    // Set the registry key so providers get the information even if they are not active now
                    registryData = new byte[providerDataSize + 4];
                    registryData[0] = (byte)(providerDataType);
                    registryData[1] = (byte)(providerDataType >> 8);
                    registryData[2] = (byte)(providerDataType >> 16);
                    registryData[3] = (byte)(providerDataType >> 24);
                    Array.Copy(providerData, 0, registryData, 4, providerDataSize);
                }
                SetOrDelete(regKeyName, "ControllerData", registryData);
                int hr;
                try
                {
                    hr = TraceEventNativeMethods.EnableTraceEx(ref providerGuid, null, m_SessionHandle,
                    1, (byte)providerLevel, matchAnyKeywords, matchAllKeywords, 0, dataDescrPtr);
                }
                catch (EntryPointNotFoundException)
                {
                    // Try with the old pre-vista API
                    hr = TraceEventNativeMethods.EnableTrace(1, (int)matchAnyKeywords, (int)providerLevel, ref providerGuid, m_SessionHandle);
                }
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(hr));
            }
            m_IsActive = true;
            return ret;
        }
        private void SetOrDelete(string regKeyName, string valueName, byte[] data)
        {
#if !Silverlight
            if (System.Runtime.InteropServices.Marshal.SizeOf(typeof(IntPtr)) == 8 &&
                regKeyName.StartsWith(@"Software\", StringComparison.OrdinalIgnoreCase))
                regKeyName = @"Software\Wow6432Node" + regKeyName.Substring(8);

            if (data == null)
            {
                Microsoft.Win32.RegistryKey regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regKeyName, true);
                if (regKey != null)
                {
                    regKey.DeleteValue(valueName, false);
                    regKey.Close();
                }
            }
            else
            {
                Microsoft.Win32.RegistryKey regKey = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(regKeyName);
                regKey.SetValue(valueName, data, Microsoft.Win32.RegistryValueKind.Binary);
                regKey.Close();
            }
#endif
        }
        /// <summary>
        /// Given a mask of kernel flags, set the array stackTracingIds of size stackTracingIdsMax to match.
        /// It returns the number of entries in stackTracingIds that were filled in.
        /// </summary>
        private unsafe int SetStackTraceIds(KernelTraceEventParser.Keywords stackCapture, TraceEventNativeMethods.STACK_TRACING_EVENT_ID* stackTracingIds, int stackTracingIdsMax)
        {
            int curID = 0;

            // PerfInfo (sample profiling)
            if ((stackCapture & KernelTraceEventParser.Keywords.Profile) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PerfInfoTaskGuid;
                stackTracingIds[curID].Type = 0x2e;     // Sample Profile
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.SystemCall) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PerfInfoTaskGuid;
                stackTracingIds[curID].Type = 0x33;     // SysCall
                curID++;
            }
            // Thread
            if ((stackCapture & KernelTraceEventParser.Keywords.Thread) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x01;     // Thread Create
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.ContextSwitch) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x24;     // Context Switch
                curID++;
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.Dispatcher) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ThreadTaskGuid;
                stackTracingIds[curID].Type = 0x32;     // Ready Thread
                curID++;
            }

            // Image
            if ((stackCapture & KernelTraceEventParser.Keywords.ImageLoad) != 0)
            {
                // Confirm this is not ImageTaskGuid
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ProcessTaskGuid;
                stackTracingIds[curID].Type = 0x0A;     // Image Load
                curID++;
            }

            // Process
            if ((stackCapture & KernelTraceEventParser.Keywords.Process) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.ProcessTaskGuid;
                stackTracingIds[curID].Type = 0x01;     // Process Create
                curID++;
            }

            // Disk
            if ((stackCapture & KernelTraceEventParser.Keywords.DiskIOInit) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.DiskIoTaskGuid;
                stackTracingIds[curID].Type = 0x0c;     // Read Init
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.DiskIoTaskGuid;
                stackTracingIds[curID].Type = 0x0d;     // Write Init
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.DiskIoTaskGuid;
                stackTracingIds[curID].Type = 0x0f;     // Flush Init
                curID++;
            }

            // Virtual Alloc
            if ((stackCapture & KernelTraceEventParser.Keywords.VirtualAlloc) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.VirtualAllocTaskGuid;
                stackTracingIds[curID].Type = 0x62;     // Flush Init
                curID++;
            }

            // Hard Faults
            if ((stackCapture & KernelTraceEventParser.Keywords.MemoryHardFaults) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PageFaultTaskGuid;
                stackTracingIds[curID].Type = 0x20;     // Hard Fault
                curID++;
            }

            // Page Faults 
            if ((stackCapture & KernelTraceEventParser.Keywords.MemoryPageFaults) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PageFaultTaskGuid;
                stackTracingIds[curID].Type = 0x0A;     // Transition Fault
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PageFaultTaskGuid;
                stackTracingIds[curID].Type = 0x0B;     // Demand zero Fault
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PageFaultTaskGuid;
                stackTracingIds[curID].Type = 0x0C;     // Copy on Write Fault
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PageFaultTaskGuid;
                stackTracingIds[curID].Type = 0x0D;     // Guard Page Fault
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.PageFaultTaskGuid;
                stackTracingIds[curID].Type = 0x0E;     // Hard Page Fault
                curID++;

                // TODO these look interesting.  
                // ! %02 49 ! Pagefile Mapped Section Create
                // ! %02 69 ! Pagefile Backed Image Mapping
                // ! %02 71 ! Contiguous Memory Generation
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.FileIOInit) != 0)
            {
                // TODO allow stacks only on open and close;
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x40;     // Create
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x41;     // Cleanup
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x42;     // Close
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x43;     // Read
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x44;     // Write
                curID++;

#if false       // TODO  (as I recall they caused failures
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x45;     // SetInformation
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x46;     // Delete
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x47;     // Rename
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x48;     // DirEnum
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x49;     // Flush
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x4A;     // QueryInformation
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x4B;     // FSControl
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.FileIoTaskGuid;
                stackTracingIds[curID].Type = 0x4D;     // DirNotify
                curID++;
#endif
            }

            if ((stackCapture & KernelTraceEventParser.Keywords.Registry) != 0)
            {
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0A;     // NtCreateKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0B;     // NtOpenKey
                curID++;
#if false       // TODO enable (as I recall they caused failures)
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0C;     // NtDeleteKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0D;     // NtQueryKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0E;     // NtSetValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x0F;     // NtDeleteValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x10;     // NtQueryValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x11;     // NtEnumerateKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x12;     // NtEnumerateValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x13;     // NtQueryMultipleValueKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x14;     // NtSetInformationKey
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x15;     // NtFlushKey
                curID++;

                // TODO What are these?  
                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x16;     // KcbCreate
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x17;     // KcbDelete
                curID++;

                stackTracingIds[curID].EventGuid = KernelTraceEventParser.RegistryTaskGuid;
                stackTracingIds[curID].Type = 0x1A;     // VirtualizeKey
                curID++;
#endif
            }

            // TODO put these in for advanced procedure calls.  
            //! %1A 21 ! ALPC: SendMessage
            //! %1A 22 ! ALPC: ReceiveMessage
            //! %1A 23 ! ALPC: WaitForReply
            //! %1A 24 ! ALPC: WaitForNewMessage
            //! %1A 25 ! ALPC: UnWait

            // I don't have heap or threadpool.  

            // Confirm we did not overflow.  
            Debug.Assert(curID <= stackTracingIdsMax);
            return curID;
        }
        private bool InsureStarted()
        {
            if (!m_Create)
                throw new NotSupportedException("Can not enable providers on a session you don't create directly");
            if (m_SessionName == KernelTraceEventParser.KernelSessionName)
                throw new NotSupportedException("Can only enable kernel providers on a kernel session");

            // Already initialized, nothing to do.  
            if (m_SessionHandle != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                return false;

            var propertiesBuff = stackalloc byte[PropertiesSize];
            var properties = GetProperties(propertiesBuff);
            bool ret = false;

            int retCode = TraceEventNativeMethods.StartTrace(out m_SessionHandle, m_SessionName, properties);
            if (retCode == 0xB7)      // STIERR_HANDLEEXISTS
            {
                ret = true;
                Stop();
                m_Stopped = false;
                Thread.Sleep(100);  // Give it some time to stop. 
                retCode = TraceEventNativeMethods.StartTrace(out m_SessionHandle, m_SessionName, properties);
            }
            if (retCode == 5 && Environment.OSVersion.Version.Major > 5)      // On Vista and we get a 'Accessed Denied' message
                throw new UnauthorizedAccessException("Error Starting ETW:  Access Denied (Administrator rights required to start ETW)");
            Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(retCode));
            return ret;
        }
        private TraceEventNativeMethods.EVENT_TRACE_PROPERTIES* GetProperties(byte* buffer)
        {
            TraceEventNativeMethods.ZeroMemory((IntPtr)buffer, (uint)PropertiesSize);
            var properties = (TraceEventNativeMethods.EVENT_TRACE_PROPERTIES*)buffer;

            properties->LoggerNameOffset = (uint)sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES);
            properties->LogFileNameOffset = properties->LoggerNameOffset + MaxNameSize * sizeof(char);

            // Copy in the session name
            if (m_SessionName.Length > MaxNameSize - 1)
                throw new ArgumentException("File name too long", "sessionName");
            char* sessionNamePtr = (char*)(((byte*)properties) + properties->LoggerNameOffset);
            CopyStringToPtr(sessionNamePtr, m_SessionName);

            properties->Wnode.BufferSize = (uint)PropertiesSize;
            properties->Wnode.Flags = TraceEventNativeMethods.WNODE_FLAG_TRACED_GUID;
            properties->LoggerNameOffset = (uint)sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES);
            properties->LogFileNameOffset = properties->LoggerNameOffset + MaxNameSize * sizeof(char);

            properties->FlushTimer = 1;              // flush every second;
            properties->BufferSize = 1024;           // 1Mb buffer blockSize
            if (m_FileName == null)
            {
                properties->LogFileMode = TraceEventNativeMethods.EVENT_TRACE_REAL_TIME_MODE;
                properties->LogFileNameOffset = 0;
            }
            else
            {
                if (m_FileName.Length > MaxNameSize - 1)
                    throw new ArgumentException("File name too long", "fileName");
                char* fileNamePtr = (char*)(((byte*)properties) + properties->LogFileNameOffset);
                CopyStringToPtr(fileNamePtr, m_FileName);
                if (m_CircularBufferMB == 0)
                {
                    properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_FILE_MODE_SEQUENTIAL;
                }
                else
                {
                    properties->LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_FILE_MODE_CIRCULAR;
                    properties->MaximumFileSize = (uint)m_CircularBufferMB;
                }
            }

            properties->MinimumBuffers = (uint)m_BufferSizeMB;
            properties->MaximumBuffers = (uint)(m_BufferSizeMB * 4);

            properties->Wnode.ClientContext = 1;    // set Timer resolution to 100ns.  
            return properties;
        }

        private unsafe void CopyStringToPtr(char* toPtr, string str)
        {
            fixed (char* fromPtr = str)
            {
                int i = 0;
                while (i < str.Length)
                {
                    toPtr[i] = fromPtr[i];
                    i++;
                }
                toPtr[i] = '\0';   // Null terminate
            }
        }

        private const int MaxNameSize = 1024;
        private int PropertiesSize = sizeof(TraceEventNativeMethods.EVENT_TRACE_PROPERTIES) + 2 * MaxNameSize * sizeof(char);

        // Data that is exposed through properties.  
        private string m_SessionName;             // Session name (identifies it uniquely on the machine)
        private string m_FileName;                // Where to log (null means real time session)
        private int m_BufferSizeMB;
        private int m_CircularBufferMB;

        // Internal state
        private bool m_Create;                    // Should create if it does not exist.
        private bool m_IsActive;                  // Session is active (InsureSession has been called)
        private bool m_Stopped;                   // The Stop() method was called (avoids reentrancy)
        private bool m_StopOnDispose;             // Should we Stop() when the object is destroyed?
        private ulong m_SessionHandle;            // OS handle
        #endregion
    }

    /// <summary>
    /// Indicates to a provider whether verbose events should be logged.  
    /// </summary>
    public enum TraceEventLevel
    {
        Always = 0,
        Critical = 1,
        Error = 2,
        Warning = 3,
        Informational = 4,
        Verbose = 5,
    };
}

