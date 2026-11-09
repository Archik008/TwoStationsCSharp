using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;
using StationApp.Core;

namespace TeacherApp;

// =====================================================================
//  ГИД — график исполненного движения  [ДОБАВЛЕНО 22.10.2026, этап 5]
//   • GidRecord — одно занятие: расписание урока (нормативный график) и события поездов со всех станций
//     (s_train_event: прибытие, отправление, проследование). Файл данные\gid\<gameId>.json.
//   • GidPainter — рисует график: ось времени (игровое) × пункты АКЖТ — пост — Алгабас (по длинам перегонов).
//     Нормативный — пунктиром, исполненный — сплошной; цвет по категории поезда.
//   • GidView — график в окне: колесо — масштаб, перетаскивание и стрелки — прокрутка, +/− — масштаб,
//     Home — весь урок, наведение на нитку — номер, категория, план и факт.
//   • GidStats — показатели работы: участковая и техническая скорость, выполнение графика.
//   • GidReport — раздел подробного отчёта (картинка PNG и таблицы).
// =====================================================================

public class GidRecord
{
    public string GameId { get; set; } = "";
    public string LessonName { get; set; } = "";
    public DateTime Started { get; set; }
    public double ClockStart { get; set; }
    public ScheduleData Schedule { get; set; }
    public List<TrainEventRec> Events { get; set; } = new();
}

public enum GidMode { Normative, Actual, Both }

public static class Gid
{
    static string GidDir => Path.Combine(Db.Dir, "gid");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>Текущее (или последнее) занятие.</summary>
    public static GidRecord Current { get; private set; }
    public static event Action Changed;

    public static void Begin(string gameId, Lesson lesson, double clock)
    {
        Current = new GidRecord
        {
            GameId = gameId, LessonName = lesson.Name, Started = DateTime.Now, ClockStart = clock,
            Schedule = Db.FindSchedule(lesson.ScheduleId)
        };
        Save();
        Changed?.Invoke();
    }

    public static void Add(JsonElement d)
    {
        if (Current == null || !d.TryGetProperty("ev", out var e)) return;
        var ev = e.Deserialize<TrainEventRec>(Db.Wire);
        if (ev == null) return;
        Current.Events.Add(ev);
        Save();
        Changed?.Invoke();
    }

    public static void Save()
    {
        if (Current == null) return;
        try
        {
            Directory.CreateDirectory(GidDir);
            File.WriteAllText(Path.Combine(GidDir, Current.GameId + ".json"), JsonSerializer.Serialize(Current, Opt));
        }
        catch { }
    }

    public static GidRecord Load(string gameId)
    {
        if (Current?.GameId == gameId) return Current;
        var p = Path.Combine(GidDir, gameId + ".json");
        try { return File.Exists(p) ? JsonSerializer.Deserialize<GidRecord>(File.ReadAllText(p), Opt) : null; }
        catch { return null; }
    }

    /// <summary>Сохранённые занятия (новые сверху).</summary>
    public static List<GidRecord> All()
    {
        var list = new List<GidRecord>();
        if (!Directory.Exists(GidDir)) return list;
        foreach (var f in Directory.GetFiles(GidDir, "*.json"))
            try { if (JsonSerializer.Deserialize<GidRecord>(File.ReadAllText(f), Opt) is { } r) list.Add(r); } catch { }
        return list.OrderByDescending(r => r.Started).ToList();
    }

    public static double Km(string station) => station switch
    {
        SchedPoints.Akzht => 0,
        SchedPoints.Post => Db.Settings.LenAkzhtPost,
        SchedPoints.Algabas => Db.Settings.LenAkzhtPost + Db.Settings.LenPostAlgabas,
        _ => 0
    };

    public static double TotalKm => Math.Max(0.1, Db.Settings.LenAkzhtPost + Db.Settings.LenPostAlgabas);

