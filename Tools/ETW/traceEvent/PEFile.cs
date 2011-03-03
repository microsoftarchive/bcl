//  Copyright (c) Microsoft Corporation.  All rights reserved.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;

namespace PEFile
{
    unsafe sealed class PEBuffer : IDisposable
    {
        public PEBuffer(Stream stream, int buffSize = 512)
        {
            m_stream = stream;
            GetBuffer(buffSize);
        }
        public byte* Fetch(int filePos, int size)
        {
            if (size > m_buff.Length)
                GetBuffer(size);
            if (!(m_buffPos <= filePos && filePos + size <= m_buffPos + m_buffLen))
            {
                // Read in the block of 'size' bytes at filePos
                m_buffPos = filePos;
                m_stream.Seek(m_buffPos, SeekOrigin.Begin);
                m_buffLen = 0;
                while (m_buffLen < m_buff.Length)
                {
                    var count = m_stream.Read(m_buff, m_buffLen, size - m_buffLen);
                    if (count == 0)
                        break;
                    m_buffLen += count;
                }
            }
            return &m_buffPtr[filePos - m_buffPos];
        }
        public int Length { get { return m_buffLen; } }
        public void Dispose()
        {
            m_pinningHandle.Free();
        }
        #region private
        private void GetBuffer(int buffSize)
        {
            m_buff = new byte[buffSize];
            fixed (byte* ptr = m_buff)
                m_buffPtr = ptr;
            m_buffLen = 0;
            m_pinningHandle = GCHandle.Alloc(m_buff, GCHandleType.Pinned);
        }

        int m_buffPos;
        int m_buffLen;      // Number of valid bytes in m_buff
        byte[] m_buff;
        byte* m_buffPtr;
        GCHandle m_pinningHandle;
        Stream m_stream;
        #endregion
    }

    unsafe public class PEFile : IDisposable
    {
        public PEFile(string filePath)
        {
            m_stream = File.OpenRead(filePath);
            m_headerBuff = new PEBuffer(m_stream);

            Header = new PEHeader(m_headerBuff.Fetch(0, 512));
            // We did not read in the complete header, Try again using the right sized buffer.  
            if (Header.Size > m_headerBuff.Length)
                Header = new PEHeader(m_headerBuff.Fetch(0, Header.Size));

            if (Header.Size > m_headerBuff.Length)
                throw new InvalidOperationException("Bad PE Header in " + filePath);
        }
        /// <summary>
        /// The Header for the PE file.  This contains the infor in a link /dump /headers 
        /// </summary>
        public PEHeader Header { get; private set; }
        /// <summary>
        /// Looks up the debug signature information in the EXE.   Returns true and sets the parameters if it is found. 
        /// </summary>
        public bool GetPdbSignature(ref string pdbName, ref Guid pdbGuid, ref int pdbAge)
        {
            if (Header.DebugDirectory.VirtualAddress != 0)
            {
                var buff = AllocBuff();
                var debugEntries = (IMAGE_DEBUG_DIRECTORY*)FetchRVA(Header.DebugDirectory.VirtualAddress, Header.DebugDirectory.Size, buff);
                Debug.Assert(Header.DebugDirectory.Size % sizeof(IMAGE_DEBUG_DIRECTORY) == 0);
                int debugCount = Header.DebugDirectory.Size / sizeof(IMAGE_DEBUG_DIRECTORY);
                for (int i = 0; i < debugCount; i++)
                {
                    if (debugEntries[i].Type == IMAGE_DEBUG_TYPE.CODEVIEW)
                    {
                        var info = (CV_INFO_PDB70*)buff.Fetch((int)debugEntries[i].PointerToRawData, debugEntries[i].SizeOfData);
                        if (info->CvSignature == CV_INFO_PDB70.PDB70CvSignature)
                        {
                            pdbGuid = info->Signature;
                            pdbAge = info->Age;
                            pdbName = info->PdbFileName;
                            return true;
                        }
                    }
                }
                FreeBuff(buff);
            }
            return false;
        }
        public void Dispose()
        {
            m_stream.Close();
            m_headerBuff.Dispose();
            if (m_freeBuff != null)
                m_freeBuff.Dispose();
        }

        #region private
        PEBuffer m_headerBuff;
        PEBuffer m_freeBuff;
        FileStream m_stream;


