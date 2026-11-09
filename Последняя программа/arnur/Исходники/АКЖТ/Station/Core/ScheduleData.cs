using System.Text.Json.Serialization;

namespace StationApp.Core;

// =====================================================================
//  УЧЕБНОЕ РАСПИСАНИЕ — ДАННЫЕ  [ДОБАВЛЕНО 22.10.2026, этап 5]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса; программа «Преподаватель» подключает его ссылкой
//  (как Model.cs). Здесь только данные и проверка — без окон и без модели станции.
//
//  Линия (слева направо = чётное направление →, номера поездов чётные):
//     тоннель ─ АКЖТ ═══ (двухпутный перегон) ═══ Путевой пост ─── (однопутный перегон) ─── Алгабас (тупиковая)
//                                                         └─ тоннель II
//  Чётные (→): из тоннеля к Ч АКЖТ → АКЖТ → пост → Алгабас (конечная) или тоннель II.
//  Нечётные (←): из тоннеля I к посту или со станции Алгабас → пост → АКЖТ → тоннель (или конечная на АКЖТ).
//  Время — игровое, «ЧЧ:ММ». На посту путей нет — одно время проследования (Arr = Dep).
//
//  Откуда поезд (Origin):
//    tun_akzht — из тоннеля к входному Ч АКЖТ (чётные);  tun_post — из тоннеля I к посту (нечётные);
//    form      — формирование: стоит на пути первой станции и отправляется по расписанию;
//    turn      — оборот: поезд TurnFrom, прибывший на первую станцию, разворачивается и уходит с этим номером.
// =====================================================================

public static class SchedPoints
{
    public const string Akzht = "akzht", Post = "post", Algabas = "algabas";
    /// <summary>Порядок пунктов по линии слева направо (чётное направление).</summary>
    public static readonly string[] Order = { Akzht, Post, Algabas };

    public static string Name(string id) => id switch
    {
        Akzht => "АКЖТ", Post => "Путевой пост", Algabas => "Алгабас", _ => id
    };

    public const string FromTunnelAkzht = "tun_akzht", FromTunnelPost = "tun_post", Formation = "form", Turn = "turn";

    public static readonly (string id, string name)[] Origins =
    {
        (FromTunnelAkzht, "из тоннеля к Ч АКЖТ"),
        (FromTunnelPost, "из тоннеля I к посту"),
        (Formation, "формирование на станции"),
        (Turn, "оборот поезда"),
    };

    public static string OriginName(string id) => Origins.FirstOrDefault(o => o.id == id).name ?? id;

    public static readonly string[] Categories = { "пассажирский", "пригородный", "грузовой", "хозяйственный", "локомотив" };

    public static bool IsPassenger(string category) => category is "пассажирский" or "пригородный";
}

/// <summary>Время «ЧЧ:ММ» ↔ секунды от полуночи.</summary>
public static class SchedTime
{
    public static double? Parse(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return null;
        return TimeSpan.TryParse(s, out var t) && t.TotalHours < 48 ? t.TotalSeconds : null;
    }

    public static string Fmt(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec) % 86400);
        return $"{t.Hours:00}:{t.Minutes:00}";
    }

    public static string FmtSec(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec) % 86400);
        return $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>Отклонение «+3» / «−2» / «0» в минутах.</summary>
    public static string Dev(double fact, double plan)
    {
        int m = (int)Math.Round((fact - plan) / 60);
        return m > 0 ? "+" + m : m < 0 ? "−" + (-m) : "0";
    }
}

public class SchedStop
{
    public string Station { get; set; } = "";
    public string Arr { get; set; } = "";
    public string Dep { get; set; } = "";
    /// <summary>Путь приёма по ТРА: «1», «2»… (на посту не нужен).</summary>
    public string Track { get; set; } = "";