    public static Color CatColor(string cat) => cat switch
    {
        "пассажирский" => Color.FromArgb(200, 0, 0),
        "пригородный" => Color.FromArgb(225, 95, 0),
        "грузовой" => Color.FromArgb(0, 60, 170),
        "хозяйственный" => Color.FromArgb(0, 130, 60),
        "локомотив" => Color.FromArgb(110, 110, 110),
        _ => Color.FromArgb(40, 40, 40)
    };

    /// <summary>Нормативная нитка: (время, пункт) по ходу поезда.</summary>
    public static List<(double t, string st)> PlanPoints(SchedTrain t)
    {
        var pts = new List<(double, string)>();
        foreach (var s in t.Ordered)
        {
            if (s.ArrSec is double a) pts.Add((a, s.Station));
            if (s.DepSec is double d && !s.Pass) pts.Add((d, s.Station));
        }
        return pts;
    }

    /// <summary>Исполненные нитки: номер → (время, пункт) по порядку.</summary>
    public static Dictionary<int, List<(double t, string st)>> FactPoints(GidRecord r) =>
        r.Events.GroupBy(e => e.Number).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Clock).Select(e => (e.Clock, e.Station)).ToList());

    /// <summary>Текст для подсказки: план и факт по пунктам.</summary>
    public static string TrainInfo(GidRecord r, int number)
    {
        var t = r.Schedule?.Find(number);
        var sb = new StringBuilder($"Поезд №{number}" + (t != null ? $" — {t.Category}, {(t.Dir == Dir.Right ? "чётный" : "нечётный")}" : " — нет в расписании"));
        if (t != null && t.Note.Length > 0) sb.Append($" ({t.Note})");
        var stations = t?.Ordered.Select(s => s.Station).ToList() ?? new();
        foreach (var st in r.Events.Where(e => e.Number == number).Select(e => e.Station)) if (!stations.Contains(st)) stations.Add(st);
        foreach (var st in stations)
        {
            var s = t?.Stop(st);
            var ev = r.Events.Where(e => e.Number == number && e.Station == st).ToList();
            var arr = ev.FirstOrDefault(e => e.Kind is "arr" or "pass");
            var dep = ev.FirstOrDefault(e => e.Kind is "dep" or "pass");
            string Part(string what, string plan, double? pl, TrainEventRec fact)
            {
                if (plan.Length == 0 && fact == null) return "";
                string f = fact != null ? SchedTime.FmtSec(fact.Clock) + (pl is double p ? $" ({SchedTime.Dev(fact.Clock, p)})" : "") : "—";
                return $"  {what} {(plan.Length > 0 ? plan : "—")} → {f}";
            }
            sb.Append($"\n{SchedPoints.Name(st)}:");
            if (s?.Pass == true || ev.Any(e => e.Kind == "pass")) sb.Append(Part("проследование", s?.Dep ?? "", s?.DepSec, arr));
            else sb.Append(Part("приб.", s?.Arr ?? "", s?.ArrSec, arr) + Part("отпр.", s?.Dep ?? "", s?.DepSec, dep));
            var trk = ev.LastOrDefault(e => e.Track.Length > 0)?.Track;
            if (trk != null || s?.Track.Length > 0) sb.Append($"  путь {(s?.Track.Length > 0 ? s.Track : "—")} → {trk ?? "—"}");
        }
        return sb.ToString();
    }

    /// <summary>Время, которое покрывает график: от начала урока до последнего поезда (+5 мин).</summary>
    public static (double t0, double t1) Range(GidRecord r)
    {
        var times = new List<double>();
        if (r.Schedule != null) times.AddRange(r.Schedule.Trains.SelectMany(PlanPoints).Select(p => p.t));
        times.AddRange(r.Events.Select(e => e.Clock));
        double t0 = r.ClockStart > 0 ? r.ClockStart : times.DefaultIfEmpty(8 * 3600).Min();
        if (times.Count > 0) t0 = Math.Min(t0, times.Min());
        double t1 = Math.Max(t0 + 1800, times.DefaultIfEmpty(t0).Max() + 300);
        return (Math.Floor(t0 / 600) * 600, Math.Ceiling(t1 / 600) * 600);
    }
}

