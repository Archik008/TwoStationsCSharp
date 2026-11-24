using System.Drawing.Drawing2D;
using StationApp.Core;
using StationApp.Yard;

using Aspect = StationApp.Yard.Aspect;
using Wagon = StationApp.Yard.Wagon;

namespace StationApp.Ui;

// =====================================================================
//  СХЕМА СТАНЦИИ И ВИД ГОРКИ СБОКУ  [ДОБАВЛЕНО 12.11.2026]
//  Вверху — схема (вид сверху, как на пульте): парк приёма, путь надвига, горка, стрелки 1–7, замедлители,
//  сортировочный парк 1–8, парк отправления, депо. Занятые рельсовые цепи горки — красные.
//  Внизу — профиль горки «вид сбоку»: видно, как отцепы переваливают горб и катятся вниз.
//  Мышь: щелчок по стрелке — перевести; по сортировочному пути — маршрут отцепу (кнопка пути);
//  по отцепу — выбрать его в листке; правая кнопка по пути — «Сжать путь», «Вывести состав».
// =====================================================================

public sealed class YardView : Control
{
    public YardSim Sim;
    /// <summary>На что показывает мышь (для подсказки и ИИ-помощника): «стрелка 3», «1ТП», «путь 5»…</summary>
    public event Action<string> Pointed;
    public event Action<int, Point> TrackMenu;
    public event Action Clicked;

    float scale = 1, ox, oy;
    PointF mouse = new(-100, -100);
    string hover;

