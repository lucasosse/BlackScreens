using System;
using System.IO;

namespace BlackScreensWPF
{
    /// <summary>
    /// Start menu shortcuts running BlackScreens commands. Windows search and PowerToys Command Palette
    /// index the Start menu, so each one can be found, given an alias or pinned from there.
    /// </summary>
    public static class Shortcuts
    {
        private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BlackScreens");

        private static readonly (string name, string arguments, string description)[] Entries =
        {
            ("BlackScreens", "settings", "Open BlackScreens settings"),
            ("BlackScreens - Screen 1", "toggle 1", "Black out / restore screen 1"),
            ("BlackScreens - Screen 2", "toggle 2", "Black out / restore screen 2"),
            ("BlackScreens - All screens", "toggle all", "Black out / restore all screens"),
            ("BlackScreens - All but this screen", "toggle others", "Black out every screen except the one with the mouse"),
            ("BlackScreens - Restore all", "off all", "Remove every black screen"),
        };

        /// <summary>
        /// (Re)create the shortcuts, pointing at the running executable
        /// </summary>
        public static void Install()
        {
            string exe = Environment.ProcessPath;
            Directory.CreateDirectory(Folder);
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                foreach (var entry in Entries)
                {
                    dynamic link = shell.CreateShortcut(Path.Combine(Folder, entry.name + ".lnk"));
                    link.TargetPath = exe;
                    link.Arguments = entry.arguments;
                    link.Description = entry.description;
                    link.WorkingDirectory = Path.GetDirectoryName(exe);
                    link.IconLocation = exe + ",0";
                    link.Save();
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
            CommonData.dataInstance.LogToFile.Info("Start menu shortcuts created in " + Folder + " for " + exe);
        }

        public static void Remove()
        {
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, true);
            CommonData.dataInstance.LogToFile.Info("Start menu shortcuts removed");
        }
    }
}
