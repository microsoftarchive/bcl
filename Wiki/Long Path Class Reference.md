The Long Path wrapper project is made up of two classes, [LongPathFile](#LongPathFile) and [LongPathDirectory](#LongPathDirectory).

{anchor:LongPathDirectory}
# LongPathDirectory class

{code:C#}
using System;
using System.Collections.Generic;

namespace Microsoft.Experimental.IO {

    // Summary:
    //     Provides methods for creating, deleting, moving and enumerating directories
    //     and subdirectories with long paths, that is, paths that exceed 259 characters.
    public static class LongPathDirectory {

        // Summary:
        //     Creates the specified directory.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the directory to create.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     path contains one or more directories that could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //
        //   System.IO.IOException:
        //     path is a file.
        //     -or-
        //     path specifies a device that is not ready.
        //
        // Remarks:
        //     Note: Unlike System.IO.Directory.CreateDirectory(System.String), this method
        //     only creates the last directory in path.
        public static void Create(string path);
        
        // Summary:
        //     Deletes the specified empty directory.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the directory to delete.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     path could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //     -or-
        //     path refers to a directory that is read-only.
        //
        //   System.IO.IOException:
        //     path is a file.
        //     -or-
        //     path refers to a directory that is not empty.
        //     -or-
        //     refers to a directory that is in use.
        //     -or-
        //     path specifies a device that is not ready.
        public static void Delete(string path);
        
        // Summary:
        //     Returns a enumerable containing the directory names of the specified directory.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the directory to search.
        //
        // Returns:
        //     A System.Collections.Generic.IEnumerable<T> containing the directory names
        //     within path.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     path contains one or more directories that could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //
        //   System.IO.IOException:
        //     path is a file.
        //     -or-
        //     path specifies a device that is not ready.
        public static IEnumerable<string> EnumerateDirectories(string path);
        
        // Summary:
        //     Returns a enumerable containing the directory names of the specified directory
        //     that match the specified search pattern.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the directory to search.
        //
        //   searchPattern:
        //     A System.String containing search pattern to match against the names of the
        //     directories in , otherwise, null or an empty string ("") to use the default
        //     search pattern, "*".
        //
        // Returns:
        //     A System.Collections.Generic.IEnumerable<T> containing the directory names
        //     within path that match searchPattern.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     path contains one or more directories that could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //
        //   System.IO.IOException:
        //     path is a file.
        //     -or-
        //     path specifies a device that is not ready.
        public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern);
        
        // Summary:
        //     Returns a enumerable containing the file names of the specified directory.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the directory to search.
        //
        // Returns:
        //     A System.Collections.Generic.IEnumerable<T> containing the file names within
        //     path.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     path contains one or more directories that could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //
        //   System.IO.IOException:
        //     path is a file.
        //     -or-
        //     path specifies a device that is not ready.
        public static IEnumerable<string> EnumerateFiles(string path);
        
        // Summary:
        //     Returns a enumerable containing the file names of the specified directory
        //     that match the specified search pattern.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the directory to search.
        //
        //   searchPattern:
        //     A System.String containing search pattern to match against the names of the
        //     files in , otherwise, null or an empty string ("") to use the default search
        //     pattern, "*".
        //
        // Returns:
        //     A System.Collections.Generic.IEnumerable<T> containing the file names within
        //     path that match searchPattern.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     path contains one or more directories that could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //
        //   System.IO.IOException:
        //     path is a file.
        //     -or-
        //     path specifies a device that is not ready.
        public static IEnumerable<string> EnumerateFiles(string path, string searchPattern);
        
        // Summary:
        //     Returns a enumerable containing the file and directory names of the specified
        //     directory.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the directory to search.
        //
        // Returns:
        //     A System.Collections.Generic.IEnumerable<T> containing the file and directory
        //     names within path.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     path contains one or more directories that could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //
        //   System.IO.IOException:
        //     path is a file.
        //     -or-
        //     path specifies a device that is not ready.
        public static IEnumerable<string> EnumerateFileSystemEntries(string path);
        
        // Summary:
        //     Returns a enumerable containing the file and directory names of the specified
        //     directory that match the specified search pattern.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the directory to search.
        //
        //   searchPattern:
        //     A System.String containing search pattern to match against the names of the
        //     files and directories in , otherwise, null or an empty string ("") to use
        //     the default search pattern, "*".
        //
        // Returns:
        //     A System.Collections.Generic.IEnumerable<T> containing the file and directory
        //     names within paththat match searchPattern.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     path contains one or more directories that could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //
        //   System.IO.IOException:
        //     path is a file.
        //     -or-
        //     path specifies a device that is not ready.
        public static IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern);
        
        // Summary:
        //     Returns a value indicating whether the specified path refers to an existing
        //     directory.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path to check.
        //
        // Returns:
        //     true if path refers to an existing directory; otherwise, false.
        //
        // Remarks:
        //     Note that this method will return false if any error occurs while trying
        //     to determine if the specified directory exists. This includes situations
        //     that would normally result in thrown exceptions including (but not limited
        //     to); passing in a directory name with invalid or too many characters, an
        //     I/O error such as a failing or missing disk, or if the caller does not have
        //     Windows or Code Access Security (CAS) permissions to to read the directory.
        public static bool Exists(string path);
    }
}
{code:C#}

{anchor:LongPathFile}
# LongPathFile class

{code:C#}
using System;
using System.IO;

namespace Microsoft.Experimental.IO {

    // Summary:
    //     Provides static methods for creating, copying, deleting, moving, and opening
    //     of files with long paths, that is, paths that exceed 259 characters.
    public static class LongPathFile {

        // Summary:
        //     Copies the specified file to a specified new file, indicating whether to
        //     overwrite an existing file.
        //
        // Parameters:
        //   sourcePath:
        //     A System.String containing the path of the file to copy.
        //
        //   destinationPath:
        //     A System.String containing the new path of the file.
        //
        //   overwrite:
        //     true if destinationPath should be overwritten if it refers to an existing
        //     file, otherwise, false.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     sourcePath and/or destinationPath is null.
        //
        //   System.ArgumentException:
        //     sourcePath and/or destinationPath is an empty string (""), contains only
        //     white space, or contains one or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     sourcePath and/or destinationPath contains one or more components that exceed
        //     the drive-defined maximum length. For example, on Windows-based platforms,
        //     components must not exceed 255 characters.
        //
        //   System.IO.PathTooLongException:
        //     sourcePath and/or destinationPath exceeds the system-defined maximum length.
        //     For example, on Windows-based platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.FileNotFoundException:
        //     sourcePath could not be found.
        //
        //   System.IO.DirectoryNotFoundException:
        //     One or more directories in sourcePath and/or destinationPath could not be
        //     found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //     -or-
        //     overwrite is true and destinationPath refers to a file that is read-only.
        //
        //   System.IO.IOException:
        //     overwrite is false and destinationPath refers to a file that already exists.
        //     -or-
        //     sourcePath and/or destinationPath is a directory.
        //     -or-
        //     overwrite is true and destinationPath refers to a file that already exists
        //     and is in use.
        //     -or-
        //     sourcePath refers to a file that is in use.
        //     -or-
        //     sourcePath and/or destinationPath specifies a device that is not ready.
        public static void Copy(string sourcePath, string destinationPath, bool overwrite);

        //
        // Summary:
        //     Deletes the specified file.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the file to delete.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.FileNotFoundException:
        //     path could not be found.
        //
        //   System.IO.DirectoryNotFoundException:
        //     One or more directories in path could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //     -or-
        //     path refers to a file that is read-only.
        //     -or-
        //     path is a directory.
        //
        //   System.IO.IOException:
        //      refers to a file that is in use.
        //     -or-
        //     path specifies a device that is not ready.
        public static void Delete(string path);
        
        // Summary:
        //     Returns a value indicating whether the specified path refers to an existing
        //     file.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path to check.
        //
        // Returns:
        //     true if path refers to an existing file; otherwise, false.
        //
        // Remarks:
        //     Note that this method will return false if any error occurs while trying
        //     to determine if the specified file exists. This includes situations that
        //     would normally result in thrown exceptions including (but not limited to);
        //     passing in a file name with invalid or too many characters, an I/O error
        //     such as a failing or missing disk, or if the caller does not have Windows
        //     or Code Access Security (CAS) permissions to to read the file.
        public static bool Exists(string path);
        
        // Summary:
        //     Moves the specified file to a new location.
        //
        // Parameters:
        //   sourcePath:
        //     A System.String containing the path of the file to move.
        //
        //   destinationPath:
        //     A System.String containing the new path of the file.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     sourcePath and/or destinationPath is null.
        //
        //   System.ArgumentException:
        //     sourcePath and/or destinationPath is an empty string (""), contains only
        //     white space, or contains one or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     sourcePath and/or destinationPath contains one or more components that exceed
        //     the drive-defined maximum length. For example, on Windows-based platforms,
        //     components must not exceed 255 characters.
        //
        //   System.IO.PathTooLongException:
        //     sourcePath and/or destinationPath exceeds the system-defined maximum length.
        //     For example, on Windows-based platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.FileNotFoundException:
        //     sourcePath could not be found.
        //
        //   System.IO.DirectoryNotFoundException:
        //     One or more directories in sourcePath and/or destinationPath could not be
        //     found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //
        //   System.IO.IOException:
        //     destinationPath refers to a file that already exists.
        //     -or-
        //     sourcePath and/or destinationPath is a directory.
        //     -or-
        //     sourcePath refers to a file that is in use.
        //     -or-
        //     sourcePath and/or destinationPath specifies a device that is not ready.
        public static void Move(string sourcePath, string destinationPath);

        // Summary:
        //     Opens the specified file.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the file to open.
        //
        //   access:
        //     One of the System.IO.FileAccess value that specifies the operations that
        //     can be performed on the file.
        //
        //   mode:
        //     One of the System.IO.FileMode values that specifies whether a file is created
        //     if one does not exist, and determines whether the contents of existing files
        //     are retained or overwritten.
        //
        // Returns:
        //     A System.IO.FileStream that provides access to the file specified in path.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     One or more directories in path could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //     -or-
        //     path refers to a file that is read-only and access is not System.IO.FileAccess.Read.
        //     -or-
        //     path is a directory.
        //
        //   System.IO.IOException:
        //      refers to a file that is in use.
        //     -or-
        //     path specifies a device that is not ready.
        public static FileStream Open(string path, FileMode mode, FileAccess access);
        
        // Summary:
        //     Opens the specified file.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the file to open.
        //
        //   access:
        //     One of the System.IO.FileAccess value that specifies the operations that
        //     can be performed on the file.
        //
        //   mode:
        //     One of the System.IO.FileMode values that specifies whether a file is created
        //     if one does not exist, and determines whether the contents of existing files
        //     are retained or overwritten.
        //
        //   share:
        //     One of the System.IO.FileShare values specifying the type of access other
        //     threads have to the file.
        //
        // Returns:
        //     A System.IO.FileStream that provides access to the file specified in path.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     One or more directories in path could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //     -or-
        //     path refers to a file that is read-only and access is not System.IO.FileAccess.Read.
        //     -or-
        //     path is a directory.
        //
        //   System.IO.IOException:
        //      refers to a file that is in use.
        //     -or-
        //     path specifies a device that is not ready.
        public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share);
        
        // Summary:
        //     Opens the specified file.
        //
        // Parameters:
        //   path:
        //     A System.String containing the path of the file to open.
        //
        //   access:
        //     One of the System.IO.FileAccess value that specifies the operations that
        //     can be performed on the file.
        //
        //   mode:
        //     One of the System.IO.FileMode values that specifies whether a file is created
        //     if one does not exist, and determines whether the contents of existing files
        //     are retained or overwritten.
        //
        //   share:
        //     One of the System.IO.FileShare values specifying the type of access other
        //     threads have to the file.
        //
        //   bufferSize:
        //     An System.Int32 containing the number of bytes to buffer for reads and writes
        //     to the file, or 0 to specified the default buffer size, 1024.
        //
        //   options:
        //     One or more of the System.IO.FileOptions values that describes how to create
        //     or overwrite the file.
        //
        // Returns:
        //     A System.IO.FileStream that provides access to the file specified in path.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     path is null.
        //
        //   System.ArgumentException:
        //     path is an empty string (""), contains only white space, or contains one
        //     or more invalid characters as defined in System.IO.Path.GetInvalidPathChars().
        //     -or-
        //     path contains one or more components that exceed the drive-defined maximum
        //     length. For example, on Windows-based platforms, components must not exceed
        //     255 characters.
        //
        //   System.ArgumentOutOfRangeException:
        //     bufferSize is less than 0.
        //
        //   System.IO.PathTooLongException:
        //     path exceeds the system-defined maximum length. For example, on Windows-based
        //     platforms, paths must not exceed 32,000 characters.
        //
        //   System.IO.DirectoryNotFoundException:
        //     One or more directories in path could not be found.
        //
        //   System.UnauthorizedAccessException:
        //     The caller does not have the required access permissions.
        //     -or-
        //     path refers to a file that is read-only and access is not System.IO.FileAccess.Read.
        //     -or-
        //     path is a directory.
        //
        //   System.IO.IOException:
        //      refers to a file that is in use.
        //     -or-
        //     path specifies a device that is not ready.
        public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options);
    }
}
{code:C#}