using System.Text.Json;
using StationApp.Model;

namespace StationApp.Core;

// =====================================================================
//  ЗАНЯТИЕ: режим «преподаватель — студент»  [ДОБАВЛЕНО 20.10.2026, этап 2]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса — правите в одной, скопируйте в другую.
//
//  Программа «Преподаватель» подключается к тому же серверу PromServer под именем "teacher".
//  Всё общение — разовые сообщения (DispatchLink.SendEvent / EventReceived), поле data.kind:
//    преподаватель → станция:
//      t_hello   {students:[{id,name,group,pwd}], allowAdd}      список студентов для входа
//      t_login   {ok, msg, student{id,name,group}}                ответ на вход / регистрацию
//      t_session {active, gameId, lesson{...}, student, clock, running, speed, take}
//      t_time    {clock, running, speed}                         игровое время (раз в 2 с и при изменении)
//      t_take    {on}                                            преподаватель забрал / вернул управление
//      t_stop    {}                                              занятие закончено → станция шлёт s_result
//      t_screen  {w, rx,ry,rw,rh, tag}                           нужен снимок экрана → s_screen [рамка — доля окна, этап 6]
//      t_input   {act, x, y, btn, delta, text}                   мышь / клавиатура преподавателя
//      t_objects {}                                              [этап 3] нужен список объектов → s_objects
//      t_fault_add {fault{kind,obj,cond,condState,trainFilter,trainNumber,start}}   [этап 3] неисправность
//      t_fault_remove {id}                                       [этап 3] снять неисправность
//      t_lang {lang}, t_session.lang — ru / kk / en               [этап 8] язык интерфейса от преподавателя
//      t_session.lesson.schedule {name, trains:[{number,category,origin,turnFrom,stops:[{station,arr,dep,track}]}]}  [этап 5] расписание
//    станция → преподаватель:
//      s_hello {student}, s_login {id,pwd}, s_register {name,group,pwd},
//      s_progress {...}, s_result {..., faults, du46}, s_screen {jpg,w,h,tag,parts:[{name,x,y,w,h}]},
//      s_objects {signals,switches,sections,lines,trains:[{id,label}], kinds, states}, s_faults {list}  [этап 3]
//      s_train_event {ev{number,station,kind:arr|dep|pass,clock,track}}, s_result.trains — для ГИД  [этап 5]
//      s_result.docs [{title, columns, rows}] — журналы ДУ-2/47/58/60, бланки, предупреждения, радиосвязь, автопроверка  [этап 4]
//      s_result.supplied, removed, localDeparted; docs — журнал ПНП, восстановительный поезд  [этап 7]
//
//  Пока занятия нет — программа работает как раньше (время не замедляется, ничего не считается).
// =====================================================================

