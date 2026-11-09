namespace StationApp.Core;

// =====================================================================
//  ПОЕЗДА ПО РАСПИСАНИЮ НА СТАНЦИИ  [ДОБАВЛЕНО 22.10.2026, этап 5]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Данные — Core/ScheduleData.cs.
//
//  Расписание приходит от преподавателя в уроке (t_session → lesson.schedule). Пока урок идёт:
//   • ПОДАЧА. Поезд появляется с номером сам: из тоннеля — за ScheduleLeadMin до времени у первого пункта;
//     формирование — на пути за FormLeadMin до отправления; оборот — прибывший поезд разворачивается
//     за TurnLeadMin до отправления и получает новый номер. Поезд от соседней станции передаёт сосед
//     (DispatchMirror); если соседа нет на связи — поезд появляется на границе сам (AddBoundary).
//   • СТОЯНКА. Пассажирский (и пригородный) не уходит раньше расписания, даже если выходной открыт
//     (TrainSim.Hold). Грузовой может уйти раньше — на ГИД это видно.
//   • СОБЫТИЯ ДЛЯ ГИД. Прибытие (остановился на пути), отправление, проследование — по каждому поезду
//     и пункту этой программы → Events, преподавателю сообщением s_train_event, в s_result — trains.
//   • ОПОЗДАНИЕ. Задержка отправления по вине станции больше 5 мин (с учёта прибытия и минимальной
//     стоянки пассажирского 1 мин) → критическое событие «late», −20.
//  Что за пункты в этой программе и откуда подавать поезда — задаёт Model/ScheduleSetup.cs программы.
// =====================================================================

public static class Schedule
{
    /// <summary>Минут до отправления, за сколько поезд своего формирования встаёт на путь.</summary>
    public const double FormLeadMin = 10;
    /// <summary>Минут до отправления, за сколько поезд разворачивается при обороте.</summary>
    public const double TurnLeadMin = 5;
    /// <summary>Опоздание отправления по вине станции, после которого — критическое событие, с.</summary>
    public const double LateLimit = 300;

    static Interlocking il;
    static TrainSim sim;

    // ---- пункты этой программы и откуда подавать поезда ----
    class Point
    {
        public string Id;
        public readonly HashSet<TrackSection> Zone = new();
        public readonly Dictionary<TrackSection, string> Tracks = new();
    }
    static readonly List<Point> points = new();
    static readonly Dictionary<string, (TrackSection at, Dir dir)> entries = new();
    static readonly List<(string from, string peer, TrackSection at, Dir dir, DirectionLine line)> boundaries = new();

    // ---- урок ----
    public static ScheduleData Data { get; private set; }
    public static readonly List<TrainEventRec> Events = new();
    public static event Action Changed;
    static double leadSec = 60;
    static double? stepBefore;

    class Plan
    {
        public SchedTrain Train;
        public string Mode;               // entry / boundary / form / turn
        public SchedStop Stop;            // первый пункт поезда в этой программе
        public double At;
        public bool Done, Warned;
    }
    static readonly List<Plan> plans = new();

    /// <summary>Поезд в пункте этой программы (для событий ГИД и стоянки).</summary>
    class InPoint
    {
        public string Station;
        public int Number;
        public double EnterAt;
        public string Track;
        public double TrackAt = -1;
        public bool Stopped;
    }
    static readonly Dictionary<VirtualTrain, InPoint> inPoint = new();
    static readonly HashSet<(int, string)> lateReported = new();

    public static bool Active => Training.Active && Data != null;
    public static IEnumerable<string> Stations => points.Select(p => p.Id);

    // =================================================================
    //  НАСТРОЙКА (Model/ScheduleSetup.cs)
    // =================================================================

    public static void Init(Interlocking interlocking, TrainSim trains)
    {
        il = interlocking;
        sim = trains;
        sim.Hold = HoldReason;
    }

