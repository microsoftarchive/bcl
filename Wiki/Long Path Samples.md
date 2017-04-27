If you have used the [System.IO](http://msdn.microsoft.com/en-us/library/system.io.aspx) namespace before, then the [LongPathFile](Long-Path-Class-Reference#LongPathFile) and [LongPathDirectory](Long-Path-Class-Reference#LongPathDirectory) classes will feel very familiar. These classes, where ever possible, attempt to replicate the existing API and behavior of their short path equivalents; the [File](http://msdn.microsoft.com/en-us/library/system.io.file.aspx) and [Directory](http://msdn.microsoft.com/en-us/library/system.io.directory.aspx) classes.

**Note:** 

Unlike the underlying Win32 APIs, the **LongPathDirectory** and **LongPathFile** classes treat short and long directory and file names exactly the same. Because of this, there is no need to prefix long paths with the Win32 specifier _\\?\_ - doing so will result in an **ArgumentException** being thrown for passing in an invalid path.

**Known Limitations:**

* While path lengths have been increased to 32,000 characters, individual components within paths (for example, _Windows_ in _C:\Windows\System32_) remain limited to 255 characters. Passing in a path with a component that exceeds 255 characters, will result in an **ArgumentException** being thrown.

* Currently UNC paths (for example, _\\Server\Share_) are not supported. Support for UNC paths [workitem:will be added in a future version](7589).

### I want to:
[Determine if a file exists](#FileExists)
[Read from a file](#FileRead)
[Write to a file](#FileWrite)
[Delete a file](#FileDelete)
[Rename or move a file](#FileMove)
[Copy a file](#FileCopy)
[Determine if a directory exists](#DirectoryExists)
[Create or delete a directory](#DirectoryCreate)
[Enumerate files and directories](#DirectoryEnumerate)


## Common File Tasks
{anchor:#FileExists}
### Determining if a file exists

The following example determines whether a file named _C:\Temp\TextFile.txt_ exists and writes the result to the console.

{code:C#}
string tempPath = @"C:\Temp\TextFile.txt";

if (LongPathFile.Exists(tempPath)) {

    Console.WriteLine(tempPath + " exists");
}
else {

    Console.WriteLine(tempPath + " does not exist");
}
{code:C#}
**Note:** **LongPathFile.Exists** will return **false** if any error occurs while trying to determine if the specified file exists. This includes situations that would normally result in thrown exceptions including (but not limited to); passing in a file name with invalid or too many characters, an I/O error such as a failing or missing disk, or if the caller does not have Windows permissions to to read the file.

{anchor:#FileRead}
### Reading from a file

The following example opens a existing file named _C:\Temp\TextFile.txt_ and writes its contents to the console.

{code:C#}
using (FileStream stream = LongPathFile.Open(@"C:\Temp\TextFile.txt", FileMode.Open, FileAccess.Read)) {

    using (StreamReader reader = new StreamReader(stream)) {

        string line;
        while ((line = reader.ReadLine()) != null) {
                        
            Console.WriteLine(line);
        }
    }
}
{code:C#}

{anchor:#FileWrite}
### Writing to a file

The following example creates a new file named _C:\Temp\TextFile.txt_, overwriting it if it already exists, and writes some text to it.

{code:C#}
using (FileStream stream = LongPathFile.Open(@"C:\Temp\TextFile.txt", FileMode.Create, FileAccess.Write)) {

    using (StreamWriter writer = new StreamWriter(stream)) {

        writer.Write("Hello World!");
    }
}
{code:C#}

{anchor:#FileDelete}
### Deleting a file

The following example deletes a file named _C:\Temp\TextFile.txt_.

{code:C#}
LongPathFile.Delete(@"C:\Temp\TextFile.txt");
{code:C#}
**Note:** Similar to **File.Delete**, if the specified file is read-only, an **IOException** is thrown.

{anchor:#FileMove}
### Renaming or moving a file

**LongPathFile.Move** can be used for both renaming a file (such as giving it a different extension), and for moving a file to a different directory or drive.

The following example renames a file named _C:\Temp\TextFile.txt_ to _C:\Temp\TextFile.bak_.

{code:C#}
LongPathFile.Move(@"C:\Temp\TextFile.txt", @"C:\Temp\TextFile.bak");
{code:C#}

The following example moves a file named _C:\Temp\TextFile.txt_ to a different directory and drive.

{code:C#}
LongPathFile.Move(@"C:\Temp\TextFile.txt", @"D:\TextFile.txt");
{code:C#}
**Note:** Similar to **File.Move**, if the destination path already exists an **IOException** is thrown.

{anchor:#FileCopy}
### Copying a file
The following examples copies a file named _C:\Temp\TextFile.txt_ to a new file called _C:\Temp\TextFile.bak_, overwriting any existing file at the destination path.

{code:C#}
LongPathFile.Copy(@"C:\Temp\TextFile.txt", @"C:\Temp\TextFile.bak", true);
{code:C#}
**Note:** If the destination path already exists and **false** is passed for _overwrite_, an **IOException** is thrown.

## Common Directory Tasks

{anchor:#DirectoryExists}
### Determining if a directory exists

The following example determines whether a directory named _C:\Temp_ exists and writes the result to the console.

{code:C#}
string tempPath = @"C:\Temp";

if (LongPathDirectory.Exists(tempPath)) {

    Console.WriteLine(tempPath + " exists");
}
else {

    Console.WriteLine(tempPath + " does not exist");
}
{code:C#}
**Note:** **LongPathDirectory.Exists** will return **false** if any error occurs while trying to determine if the specified directory exists. This includes situations that would normally result in thrown exceptions including (but not limited to); passing in a directory name with invalid or too many characters, an I/O error such as a failing or missing disk, or if the caller does not have Windows permissions to to read the directory.

{anchor:#DirectoryCreate}
### Creating and deleting directories

The following example creates a directory named _C:\Temp_.

{code:C#}
LongPathDirectory.Create(@"C:\Temp");
{code:C#}
**Note:** Unlike **Directory.CreateDirectory**, **LongPathDirectory.Create** only creates the last directory in the specified path. If the directory already exists, calling this method is a no-op.

The following example deletes a directory named _C:\Temp_.

{code:C#}
LongPathDirectory.Delete(@"C:\Temp");
{code:C#}
**Note:** Similar to the default overload of **Directory.Delete**, if the specified directory is not empty, an **IOException** is thrown.

{anchor:#DirectoryEnumerate}
### Enumerating files and directories

Like the [Directory](http://msdn.microsoft.com/en-us/library/system.io.directory.aspx) class in .NET 4.0, **LongPathDirectory** has three methods, **EnumerateFiles**, **EnumerateFiles**, and **EnumerateFileSystemEntries** that return the contents of a directory lazily. This allows you to start handling individual files and directories without needing to wait for the file system to return all the contents of the directory.

The following example returns all the files and directories in the user's document directory, and writes them to the console.

{code:C#}
string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

IEnumerable<string> paths = LongPathDirectory.EnumerateFileSystemEntries(documentsPath);

foreach (string path in paths) {

    Console.WriteLine(path);
}
{code:C#}
The results from these methods can also be filtered by passing a search pattern. For example, the following code returns all the executables in the Windows directory and writes them to the console.

{code:C#}
string windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

IEnumerable<string> executables = LongPathDirectory.EnumerateFiles(windowsPath, "*.exe");

foreach (string executable in executables) {

    Console.WriteLine(executable);
}
{code:C#}