// ---------------------------------------------------------------------
//  Рисование графика (окно и картинка в отчёт)
// ---------------------------------------------------------------------
public class GidPainter
{
    public GidRecord Rec;
    public GidMode Mode = GidMode.Both;
    public double T0, T1;
    public int Highlight;
    public Rectangle Plot;

    const int Left = 130, Right = 20, Top = 30, Bottom = 44, Inset = 14;
    static readonly Font FSt = new("Segoe UI", 9.5f, FontStyle.Bold), FTime = new("Segoe UI", 8f), FNum = new("Segoe UI", 8.5f, FontStyle.Bold);

    public float X(double t) => Plot.Left + (float)((t - T0) / Math.Max(1, T1 - T0) * Plot.Width);
    public double T(float x) => T0 + (x - Plot.Left) / Math.Max(1f, Plot.Width) * (T1 - T0);
    public float Y(string st) => Plot.Top + Inset + (float)(Gid.Km(st) / Gid.TotalKm * (Plot.Height - 2 * Inset));

    public void Draw(Graphics g, Rectangle area)
    {
        Plot = Rectangle.FromLTRB(area.Left + Left, area.Top + Top, area.Right - Right, area.Bottom - Bottom);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillRectangle(Brushes.White, area);
        if (Plot.Width < 50 || Plot.Height < 30) return;

        // сетка времени
        double pxPerMin = Plot.Width / Math.Max(1, (T1 - T0) / 60);
        int step = pxPerMin >= 6 ? 1 : pxPerMin >= 2 ? 5 : 10;
        using var penMin = new Pen(Color.FromArgb(236, 236, 236));
        using var pen10 = new Pen(Color.FromArgb(190, 200, 210));
        using var penHour = new Pen(Color.FromArgb(120, 130, 140), 1.5f);
        double first = Math.Ceiling(T0 / 60 / step) * step * 60;
        int labelEvery = pxPerMin >= 12 ? 5 : pxPerMin >= 3 ? 10 : 30;
        for (double t = first; t <= T1; t += step * 60)
        {
            int min = (int)Math.Round(t / 60);
            float x = X(t);
            var pen = min % 60 == 0 ? penHour : min % 10 == 0 ? pen10 : penMin;
            g.DrawLine(pen, x, Plot.Top, x, Plot.Bottom);
            if (min % labelEvery == 0)
            {
                string s = SchedTime.Fmt(t);
                var sz = g.MeasureString(s, FTime);
                g.DrawString(s, FTime, Brushes.DimGray, x - sz.Width / 2, Plot.Top - sz.Height - 2);
                g.DrawString(s, FTime, Brushes.DimGray, x - sz.Width / 2, Plot.Bottom + 2);
            }
        }
        // пункты
        foreach (var st in SchedPoints.Order)
        {
            float y = Y(st);
            g.DrawLine(Pens.Gray, Plot.Left, y, Plot.Right, y);
            string name = L.T(SchedPoints.Name(st));   // [ДОБАВЛЕНО 22.10.2026, этап 8]
            var sz = g.MeasureString(name, FSt);
            g.DrawString(name, FSt, Brushes.Black, area.Left + 6, y - sz.Height / 2);
        }
        g.DrawRectangle(Pens.Gray, Plot);
        // легенда: цвет — категория, пунктир — по расписанию
        float lx = Plot.Left;
        foreach (var cat in SchedPoints.Categories.Append("нет в расписании"))
        {
            using var lp = new Pen(Gid.CatColor(cat), 2.2f);
            float ly = Plot.Bottom + 30;
            g.DrawLine(lp, lx, ly, lx + 22, ly);
            g.DrawString(L.T(cat), FTime, Brushes.Black, lx + 26, ly - 7);
            lx += 34 + g.MeasureString(L.T(cat), FTime).Width;
        }
        using (var dp = new Pen(Color.Gray, 1.4f) { DashStyle = DashStyle.Dash })
        {
            g.DrawLine(dp, lx + 10, Plot.Bottom + 30, lx + 32, Plot.Bottom + 30);
            g.DrawString(L.T("пунктир — нормативный график, сплошная — исполненный"), FTime, Brushes.Black, lx + 36, Plot.Bottom + 23);
        }

        var clip = g.Clip;
        g.SetClip(Plot);
        if (Mode != GidMode.Actual && Rec?.Schedule != null)
            foreach (var t in Rec.Schedule.Trains)
            {
                var pts = Gid.PlanPoints(t);
                var c = Gid.CatColor(t.Category);
                using var pen = new Pen(Mode == GidMode.Both ? Color.FromArgb(150, c) : c, t.Number == Highlight ? 2.5f : 1.4f) { DashStyle = DashStyle.Dash };
                DrawThread(g, pen, pts, Mode == GidMode.Normative ? t.Number : 0, c);
            }
        if (Mode != GidMode.Normative && Rec != null)
            foreach (var (n, pts) in Gid.FactPoints(Rec))
            {
                var c = Gid.CatColor(Rec.Schedule?.Find(n)?.Category);
                using var pen = new Pen(c, n == Highlight ? 3.5f : 2.2f);
                DrawThread(g, pen, pts, n, c);
            }
        g.Clip = clip;
    }

