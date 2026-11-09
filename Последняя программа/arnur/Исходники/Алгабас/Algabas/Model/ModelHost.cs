using StationApp.Algabas;
using StationApp.Core;

namespace StationApp.Model;

// =====================================================================
//  "СЕРДЦЕ" ПРОГРАММЫ: модель и один таймер (как в программе АКЖТ).
//  Таймер 10 раз в секунду: такт зависимостей, движение поездов, мигание,
//  отправка ламп на Arduino. Окно только рисует и принимает команды.
// =====================================================================

public static class ModelHost
{
    public static RailModel Model { get; private set; }
    public static bool BlinkPhase { get; private set; }
    /// <summary>[ДОБАВЛЕНО 20.10.2026] Быстрое мигание (0,2 с) — маршрут, который устанавливается (как в Python TrainSt).</summary>
    public static bool FastBlinkPhase { get; private set; }
    /// <summary>Имитация: соседний пост сам открывает свой светофор НА, когда к нему идёт поезд.</summary>
    public static bool AutoNeighbours;

    /// <summary>Что-то поменялось — окну пора перерисоваться.</summary>
    public static event Action Changed;
    /// <summary>Сообщение в журнал (из ядра или от Arduino).</summary>
    public static event Action<string, LogKind> Log;

    static System.Windows.Forms.Timer timer;
    static double blinkAcc, fastBlinkAcc, pumpAcc;
    static bool dirty;
    static readonly HashSet<string> warnedNoServo = new();

