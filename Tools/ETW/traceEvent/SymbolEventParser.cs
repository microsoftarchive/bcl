//     Copyright (c) Microsoft Corporation.  All rights reserved.
using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using Diagnostics.Eventing;

namespace Diagnostics.Eventing
{
    /// <summary>
    /// Kernel traces have information about images that are loaded, however they don't have enough information
    /// in the events themselves to unambigously look up PDBs without looking at the data inside the images.
    /// This means that symbols can't be resolved unless you are on the same machine on which you gathered the data.
    /// 
    /// XPERF solves this problem by adding new 'synthetic' events that it creates by looking at the trace and then
    /// opening each DLL mentioned and extracting the information needed to look PDBS up on a symbol server (this 
    /// includes the PE file's TimeDateStamp as well as a PDB Guid, and 'pdbAge' that can be found in the DLLs header.
    /// 
    /// These new events are added when XPERF runs the 'merge' command (or -d flag is passed).  It is also exposed 
    /// through the KernelTraceControl.dll!CreateMergedTraceFile API.   
    /// 
    /// SymbolTraceEventParser is a parser for extra events.   
    /// </summary>
    public sealed class SymbolTraceEventParser : TraceEventParser
    {
        public static string ProviderName = "KernelTraceControl";
        public static Guid ProviderGuid = new Guid(0x28ad2447, 0x105b, 0x4fe2, 0x95, 0x99, 0xe5, 0x9b, 0x2a, 0xa9, 0xa6, 0x34);

        public SymbolTraceEventParser(TraceEventSource source)
            : base(source)
        {
        }

        /// <summary>
        ///  The DbgIDRSDS event is added by XPERF for every Image load.  It contains the 'PDB signature' for the DLL, 
        ///  which is enough to unambigously look the image's PDB up on a symbol server.  
        /// </summary>
        public event Action<DbgIDRSDSTraceData> DbgIDRSDS
        {
            add
            {
                source.RegisterEventTemplate(new DbgIDRSDSTraceData(value, 0xFFFF, 0, "ImageId", ImageIDTaskGuid, DBGID_LOG_TYPE_RSDS, "DbgID/RSDS", ProviderGuid, ProviderName));
            }
            remove
            {
                throw new NotImplementedException();
            }
        }
        /// <summary>
        /// Every DLL has a Timestamp in the PE file itself that indicates when it is built.  This event dumps this timestamp.
        /// This timestamp is used to be as the 'signature' of the image and is used as a key to find the symbols, however 
        /// this has mostly be superseeded by the DbgID/RSDS event. 
        /// </summary>
        public event Action<ImageIDTraceData> ImageID
        {
            add
            {
                source.RegisterEventTemplate(new ImageIDTraceData(value, 0xFFFF, 0, "ImageId", ImageIDTaskGuid, DBGID_LOG_TYPE_IMAGEID, "Info", ProviderGuid, ProviderName));
            }
            remove
            {
                throw new NotImplementedException();
            }

        }
        /// <summary>
        /// The FileVersion event contains information from the file version resource that most DLLs have that indicated
        /// detailed information about the exact version of the DLL.  (What is in the File->Properties->Version property
        /// page)
        /// </summary>
        public event Action<FileVersionTraceData> FileVersion
        {
            add
            {
                source.RegisterEventTemplate(new FileVersionTraceData(value, 0xFFFF, 0, "ImageId", ImageIDTaskGuid, DBGID_LOG_TYPE_FILEVERSION, "FileVersion", ProviderGuid, ProviderName));
            }
            remove
            {
                throw new NotImplementedException();
            }

        }

        #region Private
        public const int DBGID_LOG_TYPE_IMAGEID = 0x00;
        public const int DBGID_LOG_TYPE_NONE = 0x20;
        public const int DBGID_LOG_TYPE_RSDS = 0x24;
        public const int DBGID_LOG_TYPE_FILEVERSION = 0x40;

        internal static Guid ImageIDTaskGuid = new Guid(unchecked((int) 0xB3E675D7), 0x2554, 0x4f18, 0x83, 0x0B, 0x27, 0x62, 0x73, 0x25, 0x60, 0xDE);
        #endregion 
    }

    public sealed class FileVersionTraceData : TraceEvent
    {
        public int ImageSize { get { return GetInt32At(0); } }
        public int TimeDateStamp { get { return GetInt32At(4); } }
        public string OrigFileName { get { return GetUnicodeStringAt(8); } } 
        public string FileDescription { get { return GetUnicodeStringAt(SkipUnicodeString(8, 1)); } } 
        public string FileVersion { get { return GetUnicodeStringAt(SkipUnicodeString(8, 2)); } } 
        public string BinFileVersion { get { return GetUnicodeStringAt(SkipUnicodeString(8, 3)); } } 
        public string VerLanguage { get { return GetUnicodeStringAt(SkipUnicodeString(8, 4)); }} 
        public string ProductName { get { return GetUnicodeStringAt(SkipUnicodeString(8, 5)); }} 
        public string CompanyName { get { return GetUnicodeStringAt(SkipUnicodeString(8, 6)); }} 
        public string ProductVersion { get { return GetUnicodeStringAt(SkipUnicodeString(8, 7)); }} 
        public string FileId { get { return GetUnicodeStringAt(SkipUnicodeString(8, 8)); }} 
        public string ProgramId { get { return GetUnicodeStringAt(SkipUnicodeString(8, 9)); }} 