    [JsonIgnore] public double? ArrSec => SchedTime.Parse(Arr);
    [JsonIgnore] public double? DepSec => SchedTime.Parse(Dep);
    /// <summary>Проследование без остановки (время прибытия и отправления одно или задано одно из них при транзите).</summary>
    [JsonIgnore] public bool Pass => ArrSec is double a && DepSec is double d && Math.Abs(d - a) < 1;
    /// <summary>Стоянка, с (0 — проследование, null — начальный или конечный пункт).</summary>
    [JsonIgnore] public double? Dwell => ArrSec is double a && DepSec is double d ? d - a : null;
    /// <summary>Первое известное время в пункте (для порядка и появления поезда).</summary>
    [JsonIgnore] public double? FirstSec => ArrSec ?? DepSec;
}

public class SchedTrain
{
    public int Number { get; set; }
    public string Category { get; set; } = "грузовой";
    public string Origin { get; set; } = SchedPoints.FromTunnelAkzht;
    /// <summary>Для оборота: номер поезда, который разворачивается в этот.</summary>
    public int TurnFrom { get; set; }
    public string Note { get; set; } = "";
    public List<SchedStop> Stops { get; set; } = new();

    /// <summary>Чётные номера — чётное направление (→), нечётные — нечётное (←), как у виртуальных поездов.</summary>
    [JsonIgnore] public Dir Dir => Number % 2 == 0 ? Dir.Right : Dir.Left;
    [JsonIgnore] public bool Passenger => SchedPoints.IsPassenger(Category);

    public SchedStop Stop(string station) => Stops.FirstOrDefault(s => s.Station == station);

    /// <summary>Пункты по ходу поезда.</summary>
    [JsonIgnore]
    public List<SchedStop> Ordered
    {
        get
        {
            var order = Dir == Dir.Right ? SchedPoints.Order : SchedPoints.Order.Reverse().ToArray();
            return order.Select(Stop).Where(s => s != null).ToList();
        }
    }

    [JsonIgnore] public double FirstSec => Ordered.Select(s => s.FirstSec).FirstOrDefault(x => x != null) ?? 0;

    /// <summary>Куда идёт поезд после последнего пункта.</summary>
    [JsonIgnore]
    public string Destination
    {
        get
        {
            var last = Ordered.LastOrDefault();
            if (last == null) return "";
            if (last.DepSec == null) return "конечная — " + SchedPoints.Name(last.Station) + (last.Track.Length > 0 ? ", путь " + last.Track : "");
            return last.Station switch
            {
                SchedPoints.Akzht => "тоннель (за АКЖТ)",
                SchedPoints.Post => "тоннель II (за постом)",
                _ => SchedPoints.Name(last.Station)
            };
        }
    }

    public override string ToString() => $"№{Number} ({Category})";
}

