using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Apollo.Services;

internal static class ApplicationIconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;

    public static ImageSource? TryGetIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        var fileInfo = new ShellFileInfo();
        try
        {
            IntPtr result = SHGetFileInfo(
                executablePath!,
                0,
                ref fileInfo,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiIcon | ShgfiSmallIcon);

            if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero)
            {
                return null;
            }

            BitmapSource icon = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.IconHandle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            icon.Freeze();
            return icon;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or ExternalException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (fileInfo.IconHandle != IntPtr.Zero)
            {
                DestroyIcon(fileInfo.IconHandle);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}