    void DrawThread(Graphics g, Pen pen, List<(double t, string st)> pts, int label, Color c)
    {
        if (pts.Count == 0) return;
        var p = pts.Select(x => new PointF(X(x.t), Y(x.st))).ToArray();
        if (p.Length == 1) g.FillEllipse(new SolidBrush(c), p[0].X - 3, p[0].Y - 3, 6, 6);
        else g.DrawLines(pen, p);
        if (label == 0) return;
        // номер — над первым наклонным отрезком (или у первой точки)
        int i = Enumerable.Range(0, p.Length - 1).FirstOrDefault(k => Math.Abs(p[k + 1].Y - p[k].Y) > 1, 0);
        var a = p[i];
        var b = p.Length > 1 ? p[i + 1] : p[i];
        var mid = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        using var br = new SolidBrush(c);
        g.DrawString(label.ToString(), FNum, br, mid.X + 3, mid.Y - 14);
    }

    /// <summary>Номер поезда, чья нитка рядом с точкой (0 — нет).</summary>
    public int HitTest(Point m)
    {
        double best = 7;
        int found = 0;
        void Test(int n, List<(double t, string st)> pts)
        {
            var p = pts.Select(x => new PointF(X(x.t), Y(x.st))).ToList();
            for (int i = 0; i < p.Count; i++)
            {
                double d = i + 1 < p.Count ? SegDist(m, p[i], p[i + 1]) : Math.Sqrt(Math.Pow(m.X - p[i].X, 2) + Math.Pow(m.Y - p[i].Y, 2));
                if (d < best) { best = d; found = n; }
            }
        }
        if (Rec == null) return 0;
        if (Mode != GidMode.Normative) foreach (var (n, pts) in Gid.FactPoints(Rec)) Test(n, pts);
        if (Mode != GidMode.Actual && Rec.Schedule != null) foreach (var t in Rec.Schedule.Trains) Test(t.Number, Gid.PlanPoints(t));
        return found;
    }

    static double SegDist(Point m, PointF a, PointF b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l = dx * dx + dy * dy;
        double k = l == 0 ? 0 : Math.Clamp(((m.X - a.X) * dx + (m.Y - a.Y) * dy) / l, 0, 1);
        double x = a.X + k * dx - m.X, y = a.Y + k * dy - m.Y;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>Картинка всего графика (для отчёта), PNG в base64.</summary>
    public static string Png(GidRecord rec, int w = 1400, int h = 400)
    {
        var (t0, t1) = Gid.Range(rec);
        var p = new GidPainter { Rec = rec, T0 = t0, T1 = t1, Mode = GidMode.Both };
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) p.Draw(g, new Rectangle(0, 0, w, h));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }
}

