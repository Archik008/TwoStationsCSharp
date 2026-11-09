using StationApp.Core;

namespace StationApp.Model;

// =====================================================================
//  "СЕРДЦЕ" ПРОГРАММЫ: одна модель на все окна, один таймер.
//  Таймер 10 раз в секунду: такт зависимостей, движение поездов, мигание,
//  отправка ламп на Arduino. Окна только рисуют и принимают команды.
// =====================================================================

public static class ModelHost
{
    public static RailModel Model { get; private set; }
    public static bool BlinkPhase { get; private set; }
    /// <summary>[ДОБАВЛЕНО 20.10.2026] Быстрое мигание (0,2 с) — маршрут, который устанавливается (как в Python TrainSt).</summary>
    public static bool FastBlinkPhase { get; private set; }
    /// <summary>Имитация: соседняя станция (Алгабас) сама открывает входной, когда к ней идёт поезд.</summary>
    public static bool AutoNeighbours;

    /// <summary>Что-то поменялось — окнам пора перерисоваться.</summary>
    public static event Action Changed;
    /// <summary>Сообщение в журнал (из ядра или от Arduino).</summary>
    public static event Action<string, LogKind> Log;

    static System.Windows.Forms.Timer timer;
    static double blinkAcc, fastBlinkAcc, pumpAcc;
    static bool dirty;
    static readonly HashSet<string> warnedNoServo = new();

