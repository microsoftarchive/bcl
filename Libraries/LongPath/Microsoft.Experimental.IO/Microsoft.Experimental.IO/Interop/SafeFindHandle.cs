//     Copyright (c) Microsoft Corporation.  All rights reserved.
using System;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.Experimental.IO.Interop {
    internal sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid {
        internal SafeFindHandle()
            : base(true) {
        }

        protected override bool ReleaseHandle() {
            return NativeMethods.FindClose(base.handle);
        }
    }
}