// ---------------------------------------------------------------------
//  График в окне
// ---------------------------------------------------------------------
public class GidView : Control
{
    public readonly GidPainter P = new();
    readonly ToolTip tip = new() { InitialDelay = 200, ReshowDelay = 100, AutoPopDelay = 30000 };
    Point? drag;
    double dragT0, dragT1;
    int lastHit;
    string fitKey = "";

    public GidView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
    }

    public GidRecord Record
    {
        get => P.Rec;
        set
        {
            P.Rec = value;
            string key = (value?.GameId ?? "") + "/" + (value?.Schedule?.Id ?? "");
            if (key != fitKey) { fitKey = key; Fit(); }
            Invalidate();
        }
    }

    public GidMode Mode { get => P.Mode; set { P.Mode = value; Invalidate(); } }

    public void Fit()
    {
        if (P.Rec == null) { P.T0 = 8 * 3600; P.T1 = 9 * 3600; }
        else (P.T0, P.T1) = Gid.Range(P.Rec);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        P.Draw(e.Graphics, ClientRectangle);
        if (P.Rec == null)
            e.Graphics.DrawString(L.T("Нет данных: начните урок с расписанием или выберите прошлое занятие"), Font, Brushes.Gray, 140, 40);
    }

    void Zoom(double factor, float atX)
    {
        double at = P.T(atX);
        double span = Math.Clamp((P.T1 - P.T0) * factor, 300, 24 * 3600);
        double k = (at - P.T0) / Math.Max(1, P.T1 - P.T0);
        P.T0 = at - span * k;
        P.T1 = P.T0 + span;
        Invalidate();
    }

    void Pan(double frac)
    {
        double d = (P.T1 - P.T0) * frac;
        P.T0 += d;
        P.T1 += d;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Zoom(e.Delta > 0 ? 0.8 : 1.25, e.X);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left) { drag = e.Location; dragT0 = P.T0; dragT1 = P.T1; Cursor = Cursors.SizeWE; }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        drag = null;
        Cursor = Cursors.Default;
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag is Point d)
        {
            double dt = (e.X - d.X) / Math.Max(1f, P.Plot.Width) * (dragT1 - dragT0);
            P.T0 = dragT0 - dt;
            P.T1 = dragT1 - dt;
            Invalidate();
            return;
        }
        int hit = P.HitTest(e.Location);
        if (hit != lastHit)
        {
            lastHit = hit;
            P.Highlight = hit;
            Invalidate();
            if (hit == 0) tip.Hide(this);
            else tip.Show(string.Join("\n", Gid.TrainInfo(P.Rec, hit).Split('\n').Select(L.T)), this, e.X + 16, e.Y + 16);   // [ДОБАВЛЕНО 22.10.2026, этап 8]
        }
        base.OnMouseMove(e);
    }

    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        float mid = P.Plot.Left + P.Plot.Width / 2f;
        switch (e.KeyCode)
        {
            case Keys.Left: Pan(-0.1); break;
            case Keys.Right: Pan(0.1); break;
            case Keys.Add: case Keys.Oemplus: case Keys.Up: Zoom(0.8, mid); break;
            case Keys.Subtract: case Keys.OemMinus: case Keys.Down: Zoom(1.25, mid); break;
            case Keys.Home: Fit(); break;
        }
        base.OnKeyDown(e);
    }
}

// ---------------------------------------------------------------------
//  Показатели работы (как в тренажёре: участковая и техническая скорость, выполнение графика)
// ---------------------------------------------------------------------
public static class GidStats
{
    public record TrainRow(int Number, string Category, string Route, double Km, double TotalMin, double StopMin,
                           double? Tech, double? Section, double? PlanSection, string OnTime);

    /// <summary>Путь поезда по точкам (пункт, прибытие, отправление) — по факту или по плану.</summary>
    static List<(string st, double arr, double dep)> Visits(List<(double t, string st)> pts)
    {
        var v = new List<(string st, double arr, double dep)>();
        foreach (var (t, st) in pts)
        {
            if (v.Count > 0 && v[^1].st == st) v[^1] = (st, v[^1].arr, t);
            else v.Add((st, t, t));
        }
        return v;
    }

