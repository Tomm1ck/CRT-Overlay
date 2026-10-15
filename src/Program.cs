using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace CrtOverlay;

internal static class Program
{
    private const string InstanceName = "Local\\CrtOverlay.Instance.v2", StopName = "Local\\CrtOverlay.Stop.v2";
    internal static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrtOverlay", "crt.log");
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--cursor-guard") return CursorVisibility.RunGuard(args);
        L.Initialize();
        bool languageSpecified = false;
        try
        {
            var remaining = new List<string>(args.Length);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "--language") { remaining.Add(args[i]); continue; }
                if (++i == args.Length) throw new ArgumentException(L.T("--language требует ru или en."));
                L.SetLanguage(args[i]); languageSpecified = true;
            }
            args = remaining.ToArray();
        }
        catch (ArgumentException ex) { ShowConsole(); Console.Error.WriteLine(ex.Message); return 2; }
        if (args.Length == 1 && args[0] == "--self-test")
        {
            ShowConsole();
            try { CrtPipeline.SelfTest(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Length == 1 && args[0] == "--stop") return StopInstance();
        if (args.Contains("--help")) { ShowConsole(); Console.WriteLine(CrtSettings.Help); return 0; }
        bool openEditor = args.Contains("--editor");
        if (openEditor) args = args.Where(a => a != "--editor").ToArray();
        CrtSettings settings;
        try { settings = CrtSettings.Parse(args); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or JsonException or IOException or UnauthorizedAccessException)
        {
            ShowConsole(); Console.Error.WriteLine(ex.Message); Console.Error.WriteLine(L.T("Справка: crt.exe --help")); return 2;
        }
        using var instance = new Mutex(true, InstanceName, out bool created);
        if (!created)
        {
            nint existing = FindWindowW("CrtOverlay.Main", null);
            if (existing != 0)
            {
                if (languageSpecified) PostMessageW(existing,0x8005,L.Language == "en" ? 1u : 0u,0);
                if (openEditor) PostMessageW(existing,0x8004,0,0);
            }
            return 0;
        }
        try
        {
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName);
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) throw new NotSupportedException(L.T("Нужна Windows 10 2004 или новее."));
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); File.WriteAllText(LogPath, "CRT Overlay\n", Encoding.UTF8);
            Log($"Version={typeof(Program).Assembly.GetName().Version}; Build={typeof(Program).Assembly.ManifestModule.ModuleVersionId}; OS={RuntimeInformation.OSDescription}; OSVersion={Environment.OSVersion.Version}; Runtime={RuntimeInformation.FrameworkDescription}; Architecture={RuntimeInformation.ProcessArchitecture}.");
            Native.SetProcessDpiAwarenessContext(new nint(-4));
            using var icons = new AppIcons();
            using var window = new OverlayWindow(icons); using var hotkeys = new Hotkeys(window.Handle);
            using var cursor = new CursorVisibility(settings.Cursor == "capture");
            using var tray = new Tray(window, cursor, icons);
            using var editor = new PresetEditor(window.Handle, icons);
            window.EditorRequested = openEditor;
            bool timerSet = Native.timeBeginPeriod(1) == 0;
            try { Run(window, tray, cursor, editor, stop, settings); }
            finally { if (timerSet) Native.timeEndPeriod(1); }
            return 0;
        }
        catch (Exception ex)
        {
            Log(ex.ToString()); ShowConsole(); Console.Error.WriteLine(ex);
            if (settings.TestSeconds == 0) Native.MessageBoxW(0, ex.Message + L.T("\n\nЛог: ") + LogPath, L.T("CRT Overlay — ошибка"), 0x10);
            return 1;
        }
        finally { instance.ReleaseMutex(); }
    }
    private static int StopInstance()
    {
        try
        {
            nint hwnd = FindWindowW("CrtOverlay.Main", null); GetWindowThreadProcessId(hwnd, out uint pid);
            using var stop = EventWaitHandle.OpenExisting(StopName); stop.Set();
            nint tray = FindWindowW("CrtOverlay.Tray", null);
            if (tray != 0) PostMessageW(tray, 0x0010, 0, 0); // Закрывает также открытое popup-меню.
            if (pid != 0)
            {
                try { using var process = Process.GetProcessById((int)pid); if (!process.WaitForExit(8000)) return 1; }
                catch (ArgumentException) { }
                CursorVisibility.WaitForGuard((int)pid);
            }
            using var mutex = Mutex.OpenExisting(InstanceName);
            bool acquired;
            try { acquired = mutex.WaitOne(8000); } catch (AbandonedMutexException) { acquired = true; }
            if (acquired) mutex.ReleaseMutex();
            return acquired ? 0 : 1;
        }
        catch (WaitHandleCannotBeOpenedException) { return 0; }
    }
    private static void Run(OverlayWindow window, Tray tray, CursorVisibility cursor, PresetEditor editor, EventWaitHandle stop, CrtSettings settings)
    {
        Renderer? renderer = null;
        using var frameWaiter = new FrameWaiter(stop);
        var startup = settings.Clone(); int presetIndex = 0;
        long start = Stopwatch.GetTimestamp(), nextFrame = start, retryAt = start;
        long step = Stopwatch.Frequency / (settings.Fps > 0 ? settings.Fps : 60);
        long presented = 0, captured = 0, measuredFrames = 0, allocatedBytes = 0; int retries = 0; bool enabled = true;
        bool settingsChanged = false;
        try
        {
            while (!stop.WaitOne(0) && window.Pump())
            {
                long now = Stopwatch.GetTimestamp();
                if (settings.TestSeconds > 0 && (now - start) / (double)Stopwatch.Frequency >= settings.TestSeconds) break;
                if (window.ToggleRequested)
                {
                    window.ToggleRequested = false; enabled = !enabled; tray.Enabled = enabled;
                    editor.SetEffectEnabled(enabled);
                    cursor.SetHidden(false); window.SetVisible(false);
                    if (enabled) renderer?.ResetHistory(); // Сброс накопленного до паузы послесвечения.
                }
                if (window.PresetRequested != -2)
                {
                    presetIndex = window.PresetRequested == -1 ? (presetIndex + 1) % CrtSettings.Names.Length : window.PresetRequested;
                    window.PresetRequested = -2;
                    var replacement = presetIndex == 0 ? startup.Clone() : CrtSettings.BuiltIn(presetIndex);
                    replacement.KeepRuntime(settings); settings = replacement; settingsChanged = true; tray.PresetIndex = presetIndex;
                    editor.TakePending(); editor.Sync(settings,presetIndex);
                }
                var edited = editor.TakePending();
                if (edited != null)
                {
                    edited.KeepRuntime(settings); settings = edited; settingsChanged = true; tray.PresetIndex = -1;
                }
                if (window.EditorRequested)
                {
                    window.EditorRequested = false; cursor.SetHidden(false);
                    if (!enabled) { enabled = true; tray.Enabled = true; renderer?.ResetHistory(); }
                    editor.SetEffectEnabled(enabled); editor.Open(settings,tray.PresetIndex);
                }
                if (editor.IsOpen) cursor.SetHidden(false);
                if (window.RebuildRequested)
                {
                    window.RebuildRequested = false; cursor.SetHidden(false); window.SetVisible(false);
                    renderer?.Dispose(); renderer = null; retryAt = now + Stopwatch.Frequency / 4;
                }
                if (!enabled) { window.Wait(50); continue; }
                if (renderer == null && now < retryAt) { window.Wait(20); continue; }
                if (now < nextFrame)
                {
                    frameWaiter.WaitUntil(nextFrame, window);
                    continue;
                }
                try
                {
                    if (renderer == null)
                    {
                        renderer = Renderer.Create(window, settings); retries = 0;
                        settingsChanged = false;
                        step = (long)(Stopwatch.Frequency / (settings.Fps == 0 ? renderer.RefreshRateHz : settings.Fps));
                    }
                    if (settingsChanged) { renderer.ApplySettings(settings); settingsChanged = false; }
                    if (window.ReloadRequested)
                    {
                        window.ReloadRequested = false;
                        try { renderer.ReloadShaders(); }
                        catch (Exception ex) { Log(L.T("Прежние шейдеры сохранены: ") + ex.Message); }
                    }
                    long before = settings.TestSeconds > 0 ? GC.GetAllocatedBytesForCurrentThread() : 0;
                    bool fresh = renderer.Render(cursor.Hidden, editor.IsOpen);
                    if (settings.TestSeconds > 0 && presented > 120)
                    {
                        allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - before; measuredFrames++;
                    }
                    if (fresh) captured++;
                    if (renderer.HasFrame)
                    {
                        presented++; window.SetVisible(true); cursor.SetHidden(!editor.IsOpen && renderer.WantsCursorHidden);
                        if (settings.Cursor == "capture" && !cursor.Available)
                        {
                            settings.Cursor = "system"; renderer.ApplySettings(settings); renderer.ResetHistory();
                            Log(L.T("Используется системный курсор: скрытие недоступно."));
                        }
                    }
                    long end = Stopwatch.GetTimestamp(); nextFrame = Math.Max(nextFrame + step, end);
                }
                catch (Exception ex) when (Renderer.IsRecoverable(ex))
                {
                    cursor.SetHidden(false); window.SetVisible(false); renderer?.Dispose(); renderer = null;
                    if (retries++ == 0) Log(L.T("Восстановление захвата/устройства: ") + ex.Message);
                    retryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2;
                }
            }
            if (settings.TestSeconds > 0 && presented == 0) throw new InvalidOperationException(L.T("За время теста не получено ни одного кадра."));
        }
        finally
        {
            cursor.SetHidden(false); window.SetVisible(false); renderer?.Dispose();
            if (settings.TestSeconds > 0) Log($"Present={presented}, capture={captured}, measured={measuredFrames}, Render allocations={allocatedBytes} bytes.");
        }
    }
    internal static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}\n", Encoding.UTF8); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static void ShowConsole()
    {
        nint stdout = Native.GetStdHandle(-11), stderr = Native.GetStdHandle(-12);
        uint outType = Native.GetFileType(stdout), errType = Native.GetFileType(stderr);
        if (!Native.AttachConsole(uint.MaxValue)) Native.AllocConsole();
        if (outType is 1 or 3) Native.SetStdHandle(-11, stdout);
        if (errType is 1 or 3) Native.SetStdHandle(-12, stderr);
        Console.OutputEncoding = Encoding.UTF8;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
}