    /// <summary>Пункт этой программы: его участки — участки поездных маршрутов, пути — места остановки.</summary>
    public static void AddStation(string id, IEnumerable<RailRoute> routes, IEnumerable<(string name, TrackSection section)> tracks)
    {
        var p = new Point { Id = id };
        foreach (var r in routes.Where(r => !r.Shunting)) p.Zone.UnionWith(r.Sections);
        foreach (var (name, sec) in tracks) { p.Tracks[sec] = TrackNumber(name); p.Zone.Add(sec); }
        points.Add(p);
    }

    /// <summary>Откуда приходят поезда со схемы этой программы (из тоннеля): tun_akzht / tun_post.</summary>
    public static void AddEntry(string origin, TrackSection at, Dir dir) => entries[origin] = (at, dir);

    /// <summary>Граница с соседней программой: поезд из пункта from (у соседа peer) появляется здесь сам,
    /// если соседа нет на связи. line — перегон, направление которого должно быть по ходу поезда.</summary>
    public static void AddBoundary(string from, string peer, TrackSection at, Dir dir, DirectionLine line) =>
        boundaries.Add((from, peer, at, dir, line));

    /// <summary>[ДОБАВЛЕНО 23.10.2026, этап 7] Где на схеме могут появиться поезда со стороны (восстановительный поезд).</summary>
    public static IEnumerable<(TrackSection at, Dir dir)> EntryPoints => entries.Values.Concat(boundaries.Select(b => (b.at, b.dir)));

    /// <summary>«Путь 3» → «3», «Тупик 6» → «Тупик 6».</summary>
    static string TrackNumber(string name) => name.StartsWith("Путь ") ? name[5..].Trim() : name;

    // =================================================================
    //  УРОК
    // =================================================================

    public static void Begin(LessonTask lesson)
    {
        End();
        Events.Clear();
        inPoint.Clear();
        lateReported.Clear();
        plans.Clear();
        Data = lesson?.Schedule;
        if (lesson != null && lesson.TrainStepSec > 0)
        {
            stepBefore = sim.StepSeconds;
            sim.StepSeconds = lesson.TrainStepSec;
        }
        if (Data == null) { Changed?.Invoke(); return; }
        leadSec = Math.Max(0, lesson.ScheduleLeadMin) * 60;
        var mine = Stations.ToHashSet();
        foreach (var t in Data.Trains)
        {
            var ord = t.Ordered;
            int i = ord.FindIndex(s => mine.Contains(s.Station));
            if (i < 0) continue;                             // поезд не заходит в пункты этой программы
            var stop = ord[i];
            var p = new Plan { Train = t, Stop = stop };
            if (i == 0)
            {
                p.Mode = t.Origin switch
                {
                    SchedPoints.Formation => "form",
                    SchedPoints.Turn => "turn",
                    _ => "entry"
                };
                p.At = p.Mode switch
                {
                    "form" => (stop.DepSec ?? 0) - FormLeadMin * 60,
                    "turn" => (stop.DepSec ?? 0) - TurnLeadMin * 60,
                    _ => (stop.FirstSec ?? 0) - leadSec
                };
                if (p.Mode == "entry" && !entries.ContainsKey(t.Origin)) continue;   // не наш вход
            }
            else
            {
                p.Mode = "boundary";
                p.At = (stop.FirstSec ?? 0) - leadSec;
                string from = ord[i - 1].Station;
                if (!boundaries.Any(b => b.from == from)) continue;
            }
            plans.Add(p);
        }
        Say($"Расписание «{Data.Name}»: поездов по пунктам {string.Join(", ", Stations.Select(SchedPoints.Name))} — {plans.Count}");
        Changed?.Invoke();
    }

    /// <summary>Конец урока: проверить поезда, которые так и не отправились (до s_result).</summary>
    public static void Finish()
    {
        if (!Active) return;
        foreach (var (t, st) in inPoint)
            if (st.Stopped) CheckLate(st, Training.Clock, final: true);
    }

