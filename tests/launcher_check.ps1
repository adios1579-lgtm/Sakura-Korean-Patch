# 격리 시험 폴더의 실행기를 숨김 데스크톱에서 띄워 무엇이 일어나는지 기록한다.
#  - 사용자 화면에 창이 뜨지 않고 마우스·포커스를 가져가지 않는다.
#  - 시험 폴더에서 실행된 프로세스(실행기·게임)의 소리만 음소거한다.
#  - 뜬 창(메시지 상자 포함)의 글을 적고, -Button 을 주면 그 글자의 버튼을 누른다.
#  - 끝나면 시험 폴더에서 실행된 프로세스를 모두 끈다.
# PowerShell 7로 실행한다.
param(
    [Parameter(Mandatory)] [string]$Root,
    [Parameter(Mandatory)] [string]$Exe,
    [string]$Arguments = '',
    [int]$Seconds = 14,
    [string]$Button = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -ReferencedAssemblies System.Collections, System.Threading, System.Threading.Thread, System.Linq, System.Runtime.InteropServices, System.Diagnostics.Process, System.ComponentModel.Primitives, System.Console -TypeDefinition @'
using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator { int NotImpl1(); [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice dev); }
[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); }
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2 { int NotImpl1(); int NotImpl2(); [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e); }
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s); }
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    int N0(); int N1(); int N2(); int N3(); int N4(); int N5(); int N6(); int N7(); int N8();
    int N9(); int N10(); [PreserveSig] int GetProcessId(out uint pid); int N12(); int N13();
}
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid ctx); [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute(bool mute, ref Guid ctx); [PreserveSig] int GetMute(out bool mute);
}

