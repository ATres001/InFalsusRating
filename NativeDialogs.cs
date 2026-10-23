using System;
using System.Runtime.InteropServices;
using System.Text;

namespace InFalsusRating;

public static class NativeDialogs
{
    const uint BIF_RETURNONLYFSDIRS = 0x0001;
    const uint BIF_EDITBOX = 0x0010;
    const uint BIF_NEWDIALOGSTYLE = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct BROWSEINFOW
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHBrowseForFolderW(ref BROWSEINFOW lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder pszPath);

    [DllImport("ole32.dll")]
    static extern void CoTaskMemFree(IntPtr pv);

        public static string PickFolder(string title, IntPtr owner)
    {
        var bi = new BROWSEINFOW
        {
            hwndOwner = owner,
            lpszTitle = title ?? "",
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_EDITBOX | BIF_NEWDIALOGSTYLE,
        };
        IntPtr pidl = IntPtr.Zero;
        try { pidl = SHBrowseForFolderW(ref bi); }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[面板] 选择文件夹失败：" + ex.Message);
            return null;
        }
        if (pidl == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            return SHGetPathFromIDListW(pidl, sb) ? sb.ToString() : null;
        }
        finally { CoTaskMemFree(pidl); }
    }

    const int OFN_NOCHANGEDIR = 0x00000008;
    const int OFN_PATHMUSTEXIST = 0x00000800;
    const int OFN_FILEMUSTEXIST = 0x00001000;
    const int OFN_EXPLORER = 0x00080000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OPENFILENAMEW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public StringBuilder lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetOpenFileNameW(ref OPENFILENAMEW ofn);

        public static string PickFile(string title, string filter, string initialDir, IntPtr owner)
    {
        var sb = new StringBuilder(1024);
        var ofn = new OPENFILENAMEW
        {
            lStructSize = Marshal.SizeOf(typeof(OPENFILENAMEW)),
            hwndOwner = owner,
            lpstrFilter = filter ?? "",
            nFilterIndex = 1,
            lpstrFile = sb,
            nMaxFile = sb.Capacity,
            lpstrInitialDir = string.IsNullOrEmpty(initialDir) ? null : initialDir,
            lpstrTitle = title ?? "",
            Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER,
        };
        try
        {
            return GetOpenFileNameW(ref ofn) ? sb.ToString() : null;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[面板] 选择文件失败：" + ex.Message);
            return null;
        }
    }
}