    static (double km, double total, double stops)? Measure(List<(double t, string st)> pts)
    {
        var v = Visits(pts);
        if (v.Count < 2) return null;
        double km = Math.Abs(Gid.Km(v[^1].st) - Gid.Km(v[0].st));
        double total = v[^1].arr - v[0].dep;
        double stops = v.Skip(1).Take(v.Count - 2).Sum(x => x.dep - x.arr);
        if (km <= 0 || total <= 0) return null;
        return (km, total, stops);
    }

    public static List<TrainRow> Rows(GidRecord r)
    {
        var rows = new List<TrainRow>();
        foreach (var (n, pts) in Gid.FactPoints(r).OrderBy(x => x.Value.First().t))
        {
            var t = r.Schedule?.Find(n);
            var m = Measure(pts);
            double? plan = t != null && Measure(Gid.PlanPoints(t)) is { } pm ? pm.km / (pm.total / 3600) : null;
            string route = string.Join(" — ", Visits(pts).Select(x => SchedPoints.Name(x.st)));
            rows.Add(new TrainRow(n, t?.Category ?? "—", route, m?.km ?? 0, (m?.total ?? 0) / 60, (m?.stops ?? 0) / 60,
                m is { } a && a.total - a.stops > 0 ? a.km / ((a.total - a.stops) / 3600) : null,
                m is { } b ? b.km / (b.total / 3600) : null, plan, OnTime(r, n)));
        }
        return rows;
    }

    /// <summary>Отправления по графику: «2 из 3, макс. +7 мин».</summary>
    static string OnTime(GidRecord r, int n)
    {
        var t = r.Schedule?.Find(n);
        if (t == null) return "нет в расписании";
        var devs = new List<double>();
        foreach (var s in t.Ordered.Where(s => s.DepSec != null))
        {
            var e = r.Events.FirstOrDefault(e => e.Number == n && e.Station == s.Station && e.Kind is "dep" or "pass");
            if (e != null) devs.Add((e.Clock - s.DepSec.Value) / 60);
        }
        if (devs.Count == 0) return "—";
        return $"{devs.Count(d => d <= 5)} из {devs.Count}, макс. {SchedTime.Dev(devs.Max() * 60, 0)} мин";
    }

    /// <summary>Итог по участку: поездо-км / поездо-часы.</summary>
    public static (double? tech, double? section) Totals(List<TrainRow> rows)
    {
        var ok = rows.Where(x => x.Km > 0 && x.TotalMin > 0).ToList();
        if (ok.Count == 0) return (null, null);
        double km = ok.Sum(x => x.Km), h = ok.Sum(x => x.TotalMin) / 60, run = ok.Sum(x => x.TotalMin - x.StopMin) / 60;
        return (run > 0 ? km / run : null, km / h);
    }

    public static string Kmh(double? v) => v is double x ? x.ToString("0.0") : "—";
}

