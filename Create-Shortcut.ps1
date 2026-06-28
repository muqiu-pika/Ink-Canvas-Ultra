param(
    [string]$OutputDir = "",
    [string]$ExePath = ""
)

if (-not $OutputDir) {
    $OutputDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

if (-not $ExePath) {
    $ExePath = Join-Path $OutputDir "Ink Canvas Ultra.exe"
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Error: Exe not found at $ExePath"
    exit 1
}

$ExeName = "Ink Canvas Ultra.exe"
$ShortcutPath = Join-Path $OutputDir "视频展台.lnk"

if (Test-Path $ShortcutPath) {
    Remove-Item $ShortcutPath -Force
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ShellShortcut {
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    public class CShellLink { }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellLinkW {
        void GetPath(StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory(StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments(StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistFile {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    public static class Creator {
        public static void Create(string lnkPath, string targetAbsPath, string args, string iconPath, int iconIndex) {
            IShellLinkW link = (IShellLinkW)new CShellLink();
            link.SetPath(targetAbsPath);
            link.SetArguments(args);
            link.SetDescription("视频展台模式");
            link.SetIconLocation(iconPath, iconIndex);
            link.SetWorkingDirectory("");
            link.SetShowCmd(1);
            link.SetRelativePath(lnkPath, 0);
            IPersistFile pf = (IPersistFile)link;
            pf.Save(lnkPath, true);
            Marshal.ReleaseComObject(pf);
            Marshal.ReleaseComObject(link);
        }
    }
}
"@

[ShellShortcut.Creator]::Create($ShortcutPath, $ExePath, "--video-presenter", $ExePath, 0)

Write-Host "Shortcut created: $ShortcutPath"
Write-Host "  Target: $ExeName (portable - resolves relative to shortcut location)"
Write-Host "  Arguments: --video-presenter"
