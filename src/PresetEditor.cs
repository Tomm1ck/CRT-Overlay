using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CrtOverlay;

// UI живёт на собственном STA-потоке: перемещение окна и файловый диалог не
// останавливают развёртку. D3D-контекст остаётся исключительно на render-потоке.
internal sealed class PresetEditor : IDisposable
{
    private const uint OpenMessage = 0x8050, SyncMessage = 0x8051, ShutdownMessage = 0x8052, EffectMessage = 0x8053;
    private const string ClassName = "CrtOverlay.Editor";
    private const uint Child = 0x50000000, Tab = 0x10000;
    private const int FirstRowId = 1000, RowIdStride = 5;
    private readonly nint owner;
    private readonly AppIcons icons;
    private readonly Native.WndProc callback;
    private readonly object gate = new();
    private Thread? thread;
    private nint hwnd, panel, font, category, presets, live, status, apply, save, language, mode;
    private nint heading, restorePreset, openFile, cancel, reset;
    private readonly List<nint> fixedControls = [];
    private readonly List<Row> rows = [];
    private CrtSettings? opening, external, pending;
    private CrtSettings baseline = new(), draft = new();
    private sealed record PresetSource(int Index, string? FilePath, CrtSettings Values);
    private PresetSource source = new(-1, null, new()), baselineSource = new(-1, null, new());
    private volatile bool visible, disposed, effectEnabled = true;
    private bool filling, advanced, valid = true;
    private int scroll, selectedCategory, dpi = 96, openingPresetIndex = -1, externalPresetIndex = -1;
    private string? statusError;
    public bool IsOpen => visible;
    private static string[] Categories => [L.T("Геометрия и фокус"), L.T("Луч и маска"), L.T("Цвет и свечение"), L.T("Развёртка и послесвечение"), L.T("Шум и стекло"), L.T("Консольная графика"), L.T("Физический профиль трубки")];
    private static string[] Modes => [L.T("Основные настройки"), L.T("Расширенные настройки")];
    private sealed class Row(PropertyInfo property, OptionAttribute option)
    {
        public readonly PropertyInfo Property = property;
        public readonly OptionAttribute Option = option;
        public readonly string[]? Choices = option.Choices?.Split('|');
        public nint Label, Edit, Second, Slider, Hint, Reset;
    }
    public PresetEditor(nint owner, AppIcons icons)
    { this.owner = owner; this.icons = icons; callback = WindowProc; L.Changed += LanguageChanged; }
    private void LanguageChanged()
    { lock (gate) { if (hwnd != 0) EditorNative.PostMessageW(hwnd,L.ChangedMessage,0,0); } }
    public void Open(CrtSettings settings, int presetIndex)
    {
        lock (gate)
        {
            if (disposed) return;
            opening = settings.Clone(); openingPresetIndex = presetIndex;
            if (thread == null)
            {
                thread = new Thread(ThreadMain) { IsBackground = true, Name = "CRT preset editor" };
                thread.SetApartmentState(ApartmentState.STA); thread.Start(); return;
            }
            if (hwnd != 0) EditorNative.PostMessageW(hwnd, OpenMessage, 0, 0);
        }
    }
    public void Sync(CrtSettings settings, int presetIndex)
    {
        lock (gate)
        {
            external = settings.Clone(); externalPresetIndex = presetIndex; Interlocked.Exchange(ref pending, null);
            if (hwnd != 0) EditorNative.PostMessageW(hwnd, SyncMessage, 0, 0);
        }
    }
    public void SetEffectEnabled(bool value)
    {
        if (effectEnabled == value) return;
        effectEnabled = value;
        lock (gate) { if (hwnd != 0) EditorNative.PostMessageW(hwnd,EffectMessage,0,0); }
    }
    public CrtSettings? TakePending() => Interlocked.Exchange(ref pending, null);
    private void Publish(CrtSettings value)
    {
        // Старый черновик не должен затереть пресет, только что выбранный из трея.
        lock (gate) { if (external == null && !disposed) Interlocked.Exchange(ref pending, value.Clone()); }
    }
    private int Px(int value) => (int)Math.Round(value * dpi / 96.0);
    private static int Group(string name) => name switch
    {
        "tube-profile" or "tube-area" or "tube-scale" or "tube-content" or "view-distance-mm" or "tube-white" or "tube-luminance" or "display-white-nits" => 6,
        "curvature-x" or "curvature-y" or "corner-radius" or "overscan" or "focus" or "convergence-r" or "convergence-b" => 0,
        "mask" or "mask-strength" or "mask-scale" or "soft-mask" or "mask-compensation" or "scanline-strength" or "scanlines" or "beam-min" or "beam-max" or "horizontal-blur" => 1,
        "gamma" or "output-gamma" or "gamut" or "temperature" or "brightness" or "saturation" or "bloom" or "halation" => 2,
        "persistence" or "rolling-scan" or "rolling-compensation" or "scan-hz" or "bfi" or "interlace" or "phosphor" or "decay-r" or "decay-g" or "decay-b" => 3,
        "console" or "retro-width" or "retro-height" or "retro-dither" or "retro-filter" or "composite" => 5,
        "monitor" or "present" or "sync-interval" or "fps" or "test-seconds" or "debug-layer" or "performance-log" or "cursor" => -1,
        _ => 4 // Новые shader-настройки не потеряются, даже если ещё не распределены по группам.
    };
    private static readonly (PropertyInfo Property, OptionAttribute Option)[] ImageOptions =
        CrtSettings.Options.Where(x => Group(x.Option.Name) >= 0).ToArray();
    private static int BasicOrder(string name) => name switch
    {
        "brightness" => 0, "scanline-strength" => 1, "mask-strength" => 2,
        "bloom" => 3, "curvature-x" => 4, "curvature-y" => 5, _ => -1
    };
    private static readonly (PropertyInfo Property, OptionAttribute Option)[] BasicOptions =
        ImageOptions.Where(x => BasicOrder(x.Option.Name) >= 0).OrderBy(x => BasicOrder(x.Option.Name)).ToArray();
    private static bool SameImage(CrtSettings left, CrtSettings right)
    {
        foreach (var (property, option) in ImageOptions)
        {
            object a = property.GetValue(left)!, b = property.GetValue(right)!;
            if (option.Choices == "x,y")
            {
                if (CrtSettings.Pair((string)a) != CrtSettings.Pair((string)b)) return false;
            }
            else if (!Equals(a,b)) return false;
        }
        return true;
    }
    private string SourceName => source.FilePath != null ? Path.GetFileName(source.FilePath)
        : source.Index > 0 ? L.Preset(CrtSettings.Names[source.Index])
        : source.Index == 0 ? L.T("Настройки при запуске") : L.T("Свои настройки");
    private void RefreshPresets()
    {
        bool previous = filling; filling = true;
        try
        {
            EditorNative.SendMessageW(presets,0x14B,0,0);
            EditorNative.SendText(presets,0x143,0,L.T("Применённые настройки"));
            foreach (string name in CrtSettings.Names.Skip(1)) EditorNative.SendText(presets,0x143,0,L.Preset(name));
            if (source.Index <= 0) EditorNative.SendText(presets,0x143,0,SourceName);
            EditorNative.SendMessageW(presets,0x14E,source.Index > 0 ? source.Index : CrtSettings.Names.Length,0);
        }
        finally { filling = previous; }
    }
    private void RefreshState()
    {
        if (status == 0) return;
        bool unapplied = !valid || !SameImage(draft,baseline);
        bool modified = !valid || !SameImage(draft,source.Values);
        EditorNative.SetWindowTextW(heading,L.F("Пресет: {0}{1}",SourceName,modified ? L.T(" (изменён)") : ""));
        EditorNative.SetWindowTextW(hwnd,"CRT Overlay — " + SourceName + (modified ? " *" : ""));
        string preview = !effectEnabled ? L.T("Эффект выключен: изменения на экране не видны.")
            : Live ? L.T("Предпросмотр включён.") : L.T("Предпросмотр выключен: на экране применённые настройки.");
        string applied = !valid ? L.T("Исправьте значение в поле.")
            : unapplied ? L.T("Есть неприменённые изменения.") : L.T("Настройки применены.");
        string saved = source.FilePath != null
            ? modified ? L.T("Есть изменения, не записанные в файл.") : L.T("Нет изменений для записи в файл.")
            : modified ? L.T("Изменения не записаны в файл.")
            : source.Index > 0 ? L.T("Вы выбрали встроенный пресет.") : L.T("Файл для сохранения не выбран.");
        string hint = statusError ?? (unapplied
            ? L.T("«Отменить и закрыть» вернёт последние применённые настройки.")
            : L.T("«Сохранить в файл…» позволит открыть этот вариант позже."));
        EditorNative.SetWindowTextW(status,preview + " " + applied + "\n" + saved + "\n" + hint);
        EditorNative.EnableWindow(apply,valid && unapplied); EditorNative.EnableWindow(save,valid);
        EditorNative.SetWindowTextW(cancel,unapplied ? L.T("Отменить и закрыть") : L.T("Закрыть"));
        EditorNative.SetWindowTextW(reset,advanced ? L.T("Сбросить раздел") : L.T("Сбросить основные"));
    }
    private static string Caption(string name) => name switch
    {
        "tube-profile" => L.T("Кинескоп"), "tube-area" => L.T("Пропорции области трубки"),
        "tube-scale" => L.T("Размер области трубки"), "tube-content" => L.T("Размещение рабочего стола"),
        "view-distance-mm" => L.T("Расстояние наблюдателя, мм"), "tube-white" => L.T("Белый трубки"),
        "tube-luminance" => L.T("Нормировка яркости трубки"), "display-white-nits" => L.T("Белый вашего дисплея, кд/м²"),
        "curvature-x" => L.T("Кривизна по X"), "curvature-y" => L.T("Кривизна по Y"), "corner-radius" => L.T("Скругление углов"), "overscan" => L.T("Обрезка краёв"),
        "focus" => L.T("Расфокус по краям"), "convergence-r" => L.T("Сведение красного: X / Y"), "convergence-b" => L.T("Сведение синего: X / Y"),
        "mask" => L.T("Тип маски"), "mask-strength" => L.T("Сила маски"), "mask-scale" => L.T("Размер триады"), "soft-mask" => L.T("Смягчение маски"), "mask-compensation" => L.T("Компенсация яркости маски"),
        "scanline-strength" => L.T("Сила сканлайнов"), "scanlines" => L.T("Число строк"), "beam-min" => L.T("Луч: минимальная ширина"), "beam-max" => L.T("Луч: максимальная ширина"), "horizontal-blur" => L.T("Горизонтальное размытие"),
        "gamma" => L.T("Гамма ЭЛТ"), "output-gamma" => L.T("Выходная гамма"), "gamut" => L.T("Цветовой охват"), "temperature" => L.T("Температура белого, K"), "brightness" => L.T("Яркость"), "saturation" => L.T("Насыщенность"), "bloom" => L.T("Свечение ярких областей"), "halation" => L.T("Широкий ореол"),
        "persistence" => L.T("Послесвечение"), "rolling-scan" => L.T("Бегущая развёртка"), "rolling-compensation" => L.T("Компенсация яркости развёртки"), "scan-hz" => L.T("Частота развёртки, Гц"), "bfi" => L.T("Доля чёрных кадров"), "interlace" => L.T("Чересстрочная развёртка"), "phosphor" => L.T("Профиль люминофора"),
        "decay-r" => L.T("Затухание R, мс (custom)"), "decay-g" => L.T("Затухание G, мс (custom)"), "decay-b" => L.T("Затухание B, мс (custom)"),
        "noise" => L.T("Шум сигнала"), "flicker" => L.T("Сила мерцания"), "flicker-hz" => L.T("Частота мерцания, Гц"), "line-interference" => L.T("Помехи строк"), "vignette" => L.T("Виньетка"), "glass-reflection" => L.T("Отражение стекла"),
        "console" => L.T("Стиль консоли"), "retro-width" => L.T("Виртуальная ширина (0 = авто)"), "retro-height" => L.T("Виртуальная высота (0 = авто)"), "retro-dither" => L.T("Дизеринг консоли"), "retro-filter" => L.T("Фильтрация уменьшения"), "composite" => L.T("Смешение цветности Composite"), _ => name
    };
    private void ThreadMain()
    {
        L.ApplyThreadCulture();
        nint instance = Native.GetModuleHandleW(null);
        try
        {
            var controls = new EditorNative.CommonControls { Size = 8, Classes = 4 }; EditorNative.InitCommonControlsEx(ref controls);
            var wc = new Native.WindowClass
            {
                Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Instance = instance, ClassName = ClassName,
                Procedure = Marshal.GetFunctionPointerForDelegate(callback), Icon = icons.Large, SmallIcon = icons.Small,
                Cursor = EditorNative.LoadCursorW(0, new nint(32512)), Background = new nint(16)
            };
            if (Native.RegisterClassExW(ref wc) == 0) throw new Win32Exception();
            nint created = Native.CreateWindowExW(0x00050008, ClassName, L.T("CRT Overlay — редактор пресетов"), 0x02CF0000,
                100, 100, 940, 780, owner, 0, instance, 0);
            if (created == 0) throw new Win32Exception();
            lock (gate) hwnd = created;
            // Исключаем редактор из захвата: его элементы не попадают в шейдеры/обратную связь.
            CaptureExclusion.Require(hwnd, "editor");
            dpi = (int)EditorNative.GetDpiForWindow(hwnd); if (dpi == 0) dpi = 96;
            var monitor = new EditorNative.MonitorInfo { Size = (uint)Marshal.SizeOf<EditorNative.MonitorInfo>() };
            if (EditorNative.GetMonitorInfoW(EditorNative.MonitorFromWindow(owner,2), ref monitor))
            {
                int w = Math.Min(Px(940),monitor.Work.Right-monitor.Work.Left-24);
                int h = Math.Min(Px(780),monitor.Work.Bottom-monitor.Work.Top-24);
                Native.SetWindowPos(hwnd,0,monitor.Work.Left+12,monitor.Work.Top+12,w,h,0x14);
            }
            CreateFont(); BuildControls(); Layout();
            if (disposed) return;
            OpenOnThread();
            while (EditorNative.GetMessageW(out var message, 0, 0, 0) > 0)
            {
                if (message.Id == 0x100 && message.WParam == 0x1B) { Close(false); continue; }
                if (!EditorNative.IsDialogMessageW(hwnd, ref message))
                { Native.TranslateMessage(ref message); Native.DispatchMessageW(ref message); }
            }
        }
        catch (Exception ex) { Program.Log(L.T("Редактор: ") + ex); Native.MessageBoxW(owner, ex.Message, L.T("Редактор пресетов"), 0x10); }
        finally
        {
            visible = false;
            if (hwnd != 0) Native.DestroyWindow(hwnd);
            if (font != 0) CursorNative.DeleteObject(font);
            font = panel = 0; rows.Clear(); fixedControls.Clear();
            Native.UnregisterClassW(ClassName, instance);
            lock (gate) { hwnd = 0; thread = null; }
            GC.KeepAlive(callback);
        }
    }
    private void CreateFont()
    {
        nint previous = font;
        font = EditorNative.CreateFontW(-Px(14),0,0,0,400,0,0,0,1,0,0,5,0,"Segoe UI");
        foreach (var control in fixedControls) EditorNative.SendMessageW(control,0x30,font,1);
        foreach (var row in rows) foreach (var control in Handles(row)) if (control != 0) EditorNative.SendMessageW(control,0x30,font,1);
        if (previous != 0) CursorNative.DeleteObject(previous);
    }
    private nint Control(string type, string title, uint style, int id, nint parent = 0)
    {
        nint result = Native.CreateWindowExW(type == "EDIT" ? 0x200u : 0, type, title, Child | style,
            0,0,1,1,parent == 0 ? hwnd : parent,new nint(id),Native.GetModuleHandleW(null),0);
        if (result == 0) throw new Win32Exception();
        EditorNative.SendMessageW(result,0x30,font,1); return result;
    }
    private nint Fixed(string type, string title, uint style, int id)
    { var c = Control(type,title,style,id); fixedControls.Add(c); return c; }
    private void BuildControls()
    {
        heading = Fixed("STATIC","",0x4080,100);
        presets = Fixed("COMBOBOX","",Tab | 0x200003,101);
        RefreshPresets();
        restorePreset = Fixed("BUTTON",L.T("Вернуть пресет"),Tab,102);
        openFile = Fixed("BUTTON",L.T("Открыть файл…"),Tab,103);
        save = Fixed("BUTTON",L.T("Сохранить в файл…"),Tab,104);
        mode = Fixed("COMBOBOX","",Tab | 0x200003,112);
        foreach (string name in Modes) EditorNative.SendText(mode,0x143,0,name);
        EditorNative.SendMessageW(mode,0x14E,advanced ? 1 : 0,0);
        category = Fixed("COMBOBOX","",Tab | 0x200003,105);
        foreach (string name in Categories) EditorNative.SendText(category,0x143,0,name);
        EditorNative.SendMessageW(category,0x14E,selectedCategory,0);
        live = Fixed("BUTTON",L.T("Предпросмотр на рабочем столе"),Tab | 3,106);
        EditorNative.SendMessageW(live,0xF1,1,0);
        panel = Native.CreateWindowExW(0x10000,ClassName,"",Child | 0x02200000,0,0,1,1,hwnd,0,Native.GetModuleHandleW(null),0);
        if (panel == 0) throw new Win32Exception();
        status = Fixed("STATIC","",0,107);
        apply = Fixed("BUTTON",L.T("Применить"),Tab | 1,108);
        cancel = Fixed("BUTTON",L.T("Закрыть"),Tab,109);
        reset = Fixed("BUTTON","",Tab,110);
        language = Fixed("COMBOBOX","",Tab | 0x200003,111);
        EditorNative.SendText(language,0x143,0,"Русский (RU)");
        EditorNative.SendText(language,0x143,0,"English (EN)");
        EditorNative.SendMessageW(language,0x14E,L.Language == "en" ? 1 : 0,0);
        BuildRows();
    }
    private void RefreshLanguage()
    {
        if (language == 0) return;
        L.ApplyThreadCulture(); filling = true;
        try
        {
            // Меняем только подписи: не пересоздаём поля и не теряем незавершённый ввод.
            EditorNative.SetWindowTextW(restorePreset,L.T("Вернуть пресет"));
            EditorNative.SetWindowTextW(openFile,L.T("Открыть файл…"));
            EditorNative.SetWindowTextW(save,L.T("Сохранить в файл…"));
            EditorNative.SetWindowTextW(live,L.T("Предпросмотр на рабочем столе"));
            EditorNative.SetWindowTextW(apply,L.T("Применить"));
            EditorNative.SendMessageW(mode,0x14B,0,0);
            foreach (string name in Modes) EditorNative.SendText(mode,0x143,0,name);
            EditorNative.SendMessageW(mode,0x14E,advanced ? 1 : 0,0);
            EditorNative.SendMessageW(category,0x14B,0,0);
            foreach (string name in Categories) EditorNative.SendText(category,0x143,0,name);
            EditorNative.SendMessageW(category,0x14E,selectedCategory,0);
            RefreshPresets();
            EditorNative.SendMessageW(language,0x14E,L.Language == "en" ? 1 : 0,0);
            foreach (var row in rows)
            {
                EditorNative.SetWindowTextW(row.Label,Caption(row.Option.Name));
                EditorNative.SetWindowTextW(row.Reset,L.T("Сбросить"));
                if (row.Property.PropertyType == typeof(bool)) EditorNative.SetWindowTextW(row.Edit,L.T("Включено"));
                else if (row.Option.Choices != null && row.Second == 0)
                {
                    int selected = (int)EditorNative.SendMessageW(row.Edit,0x147,0,0);
                    EditorNative.SendMessageW(row.Edit,0x14B,0,0);
                    foreach (string choice in row.Choices!) EditorNative.SendText(row.Edit,0x143,0,L.Choice(choice));
                    EditorNative.SendMessageW(row.Edit,0x14E,selected,0);
                }
            }
        }
        finally { filling = false; }
        ValidateAndPreview(false);
    }
    private static IEnumerable<nint> Handles(Row r) { yield return r.Label; yield return r.Edit; yield return r.Second; yield return r.Slider; yield return r.Hint; yield return r.Reset; }
    private void ChangeView()
    {
        bool nextAdvanced = EditorNative.SendMessageW(mode,0x147,0,0) == 1;
        int nextCategory = (int)EditorNative.SendMessageW(category,0x147,0,0);
        if (nextCategory < 0 || (nextAdvanced == advanced && nextCategory == selectedCategory)) return;
        // Keep the old controls, including unfinished text, until every edit is valid.
        if (!ValidateAndPreview(false))
        {
            filling = true;
            try
            {
                EditorNative.SendMessageW(mode,0x14E,advanced ? 1 : 0,0);
                EditorNative.SendMessageW(category,0x14E,selectedCategory,0);
            }
            finally { filling = false; }
            return;
        }
        advanced = nextAdvanced; selectedCategory = nextCategory; BuildRows();
    }
    private void BuildRows()
    {
        filling = true;
        try
        {
            foreach (var row in rows) foreach (var control in Handles(row)) if (control != 0) Native.DestroyWindow(control);
            rows.Clear(); scroll = 0;
            var options = advanced ? ImageOptions.Where(x => Group(x.Option.Name) == selectedCategory) : BasicOptions;
            foreach (var (property, option) in options)
            {
                var row = new Row(property,option); int id = FirstRowId + rows.Count * RowIdStride;
                row.Label = Control("STATIC",Caption(option.Name),0,id,panel);
                row.Hint = Control("STATIC","--" + option.Name + (option.Choices == null && property.PropertyType != typeof(bool) ? $"   [{option.Min.ToString(CultureInfo.InvariantCulture)} … {option.Max.ToString(CultureInfo.InvariantCulture)}]" : ""),0,id,panel);
                if (property.PropertyType == typeof(bool)) row.Edit = Control("BUTTON",L.T("Включено"),Tab | 3,id+1,panel);
                else if (option.Choices != null && option.Choices != "x,y")
                {
                    row.Edit = Control("COMBOBOX","",Tab | 0x200003,id+1,panel);
                    foreach (string choice in row.Choices!) EditorNative.SendText(row.Edit,0x143,0,L.Choice(choice));
                }
                else
                {
                    row.Edit = Control("EDIT","",Tab | 0x80,id+1,panel);
                    if (option.Choices == "x,y") row.Second = Control("EDIT","",Tab | 0x80,id+2,panel);
                    else
                    {
                        row.Slider = Control("msctls_trackbar32","",Tab | 0x10,id+3,panel);
                        EditorNative.SendMessageW(row.Slider,0x406,1,new nint(1000 << 16));
                    }
                }
                row.Reset = Control("BUTTON",L.T("Сбросить"),Tab,id+4,panel);
                rows.Add(row); Fill(row);
            }
        }
        finally { filling = false; }
        Layout(); ValidateAndPreview(false);
    }
    private void Fill(Row row)
    {
        object value = row.Property.GetValue(draft)!;
        if (value is bool flag) EditorNative.SendMessageW(row.Edit,0xF1,flag ? 1 : 0,0);
        else if (row.Second != 0)
        {
            var pair = CrtSettings.Pair((string)value);
            EditorNative.SetWindowTextW(row.Edit,pair.X.ToString("R",CultureInfo.InvariantCulture));
            EditorNative.SetWindowTextW(row.Second,pair.Y.ToString("R",CultureInfo.InvariantCulture));
        }
        else if (row.Option.Choices != null)
            EditorNative.SendMessageW(row.Edit,0x14E,Array.IndexOf(row.Choices!,(string)value),0);
        else
        {
            EditorNative.SetWindowTextW(row.Edit,Convert.ToString(value,CultureInfo.InvariantCulture)!);
            SetSlider(row,Convert.ToDouble(value,CultureInfo.InvariantCulture));
        }
    }
    private void ResetRow(Row row)
    {
        row.Property.SetValue(draft,row.Property.GetValue(new CrtSettings()));
        bool previous = filling; filling = true;
        try { Fill(row); }
        finally { filling = previous; }
        // Keep the other controls' unfinished text and validate the whole draft before previewing.
        ValidateAndPreview(true);
    }
    private static double Fraction(Row row, double value) => row.Option.Name.StartsWith("decay-")
        ? Math.Log(Math.Max(value,row.Option.Min) / row.Option.Min) / Math.Log(row.Option.Max / row.Option.Min)
        : (value - row.Option.Min) / (row.Option.Max - row.Option.Min);
    private static void SetSlider(Row row, double value) => EditorNative.SendMessageW(row.Slider,0x405,1,(nint)(int)Math.Round(Math.Clamp(Fraction(row,value),0,1)*1000));
    private static string Text(nint control) { var b = new StringBuilder(256); EditorNative.GetWindowTextW(control,b,b.Capacity); return b.ToString(); }
    private static float Number(string text)
    {
        if (!float.TryParse(text.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out float value))
            throw new FormatException(L.T("Неверное число. Используйте точку или запятую для дробной части."));
        return value;
    }
    private bool ReadRow(Row row, nint activeSlider)
    {
        object value;
        if (row.Property.PropertyType == typeof(bool)) value = EditorNative.SendMessageW(row.Edit,0xF0,0,0) != 0;
        else if (row.Second != 0) value = Number(Text(row.Edit)).ToString("R",CultureInfo.InvariantCulture) + "," + Number(Text(row.Second)).ToString("R",CultureInfo.InvariantCulture);
        else if (row.Choices != null)
        {
            int index = (int)EditorNative.SendMessageW(row.Edit,0x147,0,0);
            if (index < 0) throw new ArgumentException(L.T("Выберите ") + Caption(row.Option.Name));
            value = row.Choices[index];
        }
        else
        {
            float n = Number(Text(row.Edit));
            if (!float.IsFinite(n)) throw new ArgumentException(Caption(row.Option.Name) + L.T(": требуется конечное число."));
            if (row.Property.PropertyType == typeof(int))
            {
                if (n != MathF.Truncate(n)) throw new ArgumentException(Caption(row.Option.Name) + L.T(": требуется целое число."));
                value = checked((int)n);
            }
            else value = n;
            // Do not reposition a trackbar from inside its own drag notification.
            if (row.Slider != activeSlider) SetSlider(row,n);
        }
        if (Equals(row.Property.GetValue(draft),value)) return false;
        row.Property.SetValue(draft,value); return true;
    }
    private bool ReadRows(nint activeSlider, Row? changedRow)
    {
        // A valid draft already contains every other control's current value.
        // After an input error, reread the full group to validate all pending edits together.
        if (valid && changedRow != null) return ReadRow(changedRow,activeSlider);
        bool changed = false;
        foreach (var row in rows) changed |= ReadRow(row,activeSlider);
        return changed;
    }
    private bool Live => EditorNative.SendMessageW(live,0xF0,0,0) != 0;
    private bool ValidateAndPreview(bool publish, nint activeSlider = 0, Row? changedRow = null)
    {
        try
        {
            bool wasValid = valid;
            bool changed = ReadRows(activeSlider,changedRow);
            if (wasValid && changedRow != null && !changed) return true;
            draft.Validate(); valid = true;
            statusError = null;
            if (publish && Live) Publish(draft);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        { valid = false; statusError = L.T("Не удалось применить: ") + ex.Message; }
        RefreshState();
        return valid;
    }
    private void Move(nint control, int x, int y, int w, int h) => Native.SetWindowPos(control,0,Px(x),Px(y),Px(w),Px(h),0x0014);
    private void Layout()
    {
        if (panel == 0) return;
        EditorNative.GetClientRect(hwnd,out var r); int w = (int)(r.Right * 96.0 / dpi), h = (int)(r.Bottom * 96.0 / dpi);
        Move(heading,16,12,w-188,24); Move(language,w-156,8,140,180);
        Move(presets,16,44,180,350); Move(restorePreset,204,43,160,30); Move(openFile,372,43,155,30); Move(save,535,43,165,30);
        Move(mode,16,86,194,240); Move(category,218,86,240,280);
        EditorNative.ShowWindow(category,advanced ? 4 : 0);
        int previewX = advanced ? 466 : 218;
        Move(live,previewX,85,w-previewX-16,30);
        Move(panel,16,126,w-32,Math.Max(50,h-260));
        Move(status,16,h-126,w-32,78); Move(apply,w-338,h-40,140,30); Move(cancel,w-188,h-40,172,30); Move(reset,16,h-40,190,30);
        LayoutRows();
    }
    private void LayoutRows()
    {
        EditorNative.GetClientRect(panel,out var r); int w = (int)(r.Right * 96.0 / dpi), h = (int)(r.Bottom * 96.0 / dpi);
        scroll = Math.Clamp(scroll,0,Math.Max(0,rows.Count*72-h));
        var info = new EditorNative.ScrollInfo { Size = (uint)Marshal.SizeOf<EditorNative.ScrollInfo>(), Mask=7, Min=0, Max=Math.Max(0,rows.Count*72-1), Page=(uint)Math.Max(1,h), Pos=scroll };
        EditorNative.SetScrollInfo(panel,1,ref info,true);
        int i = 0;
        foreach (var row in rows)
        {
            int y = i++ * 72 - scroll;
            Move(row.Label,6,y+5,330,24); Move(row.Hint,6,y+32,340,24);
            int x = 350, resetX = w-108, inputRight = resetX-12;
            Move(row.Edit,x,y+9,row.Second != 0 ? 100 : row.Slider != 0 ? 108 : Math.Max(130,inputRight-x),row.Option.Choices != null && row.Second == 0 ? 250 : 28);
            if (row.Second != 0) Move(row.Second,x+112,y+9,100,28);
            if (row.Slider != 0) Move(row.Slider,x+118,y+8,Math.Max(40,inputRight-x-118),34);
            Move(row.Reset,resetX,y+9,94,28);
        }
        EditorNative.InvalidateRect(panel,0,true);
    }
    private void OpenOnThread()
    {
        lock (gate)
        {
            if (!visible && opening != null)
            {
                if (openingPresetIndex >= 0 || !SameImage(opening,baseline))
                    baselineSource = new(openingPresetIndex,null,opening.Clone());
                baseline = opening.Clone(); draft = baseline.Clone(); source = baselineSource; external = null;
            }
            opening = null;
        }
        if (!visible) { RefreshPresets(); BuildRows(); }
        visible = true; EditorNative.ShowWindow(hwnd,9); EditorNative.SetForegroundWindow(hwnd); EditorNative.SetFocus(advanced ? category : presets);
    }
    private void Close(bool keep)
    {
        if (!visible) return;
        if (keep) { if (!ValidateAndPreview(false)) return; baseline = draft.Clone(); baselineSource = source; }
        Publish(baseline); draft = baseline.Clone(); source = baselineSource;
        visible = false; EditorNative.ShowWindow(hwnd,0);
    }
    private void ChoosePreset()
    {
        int index = (int)EditorNative.SendMessageW(presets,0x147,0,0);
        if (index < 0) return;
        if (index == 0) { draft = baseline.Clone(); source = baselineSource; }
        else if (index == CrtSettings.Names.Length) draft = source.Values.Clone();
        else { draft = CrtSettings.BuiltIn(index); source = new(index,null,draft.Clone()); }
        draft.KeepRuntime(baseline); RefreshPresets(); BuildRows(); ValidateAndPreview(true);
    }
    private void FileDialog(bool writing)
    {
        if (writing && !ValidateAndPreview(false)) return;
        nint buffer = Marshal.AllocHGlobal(65536);
        try
        {
            Marshal.WriteInt16(buffer,0);
            string initial = writing ? source.FilePath ?? "my-crt-preset.json" : "";
            byte[] bytes = Encoding.Unicode.GetBytes(initial + '\0'); Marshal.Copy(bytes,0,buffer,bytes.Length);
            var dialog = new EditorNative.OpenFileName
            {
                Size = (uint)Marshal.SizeOf<EditorNative.OpenFileName>(), Owner=hwnd,
                Filter=L.T("Пресеты CRT (*.json)\0*.json\0Все файлы\0*.*\0\0"), FilterIndex=1,
                File=buffer, MaxFile=32768, Title=writing ? L.T("Сохранить пресет") : L.T("Открыть пресет"), DefaultExtension="json",
                InitialDir=Directory.Exists(Path.Combine(Environment.CurrentDirectory,"presets")) ? Path.Combine(Environment.CurrentDirectory,"presets") : null,
                Flags=0x80000 | 8 | 0x800 | (writing ? 2u : 0x1000u)
            };
            bool chosen = writing ? EditorNative.GetSaveFileNameW(ref dialog) : EditorNative.GetOpenFileNameW(ref dialog);
            if (!chosen)
            {
                uint error = EditorNative.CommDlgExtendedError();
                if (error != 0) throw new InvalidOperationException(L.F("Файловый диалог: 0x{0:X}.",error));
                return;
            }
            string path = Marshal.PtrToStringUni(buffer)!;
            if (writing)
            {
                // Записываем временный файл рядом с конечным: при сбое записи старый JSON цел.
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { draft.Save(temporary); File.Move(temporary,path,true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                baseline = draft.Clone(); source = baselineSource = new(-1,path,baseline.Clone());
                Publish(baseline); RefreshPresets(); RefreshState();
            }
            else
            {
                var loaded = CrtSettings.Load(path); loaded.KeepRuntime(baseline); draft = loaded;
                source = new(-1,path,loaded.Clone()); RefreshPresets(); BuildRows(); ValidateAndPreview(true);
            }
        }
        catch (Exception ex) { statusError = L.T("Ошибка: ") + ex.Message; RefreshState(); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private nint WindowProc(nint window, uint msg, nuint wp, nint lp)
    {
        try
        {
            if (msg == L.ChangedMessage) { RefreshLanguage(); return 0; }
            if (msg == EffectMessage) { RefreshState(); return 0; }
            if (msg == OpenMessage) { OpenOnThread(); return 0; }
            if (msg == SyncMessage)
            {
                lock (gate)
                {
                    if (external != null)
                    {
                        baseline = external; draft = baseline.Clone();
                        source = baselineSource = new(externalPresetIndex,null,baseline.Clone()); external = null;
                    }
                }
                if (visible) { RefreshPresets(); BuildRows(); }
                return 0;
            }
            if (msg == ShutdownMessage)
            {
                visible = false;
                nint popup = EditorNative.GetLastActivePopup(hwnd); if (popup != hwnd) EditorNative.EndDialog(popup,0);
                Native.DestroyWindow(hwnd); Native.PostQuitMessage(0); return 0;
            }
            if (window == hwnd && msg == 0x10) { Close(false); return 0; }
            if (window == hwnd && msg == 5) { Layout(); return 0; }
            if (window == hwnd && msg == 0x24)
            {
                // MINMAXINFO: ptMinTrackSize расположен после трёх POINT.
                Marshal.WriteInt32(lp,24,Px(770)); Marshal.WriteInt32(lp,28,Px(540)); return 0;
            }
            if (window == hwnd && msg == 0x2E0)
            {
                dpi=(int)(wp & 0xffff); CreateFont(); var rect=Marshal.PtrToStructure<EditorNative.Rect>(lp);
                Native.SetWindowPos(hwnd,0,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,0x14); Layout(); return 0;
            }
            if (msg == 0x115 || msg == 0x20A)
            {
                if (msg == 0x20A) scroll -= (short)((ulong)wp >> 16) / 120 * 72;
                else
                {
                    int code=(int)(wp & 0xffff);
                    if (code == 0) scroll-=36; if (code == 1) scroll+=36;
                    if (code == 2) scroll-=260; if (code == 3) scroll+=260;
                    if (code is 4 or 5) { var si=new EditorNative.ScrollInfo { Size=(uint)Marshal.SizeOf<EditorNative.ScrollInfo>(), Mask=16 }; EditorNative.GetScrollInfo(panel,1,ref si); scroll=si.TrackPos; }
                    if (code == 6) scroll=0; if (code == 7) scroll=rows.Count*72;
                }
                LayoutRows(); return 0;
            }
            if (msg == 0x114 && !filling)
            {
                var row=rows.Find(r=>r.Slider==lp); if (row==null) return 0;
                int code = (int)(wp & 0xffff);
                // TB_THUMBPOSITION / TB_THUMBTRACK carry the requested position in HIWORD.
                int position = code is 4 or 5 ? (int)((wp >> 16) & 0xffff) : (int)EditorNative.SendMessageW(lp,0x400,0,0);
                double f = position / 1000.0;
                double v=row.Option.Name.StartsWith("decay-") ? row.Option.Min*Math.Pow(row.Option.Max/row.Option.Min,f) : row.Option.Min+(row.Option.Max-row.Option.Min)*f;
                if (row.Property.PropertyType==typeof(int)) { v=Math.Round(v); if (row.Option.Name is "retro-width" or "retro-height" && v is > 0 and < 64) v=64; }
                filling=true; EditorNative.SetWindowTextW(row.Edit,v.ToString("0.#####",CultureInfo.InvariantCulture)); filling=false;
                ValidateAndPreview(true,lp,row); return 0;
            }
            if (msg == 0x111 && !filling)
            {
                int id=(int)(wp & 0xffff), notification=(int)((wp >> 16)&0xffff);
                if (id>=FirstRowId)
                {
                    int index = (id-FirstRowId)/RowIdStride;
                    if (index < rows.Count)
                    {
                        var row = rows[index];
                        if (lp == row.Reset && notification == 0) ResetRow(row);
                        else if ((lp == row.Edit || (row.Second != 0 && lp == row.Second)) && notification is 0 or 1 or 0x300)
                            ValidateAndPreview(true,changedRow: row);
                    }
                    return 0;
                }
                if (id==101 && notification==1) { ChoosePreset(); return 0; }
                if ((id==105 || id==112) && notification==1) { ChangeView(); return 0; }
                if (id==111 && notification==1) { L.SetLanguage(EditorNative.SendMessageW(language,0x147,0,0) == 1 ? "en" : "ru"); return 0; }
                if (notification != 0) return 0;
                switch (id)
                {
                    case 102: ChoosePreset(); break;
                    case 103: FileDialog(false); break;
                    case 104: FileDialog(true); break;
                    case 106: if (Live) ValidateAndPreview(true); else { Publish(baseline); ValidateAndPreview(false); } break;
                    case 1:
                    case 108:
                        if (ValidateAndPreview(false)) { baseline=draft.Clone(); baselineSource=source; Publish(baseline); RefreshState(); }
                        break;
                    case 109: Close(false); break;
                    case 110:
                        var defaults=new CrtSettings(); foreach(var row in rows) row.Property.SetValue(draft,row.Property.GetValue(defaults)); BuildRows(); ValidateAndPreview(true); break;
                }
                return 0;
            }
        }
        catch (Exception ex) { filling=false; Program.Log(L.T("Редактор UI: ")+ex); if(status!=0) EditorNative.SetWindowTextW(status,L.T("Ошибка: ")+ex.Message); }
        return Native.DefWindowProcW(window,msg,wp,lp);
    }
    public void Dispose()
    {
        disposed=true; Thread? stopping;
        L.Changed -= LanguageChanged;
        lock(gate) { stopping=thread; if(hwnd!=0) EditorNative.PostMessageW(hwnd,ShutdownMessage,0,0); }
        stopping?.Join(5000);
    }
}