public class ScheduleData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Новое расписание";
    public string Description { get; set; } = "";
    public List<SchedTrain> Trains { get; set; } = new();

    public SchedTrain Find(int number) => Trains.FirstOrDefault(t => t.Number == number);

    public override string ToString() => Name;

    /// <summary>Ошибки расписания (пусто — всё в порядке). Проверяется в программе преподавателя при правке.</summary>
    public List<string> Validate()
    {
        var err = new List<string>();
        foreach (var g in Trains.GroupBy(t => t.Number).Where(g => g.Count() > 1))
            err.Add($"Номер {g.Key} повторяется");
        foreach (var t in Trains)
        {
            string n = $"Поезд №{t.Number}: ";
            if (t.Number <= 0) { err.Add(n + "номер должен быть больше 0"); continue; }
            var st = t.Ordered;
            if (st.Count == 0) { err.Add(n + "не задано ни одного времени"); continue; }
            foreach (var s in st)
            {
                if (s.Arr.Trim().Length > 0 && s.ArrSec == null) err.Add(n + $"{SchedPoints.Name(s.Station)}: время прибытия «{s.Arr}» — нужно ЧЧ:ММ");
                if (s.Dep.Trim().Length > 0 && s.DepSec == null) err.Add(n + $"{SchedPoints.Name(s.Station)}: время отправления «{s.Dep}» — нужно ЧЧ:ММ");
                if (s.ArrSec is double a && s.DepSec is double d && d < a) err.Add(n + $"{SchedPoints.Name(s.Station)}: отправление раньше прибытия");
            }
            // пункты подряд, без пропуска поста; время не убывает по ходу
            var idx = st.Select(s => Array.IndexOf(SchedPoints.Order, s.Station)).ToList();
            for (int i = 1; i < idx.Count; i++)
                if (Math.Abs(idx[i] - idx[i - 1]) != 1) err.Add(n + "между АКЖТ и Алгабасом нужно время проследования поста");
            for (int i = 1; i < st.Count; i++)
                if (st[i].FirstSec is double b && (st[i - 1].DepSec ?? st[i - 1].ArrSec) is double a && b < a)
                    err.Add(n + $"на {SchedPoints.Name(st[i].Station)} раньше, чем ушёл с {SchedPoints.Name(st[i - 1].Station)}");
            var first = st[0];
            var last = st[^1];
            switch (t.Origin)
            {
                case SchedPoints.FromTunnelAkzht:
                    if (t.Dir != Dir.Right || first.Station != SchedPoints.Akzht) err.Add(n + "из тоннеля к Ч АКЖТ идут только чётные поезда, первый пункт — АКЖТ");
                    break;
                case SchedPoints.FromTunnelPost:
                    if (t.Dir != Dir.Left || first.Station != SchedPoints.Post) err.Add(n + "из тоннеля I к посту идут только нечётные поезда, первый пункт — пост");
                    break;
                case SchedPoints.Formation:
                case SchedPoints.Turn:
                    if (first.Station == SchedPoints.Post) err.Add(n + "на посту путей нет — формирование и оборот только на станции");
                    if (first.DepSec == null) err.Add(n + "у первого пункта нужно время отправления");
                    if (first.Track.Trim().Length == 0) err.Add(n + "у первого пункта нужен путь");
                    if (t.Origin == SchedPoints.Turn)
                    {
                        var p = Find(t.TurnFrom);
                        if (p == null) err.Add(n + $"оборот: поезда №{t.TurnFrom} нет в расписании");
                        else if (p.Ordered.LastOrDefault() is not { } pl || pl.Station != first.Station || pl.DepSec != null)
                            err.Add(n + $"оборот: поезд №{t.TurnFrom} должен заканчиваться на {SchedPoints.Name(first.Station)} (без отправления)");
                        else if (pl.ArrSec is double pa && first.DepSec is double fd && fd < pa)
                            err.Add(n + $"оборот: отправление раньше прибытия №{t.TurnFrom}");
                    }
                    break;
                default:
                    err.Add(n + "не указано, откуда поезд");
                    break;
            }
            if (t.Dir == Dir.Right && last.Station == SchedPoints.Akzht && last.DepSec != null)
                err.Add(n + "после АКЖТ чётный поезд идёт на пост — укажите время проследования поста");
            if (t.Dir == Dir.Left && last.Station == SchedPoints.Post)
                err.Add(n + "после поста нечётный поезд идёт на АКЖТ — укажите время на АКЖТ");
            if (t.Dir == Dir.Right && last.Station == SchedPoints.Algabas && last.DepSec != null)
                err.Add(n + "Алгабас — тупиковая станция: чётный поезд на ней заканчивается (обратно — оборотом)");
            foreach (var s in st.Where(s => s.Station != SchedPoints.Post && s.ArrSec != null && s.Track.Trim().Length == 0))
                err.Add(n + $"{SchedPoints.Name(s.Station)}: не указан путь приёма");
        }
        return err;
    }
}

/// <summary>Событие поезда на станции — для ГИД (станция → преподаватель, s_train_event).</summary>
public class TrainEventRec
{
    public int Number { get; set; }
    /// <summary>Пункт: akzht / post / algabas.</summary>
    public string Station { get; set; } = "";
    /// <summary>arr — прибыл (остановился на пути), dep — отправился, pass — проследовал без остановки.</summary>
    public string Kind { get; set; } = "";
    public double Clock { get; set; }
    public string Track { get; set; } = "";
}

/// <summary>Учебные расписания «по умолчанию» (на 1 игровой час) — программа преподавателя создаёт их при первом запуске.</summary>
public static class DefaultSchedules
{
    static SchedStop A(string arr, string dep, string track) => new() { Station = SchedPoints.Akzht, Arr = arr, Dep = dep, Track = track };
    static SchedStop P(string time) => new() { Station = SchedPoints.Post, Arr = time, Dep = time };
    static SchedStop G(string arr, string dep, string track) => new() { Station = SchedPoints.Algabas, Arr = arr, Dep = dep, Track = track };

