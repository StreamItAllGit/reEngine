// @module: reEngine.Host.Core.HostNativeFileDialog
// @purpose: Pure Win32 comdlg32.dll native open-file dialog wrapper (zero WinForms dependencies, net8.0 clean) for Media Gate & Media tab pickers.
// @depends-on: System, System.IO, System.Runtime.InteropServices
// @exports: HostNativeFileDialog

using System.Runtime.InteropServices;

namespace reEngine.Host.Core;

/// <summary>
/// Opens native OS file-selection dialogs on Windows via <c>comdlg32.dll</c> without <c>System.Windows.Forms</c>.
/// </summary>
public static unsafe class HostNativeFileDialog
{
    private const uint OfnNoChangeDir = 0x00000008U;
    private const uint OfnPathMustExist = 0x00000800U;
    private const uint OfnFileMustExist = 0x00001000U;
    private const uint OfnExplorer = 0x00080000U;
    internal const uint DefaultPickerFlags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir; // 0x00081808

    private const string MediaFilter =
        "Supported Media (*.cue;*.bin;*.iso;*.chd;*.nes;*.fds;*.7z;*.zip)\0*.cue;*.bin;*.iso;*.chd;*.nes;*.fds;*.7z;*.zip\0All Files (*.*)\0*.*\0\0";

    private const string ModuleFilter =
        "reEngine Modules (*.recomp)\0*.recomp\0All Files (*.*)\0*.*\0\0";

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileNameW
    {
        public uint StructSize;
        public IntPtr HwndOwner;
        public IntPtr HInstance;
        public IntPtr Filter;
        public IntPtr CustomFilter;
        public uint MaxCustFilter;
        public uint FilterIndex;
        public IntPtr File;
        public uint MaxFile;
        public IntPtr FileTitle;
        public uint MaxFileTitle;
        public IntPtr InitialDir;
        public IntPtr Title;
        public uint Flags;
        public ushort FileOffset;
        public ushort FileExtension;
        public IntPtr DefExt;
        public IntPtr CustData;
        public IntPtr Hook;
        public IntPtr TemplateName;
        public IntPtr ReservedPtr;
        public uint ReservedInt;
        public uint FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileNameW ofn);

    /// <summary>Opens a native Win32 file picker for BYO game media (<c>.cue/.bin/.iso/.chd/.nes/.fds/.7z/.zip</c>).</summary>
    public static string? ShowSelectMediaDialog() => ShowSelectMediaDialog(IntPtr.Zero, null);

    /// <summary>Opens a native Win32 file picker for BYO game media owned by <paramref name="ownerHwnd"/>.</summary>
    public static string? ShowSelectMediaDialog(IntPtr ownerHwnd, string? initialDirectory = null) =>
        ShowOpenDialog("Select Game Media", MediaFilter, ownerHwnd, initialDirectory);

    /// <summary>Opens a native Win32 file picker for <c>.recomp</c> modules.</summary>
    public static string? ShowSelectModuleDialog() => ShowSelectModuleDialog(IntPtr.Zero, null);

    /// <summary>Opens a native Win32 file picker for <c>.recomp</c> modules owned by <paramref name="ownerHwnd"/>.</summary>
    public static string? ShowSelectModuleDialog(IntPtr ownerHwnd, string? initialDirectory = null) =>
        ShowOpenDialog("Select .recomp Module", ModuleFilter, ownerHwnd, initialDirectory);

    private static string? ShowOpenDialog(string title, string filter, IntPtr ownerHwnd, string? initialDirectory)
    {
        if (!OperatingSystem.IsWindows()) return null;

        string? safeDir = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? Path.GetFullPath(initialDirectory)
            : null;

        char[] fileBuffer = new char[4096];
        try
        {
            fixed (char* pFile = fileBuffer)
            fixed (char* pFilter = filter)
            fixed (char* pTitle = title)
            fixed (char* pDir = safeDir)
            {
                var ofn = new OpenFileNameW
                {
                    StructSize = (uint)Marshal.SizeOf<OpenFileNameW>(),
                    HwndOwner = ownerHwnd,
                    Filter = (IntPtr)pFilter,
                    FilterIndex = 1,
                    File = (IntPtr)pFile,
                    MaxFile = (uint)fileBuffer.Length,
                    InitialDir = pDir != null ? (IntPtr)pDir : IntPtr.Zero,
                    Title = (IntPtr)pTitle,
                    Flags = DefaultPickerFlags
                };

                if (!GetOpenFileNameW(ref ofn)) return null;
                int len = Array.IndexOf(fileBuffer, '\0');
                return len > 0 ? new string(fileBuffer, 0, len) : null;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            System.Diagnostics.Trace.WriteLine($"Native file dialog unavailable: {ex.Message}");
            return null;
        }
    }
}