    /// <summary>Урок закончился или отменён: вернуть скорость поездов.</summary>
    public static void End()
    {
        if (stepBefore is double s) sim.StepSeconds = s;
        stepBefore = null;
    }

    // =================================================================
    //  ТАКТ (из Training.Tick, когда игровое время идёт)
    // =================================================================

    public static void Tick()
    {
        // [ИЗМЕНЕНО 23.10.2026, этап 4] прибытие / отправление отмечаются на любом уроке (и без расписания) —
        // по ним проверяется журнал ДУ-2 (Core/Journals.cs) и строится ГИД
        if (!Training.Active) return;
        double now = Training.Clock;
        foreach (var p in plans)
            if (!p.Done && now >= p.At) Release(p, now);
        Observe(now);
    }

    static void Release(Plan p, double now)
    {
        var t = p.Train;
        if (sim.Find(t.Number) != null) { p.Done = true; return; }   // уже на схеме (передал сосед, поставили руками)
        switch (p.Mode)
        {
            case "entry":
            {
                var (at, dir) = entries[t.Origin];
                Spawn(p, at, dir, $"подходит к станции ({SchedPoints.OriginName(t.Origin)})");
                break;
            }
            case "boundary":
            {
                string from = t.Ordered[t.Ordered.IndexOf(p.Stop) - 1].Station;
                var b = boundaries.First(x => x.from == from);
                if (DispatchLink.Peer(b.peer) != null) { p.Done = true; return; }   // сосед на связи — передаст сам
                if (b.line != null && b.line.Current != b.dir)
                {
                    if (!p.Warned) Say($"Поезд №{t.Number} от станции {SchedPoints.Name(from)} ждёт смены направления перегона «{b.line.Title}»", LogKind.Refuse);
                    p.Warned = true;
                    return;
                }
                Spawn(p, b.at, b.dir, $"идёт от станции {SchedPoints.Name(from)}");
                break;
            }
            case "form":
            {
                var sec = TrackSection(p.Stop);
                if (sec == null) { p.Done = true; Say($"Поезд №{t.Number}: на {SchedPoints.Name(p.Stop.Station)} нет пути «{p.Stop.Track}»", LogKind.Refuse); return; }
                if (Spawn(p, sec, t.Dir, $"сформирован на пути {p.Stop.Track}, отправление по расписанию в {p.Stop.Dep}") is { } v)
                    inPoint[v] = new InPoint { Station = p.Stop.Station, Number = t.Number, EnterAt = now, Track = p.Stop.Track, TrackAt = now, Stopped = true };
                break;
            }
            case "turn":
            {
                var from = sim.Trains.FirstOrDefault(v => v.Number == t.TurnFrom && !v.Leaving && inPoint.TryGetValue(v, out var st) &&
                                                           st.Station == p.Stop.Station && st.TrackAt >= 0);
                if (from == null)
                {
                    if (!p.Warned && now >= (p.Stop.DepSec ?? 0))
                        Say($"Оборот: поезд №{t.TurnFrom} не прибыл на {SchedPoints.Name(p.Stop.Station)} — №{t.Number} отправить не из чего", LogKind.Refuse);
                    if (now >= (p.Stop.DepSec ?? 0)) p.Warned = true;
                    return;
                }
                if (from.Dir != t.Dir) sim.Reverse(from);
                from.Number = t.Number;
                var st2 = inPoint[from];
                st2.Number = t.Number;
                st2.Stopped = true;
                p.Done = true;
                Say($"Оборот: поезд №{t.TurnFrom} обращается в поезд №{t.Number} ({t.Category}), отправление по расписанию в {p.Stop.Dep}", LogKind.Train);
                Changed?.Invoke();
                break;
            }
        }
    }