/// <summary>Урок (деловая игра) — что должен сделать студент.</summary>
public class LessonTask
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Сколько поездов принять / отправить (0 — не требуется).</summary>
    public int Receive { get; set; }
    public int Depart { get; set; }
    /// <summary>Сколько критических событий допустимо для зачёта.</summary>
    public int MaxCritical { get; set; }
    /// <summary>Порог зачёта по 100-балльной шкале.</summary>
    public int PassScore { get; set; } = 70;
    /// <summary>Сколько поездов программа подаст сама (виртуальные) и раз во сколько игровых минут.</summary>
    public int AutoTrains { get; set; }
    public int EveryMin { get; set; } = 5;
    /// <summary>Длительность урока, игровых минут (0 — без ограничения).</summary>
    public int Minutes { get; set; }
    /// <summary>Игровое время начала, «08:00».</summary>
    public string StartClock { get; set; } = "08:00";

    // [ДОБАВЛЕНО 21.10.2026, этап 6] пульт: выдержки (игровые секунды) и раздельное управление
    public double CancelFreeSec { get; set; } = 6;
    public double CancelTrainSec { get; set; } = 180;
    public double CancelShuntSec { get; set; } = 60;
    public double IrSec { get; set; } = 180;
    public double CrankSec { get; set; } = 60;
    public bool SeparateControl { get; set; }

    // [ДОБАВЛЕНО 22.10.2026, этап 5] расписание (Core/ScheduleData.cs, Core/Schedule.cs)
    public ScheduleData Schedule { get; set; }
    /// <summary>За сколько минут до времени у первого пункта поезд появляется на подходе.</summary>
    public double ScheduleLeadMin { get; set; } = 1;
    /// <summary>Игровых секунд на проход участка виртуальным поездом на время урока (0 — как на станции).</summary>
    public double TrainStepSec { get; set; }

    // [ДОБАВЛЕНО 23.10.2026, этап 7] маневровая работа, ПНП, восстановительный поезд (Core/Shunting.cs, Core/Recovery.cs)
    /// <summary>План: подать вагонов на грузовые фронты, убрать, отправить местных (0 — не требуется).</summary>
    public int PlanSupply { get; set; }
    public int PlanRemove { get; set; }
    public int PlanLocalDepart { get; set; }
    /// <summary>Местных вагонов (на ПНП этой станции) в каждом грузовом поезде.</summary>
    public int LocalWagons { get; set; } = 4;
    /// <summary>Погрузка / выгрузка на фронте, игровых минут на вагон.</summary>
    public double PnpMinPerWagon { get; set; } = 5;
    public double RecoveryArriveMin { get; set; } = 10;
    public double RecoveryWorkMin { get; set; } = 15;
}

public class StudentInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public bool Pwd { get; set; }
    public override string ToString() => Group.Length > 0 ? $"{Name} ({Group})" : Name;
}

/// <summary>Нарушение студента: критическое событие или неправильное действие.</summary>
public class TrainingEvent
{
    public double Clock { get; set; }
    public bool Critical { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public int Penalty { get; set; }
}

public static class Training
{
    public const string TeacherId = "teacher";
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // ------------------------ состояние ------------------------
    public static bool Active { get; private set; }
    public static string GameId { get; private set; } = "";
    public static LessonTask Lesson { get; private set; }
    public static StudentInfo Student { get; private set; }
    /// <summary>Игровое время, секунды от полуночи.</summary>
    public static double Clock { get; private set; }
    public static bool Running { get; private set; }
    public static int Speed { get; private set; } = 1;
    public static bool TeacherControl { get; private set; }
    public static List<StudentInfo> Students { get; private set; } = new();
    public static bool AllowAdd { get; private set; }

    public static int Received { get; private set; }
    public static int Departed { get; private set; }
    public static readonly List<TrainingEvent> Events = new();
    /// <summary>Подробный журнал действий за занятие (уходит в отчёт).</summary>
    public static readonly List<string> Journal = new();
    static DateTime startedAt;
    static double clockStart;

    /// <summary>Что-то изменилось (для окна).</summary>
    public static event Action Changed;
    /// <summary>Ответ на вход: успех, текст.</summary>
    public static event Action<bool, string> LoginResult;

    /// <summary>Снимок экрана (задаёт окно): макс. ширина → (jpeg base64, w, h).</summary>
    /// <summary>[ИЗМЕНЕНО 21.10.2026] макс. ширина, область окна (доли 0..1, null — всё окно) → кадр.</summary>
    public static Func<int, (double x, double y, double w, double h)?, (string jpg, int w, int h)?> CaptureScreen;
    /// <summary>[ДОБАВЛЕНО 21.10.2026] Части окна, которые преподаватель может развернуть на весь экран
    /// (у АКЖТ: «АКЖТ», «Путевой пост»): название и рамка в долях окна.</summary>
    public static Func<IEnumerable<(string name, double x, double y, double w, double h)>> ScreenParts;
    /// <summary>Ввод преподавателя (задаёт окно).</summary>
    public static Action<JsonElement> InjectInput;
    /// <summary>Преподаватель забрал управление (окно выводит себя вперёд).</summary>
    public static Action TakeOver;
    /// <summary>Откуда подавать поезда урока: название → действие (кнопки имитации станции).</summary>
    public static readonly List<(string name, Action spawn)> SpawnSources = new();