        private byte* FetchRVA(int rva, int size, PEBuffer buffer)
        {
            return buffer.Fetch(Header.RvaToFileOffset(rva), size);
        }

        private PEBuffer AllocBuff()
        {
            var ret = m_freeBuff;
            if (ret == null)
                return new PEBuffer(m_stream);
            m_freeBuff = null;
            return ret;
        }
        private void FreeBuff(PEBuffer buffer)
        {
            m_freeBuff = buffer;
        }
        #endregion
    }

    unsafe public class PEHeader : IDisposable
    {
        /// <summary>
        /// Returns a PEHeader for pointer in memory.  It does NO validity checking. 
        /// </summary>
        /// <param name="startOfPEFile"></param>
        public PEHeader(IntPtr startOfPEFile) : this((void*)startOfPEFile) { }
        public PEHeader(void* startOfPEFile)
        {
            this.dosHeader = (IMAGE_DOS_HEADER*)startOfPEFile;
            this.ntHeader = (IMAGE_NT_HEADERS*)((byte*)startOfPEFile + dosHeader->e_lfanew);
            this.sections = (IMAGE_SECTION_HEADER*)(((byte*)this.ntHeader) + sizeof(IMAGE_NT_HEADERS) + ntHeader->FileHeader.SizeOfOptionalHeader);
        }

        /// <summary>
        /// The total size, including section array of the the PE header.  
        /// </summary>
        public int Size
        {
            get
            {
                return VirtualAddressToRva(this.sections) + sizeof(IMAGE_SECTION_HEADER) * ntHeader->FileHeader.NumberOfSections;
            }
        }

        public int VirtualAddressToRva(void* ptr)
        {
            return (int)((byte*)ptr - (byte*)this.dosHeader);
        }
        public void* RvaToVirtualAddress(int rva)
        {
            return ((byte*)this.dosHeader) + rva;
        }
        public int RvaToFileOffset(int rva)
        {
            for (int i = 0; i < ntHeader->FileHeader.NumberOfSections; i++)
            {

                if (sections[i].VirtualAddress <= rva && rva < sections[i].VirtualAddress + sections[i].VirtualSize)
                    return (int)sections[i].PointerToRawData + (rva - (int)sections[i].VirtualAddress);
            }
            throw new InvalidOperationException("Illegal RVA 0x" + rva.ToString("x"));
        }

        /// <summary>
        /// PEHeader pins a buffer, if you wish to eagerly dispose of this, it can be done here.  
        /// </summary>
        public void Dispose()
        {
            if (pinningHandle.IsAllocated)
                pinningHandle.Free();
            dosHeader = null;
            ntHeader = null;
        }

        public bool IsPE64 { get { return OptionalHeader32->Magic == 0x20b; } }
        public bool IsManaged { get { return ComDescriptorDirectory.VirtualAddress != 0; } }

        // fields of code:IMAGE_NT_HEADERS
        public uint Signature { get { return ntHeader->Signature; } }

        // fields of code:IMAGE_FILE_HEADER
        public MachineType Machine { get { return (MachineType)ntHeader->FileHeader.Machine; } }
        public ushort NumberOfSections { get { return ntHeader->FileHeader.NumberOfSections; } }
        public ulong TimeDateStampSec { get { return ntHeader->FileHeader.TimeDateStamp; } }
        public DateTime TimeDateStamp
        {
            get
            {
                // Convert seconds from Jan 1 1970 to DateTime ticks.  
                // The 621356004000000000L represents Jan 1 1970 as DateTime 100ns ticks.  
                DateTime ret = new DateTime((long)TimeDateStampSec * 10000000 + 621356004000000000L, DateTimeKind.Utc).ToLocalTime();

                // From what I can tell TimeDateSec does not take into account daylight savings time when
                // computing the UTC time. Because of this we adjust here to get the proper local time.  
                if (ret.IsDaylightSavingTime())
                    ret = ret.AddHours(-1.0);
                return ret;

            }
        }
        public ulong PointerToSymbolTable { get { return ntHeader->FileHeader.PointerToSymbolTable; } }
        public ulong NumberOfSymbols { get { return ntHeader->FileHeader.NumberOfSymbols; } }
        public ushort SizeOfOptionalHeader { get { return ntHeader->FileHeader.SizeOfOptionalHeader; } }
        public ushort Characteristics { get { return ntHeader->FileHeader.Characteristics; } }

