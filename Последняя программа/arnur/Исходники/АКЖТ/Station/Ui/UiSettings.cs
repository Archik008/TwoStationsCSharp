using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  РЕЖИМЫ ИНТЕРФЕЙСА И ВИДИМЫЕ ПАНЕЛИ  [ДОБАВЛЕНО 28.10.2026]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//
//  Четыре режима (кнопка «Параметры» в верхней полосе окна станции):
//   • Инженерный — всё: список занятости участков, журнал, порт Arduino и датчики, проверка ламп,
//     имитация поездов, все колонки кнопок. Для наладки макета и программы.
//   • Учебный — для занятий: схема, маршруты, стрелки, пульт ДСП, журнал, имитация поездов.
//     Без наладочного: списка занятости участков (занятость и так видна на схеме), порта Arduino, проверки ламп.
//   • Рабочий — как АРМ ДСП станции Медеу: работа с настоящим макетом. Крупная схема, название станции
//     и часы, пульт ДСП полосой вверху, «Поездные / Маневровые» внизу. Нет имитации поездов,
//     ручной занятости участков, списков маршрутов и стрелок (всё — кликом по схеме).
//   • Павлодарский — как имитационный тренажёр ДСП/ДНЦ (пульт-табло «Енисей», колледж Павлодара):
//     серая схема, под ней кнопочный пульт: кнопки светофоров (поездные, маневровые), стрелки «+ № −»,
//     кнопки участков (искусственная разделка). Пробный режим.
//  У каждого режима свой набор панелей по умолчанию; в «Параметрах» любую панель можно показать
//  или скрыть — выбор запоминается для режима. Файл интерфейс.txt рядом с настройками связи
//  (%LOCALAPPDATA%\StationAKZHT или StationAlgabas).
// =====================================================================

public enum UiMode { Engineer, Training, Work, Classic }

public static class UiSettings
{
    // ---- панели ----
    public const string Sections = "sections", Log = "log", Routes = "routes", Switches = "switches", Panel = "panel",
                        Sim = "sim", Classic = "classic", Hint = "hint", Hw = "hw", Test = "test", Clock = "clock",
                        ManualOcc = "manualocc", Journals = "journals", Ai = "ai";

    /// <summary>[ДОБАВЛЕНО 28.10.2026] Есть ли в программе ЖИ-помощник (пока только у АКЖТ): иначе его галочки в «Параметрах» нет.</summary>
    public static bool AiAvailable;

    /// <summary>Панели: id, название, пояснение.</summary>
    public static readonly (string id, string title, string hint)[] Panels =
    {
        (Clock, "Название станции и часы (крупно)", "Над схемой, как на АРМ ДСП"),
        (Hint, "Подсказка под схемой", "Строка внизу схемы: что под мышью, как задать маршрут"),
        (Sections, "Список занятости участков", "Колонка кнопок участков рядом со схемой (клик — занять / освободить вручную)"),
        (Routes, "Список маршрутов", "Колонка «Маршруты» с кнопками ПОЕЗДНЫЕ / МАНЕВРОВЫЕ (без неё эти кнопки — под схемой)"),
        (Switches, "Кнопки стрелок, направление, пригласительный", "Колонка кнопок стрелок и перегонов"),
        (Panel, "Пульт ДСП", "Замыкание стрелок, автодействие, ИР, лампы ОС / ОП / ОМ, взрез, ключ-жезл, переезд, ПНП"),
        (Classic, "Кнопочный пульт («Енисей»)", "Под схемой: кнопки светофоров, стрелки «+ № −», кнопки участков (ИР)"),
        (Log, "Журнал событий", "Сообщения программы, отказы, поезда"),
        (Sim, "Имитация поездов", "Поставить поезд, убрать поезда, скорость, выдержки"),
        (ManualOcc, "Ручная занятость и виртуальные поезда в меню ПКМ", "«Занять вручную», развернуть поезд, сделать маневровым"),
        (Journals, "Кнопки «ДУ-46» и «Журналы»", "Журналы и бланки"),
        (Hw, "Порт Arduino и строка датчиков", "Выбор платы макета и что присылают датчики"),
        (Test, "Проверка ламп", "Кнопки «Проверка» и «Лампы по одной»"),
        (Ai, "ИИ-помощник (окно вопросов)", "Открывается при запуске; отвечает про программу, маршруты и термины, сам подсказывает при отказах"),
    };

