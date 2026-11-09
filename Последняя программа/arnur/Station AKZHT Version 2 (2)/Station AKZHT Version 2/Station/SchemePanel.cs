using System.Drawing.Drawing2D;
using StationApp.Akzht;
using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

namespace StationApp;

// =====================================================================
//  ПАНЕЛЬ СХЕМЫ — одна на все станции (АКЖТ, путевой пост).
//  Рисует то, что описано в SchemeGeometry, в стиле пульта Алгабаса,
//  и принимает клики: выбор маршрута (светофор → светофор или путь),
//  перевод стрелок, занятость участков, контекстные меню.
// =====================================================================

class RailSchemePanel : Panel
{
    readonly StationView view;
    SchemeGeometry G => view.Geo;
    static Interlocking IL => ModelHost.Model.IL;

    public bool Simple;             // "железнодорожный" (упрощённый) интерфейс
    public Func<bool> ShuntMode = () => false;   // какая категория выбрана в окне (для поиска маршрута)
    bool Blink => ModelHost.BlinkPhase;

    RailSignal selected;
    string hoverText = "";

    // [LAB] на что студент показывает мышью — это нужно «ЖД AI» для вопросов вроде «для чего этот светофор?».
    // Объект запоминается, если на нём задержались (проход мимо не считается) или кликнули. Поведение схемы не меняется.
    (PointedKind, string)? labHover;
    DateTime labHoverSince;

    void LabTrack(RailSignal sig, RailSwitch sw, TrackSection sec)
    {
        (PointedKind, string)? now =
            sig != null ? (PointedKind.Signal, sig.Id) :
            sw != null ? (PointedKind.Switch, sw.Id) :
            sec != null ? (PointedKind.Section, sec.Id) : ((PointedKind, string)?)null;
        if (Equals(now, labHover)) return;
        LabCommit();
        labHover = now;
        labHoverSince = DateTime.Now;
    }

    void LabCommit()
    {
        if (labHover != null && (DateTime.Now - labHoverSince).TotalMilliseconds >= 250)
            StationContext.Point(labHover.Value.Item1, labHover.Value.Item2);
        labHover = null;
    }
    float scale = 1;
    PointF off;

    // ---- цвета (как у пульта Алгабаса) ----
    public static readonly Color Bg = Color.FromArgb(122, 166, 155);
    static readonly Color TrackColor = Color.White;
    static readonly Color RouteColor = Color.FromArgb(255, 235, 0);
    // [ИЗМЕНЕНО 12.10.2026] маневровые маршруты — жёлтым, как поездные (было голубым)
    static readonly Color ShuntRouteColor = RouteColor;
    static readonly Color OccColor = Color.FromArgb(225, 25, 25);
    static readonly Color ReleasingColor = Color.FromArgb(255, 150, 40);
    static readonly Color LampOff = Color.FromArgb(30, 30, 30);
    static readonly Color LampOffSimple = Color.FromArgb(128, 128, 128);
    static readonly Color TextDark = Color.FromArgb(35, 55, 50);
    static readonly Color Dimmed = Color.FromArgb(150, 175, 168);

    // размеры подписей — в Ui/SchemeStyle.cs (и в scheme_style.ini рядом с программой)
    readonly Font fBig = new("Bahnschrift", 20f, FontStyle.Bold);
    readonly Font fSig = new("Bahnschrift", SchemeStyle.SignalLabelSize, FontStyle.Bold);
    readonly Font fSigSmall = new("Bahnschrift", SchemeStyle.SignalLabelSmallSize, FontStyle.Bold);
    readonly Font fSection = new("Bahnschrift", SchemeStyle.SectionLabelSize, SchemeStyle.SectionLabelBold ? FontStyle.Bold : FontStyle.Regular);
    readonly Font fSmallBold = new("Bahnschrift", 9.5f, FontStyle.Bold);
    readonly Font fSw = new("Bahnschrift", SchemeStyle.SwitchLabelSize, FontStyle.Bold);
    readonly Font fInfo = new("Bahnschrift", 10.5f);
    readonly Font fTiny = new("Bahnschrift", 7.5f);
    readonly Dictionary<(float, bool), Font> textFonts = new();
    readonly StringFormat center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    const float LampR = 7.5f, LampStep = 17f, StandH = 24f;
    const float SimpleLampR = LampR + 1, SimpleLampStep = SimpleLampR * 2 + 3;

