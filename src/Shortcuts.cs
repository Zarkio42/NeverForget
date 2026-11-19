// .lnk shortcuts on the Desktop, Start Menu and the user Startup folder,
// created through the standard Shell API (IShellLink), the same way installers do.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NeverForget
{
    public static class Shortcuts
    {
        static string ExePath { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        static string LinkName { get { return Program.AppName + ".lnk"; } }

        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LinkName); } }
        static string StartupLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), LinkName); } }

        public static void CreateDesktop()
        {
            Create(DesktopLink, "");
            Create(StartMenuLink, "");
        }

        public static bool AutostartEnabled() { return File.Exists(StartupLink); }

        public static void SetAutostart(bool on)
        {
            if (on) Create(StartupLink, "--tray");
            else if (File.Exists(StartupLink)) File.Delete(StartupLink);
        }

        public static void RemoveAll()
        {
            foreach (var p in new[] { DesktopLink, StartupLink, StartMenuLink })
                if (File.Exists(p)) File.Delete(p);
        }

        static void Create(string linkPath, string args)
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(ExePath);
            link.SetArguments(args);
            link.SetWorkingDirectory(Path.GetDirectoryName(ExePath));
            link.SetDescription("Sticky notes and daily tasks");
            string ico = Path.Combine(Path.GetDirectoryName(ExePath), "neverforget.ico");
            link.SetIconLocation(File.Exists(ico) ? ico : ExePath, 0);
            ((IPersistFile)link).Save(linkPath, true);
            Marshal.ReleaseComObject(link);
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }
    }
}