// ---------------------------------------------------------------------
//  Раздел подробного отчёта
// ---------------------------------------------------------------------
public static class GidReport
{
    static string H(string s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>Пункты программы станции: АКЖТ — АКЖТ и пост, Алгабас — Алгабас.</summary>
    static string[] PointsOf(string station) => station switch
    {
        "akzht" => new[] { SchedPoints.Akzht, SchedPoints.Post },
        "algabas" => new[] { SchedPoints.Algabas },
        _ => SchedPoints.Order
    };

    public static string Section(GameResult g)
    {
        var rec = Gid.Load(g.GameId);
        if (rec == null && g.Trains.Count == 0) return "";
        rec ??= new GidRecord { GameId = g.GameId, ClockStart = g.ClockStart, Schedule = Db.Schedules.FirstOrDefault(s => s.Name == g.ScheduleName) };
        if (rec.Events.Count == 0 && g.Trains.Count > 0) rec.Events = g.Trains;
        var sb = new StringBuilder();
        sb.Append($"<h2>ГИД — график исполненного движения{(rec.Schedule != null ? " (" + H(rec.Schedule.Name) + ")" : "")}</h2>");
        sb.Append("<p>Сплошная линия — исполненный график (все станции занятия), пунктир — нормативный (расписание).</p>");
        sb.Append($"<img style='max-width:100%;border:1px solid #999' src='data:image/png;base64,{GidPainter.Png(rec)}'>");

        // выполнение расписания станцией этого студента
        var mine = PointsOf(g.Station);
        if (rec.Schedule != null)
        {
            sb.Append("<h2>Выполнение расписания станцией</h2><table><tr><th>№</th><th>Категория</th><th>Пункт</th><th>Приб. план / факт</th>" +
                      "<th>Отпр. план / факт</th><th>Откл., мин</th><th>Путь план / факт</th></tr>");
            foreach (var (t, s) in rec.Schedule.Trains.SelectMany(t => t.Ordered.Where(s => mine.Contains(s.Station)).Select(s => (t, s)))
                                       .OrderBy(x => x.s.FirstSec))
            {
                var ev = rec.Events.Where(e => e.Number == t.Number && e.Station == s.Station).ToList();
                var arr = ev.FirstOrDefault(e => e.Kind is "arr" or "pass");
                var dep = ev.FirstOrDefault(e => e.Kind is "dep" or "pass");
                string F(TrainEventRec e) => e != null ? SchedTime.FmtSec(e.Clock) : "—";
                string dev = dep != null && s.DepSec is double pd ? SchedTime.Dev(dep.Clock, pd) : arr != null && s.ArrSec is double pa ? SchedTime.Dev(arr.Clock, pa) : "не было";
                bool late = dev.StartsWith("+") && int.TryParse(dev[1..], out var m) && m > 5;
                sb.Append($"<tr><td>{t.Number}</td><td>{H(t.Category)}</td><td>{H(SchedPoints.Name(s.Station))}</td>" +
                          $"<td>{H(s.Arr.Length > 0 ? s.Arr : "—")} / {F(arr)}</td><td>{H(s.Dep.Length > 0 ? s.Dep : "—")} / {F(dep)}</td>" +
                          $"<td{(late ? " class=crit" : "")}>{H(dev)}</td><td>{H(s.Track.Length > 0 ? s.Track : "—")} / {H(ev.LastOrDefault(e => e.Track.Length > 0)?.Track ?? "—")}</td></tr>");
            }
            sb.Append("</table>");
        }

        var rows = GidStats.Rows(rec);
        if (rows.Count > 0)
        {
            var (tech, sect) = GidStats.Totals(rows);
            sb.Append($"<h2>Показатели работы участка</h2><p>Длины перегонов (условно): АКЖТ — пост {Db.Settings.LenAkzhtPost:0.#} км, " +
                      $"пост — Алгабас {Db.Settings.LenPostAlgabas:0.#} км. Время — игровое. Техническая скорость — без стоянок, " +
                      $"участковая — со стоянками на промежуточных пунктах. Итого: техническая <b>{GidStats.Kmh(tech)}</b> км/ч, " +
                      $"участковая <b>{GidStats.Kmh(sect)}</b> км/ч.</p>");
            sb.Append("<table><tr><th>№</th><th>Категория</th><th>Маршрут</th><th>км</th><th>В пути, мин</th><th>Стоянки, мин</th>" +
                      "<th>Техническая, км/ч</th><th>Участковая, км/ч</th><th>Участковая по графику</th><th>Отправления по графику (±5 мин)</th></tr>");
            foreach (var r in rows)
                sb.Append($"<tr><td>{r.Number}</td><td>{H(r.Category)}</td><td>{H(r.Route)}</td><td>{r.Km:0.#}</td><td>{r.TotalMin:0.0}</td>" +
                          $"<td>{r.StopMin:0.0}</td><td>{GidStats.Kmh(r.Tech)}</td><td>{GidStats.Kmh(r.Section)}</td><td>{GidStats.Kmh(r.PlanSection)}</td><td>{H(r.OnTime)}</td></tr>");
            sb.Append("</table>");
        }
        return sb.ToString();
    }
}