    public static void Start()
    {
        Model = new RailModel();
        var il = Model.IL;
        il.Log += (m, k) => Log?.Invoke(m, k);
        il.Changed += () => dirty = true;
        il.SwitchCommand += OnSwitchCommand;

        ArduinoLink.Log += m => Log?.Invoke(m, LogKind.Info);
        ArduinoLink.DataLeftToRight = true;          // прошивка Алгабаса: 1-й символ "Data:" = вход 0
        ArduinoLink.SensorsReceived += OnSensors;
        ArduinoLink.StatusChanged += OnArduinoStatus;
        ArduinoLink.RegisterLamps(HardwareLamps);

        // [ДОБАВЛЕНО 12.10.2026] связь диспетчеров через сервер PromServer (по ТЗ):
        // Ч и 2 — наши, отдаём их АКЖТ; НА и 1 — светофоры АКЖТ (поста), у нас только показываются;
        // направление однопутного перегона — общее, меняется по согласию обеих станций;
        // занятость участков перегона (НАУ, Б12) дублируется, поезда переходят на схему соседа
        DispatchLink.StationId = "algabas";
        DispatchLink.StationName = "Станция Алгабас";
        DispatchLink.SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationAlgabas", "server.txt");
        Ui.UiSettings.Load();   // [ДОБАВЛЕНО 28.10.2026] режим интерфейса и панели (интерфейс.txt рядом с server.txt)
        Dispatch = new DispatchMirror(il, Model.Trains, "akzht", "АКЖТ",
            own: new[] { ("Ч", "Ч"), ("2-ОП", "2-ОП") },
            mirror: new[] { ("НА", "НА-ПОСТ"), ("1-ОП", "1-ОП") },
            sharedLines: new[] { ("ОП", Model.Algabas.Line) },    // перегон пост — Алгабас: направление общее
            sharedSections: new[] { ("НАУ", "НАУ"), ("Б12", "Б12") });
        DispatchLink.Log += (m, k) => Log?.Invoke(m, k);
        // [ДОБАВЛЕНО 20.10.2026, этап 2] занятие с преподавателем; поезда урока — из кнопок имитации
        Training.Init(il, Model.Trains);
        Training.SpawnSources.AddRange(Model.Algabas.View.SpawnButtons);
        ScheduleSetup.Register(Model);   // [ДОБАВЛЕНО 22.10.2026, этап 5] пункт расписания: Алгабас
        DispatchLink.StatusChanged += OnDispatchChanged;
        DispatchLink.PeersChanged += OnDispatchChanged;
        Dispatch.Apply();   // [ДОБАВЛЕНО 28.10.2026] светофоры АКЖТ (НА, 1) погашены с самого запуска, пока нет данных

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
    /// <summary>Какие светофоры отдаём АКЖТ и какие берём у неё.</summary>
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
        PumpSwitchSync(dt);

        fileAcc += dt;
        if (fileAcc >= 1.0)
        {
            fileAcc = 0;
            ReloadSensorMapIfChanged();
        }

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

    // ---------------- Arduino: стрелки и лампы ----------------
    static void OnSwitchCommand(RailSwitch sw)
    {
        if (!Model.Hw.Servos.TryGetValue(sw.Id, out var channels) || channels.Length == 0)
        {
            if (warnedNoServo.Add(sw.Id))
                Log?.Invoke($"Стрелка {sw.Label}: канал сервопривода не задан (карта в AlgabasHardware.cs) — переводится только на экране", LogKind.Aux);
            return;
        }
        var pos = sw.Plus ? "плюс" : "минус";
        Log?.Invoke($"Стрелка {sw.Label} → шилд {string.Join(" и ", channels)}, {pos}", LogKind.Info);
        foreach (var ch in channels) ArduinoLink.SendSwitch(ch, sw.Plus);
    }

    // ---------------- [ДОБАВЛЕНО 27.10.2026] установка стрелок при подключении Arduino ----------------
    // Серво не сообщают, где стоят, а программа при запуске считает все стрелки в «+».
    // Поэтому после подключения каждая стрелка с сервоприводом ставится в положение с экрана —
    // по одной, с паузой: прошивка двигает серво по очереди и в это время почти не читает порт.
    const double SyncStartDelay = 2.5;   // плата перезагружается ~2 с после открытия порта
    const double SyncStep = 1.2;         // самый длинный ход серво ~0,9 с
    static readonly Queue<RailSwitch> syncQueue = new();
    static double syncWait;
    static bool wasConnected;

    static void StartSwitchSync()
    {
        syncQueue.Clear();
        foreach (var sw in Model.IL.Switches.Values)
            if (Model.Hw.Servos.TryGetValue(sw.Id, out var ch) && ch.Length > 0) syncQueue.Enqueue(sw);
        if (syncQueue.Count == 0) return;
        syncWait = SyncStartDelay;
        Log?.Invoke($"Arduino подключена: стрелки на макете будут поставлены как на экране ({syncQueue.Count} шт.)", LogKind.Info);
    }

    static void PumpSwitchSync(double dt)
    {
        if (syncQueue.Count == 0) return;
        syncWait -= dt;
        if (syncWait > 0) return;
        syncWait = SyncStep;

        var sw = syncQueue.Dequeue();
        if (!sw.PendingCommand) OnSwitchCommand(sw);    // при неисправности команда уйдёт после устранения
        if (syncQueue.Count == 0) Log?.Invoke("Стрелки на макете поставлены как на экране", LogKind.Info);
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

    // ---------------- Arduino: датчики занятости (строка "Data:") ----------------
    static DateTime lastData = DateTime.MinValue;
    static bool dataStaleWarned;

    static bool[] lastOccupied;

    static void OnSensors(bool[] occupied)
    {
        lastData = DateTime.Now;
        if (dataStaleWarned)
        {
            dataStaleWarned = false;
            Log?.Invoke("Датчики занятости снова присылают данные", LogKind.Info);
        }
        foreach (var (secId, bit) in Model.Hw.Sensors)
        {
            if (bit < 0 || bit >= occupied.Length || !Model.IL.Sections.TryGetValue(secId, out var s)) continue;
            if (s.HwOcc == occupied[bit]) continue;
            s.HwOcc = occupied[bit];
            Log?.Invoke($"Датчик (вход {bit}): участок {s.Label} {(s.HwOcc ? "ЗАНЯТ" : "свободен")}", LogKind.Train);
            dirty = true;
        }

        // [ДОБАВЛЕНО 09.10.2026] входы, которые меняются, но ни к какому участку не привязаны, —
        // подсказка для заполнения датчики.txt
        var mapped = Model.Hw.Sensors.Values.ToHashSet();
        for (int i = 0; i < occupied.Length; i++)
        {
            if (mapped.Contains(i)) continue;
            bool was = lastOccupied != null && i < lastOccupied.Length && lastOccupied[i];
            if (occupied[i] == was) continue;
            Log?.Invoke($"Вход {i}: {(occupied[i] ? "ЗАНЯТ" : "свободен")} — к участку не привязан (впишите номер {i} в датчики.txt)", LogKind.Aux);
        }
        lastOccupied = occupied;
    }

    // ---------------- карта датчиков из файла датчики.txt ----------------
    static double fileAcc;
    static bool sensorFileLoaded;
    static DateTime sensorFileTime;

    static DateTime SensorFileTime()
    {
        var path = AlgabasHardware.SensorsFile;
        try { return File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.MinValue; }
        catch { return sensorFileTime; }
    }

    /// <summary>Перечитать датчики.txt, если его изменили (проверяется раз в секунду).
    /// Первый раз — через 1 с после запуска, когда окно журнала уже открыто.</summary>
    static void ReloadSensorMapIfChanged()
    {
        if (sensorFileLoaded && SensorFileTime() == sensorFileTime) return;
        bool first = !sensorFileLoaded;

        foreach (var msg in AlgabasHardware.LoadSensors(Model.Hw, Model.IL.Sections.Values))
            Log?.Invoke(msg, msg.StartsWith("Карта датчиков:") ? LogKind.Info : LogKind.Aux);
        sensorFileTime = SensorFileTime();          // после загрузки: файл мог быть только что создан
        sensorFileLoaded = true;
        if (!first) Log?.Invoke("датчики.txt изменён — карта перечитана", LogKind.Info);

        // занятость участков, которые больше не привязаны к датчику, снимаем; привязанные обновятся со следующей строкой "Data:"
        var bound = Model.Hw.Sensors.Keys.ToHashSet();
        foreach (var s in Model.IL.Sections.Values)
            if (s.HwOcc && !bound.Contains(s.Id)) { s.HwOcc = false; dirty = true; }
        lastOccupied = null;
    }

    /// <summary>Если Arduino перестала присылать "Data:" (кабель выдернули и т.п.) —
    /// не держим старую занятость, а снимаем её и предупреждаем.</summary>
    static void CheckSensorsAlive()
    {
        if (lastData == DateTime.MinValue || dataStaleWarned) return;
        if ((DateTime.Now - lastData).TotalSeconds < 3) return;
        dataStaleWarned = true;
        ClearHardwareOccupancy();
        Log?.Invoke("Датчики занятости молчат больше 3 с — занятость от датчиков снята. Проверьте Arduino.", LogKind.Refuse);
    }

    static void OnArduinoStatus()
    {
        bool connected = ArduinoLink.IsConnected;
        bool justConnected = connected && !wasConnected;
        wasConnected = connected;
        if (justConnected) StartSwitchSync();
        if (connected) return;
        syncQueue.Clear();
        lastData = DateTime.MinValue;
        dataStaleWarned = false;
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
            if (ArduinoLink.LastDataLine == null || dataStaleWarned) return "Датчики: данных нет (Arduino не присылает \"Data:\")";
            var busy = Model.Hw.Sensors.Keys
                .Select(k => Model.IL.Sections[k])
                .Where(s => s.HwOcc).Select(s => s.Label).ToList();
            return "Датчики: работают. " + (busy.Count == 0 ? "Все участки с датчиками свободны" : "Заняты: " + string.Join(", ", busy)) +
                   "   [" + ArduinoLink.LastDataLine + "]";
        }
    }
}
