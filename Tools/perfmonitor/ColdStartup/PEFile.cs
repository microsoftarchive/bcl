//  Copyright (c) Microsoft Corporation.  All rights reserved.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;

namespace PEFile
{
    unsafe class PEHeader : IDisposable
    {
        /// <summary>
        /// Retrives the PEHeader for 'fileName'  It will throw an exception if 'filePath' cannot be opened,
        /// but will return NULL if the contents cannot be parsed into a PEHeader (it does not meet basic
        /// layout requirements).  
        /// </summary>
        public static PEHeader HeaderForFile(string filePath)
        {
            using (FileStream exe = File.OpenRead(filePath))
            {
                const int readSize = 1024;
                byte[] buffer = new byte[readSize];
                int count = exe.Read(buffer, 0, readSize);
                if (count < 512)        // Not strictly true, but  no interesting PE file is less than this size. 
                    return null;
                fixed (byte* ptr = buffer)
                {
                    IMAGE_DOS_HEADER* dosHeader = (IMAGE_DOS_HEADER*)ptr;
                    if (dosHeader->e_magic != IMAGE_DOS_HEADER.IMAGE_DOS_SIGNATURE)
                        return null;
                    if (dosHeader->e_lfanew + sizeof(IMAGE_NT_HEADERS) + sizeof(IMAGE_OPTIONAL_HEADER32) > count)
                        return null;
                    PEHeader header = new PEHeader(ptr);
                    if (header.Magic != 0x10B || header.Magic == 0x20b)
                        return null;

                    // Make certain that the directories fit in the allocated size. \
#if DEBUG
                    long length = (byte*)&header.ntDirectories[header.NumberOfRvaAndSizes] - ptr;
#endif
                    if ((byte*)&header.ntDirectories[header.NumberOfRvaAndSizes] > &ptr[count])
                        return null;

                    // TODO support sections at some point 
                    header.pinningHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    return header;
                }
            }
        }
        /// <summary>
        /// Returns a PEHeader for pointer in memory.  It does NO validity checking. 
        /// </summary>
        /// <param name="startOfPEFile"></param>
        public PEHeader(IntPtr startOfPEFile) : this((void*)startOfPEFile) { }
        public PEHeader(void* startOfPEFile)
        {
            this.dosHeader = (IMAGE_DOS_HEADER*)startOfPEFile;
            this.ntHeader = (IMAGE_NT_HEADERS*)((byte*)startOfPEFile + dosHeader->e_lfanew);
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
        public uint VirtualAddress;
        public uint Size;
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

    #endregion

}