    static Interlocking il;
    static TrainSim trains;
    static string StationName => DispatchLink.StationName;

    public static bool TeacherOnline => DispatchLink.IsConnected && DispatchLink.Peers.TryGetValue(TeacherId, out var p) && p.Online;
    /// <summary>Занятие идёт, но время на паузе: модель стоит, пульт не принимает команды.</summary>
    public static bool Paused => Active && !Running;
    public static string ClockText => Fmt(Clock);
    public static string Fmt(double sec) { var t = TimeSpan.FromSeconds(Math.Max(0, sec) % 86400); return $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}"; }

    /// <summary>Шаг модели с учётом паузы и ускорения (вне занятия — как есть).</summary>
    public static double ModelDt(double dt) => !Active ? dt : Running ? dt * Speed : 0;

    public static void Init(Interlocking interlocking, TrainSim sim)
    {
        il = interlocking;
        trains = sim;
        DispatchLink.EventReceived += OnEvent;
        DispatchLink.PeersChanged += OnPeers;
        il.RouteRefused += (r, why) => Wrong("refuse", $"Попытка задать маршрут {r.Name}: {why}", 2);
        il.CancelledBeforeTrain += r => Wrong("cancel", $"Отмена маршрута {r.Name} при поезде на участке приближения {r.Start.Approach?.Label}", 5);
        il.TrainPassed += OnTrainPassed;
        ModelHost.Log += (m, _) => { if (Active && Journal.Count < 5000) Journal.Add($"{ClockText}  {m}"); };
        // [ДОБАВЛЕНО 20.10.2026, этап 3] неисправности, журнал осмотра ДУ-46, кнопки со счётчиком
        Faults.Init(il, sim);
        Du46.Init(il);
        Schedule.Init(il, sim);   // [ДОБАВЛЕНО 22.10.2026, этап 5] поезда по расписанию
        Radio.Init(il, sim);      // [ДОБАВЛЕНО 23.10.2026, этап 4] радиосвязь, предупреждения, «окна»
        Journals.Init(il, sim);   // [ДОБАВЛЕНО 23.10.2026, этап 4] журналы ДУ-2/47/58/60, бланки
        Shunting.Init(il, sim);   // [ДОБАВЛЕНО 23.10.2026, этап 7] вагоны, ПНП
        Recovery.Init(il, sim);   // [ДОБАВЛЕНО 23.10.2026, этап 7] восстановительный поезд
        Faults.Changed += () => { if (Active) Send(Faults.FaultsMessage()); };
        // [ДОБАВЛЕНО 21.10.2026, этап 6] взрез стрелки поездом — критическое событие (кроме отказа тормозов)
        il.SwitchCut += (sw, t) =>
        {
            if (t != null && !t.FaultBrakes)
                AddEvent(true, "cut", $"Взрез стрелки {sw.Label} поездом №{t.Number}", 20);
        };
    }

    // ---- [ДОБАВЛЕНО 21.10.2026, этап 6] выдержки пульта на время урока ----
    static (double free, double train, double shunt, double ir, double crank, bool separate)? panelBefore;

    static void ApplyPanelSettings()
    {
        panelBefore ??= (il.CancelFreeSeconds, il.ArtificialReleaseSeconds, il.ShuntReleaseSeconds, il.IrSeconds, il.CrankSeconds, il.SeparateControl);
        il.CancelFreeSeconds = Lesson.CancelFreeSec;
        il.ArtificialReleaseSeconds = Lesson.CancelTrainSec;
        il.ShuntReleaseSeconds = Lesson.CancelShuntSec;
        il.IrSeconds = Lesson.IrSec;
        il.CrankSeconds = Lesson.CrankSec;
        il.SeparateControl = Lesson.SeparateControl;
    }

