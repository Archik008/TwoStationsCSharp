using StationApp.Core;

namespace StationApp.Model;

// =====================================================================
//  "СЕРДЦЕ" ПРОГРАММЫ «СТАНЦИЯ ПО СХЕМЕ»  [ДОБАВЛЕНО 10.11.2026]
//  Как Model/ModelHost.cs Алгабаса: одна модель и один таймер 10 раз в секунду (такт зависимостей,
//  движение поездов, мигание). Отличия: станция строится из файла .station.json; макета Arduino
//  и связи с соседними станциями пока нет (первый этап) — общий код окна получает пустую связь.
// =====================================================================

public static class ModelHost
{
    public static RailModel Model { get; private set; }
    public static bool BlinkPhase { get; private set; }
    public static bool FastBlinkPhase { get; private set; }
    /// <summary>Имитация: соседние станции сами открывают свои светофоры, когда к ним идёт поезд.</summary>
    public static bool AutoNeighbours;

    /// <summary>Что-то поменялось — окну пора перерисоваться.</summary>
    public static event Action Changed;
    /// <summary>Сообщение в журнал.</summary>
    public static event Action<string, LogKind> Log;

    /// <summary>Связь с соседями: у станций из файла пока пустая (нужна общему коду окна).</summary>
    public static DispatchMirror Dispatch { get; private set; }

    /// <summary>Папка настроек этой программы на ПК: язык, режим интерфейса, последняя открытая схема.</summary>
    public static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationScheme");

    static System.Windows.Forms.Timer timer;
    static double blinkAcc, fastBlinkAcc;
    static bool dirty;

    /// <summary>Построить станцию из файла схемы. Ошибка в файле — исключение (окно ещё не открыто).</summary>
    public static void Start(string path)
    {
        Model = new RailModel(path);
        var il = Model.IL;
        il.Log += (m, k) => Log?.Invoke(m, k);
        il.Changed += () => dirty = true;

        DispatchLink.StationId = "scheme";
        DispatchLink.StationName = Model.Layout.View.Title;
        DispatchLink.SettingsFile = Path.Combine(SettingsDir, "server.txt");
        Ui.UiSettings.Load();     // режим интерфейса и панели (интерфейс.txt рядом с server.txt)
        Dispatch = new DispatchMirror(il, Model.Trains, "none", "—",
            own: Array.Empty<(string, string)>(), mirror: Array.Empty<(string, string)>(),
            sharedLines: Array.Empty<(string, DirectionLine)>(), sharedSections: Array.Empty<(string, string)>());
        DispatchLink.Log += (m, k) => Log?.Invoke(m, k);
        Training.Init(il, Model.Trains);
        Training.SpawnSources.AddRange(Model.Layout.View.SpawnButtons);

        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => OnTimer();
        timer.Start();
    }

    public static void Stop()
    {
        timer?.Stop();
        ArduinoLink.Disconnect();
        DispatchLink.Stop(quiet: true);
    }

    public static void MarkDirty() => dirty = true;

    static void OnTimer()
    {
        const double dt = 0.1;
        if (AutoNeighbours)
            foreach (var sig in Model.IL.Signals.Values)
                if (sig.Kind == SignalKind.Neighbor && !sig.NeighborOpen) { sig.NeighborOpen = true; dirty = true; }
        double mdt = Training.ModelDt(dt);
        Model.IL.Tick(mdt);
        Model.Trains.Tick(mdt);
        Training.Tick(dt);

        blinkAcc += dt;
        if (blinkAcc >= 0.5)
        {
            blinkAcc = 0;
            BlinkPhase = !BlinkPhase;
            dirty = true;
        }

        fastBlinkAcc += dt;
        if (fastBlinkAcc >= 0.199)
        {
            fastBlinkAcc = 0;
            FastBlinkPhase = !FastBlinkPhase;
            if (Model.IL.ActiveRoutes.Any(r => r.Setting)) dirty = true;
        }

        if (dirty)
        {
            dirty = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Строка для окна: что сейчас говорят датчики (у станций из файла макета пока нет).</summary>
    public static string SensorStatusText => "Датчики: нет связи с Arduino";
}
