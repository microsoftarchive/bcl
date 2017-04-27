## ZipArchive Class

{code:C#}
namespace Microsoft.Experimental.IO.Compression
{
    public class ZipArchive
    {
        // Summary:
        //     Opens a ZipArchive on the specified path for reading. The specified file
        //     is opened with FileMode.Open.
        // 
        // Parameters:
        //     path:
        //         A string specifying the path on the filesystem to open the archive on.
        //         The path is permitted to specify relative or absolute path information.
        //         Relative path information is interpreted as relative to the current working
        //         directory.
        //
        // Exceptions
        //     ArgumentException:
        //         path is a zero-length string, contains only white space, or contains one or
        //         more invalid characters as defined by InvalidPathChars.
        //     ArgumentNullException:
        //         path is null.
        //     PathTooLongException:
        //         The specified path, file name, or both exceed the system-defined maximum
        //         length. For example, on Windows-based platforms, paths must be less than
        //         248 characters, and file names must be less than 260 characters.
        //     DirectoryNotFoundException:
        //         The specified path is invalid, (for example, it is on an unmapped drive).
        //     IOException:
        //         An I/O error occurred while opening the file. 
        //     UnauthorizedAccessException:
        //         path specified a directory.
        //         -or-
        //         The caller does not have the required permission.
        //     FileNotFoundException:
        //         The file specified in path was not found. 
        //     NotSupportedException:
        //         path is in an invalid format. 
        //     InvalidDataException:
        //         The specified file could not be interpreted as a Zip file.
        public ZipArchive(String path);

        // Summary:
        //     Opens a ZipArchive on the specified path in the specified ZipArchiveMode mode.
        // 
        // Parameters:
        //     path:
        //         A string specifying the path on the filesystem to open the archive on.
        //         The path is permitted to specify relative or absolute path information.
        //         Relative path information is interpreted as relative to the current working
        //         directory.
        //     mode:
        //         See the description of the ZipArchiveMode enum. If Read is specified, the
        //         file is opened with System.IO.FileMode.Open, and will throw a FileNotFoundException
        //         if the file does not exist. If Create is specified, the file is opened with
        //         System.IO.FileMode.CreateNew, and will throw a System.IO.IOException if the
        //         file already exists. If Update is specified, the file is opened with
        //         System.IO.FileMode.OpenOrCreate. If the file exists and is Zip file, its entries
        //         will become accessible, and may be modified, and new entries may be created.
        //         If the file exists and is not a Zip file, a ZipArchiveException will be thrown.
        //         If the file exists and is empty or does not exist, a new Zip file will be created.
        //         Note that creating a Zip file with the ZipArchiveMode.Create mode is more efficient
        //         when creating a new Zip file.
        // 
        // Exceptions
        //     ArgumentException:
        //         path is a zero-length string, contains only white space, or contains one or more
        //         invalid characters as defined by InvalidPathChars.
        //     ArgumentNullException:
        //         path is null.
        //     PathTooLongException:
        //         The specified path, file name, or both exceed the system-defined maximum length.
        //         For example, on Windows-based platforms, paths must be less than 248 characters,
        //         and file names must be less than 260 characters.
        //     DirectoryNotFoundException:
        //         The specified path is invalid, (for example, it is on an unmapped drive).
        //     IOException:
        //         An I/O error occurred while opening the file. 
        //     UnauthorizedAccessException:
        //         path specified a directory.
        //         -or-
        //         The caller does not have the required permission.
        //     ArgumentOutOfRangeException:
        //         mode specified an invalid value.
        //     FileNotFoundException:
        //         The file specified in path was not found. 
        //     NotSupportedException:
        //         path is in an invalid format. 
        //     InvalidDataException:
        //         The specified file could not be interpreted as a Zip file.
        //         -or-
        //         mode is Update and an entry is missing from the archive or
        //         is corrupt and cannot be read.
        //         -or-
        //         mode is Update and an entry is too large to fit into memory.
        public ZipArchive(String path, ZipArchiveMode mode);

        // Summary:
        //     Initializes a new instance of ZipArchive on the given stream for reading.
        // 
        // Parameters:
        //     stream:
        //         The stream containing the archive to be read.
        // 
        // Exceptions
        //     ArgumentException:
        //         The stream is already closed or does not support reading.
        //     ArgumentNullException:
        //         The stream is null.
        //     InvalidDataException:
        //         The contents of the stream could not be interpreted as a Zip archive.
        public ZipArchive(Stream stream);

        // Summary:
        //     Initializes a new instance of ZipArchive on the given stream in the specified mode.
        // 
        // Parameters:
        //     stream:
        //         The input or output stream.
        //     mode:
        //         See the description of the ZipArchiveMode enum. Read requires the stream to support
        //         reading, Create requires the stream to support writing, and Update requires the
        //         stream to support reading, writing, and seeking.
        // 
        // Exceptions
        //     ArgumentException:
        //         The stream is already closed. -or- mode is incompatible with the capabilities
        //         of the stream.
        //     ArgumentNullException:
        //         The stream is null.
        //     ArgumentOutOfRangeException:
        //         mode specified an invalid value.
        //     InvalidDataException:
        //         The contents of the stream could not be interpreted as a Zip file.
        //         -or-
        //         mode is Update and an entry is missing from the archive or is corrupt and
        //         cannot be read.
        //         -or-
        //         mode is Update and an entry is too large to fit into memory.
        public ZipArchive(Stream stream, ZipArchiveMode mode);

        // Summary:
        //     Initializes a new instance of ZipArchive on the given stream in the specified mode,
        //     specifying whether to leave the stream open.
        // 
        // Parameters:
        //     stream:
        //         The input or output stream.
        //     mode:
        //         See the description of the ZipArchiveMode enum. Read requires the stream to
        //         support reading, Create requires the stream to support writing, and Update
        //         requires the stream to support reading, writing, and seeking.
        //     leaveOpen:
        //         true to leave the stream open upon disposing the ZipArchive, otherwise false.
        // 
        // Exceptions
        //     ArgumentException:
        //         The stream is already closed.
        //         -or-
        //         mode is incompatible with the capabilities of the stream.
        //     ArgumentNullException:
        //         The stream is null.
        //     ArgumentOutOfRangeException:
        //         mode specified an invalid value.
        //     InvalidDataException:
        //         The contents of the stream could not be interpreted as a Zip file.
        //         -or-
        //         mode is Update and an entry is missing from the archive or is corrupt and
        //         cannot be read.
        //         -or-
        //         mode is Update and an entry is too large to fit into memory.
        public ZipArchive(Stream stream, ZipArchiveMode mode, Boolean leaveOpen);

        // Summary:
        //     The collection of entries that are currently in the ZipArchive. This may not
        //     accurately represent the actual entries that are present in the underlying file
        //     or stream.
        // 
        // Exceptions
        //     NotSupportedException:
        //         The ZipArchive does not support reading.
        //     ObjectDisposedException:
        //         The ZipArchive has already been closed.
        //     InvalidDataException:
        //         The Zip archive is corrupt and the entries cannot be retrieved.
        public ReadOnlyCollection<ZipArchiveEntry> Entries;

        // Summary:
        //     The ZipArchiveMode that the ZipArchive was initialized with.
        public ZipArchiveMode Mode;

        // Summary:
        //     Releases the unmanaged resources used by ZipArchive and optionally finishes
        //     writing the archive and releases the managed resources.
        // 
        // Parameters:
        //     disposing:
        //         true to finish writing the archive and release unmanaged and managed resources,
        //         false to release only unmanaged resources.
        protected virtual void Dispose(Boolean disposing);

        // Summary:
        //     Creates an empty entry in the Zip archive with the specified entry name. There
        //     are no restrictions on the names of entries. The last write time of the entry
        //     is set to the current time. If an entry with the specified name already exists
        //     in the archive, a second entry will be created that has an identical name.
        // 
        // Parameters:
        //     entryName:
        //         A path relative to the root of the archive, indicating the name of the
        //         entry to be created.
        // 
        // Exceptions
        //     ArgumentException:
        //         entryName is a zero-length string.
        //     ArgumentNullException:
        //         entryName is null.
        //     NotSupportedException:
        //         The ZipArchive does not support writing.
        //     ObjectDisposedException:
        //         The ZipArchive has already been closed.
        // 
        // Returns:
        //     A wrapper for the newly created file entry in the archive.
        public ZipArchiveEntry CreateEntry(String entryName);

        // Summary:
        //     Finishes writing the archive and releases all resources used by the ZipArchive
        //     object, unless the object was constructed with leaveOpen as true. Any streams
        //     from opened entries in the ZipArchive still open will throw exceptions on
        //     subsequent writes, as the underlying streams will have been closed.
        public void Dispose();

        // Summary:
        //     Retrieves a wrapper for the file entry in the archive with the specified name.
        //     Names are compared using ordinal comparison. If there are multiple entries in
        //     the archive with the specified name, the first one found will be returned.
        // 
        // Parameters:
        //     entryName:
        //         A path relative to the root of the archive, identifying the desired entry.
        // 
        // Exceptions
        //     ArgumentException:
        //         entryName is a zero-length string.
        //     ArgumentNullException:
        //         entryName is null.
        //     NotSupportedException:
        //         The ZipArchive does not support reading.
        //     ObjectDisposedException:
        //         The ZipArchive has already been closed.
        //     InvalidDataException:
        //         The Zip archive is corrupt and the entries cannot be retrieved.
        // 
        // Returns:
        //     A wrapper for the file entry in the archive. If no entry in the archive exists
        //     with the specified name, null will be returned.
        public ZipArchiveEntry GetEntry(String entryName);

        // Summary:
        //     Adds a file from the file system to the archive under the specified entry name.
        //     The new entry in the archive will contain the contents of the file. The last
        //     write time of the archive entry is set to the last write time of the file on
        //     the file system. If an entry with the specified name already exists in the
        //     archive, a second entry will be created that has an identical name. If the
        //     specified source file has an invalid last modified time, the first datetime
        //     representable in the Zip timestamp format (midnight on January 1, 1980) will
        //     be used.
        // 
        // Parameters:
        //     sourceFileName:
        //         The path to the file on the file system to be copied from. The path is
        //     permitted to specify relative or absolute path information. Relative path
        //     information is interpreted as relative to the current working directory.
        //     entryName:
        //         The name of the entry to be created.
        // 
        // Exceptions
        //     ArgumentException:
        //         sourceFileName is a zero-length string, contains only white space, or
        //     contains one or more invalid characters as defined by InvalidPathChars.
        //         -or-
        //         entryName is a zero-length string.
        //     ArgumentNullException:
        //         sourceFileName or entryName is null.
        //     PathTooLongException:
        //         In sourceFileName, the specified path, file name, or both exceed the
        //         system-defined maximum length. For example, on Windows-based platforms,
        //         paths must be less than 248 characters, and file names must be less than
        //         260 characters.
        //     DirectoryNotFoundException:
        //         The specified sourceFileName is invalid, (for example, it is on an
        //         unmapped drive).
        //     IOException:
        //         An I/O error occurred while opening the file specified by sourceFileName.
        //     UnauthorizedAccessException:
        //         sourceFileName specified a directory.
        //         -or-
        //         The caller does not have the required permission.
        //     FileNotFoundException:
        //         The file specified in sourceFileName was not found. 
        //     NotSupportedException:
        //         sourceFileName is in an invalid format or the ZipArchive does not support
        //         writing.
        //     ObjectDisposedException:
        //         The ZipArchive has already been closed.
        // 
        // Returns:
        //     A wrapper for the newly created entry.
        public ZipArchiveEntry CreateEntryFromFile(String sourceFileName, String entryName);

        // Summary:
        //     Extracts all of the files in the archive to a directory on the file system.
        //     The specified directory must not exist. This method will create all subdirectories
        //     and the specified directory. If there is an error while extracting the archive,
        //     the archive will remain partially extracted. Each entry will be extracted such
        //     that the extracted file has the same relative path to destinationDirectoryName
        //     as the entry has to the root of the archive. If a file to be archived has an
        //     invalid last modified time, the first datetime representable in the Zip timestamp
        //     format (midnight on January 1, 1980) will be used.
        // 
        // Parameters:
        //     destinationDirectoryName:
        //         The path to the directory on the file system. The directory specified must not
        //         exist. The path is permitted to specify relative or absolute path information.
        //         Relative path information is interpreted as relative to the current working
        //         directory.
        // 
        // Exceptions
        //     ArgumentException:
        //         destinationDirectoryName is a zero-length string, contains only white space,
        //         or contains one or more invalid characters as defined by InvalidPathChars.
        //     ArgumentNullException:
        //         destinationDirectoryName is null.
        //     PathTooLongException:
        //         The specified path, file name, or both exceed the system-defined maximum
        //         length. For example, on Windows-based platforms, paths must be less than
        //         248 characters, and file names must be less than 260 characters.
        //     DirectoryNotFoundException:
        //         The specified path is invalid, (for example, it is on an unmapped drive).
        //     IOException:
        //         The directory specified by destinationDirectoryName already exists.
        //         -or-
        //         An archive entry’s name is zero-length, contains only white space, or contains
        //         one or more invalid characters as defined by InvalidPathChars.
        //         -or-
        //         Extracting an archive entry would have resulted in a destination file that is
        //         outside destinationDirectoryName (for example, if the entry name contains
        //         parent directory accessors).
        //         -or-
        //         An archive entry has the same name as an already extracted entry from the
        //         same archive.
        //     UnauthorizedAccessException:
        //         The caller does not have the required permission.
        //     NotSupportedException:
        //         destinationDirectoryName is in an invalid format. 
        //     InvalidDataException:
        //         An archive entry was not found or was corrupt.
        //         -or-
        //         An archive entry has been compressed using a compression method that is not
        //         supported.
        public void ExtractToDirectory(String destinationDirectoryName);

        // Summary:
        //     Creates a Zip archive at the path destinationArchive that contains the files and
        //     directories in the directory specified by sourceDirectoryName. The directory
        //     structure is preserved in the archive, and a recursive search is done for files
        //     to be archived. The archive must not exist. If the directory is empty, an empty
        //     archive will be created. If a file in the directory cannot be added to the archive,
        //     the archive will be left incomplete and invalid and the method will throw an
        //     exception. This method optionally includes the base directory in the archive.
        //     If an error is encountered while adding files to the archive, this method will
        //     stop adding files and leave the archive in an invalid state. The paths are
        //     permitted to specify relative or absolute path information. Relative path
        //     information is interpreted as relative to the current working directory. If a
        //     file in the archive has data in the last write time field that is not a valid
        //     Zip timestamp, an indicator value of 1980 January 1 at midnight will be used for
        //     the file’s last modified time.
        // 
        // Parameters:
        //     sourceDirectoryName:
        //         The path to the directory on the file system to be archived.
        //     destinationArchive:
        //         The name of the archive to be created.
        //     includeBaseDirectory:
        //         True to indicate that a directory named sourceDirectoryName should be included
        //         at the root of the archive. False to indicate that the files and directories
        //         in sourceDirectoryName should be included directly in the archive.
        // 
        // Exceptions
        //     ArgumentException:
        //         sourceDirectoryName or destinationArchive is a zero-length string, contains
        //         only white space, or contains one or more invalid characters as defined by
        //         InvalidPathChars.
        //     ArgumentNullException:
        //         sourceDirectoryName or destinationArchive is null.
        //     PathTooLongException:
        //         In sourceDirectoryName or destinationArchive, the specified path, file name,
        //         or both exceed the system-defined maximum length. For example, on Windows
        //         based platforms, paths must be less than 248 characters, and file names
        //         must be less than 260 characters.
        //     DirectoryNotFoundException:
        //         The path specified in sourceDirectoryName or destinationArchive is invalid,
        //         (for example, it is on an unmapped drive).
        //         -or-
        //         The directory specified by sourceDirectoryName does not exist.
        //     IOException:
        //         destinationArchive exists.
        //         -or-
        //         An I/O error occurred while opening a file to be archived.
        //     UnauthorizedAccessException:
        //         destinationArchive specified a directory.
        //         -or-
        //         The caller does not have the required permission.
        //     NotSupportedException:
        //         sourceDirectoryName or destinationArchive is in an invalid format.
        public static void CreateFromDirectory(String sourceDirectoryName,
                                               String destinationArchive,
                                               Boolean includeBaseDirectory);

        // Summary:
        //     Creates a Zip archive at the path destinationArchive that contains the files
        //     and directories in the directory specified by sourceDirectoryName. The directory
        //     structure is preserved in the archive, and a recursive search is done for files
        //     to be archived. The archive must not exist. If the directory is empty, an empty
        //     archive will be created. If a file in the directory cannot be added to the
        //     archive, the archive will be left incomplete and invalid and the method will
        //     throw an exception. This method does not include the base directory in the archive.
        //     If an error is encountered while adding files to the archive, this method will
        //     stop adding files and leave the archive in an invalid state. The paths are
        //     permitted to specify relative or absolute path information. Relative path
        //     information is interpreted as relative to the current working directory. If a
        //     file in the archive has data in the last write time field that is not a valid Zip
        //     timestamp, an indicator value of 1980 January 1 at midnight will be used for the
        //     file’s last modified time.
        // 
        // Parameters:
        //     sourceDirectoryName:
        //         The path to the directory on the file system to be archived. 
        //     destinationArchive:
        //         The name of the archive to be created.
        // 
        // Exceptions
        //     ArgumentException:
        //         sourceDirectoryName or destinationArchive is a zero-length string, contains
        //         only white space, or contains one or more invalid characters as defined by
        //         InvalidPathChars.
        //     ArgumentNullException:
        //         sourceDirectoryName or destinationArchive is null.
        //     PathTooLongException:
        //         In sourceDirectoryName or destinationArchive, the specified path, file name,
        //         or both exceed the system-defined maximum length. For example, on Windows-based
        //         platforms, paths must be less than 248 characters, and file names must be less
        //         than 260 characters.
        //     DirectoryNotFoundException:
        //         The path specified in sourceDirectoryName or destinationArchive is invalid,
        //         (for example, it is on an unmapped drive).
        //         -or-
        //         The directory specified by sourceDirectoryName does not exist.
        //     IOException:
        //         destinationArchive exists.
        //         -or-
        //         An I/O error occurred while opening a file to be archived.
        //     UnauthorizedAccessException:
        //         destinationArchive specified a directory.
        //         -or-
        //         The caller does not have the required permission.
        //     NotSupportedException:
        //         sourceDirectoryName or destinationArchive is in an invalid format.
        public static void CreateFromDirectory(String sourceDirectoryName, String destinationArchive);

        // Summary:
        //     Extracts all of the files in the specified archive to a directory on the file
        //     system. The specified directory must not exist. This method will create all
        //     subdirectories and the specified directory. If there is an error while extracting
        //     the archive, the archive will remain partially extracted. Each entry will be
        //     extracted such that the extracted file has the same relative path to the
        //     destinationDirectoryName as the entry has to the archive. The path is permitted
        //     to specify relative or absolute path information. Relative path information is
        //     interpreted as relative to the current working directory. If a file to be archived
        //     has an invalid last modified time, the first datetime representable in the Zip
        //     timestamp format (midnight on January 1, 1980) will be used.
        // 
        // Parameters:
        //     sourceArchive:
        //         The path to the archive on the file system that is to be extracted.
        //     destinationDirectoryName:
        //         The path to the directory on the file system. The directory specified must
        //         not exist, but the directory that it is contained in must exist.
        // 
        // Exceptions
        //     ArgumentException:
        //         sourceArchive or destinationDirectoryName is a zero-length string, contains
        //         only white space, or contains one or more invalid characters as defined by
        //         InvalidPathChars.
        //     ArgumentNullException:
        //         sourceArchive or destinationDirectoryName is null.
        //     PathTooLongException:
        //         sourceArchive or destinationDirectoryName specifies a path, file name, or
        //         both exceed the system-defined maximum length. For example, on Windows-based
        //         platforms, paths must be less than 248 characters, and file names must be less
        //         than 260 characters.
        //     DirectoryNotFoundException:
        //         The path specified by sourceArchive or destinationDirectoryName is invalid,
        //         (for example, it is on an unmapped drive).
        //     IOException:
        //         The directory specified by destinationDirectoryName already exists.
        //         -or-
        //         An I/O error has occurred.
        //         -or-
        //         An archive entry’s name is zero-length, contains only white space, or
        //         contains one or more invalid characters as defined by InvalidPathChars.
        //         -or-
        //         Extracting an archive entry would result in a file destination that is
        //         outside the destination directory (for example, because of parent directory
        //         accessors).
        //         -or-
        //         An archive entry has the same name as an already extracted entry from the
        //         same archive.
        //     UnauthorizedAccessException:
        //         The caller does not have the required permission.
        //     NotSupportedException:
        //         sourceArchive or destinationDirectoryName is in an invalid format. 
        //     FileNotFoundException:
        //         sourceArchive was not found.
        //     InvalidDataException:
        //         The archive specified by sourceArchive: Is not a valid ZipArchive
        //         -or-
        //         An archive entry was not found or was corrupt.
        //         -or-
        //         An archive entry has been compressed using a compression method that is
        //         not supported.
        public static void ExtractToDirectory(String sourceArchive, String destinationDirectoryName);
    }
}
{code:C#}

## ZipArchiveEntry Class

{code:C#}
namespace Microsoft.Experimental.IO.Compression
{
    public class ZipArchiveEntry
    {
        // Summary:
        //     The last write time of the entry as stored in the Zip archive.
        //     When setting this property, the DateTime will be converted to
        //     the Zip timestamp format, which supports a resolution of two
        //     seconds. If the data in the last write time field is not a
        //     valid Zip timestamp, an indicator value of 1980 January 1
        //     at midnight will be returned.
        //
        // Exceptions:
        //     NotSupportedException:
        //         An attempt to set this property was made, but the ZipArchive
        //         that this entry belongs to was opened in read-only mode.
        //     ArgumentOutOfRangeException:
        //         An attempt was made to set this property to a value that
        //         cannot be represented in the Zip timestamp format. The
        //         earliest date/time that can be represented is 1980
        //         January 1 0:00:00 (midnight), and the last date/time that
        //         can be represented is 2107 December 31 23:59:58 (one second
        //         before midnight).
        public DateTimeOffset LastWriteTime;

        // Summary:
        //     The relative path of the entry as stored in the Zip archive.
        //     Note that Zip archives allow any string to be the path of the
        //     entry, including invalid and absolute paths.
        public String FullName;

        // Summary:
        //     The filename of the entry. This is equivalent to the substring
        //     of Fullname that follows the final directory separator character.
        public String Name;

        // Summary:
        //     The compressed size of the entry. If the archive that the entry
        //     belongs to is in Create mode, attempts to get this property will
        //     always throw an exception. If the archive that the entry belongs
        //     to is in update mode, this property will only be valid if the
        //     entry has not been opened. 
        //
        // Exceptions:
        //     InvalidOperationException:
        //         This property is not available because the entry has been
        //         written to or modified.
        public Int64 CompressedLength;

        // Summary:
        //     The uncompressed size of the entry. This property is not valid
        //     in Create mode, and it is only valid in Update mode if the entry
        //     has not been opened.
        //
        // Exceptions:
        //     InvalidOperationException:
        //         This property is not available because the entry has been
        //         written to or modified.
        public Int64 Length;

        // Summary:
        //     The ZipArchive that this entry belongs to. If this entry has
        //     been deleted, this will return null.
        public ZipArchive Archive;

        // Summary:
        //     Opens the entry. If the archive that the entry belongs to was
        //     opened in Read mode, the returned stream will be readable, and
        //     it may or may not be seekable. If Create mode, the returned stream
        //     will be writeable and not seekable. If Update mode, the returned
        //     stream will be readable, writeable, seekable, and support SetLength.
        //
        // Exceptions:
        //     IOException:
        //         The entry is already currently open for writing.
        //         -or-
        //         The entry has been deleted from the archive.
        //         -or-
        //         The archive that this entry belongs to was opened in
        //         ZipArchiveMode.Create, and this entry has already been written
        //         to once.
        //     InvalidDataException:
        //         The entry is missing from the archive or is corrupt and cannot
        //         be read.
        //         -or-
        //         The entry has been compressed using a compression method that
        //         is not supported.
        //     ObjectDisposedException:
        //         The ZipArchive that this entry belongs to has been disposed.
        // 
        // Returns:
        //     A Stream that represents the contents of the entry.
        public Stream Open();

        // Summary:
        //     Deletes the entry from the archive.
        //
        // Exceptions:
        //     IOException:
        //         The entry is already open for reading or writing.
        //     NotSupportedException:
        //         The ZipArchive that this entry belongs to was opened in a mode
        //         other than ZipArchiveMode.Update. 
        //     ObjectDisposedException:
        //         The ZipArchive that this entry belongs to has been disposed.
        public void Delete();

        // Summary:
        //     Returns the FullName of the entry.
        // 
        // Returns:
        //     FullName of the entry
        public override String ToString();

        // Summary:
        //     Creates a file on the file system with the entry’s contents and the
        //     specified name. The last write time of the file is set to the
        //     entry’s last write time. This method does allows overwriting of
        //     an existing file with the same name.
        // 
        // Parameters:
        //     destinationFileName:
        //         The name of the file that will hold the contents of the entry.
        //         The path is permitted to specify relative or absolute path
        //         information. Relative path information is interpreted as
        //         relative to the current working directory.
        //     overwrite:
        //         True to indicate overwrite.
        // 
        // Exceptions:
        //     UnauthorizedAccessException:
        //         The caller does not have the required permission.
        //     ArgumentException:
        //         destinationFileName is a zero-length string, contains only
        //         white space, or contains one or more invalid characters as
        //         defined by InvalidPathChars.
        //         -or-
        //         destinationFileName specifies a directory.
        //     ArgumentNullException:
        //         destinationFileName is null.
        //     PathTooLongException:
        //         The specified path, file name, or both exceed the system-
        //         defined maximum length. For example, on Windows-based platforms,
        //         paths must be less than 248 characters, and file names must be
        //         less than 260 characters.
        //     DirectoryNotFoundException:
        //         The path specified in destinationFileName is invalid (for example,
        //         it is on an unmapped drive).
        //     IOException:
        //         destinationFileName exists and overwrite is false.
        //         -or-
        //         An I/O error has occurred.
        //         -or-
        //         The entry is currently open for writing.
        //         -or-
        //         The entry has been deleted from the archive.
        //     NotSupportedException:
        //         destinationFileName is in an invalid format
        //         -or-
        //         The ZipArchive that this entry belongs to was opened in a
        //         write-only mode.
        //     InvalidDataException:
        //         The entry is missing from the archive or is corrupt and cannot
        //         be read
        //         -or-
        //         The entry has been compressed using a compression method that
        //         is not supported.
        //     ObjectDisposedException:
        //         The ZipArchive that this entry belongs to has been disposed.
        public void ExtractToFile(String destinationFileName, Boolean overwrite);

        // Summary:
        //     Creates a file on the file system with the entry’s contents and the
        //     specified name. The last write time of the file is set to the entry’s
        //     last write time. This method does not allow overwriting of an existing
        //     file with the same name. Attempting to extract explicit directories
        //     (entries with names that end in directory separator characters) will
        //     not result in the creation of a directory.
        // 
        // Parameters:
        //     destinationFileName:
        //         The name of the file that will hold the contents of the entry.
        //         The path is permitted to specify relative or absolute path
        //         information. Relative path information is interpreted as
        //         relative to the current working directory.
        // 
        // Exceptions:
        //     UnauthorizedAccessException:
        //         The caller does not have the required permission.
        //     ArgumentException:
        //         destinationFileName is a zero-length string, contains only white
        //         space, or contains one or more invalid characters as defined by
        //         InvalidPathChars.
        //         -or-
        //         destinationFileName specifies a directory.
        //     ArgumentNullException:
        //         destinationFileName is null.
        //     PathTooLongException:
        //         The specified path, file name, or both exceed the system-defined
        //         maximum length. For example, on Windows-based platforms, paths
        //         must be less than 248 characters, and file names must be less
        //         than 260 characters.
        //     DirectoryNotFoundException:
        //         The path specified in destinationFileName is invalid (for example,
        //         it is on an unmapped drive).
        //     IOException:
        //         destinationFileName exists.
        //         -or-
        //         An I/O error has occurred.
        //         -or-
        //         The entry is currently open for writing.
        //         -or-
        //         The entry has been deleted from the archive.
        //     NotSupportedException:
        //         destinationFileName is in an invalid format
        //         -or-
        //         The ZipArchive that this entry belongs to was opened in a
        //         write-only mode.
        //     InvalidDataException:
        //         The entry is missing from the archive or is corrupt and cannot
        //         be read
        //         -or-
        //         The entry has been compressed using a compression method that
        //         is not supported.
        //     ObjectDisposedException:
        //         The ZipArchive that this entry belongs to has been disposed.
        public void ExtractToFile(String destinationFileName);
    }
}
{code:C#}

## ZipArchiveMode Enum

{code:C#}
namespace Microsoft.Experimental.IO.Compression
{
    public enum ZipArchiveMode
    {
        // Summary:
        //     Only reading entries from the archive is permitted. If the underlying
        //     file or stream is seekable, then files will be read from the archive
        //     on-demand as they are requested. If the underlying file or stream is not
        //     seekable, the entire archive will be held in memory. Requires that the
        //     underlying file or stream is readable.
        Read,

        // Summary:
        //     Only supports the creation of new archives. Only writing to newly created
        //     entries in the archive is permitted. Each entry in the archive can only
        //     be opened for writing once. If only one entry is written to at a time, data
        //     will be written to the underlying stream or file as soon as it is available.
        //     The underlying stream must be writeable, but need not be seekable.
        Create,

        // Summary:
        //     Reading and writing from entries in the archive is permitted. Requires that
        //     the contents of the entire archive be held in memory. The underlying file
        //     or stream must be readable, writeable and seekable. No data will be written
        //     to the underlying file or stream until the archive is disposed.
        Update
    }
}
{code:C#}