    static void RestorePanelSettings()
    {
        if (panelBefore is not var (free, train, shunt, ir, crank, separate)) return;
        (il.CancelFreeSeconds, il.ArtificialReleaseSeconds, il.ShuntReleaseSeconds, il.IrSeconds, il.CrankSeconds, il.SeparateControl) =
            (free, train, shunt, ir, crank, separate);
        panelBefore = null;
    }

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Запись только в журнал для преподавателя (студент её не видит).</summary>
    public static void Note(string m)
    {
        if (Active && Journal.Count < 5000) Journal.Add($"{ClockText}  {m}");
    }

    static void Say(string m, LogKind k = LogKind.Aux) => il?.Say("[Занятие] " + m, k);

    // =================================================================
    //  ПОДСЧЁТ
    // =================================================================

    static void OnTrainPassed(RailRoute r)
    {
        if (!Active) return;
        if (r.ExitSection != null) { Departed++; Say($"Поезд отправлен ({r.Name}). Отправлено: {Departed}"); }
        else if (r.Start.Has("yellow1")) { Received++; Say($"Поезд принят ({r.Name}). Принято: {Received}"); }
        Changed?.Invoke();
    }

    static void Wrong(string code, string text, int penalty) => AddEvent(false, code, text, penalty);

    /// <summary>[ИЗМЕНЕНО 20.10.2026, этап 3] public — нарушения добавляют и Faults, Du46. В конце урока
    /// (finishing) засчитываются и на паузе.</summary>
    public static void AddEvent(bool critical, string code, string text, int penalty)
    {
        if (!Active || (Paused && !finishing)) return;
        Events.Add(new TrainingEvent { Clock = Clock, Critical = critical, Code = code, Text = text, Penalty = penalty });
        Say((critical ? "КРИТИЧЕСКОЕ СОБЫТИЕ: " : "Неправильное действие: ") + text, LogKind.Refuse);
        Changed?.Invoke();
    }

    /// <summary>Штраф по видам нарушений; у мелких — потолок, чтобы одна ошибка не съела всю оценку.</summary>
    static readonly Dictionary<string, int> PenaltyCap = new() { ["refuse"] = 20, ["cancel"] = 20, ["du46_btn"] = 20,
        // [ДОБАВЛЕНО 23.10.2026, этап 4] журналы и бланки
        ["du2"] = 10, ["du58"] = 10, ["du60"] = 10, ["du47"] = 10, ["du61"] = 15, ["blank"] = 10, ["permit_route"] = 15 };

    public static int CriticalCount => Events.Count(e => e.Critical);
    public static int WrongCount => Events.Count(e => !e.Critical);

    public static int Score
    {
        get
        {
            int s = 100;
            foreach (var g in Events.GroupBy(e => e.Code))
            {
                int sum = g.Sum(e => e.Penalty);
                s -= PenaltyCap.TryGetValue(g.Key, out var cap) ? Math.Min(sum, cap) : sum;
            }
            if (Lesson != null)
            {
                s -= 10 * Math.Max(0, Lesson.Receive - Received);
                s -= 10 * Math.Max(0, Lesson.Depart - Departed);
                s -= Shunting.PlanPenalty();   // [ДОБАВЛЕНО 23.10.2026, этап 7] подано / убрано / отправлено местных вагонов
            }
            return Math.Clamp(s, 0, 100);
        }
    }

    public static bool Passed => Lesson != null && Score >= Lesson.PassScore && CriticalCount <= Lesson.MaxCritical;

    // ---- задержка поезда у закрытого входного светофора (критическое событие, как в тренажёре ДСП) ----
    const double DelayLimit = 180;     // игровых секунд
    static readonly Dictionary<RailSignal, double> waitSince = new();
    static readonly HashSet<RailSignal> delayReported = new();

