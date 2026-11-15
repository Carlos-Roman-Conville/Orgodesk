using System.Runtime.InteropServices;

// DLL planting: this assembly's P/Invokes load their DLLs from System32 only.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
