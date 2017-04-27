## ZipArchive Samples

### Zipping/unzipping directories
If you just want to extract an archive to a directory or create an archive from a directory, a single line of code is all you need:

{code:cs}
ZipArchive.ExtractToDirectory("photos.zip", @"photos\summer2010");
{code:cs}

{code:cs}
ZipArchive.CreateFromDirectory(@"docs\attach", "attachment.zip");
{code:cs}

### More advanced manipulation
If you need more sophisticated manipulation of Zip archives, the ZipArchive and ZipArchiveEntry classes need to be used together. The following example extracts only the text files from an archive:

{code:cs}
using (var archive = new ZipArchive("data.zip"))
{
    foreach (var entry in archive.Entries)
    {
        if (entry.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            entry.ExtractToFile(Path.Combine(directory, entry.FullName));
        }
    }
}
{code:cs}

The following sample creates an archive containing one file from the filesystem, and also generates an index of what's included in the archive on the fly:

{code:cs}
using (var archive = new ZipArchive("new.zip", ZipArchiveMode.Create))
{
    var readmeEntry = archive.CreateEntry("Readme.txt");
    using (var writer = new StreamWriter(readmeEntry.Open()))
    {
        writer.WriteLine("Included files: ");
        writer.WriteLine("data.dat");
    }

    archive.CreateEntryFromFile("data.dat", "data.dat");
}
{code:cs}

If you don't want to (or can't) touch the filesystem, ZipArchive can work with just streams. The following sample creates an archive on the output stream that contains one file whose contents are the input stream:

{code:cs}
using (ZipArchive archive = new ZipArchive(outstream, ZipArchiveMode.Create))
{
    ZipArchiveEntry entry = archive.CreateEntry("data.dat");
    using (Stream entryStream = entry.Open())
    {
        instream.CopyTo(entryStream);
    }
}
{code:cs}

### ZipArchiveModes

As shown in the samples above, there are different ZipArchiveMode values that tell the ZipArchive constructor to behave in different ways. Read (the default) provides read-only access. Create allows the creation of archives with some restrictions. For example, in Create mode opening two entries at the same time is an error - the first one must be opened, written to, and then closed before the next one is opened. Update, which was not used in any of the samples, allows arbitrary manipulations of archives. The downside to Update mode is that providing this functionality requires loading the entire archive into memory. Read and Create mode, on the other hand, read and write directly from the underlying store with only a small buffer.