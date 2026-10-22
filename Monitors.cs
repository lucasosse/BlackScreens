using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BlackScreensWPF
{
    /// <summary>
    /// One connected monitor, read fresh from Windows each time it is needed
    /// </summary>
    public sealed class MonitorInfo
    {
        /// <summary>GDI name like "\\.\DISPLAY1". Windows can reassign these after sleep or reconnect.</summary>
        public string DeviceName { get; init; }
        /// <summary>Stable id of the physical monitor (EDID based device path), falls back to DeviceName</summary>
        public string DevicePath { get; init; }
        /// <summary>Name like "LG ULTRAGEAR"</summary>
        public string FriendlyName { get; init; }
        public Rectangle Bounds { get; init; }
        public bool Primary { get; init; }
    }

    /// <summary>
    /// Monitor enumeration. Nothing is cached: native display handles go stale after sleep or a monitor
    /// reconnect, which used to crash the app on the next hotkey press.
    /// </summary>
    public static class Monitors
    {
        /// <summary>
        /// All active monitors, numbered by position (left to right, then top to bottom), so that
        /// "screen 1" stays the same physical monitor even when Windows reorders its display list.
        /// </summary>
        public static IReadOnlyList<MonitorInfo> GetAll()
        {
            Dictionary<string, (string friendlyName, string devicePath)> names = QueryNames();
            List<MonitorInfo> monitors = new List<MonitorInfo>();
            foreach (Screen screen in Screen.AllScreens)
            {
                names.TryGetValue(screen.DeviceName, out var name);
                monitors.Add(new MonitorInfo
                {
                    DeviceName = screen.DeviceName,
                    DevicePath = String.IsNullOrEmpty(name.devicePath) ? screen.DeviceName : name.devicePath,
                    FriendlyName = String.IsNullOrEmpty(name.friendlyName) ? screen.DeviceName.TrimStart('\\', '.') : name.friendlyName,
                    Bounds = screen.Bounds,
                    Primary = screen.Primary
                });
            }
            return monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
        }

        /// <summary>
        /// Map GDI device name -> (friendly name, device path) through QueryDisplayConfig.
        /// Returns an empty map on any failure, callers then fall back to GDI names.
        /// </summary>
        private static Dictionary<string, (string, string)> QueryNames()
        {
            Dictionary<string, (string, string)> result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
                    return result;
                DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                    return result;

                for (int i = 0; i < pathCount; i++)
                {
                    DISPLAYCONFIG_SOURCE_DEVICE_NAME source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    source.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    source.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                    source.header.adapterId = paths[i].sourceInfo.adapterId;
                    source.header.id = paths[i].sourceInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref source) != 0)
                        continue;

                    DISPLAYCONFIG_TARGET_DEVICE_NAME target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    target.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                    target.header.adapterId = paths[i].targetInfo.adapterId;
                    target.header.id = paths[i].targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref target) != 0)
                        continue;

                    // First path wins when a source is cloned to several targets
                    if (!result.ContainsKey(source.viewGdiDeviceName))
                        result[source.viewGdiDeviceName] = (target.monitorFriendlyDeviceName, target.monitorDevicePath);
                }
            }
            catch (Exception ex)
            {
                CommonData.dataInstance.LogToFile.Warn(ex, "Monitors.QueryNames() failed");
            }
            return result;
        }

        #region Win32

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public uint refreshRateNumerator;
            public uint refreshRateDenominator;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        // Only the header is read; the 48 byte union (target/source/desktop image mode) is left opaque
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            [FieldOffset(0)] public uint infoType;
            [FieldOffset(4)] public uint id;
            [FieldOffset(8)] public LUID adapterId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        #endregion
    }
}