    static VirtualTrain Spawn(Plan p, TrackSection at, Dir dir, string what)
    {
        var t = p.Train;
        if (at.Occupied)
        {
            if (!p.Warned) Say($"Поезд №{t.Number} ждёт: участок {at.Label} занят", LogKind.Refuse);
            p.Warned = true;
            return null;
        }
        // как TrainSim.Spawn, но с номером из расписания
        var v = new VirtualTrain { Number = t.Number, Dir = dir };
        v.Cells.Add(at);
        at.TrainCount++;
        sim.Trains.Add(v);
        sim.OnSpawned(v);
        v.Category = t.Category;   // [ДОБАВЛЕНО 23.10.2026, этап 4] категория из расписания
        p.Done = true;
        il.Say($"Поезд №{t.Number} ({t.Category}) {what} — участок {at.Label}", LogKind.Train);
        il.ComputeAspects();
        il.NotifyChanged();
        Changed?.Invoke();
        return v;
    }

    static TrackSection TrackSection(SchedStop s)
    {
        var p = points.FirstOrDefault(x => x.Id == s.Station);
        return p?.Tracks.FirstOrDefault(kv => kv.Value == s.Track.Trim()).Key;
    }

    // =================================================================
    //  ГДЕ ПОЕЗДА: прибытие, отправление, проследование
    // =================================================================

    static Point PointOf(TrackSection s) => s == null ? null : points.FirstOrDefault(p => p.Zone.Contains(s));

    static void Observe(double now)
    {
        foreach (var t in sim.Trains)
        {
            inPoint.TryGetValue(t, out var st);
            if (t.Shunting)
            {
                if (st != null) inPoint.Remove(t);   // манёвры на ГИД не попадают
                continue;
            }
            var pt = t.Leaving ? null : PointOf(t.Head);
            if (st != null && st.Number != t.Number) st.Number = t.Number;   // номер поменяли (оборот, ПКМ)
            if (st != null && st.Station != pt?.Id)
            {
                Leave(st, now);
                inPoint.Remove(t);
                st = null;
            }
            if (st == null && pt != null)
                inPoint[t] = st = new InPoint { Station = pt.Id, Number = t.Number, EnterAt = now };
            if (st == null) continue;
            if (pt.Tracks.TryGetValue(t.Head, out var track))
            {
                if (st.TrackAt < 0) { st.Track = track; st.TrackAt = now; }
                if (!st.Stopped && t.Waiting.Length > 0)
                {
                    st.Stopped = true;
                    Emit(st, "arr", st.TrackAt);
                }
            }
        }
        foreach (var (t, st) in inPoint.ToList())
            if (!sim.Trains.Contains(t))
            {
                Leave(st, now);
                inPoint.Remove(t);
            }
    }

    static void Leave(InPoint st, double now)
    {
        Emit(st, st.Stopped ? "dep" : "pass", st.Stopped ? now : st.TrackAt >= 0 ? st.TrackAt : st.EnterAt);
        if (st.Stopped) CheckLate(st, now, final: false);
    }

    static double MinDwell(SchedTrain t, SchedStop s) => t.Passenger && s.Dwell > 0 ? 60 : 0;

    static void CheckLate(InPoint st, double now, bool final)
    {
        var t = Data?.Find(st.Number);
        var s = t?.Stop(st.Station);
        if (s?.DepSec is not double dep) return;
        double ready = Math.Max(dep, (st.TrackAt >= 0 ? st.TrackAt : st.EnterAt) + MinDwell(t, s));
        double late = now - ready;
        if (late <= LateLimit || !lateReported.Add((st.Number, st.Station))) return;
        string fact = final ? $"к концу урока не отправлен ({SchedTime.Fmt(now)})" : $"отправлен в {SchedTime.Fmt(now)}";
        Training.AddEvent(true, "late", $"Опоздание отправления поезда №{st.Number} со станции {SchedPoints.Name(st.Station)} " +
                                        $"на {(int)(late / 60)} мин: по расписанию {s.Dep}, {fact}", 20);
    }