        // fields of code:IMAGE_OPTIONAL_HEADER32 (or code:IMAGE_OPTIONAL_HEADER64)
        // these first ones don't depend on whether we are PE or PE64
        public ushort Magic { get { return OptionalHeader32->Magic; } }
        public byte MajorLinkerVersion { get { return OptionalHeader32->MajorLinkerVersion; } }
        public byte MinorLinkerVersion { get { return OptionalHeader32->MinorLinkerVersion; } }
        public uint SizeOfCode { get { return OptionalHeader32->SizeOfCode; } }
        public uint SizeOfInitializedData { get { return OptionalHeader32->SizeOfInitializedData; } }
        public uint SizeOfUninitializedData { get { return OptionalHeader32->SizeOfUninitializedData; } }
        public uint AddressOfEntryPoint { get { return OptionalHeader32->AddressOfEntryPoint; } }
        public uint BaseOfCode { get { return OptionalHeader32->BaseOfCode; } }

        // These depend on the whether you are PE32 or PE64
        public ulong ImageBase { get { if (IsPE64) return OptionalHeader64->ImageBase; else return OptionalHeader32->ImageBase; } }
        public uint SectionAlignment { get { if (IsPE64) return OptionalHeader64->SectionAlignment; else return OptionalHeader32->SectionAlignment; } }
        public uint FileAlignment { get { if (IsPE64) return OptionalHeader64->FileAlignment; else return OptionalHeader32->FileAlignment; } }
        public ushort MajorOperatingSystemVersion { get { if (IsPE64) return OptionalHeader64->MajorOperatingSystemVersion; else return OptionalHeader32->MajorOperatingSystemVersion; } }
        public ushort MinorOperatingSystemVersion { get { if (IsPE64) return OptionalHeader64->MinorOperatingSystemVersion; else return OptionalHeader32->MinorOperatingSystemVersion; } }
        public ushort MajorImageVersion { get { if (IsPE64) return OptionalHeader64->MajorImageVersion; else return OptionalHeader32->MajorImageVersion; } }
        public ushort MinorImageVersion { get { if (IsPE64) return OptionalHeader64->MinorImageVersion; else return OptionalHeader32->MinorImageVersion; } }
        public ushort MajorSubsystemVersion { get { if (IsPE64) return OptionalHeader64->MajorSubsystemVersion; else return OptionalHeader32->MajorSubsystemVersion; } }
        public ushort MinorSubsystemVersion { get { if (IsPE64) return OptionalHeader64->MinorSubsystemVersion; else return OptionalHeader32->MinorSubsystemVersion; } }
        public uint Win32VersionValue { get { if (IsPE64) return OptionalHeader64->Win32VersionValue; else return OptionalHeader32->Win32VersionValue; } }
        public uint SizeOfImage { get { if (IsPE64) return OptionalHeader64->SizeOfImage; else return OptionalHeader32->SizeOfImage; } }
        public uint SizeOfHeaders { get { if (IsPE64) return OptionalHeader64->SizeOfHeaders; else return OptionalHeader32->SizeOfHeaders; } }
        public uint CheckSum { get { if (IsPE64) return OptionalHeader64->CheckSum; else return OptionalHeader32->CheckSum; } }
        public ushort Subsystem { get { if (IsPE64) return OptionalHeader64->Subsystem; else return OptionalHeader32->Subsystem; } }
        public ushort DllCharacteristics { get { if (IsPE64) return OptionalHeader64->DllCharacteristics; else return OptionalHeader32->DllCharacteristics; } }
        public ulong SizeOfStackReserve { get { if (IsPE64) return OptionalHeader64->SizeOfStackReserve; else return OptionalHeader32->SizeOfStackReserve; } }
        public ulong SizeOfStackCommit { get { if (IsPE64) return OptionalHeader64->SizeOfStackCommit; else return OptionalHeader32->SizeOfStackCommit; } }
        public ulong SizeOfHeapReserve { get { if (IsPE64) return OptionalHeader64->SizeOfHeapReserve; else return OptionalHeader32->SizeOfHeapReserve; } }
        public ulong SizeOfHeapCommit { get { if (IsPE64) return OptionalHeader64->SizeOfHeapCommit; else return OptionalHeader32->SizeOfHeapCommit; } }
        public uint LoaderFlags { get { if (IsPE64) return OptionalHeader64->LoaderFlags; else return OptionalHeader32->LoaderFlags; } }
        public uint NumberOfRvaAndSizes { get { if (IsPE64) return OptionalHeader64->NumberOfRvaAndSizes; else return OptionalHeader32->NumberOfRvaAndSizes; } }

