﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Documents
{
    using System.Runtime.InteropServices;
    internal static partial class NativeMethods
    {
        public static class Windows
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct RTL_OSVERSIONINFOEX
            {
                internal uint dwOSVersionInfoSize;
                internal uint dwMajorVersion;
                internal uint dwMinorVersion;
                internal uint dwBuildNumber;
                internal uint dwPlatformId;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                internal string szCSDVersion;
            }

            // This call avoids the shimming Windows does to report old versions
            [DllImport("ntdll")]
            private static extern int RtlGetVersion(out RTL_OSVERSIONINFOEX lpVersionInformation);

            internal static string RtlGetVersion()
            {
                RTL_OSVERSIONINFOEX osvi = new RTL_OSVERSIONINFOEX();
                osvi.dwOSVersionInfoSize = (uint)Marshal.SizeOf(osvi);
                if (RtlGetVersion(out osvi) == 0)
                {
                    return $"{osvi.dwMajorVersion}.{osvi.dwMinorVersion}.{osvi.dwBuildNumber}";
                }
                else
                {
                    return null;
                }
            }
        }
    }
}