    static void Emit(InPoint st, string kind, double clock)
    {
        var e = new TrainEventRec { Number = st.Number, Station = st.Station, Kind = kind, Clock = clock, Track = st.Track ?? "" };
        Events.Add(e);
        if (Training.TeacherOnline) DispatchLink.SendEvent(Training.TeacherId, new { kind = "s_train_event", ev = e });

        var t = Data?.Find(st.Number);
        var s = t?.Stop(st.Station);
        double? plan = kind == "arr" ? s?.ArrSec : s?.DepSec ?? s?.ArrSec;
        string what = kind switch { "arr" => "прибыл на", "dep" => "отправился с", _ => "проследовал" };
        string dev = plan is double pl ? $" (по расписанию {SchedTime.Fmt(pl)}, {SchedTime.Dev(clock, pl)} мин)" : t == null ? " (нет в расписании)" : "";
        string trk = e.Track.Length > 0 && kind != "pass" ? $", путь {e.Track}" : "";
        Training.Note($"Поезд №{st.Number} {what} {SchedPoints.Name(st.Station)}{trk} в {SchedTime.FmtSec(clock)}{dev}");
        if (t != null && kind == "arr" && s != null && s.Track.Trim().Length > 0 && e.Track != s.Track.Trim())
            Training.Note($"Поезд №{st.Number} принят на путь {e.Track}, по расписанию (ТРА) — путь {s.Track}");
        Changed?.Invoke();
    }

    // =================================================================
    //  СТОЯНКА ПО РАСПИСАНИЮ (TrainSim.Hold)
    // =================================================================

    static string HoldReason(VirtualTrain v)
    {
        if (!Active || v.Shunting || v.ShortStop || !inPoint.TryGetValue(v, out var st) || st.TrackAt < 0) return null;   // [ИЗМЕНЕНО 23.10.2026, этап 4] «Сократить стоянку»
        var pt = PointOf(v.Head);
        if (pt == null || !pt.Tracks.ContainsKey(v.Head)) return null;
        var t = Data.Find(v.Number);
        var s = t?.Stop(st.Station);
        if (t == null || !t.Passenger || s?.DepSec is not double dep || s.Pass) return null;
        double until = Math.Max(dep, st.TrackAt + MinDwell(t, s));
        return Training.Clock < until ? $"стоит на {v.Head.Label}: стоянка по расписанию до {SchedTime.Fmt(until + 59)}" : null;
    }

    // =================================================================
    //  ДЛЯ ОКНА «РАСПИСАНИЕ»
    // =================================================================

    /// <summary>Факт по поезду в пункте: прибытие, отправление (или проследование).</summary>
    public static (double? arr, double? dep) Fact(int number, string station)
    {
        double? arr = null, dep = null;
        foreach (var e in Events.Where(e => e.Number == number && e.Station == station))
        {
            if (e.Kind == "arr") arr = e.Clock;
            else if (e.Kind == "dep") dep = e.Clock;
            else { arr = e.Clock; dep = e.Clock; }
        }
        return (arr, dep);
    }

    /// <summary>Фактический путь приёма.</summary>
    public static string FactTrack(int number, string station) =>
        Events.LastOrDefault(e => e.Number == number && e.Station == station && e.Track.Length > 0)?.Track ?? "";

    /// <summary>Где поезд сейчас: «ожидается», «в пути», «на станции», «отправлен», «проследовал».</summary>
    public static string State(int number, string station)
    {
        var (arr, dep) = Fact(number, station);
        if (dep != null) return arr == dep ? "проследовал" : "отправлен";
        if (arr != null || inPoint.Values.Any(s => s.Number == number && s.Station == station)) return "на станции";
        return sim.Find(number) != null ? "в пути" : "ожидается";
    }

    static void Say(string m, LogKind k = LogKind.Aux) => il?.Say("[Расписание] " + m, k);
}