        public IMAGE_DATA_DIRECTORY Directory(int idx)
        {
            if (idx >= NumberOfRvaAndSizes)
                return new IMAGE_DATA_DIRECTORY();
            return ntDirectories[idx];
        }
        public IMAGE_DATA_DIRECTORY ExportDirectory { get { return Directory(0); } }
        public IMAGE_DATA_DIRECTORY ImportDirectory { get { return Directory(1); } }
        public IMAGE_DATA_DIRECTORY ResourceDirectory { get { return Directory(2); } }
        public IMAGE_DATA_DIRECTORY ExceptionDirectory { get { return Directory(3); } }
        public IMAGE_DATA_DIRECTORY CertificatesDirectory { get { return Directory(4); } }
        public IMAGE_DATA_DIRECTORY BaseRelocationDirectory { get { return Directory(5); } }
        public IMAGE_DATA_DIRECTORY DebugDirectory { get { return Directory(6); } }
        public IMAGE_DATA_DIRECTORY ArchitectureDirectory { get { return Directory(7); } }
        public IMAGE_DATA_DIRECTORY GlobalPointerDirectory { get { return Directory(8); } }
        public IMAGE_DATA_DIRECTORY ThreadStorageDirectory { get { return Directory(9); } }
        public IMAGE_DATA_DIRECTORY LoadConfigurationDirectory { get { return Directory(10); } }
        public IMAGE_DATA_DIRECTORY BoundImportDirectory { get { return Directory(11); } }
        public IMAGE_DATA_DIRECTORY ImportAddressTableDirectory { get { return Directory(12); } }
        public IMAGE_DATA_DIRECTORY DelayImportDirectory { get { return Directory(13); } }
        public IMAGE_DATA_DIRECTORY ComDescriptorDirectory { get { return Directory(14); } }
        #region private
        private IMAGE_OPTIONAL_HEADER32* OptionalHeader32 { get { return (IMAGE_OPTIONAL_HEADER32*)(((byte*)this.ntHeader) + sizeof(IMAGE_NT_HEADERS)); } }
        private IMAGE_OPTIONAL_HEADER64* OptionalHeader64 { get { return (IMAGE_OPTIONAL_HEADER64*)(((byte*)this.ntHeader) + sizeof(IMAGE_NT_HEADERS)); } }
        private IMAGE_DATA_DIRECTORY* ntDirectories
        {
            get
            {
                if (IsPE64)
                    return (IMAGE_DATA_DIRECTORY*)(((byte*)this.ntHeader) + sizeof(IMAGE_NT_HEADERS) + sizeof(IMAGE_OPTIONAL_HEADER64));
                else
                    return (IMAGE_DATA_DIRECTORY*)(((byte*)this.ntHeader) + sizeof(IMAGE_NT_HEADERS) + sizeof(IMAGE_OPTIONAL_HEADER32));
            }
        }

        private IMAGE_DOS_HEADER* dosHeader;
        private IMAGE_NT_HEADERS* ntHeader;
        private IMAGE_SECTION_HEADER* sections;
        GCHandle pinningHandle;
        #endregion

    }

    public enum MachineType : ushort
    {
        Native = 0,
        I386 = 0x014c,
        Itanium = 0x0200,
        x64 = 0x8664
    };

    public struct IMAGE_DATA_DIRECTORY
    {
        public int VirtualAddress;
        public int Size;
    }

