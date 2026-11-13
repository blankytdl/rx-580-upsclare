using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Fsr4Gfx803.ControlCenter.Services;

public sealed record InjectionResult(bool Success, string Message);

public static class NativeInjector
{
    private const uint ProcessCreateThread = 0x0002;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemCommitReserve = 0x3000;
    private const uint PageReadWrite = 0x04;
    private const uint MemRelease = 0x8000;
    private const uint WaitObject0 = 0;
    private const uint ImageFileMachineUnknown = 0;
    private const ushort ImageFileMachineAmd64 = 0x8664;

    public static InjectionResult InjectDll(int processId, string dllPath)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "DLL attachment is available only on Windows.");
        if (!File.Exists(dllPath))
            return new(false, "The selected DLL does not exist.");
        if (!string.Equals(Path.GetExtension(dllPath), ".dll", StringComparison.OrdinalIgnoreCase))
            return new(false, "Choose a .dll file.");
        if (!Environment.Is64BitProcess)
            return new(false, "This build is 64-bit only. Use the x64 application.");
        
        IntPtr process = IntPtr.Zero;
        IntPtr remoteBuffer = IntPtr.Zero;
        IntPtr thread = IntPtr.Zero;
        try
        {
            process = OpenProcess(
                ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation | ProcessVmRead | ProcessVmWrite,
                false, processId);
            if (process == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed. Try running the game normally and check permissions.");

            EnsureTargetIs64Bit(process);

            string fullPath = Path.GetFullPath(dllPath);
            byte[] payload = Encoding.Unicode.GetBytes(fullPath + "\0");
            remoteBuffer = VirtualAllocEx(process, IntPtr.Zero, (nuint)payload.Length, MemCommitReserve, PageReadWrite);
            if (remoteBuffer == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed.");

            if (!WriteProcessMemory(process, remoteBuffer, payload, (nuint)payload.Length, out nuint written) || written != (nuint)payload.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed.");

            IntPtr kernel32 = GetModuleHandleW("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not locate kernel32.dll.");
            IntPtr loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve LoadLibraryW.");

            thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remoteBuffer, 0, out _);
            if (thread == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed. The game may restrict remote thread creation.");

            uint wait = WaitForSingleObject(thread, 15000);
            if (wait != WaitObject0)
                return new(false, wait == 0x102
                    ? "The remote LoadLibrary call timed out. The DLL may still be loading; check the game before trying again."
                    : $"The remote thread returned wait status 0x{wait:X}.");

            if (!GetExitCodeThread(thread, out uint moduleHandle) || moduleHandle == 0)
                return new(false, "LoadLibraryW returned NULL. Check the DLL architecture and its dependencies.");

            return new(true, $"Windows loaded the DLL into PID {processId}. This confirms loading only; it does not prove that the DLL implements FSR4.");
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
        finally
        {
            if (thread != IntPtr.Zero) CloseHandle(thread);
            if (remoteBuffer != IntPtr.Zero && process != IntPtr.Zero)
                VirtualFreeEx(process, remoteBuffer, 0, MemRelease);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    private static void EnsureTargetIs64Bit(IntPtr process)
    {
        try
        {
            if (!IsWow64Process2(process, out ushort processMachine, out ushort nativeMachine))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not determine target architecture.");
            if (nativeMachine != ImageFileMachineAmd64 || processMachine != ImageFileMachineUnknown)
                throw new InvalidOperationException("The selected game is not a native 64-bit process. This injector and payload require x64.");
        }
        catch (EntryPointNotFoundException)
        {
            if (!IsWow64Process(process, out bool wow64))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not determine target architecture.");
            if (wow64 || !Environment.Is64BitOperatingSystem)
                throw new InvalidOperationException("The selected game is not a native 64-bit process. This injector and payload require x64.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint allocationType, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, nuint stackSize, IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string moduleName);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr process, out bool wow64);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
}