    public static void Start(bool depot)
    {
        Model = new RailModel(depot);
        var il = Model.IL;
        il.Log += (m, k) => Log?.Invoke(m, k);
        il.Changed += () => dirty = true;
        il.SwitchCommand += OnSwitchCommand;

        ArduinoLink.Log += m => Log?.Invoke(m, LogKind.Info);
        ArduinoLink.SensorsReceived += OnSensors;
        ArduinoLink.PinSensorsReceived += OnPinSensors;
        ArduinoLink.StatusChanged += OnArduinoStatus;
        ArduinoLink.RegisterLamps(HardwareLamps);

        // [ДОБАВЛЕНО 12.10.2026] связь диспетчеров через сервер PromServer (по ТЗ):
        // НА и 1 (пост) — наши, отдаём их Алгабасу; Ч и 2 — светофоры Алгабаса, у нас только показываются;
        // направление однопутного перегона — общее, меняется по согласию обеих станций
        DispatchLink.StationId = "akzht";
        DispatchLink.StationName = "АКЖТ + путевой пост";
        DispatchLink.SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationAKZHT", "server.txt");
        Ui.UiSettings.Load();   // [ДОБАВЛЕНО 28.10.2026] режим интерфейса и панели (интерфейс.txt рядом с server.txt)
        Dispatch = new DispatchMirror(il, Model.Trains, "algabas", "Алгабас",
            own: new[] { ("НА", "НА"), ("1-ОП", "1-ОП") },
            mirror: new[] { ("Ч", "Ч-АЛГ"), ("2-ОП", "2-ОП") },
            sharedLines: new[] { ("ОП", Model.Post.Line) },       // перегон пост — Алгабас: направление общее
            sharedSections: new[] { ("НАУ", "НАУ"), ("Б12", "Б12") });   // занятость перегона дублируется, поезда переходят
        DispatchLink.Log += (m, k) => Log?.Invoke(m, k);
        // [ДОБАВЛЕНО 20.10.2026, этап 2] занятие с преподавателем; поезда урока — из кнопок имитации обеих схем
        Training.Init(il, Model.Trains);
        Training.SpawnSources.AddRange(Model.Akzht.View.SpawnButtons.Concat(Model.Post.View.SpawnButtons));
        ScheduleSetup.Register(Model);   // [ДОБАВЛЕНО 22.10.2026, этап 5] пункты расписания: АКЖТ и пост
        DispatchLink.StatusChanged += OnDispatchChanged;
        DispatchLink.PeersChanged += OnDispatchChanged;
        Dispatch.Apply();   // [ДОБАВЛЕНО 28.10.2026] светофоры Алгабаса (Ч, 2) погашены с самого запуска, пока нет данных

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

    // ---------------- связь диспетчеров ----------------
    /// <summary>Какие светофоры отдаём Алгабасу и какие берём у него.</summary>
    public static DispatchMirror Dispatch { get; private set; }

    static void OnDispatchChanged()
    {
        Dispatch.Apply();
        dirty = true;
    }

    static void OnTimer()
    {
        const double dt = 0.1;
        if (AutoNeighbours)
            foreach (var sig in Model.IL.Signals.Values)
                if (sig.Kind == SignalKind.Neighbor && !sig.NeighborOpen) { sig.NeighborOpen = true; dirty = true; }
        // [ИЗМЕНЕНО 20.10.2026, этап 2] на занятии модель идёт по игровому времени: пауза и ускорение ×1…×10
        double mdt = Training.ModelDt(dt);
        Model.IL.Tick(mdt);
        Model.Trains.Tick(mdt);
        Training.Tick(dt);
        Dispatch.Tick();

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

        CheckSensorsAlive();

        pumpAcc += dt;
        if (pumpAcc >= 0.35)
        {
            pumpAcc = 0;
            ArduinoLink.PumpLights();
        }

        if (dirty)
        {
            dirty = false;
            Changed?.Invoke();
        }
    }

    // ---------------- Arduino ----------------
    static void OnSwitchCommand(RailSwitch sw)
    {
        if (!Model.Hw.Servos.TryGetValue(sw.Id, out var channels) || channels.Length == 0)
        {
            if (warnedNoServo.Add(sw.Id))
                Log?.Invoke($"Стрелки {sw.Label}: канал сервопривода не задан (карта в AkzhtHardware.cs / PostHardware.cs) — переводится только на экране", LogKind.Aux);
            return;
        }
        foreach (var ch in channels) ArduinoLink.SendSwitch(ch, sw.Plus);
    }

    static IEnumerable<(int b, int bit)> HardwareLamps()
    {
        var il = Model.IL;
        foreach (var ((sig, lamp), pos) in Model.Hw.Lamps)
        {
            if (!il.Signals.TryGetValue(sig, out var s)) continue;
            if (il.TestMode || s.IsLampOn(lamp, BlinkPhase)) yield return pos;
        }
    }

    static void OnSensors(bool[] occupied)
    {
        foreach (var (secId, bit) in Model.Hw.Sensors)
        {
            if (bit < 0 || bit >= occupied.Length || !Model.IL.Sections.TryGetValue(secId, out var s)) continue;
            if (s.HwOcc != occupied[bit]) { s.HwOcc = occupied[bit]; dirty = true; }
        }
    }

    // ---------------- датчики, подключённые прямо к пинам (строка "Occ:") ----------------
    static DateTime lastOcc = DateTime.MinValue;
    static bool occStaleWarned;

    static void OnPinSensors(bool[] occupied)
    {
        lastOcc = DateTime.Now;
        if (occStaleWarned)
        {
            occStaleWarned = false;
            Log?.Invoke("Датчики занятости снова присылают данные", LogKind.Info);
        }
        foreach (var (secId, idx) in Model.Hw.PinSensors)
        {
            if (idx < 0 || idx >= occupied.Length || !Model.IL.Sections.TryGetValue(secId, out var s)) continue;
            if (s.HwOcc == occupied[idx]) continue;
            s.HwOcc = occupied[idx];
            string pin = Model.Hw.PinNames.TryGetValue(idx, out var pn) ? pn : $"датчик {idx + 1}";
            Log?.Invoke($"Датчик ({pin}): участок {s.Label} {(s.HwOcc ? "ЗАНЯТ" : "свободен")}", LogKind.Train);
            dirty = true;
        }
    }

    /// <summary>Если прошивка с датчиками перестала присылать "Occ:" (кабель выдернули и т.п.) —
    /// не держим старую занятость, а снимаем её и предупреждаем.</summary>
    static void CheckSensorsAlive()
    {
        if (lastOcc == DateTime.MinValue || occStaleWarned) return;
        // [ИЗМЕНЕНО 28.10.2026] было 3 с: скетч StationMain_AKZHT повторяет "Occ:" раз в 2 с — запас на задержки порта
        if ((DateTime.Now - lastOcc).TotalSeconds < 6) return;
        occStaleWarned = true;
        ClearHardwareOccupancy();
        Log?.Invoke("Датчики занятости молчат больше 6 с — занятость от датчиков снята. Проверьте Arduino.", LogKind.Refuse);
    }

    static void OnArduinoStatus()
    {
        if (ArduinoLink.IsConnected) return;
        // порт закрыт — данных от датчиков больше нет, занятость "по датчику" снимаем
        lastOcc = DateTime.MinValue;
        occStaleWarned = false;
        ClearHardwareOccupancy();
    }

    static void ClearHardwareOccupancy()
    {
        foreach (var s in Model.IL.Sections.Values)
            if (s.HwOcc) { s.HwOcc = false; dirty = true; }
    }

    /// <summary>Строка для окна: что сейчас говорят датчики.</summary>
    public static string SensorStatusText
    {
        get
        {
            if (!ArduinoLink.IsConnected) return "Датчики: нет связи с Arduino";
            if (Model.Hw.PinSensors.Count == 0) return "Датчики: не подключены";
            if (ArduinoLink.LastOccLine == null || occStaleWarned)
                return ArduinoLink.LastDataLine != null
                    ? "Датчики: прошивка без чтения пинов (приходит только \"Data:\") — залейте StationMain_AKZHT"
                    : "Датчики: данных нет";
            var parts = Model.Hw.PinSensors.OrderBy(kv => kv.Value).Select(kv =>
            {
                var s = Model.IL.Sections[kv.Key];
                string pin = Model.Hw.PinNames.TryGetValue(kv.Value, out var pn) ? pn : $"датчик {kv.Value + 1}";
                return $"{s.Label} ({pin}): {(s.HwOcc ? "ЗАНЯТ" : "свободен")}";
            });
            return "Датчики: " + string.Join("   ", parts);
        }
    }

    // ---------------- настройки (вариант с депо) ----------------
    static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationAKZHT", "settings.txt");

    public static bool LoadDepotSetting()
    {
        try { return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains("depot=1"); }
        catch { return false; }
    }

    public static void SaveDepotSetting(bool depot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, depot ? "depot=1" : "depot=0");
        }
        catch { }
    }
}
