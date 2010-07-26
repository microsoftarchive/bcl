// Copyright (c) Microsoft Corporation.  All rights reserved
using System;
using System.Diagnostics;
using System.Reflection;
using System.IO;

public class ResourceUtilities
{
    public static bool UnpackResourceAsFile(string resourceName, string targetFileName)
    {
        return UnpackResourceAsFile(resourceName, targetFileName, System.Reflection.Assembly.GetEntryAssembly());
    }
    public static bool UnpackResourceAsFile(string resourceName, string targetFileName, Assembly sourceAssembly)
    {
        Stream sourceStream = sourceAssembly.GetManifestResourceStream(resourceName);
        if (sourceStream == null)
            return false;

        var dir = Path.GetDirectoryName(targetFileName);
        if (dir.Length > 0)
            Directory.CreateDirectory(dir);     // Create directory if needed.  
        FileUtilities.ForceDelete(targetFileName);
        FileStream targetStream = File.Open(targetFileName, FileMode.Create);
        StreamUtilities.CopyStream(sourceStream, targetStream);
        targetStream.Close();
        return true;
    }
}