    static readonly Dictionary<UiMode, HashSet<string>> defaults = new()
    {
        [UiMode.Engineer] = new() { Hint, Sections, Routes, Switches, Panel, Log, Sim, ManualOcc, Journals, Hw, Test },
        [UiMode.Training] = new() { Hint, Routes, Switches, Panel, Log, Sim, ManualOcc, Journals, Ai },
        [UiMode.Work] = new() { Clock, Panel, Journals, Hw },
        [UiMode.Classic] = new() { Clock, Panel, Classic, ManualOcc, Journals, Ai },
    };

    public static UiMode Mode { get; private set; } = UiMode.Engineer;
    static readonly Dictionary<UiMode, Dictionary<string, bool>> overrides = new();

    /// <summary>Режим или панели поменялись — окна перестраиваются.</summary>
    public static event Action Changed;

    public static string ModeName(UiMode m) => m switch
    {
        UiMode.Engineer => "Инженерный",
        UiMode.Training => "Учебный",
        UiMode.Work => "Рабочий",
        _ => "Павлодарский"
    };

    public static string ModeHint(UiMode m) => m switch
    {
        UiMode.Engineer => "Всё сразу: наладка макета и программы, проверка ламп, порт Arduino, список занятости участков.",
        UiMode.Training => "Для занятий: схема, маршруты, стрелки, пульт ДСП, журнал, имитация поездов. Без наладочных панелей.",
        UiMode.Work => "Работа с настоящим макетом, как АРМ ДСП станции Медеу: крупная схема и часы, пульт полосой вверху. " +
                       "Без имитации поездов и ручной занятости — всё кликом по схеме.",
        _ => "Как тренажёр ДСП Павлодарского колледжа (пульт-табло «Енисей»): серая схема, под ней кнопки светофоров, " +
             "стрелки «+ № −» и кнопки участков. Пробный режим."
    };

    public static bool DefaultShow(UiMode m, string id) => defaults[m].Contains(id);

    /// <summary>Показывать ли панель в текущем режиме.</summary>
    public static bool Show(string id) => Show(Mode, id);

    public static bool Show(UiMode m, string id) =>
        overrides.TryGetValue(m, out var o) && o.TryGetValue(id, out var v) ? v : DefaultShow(m, id);

    public static void SetMode(UiMode m)
    {
        if (Mode == m) return;
        Mode = m;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Выбор панелей для режима (из окна «Параметры»).</summary>
    public static void SetPanels(UiMode m, Dictionary<string, bool> show)
    {
        var o = overrides.TryGetValue(m, out var d) ? d : overrides[m] = new();
        o.Clear();
        foreach (var (id, v) in show)
            if (v != DefaultShow(m, id)) o[id] = v;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Режим и его панели разом (окно «Параметры») — окна перестраиваются один раз.</summary>
    public static void Apply(UiMode m, Dictionary<string, bool> show)
    {
        var o = overrides.TryGetValue(m, out var d) ? d : overrides[m] = new();
        o.Clear();
        foreach (var (id, v) in show)
            if (v != DefaultShow(m, id)) o[id] = v;
        if (o.Count == 0) overrides.Remove(m);
        Mode = m;
        Save();
        Changed?.Invoke();
    }

    public static void ResetPanels(UiMode m)
    {
        overrides.Remove(m);
        Save();
        Changed?.Invoke();
    }

    // ------------------------ файл ------------------------

    static string FilePath => Path.Combine(Path.GetDirectoryName(DispatchLink.SettingsFile) ?? AppContext.BaseDirectory, "интерфейс.txt");

    /// <summary>Прочитать режим и панели (вызывать после того, как задан DispatchLink.SettingsFile).</summary>
    public static void Load()
    {
        Mode = UiMode.Engineer;
        overrides.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Split('#')[0].Trim();
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line[..eq].Trim(), val = line[(eq + 1)..].Trim();
                if (key == "режим" && Enum.TryParse<UiMode>(val, out var m)) Mode = m;
                else if (key.Contains('.'))
                {
                    var parts = key.Split('.');
                    if (Enum.TryParse<UiMode>(parts[0], out var pm) && Panels.Any(p => p.id == parts[1]))
                        (overrides.TryGetValue(pm, out var d) ? d : overrides[pm] = new())[parts[1]] = val == "1";
                }
            }
        }
        catch { }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var lines = new List<string> { "# Режим интерфейса и панели (кнопка «Параметры»). Режимы: Engineer, Training, Work, Classic", $"режим = {Mode}" };
            foreach (var (m, o) in overrides)
                foreach (var (id, v) in o) lines.Add($"{m}.{id} = {(v ? 1 : 0)}");
            File.WriteAllLines(FilePath, lines);
        }
        catch { }
    }
}
