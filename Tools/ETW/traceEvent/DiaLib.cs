using System;

public static class DiaLoader
{
    // Fields
    private static readonly Guid DiaSourceClassGuid = new Guid("{B86AE24D-BF2F-4AC9-B5A2-34B14E4CE11D}");

    // Methods
    static DiaLoader();
    public static IDiaDataSource GetDiaSourceObject();

    // Nested Types
    [ComImport, ComVisible(false), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        void CreateInstance([MarshalAs(UnmanagedType.Interface)] object aggregator, ref Guid refiid, [MarshalAs(UnmanagedType.Interface)] out object createdObject);
        void LockServer(bool incrementRefCount);
    }

    private static class SafeNativeMethods32
    {
        // Methods
        [return: MarshalAs(UnmanagedType.Interface)]
        [DllImport("msdia100.dll", CharSet=CharSet.Unicode, ExactSpelling=true, PreserveSig=false)]
        internal static extern object DllGetClassObject([In, MarshalAs(UnmanagedType.LPStruct)] Guid rclsid, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid);
    }

    private static class SafeNativeMethods64
    {
        // Methods
        [return: MarshalAs(UnmanagedType.Interface)]
        [DllImport("ref/AMD64/msdia100.dll", CharSet=CharSet.Unicode, ExactSpelling=true, PreserveSig=false)]
        internal static extern object DllGetClassObject([In, MarshalAs(UnmanagedType.LPStruct)] Guid rclsid, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid);
    }
}

 
Expand Methods
 