    static void CheckDelays()
    {
        foreach (var s in il.Signals.Values)
        {
            if (s.Kind != SignalKind.Controlled || !s.Has("yellow1") || s.Approach == null) continue;
            bool closed = AspectInfo.IsRestrictive(s.Aspect) && !s.InviteOn;
            bool train = s.Approach.HwOcc ||
                         trains.Trains.Any(t => !t.Leaving && !t.Shunting && t.Head == s.Approach && t.Dir == s.Dir);
            if (!(closed && train)) { waitSince.Remove(s); delayReported.Remove(s); continue; }
            if (!waitSince.TryGetValue(s, out var since)) { waitSince[s] = Clock; continue; }
            if (Clock - since >= DelayLimit && delayReported.Add(s))
                AddEvent(true, "delay", $"Задержка поезда у закрытого входного светофора {s.Label} (участок {s.Approach.Label}) более 3 мин", 20);
        }
    }

    // =================================================================
    //  ТАКТ (из ModelHost, 10 раз в секунду, dt — реальные секунды)
    // =================================================================

    static double progressAcc, spawnAt;
    static int spawned, spawnIndex;
    static string lastProgress = "";

    public static void Tick(double dt)
    {
        if (!Active) return;
        if (Running)
        {
            Clock += dt * Speed;
            CheckDelays();
            AutoTrains();
            Faults.Tick();
            Du46.CheckPresses(false);
            Schedule.Tick();   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        }
        progressAcc += dt;
        if (progressAcc >= 2) { progressAcc = 0; SendProgress(false); }
    }

    static void AutoTrains()
    {
        if (Lesson == null || Lesson.AutoTrains <= spawned || SpawnSources.Count == 0 || Clock < spawnAt) return;
        var (name, spawn) = SpawnSources[spawnIndex++ % SpawnSources.Count];
        spawned++;
        spawnAt = Clock + Math.Max(1, Lesson.EveryMin) * 60;
        Say($"По уроку подаётся поезд {spawned} из {Lesson.AutoTrains}: {name}");
        spawn();
    }

    // =================================================================
    //  СООБЩЕНИЯ
    // =================================================================

    static bool teacherWasOnline;

    static void OnPeers()
    {
        bool on = TeacherOnline;
        if (on == teacherWasOnline) return;
        teacherWasOnline = on;
        if (on) Send(new { kind = "s_hello", student = Student });
        else Say(Active ? "Связь с преподавателем потеряна — занятие продолжается" : "Преподаватель отключился", LogKind.Refuse);
        if (!on) TeacherControl = false;
        Changed?.Invoke();
    }

    static void Send(object data)
    {
        if (TeacherOnline) DispatchLink.SendEvent(TeacherId, data);
    }

    public static void Login(string id, string pwd) => Send(new { kind = "s_login", id, pwd });
    public static void Register(string name, string group, string pwd) => Send(new { kind = "s_register", name, group, pwd });