    /// <summary>Панели, у которых пути должны быть на одном уровне (АКЖТ и пост рядом):
    /// у них общий масштаб и общая высота схемы, поэтому одинаковая проектная Y
    /// оказывается на одной высоте экрана. null — панель масштабируется сама по себе.</summary>
    public List<RailSchemePanel> LevelGroup;

    /// <summary>Куда прижать схему по горизонтали: Near — к левому краю, Far — к правому, Center — по центру.
    /// На двух мониторах АКЖТ прижата вправо, пост — влево: пути стыкуются ровно по границе мониторов.</summary>
    public StringAlignment HAlign = StringAlignment.Center;

    public RailSchemePanel(StationView v)
    {
        view = v;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Bg;
    }

    // =================================================================
    //  МАСШТАБ
    // =================================================================
    void UpdateTransform()
    {
        // [ИЗМЕНЕНО 08.10.2026] Общий масштаб с соседней схемой: пути I и II АКЖТ и поста —
        // на одном уровне. Скрытая (кнопкой «Вид») схема в расчёт не берётся.
        var group = LevelGroup?.Where(p => p.Width > 0 && p.Height > 30 && p.FindForm() != null).ToList();
        if (group == null || group.Count < 2 || !group.Contains(this)) group = new List<RailSchemePanel> { this };
        float designH = group.Max(p => p.G.DesignH);

        float usableH = Height - 30;
        scale = group.Min(p => Math.Min(p.Width / p.G.DesignW, (p.Height - 30) / designH));
        if (scale <= 0) scale = 1;
        float freeW = Width - G.DesignW * scale;
        float offX = HAlign switch { StringAlignment.Near => 0, StringAlignment.Far => freeW, _ => freeW / 2f };
        off = new PointF(offX, (usableH - designH * scale) / 2f);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (LevelGroup == null) return;
        foreach (var p in LevelGroup)
            if (p != this) p.Invalidate();      // масштаб соседа зависит и от нашего размера
    }

    PointF ToDesign(Point p) => new((p.X - off.X) / scale, (p.Y - off.Y) / scale);

    // =================================================================
    //  РИСОВАНИЕ
    // =================================================================
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        UpdateTransform();

        var st = g.Save();
        g.TranslateTransform(off.X, off.Y);
        g.ScaleTransform(scale, scale);

        DrawTexts(g);
        foreach (var c in G.Crossings) DrawCrossing(g, c);
        DrawTunnelsBack(g);
        DrawTracks(g);
        DrawJointsAndBuffers(g);
        DrawTunnelsFront(g);
        DrawSwitchLabels(g);
        DrawSectionLabels(g);       // и в упрощённом режиме: упрощаются только светофоры
        foreach (var sg in G.Signals) DrawSignal(g, sg);
        DrawTrains(g);
        foreach (var d in G.Directions) DrawDirection(g, d);

        g.Restore(st);