public static class LauncherCheck
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateDesktop(string name, IntPtr dev, IntPtr devmode, int flags, uint access, IntPtr sa);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetThreadDesktop(IntPtr d);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    static string Cls(IntPtr h) { var s = new StringBuilder(128); GetClassName(h, s, 128); return s.ToString(); }
    static string Txt(IntPtr h) { var s = new StringBuilder(2048); GetWindowText(h, s, 2048); return s.ToString().Replace("\r", "").Replace("\n", " / "); }

    // 시험 폴더에서 실행된 프로세스만 고른다(사용자의 실제 게임은 건드리지 않는다).
    static List<Process> Ours(string prefix)
    {
        var list = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainModule.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { list.Add(p); continue; } } catch { }
            p.Dispose();
        }
        return list;
    }
    static volatile bool stop; static int mutes;
    static void MuteLoop(string prefix)
    {
        while (!stop)
        {
            try
            {
                var pids = new HashSet<uint>(); foreach (var p in Ours(prefix)) { pids.Add((uint)p.Id); p.Dispose(); }
                if (pids.Count > 0)
                {
                    var en = (IMMDeviceEnumerator)new MMDeviceEnumerator(); IMMDevice dev;
                    if (en.GetDefaultAudioEndpoint(0, 1, out dev) == 0)
                    {
                        Guid iid = typeof(IAudioSessionManager2).GUID; object o;
                        if (dev.Activate(ref iid, 0x17, IntPtr.Zero, out o) == 0)
                        {
                            IAudioSessionEnumerator se;
                            if (((IAudioSessionManager2)o).GetSessionEnumerator(out se) == 0)
                            {
                                int n; se.GetCount(out n);
                                for (int i = 0; i < n; i++)
                                {
                                    IAudioSessionControl2 c; if (se.GetSession(i, out c) != 0) continue;
                                    uint pid; c.GetProcessId(out pid);
                                    if (pids.Contains(pid)) { var v = (ISimpleAudioVolume)c; bool m; v.GetMute(out m); if (!m) { Guid g = Guid.Empty; v.SetMute(true, ref g); mutes++; } }
                                    Marshal.ReleaseComObject(c);
                                }
                                Marshal.ReleaseComObject(se);
                            }
                            Marshal.ReleaseComObject(o);
                        }
                        Marshal.ReleaseComObject(dev);
                    }
                    Marshal.ReleaseComObject(en);
                }
            }
            catch { }
            Thread.Sleep(20);
        }
    }

    public static string Run(string root, string exe, string args, int seconds, string button)
    {
        var sb = new StringBuilder();
        string prefix = System.IO.Path.GetFullPath(root).TrimEnd('\\') + "\\";
        string desktop = "SakuraLauncherCheck_" + Environment.ProcessId;
        IntPtr d = CreateDesktop(desktop, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if (d == IntPtr.Zero) return "CreateDesktop 오류";
        var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO)); si.lpDesktop = desktop;
        PROCESS_INFORMATION pi;
        stop = false; mutes = 0;
        var mute = new Thread(delegate() { MuteLoop(prefix); }); mute.IsBackground = true; mute.Start();
        if (!CreateProcess(exe, new StringBuilder("\"" + exe + "\" " + args), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, root, ref si, out pi))
        { stop = true; CloseDesktop(d); return "CreateProcess 오류 " + Marshal.GetLastWin32Error(); }
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        var worker = new Thread(delegate()
        {
            if (!SetThreadDesktop(d)) { sb.AppendLine("SetThreadDesktop 오류"); return; }
            var start = DateTime.UtcNow; var said = new HashSet<string>(); bool clicked = false;
            while ((DateTime.UtcNow - start).TotalSeconds < seconds)
            {
                Thread.Sleep(700);
                var ours = Ours(prefix); var pids = new HashSet<uint>(); var names = new List<string>();
                foreach (var p in ours) { pids.Add((uint)p.Id); names.Add(p.ProcessName); p.Dispose(); }
                names.Sort(); string line = "프로세스: " + (names.Count == 0 ? "(없음)" : string.Join(", ", names));
                if (said.Add(line)) sb.AppendLine(string.Format("[{0:0.0}초] {1}", (DateTime.UtcNow - start).TotalSeconds, line));
                EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    uint pid; GetWindowThreadProcessId(h, out pid);
                    if (!pids.Contains(pid) || !IsWindowVisible(h) || Cls(h).StartsWith("UAC")) return true;
                    var parts = new List<string>(); IntPtr target = IntPtr.Zero;
                    EnumChildWindows(h, delegate(IntPtr ch, IntPtr x) { string t = Txt(ch); if (t.Length > 0) parts.Add("[" + Cls(ch) + "] " + t); if (button.Length > 0 && Cls(ch) == "Button" && t.Replace("&", "").StartsWith(button)) target = ch; return true; }, IntPtr.Zero);
                    string w = "창 [" + Cls(h) + "] \"" + Txt(h) + "\"" + (parts.Count > 0 ? " :: " + string.Join(" | ", parts) : "");
                    if (said.Add(w)) sb.AppendLine(string.Format("[{0:0.0}초] {1}", (DateTime.UtcNow - start).TotalSeconds, w));
                    if (target != IntPtr.Zero && !clicked) { clicked = true; PostMessage(target, 0x00F5, IntPtr.Zero, IntPtr.Zero); sb.AppendLine("   -> '" + button + "' 버튼 누름"); }
                    return true;
                }, IntPtr.Zero);
            }
        });
        worker.Start(); worker.Join();
        foreach (var p in Ours(prefix)) { try { p.Kill(); p.WaitForExit(5000); sb.AppendLine("종료시킴: " + p.ProcessName); } catch { } p.Dispose(); }
        stop = true; mute.Join(1000);
        sb.AppendLine("음소거 설정 " + mutes + "회");
        CloseDesktop(d);
        return sb.ToString();
    }
}
'@
$save = 'D:\ＳＡＫＵＲＡ～雪月華～\AdvData\SaveData'
function SaveState { $h = @{}; Get-ChildItem -LiteralPath $save -File | ForEach-Object { $h[$_.Name] = "$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }; $h }
$before = SaveState
[LauncherCheck]::Run($Root, (Join-Path $Root $Exe), $Arguments, $Seconds, $Button)
$after = SaveState
$changed = @($after.Keys | Where-Object { $before[$_] -ne $after[$_] }) + @($before.Keys | Where-Object { -not $after.ContainsKey($_) })
"사용자 세이브 폴더 변화: " + $(if ($changed.Count) { $changed -join ', ' } else { '없음' })
$log = Join-Path $Root 'updates\last-check.txt'
if (Test-Path -LiteralPath $log) { "last-check.txt: " + (Get-Content -LiteralPath $log -Raw -Encoding UTF8).Trim() }