    static SchedTrain T(int n, string cat, string origin, params SchedStop[] stops) =>
        new() { Number = n, Category = cat, Origin = origin, Stops = stops.ToList() };

    const string TA = SchedPoints.FromTunnelAkzht, TP = SchedPoints.FromTunnelPost, F = SchedPoints.Formation, TU = SchedPoints.Turn;

    public static List<ScheduleData> Build() => new()
    {
        new()
        {
            Id = "uch1", Name = "Учебное №1 — основы (08:00–09:00)",
            Description = "6 поездов: грузовые и пригородные, приём и отправление, проследование без остановки, оборот на Алгабасе, " +
                          "смена направления однопутного перегона пост — Алгабас.",
            Trains =
            {
                T(2002, "грузовой", TA, A("08:05", "08:10", "4"), P("08:12"), G("08:14", "", "3")),
                T(6002, "пригородный", TA, A("08:16", "08:18", "2"), P("08:20")),
                new() { Number = 2001, Category = "грузовой", Origin = TU, TurnFrom = 2002,
                        Stops = { G("", "08:28", "3"), P("08:30"), A("08:32", "08:38", "3") } },
                T(6001, "пригородный", TP, P("08:40"), A("08:42", "08:44", "1")),
                T(2004, "грузовой", TA, A("08:48", "08:48", "2"), P("08:50")),
                T(3001, "хозяйственный", TP, P("08:52"), A("08:54", "", "5")),
            }
        },
        new()
        {
            Id = "uch2", Name = "Учебное №2 — интенсивное, скрещение на однопутке (08:00–09:00)",
            Description = "8 поездов, плотный график. На Алгабасе — скрещение: №2101 уходит только после прибытия №2102, " +
                          "направление перегона пост — Алгабас меняется несколько раз. Пассажирские стоят по расписанию.",
            Trains =
            {
                T(2102, "грузовой", TA, A("08:04", "08:08", "4"), P("08:10"), G("08:12", "", "3")),
                T(2101, "грузовой", F, G("", "08:15", "1"), P("08:17"), A("08:19", "08:19", "1")),
                T(102, "пассажирский", TA, A("08:14", "08:16", "2"), P("08:18"), G("08:21", "", "2")),
                T(6202, "пригородный", TA, A("08:24", "08:26", "2"), P("08:28")),
                new() { Number = 101, Category = "пассажирский", Origin = TU, TurnFrom = 102,
                        Stops = { G("", "08:33", "2"), P("08:35"), A("08:37", "08:39", "1") } },
                T(2103, "грузовой", TP, P("08:41"), A("08:43", "08:50", "4")),
                T(2104, "грузовой", TA, A("08:44", "08:44", "2"), P("08:46"), G("08:48", "", "4")),
                T(6201, "пригородный", TP, P("08:52"), A("08:54", "08:56", "1")),
            }
        },
        new()
        {
            Id = "uch3", Name = "Учебное №3 — АКЖТ и пост, без Алгабаса (08:00–09:00)",
            Description = "9 поездов из тоннелей через АКЖТ и пост (Алгабас не нужен): обгон, проследование, " +
                          "поезда своего формирования.",
            Trains =
            {
                T(2202, "грузовой", TA, A("08:04", "08:09", "3"), P("08:11")),
                T(6302, "пригородный", TA, A("08:12", "08:14", "2"), P("08:16")),
                T(2201, "грузовой", TP, P("08:15"), A("08:17", "08:24", "4")),
                T(6301, "пригородный", TP, P("08:22"), A("08:24", "08:26", "1")),
                T(2204, "грузовой", TA, A("08:30", "08:30", "2"), P("08:32")),
                T(2203, "грузовой", TP, P("08:36"), A("08:38", "08:38", "1")),
                T(6304, "пригородный", TA, A("08:42", "08:44", "2"), P("08:46")),
                T(6303, "пригородный", TP, P("08:48"), A("08:50", "08:52", "1")),
                T(3202, "хозяйственный", F, A("", "08:55", "5"), P("08:57")),
            }
        },
    };
}
