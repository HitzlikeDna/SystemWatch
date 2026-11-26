using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SystemWatch.Native;

namespace SystemWatch.Monitoring;

/// <summary>
/// Liest alle Prozesse mit einem einzigen Systemaufruf (NtQuerySystemInformation) – ohne für
/// jeden Prozess ein Handle zu öffnen. Liefert CPU-Last, privaten Arbeitsspeicher (wie der
/// Task-Manager) und ordnet jeden Prozess als App, Hintergrund- oder Windows-Prozess ein.
/// </summary>
internal sealed class ProcessSampler : IDisposable
{
    /// <summary>
    /// Systemprozesse, deren Pfad ohne Administratorrechte nicht lesbar ist. Andere unlesbare
    /// Prozesse (z. B. Dienste von Drittherstellern) gelten als Hintergrundprozesse.
    /// </summary>
    private static readonly HashSet<string> KnownWindowsImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Secure System", "Memory Compression", "MemCompression",
        "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "LsaIso.exe",
        "svchost.exe", "fontdrvhost.exe", "dwm.exe", "WUDFHost.exe", "spoolsv.exe", "audiodg.exe",
        "WmiPrvSE.exe", "dllhost.exe", "conhost.exe", "SearchIndexer.exe", "MsMpEng.exe", "NisSrv.exe",
        "SecurityHealthService.exe", "SgrmBroker.exe", "MpDefenderCoreService.exe", "unsecapp.exe",
        "TrustedInstaller.exe", "TiWorker.exe", "sppsvc.exe", "wlanext.exe", "dasHost.exe", "upfc.exe",
    };

    private readonly int _processorCount = Environment.ProcessorCount;
    private readonly string[] _windowsDirectories =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\",
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\') + "\\Windows Defender\\",
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).TrimEnd('\\') + "\\Microsoft\\Windows Defender\\",
    ];

    private readonly Dictionary<ProcessIdentity, ProcessMeta> _meta = new();
    private readonly Dictionary<string, ImageMeta> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _windowPids = new();
    private readonly NativeMethods.EnumWindowsProc _enumWindows;
    private readonly NativeMethods.EnumWindowsProc _enumChildWindows;
    private readonly StringBuilder _textBuffer = new(1024);

    private Dictionary<ProcessIdentity, long> _previousCpuTime = new();
    private long _previousTimestamp;

    private IntPtr _buffer;
    private int _bufferSize = 1 << 20;

    private int _frameHostPid;
    private int _frameChildPid;

    public ProcessSampler()
    {
        _enumWindows = OnTopLevelWindow;
        _enumChildWindows = OnFrameChildWindow;
    }

    public void Sample(SystemSnapshot snapshot, IReadOnlyDictionary<int, double> gpuByPid, bool gpuAvailable)
    {
        if (!QuerySystemProcessInformation())
            return;

        long now = Stopwatch.GetTimestamp();
        double elapsed100Ns = _previousTimestamp == 0
            ? 0
            : (now - _previousTimestamp) * 10_000_000.0 / Stopwatch.Frequency;

        CollectWindowOwners();

        var processes = new List<ProcessSample>(_previousCpuTime.Count + 32);
        var cpuTimes = new Dictionary<ProcessIdentity, long>(_previousCpuTime.Count + 32);
        int threads = 0, handles = 0;

        IntPtr entry = _buffer;
        while (true)
        {
            var info = Marshal.PtrToStructure<NativeMethods.SYSTEM_PROCESS_INFORMATION>(entry);
            int pid = (int)info.UniqueProcessId.ToInt64();

            threads += (int)info.NumberOfThreads;
            handles += (int)info.HandleCount;

            if (pid != 0) // 0 = Leerlaufprozess
            {
                var id = new ProcessIdentity(pid, info.CreateTime);
                long cpuTime = info.UserTime + info.KernelTime;
                cpuTimes[id] = cpuTime;

                double cpu = 0;
                if (elapsed100Ns > 0 && _previousCpuTime.TryGetValue(id, out long previousCpuTime))
                    cpu = Math.Min(100, Math.Max(0, (cpuTime - previousCpuTime) / (elapsed100Ns * _processorCount) * 100));

                string imageName = ReadImageName(info.ImageName, pid);
                var meta = GetMeta(id, imageName);

                processes.Add(new ProcessSample
                {
                    Identity = id,
                    ImageName = imageName,
                    DisplayName = meta.DisplayName,
                    ImagePath = meta.Path,
                    Icon = meta.Icon,
                    Category = _windowPids.Contains(pid) ? ProcessCategory.App
                             : meta.IsWindowsComponent ? ProcessCategory.Windows
                             : ProcessCategory.Background,
                    CpuPercent = cpu,
                    PrivateBytes = (ulong)Math.Max(0, info.WorkingSetPrivateSize),
                    GpuPercent = gpuAvailable ? (gpuByPid.TryGetValue(pid, out double gpu) ? gpu : 0) : null,
                    ThreadCount = (int)info.NumberOfThreads,
                });
            }

            if (info.NextEntryOffset == 0)
                break;
            entry = IntPtr.Add(entry, (int)info.NextEntryOffset);
        }

        _previousCpuTime = cpuTimes;
        _previousTimestamp = now;
        PruneMeta(cpuTimes);

        snapshot.Processes = processes;
        snapshot.ProcessCount = processes.Count;
        snapshot.ThreadCount = threads;
        snapshot.HandleCount = handles;
    }

    // ───────────── Rohdaten ─────────────

    private bool QuerySystemProcessInformation()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (_buffer == IntPtr.Zero)
                _buffer = Marshal.AllocHGlobal(_bufferSize);

            int status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemProcessInformation, _buffer, _bufferSize, out int needed);

            if (status != NativeMethods.StatusInfoLengthMismatch)
                return status >= 0;

            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            _bufferSize = Math.Max(_bufferSize * 2, needed + (64 << 10));
        }

        return false;
    }

    private static string ReadImageName(NativeMethods.UNICODE_STRING name, int pid)
    {
        if (name.Buffer == IntPtr.Zero || name.Length == 0)
            return pid == 4 ? "System" : "Unbekannt";
        return Marshal.PtrToStringUni(name.Buffer, name.Length / 2);
    }

    // ───────────── Fenster → App-Erkennung ─────────────

    private void CollectWindowOwners()
    {
        _windowPids.Clear();
        NativeMethods.EnumWindows(_enumWindows, IntPtr.Zero);
    }

    private bool OnTopLevelWindow(IntPtr hwnd, IntPtr lParam)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) ||
            NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != IntPtr.Zero ||
            (NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle) & NativeMethods.WsExToolWindow) != 0 ||
            NativeMethods.GetWindowTextLength(hwnd) == 0)
            return true;

        // Unsichtbar gemachte Fenster (z. B. angehaltene Store-Apps) zählen nicht.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return true;

        NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);
        string className = GetClassName(hwnd);

        if (className == "Progman") // Desktop
            return true;

        if (className == "ApplicationFrameWindow")
        {
            // Store-Apps: Das Fenster gehört dem ApplicationFrameHost, die App steckt im Kindfenster.
            _frameHostPid = pid;
            _frameChildPid = 0;
            NativeMethods.EnumChildWindows(hwnd, _enumChildWindows, IntPtr.Zero);
            if (_frameChildPid != 0)
                _windowPids.Add(_frameChildPid);
            return true;
        }

        _windowPids.Add(pid);
        return true;
    }

    private bool OnFrameChildWindow(IntPtr hwnd, IntPtr lParam)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);
        if (pid != _frameHostPid)
        {
            _frameChildPid = pid;
            return false;
        }

        return true;
    }

    private string GetClassName(IntPtr hwnd)
    {
        _textBuffer.Clear();
        NativeMethods.GetClassName(hwnd, _textBuffer, _textBuffer.Capacity);
        return _textBuffer.ToString();
    }

    // ───────────── Pfad, Beschreibung, Symbol ─────────────

    private ProcessMeta GetMeta(ProcessIdentity id, string imageName)
    {
        if (_meta.TryGetValue(id, out var meta))
            return meta;

        string? path = QueryImagePath(id.Pid);
        ImageMeta? image = path == null ? null : GetImageMeta(path);

        meta = new ProcessMeta(
            path,
            image?.Description is { Length: > 0 } description ? description : FriendlyName(imageName),
            image?.Icon,
            IsWindowsComponent(path, imageName));

        _meta[id] = meta;
        return meta;
    }

    private bool IsWindowsComponent(string? path, string imageName)
    {
        if (path == null)
            return KnownWindowsImages.Contains(imageName);

        foreach (var directory in _windowsDirectories)
        {
            if (path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private string? QueryImagePath(int pid)
    {
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            _textBuffer.Clear();
            int size = _textBuffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, _textBuffer, ref size)
                ? _textBuffer.ToString(0, size)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private ImageMeta GetImageMeta(string path)
    {
        if (_images.TryGetValue(path, out var image))
            return image;

        string? description = null;
        try
        {
            description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
        }
        catch
        {
            // Datei nicht lesbar
        }

        image = new ImageMeta(description, LoadIcon(path));
        _images[path] = image;
        return image;
    }

    private static ImageSource? LoadIcon(string path)
    {
        var icons = new IntPtr[1];
        try
        {
            if (NativeMethods.ExtractIconEx(path, 0, icons, null, 1) == 0 || icons[0] == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            // In ein eigenständiges, eingefrorenes Bitmap kopieren, damit der UI-Thread es nutzen darf.
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            var bitmap = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
                PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (icons[0] != IntPtr.Zero)
                NativeMethods.DestroyIcon(icons[0]);
        }
    }

    private static string FriendlyName(string imageName) =>
        imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? imageName.Substring(0, imageName.Length - 4)
            : imageName;

    private void PruneMeta(Dictionary<ProcessIdentity, long> alive)
    {
        if (_meta.Count <= alive.Count + 64)
            return;

        var dead = new List<ProcessIdentity>();
        foreach (var key in _meta.Keys)
        {
            if (!alive.ContainsKey(key))
                dead.Add(key);
        }

        foreach (var key in dead)
            _meta.Remove(key);
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    private sealed class ProcessMeta(string? path, string displayName, ImageSource? icon, bool isWindowsComponent)
    {
        public string? Path { get; } = path;
        public string DisplayName { get; } = displayName;
        public ImageSource? Icon { get; } = icon;
        public bool IsWindowsComponent { get; } = isWindowsComponent;
    }

    private sealed class ImageMeta(string? description, ImageSource? icon)
    {
        public string? Description { get; } = description;
        public ImageSource? Icon { get; } = icon;
    }
}