    static readonly Color Bg = Color.FromArgb(27, 31, 36), Free = Color.FromArgb(126, 134, 145), Busy = Color.FromArgb(229, 72, 59),
        Dim = Color.FromArgb(58, 64, 72), Txt = Color.FromArgb(200, 205, 211), TxtDim = Color.FromArgb(130, 137, 146),
        Lunar = Color.FromArgb(235, 238, 255), Blue = Color.FromArgb(40, 90, 230);
    static readonly Font F13 = new("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel), F12 = new("Segoe UI", 12f, GraphicsUnit.Pixel),
        F11 = new("Segoe UI", 11f, GraphicsUnit.Pixel), F15 = new("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);

    public YardView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Bg;
    }

    PointF ToV(Point p) => new((p.X - ox) / scale, (p.Y - oy) / scale);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Bg);
        if (Sim == null) return;
        Lay.Fit(Height / (float)Math.Max(1, Width));
        scale = Math.Min(Width / Lay.W, Height / Lay.Hv);
        ox = (Width - Lay.W * scale) / 2;
        oy = (Height - Lay.Hv * scale) / 2;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.TranslateTransform(ox, oy);
        g.ScaleTransform(scale, scale);
        // [ИЗМЕНЕНО 24.11.2026] у горки из файла редактора парки, светофоры и подписи — по её схеме (G.*)
        if (Lay.Cur.Builtin) DrawStatic(g); else DrawStaticGeo(g);
        DrawHump(g);
        if (Lay.Cur.Builtin) DrawSignals(g); else DrawSignalsGeo(g);
        DrawVehicles(g);
        if (Lay.Cur.Builtin) DrawLabels(g); else DrawLabelsGeo(g);
        DrawProfile(g);
        DrawHover(g);
    }

    // =================================================================
    //  СХЕМА
    // =================================================================
    static void Poly(Graphics g, Color c, float w, params PointF[] p)
    {
        using var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, p);
    }

    void Label(Graphics g, string ru, Font f, Color c, float x, float y, bool center = false)
    {
        var s = L.T(ru);
        if (center) { var sz = g.MeasureString(s, f); x -= sz.Width / 2; }
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, x, y);
    }

    void DrawStatic(Graphics g)
    {
        // подписи парков
        Label(g, "Парк приёма", F15, TxtDim, 335, 84, true);
        Label(g, "Горка", F15, TxtDim, 700, 146, true);
        Label(g, "Сортировочный парк", F15, TxtDim, 1200, 8, true);
        Label(g, "Парк отправления", F15, TxtDim, 1510, 70, true);
        // приём
        Poly(g, Free, 3, new(0, 190), new(60, 190));
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            float y = Lay.ArrY(a);
            Poly(g, Free, 3, new(60, 190), new(120, y), new(150, y));
            bool busy = Sim.Arr[a].Train != null || Sim.Arr[a].State == ArrState.Arriving;
            Poly(g, busy ? Color.FromArgb(150, Busy) : Free, 4, new(150, y), new(520, y));
            bool route = Sim.Pushing != null && Sim.Pushing.Arr == a;
            Poly(g, route ? Lunar : Free, route ? 4 : 3, new(520, y), new(548, y), new(585, 190));
        }
        // путь надвига
        bool pushBusy = Sim.Pushing != null && Sim.Pushing.Head > Lay.SPushTrack - 5;
        Poly(g, pushBusy ? Busy : Sim.Pushing != null ? Lunar : Free, 4, new(585, 190), new(700, 190));
        // обходной путь, депо, стоянка горочного локомотива
        Poly(g, Dim, 3, new(108, 296), new(580, 296), new(612, 236), Lay.HumpLocoPark);
        Poly(g, Dim, 3, new(600, 190), new(612, 236));
        for (int a = 1; a <= Lay.ArrTracks; a++) Poly(g, Dim, 2, new(108, 296), new(118, Lay.ArrY(a)), new(132, Lay.ArrY(a)));
        Poly(g, Dim, 3, new(490, 296), Lay.DepotGate);
        Poly(g, Dim, 3, Lay.DepotGate, new(490, 404), new(1596, 404), new(1596, Lay.DepY(1)));
        using (var b = new SolidBrush(Color.FromArgb(40, 46, 54))) g.FillRectangle(b, 290, 344, 180, 40);
        using (var p = new Pen(Dim, 2)) g.DrawRectangle(p, 290, 344, 180, 40);
        Label(g, "Депо (ТЧ)", F13, TxtDim, 380, 355, true);
        Label(g, "стоянка ТЭМ18", F11, TxtDim, 630, 242, true);
        Label(g, "обходной путь", F11, TxtDim, 340, 299, true);
        // выход сортировочных путей в парк отправления, сам парк
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            int d = Math.Clamp((int)Math.Round((Lay.TrackY(k) - 110) / 60f) + 1, 1, 4);
            Poly(g, Dim, 2, new(1400, Lay.TrackY(k)), new(1412, Lay.TrackY(k)), new(1430, Lay.DepY(d)));
        }
        for (int d = 1; d <= Lay.DepTracks; d++)
        {
            float y = Lay.DepY(d);
            Poly(g, Sim.Dep[d].State == DepState.Free ? Free : Color.FromArgb(150, Busy), 4, new(1430, y), new(1588, y));
            Poly(g, Dim, 3, new(1588, y), new(1600, y));
            Poly(g, Dim, 2, new(1596, y + 12), new(1588, y));
        }
    }

    void DrawHump(Graphics g)
    {
        bool blink = Environment.TickCount / 250 % 2 == 0;
        foreach (var e in Sim.Edges)
        {
            bool occ = Sim.Occupied(e);
            Color c = occ ? Busy : Free;
            float w = e.S0 >= Lay.STrack ? 4 : 5;
            if (e.S0 >= Lay.STrack) c = occ ? Color.FromArgb(160, 64, 56) : Free;
            if (e.Sw == null) { Poly(g, c, w, Lay.Line(e.S0, e.S1, e.Lo)); continue; }
            var sw = e.Sw;
            Poly(g, c, w, Lay.Line(e.S0, sw.S, e.Lo));
            var plus = Lay.Line(sw.S, e.S1, sw.Track(true));
            var minus = Lay.Line(sw.S, e.S1, sw.Track(false));
            if (sw.Throwing) { Poly(g, Dim, w, plus); Poly(g, Dim, w, minus); }
            else if (sw.Plus) { Poly(g, Dim, w, minus); Poly(g, c, w, plus); }
            else { Poly(g, Dim, w, plus); Poly(g, c, w, minus); }
        }
        // предельные столбики у начала сортировочных путей
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            var p = Lay.At(Lay.STrack + 4, k, 1);
            using var b = new SolidBrush(Color.FromArgb(230, 230, 230));
            g.FillRectangle(b, p.X - 1, p.Y - 7, 2, 5);
        }
        // замедлители
        foreach (var r in Sim.Retarders)
        {
            var pts = Lay.Line(r.S0, r.S1, r.Lo);
            Color c = r.Step == 0 ? Color.FromArgb(90, 98, 110) : Color.FromArgb(255, 200 - r.Step * 30, 40);
            using (var pen = new Pen(c, 11)) g.DrawLines(pen, pts);
            Poly(g, Color.FromArgb(30, 30, 30), 2, pts);
            var mid = pts[pts.Length / 2];
            float dy = Lay.Cur.Builtin && r.Lo == 5 ? 10 : -27;
            Label(g, $"{r.Name}: {(r.Step == 0 ? "О" : r.Step.ToString())}", F12, r.Step == 0 ? Txt : c, mid.X, mid.Y + dy, true);
        }
        // номера стрелок
        foreach (var sw in Sim.Switches.Skip(1))
        {
            var p = Lay.SwitchPoint(sw);
            bool hot = hover == "стрелка " + sw.No;
            using var b = new SolidBrush(sw.Throwing ? (blink ? Color.Gold : Color.FromArgb(120, 100, 20)) : hot ? Color.FromArgb(80, 110, 160) : Color.FromArgb(50, 56, 64));
            g.FillEllipse(b, p.X - 9, p.Y - 22, 18, 16);
            Label(g, sw.No.ToString(), F12, Color.White, p.X, p.Y - 22, true);
        }
        // горб
        var h = Lay.At(0, 1, 1);
        using (var b = new SolidBrush(Color.FromArgb(200, 200, 200))) g.FillPolygon(b, new PointF[] { new(h.X - 6, h.Y + 12), new(h.X + 6, h.Y + 12), new(h.X, h.Y + 5) });
        Label(g, "горб", F11, TxtDim, h.X, h.Y + 12, true);
    }

    void Lamp(Graphics g, float x, float y, float r, Color on, bool lit)
    {
        using var b = new SolidBrush(lit ? on : Color.FromArgb(45, 45, 48));
        g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r);
        if (lit) { using var glow = new Pen(Color.FromArgb(90, on), 3); g.DrawEllipse(glow, x - r - 2, y - r - 2, 2 * r + 4, 2 * r + 4); }
    }

    void HumpSignal(Graphics g, float x, float y, string name, float r = 5)
    {
        var a = Sim.Hump;
        using (var b = new SolidBrush(Color.FromArgb(20, 20, 22))) g.FillRectangle(b, x - r - 3, y - 4 * r * 2 - 6, 2 * r + 6, 4 * r * 2 + 6);
        Poly(g, Color.Gray, 2, new(x, y), new(x, y + 12));
        float ly = y - r - 3;
        Lamp(g, x, ly, r, Color.FromArgb(255, 60, 50), a is Aspect.Red or Aspect.RedN);
        Lamp(g, x, ly - 2 * r - 2, r, Color.FromArgb(255, 205, 40), a is Aspect.Yellow or Aspect.YellowGreen);
        Lamp(g, x, ly - 4 * r - 4, r, Color.FromArgb(60, 220, 90), a is Aspect.Green or Aspect.YellowGreen);
        // световой указатель «Н»
        var nb = new RectangleF(x - r - 3, ly - 6 * r - 9, 2 * r + 6, 2 * r + 2);
        using (var b = new SolidBrush(Color.FromArgb(20, 20, 22))) g.FillRectangle(b, nb);
        if (a == Aspect.RedN) Label(g, "Н", F11, Color.White, x, nb.Y - 2, true);
        Label(g, name, F12, Txt, x + r + 6, y - 8);
    }

    void DrawSignals(Graphics g)
    {
        var p = Lay.SignalPoint;
        HumpSignal(g, p.X, p.Y - 8, "Г", 6);
        HumpSignal(g, 470, 112, "ПГ", 4);       // повторитель горочного светофора над парком приёма
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            float y = Lay.ArrY(a);
            bool open = Sim.Pushing != null && Sim.Pushing.Arr == a && Sim.Arr[a].Train == Sim.Pushing;
            Lamp(g, 530, y - 9, 4, open ? Lunar : Blue, true);
            Label(g, "М" + a, F11, TxtDim, 536, y - 18);
        }
    }

    // ---- вагоны и локомотивы на схеме ----
    void Body(Graphics g, PointF a, PointF b, Color c, float w, bool sel = false)
    {
        // небольшой зазор между вагонами
        float dx = b.X - a.X, dy = b.Y - a.Y, l = MathF.Sqrt(dx * dx + dy * dy);
        if (l < 0.5f) return;
        float k = Math.Min(0.6f, l * 0.15f) / l;
        var a2 = new PointF(a.X + dx * k, a.Y + dy * k);
        var b2 = new PointF(b.X - dx * k, b.Y - dy * k);
        if (sel) { using var sp = new Pen(Color.Gold, w + 4); g.DrawLine(sp, a2, b2); }
        using var pen = new Pen(c, w) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawLine(pen, a2, b2);
    }

    // [ИЗМЕНЕНО 17.11.2026] вагон рисуется ломаной по рельсам (Lay.Path), а не прямой от головы к хвосту:
    // раньше на поворотах и на спусках/подъёмах схемы вагон срезал угол и «вылезал» за путь. Вагоны толще.
    public const float WagonW = 12, LocoW = 14;
    const double Gap = 0.7;   // зазор между вагонами, м

    static void BodyPath(Graphics g, PointF[] pts, Color c, float w, bool sel = false)
    {
        if (pts.Length < 2) return;
        if (sel) { using var sp = new Pen(Color.Gold, w + 4) { LineJoin = LineJoin.Round }; g.DrawLines(sp, pts); }
        using var pen = new Pen(c, w) { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);
    }

    void WagonOnScheme(Graphics g, Wagon w, double front, int track, int arr, bool sel)
    {
        var pts = Lay.Path(front - w.Length + Gap, front - Gap, track, arr);
        var c = w.Loaded ? w.Kind.Body : Blend(w.Kind.Body, Bg, 0.45f);
        BodyPath(g, pts, c, WagonW, sel);
        if (w.Mark != "") BodyPath(g, pts, Color.FromArgb(220, 30, 30), 4);
    }

    void LocoOnScheme(Graphics g, LocoKind k, double front, int track, int arr)
    {
        var pts = Lay.Path(front - k.Length + Gap, front - Gap, track, arr);
        BodyPath(g, pts, k.Body, LocoW);
        BodyPath(g, pts, k.Stripe, 3);
    }

    /// <summary>Ломаная «сцены» от d0 до d1 (точек схемы от начала) — тоже по рельсам.</summary>
    static PointF[] MoverPath(Mover m, double d0, double d1)
    {
        var list = new List<PointF> { m.At(d0, out _) };
        double acc = 0;
        for (int i = 1; i < m.Pts.Length; i++)
        {
            acc += Mover.Dist(m.Pts[i - 1], m.Pts[i]);
            if (acc > d0 && acc < d1) list.Add(m.Pts[i]);
        }
        list.Add(m.At(d1, out _));
        return list.ToArray();
    }

    static Color Blend(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    void DrawCutsOnScheme(Graphics g, Cut c, double head, int track, int arr)
    {
        double s = head;
        bool sel = Sim.SelectedCut == c;
        foreach (var w in c.Wagons) { WagonOnScheme(g, w, s, track, arr, sel); s -= w.Length; }
    }

    void DrawVehicles(Graphics g)
    {
        // составы в парке приёма
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            var t = Sim.Arr[a].Train;
            if (t == null || t == Sim.Pushing) continue;
            if (t.TrainLoco != null) LocoOnScheme(g, t.TrainLoco, t.Head + 1 + t.TrainLoco.Length, 1, a);
            foreach (var c in t.Cuts) DrawCutsOnScheme(g, c, c.Head, 1, a);
            if (t.HumpLoco) LocoOnScheme(g, Rolling.TEM18, t.LocoFront - 1, 1, a);
        }
        // надвигаемый состав
        if (Sim.Pushing is Train pt)
        {
            foreach (var c in pt.Cuts) DrawCutsOnScheme(g, c, c.Head, pt.Lo, pt.Arr);
            LocoOnScheme(g, Rolling.TEM18, pt.LocoFront - 0.5, pt.Lo, pt.Arr);
        }
        if (Sim.Loco == HumpLocoState.AtHorb) LocoOnScheme(g, Rolling.TEM18, Sim.LocoS, 1, Sim.LocoArr);
        // отцепы на горке и на путях
        foreach (var c in Sim.Cuts) if (c.Active) DrawCutsOnScheme(g, c, c.Head, c.Lo, 1);
        // парк отправления
        for (int d = 1; d <= Lay.DepTracks; d++)
        {
            var dt = Sim.Dep[d];
            if (dt.Wagons.Count == 0) continue;
            float y = Lay.DepY(d), x = Lay.Cur.DepX1 - 2;
            if (dt.State is DepState.BrakeTest or DepState.Ready && dt.Loco != null)
            {
                float l = (float)(dt.Loco.Length * Lay.SceneScale);
                Body(g, new(x - l, y), new(x, y), dt.Loco.Body, LocoW);
                Body(g, new(x - l, y), new(x, y), dt.Loco.Stripe, 3);
                x -= l;
            }
            foreach (var w in dt.Wagons)
            {
                float l = (float)(w.Length * Lay.SceneScale);
                if (x - l < Lay.Cur.DepX0 + 2) break;
                Body(g, new(x - l, y), new(x, y), w.Loaded ? w.Kind.Body : Blend(w.Kind.Body, Bg, 0.45f), WagonW);
                x -= l;
            }
        }
        // «сцены»: приём, депо, горочный локомотив, вывод составов, отправление
        foreach (var m in Sim.Movers)
        {
            double d = m.Pos;
            foreach (var (len, loco, wagon) in m.Vehicles)
            {
                if (d <= 0) break;
                var pts = MoverPath(m, Math.Max(0, d - len + 0.6), d - 0.6);
                if (pts[0].X <= Lay.W || pts[^1].X <= Lay.W)
                {
                    if (loco != null) { BodyPath(g, pts, loco.Body, LocoW); BodyPath(g, pts, loco.Stripe, 3); }
                    else BodyPath(g, pts, wagon.Loaded ? wagon.Kind.Body : Blend(wagon.Kind.Body, Bg, 0.45f), WagonW);
                }
                d -= len + 0.6;
            }
        }
    }

    void DrawLabels(Graphics g)
    {
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            float y = Lay.TrackY(k);
            int n = Sim.Cuts.Where(c => c.Active && c.Track == k).Sum(c => c.Wagons.Count);   // и те, что ещё катятся по пути
            bool hot = hover == "путь " + k;
            var col = hot ? Color.White : n >= YardSim.Norm(k) ? Color.FromArgb(120, 220, 120) : Txt;
            Label(g, $"{k}", F13, col, 1006, y - 19);
            Label(g, L.T(YardSim.TrackNames[k]) + $" · {n}/{YardSim.Norm(k)}", F11, col, 1022, y - 17);
        }
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            var at = Sim.Arr[a];
            float y = Lay.ArrY(a);
            string st = at.State switch
            {
                ArrState.Free => "свободен",
                ArrState.Arriving => "приём поезда",
                ArrState.Uncoupling => "отцепка локомотива",
                ArrState.Inspection => "осмотр ПТО",
                ArrState.WaitLoco => "ждёт горочный локомотив",
                ArrState.LocoComing => "заезд горочного локомотива",
                ArrState.Ready => "готов к роспуску",
                ArrState.Pushing => "надвиг",
                _ => "",
            };
            Label(g, "П" + a, F13, Txt, 152, y - 19);
            string info = at.Train != null ? $"{at.Train.Name} · {at.Train.WagonCount} " + L.T("ваг.") + " · " + L.T(st) : L.T(st);
            Label(g, info, F11, at.State == ArrState.Ready ? Color.FromArgb(120, 220, 120) : TxtDim, 178, y - 17);
        }
        for (int d = 1; d <= Lay.DepTracks; d++)
        {
            var dt = Sim.Dep[d];
            float y = Lay.DepY(d);
            string st = dt.State switch
            {
                DepState.Free => "",
                DepState.Filling => "вывод состава",
                DepState.WaitLoco or DepState.LocoComing => "ждёт локомотив",
                DepState.BrakeTest => "опробование тормозов",
                DepState.Leaving => "отправление",
                _ => "",
            };
            Label(g, "О" + d, F13, Txt, 1432, y - 19);
            if (st != "") Label(g, (dt.TrainNo > 0 ? "№" + dt.TrainNo + " · " : "") + L.T(st), F11, TxtDim, 1456, y - 17);
        }
    }

    // =================================================================
    //  [ДОБАВЛЕНО 24.11.2026] ГОРКА ИЗ ФАЙЛА РЕДАКТОРА: парки, светофоры, подписи — по схеме горки (YardGeo)
    // =================================================================
    static YardGeo G => Lay.Cur;

    void DrawStaticGeo(Graphics g)
    {
        foreach (var (t, p, big) in G.Labels) Label(g, t, big ? F15 : F11, TxtDim, p.X, p.Y, true);
        foreach (var d in G.Decor) if (d.Length > 1) Poly(g, Dim, 3, d);
        // парк приёма
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            bool busy = Sim.Arr[a].Train != null || Sim.Arr[a].State == ArrState.Arriving;
            if (G.ArrBody[a]?.Length > 1) Poly(g, busy ? Color.FromArgb(150, Busy) : Free, 4, G.ArrBody[a]);
            bool route = Sim.Pushing != null && Sim.Pushing.Arr == a;
            if (G.ArrLead[a]?.Length > 1) Poly(g, route ? Lunar : Free, route ? 4 : 3, G.ArrLead[a]);
        }
        // путь надвига
        bool pushBusy = Sim.Pushing != null && Sim.Pushing.Head > Lay.SPushTrack - 5;
        if (G.PushTrack.Length > 1) Poly(g, pushBusy ? Busy : Sim.Pushing != null ? Lunar : Free, 4, G.PushTrack);
        // обходной путь, стоянка горочного локомотива, депо
        float x0 = Enumerable.Range(1, Lay.ArrTracks).Min(a => Lay.At(Lay.SArrTail, 1, a).X) - 30;
        Poly(g, Dim, 3, new(x0, G.BypassY), new(G.HumpLocoPark.X - 20, G.BypassY), G.HumpLocoPark);
        Poly(g, Dim, 3, new(G.DepotGate.X + 20, G.BypassY), G.DepotGate);
        using (var b = new SolidBrush(Color.FromArgb(40, 46, 54))) g.FillRectangle(b, G.Depot);
        using (var p = new Pen(Dim, 2)) g.DrawRectangle(p, G.Depot.X, G.Depot.Y, G.Depot.Width, G.Depot.Height);
        Label(g, "Депо (ТЧ)", F13, TxtDim, G.Depot.X + G.Depot.Width / 2, G.Depot.Y + 11, true);
        Label(g, "стоянка ТЭМ18", F11, TxtDim, G.HumpLocoPark.X, G.HumpLocoPark.Y + 6, true);
        Label(g, "обходной путь", F11, TxtDim, (x0 + G.HumpLocoPark.X) / 2, G.BypassY + 3, true);
        // выход сортировочных путей в парк отправления, сам парк
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            var e = Lay.At(Lay.SEnd, k, 1);
            int d = Enumerable.Range(1, Lay.DepTracks).OrderBy(x => Math.Abs(Lay.DepY(x) - e.Y)).First();
            Poly(g, Dim, 2, e, new(e.X + 12, e.Y), new(G.DepX0, Lay.DepY(d)));
        }
        for (int d = 1; d <= Lay.DepTracks; d++)
        {
            float y = Lay.DepY(d);
            Poly(g, Sim.Dep[d].State == DepState.Free ? Free : Color.FromArgb(150, Busy), 4, new(G.DepX0, y), new(G.DepX1, y));
            Poly(g, Dim, 3, new(G.DepX1, y), new(G.DepX1 + 12, y));
        }
    }

    void DrawSignalsGeo(Graphics g)
    {
        var p = Lay.SignalPoint;
        HumpSignal(g, p.X, p.Y - 8, "Г", 6);
        if (G.Repeater is PointF rp) HumpSignal(g, rp.X, rp.Y, "ПГ", 4);
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            var m = G.ShuntSig[a];
            bool open = Sim.Pushing != null && Sim.Pushing.Arr == a && Sim.Arr[a].Train == Sim.Pushing;
            Lamp(g, m.X, m.Y, 4, open ? Lunar : Blue, true);
            Label(g, "М" + a, F11, TxtDim, m.X + 6, m.Y - 9);
        }
    }

    void DrawLabelsGeo(Graphics g)
    {
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            var p = G.TrackLabel[k];
            int n = Sim.Cuts.Where(c => c.Active && c.Track == k).Sum(c => c.Wagons.Count);
            bool hot = hover == "путь " + k;
            var col = hot ? Color.White : n >= YardSim.Norm(k) ? Color.FromArgb(120, 220, 120) : Txt;
            Label(g, $"{k}", F13, col, p.X, p.Y);
            Label(g, L.T(YardSim.TrackNames[k]) + $" · {n}/{YardSim.Norm(k)}", F11, col, p.X + 16, p.Y + 2);
        }
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            var at = Sim.Arr[a];
            var p = G.ArrLabel[a];
            string st = at.State switch
            {
                ArrState.Free => "свободен",
                ArrState.Arriving => "приём поезда",
                ArrState.Uncoupling => "отцепка локомотива",
                ArrState.Inspection => "осмотр ПТО",
                ArrState.WaitLoco => "ждёт горочный локомотив",
                ArrState.LocoComing => "заезд горочного локомотива",
                ArrState.Ready => "готов к роспуску",
                ArrState.Pushing => "надвиг",
                _ => "",
            };
            Label(g, "П" + a, F13, Txt, p.X, p.Y);
            string info = at.Train != null ? $"{at.Train.Name} · {at.Train.WagonCount} " + L.T("ваг.") + " · " + L.T(st) : L.T(st);
            Label(g, info, F11, at.State == ArrState.Ready ? Color.FromArgb(120, 220, 120) : TxtDim, p.X + 26, p.Y + 2);
        }
        for (int d = 1; d <= Lay.DepTracks; d++)
        {
            var dt = Sim.Dep[d];
            var p = G.DepLabel[d];
            string st = dt.State switch
            {
                DepState.Filling => "вывод состава",
                DepState.WaitLoco or DepState.LocoComing => "ждёт локомотив",
                DepState.BrakeTest => "опробование тормозов",
                DepState.Leaving => "отправление",
                _ => "",
            };
            Label(g, "О" + d, F13, Txt, p.X, p.Y);
            if (st != "") Label(g, (dt.TrainNo > 0 ? "№" + dt.TrainNo + " · " : "") + L.T(st), F11, TxtDim, p.X + 24, p.Y + 2);
        }
    }

    /// <summary>Тормозные позиции на виде сбоку: позиции на одном месте горки (2ТП-А и 2ТП-Б) — одной полосой.</summary>
    void ProfileRetarders(Graphics g)
    {
        foreach (var grp in Sim.Retarders.GroupBy(r => (Math.Round(r.S0 / 10), Math.Round(r.S1 / 10))))
        {
            var r = grp.First();
            if (r.S1 < Lay.ProfileFrom || r.S0 > Lay.ProfileTo) continue;
            float x0 = Lay.ProfX(r.S0), x1 = Lay.ProfX(r.S1);
            int step = grp.Max(x => x.Step);
            string name = grp.Count() == 1 ? r.Name : r.Name.Split('-')[0];
            var c = step == 0 ? Color.FromArgb(90, 98, 110) : Color.FromArgb(255, 200 - step * 30, 40);
            using var b = new SolidBrush(c);
            g.FillPolygon(b, new PointF[] { new(x0, Lay.ProfY(r.S0) + 1), new(x1, Lay.ProfY(r.S1) + 1), new(x1, Lay.ProfY(r.S1) + 6), new(x0, Lay.ProfY(r.S0) + 6) });
            Label(g, name, F11, c, (x0 + x1) / 2, Lay.ProfY((r.S0 + r.S1) / 2) + 8, true);
        }
    }

    static float DistTo(PointF[] pts, PointF v)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            PointF a = pts[i], b = pts[i + 1];
            float dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            float t = l2 <= 0 ? 0 : Math.Clamp(((v.X - a.X) * dx + (v.Y - a.Y) * dy) / l2, 0, 1);
            float x = a.X + dx * t - v.X, y = a.Y + dy * t - v.Y;
            best = Math.Min(best, MathF.Sqrt(x * x + y * y));
        }
        return best;
    }

    string HitTestGeo(PointF v, out int track, out Cut cut)
    {
        track = 0; cut = null;
        foreach (var sw in Sim.Switches.Skip(1))
        {
            var p = Lay.SwitchPoint(sw);
            if (Math.Abs(v.X - p.X) < 11 && v.Y > p.Y - 24 && v.Y < p.Y + 4) return "стрелка " + sw.No;
        }
        foreach (var r in Sim.Retarders)
            if (DistTo(Lay.Line(r.S0, r.S1, r.Lo), v) < 8) return r.Name;
        var sg = Lay.SignalPoint;
        if (Math.Abs(v.X - sg.X) < 14 && v.Y < sg.Y && v.Y > sg.Y - 70) return "горочный светофор";
        if (G.Repeater is PointF rp && Math.Abs(v.X - rp.X) < 10 && v.Y > rp.Y - 56 && v.Y < rp.Y + 4) return "повторитель горочного светофора";
        foreach (var c in Sim.Cuts.Where(c => c.Active))
            for (double s = c.Tail; s <= c.Head; s += 3)
            {
                var p = Lay.At(s, c.Lo, 1);
                if (Math.Abs(p.X - v.X) < 6 && Math.Abs(p.Y - v.Y) < 8) { cut = c; return "отцеп " + c.No; }
            }
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            var lp = G.TrackLabel[k];
            if (DistTo(Lay.Line(Lay.STrack, Lay.SEnd, k), v) < 9 || (v.X >= lp.X - 4 && v.X < lp.X + 150 && v.Y >= lp.Y - 2 && v.Y < lp.Y + 16)) { track = k; return "путь " + k; }
        }
        for (int a = 1; a <= Lay.ArrTracks; a++)
            if (G.ArrBody[a] != null && DistTo(G.ArrBody[a], v) < 10) return "П" + a;
        for (int d = 1; d <= Lay.DepTracks; d++)
            if (DistTo(G.DepLine(d), v) < 10) return "О" + d;
        if (G.PushTrack.Length > 1 && DistTo(G.PushTrack, v) < 8) return "путь надвига";
        if (Lay.Profile.Contains(v)) return "горка";
        if (G.Depot.Contains(v)) return "депо";
        return null;
    }

    // =================================================================
    //  ВИД СБОКУ
    // =================================================================
    void DrawProfile(Graphics g)
    {
        var R = Lay.Profile;
        using (var b = new LinearGradientBrush(R, Color.FromArgb(34, 44, 58), Color.FromArgb(22, 26, 31), 90f)) g.FillRectangle(b, R);
        using (var p = new Pen(Dim, 1)) g.DrawRectangle(p, R.X, R.Y, R.Width, R.Height);
        Label(g, "Вид сбоку: профиль горки (высота увеличена)", F12, TxtDim, R.X + 8, R.Y + 4);
        // насыпь и рельс
        var top = new List<PointF>();
        for (double s = Lay.ProfileFrom; s <= Lay.ProfileTo; s += 4) top.Add(new(Lay.ProfX(s), Lay.ProfY(s)));
        var ground = new List<PointF>(top) { new(top[^1].X, R.Bottom - 1), new(top[0].X, R.Bottom - 1) };
        using (var b = new SolidBrush(Color.FromArgb(66, 58, 48))) g.FillPolygon(b, ground.ToArray());
        Poly(g, Color.FromArgb(170, 175, 182), 2, top.ToArray());
        // отметки: светофор Г, горб, замедлители, стрелочная зона, начало путей
        void Mark(double s, string ru, Color c, float dy = 0)
        {
            float x = Lay.ProfX(s), y = Lay.ProfY(s);
            using var p = new Pen(Color.FromArgb(110, c), 1) { DashStyle = DashStyle.Dot };
            g.DrawLine(p, x, y + 4, x, R.Bottom - 16 - dy);
            Label(g, ru, F11, c, x, R.Bottom - 16 - dy, true);
        }
        Mark(Lay.SSignal, "светофор Г", Txt);
        Mark(0, "горб", Color.White);
        foreach (var (ms, mt, mdy) in Lay.Cur.ProfileMarks) Mark(ms, mt, TxtDim, mdy);   // [24.11.2026] у горки (было 72, 137, 177, пути 1–8)
        Mark(Lay.Cur.SUncouple, "расцепка", TxtDim);
        foreach (var r in Lay.Cur.Builtin ? Sim.Retarders.Take(2) : Enumerable.Empty<Retarder>())
        {
            float x0 = Lay.ProfX(r.S0), x1 = Lay.ProfX(r.S1);
            string name = r.Lo == 1 && r.Hi == 8 ? "1ТП" : "2ТП";
            int step = r.Lo == 1 && r.Hi == 8 ? r.Step : Math.Max(Sim.Retarders[1].Step, Sim.Retarders[2].Step);
            var c = step == 0 ? Color.FromArgb(90, 98, 110) : Color.FromArgb(255, 200 - step * 30, 40);
            using var b = new SolidBrush(c);
            g.FillPolygon(b, new PointF[] { new(x0, Lay.ProfY(r.S0) + 1), new(x1, Lay.ProfY(r.S1) + 1), new(x1, Lay.ProfY(r.S1) + 6), new(x0, Lay.ProfY(r.S0) + 6) });
            Label(g, name, F11, c, (x0 + x1) / 2, Lay.ProfY((r.S0 + r.S1) / 2) + 8, true);
        }
        if (!Lay.Cur.Builtin) ProfileRetarders(g);
        var sp = new PointF(Lay.ProfX(Lay.SSignal), Lay.ProfY(Lay.SSignal));
        HumpSignal(g, sp.X - 10, sp.Y - 2, "", 4);

        // подвижной состав
        var clip = g.Clip;
        g.SetClip(R);
        if (Sim.Pushing is Train pt)
        {
            foreach (var c in pt.Cuts) SideCut(g, c, false);
            SideLoco(g, Rolling.TEM18, pt.LocoFront - 0.5);
        }
        else
        {
            var wait = Sim.Arr.Skip(1).Select(a => a.Train).FirstOrDefault(t => t != null && t.HumpLoco);
            _ = wait;
        }
        if (Sim.Loco == HumpLocoState.AtHorb) SideLoco(g, Rolling.TEM18, Sim.LocoS);
        foreach (var c in Sim.Cuts) if (c.Active && c.Tail < Lay.ProfileTo + 5) SideCut(g, c, true);
        g.Clip = clip;
    }

    void SideCut(Graphics g, Cut c, bool free)
    {
        double s = c.Head;
        foreach (var w in c.Wagons)
        {
            if (s - w.Length < Lay.ProfileTo + 20 && s > Lay.ProfileFrom - 20) SideVehicle(g, s, w.Length, w, null, Sim.SelectedCut == c);
            s -= w.Length;
        }
        if (free && c.Head > Lay.ProfileFrom && c.Tail < Lay.ProfileTo)
        {
            double mid = Math.Clamp(c.Head - c.Length / 2, Lay.ProfileFrom + 10, Lay.ProfileTo - 10);
            string route = c.Route > 0 ? "→" + c.Route : "→ ?";
            var col = c.Route == 0 || (c.Lo > c.Route || c.Hi < c.Route) ? Color.FromArgb(255, 120, 100) : Color.White;
            Label(g, $"{c.No} {route} · {c.V * 3.6:0.0}", F11, col, Lay.ProfX(mid), Lay.ProfY(mid) - 42, true);
        }
    }

    void SideLoco(Graphics g, LocoKind k, double front)
    {
        if (front < Lay.ProfileFrom - 10 || front - k.Length > Lay.ProfileTo) return;
        SideVehicle(g, front, k.Length, null, k, false);
    }

    /// <summary>Вагон или локомотив «вид сбоку»: корпус по роду, тележки; наклон — по профилю.</summary>
    void SideVehicle(Graphics g, double front, double len, Wagon w, LocoKind loco, bool sel)
    {
        // [ИЗМЕНЕНО 17.11.2026] вагон стоит на двух тележках, и обе — на рельсе. Раньше корпус ставился на
        // хорду «голова — хвост»: на горбу он проваливался в рельс, в низине — висел над ним.
        const double B = 0.18;                               // тележка — 18 % длины от конца
        double sf = front - 0.4 - (len - 0.8) * B, sb = front - len + 0.4 + (len - 0.8) * B;
        float xf = Lay.ProfX(sf), yf = Lay.ProfY(sf), xb = Lay.ProfX(sb), yb = Lay.ProfY(sb);
        // рельс между тележками выше прямой (на горбу) — корпус приподнять, чтобы рельс не «протыкал» вагон
        float lift = 0;
        for (double s = sb; s <= sf; s += 0.5)
        {
            float x = Lay.ProfX(s), y = Lay.ProfY(s);
            float yc = xf == xb ? yb : yb + (yf - yb) * (x - xb) / (xf - xb);
            lift = Math.Max(lift, yc - y);
        }
        float D = MathF.Sqrt((xf - xb) * (xf - xb) + (yf - yb) * (yf - yb));
        float L = D / (1 - 2 * (float)B);
        float ang = MathF.Atan2(yf - yb, xf - xb) * 180 / MathF.PI;
        var st = g.Save();
        lift = Math.Max(0, lift - 4);                        // под полом корпуса зазор ~5 точек — тележки остаются на рельсе
        g.TranslateTransform((xb + xf) / 2, (yb + yf) / 2 - lift);
        g.RotateTransform(ang);
        float hl = L / 2, pxm = Lay.ProfPxPerM * 1.25f;
        // тележки
        using (var wb = new SolidBrush(Color.FromArgb(25, 25, 25)))
        {
            foreach (var x in new[] { -D / 2, D / 2 })
            {
                g.FillEllipse(wb, x - 5, -6, 5, 5);
                g.FillEllipse(wb, x + 1, -6, 5, 5);
            }
        }
        if (loco != null) DrawLocoSide(g, loco, hl, pxm);
        else DrawWagonSide(g, w, hl, pxm);
        if (sel) { using var p = new Pen(Color.Gold, 2); g.DrawRectangle(p, -hl, -pxm * 4.6f, 2 * hl, pxm * 4.6f - 3); }
        g.Restore(st);
    }

    static void DrawWagonSide(Graphics g, Wagon w, float hl, float pxm)
    {
        var k = w.Kind;
        using var body = new SolidBrush(k.Body);
        using var dark = new SolidBrush(k.Dark);
        using var line = new Pen(k.Dark, 1);
        float floor = -6;
        if (k == Rolling.Covered || k == Rolling.Reefer || k == Rolling.Service)
        {
            float h = pxm * 3.8f;
            g.FillRectangle(body, -hl, floor - h, 2 * hl, h);
            g.DrawRectangle(line, -hl, floor - h, 2 * hl, h);
            if (k == Rolling.Covered) { g.FillRectangle(dark, -pxm * 1.0f, floor - h + 3, pxm * 2.0f, h - 4); }
            if (k == Rolling.Reefer) { using var s = new SolidBrush(Color.FromArgb(40, 110, 190)); g.FillRectangle(s, -hl, floor - h * 0.45f, 2 * hl, 3); g.FillRectangle(dark, hl - pxm * 1.6f, floor - h + 2, pxm * 1.2f, h * 0.35f); }
            if (k == Rolling.Service)
            {
                using var win = new SolidBrush(Color.FromArgb(255, 225, 120));
                for (float x = -hl + pxm * 1.2f; x < hl - pxm * 1.5f; x += pxm * 2.2f) g.FillRectangle(win, x, floor - h + 4, pxm * 1.1f, h * 0.35f);
                using var red = new SolidBrush(Color.FromArgb(220, 30, 30));
                g.FillRectangle(red, -hl, floor - 4, 2 * hl, 2);
            }
        }
        else if (k == Rolling.Gondola)
        {
            float h = pxm * 2.6f;
            if (w.Loaded) { using var coal = new SolidBrush(Color.FromArgb(25, 25, 25)); g.FillEllipse(coal, -hl + 2, floor - h - 5, 2 * hl - 4, 10); }
            g.FillRectangle(body, -hl, floor - h, 2 * hl, h);
            for (float x = -hl + pxm * 1.6f; x < hl; x += pxm * 1.6f) g.DrawLine(line, x, floor - h, x, floor);
        }
        else if (k == Rolling.Flat || k == Rolling.Container)
        {
            g.FillRectangle(body, -hl, floor - pxm * 0.7f, 2 * hl, pxm * 0.7f);
            if (w.Loaded)
            {
                if (k == Rolling.Container)
                {
                    using var c1 = new SolidBrush(Color.FromArgb(200, 70, 50));
                    using var c2 = new SolidBrush(Color.FromArgb(50, 130, 90));
                    float cw = hl - 2, h = pxm * 2.6f;
                    g.FillRectangle(c1, -hl + 1, floor - pxm * 0.7f - h, cw, h);
                    g.FillRectangle(c2, 1, floor - pxm * 0.7f - h, cw, h);
                }
                else
                {
                    using var tim = new SolidBrush(Color.FromArgb(140, 95, 50));
                    g.FillRectangle(tim, -hl + 4, floor - pxm * 0.7f - pxm * 1.6f, 2 * hl - 8, pxm * 1.6f);
                }
            }
        }
        else if (k == Rolling.Tank || k == Rolling.GasTank)
        {
            float h = pxm * 2.8f;
            g.FillRectangle(dark, -hl, floor - pxm * 0.5f, 2 * hl, pxm * 0.5f);
            using var path = new GraphicsPath();
            path.AddArc(-hl + 1, floor - pxm * 0.5f - h, h, h, 90, 180);
            path.AddArc(hl - 1 - h, floor - pxm * 0.5f - h, h, h, 270, 180);
            path.CloseFigure();
            g.FillPath(body, path);
            g.FillRectangle(body, -pxm * 0.5f, floor - pxm * 0.5f - h - pxm * 0.6f, pxm, pxm * 0.6f);
            if (k == Rolling.GasTank) { using var red = new SolidBrush(Color.FromArgb(210, 40, 40)); g.FillRectangle(red, -hl + h / 2, floor - pxm * 0.5f - h * 0.55f, 2 * hl - h, h * 0.18f); }
        }
        else if (k == Rolling.Hopper)
        {
            float h = pxm * 3.4f;
            g.FillPolygon(body, new PointF[] { new(-hl, floor - h), new(hl, floor - h), new(hl, floor - h * 0.45f), new(hl * 0.35f, floor), new(-hl * 0.35f, floor), new(-hl, floor - h * 0.45f) });
            for (float x = -hl + pxm * 1.5f; x < hl; x += pxm * 1.5f) g.DrawLine(line, x, floor - h, x, floor - h * 0.45f);
        }
    }

    static void DrawLocoSide(Graphics g, LocoKind k, float hl, float pxm)
    {
        using var body = new SolidBrush(k.Body);
        using var stripe = new SolidBrush(k.Stripe);
        using var win = new SolidBrush(Color.FromArgb(170, 210, 240));
        float floor = -6;
        if (k.Shunting)
        {
            // ТЭМ18: длинный капот и кабина ближе к одному концу
            float h = pxm * 3.0f, cab = pxm * 4.3f;
            g.FillRectangle(body, -hl, floor - h, 2 * hl - pxm * 3.2f, h);
            g.FillRectangle(body, hl - pxm * 3.2f, floor - cab, pxm * 3.2f, cab);
            g.FillRectangle(win, hl - pxm * 2.8f, floor - cab + 3, pxm * 2.4f, cab * 0.3f);
            g.FillRectangle(stripe, -hl, floor - h * 0.35f, 2 * hl, 3);
        }
        else
        {
            float h = pxm * 4.2f;
            g.FillRectangle(body, -hl, floor - h, 2 * hl, h);
            g.FillRectangle(stripe, -hl, floor - h * 0.4f, 2 * hl, 4);
            g.FillRectangle(win, hl - pxm * 1.6f, floor - h + 3, pxm * 1.3f, h * 0.3f);
            g.FillRectangle(win, -hl + pxm * 0.3f, floor - h + 3, pxm * 1.3f, h * 0.3f);
            if (k.Electric)
            {
                using var p = new Pen(Color.FromArgb(40, 40, 40), 1.5f);
                foreach (var x in new[] { -hl * 0.5f, hl * 0.5f })
                {
                    g.DrawLine(p, x - 6, floor - h, x, floor - h - 9);
                    g.DrawLine(p, x, floor - h - 9, x + 6, floor - h - 9);
                }
                using var sep = new Pen(Color.FromArgb(30, 60, 90), 1);
                g.DrawLine(sep, 0, floor - h, 0, floor);
            }
        }
    }

    // =================================================================
    //  МЫШЬ
    // =================================================================
    string HitTest(PointF v, out int track, out Cut cut)
    {
        track = 0; cut = null;
        if (Sim == null) return null;
        if (!Lay.Cur.Builtin) return HitTestGeo(v, out track, out cut);
        foreach (var sw in Sim.Switches.Skip(1))
        {
            var p = Lay.SwitchPoint(sw);
            if (Math.Abs(v.X - p.X) < 11 && v.Y > p.Y - 24 && v.Y < p.Y + 4) return "стрелка " + sw.No;
        }
        foreach (var r in Sim.Retarders)
            if (Lay.Line(r.S0, r.S1, r.Lo).Any(p => Math.Abs(p.X - v.X) < 18 && Math.Abs(p.Y - v.Y) < 9)) return r.Name;
        var sg = Lay.SignalPoint;
        if (Math.Abs(v.X - sg.X) < 14 && v.Y < sg.Y && v.Y > sg.Y - 70) return "горочный светофор";
        if (Math.Abs(v.X - 470) < 10 && v.Y > 60 && v.Y < 116) return "повторитель горочного светофора";
        foreach (var c in Sim.Cuts.Where(c => c.Active))
            for (double s = c.Tail; s <= c.Head; s += 3)
            {
                var p = Lay.At(s, c.Lo, 1);
                if (Math.Abs(p.X - v.X) < 6 && Math.Abs(p.Y - v.Y) < 8) { cut = c; return "отцеп " + c.No; }
            }
        if (v.X > 990 && v.X < 1410)
            for (int k = 1; k <= Lay.Tracks; k++)
                if (Math.Abs(v.Y - (Lay.TrackY(k) - 6)) < 14) { track = k; return "путь " + k; }
        if (v.X > 140 && v.X < 530)
            for (int a = 1; a <= Lay.ArrTracks; a++)
                if (Math.Abs(v.Y - (Lay.ArrY(a) - 6)) < 14) return "П" + a;
        if (v.X > 1425 && v.X < 1600)
            for (int d = 1; d <= Lay.DepTracks; d++)
                if (Math.Abs(v.Y - (Lay.DepY(d) - 6)) < 16) return "О" + d;
        if (v.X > 590 && v.X < 700 && Math.Abs(v.Y - 190) < 10) return "путь надвига";
        if (Lay.Profile.Contains(v)) return "горка";
        if (v.X > 290 && v.X < 470 && v.Y > 344 && v.Y < 384) return "депо";
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        mouse = ToV(e.Location);
        var h = HitTest(mouse, out _, out _);
        if (h != hover)
        {
            hover = h;
            Cursor = h != null && (h.StartsWith("стрелка") || h.StartsWith("путь ") || h.StartsWith("отцеп")) ? Cursors.Hand : Cursors.Default;
            if (h != null) Pointed?.Invoke(h);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = null; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var v = ToV(e.Location);
        var h = HitTest(v, out int track, out var cut);
        if (h == null) return;
        if (e.Button == MouseButtons.Right)
        {
            if (track > 0) TrackMenu?.Invoke(track, e.Location);
            return;
        }
        if (h.StartsWith("стрелка ")) Sim.ThrowSwitch(int.Parse(h[8..]));
        else if (track > 0) Sim.RouteButton(track);
        else if (cut != null) Sim.SelectedCut = Sim.SelectedCut == cut ? null : cut;
        Clicked?.Invoke();
        base.OnMouseDown(e);
    }

    void DrawHover(Graphics g)
    {
        if (hover == null) return;
        string tip = Tip(hover);
        if (string.IsNullOrEmpty(tip)) return;
        var s = L.T(tip);
        var sz = g.MeasureString(s, F12, 360);
        float x = Math.Min(mouse.X + 14, Lay.W - sz.Width - 8), y = Math.Min(mouse.Y + 14, Lay.Hv - sz.Height - 8);
        using var b = new SolidBrush(Color.FromArgb(235, 15, 18, 22));
        g.FillRectangle(b, x - 4, y - 3, sz.Width + 8, sz.Height + 6);
        using var p = new Pen(Color.FromArgb(90, 110, 140));
        g.DrawRectangle(p, x - 4, y - 3, sz.Width + 8, sz.Height + 6);
        using var tb = new SolidBrush(Color.White);
        g.DrawString(s, F12, tb, new RectangleF(x, y, 360, sz.Height + 2));
    }

    string Tip(string id)
    {
        if (id.StartsWith("стрелка "))
        {
            var sw = Sim.Switches[int.Parse(id[8..])];
            return $"Стрелка {sw.No}: " + (sw.Throwing ? "переводится" : sw.Plus ? $"плюс — на пути {sw.Range(true)}" : $"минус — на пути {sw.Range(false)}") + ". Щелчок — перевести";
        }
        if (id.StartsWith("путь ") && int.TryParse(id[5..], out int k))
            return $"Путь {k} — {YardSim.TrackNames[k]}: {Sim.OnTrack(k).Sum(c => c.Wagons.Count)} ваг. (норма {YardSim.Norm(k)}). Щелчок — маршрут отцепу на этот путь, правая кнопка — маневры";
        if (id.StartsWith("отцеп "))
        {
            var c = Sim.Cuts.FirstOrDefault(x => "отцеп " + x.No == id && x.Active);
            if (c == null) return null;
            return $"Отцеп {c.No}: {c.Wagons.Count} ваг. ({c.KindsText}), {c.Mass:0} т, {c.V * 3.6:0.0} км/ч, по листку — путь {c.Dest}";
        }
        var r = Sim.Retarders.FirstOrDefault(x => x.Name == id);
        if (r != null) return $"Тормозная позиция {r.Name}: ступень {(r.Step == 0 ? "О (оттормозить)" : r.Step.ToString())}" + (r.Cut != null ? $", рек. скорость {r.Target * 3.6:0.0} км/ч" : "");
        if (id == "горочный светофор") return "Горочный светофор Г: " + YardSim.AspectName(Sim.Hump);
        return null;
    }
}
