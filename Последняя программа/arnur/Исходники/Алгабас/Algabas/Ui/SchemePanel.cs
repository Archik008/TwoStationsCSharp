using System.Drawing.Drawing2D;
using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

namespace StationApp;

// =====================================================================
//  ПАНЕЛЬ СХЕМЫ — общая для всех станций (копия из программы АКЖТ, файл SchemePanel.cs).
//  [ИЗМЕНЕНО 20.10.2026, правки преподавателя] интерфейс как у АКЖТ (версия 4.2 АКЖТ): крупные названия
//  участков, размеры подписей из scheme_style.ini, в упрощённом режиме упрощаются только светофоры.
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

    /// <summary>[ДОБАВЛЕНО 28.10.2026] Студент показал на объект схемы: задержал мышь на нём (0,25 с) или кликнул.
    /// Объект — RailSignal, RailSwitch или TrackSection. Нужно ЖИ-помощнику («для чего этот светофор?»).</summary>
    public static event Action<object> Pointed;
    object hoverObj;
    DateTime hoverSince;

    void CommitPointed()
    {
        if (hoverObj != null && (DateTime.Now - hoverSince).TotalMilliseconds >= 250) Pointed?.Invoke(hoverObj);
        hoverObj = null;
    }
    float scale = 1;
    PointF off;

    // ---- цвета (как у пульта Алгабаса) ----
    // [ИЗМЕНЕНО 28.10.2026] цвета берутся из палитры режима интерфейса (Ui/SchemePalette.cs);
    // в инженерном и учебном режимах — прежние
    static SchemePalette Pal => SchemePalette.Current;
    public static Color Bg => Pal.Bg;
    static Color TrackColor => Pal.Track;
    static Color RouteColor => Pal.Route;
    // [ИЗМЕНЕНО 12.10.2026] маневровые маршруты — жёлтым, как поездные (было голубым)
    static Color ShuntRouteColor => RouteColor;
    static Color OccColor => Pal.Occ;
    static Color ReleasingColor => Pal.Releasing;
    static Color LampOff => Pal.LampOff;
    static Color LampOffSimple => Pal.LampOffSimple;
    static Color TextDark => Pal.Text;
    static Color Dimmed => Pal.Dimmed;

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

    // ---- [ДОБАВЛЕНО 13.11.2026] настройка своих маршрутов («Станция по схеме», окно «Настройка»); у Алгабаса и АКЖТ — null ----
    /// <summary>Левый клик по светофору, стрелке или участку сначала отдаётся сюда; true — клик забран (маршрут не набирается).</summary>
    public Func<object, bool> EditClick = null;
    /// <summary>Черновик маршрута: эти участки (Id) рисуются жёлтым, эти светофоры — в жёлтой рамке.</summary>
    public HashSet<string> DraftSections = null;
    public HashSet<RailSignal> DraftSignals = null;

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

        // [ИЗМЕНЕНО 09.11.2026] Раньше каждая схема центрировалась по высоте СВОЕЙ панели. В рабочем режиме полоса
        // пульта у поста на ряд выше, чем у АКЖТ, панель поста начинается ниже — и пути I и II на стыке расходились.
        // Теперь вся группа центрируется по общей полосе экрана (от нижнего из верхних краёв до верхнего из нижних).
        float bandTop = 0, bandH = Height;
        if (group.Count > 1)
        {
            int myTop = PointToScreen(Point.Empty).Y;
            int top = group.Max(p => p.PointToScreen(Point.Empty).Y);
            int bottom = group.Min(p => p.PointToScreen(Point.Empty).Y + p.Height);
            if (bottom - top > 60) { bandTop = top - myTop; bandH = bottom - top; }
        }
        float usableH = bandH - (view.MinScale > 0 ? 30 : hintBoxH + 6);   // [ИЗМЕНЕНО 17.11.2026] было 30 — под одну строку подсказки
        scale = group.Min(p => Math.Min(p.Width / p.G.DesignW, usableH / designH));
        if (scale <= 0) scale = 1;
        if (view.MinScale > 0 && group.Count == 1) { ScrollTransform(scale); return; }   // [ДОБАВЛЕНО 12.11.2026]
        if (AutoScroll) { AutoScroll = false; AutoScrollMinSize = Size.Empty; }
        float freeW = Width - G.DesignW * scale;
        float offX = HAlign switch { StringAlignment.Near => 0, StringAlignment.Far => freeW, _ => freeW / 2f };
        off = new PointF(offX, bandTop + (usableH - designH * scale) / 2f);
    }

    // ---- [ДОБАВЛЕНО 12.11.2026] прокрутка вместо мелкого масштаба (StationView.MinScale > 0 — «Станция по схеме») ----
    /// <summary>Множитель Ctrl+колеса к начальному масштабу (1 — начальный: вписать в окно, но не мельче MinScale).</summary>
    float zoomMul = 1, lastBase = 1;
    const float HintH = 46;          // полоса подсказки внизу панели (до двух строк)

    /// <summary>Масштаб = вписанный в окно, но не мельче MinScale (и × Ctrl+колесо, не мельче вписанного); схема крупнее
    /// окна — полосы прокрутки, меньше — по центру.</summary>
    void ScrollTransform(float fit)
    {
        float baseScale = lastBase = Math.Max(fit, view.MinScale);
        scale = Math.Clamp(baseScale * zoomMul, fit, Math.Max(fit, 4f));
        if (!AutoScroll) AutoScroll = true;
        var need = new Size((int)Math.Ceiling(G.DesignW * scale), (int)Math.Ceiling(G.DesignH * scale + HintH));
        // полоса прокрутки отнимает место у другой стороны — сравниваем с окном без полос
        var full = Size;
        var ms = need.Width > full.Width || need.Height > full.Height ? need : Size.Empty;
        if (AutoScrollMinSize != ms) AutoScrollMinSize = ms;
        var cs = ClientSize;
        float fx = Math.Max(0, (cs.Width - G.DesignW * scale) / 2f);
        float fy = Math.Max(0, (cs.Height - HintH - G.DesignH * scale) / 2f);
        off = new PointF(AutoScrollPosition.X + fx, AutoScrollPosition.Y + fy);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    /// <summary>Колесо — вверх / вниз, Shift+колесо — влево / вправо, Ctrl+колесо — масштаб (точка под мышью остаётся на месте).</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (view.MinScale <= 0) { base.OnMouseWheel(e); return; }
        var sp = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);
        if ((ModifierKeys & Keys.Control) != 0)
        {
            UpdateTransform();
            var dp = ToDesign(e.Location);
            zoomMul *= e.Delta > 0 ? 1.15f : 1 / 1.15f;
            UpdateTransform();
            zoomMul = scale / lastBase;       // упёрлись в пределы (вписанный … 4) — множитель дальше не копится
            var cs = ClientSize;
            float fx = Math.Max(0, (cs.Width - G.DesignW * scale) / 2f);
            float fy = Math.Max(0, (cs.Height - HintH - G.DesignH * scale) / 2f);
            AutoScrollPosition = new Point((int)(dp.X * scale + fx - e.X), (int)(dp.Y * scale + fy - e.Y));
        }
        else if ((ModifierKeys & Keys.Shift) != 0)
            AutoScrollPosition = new Point(sp.X - e.Delta, sp.Y);
        else
            AutoScrollPosition = new Point(sp.X, sp.Y - e.Delta);
        if (e is HandledMouseEventArgs h) h.Handled = true;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (LevelGroup == null) return;
        foreach (var p in LevelGroup)
            if (p != this) p.Invalidate();      // масштаб соседа зависит и от нашего размера
    }

    /// <summary>[ДОБАВЛЕНО 09.11.2026] Панель сдвинулась (поменялась высота полосы над ней) — общая полоса группы другая.</summary>
    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        Invalidate();
        if (LevelGroup == null) return;
        foreach (var p in LevelGroup)
            if (p != this) p.Invalidate();
    }

    PointF ToDesign(Point p) => new((p.X - off.X) / scale, (p.Y - off.Y) / scale);

    /// <summary>[ДОБАВЛЕНО 18.11.2026] Для программы «ДНЦ»: невидимая копия схемы рисуется как настоящая —
    /// с выбранным светофором начала маршрута и тем же видом (упрощённый).</summary>
    public void CopyLook(RailSchemePanel from)
    {
        selected = from.selected;
        Simple = from.Simple;
        ShuntMode = from.ShuntMode;
    }

    /// <summary>[ДОБАВЛЕНО 18.11.2026] Что на схеме в точке чертежа: светофор или участок (null — ничего) — для ДНЦ.</summary>
    public object HitDesign(float x, float y)
    {
        var p = new PointF(x, y);
        return (object)SignalAt(p) ?? SectionAt(p);
    }

    /// <summary>[ДОБАВЛЕНО 18.11.2026] Точка чертежа → точка на панели (клиентские координаты): ДНЦ кликает по чертежу.</summary>
    public Point DesignToClient(float x, float y)
    {
        UpdateTransform();
        return new Point((int)Math.Round(off.X + x * scale), (int)Math.Round(off.Y + y * scale));
    }

    /// <summary>[ДОБАВЛЕНО 18.11.2026] Где на панели нарисована схема (клиентские координаты) — для программы «ДНЦ».</summary>
    public RectangleF DrawnRect
    {
        get
        {
            UpdateTransform();
            return new RectangleF(off.X, off.Y, G.DesignW * scale, G.DesignH * scale);
        }
    }

    // =================================================================
    //  РИСОВАНИЕ
    // =================================================================
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        MeasureHint(g);
        UpdateTransform();
        UpdateTargets();

        var st = g.Save();
        g.TranslateTransform(off.X, off.Y);
        g.ScaleTransform(scale, scale);
        if (G.AutoLabels) PlaceLabels(g);   // [ДОБАВЛЕНО 23.11.2026]

        DrawTexts(g);
        foreach (var c in G.Crossings) DrawCrossing(g, c);
        DrawTunnelsBack(g);
        DrawTracks(g);
        RadioUi.DrawRestrictions(g, G.Pieces.Select(p => (p.SectionId, p.Pts)), IL);   // [ДОБАВЛЕНО 23.10.2026, этап 4] предупреждения, «окна»
        ShuntUi.DrawPnp(g, G.Pieces.Select(p => (p.SectionId, p.Pts)), fSmallBold);   // [ДОБАВЛЕНО 23.10.2026, этап 7] ПНП и его лампы
        DrawBrakes(g);              // [ДОБАВЛЕНО 12.11.2026] тормозные позиции горки (только «Станция по схеме»)
        DrawSignsAndSwitchInds(g);  // [ДОБАВЛЕНО 20.11.2026] знаки у пути и стрелочные указатели (только «Станция по схеме»)
        DrawJointsAndBuffers(g);
        DrawTunnelsFront(g);
        DrawSwitchLabels(g);
        DrawSectionLabels(g);       // и в упрощённом режиме: упрощаются только светофоры
        foreach (var sg in G.Signals) DrawSignal(g, sg);
        DrawTrains(g);
        foreach (var d in G.Directions) DrawDirection(g, d);

        g.Restore(st);

        if (!UiSettings.Show(UiSettings.Hint) && hoverText.Length == 0) return;   // [ДОБАВЛЕНО 28.10.2026] подсказка скрыта в «Параметрах»
        using var hb = new SolidBrush(TextDark);
        string line = hoverText.Length > 0 ? hoverText : DefaultHint;
        // [ИЗМЕНЕНО 12.11.2026] ClientSize — без полос прокрутки (у Алгабаса полос нет, то же, что Width / Height)
        var cl = ClientSize;
        // [ИЗМЕНЕНО 17.11.2026] высота — по тексту: на узком окне подсказка в две строки (было 24 — вторая обрезалась)
        var hr = new RectangleF(8, cl.Height - hintBoxH - 2, cl.Width - 16, hintBoxH);
        if (view.MinScale > 0)
        {
            // [ДОБАВЛЕНО 12.11.2026] схема прокручивается под подсказкой — подсказке своя полоса фона, до двух строк
            var sz = g.MeasureString(L.T(line), fInfo, (int)hr.Width);
            float h = Math.Min(sz.Height, HintH - 4) + 2;
            hr = new RectangleF(8, cl.Height - h - 2, cl.Width - 16, h);
            using var bgb = new SolidBrush(Bg);
            g.FillRectangle(bgb, 0, hr.Y - 2, cl.Width, cl.Height - hr.Y + 2);
        }
        g.DrawString(L.T(line), fInfo, hb, hr);   // [ДОБАВЛЕНО 22.10.2026, этап 8] L.T — перевод
    }

    string DefaultHint =>
        "ЛКМ: светофор начала → светофор конца или путь назначения = маршрут  |  ЛКМ по номеру стрелки — перевод  |  " +
        "ПКМ по пути, светофору, стрелке или поезду — меню  |  обе кнопки мыши — положение стрелок" +
        (view.MinScale > 0 ? "  |  колесо, Shift+колесо — прокрутка, Ctrl+колесо — масштаб" : "");

    /// <summary>[ДОБАВЛЕНО 17.11.2026] Высота подсказки под схемой (обычная подсказка, в 1–2 строки по ширине панели).
    /// Схема вписывается в высоту без неё — на маленьком мониторе подсказка не наезжает на пути и не обрезается.</summary>
    float hintBoxH = 24;

    void MeasureHint(Graphics g)
    {
        float h = 24;
        if (UiSettings.Show(UiSettings.Hint))
        {
            var sz = g.MeasureString(L.T(DefaultHint), fInfo, Math.Max(50, ClientSize.Width - 16));
            h = Math.Clamp(sz.Height + 2, 24, 64);
        }
        hintBoxH = h;
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
            g.DrawString(L.T(t.Text), TextFont(t.Size, t.Bold), t.White ? w : d, t.Pos, sf);
        }
        foreach (var b in G.Boundaries) BoundarySign(g, b.X, b.Y);
    }

    void BoundarySign(Graphics g, float x, float y)
    {
        // [ИЗМЕНЕНО 09.11.2026] табличка всегда белая — текст и рамка всегда тёмные: в рабочем режиме цвет текста
        // схемы светлый (TextDark почти белый), и надпись на белом не была видна
        var ink = Color.FromArgb(25, 30, 30);
        using var p = new Pen(ink, 1.5f);
        using var b = new SolidBrush(Color.White);
        using var t = new SolidBrush(ink);
        var r = new RectangleF(x - 36, y, 72, 25);   // [ИЗМЕНЕНО 09.11.2026] было 22 — верх двух строк обрезался
        g.FillRectangle(b, r);
        g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        g.DrawString(L.T("граница станции"), fTiny, t, r, center);
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
        g.DrawString(L.T(closed ? "ПЕРЕЕЗД ЗАКРЫТ" : "Переезд"), fSmallBold, tb, x, y2 + 26, center);
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

    // [ДОБАВЛЕНО 23.11.2026] Набор маршрута: нажат светофор начала — всё, куда можно кликнуть вторым (светофоры конца
    // и пути назначения маршрутов от него, поездных и маневровых), мигает зелёным, чтобы было заметно (просьба Мади)
    static readonly Color TargetColor = Color.FromArgb(40, 230, 90);
    RailSignal targetsOf;
    readonly HashSet<string> targetSecs = new();
    readonly HashSet<RailSignal> targetSigs = new();
    bool TargetBlink => selected != null && Blink;

    void UpdateTargets()
    {
        if (targetsOf == selected) return;
        targetsOf = selected;
        targetSecs.Clear();
        targetSigs.Clear();
        if (selected == null) return;
        foreach (var r in IL.Routes)
        {
            if (r.Start != selected) continue;
            if (r.End != null) targetSigs.Add(r.End);
            if (r.Dest != null) targetSecs.Add(r.Dest.Id);
        }
        // и любой светофор на пути назначения (второй клик по нему тоже задаёт маршрут — RouteFinder)
        foreach (var r in IL.Routes)
        {
            if (r.Start != selected) continue;
            foreach (var sg in G.Signals)
                if (IL.Signals.TryGetValue(sg.SignalId, out var s) && s.At != null && s != selected && (s.At == r.Dest || s.At == r.ExitSection))
                    targetSigs.Add(s);
        }
    }

    // ---------------------- пути ----------------------
    // [ИЗМЕНЕНО 20.10.2026] setting — маршрут устанавливается, светофор ещё не открылся: мигает (как в Python TrainSt)
    Color PieceColor(TrackPiece p, out bool releasing, out bool setting)
    {
        releasing = false;
        setting = false;
        var sec = IL.Sections[p.SectionId];
        if (TargetBlink && targetSecs.Contains(p.SectionId)) return TargetColor;   // [23.11.2026] путь назначения — можно кликнуть
        if (DraftSections != null && DraftSections.Contains(p.SectionId)) return RouteColor;   // [ДОБАВЛЕНО 13.11.2026] черновик маршрута
        if (sec.Occupied) return OccColor;
        foreach (var r in IL.ActiveRoutes)
        {
            if (!r.Pieces.Contains(p.Id)) continue;
            if (r.Released.Contains(sec)) continue;
            if (r.Releasing) { releasing = true; return ReleasingColor; }
            setting = r.Setting;
            return r.Shunting ? ShuntRouteColor : RouteColor;
        }
        // [ДОБАВЛЕНО 12.10.2026] перегон в поездном маршруте на перегон (нашем или соседа) — до входного соседа
        if (sec.LineRoute) return RouteColor;
        return TrackColor;
    }

    /// <summary>[ДОБАВЛЕНО 11.11.2026] Пути, подписанные в разрыве линии (их подпись под путём не повторяем).</summary>
    readonly HashSet<string> gapLabeled = new();

    void DrawTracks(Graphics g)
    {
        // [ДОБАВЛЕНО 11.11.2026] название пути в разрыве «——▶ 3П ◀——» (РУ-30-80, стр. 89–90): на время рисования путей
        // вырезаем полоску вдоль линии; список заполняет только «Станция по схеме», у остальных он пустой
        gapLabeled.Clear();
        var gaps = new List<(TrackNameGap Gp, float TextHalf, float Arrow)>();
        foreach (var gp in G.TrackNameGaps)
        {
            float textHalf = g.MeasureString(gp.Text, fSection).Width / 2 + 3;
            if (textHalf + 12 <= gp.MaxHalf - 6) gaps.Add((gp, textHalf, 12));
            else if (textHalf <= gp.MaxHalf - 2) gaps.Add((gp, textHalf, 0));    // короткий путь: только название, без стрелок
        }
        Region clipSave = null;
        if (gaps.Count > 0)
        {
            clipSave = g.Clip;
            foreach (var (gp, th, ar) in gaps) g.ExcludeClip(new Rectangle((int)(gp.Center.X - th - ar), (int)(gp.Center.Y - 6), (int)(2 * (th + ar)), 12));
        }
        try { DrawTrackLines(g); }
        finally { if (clipSave != null) g.Clip = clipSave; }
        if (gaps.Count == 0) return;
        using var ab = new SolidBrush(TrackColor);
        using var tb = new SolidBrush(Pal.SectionLabel.IsEmpty ? SchemeStyle.SectionLabelColor : Pal.SectionLabel);
        foreach (var (gp, th, ar) in gaps)
        {
            float cx = gp.Center.X, cy = gp.Center.Y;
            if (ar > 0)
            {
                g.FillPolygon(ab, new[] { new PointF(cx - th - ar, cy - 5), new PointF(cx - th, cy), new PointF(cx - th - ar, cy + 5) });
                g.FillPolygon(ab, new[] { new PointF(cx + th + ar, cy - 5), new PointF(cx + th, cy), new PointF(cx + th + ar, cy + 5) });
            }
            g.DrawString(gp.Text, fSection, tb, gp.Center, center);
            gapLabeled.Add(gp.SectionId);
        }
    }

    void DrawTrackLines(Graphics g)
    {
        using (var dp = new Pen(TrackColor, 5f))
            foreach (var pts in G.Decor) g.DrawLines(dp, pts);

        // [ДОБАВЛЕНО 13.10.2026] хвост перегона за светофором соседа — жёлтый вместе с маршрутом на перегон
        foreach (var (pts, secId) in G.LineDecor)
        {
            var col = IL.Sections.TryGetValue(secId, out var ls) && ls.LineRoute ? RouteColor : TrackColor;
            using var lp = new Pen(col, 5f);
            g.DrawLines(lp, pts);
        }

        foreach (var p in G.Pieces)
        {
            var col = PieceColor(p, out bool releasing, out bool setting);
            if (releasing && !Blink) col = TrackColor;
            if (setting && !ModelHost.FastBlinkPhase) col = TrackColor;

            if (p.SwitchId == null)
            {
                using var pen = new Pen(col, 5f);
                g.DrawLines(pen, p.Pts);
                continue;
            }

            var sw = IL.Switches[p.SwitchId];
            bool activeBranch = sw.ShownPlus == p.StraightBranch;   // [ИЗМЕНЕНО 21.10.2026, этап 6] у стрелки на макете — макет
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

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Тормозная позиция — вагонный замедлитель: толстая тёмная полоса на пути с двумя
    /// светлыми шинами, каёмка — цвета пути (видно занятость и маршрут), подпись «ТП1» сбоку — как в редакторе.</summary>
    void DrawBrakes(Graphics g)
    {
        if (G.Brakes.Count == 0) return;
        const float bw = 10f;
        using var bar = new Pen(Color.FromArgb(0x14, 0x14, 0x14), bw);
        using var shoe = new Pen(Color.FromArgb(0x8c, 0x8c, 0x8c), 1.3f);
        using var lb = new SolidBrush(Pal.SwitchLabel.IsEmpty ? TextDark : Pal.SwitchLabel);
        foreach (var b in G.Brakes)
        {
            var piece = G.Pieces.FirstOrDefault(p => p.Id == b.PieceId);
            var col = TrackColor;
            if (piece != null)
            {
                col = PieceColor(piece, out bool releasing, out bool setting);
                if (releasing && !Blink) col = TrackColor;
                if (setting && !ModelHost.FastBlinkPhase) col = TrackColor;
            }
            PointF At(float k) => new(b.A.X + (b.B.X - b.A.X) * k, b.A.Y + (b.B.Y - b.A.Y) * k);
            using (var rim = new Pen(col, bw + 3f)) g.DrawLine(rim, At(0.14f), At(0.86f));
            g.DrawLine(bar, At(0.16f), At(0.84f));
            float dx = b.B.X - b.A.X, dy = b.B.Y - b.A.Y, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1) continue;
            float nx = -dy / len * bw * 0.22f, ny = dx / len * bw * 0.22f;
            var p1 = At(0.22f); var p2 = At(0.78f);
            foreach (var s in new[] { -1f, 1f })
                g.DrawLine(shoe, p1.X + s * nx, p1.Y + s * ny, p2.X + s * nx, p2.Y + s * ny);
        }
        foreach (var b in G.Brakes)
            if (!string.IsNullOrEmpty(b.Label)) g.DrawString(L.T(b.Label), fSw, lb, brakeAt.TryGetValue(b, out var bp) ? bp : b.LabelPos, center);
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
        foreach (var (label, (pt0, swId)) in G.SwitchLabels)
        {
            var pt = pt0;
            var sw = IL.Switches[swId];
            // [ИЗМЕНЕНО 21.10.2026, этап 6] пометки пульта: К — колпачок, З — заслонка, Л — замок/скоба, В — выключена, М — макет
            string marks = PanelUi.Marks(sw);
            string txt = label + sw.PosText + (marks.Length > 0 ? " " + marks : "");
            if (swAt.TryGetValue(label, out var placed)) pt = placed;   // [23.11.2026] место, свободное от путей и огней
            var size = g.MeasureString(txt, fSw);
            var rect = new RectangleF(pt.X - size.Width / 2 - 2, pt.Y - size.Height / 2, size.Width + 4, size.Height);
            if (PanelUi.ShowSwitchPositions)           // Е13: контроль положения всех стрелок
                using (var pb = new SolidBrush(sw.ShownPlus ? Color.FromArgb(90, 210, 110) : Color.FromArgb(255, 220, 60))) g.FillRectangle(pb, rect);
            else if (sw.Cut)
                using (var cb = new SolidBrush(Blink ? Color.FromArgb(230, 40, 40) : Color.FromArgb(255, 190, 190))) g.FillRectangle(cb, rect);
            else if (sw.Locked)
                using (var lb = new SolidBrush(Color.FromArgb(255, 225, 90))) g.FillRectangle(lb, rect);
            if (sw.RedCap)
                using (var rp = new Pen(Color.FromArgb(220, 0, 0), 2.5f)) g.DrawRectangle(rp, rect.X, rect.Y, rect.Width, rect.Height);
            Color swText = Pal.SwitchLabel.IsEmpty ? TextDark : Pal.SwitchLabel;   // [ИЗМЕНЕНО 28.10.2026] цвет режима
            Color tc = !sw.Controlled ? (Blink ? Color.FromArgb(200, 0, 0) : swText)
                     : sw.ShownPlus ? swText : Color.FromArgb(150, 50, 0);
            using var tb = new SolidBrush(tc);
            g.DrawString(L.T(txt), fSw, tb, pt, center);
        }
    }

    void DrawSectionLabels(Graphics g)
    {
        using var b = new SolidBrush(Pal.SectionLabel.IsEmpty ? SchemeStyle.SectionLabelColor : Pal.SectionLabel);   // [ИЗМЕНЕНО 28.10.2026] цвет режима
        // половина высоты букв + половина толщины пути + зазор
        float need = fSection.Size * 96f / 72f / 2f + 2.5f + SchemeStyle.SectionLabelGap;
        foreach (var (id, pt) in G.SectionLabels)
            if (!gapLabeled.Contains(id) && IL.Sections.TryGetValue(id, out var s))    // [11.11.2026] уже подписан в разрыве
                g.DrawString(s.Label, fSection, b, secAt.TryGetValue(id, out var sp) ? sp : AwayFromTrack(pt, need), center);
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

    // =================================================================
    //  [ДОБАВЛЕНО 23.11.2026] РАССТАНОВКА ПОДПИСЕЙ (G.AutoLabels — «Станция по схеме»)
    //  Схема из редактора строится по клеткам, и подписи, поставленные «по клетке», налезали на огни соседнего
    //  светофора («14+» под огнями М14), на пути (ТП1 на линии) и друг на друга. Как на ручной схеме Алгабаса:
    //  имя светофора — над огнями (у «→» — под огнями), номер стрелки — у остряка по сторону ответвления,
    //  название участка — у середины прямого куска. Каждая подпись пробует места по очереди, от лучшего
    //  (как на образце) к запасным — с другой стороны пути, сдвиг вдоль пути, сбоку от огней, —
    //  и берёт первое, где ничего нет; если свободного нет — где перекрытие меньше всего.
    //  Порядок: светофоры → стрелки → ТП → участки; поставленная подпись — сама препятствие для следующих.
    // =================================================================
    readonly Dictionary<string, PointF> sigAt = new(), swAt = new(), secAt = new();
    readonly Dictionary<string, RectangleF> sigBox = new();
    readonly Dictionary<BrakeGeom, PointF> brakeAt = new();
    readonly List<RectangleF> lblBoxes = new();
    readonly List<(PointF A, PointF B)> lblSegs = new();
    readonly HashSet<string> sigFlip = new();
    string placedKey;

    Font SigFont(RailSignal s) => s.Kind == SignalKind.Automatic || s.IsShuntOnly ? fSigSmall : fSig;

    /// <summary>Размер подписи без полей MeasureString (по высоте — буквы, без межстрочного запаса).</summary>
    static SizeF TextBox(Graphics g, string text, Font f)
    {
        var sz = g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic);
        return new SizeF(sz.Width + 4, f.Size * 96f / 72f * 0.82f + 2);
    }

    static RectangleF Around(PointF c, SizeF sz) => new(c.X - sz.Width / 2, c.Y - sz.Height / 2, sz.Width, sz.Height);

    void PlaceLabels(Graphics g)
    {
        // меняются: режим (простые огни), тексты стрелок (+/−, пометки), язык
        var key = new System.Text.StringBuilder(Simple ? "s" : "f").Append(L.Current).Append(G.GetHashCode());
        foreach (var (label, (_, swId)) in G.SwitchLabels)
            if (IL.Switches.TryGetValue(swId, out var sw)) key.Append('|').Append(label).Append(sw.PosText).Append(PanelUi.Marks(sw));
        if (key.ToString() == placedKey) return;
        placedKey = key.ToString();
        sigAt.Clear(); swAt.Clear(); secAt.Clear(); sigBox.Clear(); brakeAt.Clear(); sigFlip.Clear();
        lblBoxes.Clear(); lblSegs.Clear();

        // ---- препятствия: пути, огни, упоры, надписи схемы ----
        foreach (var p in G.Pieces) for (int i = 0; i + 1 < p.Pts.Length; i++) lblSegs.Add((p.Pts[i], p.Pts[i + 1]));
        foreach (var d in G.Decor) for (int i = 0; i + 1 < d.Length; i++) lblSegs.Add((d[i], d[i + 1]));
        foreach (var (d, _) in G.LineDecor) for (int i = 0; i + 1 < d.Length; i++) lblSegs.Add((d[i], d[i + 1]));
        foreach (var b in G.Buffers) lblBoxes.Add(new RectangleF(b.X - 4, b.Y - 17, 8, 34));
        foreach (var b in G.Boundaries) lblBoxes.Add(new RectangleF(b.X - 36, b.Y, 72, 25));
        foreach (var c in G.Crossings) lblBoxes.Add(RectangleF.FromLTRB(c.X - 32, c.Y1 - 50, c.X + 32, c.Y2 + 36));
        foreach (var t in G.Texts)
        {
            var sz = TextBox(g, L.T(t.Text), TextFont(t.Size, t.Bold));
            float x = t.Align switch { StringAlignment.Center => t.Pos.X - sz.Width / 2, StringAlignment.Far => t.Pos.X - sz.Width, _ => t.Pos.X };
            lblBoxes.Add(new RectangleF(x, t.Pos.Y - sz.Height / 2, sz.Width, sz.Height));
        }
        foreach (var d in G.Directions)
        {
            lblSegs.Add((new PointF(d.ArrowX1, d.ArrowY), new PointF(d.ArrowX2, d.ArrowY)));
            lblBoxes.Add(Around(d.Pos, TextBox(g, L.T(d.Line.Title + ", " + d.RightText), fSmallBold)));
        }
        var lamps = new Dictionary<string, (RectangleF Body, float Cx, float Y, float End, float Dir)>();
        var tracks = lblSegs.ToList();
        int TrackHits(RectangleF body) => tracks.Count(t => SegHitsRect(t.A, t.B, RectangleF.Inflate(body, 2.5f, 2.5f)));
        foreach (var sg in G.Signals)
        {
            if (!IL.Signals.TryGetValue(sg.SignalId, out var s)) continue;
            int n = DrawnLamps(s);
            float r = Simple ? SimpleLampR : LampR;
            RectangleF Body(out PointF l0, out float dir, out float y, out float far)
            {
                (l0, dir, y) = LampBase(sg);
                far = l0.X + dir * ((n - 1) * Step + r + (s.Indicator ? 22 : 0));
                return RectangleF.FromLTRB(Math.Min(l0.X - dir * r, far), y - r - 1, Math.Max(l0.X - dir * r, far), y + r + 1);
            }
            var body = Body(out var l0, out var dir, out var y, out var far);
            int hits = TrackHits(body);
            if (hits > 0)
            {
                // огни на соседнем пути (стрелка, съезд рядом с мачтой) — пробуем по другую сторону мачты
                sigFlip.Add(sg.SignalId);
                var body2 = Body(out var l02, out var dir2, out var y2, out var far2);
                if (TrackHits(body2) < hits) (body, l0, dir, y, far) = (body2, l02, dir2, y2, far2);
                else sigFlip.Remove(sg.SignalId);
            }
            lamps[sg.SignalId] = (body, l0.X + dir * (n - 1) * Step / 2f, y, far, dir);
            lblBoxes.Add(body);
            lblSegs.Add((new PointF(sg.Foot.X, sg.Foot.Y + (sg.Above ? -6 : 6)), new PointF(sg.Foot.X, y)));   // мачта
        }

        // ---- светофоры: над огнями (у «→» — под), дальше, сбоку от огней, сдвиг ----
        foreach (var sg in G.Signals)
        {
            if (!lamps.TryGetValue(sg.SignalId, out var lm)) continue;
            var s = IL.Signals[sg.SignalId];
            var sz = TextBox(g, s.Label + sg.Suffix, SigFont(s));
            float o = sg.Above ? -1 : 1, near = lm.Body.Height / 2 + sz.Height / 2 + 1;
            var c = new List<PointF> { new(lm.Cx, lm.Y + o * 22) };
            foreach (var dx in new[] { 8f, -8f, 16f, -16f }) c.Add(new(lm.Cx + dx, lm.Y + o * 22));
            c.Add(new(lm.End + lm.Dir * (sz.Width / 2 + 3), lm.Y));                       // за последним огнём
            c.Add(new(lm.Body.X + (lm.Dir < 0 ? lm.Body.Width + sz.Width / 2 + 3 : -sz.Width / 2 - 3), lm.Y));   // у мачты
            foreach (var dy in new[] { 30f, 38f, 46f })
                foreach (var dx in new[] { 0f, 12f, -12f, 24f, -24f }) c.Add(new(lm.Cx + dx, lm.Y + o * dy));
            c.Add(new(lm.Cx, lm.Y - o * near));                                             // с другой стороны огней
            var at = Pick(sz, c);
            sigAt[sg.SignalId] = at;
            sigBox[sg.SignalId] = Around(at, sz);
        }

        // ---- стрелки: у остряка по сторону ответвления, сдвиг вдоль пути, по другую сторону, дальше ----
        foreach (var (label, (pt, swId)) in G.SwitchLabels)
        {
            if (!IL.Switches.TryGetValue(swId, out var sw)) continue;
            string marks = PanelUi.Marks(sw);
            var sz = TextBox(g, L.T(label + sw.PosText + (marks.Length > 0 ? " " + marks : "")), fSw);
            var tip = SwitchPoint(swId, pt);
            float side = pt.Y < tip.Y ? -1 : 1, dy0 = Math.Max(Math.Abs(pt.Y - tip.Y), sz.Height / 2 + 5);
            var c = new List<PointF>();
            foreach (var (sgn, dy) in new[] { (side, dy0), (-side, dy0), (side, dy0 + 10), (-side, dy0 + 10), (side, dy0 + 20) })
                foreach (var dx in new[] { 0f, -10f, 10f, -20f, 20f, -32f, 32f, -46f, 46f })
                    c.Add(new(pt.X + dx, tip.Y + sgn * dy));
            swAt[label] = Pick(sz, c);
        }

        // ---- тормозные позиции: сбоку от полосы, с любой стороны ----
        foreach (var b in G.Brakes)
        {
            if (string.IsNullOrEmpty(b.Label)) continue;
            var sz = TextBox(g, L.T(b.Label), fSw);
            float dx = b.B.X - b.A.X, dy = b.B.Y - b.A.Y, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1) continue;
            float nx = -dy / len, ny = dx / len;
            if (ny < 0 || (ny == 0 && nx < 0)) { nx = -nx; ny = -ny; }
            var mid = new PointF((b.A.X + b.B.X) / 2, (b.A.Y + b.B.Y) / 2);
            var c = new List<PointF>();
            foreach (var k in new[] { 1f, -1f })
                foreach (var dist in new[] { 18f, 26f, 34f })
                {
                    float need = dist + Math.Abs(nx) * sz.Width / 2 + Math.Abs(ny) * sz.Height / 2 - 8;   // не на полосе замедлителя
                    c.Add(new(mid.X + k * nx * need, mid.Y + k * ny * need));
                }
            brakeAt[b] = Pick(sz, c);
        }

        // ---- участки (галочка «Подписи участков и путей»): у середины куска, с другой стороны, сдвиг вдоль ----
        float needSec = fSection.Size * 96f / 72f / 2f + 2.5f + SchemeStyle.SectionLabelGap;
        foreach (var (id, pt0) in G.SectionLabels)
        {
            if (!IL.Sections.TryGetValue(id, out var s)) continue;
            if (G.TrackNameGaps.Any(x => x.SectionId == id)) continue;   // подписан в разрыве пути — его место на линии
            var sz = TextBox(g, s.Label, fSection);
            var pt = AwayFromTrack(pt0, needSec);
            var c = new List<PointF>();
            float trackY = pt0.Y - 16;   // AutoSectionLabels: на 16 ниже середины куска
            foreach (var y in new[] { pt.Y, 2 * trackY - pt.Y })
                foreach (var dx in new[] { 0f, -15f, 15f, -30f, 30f, -50f, 50f }) c.Add(new(pt.X + dx, y));
            secAt[id] = Pick(sz, c);
        }
    }

    /// <summary>Остряк стрелки: начало ветки, ближайшее к подписи (у съезда веток несколько).</summary>
    PointF SwitchPoint(string swId, PointF near)
    {
        PointF best = near; float bd = float.MaxValue;
        foreach (var p in G.Pieces)
        {
            if (p.SwitchId != swId || p.Pts.Length == 0) continue;
            var q = p.PointAtStart ? p.Pts[0] : p.Pts[^1];
            float d = (q.X - near.X) * (q.X - near.X) + (q.Y - near.Y) * (q.Y - near.Y);
            if (d < bd) { bd = d; best = q; }
        }
        return best;
    }

    /// <summary>Первое место без перекрытий; нет такого — с самым малым. Выбранное место становится препятствием.</summary>
    PointF Pick(SizeF sz, List<PointF> cands)
    {
        PointF best = cands[0];
        float bestCost = float.MaxValue;
        foreach (var c in cands)
        {
            float k = LabelCost(Around(c, sz));
            if (k < bestCost) { bestCost = k; best = c; }
            if (k <= 0) break;
        }
        lblBoxes.Add(Around(best, sz));
        return best;
    }

    float LabelCost(RectangleF r)
    {
        float cost = 0;
        foreach (var b in lblBoxes)
            if (r.IntersectsWith(b)) { var i = RectangleF.Intersect(r, b); cost += 20 + i.Width * i.Height; }
        var thick = RectangleF.Inflate(r, 3.5f, 3.5f);   // пол-толщины пути и зазор
        foreach (var (a, b) in lblSegs)
            if (SegHitsRect(a, b, thick)) cost += 400;
        if (r.Left < 0 || r.Top < 0 || r.Right > G.DesignW || r.Bottom > G.DesignH) cost += 100;
        return cost;
    }

    /// <summary>Отрезок задевает прямоугольник (отсечение Лианга — Барски).</summary>
    static bool SegHitsRect(PointF a, PointF b, RectangleF r)
    {
        float t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        bool Clip(float p, float q)
        {
            if (p == 0) return q >= 0;
            float t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
        return Clip(-dx, a.X - r.Left) && Clip(dx, r.Right - a.X) && Clip(-dy, a.Y - r.Top) && Clip(dy, r.Bottom - a.Y);
    }

    // ---------------------- светофоры ----------------------
    (PointF lamp0, float dir, float lampY) LampBase(SignalGeom sg)
    {
        float dir = sg.LampsToLeft ? -1 : 1;
        if (sigFlip.Contains(sg.SignalId)) dir = -dir;   // [23.11.2026] огни легли бы на соседний путь — по другую сторону мачты
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

    // ---- [ДОБАВЛЕНО 20.11.2026] головки и «окно» на мачте (маршрутный указатель, зелёная полоса) — как в конструкторе станций:
    // окно между пригласительным (лунно-белый у кронштейна) и основными огнями, без пригласительного — у кронштейна ----
    const float HeadGap = 6f, IndBox = 16f, StripW = 6f;

    // [ИЗМЕНЕНО 20.11.2026] в окне первым (у кронштейна, под огнями) — световой указатель «Н» горочного и ПГ
    // (РУ-30-80 листы 45–46: «Г ⊢□◎○●⊘»), и в упрощённом режиме; полос — одна или две. У Алгабаса и АКЖТ этого нет — без изменений.
    static int Strips(RailSignal s) => s.Strip ? Math.Max(1, s.StripCount) : 0;

    float SlotW(RailSignal s)
    {
        float w = s.Indicator ? 2 + IndBox : 0;
        if (!Simple) w += (s.RouteInd && !s.RouteIndOwnMast ? 2 + IndBox : 0) + Strips(s) * (2 + StripW);
        return w > 0 ? w + 4 : 0;
    }

    /// <summary>Перед какой лампой (от кронштейна) окно на мачте; -1 — окна нет.</summary>
    int SlotAt(RailSignal s)
    {
        if (SlotW(s) == 0) return -1;
        if (Simple || s.Indicator) return 0;
        int n = s.Lamps.Length;
        bool inv = s.Kind == SignalKind.Controlled && !s.IsShuntOnly && n >= 3 && s.Lamps[n - 1] == "white";
        int at = inv ? 1 : 0;
        if (s.Heads != null && at > 0)
        {
            int acc = 0;
            foreach (var p in s.Heads) { acc += p; if (acc >= at && acc < n) { at = acc; break; } }
        }
        return at;
    }

    /// <summary>Смещение i-й лампы (от кронштейна) от первой вдоль ряда: шаг, промежутки между головками, окно.</summary>
    float LampAlong(RailSignal s, int i)
    {
        if (Simple) return i * SimpleLampStep + (SlotAt(s) == 0 ? SlotW(s) : 0);    // [20.11.2026] окно с «Н» у кронштейна
        float o = i * LampStep;
        if (s.Heads != null)
        {
            int acc = 0;
            foreach (var p in s.Heads) { acc += p; if (acc <= i && acc < s.Lamps.Length) o += HeadGap; }
        }
        int slot = SlotAt(s);
        if (slot >= 0 && i >= slot) o += SlotW(s);
        return o;
    }

    RectangleF SignalRect(SignalGeom sg)
    {
        var s = IL.Signals[sg.SignalId];
        var (l0, dir, y) = LampBase(sg);
        int n = DrawnLamps(s);
        float r = Simple ? SimpleLampR : LampR;
        float x1 = l0.X - dir * r, x2 = l0.X + dir * (LampAlong(s, n - 1) + r);
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

        // [20.11.2026] в упрощённом режиме маневровый — по-прежнему одна лампа (головки видны в обычном режиме)
        int slot = SlotAt(s);
        if (Simple)
        {
            Color c = LampOffSimple;
            switch (s.Aspect)
            {
                case Aspect.Red: case Aspect.Invite: case Aspect.RedBack: c = LampColor(s.Has("red") ? "red" : "blue"); break;
                case Aspect.Green: case Aspect.YellowGreen: c = LampColor("green"); break;
                case Aspect.GreenBlinkYellow: case Aspect.GreenBlink: c = Blink ? LampColor("green") : LampOffSimple; break;    // [23.11.2026]
                case Aspect.Yellow: case Aspect.TwoYellow: c = LampColor("yellow"); break;
                case Aspect.YellowBlink: case Aspect.TwoYellowTopBlink: c = Blink ? LampColor("yellow") : LampOffSimple; break;
                case Aspect.White: c = LampColor("white"); break;
                case Aspect.Blue: c = LampColor("blue"); break;
            }
            if (IL.TestMode) c = Color.White;
            // [20.11.2026] окно с «Н» у кронштейна — лампы сдвинуты на LampAlong; мачта через окно
            if (slot >= 0)
                using (var mp = new Pen(Color.White, 2f)) g.DrawLine(mp, l0.X - dir * LampR, y, l0.X + dir * LampAlong(s, 0), y);
            Lamp(g, new PointF(l0.X + dir * LampAlong(s, 0), y), c, SimpleLampR);

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
                Lamp(g, new PointF(l0.X + dir * LampAlong(s, 1), y), c2, SimpleLampR);
            }
            else if (s.Aspect == Aspect.Invite && Blink)
                Lamp(g, new PointF(l0.X + dir * LampAlong(s, 1), y), LampColor("white"), 5f);
        }
        else
        {
            int nL = s.Lamps.Length;
            float lr = LampR;
            // [ДОБАВЛЕНО 20.11.2026] мачта через промежутки между головками и окно — головки не «висят» в воздухе
            if (s.Heads != null || slot >= 0)
                using (var mp = new Pen(Color.White, 2f)) g.DrawLine(mp, l0.X - dir * LampR, y, l0.X + dir * LampAlong(s, nL - 1), y);
            // [ДОБАВЛЕНО 20.11.2026] головки: скруглённый корпус у каждой группы; пригласительный отдельно — маленький
            bool smallInv = s.Heads is { Length: > 1 } && s.Heads[0] == 1 && nL >= 3 && s.Lamps[nL - 1] == "white" && s.Kind == SignalKind.Controlled;
            if (s.Heads != null)
            {
                using var hb = new SolidBrush(Color.FromArgb(30, 34, 33));
                int i0 = 0;
                foreach (var p in s.Heads)
                {
                    float a0 = l0.X + dir * LampAlong(s, i0), a1 = l0.X + dir * LampAlong(s, i0 + p - 1);
                    float pad = smallInv && i0 == 0 ? lr * 0.95f : lr + 2.5f;
                    float hx0 = Math.Min(a0, a1) - pad, hx1 = Math.Max(a0, a1) + pad, hh = 2 * pad;
                    using var path = new GraphicsPath();
                    path.AddArc(hx0, y - pad, hh, hh, 90, 180);
                    path.AddArc(hx1 - hh, y - pad, hh, hh, 270, 180);
                    path.CloseFigure();
                    g.FillPath(hb, path);
                    i0 += p;
                }
            }
            // Lamps перечислены сверху вниз по мачте; на схеме нижняя лампа ближе всего к кронштейну
            for (int i = 0; i < nL; i++)
            {
                var name = s.Lamps[nL - 1 - i];
                bool on = IL.TestMode || s.IsLampOn(name, Blink);
                Lamp(g, new PointF(l0.X + dir * LampAlong(s, i), y), on ? LampColor(name) : LampOff, smallInv && i == 0 ? lr * 0.75f : lr);
            }
        }
        // [ДОБАВЛЕНО 20.11.2026] окно на мачте у кронштейна или над пригласительным: указатель «Н» горочного / ПГ, маршрутный
        // указатель, затем зелёные полосы (поперёк ряда огней); в упрощённом режиме — только «Н»
        if (slot >= 0)
        {
            float start = slot == 0 ? l0.X - dir * LampR : l0.X + dir * (LampAlong(s, slot - 1) + LampR);
            float cur = 0;
            if (s.Indicator)
            {
                cur += 2;
                var ic = new PointF(start + dir * (cur + IndBox / 2), y);
                var box = new RectangleF(ic.X - IndBox / 2, ic.Y - IndBox / 2, IndBox, IndBox);
                using (var bb = new SolidBrush(Color.FromArgb(25, 25, 25))) g.FillRectangle(bb, box);
                using (var bp = new Pen(Color.FromArgb(20, 20, 20), 1.2f)) g.DrawRectangle(bp, box.X, box.Y, box.Width, box.Height);
                if (s.Aspect == Aspect.RedBack || IL.TestMode) g.DrawString("Н", fSigSmall, Brushes.White, ic, center);
                cur += IndBox;
            }
            if (!Simple && s.RouteInd && !s.RouteIndOwnMast) { cur += 2; DrawRouteInd(g, s, new PointF(start + dir * (cur + IndBox / 2), y)); cur += IndBox; }
            if (!Simple)
                for (int j = 0; j < Strips(s); j++)
                {
                    cur += 2;
                    float sx0 = start + dir * cur, sx1 = start + dir * (cur + StripW);
                    var bar = RectangleF.FromLTRB(Math.Min(sx0, sx1), y - LampR * 1.2f, Math.Max(sx0, sx1), y + LampR * 1.2f);
                    // [ИЗМЕНЕНО 23.11.2026] полоса горит, когда её зажёг расчёт показания (StripLit), и при проверке ламп
                    using (var sb = new SolidBrush(IL.TestMode || j < s.StripLit ? LampColor("green") : Color.FromArgb(20, 70, 40))) g.FillRectangle(sb, bar);
                    using (var spn = new Pen(Color.FromArgb(20, 20, 20), 1f)) g.DrawRectangle(spn, bar.X, bar.Y, bar.Width, bar.Height);
                    cur += StripW;
                }
        }
        // [ДОБАВЛЕНО 20.11.2026] маршрутный указатель на отдельной мачте — позади светофора (Инструкция РК п. 70)
        if (s.RouteInd && s.RouteIndOwnMast)
        {
            float mx = sg.Foot.X - dir * 26;
            float ty = sg.Foot.Y + (sg.Above ? -6 : 6);
            using (var mp = new Pen(Color.White, 2f)) { g.DrawLine(mp, mx, ty, mx, y); g.DrawLine(mp, mx, y, mx + dir * 4, y); }
            DrawRouteInd(g, s, new PointF(mx + dir * (4 + IndBox / 2), y));
        }
        else if (s.RouteInd && Simple)
            DrawRouteInd(g, s, new PointF(l0.X + dir * (LampAlong(s, DrawnLamps(s) - 1) + SimpleLampR + 2 + IndBox / 2), y));
        var rect = SignalRect(sg);
        if (DraftSignals != null && DraftSignals.Contains(s))     // [ДОБАВЛЕНО 13.11.2026] светофор в черновике маршрута
        {
            using var dp = new Pen(Color.FromArgb(255, 230, 60), 2.5f);
            g.DrawRectangle(dp, rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
        }
        if (TargetBlink && targetSigs.Contains(s))   // [23.11.2026] светофор конца — можно кликнуть
        {
            using var tp = new Pen(TargetColor, 2.5f);
            g.DrawRectangle(tp, rect.X, rect.Y, rect.Width, rect.Height);
        }
        if (selected == s && Blink)
        {
            using var hp = new Pen(Color.White, 2f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(hp, rect.X, rect.Y, rect.Width, rect.Height);
        }

        int n = DrawnLamps(s);
        float cx = l0.X + dir * (LampAlong(s, 0) + LampAlong(s, n - 1)) / 2f;    // [20.11.2026] с головками и окном
        float ly = sg.Above ? y - 22 : y + 22;
        Color tc = Color.White;
        if (selected != null && selected != s)
            tc = targetSigs.Contains(s) ? (Blink ? TargetColor : Color.White) : Dimmed;   // [23.11.2026] мигает зелёным (было светло-жёлтым)
        if (s.Kind == SignalKind.Automatic && s.Aspect == Aspect.Dark) tc = Dimmed;
        using var tb = new SolidBrush(tc);
        var font = SigFont(s);
        if (sigAt.TryGetValue(sg.SignalId, out var lp)) (cx, ly) = (lp.X, lp.Y);   // [23.11.2026] место, свободное от путей
        g.DrawString(s.Label + sg.Suffix, font, tb, cx, ly, center);
    }

    /// <summary>[ДОБАВЛЕНО 20.11.2026] Маршрутный указатель: тёмный квадрат, при открытом маршруте — номер пути
    /// (зелёный у выходного и маршрутного, белый у входного и маневрового); при проверке ламп — «8».</summary>
    void DrawRouteInd(Graphics g, RailSignal s, PointF c)
    {
        var box = new RectangleF(c.X - IndBox / 2, c.Y - IndBox / 2, IndBox, IndBox);
        var col = s.RouteIndGreen ? LampColor("green") : LampColor("white");
        using (var bb = new SolidBrush(Color.FromArgb(25, 25, 25))) g.FillRectangle(bb, box);
        using (var bp = new Pen(col, 1.4f)) g.DrawRectangle(bp, box.X, box.Y, box.Width, box.Height);
        string txt = IL.TestMode ? "8" : s.RouteIndText?.Invoke();
        if (!string.IsNullOrEmpty(txt))
            using (var tb = new SolidBrush(col)) g.DrawString(txt, txt.Length > 2 ? fTiny : fSigSmall, tb, c, center);
    }

    /// <summary>[ДОБАВЛЕНО 20.11.2026] Знаки у пути (предельный столбик, ГС, ГБ) и стрелочные указатели нецентрализованных стрелок.</summary>
    void DrawSignsAndSwitchInds(Graphics g)
    {
        if (G.Signs.Count == 0 && G.SwitchInds.Count == 0) return;
        using var dark = new Pen(Color.FromArgb(20, 20, 20), 1.4f);
        using var db = new SolidBrush(Color.FromArgb(20, 20, 20));
        foreach (var sg in G.Signs)
        {
            var p = sg.Pos;
            if (sg.Type == "limit")
            {
                // предельный столбик: белый с чёрной верхушкой, у пути
                var post = new RectangleF(p.X - 2.5f, p.Y + 6, 5, 14);
                g.FillRectangle(Brushes.White, post);
                g.FillRectangle(db, post.X, post.Y, post.Width, 5);
                g.DrawRectangle(dark, post.X, post.Y, post.Width, post.Height);
                continue;
            }
            // табличка на стойке справа по ходу: «→» — под путём, «←» — над путём
            var plate = new RectangleF(p.X - 15, sg.Below ? p.Y + 14 : p.Y - 28, 30, 14);
            using (var wp = new Pen(Color.White, 1.6f)) g.DrawLine(wp, p.X, sg.Below ? p.Y + 4 : p.Y - 4, p.X, sg.Below ? plate.Top : plate.Bottom);
            g.FillRectangle(Brushes.White, plate);
            g.DrawRectangle(dark, plate.X, plate.Y, plate.Width, plate.Height);
            g.DrawString(sg.Text, fTiny, db, new PointF(p.X, plate.Y + plate.Height / 2), center);
        }
        foreach (var si in G.SwitchInds)
        {
            if (!IL.Switches.TryGetValue(si.SwitchId, out var sw)) continue;
            bool plus = sw.ShownPlus;
            float w = plus ? 5 : 13, h = 12;
            var r = new RectangleF(si.Pos.X - w / 2, si.Pos.Y - h / 2, w, h);
            using (var b = new SolidBrush(plus ? Color.White : LampColor("yellow"))) g.FillRectangle(b, r);
            g.DrawRectangle(dark, r.X, r.Y, r.Width, r.Height);
        }
    }

    static void Lamp(Graphics g, PointF c, Color color, float r)
    {
        using var b = new SolidBrush(color);
        using var p = new Pen(Color.FromArgb(20, 20, 20), 1.2f);
        g.FillEllipse(b, c.X - r, c.Y - r, r * 2, r * 2);
        g.DrawEllipse(p, c.X - r, c.Y - r, r * 2, r * 2);
    }

    // ---------------------- поезда и направление ----------------------
    /// <summary>[ДОБАВЛЕНО 20.10.2026] Где нарисованы ярлыки поездов (в проектных координатах) — для ПКМ и подсказки.</summary>
    readonly List<(VirtualTrain train, RectangleF rect)> trainRects = new();

    void DrawTrains(Graphics g)
    {
        trainRects.Clear();
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
            // [ИЗМЕНЕНО 20.10.2026] маневровый состав — с буквой «М» и другим цветом
            string txt = t.NoLoco ? $"ваг. {t.Consist.Count}{(t.Consist.Any(w => w.Shoe) ? " Б" : "")}"   // [ДОБАВЛЕНО 23.10.2026, этап 7] вагоны без локомотива
                                  : $"{(t.Shunting ? "М " : "")}№{t.Number} {(t.Dir == Dir.Right ? "→" : "←")}";
            var size = g.MeasureString(txt, fSmallBold);
            var r = new RectangleF(mid.X - size.Width / 2 - 4, mid.Y - 11 - 22, size.Width + 8, 22);
            trainRects.Add((t, r));
            using var bb = new SolidBrush(t.Waiting.Length > 0 ? Color.FromArgb(120, 40, 40)
                                         : t.Shunting ? Color.FromArgb(20, 110, 100) : Color.FromArgb(25, 60, 140));
            using var tb = new SolidBrush(Color.White);
            g.FillRectangle(bb, r);
            g.DrawString(L.T(txt), fSmallBold, tb, r, center);
        }
        // [ДОБАВЛЕНО 23.10.2026, этап 4] номер настоящего поезда на макете — на первом его участке
        foreach (var grp in IL.Sections.Values.Where(s => s.HwTrainNo > 0 && s.TrainCount == 0).GroupBy(s => s.HwTrainNo))
        {
            var piece = grp.Select(s => G.Pieces.FirstOrDefault(p => p.SectionId == s.Id)).FirstOrDefault(p => p != null);
            if (piece == null) continue;
            var mid = new PointF((piece.Pts[0].X + piece.Pts[^1].X) / 2, (piece.Pts[0].Y + piece.Pts[^1].Y) / 2);
            string txt = $"№{grp.Key} (макет)";
            var size = g.MeasureString(L.T(txt), fSmallBold);
            var r = new RectangleF(mid.X - size.Width / 2 - 4, mid.Y - 33, size.Width + 8, 22);
            using var bb = new SolidBrush(Color.FromArgb(90, 60, 130));
            using var tb = new SolidBrush(Color.White);
            g.FillRectangle(bb, r);
            g.DrawString(L.T(txt), fSmallBold, tb, r, center);
        }
    }

    void DrawDirection(Graphics g, DirectionGeom d)
    {
        var line = d.Line;
        using var tb = new SolidBrush(TextDark);
        string text = line.Title + ", " + (line.Current == Dir.Right ? d.RightText : d.LeftText);
        if (line.AuxRunning) text += $"  (ВСП смена: {Math.Ceiling(line.AuxTimer)} с)";
        g.DrawString(L.T(text), fSmallBold, tb, d.Pos, center);
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
            if (SignalRect(sg).Contains(p) || (sigBox.TryGetValue(sg.SignalId, out var lb) && lb.Contains(p))) return IL.Signals[sg.SignalId];
        return null;
    }

    RailSwitch SwitchAt(PointF p)
    {
        foreach (var (label, (pt0, swId)) in G.SwitchLabels)
        {
            var pt = swAt.TryGetValue(label, out var q) ? q : pt0;   // [23.11.2026] подпись могла переехать
            if (Math.Abs(p.X - pt.X) < 18 && Math.Abs(p.Y - pt.Y) < 13)
                return IL.Switches[swId];
        }
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
        var train = sig == null && sw == null ? TrainAt(p, sec) : null;

        if (train != null)
            text = $"Поезд №{train.Number}, {train.ModeText}{RadioUi.TrainHint(train)}, едет {(train.Dir == Dir.Right ? "→" : "←")}" +
                   $"{(train.Waiting.Length > 0 ? ", " + train.Waiting : "")}. ПКМ — меню поезда (радиосвязь, номер, бланк)";
        else if (sig != null)
            text = $"{sig.Title}. Сейчас: {AspectInfo.Short(sig.Aspect)}. {AspectInfo.Meaning(sig.Aspect)}";
        else if (sw != null)
            text = $"Стрелка {sw.Label} ({sw.Title}): {(sw.ShownPlus ? "плюс (прямо)" : "минус (на отклонение)")}" +
                   $"{(sw.Controlled ? "" : sw.MoveTimer > 0 ? ", переводится" : ", нет контроля")}{(sw.Locked ? ", заперта" : "")}" +
                   (PanelUi.Marks(sw).Length > 0 ? $". Пометки: {PanelUi.Marks(sw)} (К — колпачок, З — заслонка, Л — замок, В — выключена, М — макет)" : "");
        else if (sec != null)
            text = $"{sec.Label} — {sec.Title}: {(sec.Occupied ? "ЗАНЯТ" : "свободен")}" +
                   $"{(sec.LockedBy != null ? $", замкнут в маршруте {sec.LockedBy.Name}" : "")}" +
                   RadioUi.SectionHint(sec);   // [ДОБАВЛЕНО 23.10.2026, этап 4]

        Cursor = sig != null || sw != null || (selected != null && sec != null) ? Cursors.Hand : Cursors.Default;
        if (text != hoverText) { hoverText = text; Invalidate(); }
        object now = (object)sig ?? (object)sw ?? sec;      // [ДОБАВЛЕНО 28.10.2026] для ЖИ: на чём задержалась мышь
        if (!Equals(now, hoverObj)) { CommitPointed(); hoverObj = now; hoverSince = DateTime.Now; }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (PanelUi.ShowSwitchPositions && MouseButtons == MouseButtons.None) { PanelUi.ShowSwitchPositions = false; ModelHost.MarkDirty(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        CommitPointed();
        if (hoverText.Length > 0) { hoverText = ""; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // [ДОБАВЛЕНО 21.10.2026, этап 6] Е13: обе кнопки мыши — контроль положения всех стрелок
        if (MouseButtons == (MouseButtons.Left | MouseButtons.Right))
        {
            PanelUi.ShowSwitchPositions = true;
            ModelHost.MarkDirty();
            return;
        }
        var p = ToDesign(e.Location);
        var sig = SignalAt(p);
        var sw = sig == null ? SwitchAt(p) : null;
        if (((object)sig ?? (object)sw ?? SectionAt(p)) is { } clicked) Pointed?.Invoke(clicked);   // [ДОБАВЛЕНО 28.10.2026] клик = «показал»
        // [ДОБАВЛЕНО 13.11.2026] настройка своего маршрута: клик забирает окно настройки (false — обычная работа)
        if (EditClick != null && e.Button == MouseButtons.Left && ((object)sig ?? (object)sw ?? SectionAt(p)) is { } ed && EditClick(ed))
        {
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            if (sw != null) { IL.ToggleSwitch(sw); return; }
            // [ДОБАВЛЕНО 13.11.2026] горочный: меню оператора горки (режимы, маневровый маршрут от него)
            if (sig != null && sig.Hump != null && selected == null) { HumpMenu(sig).Show(this, e.Location); return; }
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
            var train = TrainAt(p, sec);
            if (train != null) { TrainMenu(train, sec).Show(this, e.Location); return; }
            if (sec != null) SectionMenu(sec).Show(this, e.Location);   // [ИЗМЕНЕНО 20.10.2026, этап 3] было: сразу занять / освободить
        }
    }

    /// <summary>Нажатый светофор начала (кнопочный пульт подсвечивает его кнопку).</summary>
    public RailSignal Selected => selected;

    // [ИЗМЕНЕНО 28.10.2026] public — кнопки светофоров кнопочного пульта (Ui/ClassicPanel.cs) работают как клик по схеме
    public void SelectSignal(RailSignal s)
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
                if (s.RepeatOf != null)     // [ДОБАВЛЕНО 13.11.2026]
                {
                    IL.Say($"{s.Label} — повторитель горочного {s.RepeatOf.Label}: показывает то же, что он.", LogKind.Refuse);
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
            // [ИЗМЕНЕНО 09.11.2026] в рабочем режиме кнопок ПОЕЗДНЫЕ / МАНЕВРОВЫЕ нет — показываем маршруты обоих видов
            bool any = UiSettings.Mode == UiMode.Work;
            var ends = IL.Routes.Where(r => r.Start == s && (any || r.Shunting == shunt)).Select(r => r.Name).ToList();
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

    /// <summary>Второй клик набора: светофор конца или путь назначения (public — для кнопочного пульта).</summary>
    public void SelectTarget(object target)
    {
        var from = selected;
        if (from == null) return;
        selected = null;
        var route = RouteFinder.Find(IL.Routes, from, target, ShuntMode());
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

    /// <summary>[ДОБАВЛЕНО 20.10.2026] Кнопка «Отмена набора»: сбросить нажатый светофор начала (как на пульте ЭЦ).</summary>
    public void CancelSelection()
    {
        if (selected == null) { IL.Say("Набор маршрута не начат — отменять нечего", LogKind.Refuse); return; }
        IL.Say($"Отмена набора: светофор {selected.Label} сброшен");
        ClearSelection();
    }

    public void ClearSelection()
    {
        selected = null;
        Invalidate();
    }

    // ---------------------- контекстные меню ----------------------
    /// <summary>[ДОБАВЛЕНО 13.11.2026] Режимы горочного светофора (РУ-30-80 листы 45–47; caredenis.ru урок 14).</summary>
    void AddHumpModes(ContextMenuStrip m, RailSignal s)
    {
        foreach (var (mode, text, _) in HumpInfo.Modes)
        {
            var md = mode;
            m.Items.Add(new ToolStripMenuItem(text, null, (_, _) => IL.SetHumpMode(s, md)) { Checked = s.Hump == md });
        }
    }

    /// <summary>[ДОБАВЛЕНО 13.11.2026] Левый клик по горочному: режимы оператора горки и маневровый маршрут от него.</summary>
    ContextMenuStrip HumpMenu(RailSignal s)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem($"Горочный {s.Label}: {AspectInfo.Short(s.Aspect)}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        AddHumpModes(m, s);
        if (IL.Routes.Any(r => r.Start == s))
        {
            m.Items.Add(new ToolStripSeparator());
            var act = IL.ActiveRoutes.FirstOrDefault(r => r.Start == s);
            if (act != null) m.Items.Add($"Отменить маршрут {act.Name}", null, (_, _) => IL.CancelRoute(act));
            else m.Items.Add("Маневровый маршрут от него… (кликните путь или светофор конца)", null, (_, _) => SelectSignal(s));
        }
        return m;
    }

    ContextMenuStrip SignalMenu(RailSignal s)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem($"{s.Label}: {AspectInfo.Short(s.Aspect)}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        if (s.Hump != null) { AddHumpModes(m, s); m.Items.Add(new ToolStripSeparator()); }   // [ДОБАВЛЕНО 13.11.2026]

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
        AddFaultItem(m, "S:" + s.Id);
        return m;
    }

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] «Неисправность…» — окно журнала осмотра ДУ-46 по объекту (как в тренажёре ДСП).</summary>
    static void AddFaultItem(ContextMenuStrip m, string obj)
    {
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Неисправность… (журнал ДУ-46)", null, (_, _) => Du46Ui.Open(obj));
    }

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] ПКМ по участку: занятость вручную (имитация) и «Неисправность…».</summary>
    ContextMenuStrip SectionMenu(TrackSection sec)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem($"Участок {sec.Label}: {(sec.Occupied ? "занят" : "свободен")}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        // [ИЗМЕНЕНО 28.10.2026] ручная занятость — не во всех режимах интерфейса (освободить можно всегда)
        if (UiSettings.Show(UiSettings.ManualOcc) || sec.ManualOcc)
            m.Items.Add(sec.ManualOcc ? "Освободить вручную (имитация)" : "Занять вручную (имитация)", null, (_, _) =>
            {
                sec.ManualOcc = !sec.ManualOcc;
                IL.Say($"Участок {sec.Label} {(sec.ManualOcc ? "занят (вручную)" : "освобождён (вручную)")}");
                IL.NotifyChanged();
            });
        // [ДОБАВЛЕНО 18.11.2026] виртуальный поезд — в любом режиме интерфейса (и в рабочем, и в павлодарском)
        if (!sec.Occupied)
        {
            var put = new ToolStripMenuItem("Поставить виртуальный поезд");
            put.DropDownItems.Add("← головой влево", null, (_, _) => ModelHost.Model.Trains.Spawn(sec, Dir.Left));
            put.DropDownItems.Add("головой вправо →", null, (_, _) => ModelHost.Model.Trains.Spawn(sec, Dir.Right));
            put.DropDownItems.Add(new ToolStripSeparator());
            put.DropDownItems.Add("маневровый ←", null, (_, _) => ModelHost.Model.Trains.Spawn(sec, Dir.Left, true));
            put.DropDownItems.Add("маневровый →", null, (_, _) => ModelHost.Model.Trains.Spawn(sec, Dir.Right, true));
            m.Items.Add(put);
        }
        RadioUi.AddSectionItems(m, sec, this);   // [ДОБАВЛЕНО 23.10.2026, этап 4] предупреждение / окно, поезд на макете
        AddFaultItem(m, "P:" + sec.Id);
        return m;
    }

    /// <summary>Поезд под мышью: по его ярлыку или по участку, на котором он стоит.</summary>
    VirtualTrain TrainAt(PointF p, TrackSection sec)
    {
        foreach (var (t, r) in trainRects)
            if (r.Contains(p) && ModelHost.Model.Trains.Trains.Contains(t)) return t;
        return sec != null ? ModelHost.Model.Trains.At(sec) : null;
    }

    /// <summary>[ДОБАВЛЕНО 20.10.2026, правки преподавателя] ПКМ по поезду: развернуть, сменить режим.</summary>
    ContextMenuStrip TrainMenu(VirtualTrain t, TrackSection sec)
    {
        if (t.NoLoco) return ShuntUi.WagonGroupMenu(t, this);   // [ДОБАВЛЕНО 23.10.2026, этап 7] отцепленные вагоны
        var sim = ModelHost.Model.Trains;
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem($"Поезд №{t.Number}: {t.ModeText}, едет {(t.Dir == Dir.Right ? "→" : "←")}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        // [ИЗМЕНЕНО 18.11.2026] виртуальным поездом управляем в любом режиме; ручная занятость — по-прежнему по «Параметрам»
        bool virt = UiSettings.Show(UiSettings.ManualOcc);
        m.Items.Add($"Развернуть (ехать {(t.Dir == Dir.Right ? "←" : "→")})", null, (_, _) => sim.Reverse(t));
        m.Items.Add(t.Shunting ? "Сделать поездным" : "Сделать маневровым", null, (_, _) => sim.SetShunting(t, !t.Shunting));
        if (sec != null && (virt || sec.ManualOcc))
        {
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(sec.ManualOcc ? $"Освободить участок {sec.Label} (вручную)" : $"Занять участок {sec.Label} (вручную)", null, (_, _) =>
            {
                sec.ManualOcc = !sec.ManualOcc;
                IL.Say($"Участок {sec.Label} {(sec.ManualOcc ? "занят (вручную)" : "освобождён (вручную)")}");
                IL.NotifyChanged();
            });
        }
        RadioUi.AddTrainItems(m, t, this);   // [ДОБАВЛЕНО 23.10.2026, этап 4] номер и индекс, радиосвязь, предупреждение, бланк
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Неисправность… (журнал ДУ-46)", null, (_, _) => Du46Ui.Open("T:" + t.Number));
        return m;
    }

    ContextMenuStrip SwitchMenu(RailSwitch sw)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem($"Стрелка {sw.Label}: {(sw.ShownPlus ? "плюс" : "минус")}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Перевести", null, (_, _) => IL.ToggleSwitch(sw));
        m.Items.Add("Вспомогательный перевод…", null, (_, _) => AskAuxSwitch(this, sw));
        PanelUi.AddSwitchItems(m, sw, this);          // [ДОБАВЛЕНО 21.10.2026, этап 6] колпачок, курбель, выключение, макет
        AddFaultItem(m, "W:" + sw.Id);
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