        #region Private
        internal FileVersionTraceData(Action<FileVersionTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opCode, string opCodeName, Guid providerGuid, string providerName) :
            base(eventID, task, taskName, taskGuid, opCode, opCodeName, providerGuid, providerName)
        {
            this.action = action;
        }

        protected internal override void Dispatch()
        {
            action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(EventDataLength == SkipUnicodeString(8, 10));
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ImageSize", "TimeDateStamp", "OrigFileName", "FileDescription", "FileVersion",
                        "BinFileVersion", "VerLanguage", "ProductName", "CompanyName", "ProductVersion", "FileId", "ProgramId" };
                }
                    return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ImageSize;
                case 1:
                    return TimeDateStamp;
                case 2:
                    return OrigFileName;
                case 3:
                    return FileDescription;
                case 4:
                    return FileVersion;
                case 5:
                    return BinFileVersion;
                case 6:
                    return VerLanguage;
                case 7:
                    return ProductName;
                case 8:
                    return CompanyName;
                case 9:
                    return ProductVersion;
                case 10:
                    return FileId;
                case 11:
                    return ProgramId;
                default:
                    Debug.Assert(false, "invalid index");
                    return null;
            }
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            sb.XmlAttribHex("ImageSize", ImageSize);
            sb.XmlAttribHex("TimeDateStamp", TimeDateStamp);
            sb.XmlAttrib("OrigFileName", OrigFileName);
            sb.XmlAttrib("FileDescription", FileDescription);
            sb.XmlAttrib("FileVersion", FileVersion);
            sb.XmlAttrib("BinFileVersion", BinFileVersion);
            sb.XmlAttrib("VerLanguage", VerLanguage);
            sb.XmlAttrib("ProductName", ProductName);
            sb.XmlAttrib("CompanyName", CompanyName);
            sb.XmlAttrib("ProductVersion", ProductVersion);
            sb.XmlAttrib("FileId", FileId);
            sb.XmlAttrib("ProgramId", ProgramId);
            sb.Append("/>");
            return sb;
        }
        private Action<FileVersionTraceData> action;
        #endregion
    }
    public sealed class DbgIDRSDSTraceData : TraceEvent
    {
        public Address ImageBase { get { return GetHostPointer(0); } }
        // public int ProcessID { get { return GetInt32At(HostOffset(4, 1)); } }    // This seems to be redundant with the ProcessID in the event header
        public Guid GuidSig { get { return GetGuidAt(HostOffset(8, 1)); } }
        public int Age { get { return GetInt32At(HostOffset(24, 1)); } }
        public string PdbFileName { get { return GetAsciiStringAt(HostOffset(28, 1)); } }

        #region Private
        internal DbgIDRSDSTraceData(Action<DbgIDRSDSTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opCode, string opCodeName, Guid providerGuid, string providerName) :
            base(eventID, task, taskName, taskGuid, opCode, opCodeName, providerGuid, providerName)
        {
            this.action = action;
        }

        protected internal override void Dispatch()
        {
            action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(EventDataLength == SkipAsciiString(HostOffset(32, 1)));
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ImageBase", "GuidSig", "Age", "PDBFileName" };
                }
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ImageBase;
                case 1:
                    return GuidSig;
                case 2:
                    return Age;
                case 3:
                    return PdbFileName;
                default:
                    Debug.Assert(false, "invalid index");
                    return null;
            }
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            sb.XmlAttribHex("ImageBase", ImageBase);
            sb.XmlAttrib("GuidSig", GuidSig);
            sb.XmlAttrib("Age", Age);
            sb.XmlAttrib("PdbFileName", PdbFileName);
            sb.Append("/>");
            return sb;
        }
        private Action<DbgIDRSDSTraceData> action;
        #endregion
    }
    public sealed class ImageIDTraceData : TraceEvent
    {
        public Address ImageBase { get { return GetHostPointer(0); } }
        public long ImageSize { get { return GetIntPtrAt(HostOffset(4, 1)); } }
        // Seems to always be 0
        // public int ProcessID { get { return GetInt32At(HostOffset(8, 2)); } }
        public int TimeDateStamp { get { return GetInt32At(HostOffset(12, 2)); } }
        public string OriginalFileName { get { return GetUnicodeStringAt(HostOffset(16, 2)); } }

        #region Private
        internal ImageIDTraceData(Action<ImageIDTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opCode, string opCodeName, Guid providerGuid, string providerName):
            base(eventID, task, taskName, taskGuid, opCode, opCodeName, providerGuid, providerName)
        {
            this.Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(EventDataLength == SkipUnicodeString(HostOffset(16, 2)));
        }

        public override string[] PayloadNames
        {
            get {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ImageBase", "ImageSize", "ProcessID", "TimeDateStamp", "OriginalFileName" };
                }
                return payloadNames;

            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ImageBase;                    
                case 1:
                    return ImageSize;                    
                case 2:
                    return 0;
                case 3:
                    return TimeDateStamp;
                case 4:
                    return OriginalFileName;
                default:
                    Debug.Assert(false, "bad index value");
                    return null;
            }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            sb.XmlAttribHex("ImageBase", ImageBase);
            sb.XmlAttribHex("ImageSize", ImageSize);
            sb.XmlAttribHex("TimeDateStamp", TimeDateStamp);
            sb.XmlAttrib("OriginalFileName", OriginalFileName);
            sb.Append("/>");
            return sb;
        }

        private event Action<ImageIDTraceData> Action;        
        #endregion
    }
}
