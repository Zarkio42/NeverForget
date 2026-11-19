using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("NeverForget")]
[assembly: AssemblyDescription("Sticky notes, Kanban board and daily tasks")]
[assembly: AssemblyCompany("NeverForget contributors")]
[assembly: AssemblyProduct("NeverForget")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

// Every P/Invoke in this assembly targets a Windows system DLL (winsqlite3, user32, dwmapi):
// load them only from System32, never from the application folder (prevents DLL planting).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