    static void OnEvent(string from, JsonElement d)
    {
        if (from != TeacherId) return;
        try
        {
            switch (Str(d, "kind"))
            {
                case "t_hello":
                    Students = d.TryGetProperty("students", out var st) ? st.Deserialize<List<StudentInfo>>(Json) ?? new() : new();
                    AllowAdd = Bool(d, "allowAdd");
                    break;
                case "t_login":
                {
                    bool ok = Bool(d, "ok");
                    if (ok && d.TryGetProperty("student", out var s) && s.ValueKind == JsonValueKind.Object)
                    {
                        Student = s.Deserialize<StudentInfo>(Json);
                        Say($"За пультом: {Student}");
                    }
                    LoginResult?.Invoke(ok, Str(d, "msg"));
                    break;
                }
                case "t_session":
                    if (Str(d, "lang").Length > 0) L.Set(Str(d, "lang"));   // [ДОБАВЛЕНО 22.10.2026, этап 8]
                    OnSession(d);
                    break;
                case "t_lang": L.Set(Str(d, "lang")); return;   // [ДОБАВЛЕНО 22.10.2026, этап 8] язык от преподавателя
                case "t_time": ReadTime(d); break;
                case "t_take":
                    TeacherControl = Bool(d, "on");
                    if (TeacherControl) TakeOver?.Invoke();
                    Say(TeacherControl ? "Управление пультом забрал преподаватель" : "Преподаватель вернул управление");
                    break;
                case "t_stop":
                    if (Active) Finish();
                    break;
                case "t_screen":
                {
                    int w = d.TryGetProperty("w", out var we) && we.TryGetInt32(out var wi) ? wi : 1600;
                    // [ИЗМЕНЕНО 21.10.2026] область окна и метка кадра (большое окно экрана у преподавателя)
                    double Num(string n, double def) => d.TryGetProperty(n, out var v) && v.TryGetDouble(out var x) ? x : def;
                    (double, double, double, double)? region = d.TryGetProperty("rw", out _)
                        ? (Num("rx", 0), Num("ry", 0), Num("rw", 1), Num("rh", 1)) : null;
                    var shot = CaptureScreen?.Invoke(w, region);
                    var parts = ScreenParts?.Invoke()?.Select(p => new { name = p.name, x = p.x, y = p.y, w = p.w, h = p.h }).ToList();
                    if (shot is { } s) Send(new { kind = "s_screen", jpg = s.jpg, w = s.w, h = s.h, tag = Str(d, "tag"), parts });
                    return;   // без Changed — кадры идут часто
                }
                case "t_input":
                    if (TeacherControl) InjectInput?.Invoke(d);
                    return;
                case "t_objects":
                    Send(Faults.ObjectsMessage());
                    return;
                case "t_fault_add":
                    if (d.TryGetProperty("fault", out var fe) && fe.ValueKind == JsonValueKind.Object) Faults.Add(fe);
                    Send(Faults.FaultsMessage());
                    return;
                case "t_fault_remove":
                    Faults.Remove(Str(d, "id"));
                    Send(Faults.FaultsMessage());
                    return;
            }
        }
        catch (Exception ex)
        {
            Say("ошибка сообщения преподавателя: " + ex.Message, LogKind.Refuse);
        }
        Changed?.Invoke();
    }

    static void ReadTime(JsonElement d)
    {
        if (d.TryGetProperty("clock", out var c) && c.TryGetDouble(out var cv)) Clock = cv;
        bool running = Bool(d, "running");
        if (running != Running && Active) Say(running ? "Игровое время: ПУСК" : "Игровое время: ПАУЗА");
        Running = running;
        if (d.TryGetProperty("speed", out var sp) && sp.TryGetInt32(out var spv)) Speed = Math.Clamp(spv, 1, 10);
    }

    static void OnSession(JsonElement d)
    {
        if (!Bool(d, "active"))
        {
            if (Active) { Active = false; Running = false; TeacherControl = false; RestorePanelSettings(); Schedule.End(); Shunting.End(); Say("Преподаватель отменил занятие"); }
            return;
        }
        string id = Str(d, "gameId");
        if (d.TryGetProperty("student", out var s) && s.ValueKind == JsonValueKind.Object)
            Student = s.Deserialize<StudentInfo>(Json);
        TeacherControl = Bool(d, "take");
        if (id != GameId || !Active)
        {
            Lesson = d.GetProperty("lesson").Deserialize<LessonTask>(Json);
            Begin(id);
        }
        ReadTime(d);
    }