    #region private classes
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct IMAGE_DOS_HEADER
    {
        public const short IMAGE_DOS_SIGNATURE = 0x5A4D;       // MZ.  
        [FieldOffset(0)]
        public short e_magic;
        [FieldOffset(60)]
        public int e_lfanew;            // Offset to the IMAGE_FILE_HEADER
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct IMAGE_NT_HEADERS
    {
        public uint Signature;
        public IMAGE_FILE_HEADER FileHeader;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct IMAGE_FILE_HEADER
    {
        public ushort Machine;
        public ushort NumberOfSections;
        public uint TimeDateStamp;
        public uint PointerToSymbolTable;
        public uint NumberOfSymbols;
        public ushort SizeOfOptionalHeader;
        public ushort Characteristics;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct IMAGE_OPTIONAL_HEADER32
    {
        public ushort Magic;
        public byte MajorLinkerVersion;
        public byte MinorLinkerVersion;
        public uint SizeOfCode;
        public uint SizeOfInitializedData;
        public uint SizeOfUninitializedData;
        public uint AddressOfEntryPoint;
        public uint BaseOfCode;
        public uint BaseOfData;
        public uint ImageBase;
        public uint SectionAlignment;
        public uint FileAlignment;
        public ushort MajorOperatingSystemVersion;
        public ushort MinorOperatingSystemVersion;
        public ushort MajorImageVersion;
        public ushort MinorImageVersion;
        public ushort MajorSubsystemVersion;
        public ushort MinorSubsystemVersion;
        public uint Win32VersionValue;
        public uint SizeOfImage;
        public uint SizeOfHeaders;
        public uint CheckSum;
        public ushort Subsystem;
        public ushort DllCharacteristics;
        public uint SizeOfStackReserve;
        public uint SizeOfStackCommit;
        public uint SizeOfHeapReserve;
        public uint SizeOfHeapCommit;
        public uint LoaderFlags;
        public uint NumberOfRvaAndSizes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct IMAGE_OPTIONAL_HEADER64
    {
        public ushort Magic;
        public byte MajorLinkerVersion;
        public byte MinorLinkerVersion;
        public uint SizeOfCode;
        public uint SizeOfInitializedData;
        public uint SizeOfUninitializedData;
        public uint AddressOfEntryPoint;
        public uint BaseOfCode;
        public ulong ImageBase;
        public uint SectionAlignment;
        public uint FileAlignment;
        public ushort MajorOperatingSystemVersion;
        public ushort MinorOperatingSystemVersion;
        public ushort MajorImageVersion;
        public ushort MinorImageVersion;
        public ushort MajorSubsystemVersion;
        public ushort MinorSubsystemVersion;
        public uint Win32VersionValue;
        public uint SizeOfImage;
        public uint SizeOfHeaders;
        public uint CheckSum;
        public ushort Subsystem;
        public ushort DllCharacteristics;
        public ulong SizeOfStackReserve;
        public ulong SizeOfStackCommit;
        public ulong SizeOfHeapReserve;
        public ulong SizeOfHeapCommit;
        public uint LoaderFlags;
        public uint NumberOfRvaAndSizes;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe internal struct IMAGE_SECTION_HEADER
    {
        public string Name
        {
            get
            {
                fixed (byte* ptr = NameBytes)
                {
                    if (ptr[7] == 0)
                        return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)ptr);
                    else
                        return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)ptr, 8);
                }
            }
        }
        public fixed byte NameBytes[8];
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint SizeOfRawData;
        public uint PointerToRawData;
        public uint PointerToRelocations;
        public uint PointerToLinenumbers;
        public ushort NumberOfRelocations;
        public ushort NumberOfLinenumbers;
        public uint Characteristics;
    };

    struct IMAGE_DEBUG_DIRECTORY
    {
        public int Characteristics;
        public int TimeDateStamp;
        public short MajorVersion;
        public short MinorVersion;
        public IMAGE_DEBUG_TYPE Type;
        public int SizeOfData;
        public int AddressOfRawData;
        public int PointerToRawData;
    };

    enum IMAGE_DEBUG_TYPE
    {
        UNKNOWN = 0,
        COFF = 1,
        CODEVIEW = 2,
        FPO = 3,
        MISC = 4,
        BBT  = 10,
    };

    unsafe struct CV_INFO_PDB70
    {
        public const int PDB70CvSignature = 0x53445352; // RSDS in ascii

        public int CvSignature;
        public Guid Signature;
        public int Age;
        public fixed byte bytePdbFileName[1];   // Actually variable sized. 
        public string PdbFileName
        {
            get
            {
                fixed (byte* ptr = bytePdbFileName)
                    return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)ptr);
            }
        }
    } ;

    #endregion
}