        using var hb = new SolidBrush(TextDark);
        string line = hoverText.Length > 0
            ? hoverText
            : "ЛКМ: светофор начала → светофор конца или путь назначения = маршрут  |  ЛКМ по номеру стрелки — перевод  |  " +
              "ПКМ по пути — занять/освободить  |  ПКМ по светофору или стрелке — меню";
        g.DrawString(line, fInfo, hb, new RectangleF(8, Height - 26, Width - 16, 24));
    }

    Font TextFont(float size, bool bold)
    {
        if (!textFonts.TryGetValue((size, bold), out var f))
            textFonts[(size, bold)] = f = new Font("Bahnschrift", size, bold ? FontStyle.Bold : FontStyle.Regular);
        return f;
    }

    void DrawTexts(Graphics g)
    {
        using var w = new SolidBrush(Color.White);
        using var d = new SolidBrush(TextDark);
        if (view.Caption.Length > 0) g.DrawString(view.Caption, fBig, w, G.DesignW / 2, 40, center);
        foreach (var t in G.Texts)
        {
            var sf = new StringFormat { Alignment = t.Align, LineAlignment = StringAlignment.Center };
            g.DrawString(t.Text, TextFont(t.Size, t.Bold), t.White ? w : d, t.Pos, sf);
        }
        foreach (var b in G.Boundaries) BoundarySign(g, b.X, b.Y);
    }

    void BoundarySign(Graphics g, float x, float y)
    {
        using var p = new Pen(TextDark, 1.5f);
        using var b = new SolidBrush(Color.White);
        using var t = new SolidBrush(TextDark);
        var r = new RectangleF(x - 36, y, 72, 22);
        g.FillRectangle(b, r);
        g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        g.DrawString("граница станции", fTiny, t, r, center);
    }

    void DrawCrossing(Graphics g, CrossingGeom cg)
    {
        bool closed = cg.Crossing.Closed;
        var c = closed ? (Blink ? Color.FromArgb(230, 20, 20) : Color.FromArgb(120, 20, 20)) : Color.FromArgb(40, 150, 60);
        using var p = new Pen(c, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float x = cg.X, y1 = cg.Y1 - 38, y2 = cg.Y2 + 38;
        foreach (var dx in new[] { -16f, 16f })
        {
            float s = Math.Sign(dx);
            g.DrawLines(p, new[]
            {
                new PointF(x + dx + s * 12, y1 - 10), new PointF(x + dx, y1 + 4),
                new PointF(x + dx, y2 - 4), new PointF(x + dx + s * 12, y2 + 10)
            });
        }
        using var tb = new SolidBrush(closed ? Color.FromArgb(160, 0, 0) : TextDark);
        g.DrawString(closed ? "ПЕРЕЕЗД ЗАКРЫТ" : "Переезд", fSmallBold, tb, x, y2 + 26, center);
    }

    // ---------------------- тоннели ----------------------
    void DrawTunnelsBack(Graphics g)
    {
        using var b = new SolidBrush(Color.FromArgb(90, 110, 100));
        foreach (var t in G.Tunnels)
        {
            float x1 = t.OpensRight ? t.Portal.X : t.Portal.X - t.Length;
            g.FillRectangle(b, x1, t.Portal.Y - 22, t.Length, 44);
        }
    }

    void DrawTunnelsFront(Graphics g)
    {
        using var pen = new Pen(Color.FromArgb(40, 40, 40), 5f);
        foreach (var t in G.Tunnels)
        {
            float x = t.Portal.X, y = t.Portal.Y;
            float dir = t.OpensRight ? 1 : -1;
            // портал: арка "[" у входа в тоннель
            g.DrawLines(pen, new[]
            {
                new PointF(x + dir * 10, y - 24), new PointF(x, y - 24), new PointF(x, y + 24), new PointF(x + dir * 10, y + 24)
            });
        }
    }

    // ---------------------- пути ----------------------
    Color PieceColor(TrackPiece p, out bool releasing)
    {
        releasing = false;
        var sec = IL.Sections[p.SectionId];
        if (sec.Occupied) return OccColor;
        foreach (var r in IL.ActiveRoutes)
        {
            if (!r.Pieces.Contains(p.Id)) continue;
            if (r.Released.Contains(sec)) continue;
            if (r.Releasing) { releasing = true; return ReleasingColor; }
            return r.Shunting ? ShuntRouteColor : RouteColor;
        }
        // [ДОБАВЛЕНО 12.10.2026] перегон в поездном маршруте на перегон (нашем или соседа) — до входного соседа
        if (sec.LineRoute) return RouteColor;
        return TrackColor;
    }

    void DrawTracks(Graphics g)
    {
        using (var dp = new Pen(TrackColor, 5f))
            foreach (var pts in G.Decor) g.DrawLines(dp, pts);

        foreach (var p in G.Pieces)
        {
            var col = PieceColor(p, out bool releasing);
            if (releasing && !Blink) col = TrackColor;

            if (p.SwitchId == null)
            {
                using var pen = new Pen(col, 5f);
                g.DrawLines(pen, p.Pts);
                continue;
            }

            var sw = IL.Switches[p.SwitchId];
            bool activeBranch = sw.Plus == p.StraightBranch;
            if (activeBranch)
            {
                if (!sw.Controlled && !Blink) col = Color.FromArgb(190, 205, 200);   // без контроля — мигает
                using var pen = new Pen(col, 5f);
                g.DrawLines(pen, p.Pts);
            }
            else
            {
                // неактивная ветка: тоньше и с разрывом у остряка — видно, куда стоит стрелка
                var pts = CutAtPoint(p.Pts, p.PointAtStart, 18f);
                var faded = col == TrackColor ? Color.FromArgb(200, 222, 216) : Color.FromArgb(150, col);
                using var pen = new Pen(faded, 2.5f);
                if (pts.Length >= 2) g.DrawLines(pen, pts);
            }
        }
    }

    static PointF[] CutAtPoint(PointF[] pts, bool atStart, float cut)
    {
        var list = pts.ToList();
        if (!atStart) list.Reverse();
        var a = list[0];
        var b = list[1];
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > cut) list[0] = new PointF(a.X + dx / len * cut, a.Y + dy / len * cut);
        else list.RemoveAt(0);
        if (!atStart) list.Reverse();
        return list.ToArray();
    }

    void DrawJointsAndBuffers(Graphics g)
    {
        using var bg = new SolidBrush(Bg);
        foreach (var j in G.Joints) g.FillRectangle(bg, j.X - 2f, j.Y - 4f, 4f, 8f);
        using var bp = new Pen(Color.Black, 6f);
        foreach (var b in G.Buffers) g.DrawLine(bp, b.X, b.Y - 16, b.X, b.Y + 16);
    }

    void DrawSwitchLabels(Graphics g)
    {
        foreach (var (label, (pt, swId)) in G.SwitchLabels)
        {
            var sw = IL.Switches[swId];
            string txt = label + sw.PosText;
            var size = g.MeasureString(txt, fSw);
            var rect = new RectangleF(pt.X - size.Width / 2 - 2, pt.Y - size.Height / 2, size.Width + 4, size.Height);
            if (sw.Locked)
                using (var lb = new SolidBrush(Color.FromArgb(255, 225, 90))) g.FillRectangle(lb, rect);
            Color tc = !sw.Controlled ? (Blink ? Color.FromArgb(200, 0, 0) : TextDark)
                     : sw.Plus ? TextDark : Color.FromArgb(150, 50, 0);
            using var tb = new SolidBrush(tc);
            g.DrawString(txt, fSw, tb, pt, center);
        }
    }

    void DrawSectionLabels(Graphics g)
    {
        using var b = new SolidBrush(SchemeStyle.SectionLabelColor);
        // половина высоты букв + половина толщины пути + зазор
        float need = fSection.Size * 96f / 72f / 2f + 2.5f + SchemeStyle.SectionLabelGap;
        foreach (var (id, pt) in G.SectionLabels)
            if (IL.Sections.TryGetValue(id, out var s))
                g.DrawString(s.Label, fSection, b, AwayFromTrack(pt, need), center);
    }

    /// <summary>Точки подписей заданы под старый мелкий шрифт (в 14–16 от пути). Крупную надпись
    /// отодвигаем от ближайшего горизонтального пути, чтобы она не наезжала на линию.</summary>
    PointF AwayFromTrack(PointF pt, float need)
    {
        float bestDy = float.MaxValue;
        foreach (var piece in G.Pieces)
            for (int i = 0; i < piece.Pts.Length - 1; i++)
            {
                var a = piece.Pts[i];
                var c = piece.Pts[i + 1];
                if (Math.Abs(a.Y - c.Y) > 0.5f) continue;
                if (pt.X < Math.Min(a.X, c.X) || pt.X > Math.Max(a.X, c.X)) continue;
                float dy = pt.Y - a.Y;
                if (Math.Abs(dy) < Math.Abs(bestDy)) bestDy = dy;
            }
        if (bestDy == float.MaxValue || Math.Abs(bestDy) >= need) return pt;
        float trackY = pt.Y - bestDy;
        return new PointF(pt.X, trackY + (bestDy < 0 ? -need : need));
    }

    // ---------------------- светофоры ----------------------
    (PointF lamp0, float dir, float lampY) LampBase(SignalGeom sg)
    {
        float dir = sg.LampsToLeft ? -1 : 1;
        float y = sg.Foot.Y + (sg.Above ? -StandH : StandH);
        return (new PointF(sg.Foot.X + dir * 14, y), dir, y);
    }

    /// <summary>Сколько ламп рисуется: в упрощённом режиме одна, у светофоров с 5+ огнями — две.</summary>
    int DrawnLamps(RailSignal s)
    {
        if (!Simple) return Math.Max(1, s.Lamps.Length);
        int min = SchemeStyle.SimpleSecondLampMinLamps;
        // [ИЗМЕНЕНО 12.10.2026] вторая лампа и у светофоров, отмеченных в схеме (SimpleSecondLamp), — например Н поста
        return min > 0 && (s.Lamps.Length >= min || s.SimpleSecondLamp) ? 2 : 1;
    }

    float Step => Simple ? SimpleLampStep : LampStep;

    RectangleF SignalRect(SignalGeom sg)
    {
        var s = IL.Signals[sg.SignalId];
        var (l0, dir, y) = LampBase(sg);
        int n = DrawnLamps(s);
        float r = Simple ? SimpleLampR : LampR;
        float x1 = l0.X - dir * r, x2 = l0.X + dir * ((n - 1) * Step + r);
        float left = Math.Min(x1, x2) - 4, right = Math.Max(x1, x2) + 4;
        float top = sg.Above ? y - 36 : y - LampR - 4;
        float bottom = sg.Above ? y + LampR + 4 : y + 36;
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    static Color LampColor(string lamp) => lamp switch
    {
        "yellow" or "yellow1" => Color.FromArgb(255, 215, 0),
        "green" => Color.FromArgb(0, 210, 80),
        "red" => Color.FromArgb(235, 25, 25),
        "white" => Color.FromArgb(235, 240, 255),
        "blue" => Color.FromArgb(40, 90, 255),
        _ => Color.Gray
    };

    void DrawSignal(Graphics g, SignalGeom sg)
    {
        var s = IL.Signals[sg.SignalId];
        var (l0, dir, y) = LampBase(sg);

        using (var sp = new Pen(Color.White, 2f))
        {
            float ty = sg.Foot.Y + (sg.Above ? -6 : 6);
            g.DrawLine(sp, sg.Foot.X, ty, sg.Foot.X, y);
            g.DrawLine(sp, sg.Foot.X, y, l0.X - dir * LampR, y);
        }

        if (Simple)
        {
            Color c = LampOffSimple;
            switch (s.Aspect)
            {
                case Aspect.Red: case Aspect.Invite: c = LampColor("red"); break;
                case Aspect.Green: c = LampColor("green"); break;
                case Aspect.Yellow: case Aspect.TwoYellow: c = LampColor("yellow"); break;
                case Aspect.YellowBlink: case Aspect.TwoYellowTopBlink: c = Blink ? LampColor("yellow") : LampOffSimple; break;
                case Aspect.White: c = LampColor("white"); break;
                case Aspect.Blue: c = LampColor("blue"); break;
            }
            if (IL.TestMode) c = Color.White;
            Lamp(g, l0, c, SimpleLampR);

            if (DrawnLamps(s) == 2)
            {
                // [ДОБАВЛЕНО 08.10.2026] вторая лампа у светофоров с 5+ огнями (входные):
                // нижний жёлтый при «двух жёлтых» и лунно-белый мигающий при пригласительном
                Color c2 = s.Aspect switch
                {
                    Aspect.TwoYellow or Aspect.TwoYellowTopBlink => LampColor("yellow"),
                    Aspect.Invite => Blink ? LampColor("white") : LampOffSimple,
                    _ => LampOffSimple
                };
                if (IL.TestMode) c2 = Color.White;
                Lamp(g, new PointF(l0.X + dir * SimpleLampStep, y), c2, SimpleLampR);
            }
            else if (s.Aspect == Aspect.Invite && Blink)
                Lamp(g, new PointF(l0.X + dir * SimpleLampStep, y), LampColor("white"), 5f);
        }
        else
        {
            // Lamps перечислены сверху вниз по мачте; на схеме нижняя лампа ближе всего к кронштейну
            for (int i = 0; i < s.Lamps.Length; i++)
            {
                var name = s.Lamps[s.Lamps.Length - 1 - i];
                bool on = IL.TestMode || s.IsLampOn(name, Blink);
                Lamp(g, new PointF(l0.X + dir * i * LampStep, y), on ? LampColor(name) : LampOff, LampR);
            }
        }

        var rect = SignalRect(sg);
        if (selected == s)
        {
            using var hp = new Pen(Color.White, 2f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(hp, rect.X, rect.Y, rect.Width, rect.Height);
        }

        int n = DrawnLamps(s);
        float cx = l0.X + dir * (n - 1) * Step / 2f;
        float ly = sg.Above ? y - 22 : y + 22;
        Color tc = Color.White;
        if (selected != null && selected != s)
            tc = IL.Routes.Any(r => r.Start == selected && r.End == s) ? Color.FromArgb(255, 255, 160) : Dimmed;
        if (s.Kind == SignalKind.Automatic && s.Aspect == Aspect.Dark) tc = Dimmed;
        using var tb = new SolidBrush(tc);
        var font = s.Kind == SignalKind.Automatic || s.IsShuntOnly ? fSigSmall : fSig;
        g.DrawString(s.Label + sg.Suffix, font, tb, cx, ly, center);
    }

    static void Lamp(Graphics g, PointF c, Color color, float r)
    {
        using var b = new SolidBrush(color);
        using var p = new Pen(Color.FromArgb(20, 20, 20), 1.2f);
        g.FillEllipse(b, c.X - r, c.Y - r, r * 2, r * 2);
        g.DrawEllipse(p, c.X - r, c.Y - r, r * 2, r * 2);
    }

    // ---------------------- поезда и направление ----------------------
    void DrawTrains(Graphics g)
    {
        foreach (var t in ModelHost.Model.Trains.Trains)
        {
            var head = t.Head;
            if (head == null) continue;
            var piece = G.Pieces.FirstOrDefault(p => p.SectionId == head.Id && (p.SwitchId == null || p.StraightBranch))
                        ?? G.Pieces.FirstOrDefault(p => p.SectionId == head.Id);
            if (piece == null) continue;           // поезд на другом экране
            var a = piece.Pts[0];
            var b = piece.Pts[^1];
            var mid = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            string txt = $"№{t.Number} {(t.Dir == Dir.Right ? "→" : "←")}";
            var size = g.MeasureString(txt, fSmallBold);
            var r = new RectangleF(mid.X - size.Width / 2 - 4, mid.Y - 11 - 22, size.Width + 8, 22);
            using var bb = new SolidBrush(t.Waiting.Length > 0 ? Color.FromArgb(120, 40, 40) : Color.FromArgb(25, 60, 140));
            using var tb = new SolidBrush(Color.White);
            g.FillRectangle(bb, r);
            g.DrawString(txt, fSmallBold, tb, r, center);
        }
    }

    void DrawDirection(Graphics g, DirectionGeom d)
    {
        var line = d.Line;
        using var tb = new SolidBrush(TextDark);
        string text = line.Title + ", " + (line.Current == Dir.Right ? d.RightText : d.LeftText);
        if (line.AuxRunning) text += $"  (ВСП смена: {Math.Ceiling(line.AuxTimer)} с)";
        g.DrawString(text, fSmallBold, tb, d.Pos, center);
        using var p = new Pen(TextDark, 3f) { CustomEndCap = new AdjustableArrowCap(5, 6) };
        if (line.Current == Dir.Right) g.DrawLine(p, d.ArrowX1, d.ArrowY, d.ArrowX2, d.ArrowY);
        else g.DrawLine(p, d.ArrowX2, d.ArrowY, d.ArrowX1, d.ArrowY);
    }

    // =================================================================
    //  ПОИСК ОБЪЕКТОВ ПОД МЫШЬЮ
    // =================================================================
    RailSignal SignalAt(PointF p)
    {
        foreach (var sg in G.Signals)
            if (SignalRect(sg).Contains(p)) return IL.Signals[sg.SignalId];
        return null;
    }

    RailSwitch SwitchAt(PointF p)
    {
        foreach (var (_, (pt, swId)) in G.SwitchLabels)
            if (Math.Abs(p.X - pt.X) < 18 && Math.Abs(p.Y - pt.Y) < 13)
                return IL.Switches[swId];
        return null;
    }

    TrackSection SectionAt(PointF p)
    {
        string best = null;
        float bestD = 10f;
        foreach (var piece in G.Pieces)
            for (int i = 0; i < piece.Pts.Length - 1; i++)
            {
                float d = DistToSeg(p, piece.Pts[i], piece.Pts[i + 1]);
                if (d < bestD) { bestD = d; best = piece.SectionId; }
            }
        return best == null ? null : IL.Sections[best];
    }

    static float DistToSeg(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        float t = len2 == 0 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Clamp(t, 0f, 1f);
        float cx = a.X + t * dx, cy = a.Y + t * dy;
        return MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }

    // =================================================================
    //  МЫШЬ
    // =================================================================
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToDesign(e.Location);
        string text = "";
        var sig = SignalAt(p);
        var sw = sig == null ? SwitchAt(p) : null;
        var sec = sig == null && sw == null ? SectionAt(p) : null;
        LabTrack(sig, sw, sec);                  // [LAB]

        if (sig != null)
            text = $"{sig.Title}. Сейчас: {AspectInfo.Short(sig.Aspect)}. {AspectInfo.Meaning(sig.Aspect)}";
        else if (sw != null)
            text = $"Стрелка {sw.Label} ({sw.Title}): {(sw.Plus ? "плюс (прямо)" : "минус (на отклонение)")}" +
                   $"{(sw.Controlled ? "" : ", переводится")}{(sw.Locked ? ", заперта" : "")}";
        else if (sec != null)
            text = $"{sec.Label} — {sec.Title}: {(sec.Occupied ? "ЗАНЯТ" : "свободен")}" +
                   $"{(sec.LockedBy != null ? $", замкнут в маршруте {sec.LockedBy.Name}" : "")}";

        Cursor = sig != null || sw != null || (selected != null && sec != null) ? Cursors.Hand : Cursors.Default;
        if (text != hoverText) { hoverText = text; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        LabCommit();                             // [LAB]
        if (hoverText.Length > 0) { hoverText = ""; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var p = ToDesign(e.Location);
        var sig = SignalAt(p);
        var sw = sig == null ? SwitchAt(p) : null;

        // [LAB] клик по объекту = «показал на него»
        if (sig != null) StationContext.Point(PointedKind.Signal, sig.Id);
        else if (sw != null) StationContext.Point(PointedKind.Switch, sw.Id);
        else { var secLab = SectionAt(p); if (secLab != null) StationContext.Point(PointedKind.Section, secLab.Id); }

        if (e.Button == MouseButtons.Left)
        {
            if (sw != null) { IL.ToggleSwitch(sw); return; }
            if (sig != null) { SelectSignal(sig); return; }
            if (selected != null)
            {
                var sec = SectionAt(p);
                if (sec != null) SelectTarget(sec);
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (sig != null) { SignalMenu(sig).Show(this, e.Location); return; }
            if (sw != null) { SwitchMenu(sw).Show(this, e.Location); return; }
            var sec = SectionAt(p);
            if (sec != null)
            {
                sec.ManualOcc = !sec.ManualOcc;
                IL.Say($"Участок {sec.Label} {(sec.ManualOcc ? "занят (вручную)" : "освобождён (вручную)")}");
                IL.NotifyChanged();
            }
        }
    }

    void SelectSignal(RailSignal s)
    {
        if (selected == null)
        {
            if (!IL.Routes.Any(r => r.Start == s))
            {
                if (s.Remote != null)
                {
                    IL.Say($"{s.Label} — показание задаёт соседняя станция по сети (связь диспетчеров).", LogKind.Refuse);
                    return;
                }
                IL.Say(s.Kind switch
                {
                    SignalKind.Automatic => $"{s.Label} — проходной светофор автоблокировки, работает сам.",
                    SignalKind.Neighbor => $"{s.Label} — светофор соседней станции. Открыть его может только её дежурный (ПКМ → имитация).",
                    _ => $"От светофора {s.Label} маршрутов нет."
                }, LogKind.Refuse);
                return;
            }
            selected = s;
            bool shunt = ShuntMode();
            var ends = IL.Routes.Where(r => r.Start == s && r.Shunting == shunt).Select(r => r.Name).ToList();
            if (ends.Count == 0) ends = IL.Routes.Where(r => r.Start == s).Select(r => r.Name).ToList();
            IL.Say($"Выбран {s.Label}. Кликните светофор конца или путь назначения. Возможные маршруты: {string.Join("; ", ends)}");
            Invalidate();
            return;
        }
        if (selected == s)
        {
            selected = null;
            IL.Say("Выбор маршрута отменён");
            Invalidate();
            return;
        }
        SelectTarget(s);
    }

    void SelectTarget(object target)
    {
        var from = selected;
        selected = null;
        var route = AkzhtLayout.Find(IL.Routes, from, target, ShuntMode());
        if (route == null)
        {
            string t = target is RailSignal sg ? sg.Label : ((TrackSection)target).Label;
            IL.Say($"Маршрута от {from.Label} до {t} нет", LogKind.Refuse);
            Invalidate();
            return;
        }
        if (route.Active) IL.CancelRoute(route);
        else IL.SetRoute(route);
        Invalidate();
    }

    public void ClearSelection()
    {
        selected = null;
        Invalidate();
    }

    // ---------------------- контекстные меню ----------------------
    ContextMenuStrip SignalMenu(RailSignal s)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem($"{s.Label}: {AspectInfo.Short(s.Aspect)}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());

        if (s.Kind == SignalKind.Controlled)
        {
            foreach (var r in IL.Routes.Where(r => r.Start == s))
            {
                var route = r;
                string kind = route.Shunting ? "маневровый" : "поездной";
                if (route.Active) m.Items.Add($"Отменить: {route.Name} ({kind})", null, (_, _) => IL.CancelRoute(route));
                else m.Items.Add($"Задать: {route.Name} ({kind})", null, (_, _) => IL.SetRoute(route));
            }
            if (s.Has("white") && !s.IsShuntOnly && s.Lamps.Contains("yellow1"))
            {
                m.Items.Add(new ToolStripSeparator());
                if (s.InviteOn) m.Items.Add("Погасить пригласительный", null, (_, _) => IL.CancelInvite(s, "погашен дежурным"));
                else m.Items.Add("Пригласительный сигнал…", null, (_, _) => AskInvite(this, s));
            }
        }
        else if (s.Remote != null)
        {
            m.Items.Add(new ToolStripMenuItem("Показание приходит по сети от соседней станции") { Enabled = false });
        }
        else if (s.Kind == SignalKind.Neighbor)
        {
            m.Items.Add(s.NeighborOpen ? "Имитация: соседняя станция закрывает входной" : "Имитация: соседняя станция открывает входной (принимает поезд)",
                null, (_, _) =>
                {
                    s.NeighborOpen = !s.NeighborOpen;
                    IL.Say($"Имитация: соседняя станция {(s.NeighborOpen ? "открыла" : "закрыла")} входной {s.Label}");
                    IL.NotifyChanged();
                });
        }
        else
        {
            m.Items.Add(new ToolStripMenuItem("Проходной — работает автоматически по занятости") { Enabled = false });
        }
        return m;
    }

    ContextMenuStrip SwitchMenu(RailSwitch sw)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem($"Стрелка {sw.Label}: {(sw.Plus ? "плюс" : "минус")}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Перевести", null, (_, _) => IL.ToggleSwitch(sw));
        m.Items.Add("Вспомогательный перевод…", null, (_, _) => AskAuxSwitch(this, sw));
        return m;
    }

    // ---------------------- подтверждения (как у реального ДСП) ----------------------
    public static void AskInvite(IWin32Window owner, RailSignal s)
    {
        var res = MessageBox.Show(owner,
            $"Открыть ПРИГЛАСИТЕЛЬНЫЙ сигнал на светофоре {s.Label}?\n\n" +
            "Пригласительный открывают, когда обычный сигнал открыть нельзя (например, неисправна рельсовая цепь).\n" +
            "Дежурный обязан лично убедиться, что маршрут свободен и стрелки стоят правильно.\n" +
            "Поезд проследует со скоростью не более 20 км/ч.",
            "Ответственная команда", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (res == DialogResult.OK) IL.SetInvite(s);
    }

    public static void AskAuxSwitch(IWin32Window owner, RailSwitch sw)
    {
        var res = MessageBox.Show(owner,
            $"ВСПОМОГАТЕЛЬНЫЙ перевод стрелки {sw.Label}?\n\n" +
            "Применяется только при ложной занятости стрелочного участка.\n" +
            "Дежурный обязан лично убедиться, что на стрелке нет подвижного состава.\n" +
            "Команда записывается в журнал.",
            "Ответственная команда", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (res == DialogResult.OK) IL.ToggleSwitch(sw, aux: true);
    }
}