    /// <summary>Новое занятие: пульт в исходное, счётчики с нуля.</summary>
    static void Begin(string id)
    {
        GameId = id;
        Active = true;
        Received = Departed = 0;
        Events.Clear();
        Journal.Clear();
        waitSince.Clear();
        delayReported.Clear();
        spawned = spawnIndex = 0;
        startedAt = DateTime.Now;
        Clock = clockStart = ParseClock(Lesson.StartClock);
        spawnAt = Clock + 30;
        Faults.Reset();
        Du46.ResetLesson();
        Radio.ResetLesson();      // [ДОБАВЛЕНО 23.10.2026, этап 4]
        Journals.ResetLesson();
        Shunting.Begin(Lesson);   // [ДОБАВЛЕНО 23.10.2026, этап 7]
        Recovery.Begin(Lesson);
        trains.RemoveAll();
        il.ResetAll();
        ApplyPanelSettings();
        Schedule.Begin(Lesson);   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        Say($"Начато занятие «{Lesson.Name}». Студент: {Student?.ToString() ?? "не вошёл"}. Время ждёт пуска преподавателем");
    }

    /// <summary>Конец занятия: результат — преподавателю.</summary>
    static bool finishing;

    static void Finish()
    {
        // [ДОБАВЛЕНО 20.10.2026, этап 3] последние проверки: неисправности и кнопки со счётчиком
        finishing = true;
        Faults.Finish();
        Du46.CheckPresses(true);
        Schedule.Finish();   // [ДОБАВЛЕНО 22.10.2026, этап 5] поезда, так и не отправленные
        Journals.Finish();   // [ДОБАВЛЕНО 23.10.2026, этап 4] автопроверка журналов
        finishing = false;
        SendProgress(true);
        Send(new
        {
            kind = "s_result",
            gameId = GameId,
            station = DispatchLink.StationId,
            stationName = StationName,
            student = Student,
            lessonId = Lesson?.Id,
            lessonName = Lesson?.Name,
            started = startedAt.ToString("s"),
            ended = DateTime.Now.ToString("s"),
            clockStart,
            clockEnd = Clock,
            received = Received,
            departed = Departed,
            taskReceive = Lesson?.Receive ?? 0,
            taskDepart = Lesson?.Depart ?? 0,
            events = Events,
            score = Score,
            passed = Passed,
            journal = Journal,
            faults = Faults.ReportList(),
            du46 = Du46.Records,
            trains = Schedule.Events,             // [ДОБАВЛЕНО 22.10.2026, этап 5] для ГИД
            scheduleName = Schedule.Data?.Name,
            docs = Journals.ReportDocs().Concat(Shunting.ReportDocs()).Concat(RecoveryDoc()).ToList(),   // [ДОБАВЛЕНО 23.10.2026, этапы 4, 7]
            supplied = Shunting.Supplied, removed = Shunting.Removed, localDeparted = Shunting.LocalDeparted,
        });
        Active = false;
        Running = false;
        TeacherControl = false;
        RestorePanelSettings();
        Schedule.End();   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        Shunting.End();   // [ДОБАВЛЕНО 23.10.2026, этап 7]
        Say($"Занятие закончено. Оценка: {Score} из 100 — {(Passed ? "ЗАЧЁТ" : "НЕЗАЧЁТ")}. Результат отправлен преподавателю");
    }

    /// <summary>[ДОБАВЛЕНО 23.10.2026, этап 7] Таблица вызовов восстановительного поезда для отчёта.</summary>
    static IEnumerable<object> RecoveryDoc()
    {
        if (Recovery.Calls.Count == 0) yield break;
        yield return new
        {
            title = "Восстановительный поезд",
            columns = new[] { "Поезд", "К поезду", "Место схода", "Вызван", "Подход", "Итог" },
            rows = Recovery.ReportRows().ToList(),
        };
    }

    static void SendProgress(bool force)
    {
        var p = new
        {
            kind = "s_progress",
            student = Student,
            active = Active,
            received = Received,
            departed = Departed,
            critical = CriticalCount,
            wrong = WrongCount,
            score = Score,
            take = TeacherControl,
        };
        string json = JsonSerializer.Serialize(p);
        if (!force && json == lastProgress) return;
        lastProgress = json;
        Send(p);
    }

    public static double ParseClock(string s) =>
        TimeSpan.TryParse(s, out var t) ? t.TotalSeconds : 8 * 3600;

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
