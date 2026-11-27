using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StationApp.Common;

// =====================================================================
//  [ДОБАВЛЕНО 26.11.2026] ОКНО «РАЗРАБОТЧИКИ» — ОДНО НА ВСЕ ПРОГРАММЫ
//  Подключается ссылкой в каждый проект (АКЖТ, Алгабас, Станция по схеме, Сортировочная, ДНЦ, Преподаватель,
//  оба редактора станций). Тексты — здесь же на трёх языках (ru / kk / en): окно не зависит от словарей.
//  Вызов: DevelopersForm.Open(owner, "Название программы", L.Current).
//  Вид: тёмное окно без рамки, сверху — полоса с путями и бегущим поездом, четыре карточки с именами,
//  внизу — колледж, город и год. Нажатие на карточку открывает панель «что делал» этого человека
//  (закрыть панель — «✕», клик мимо неё или Esc). Окно закрывается «Закрыть» или Esc; таскается за шапку.
// =====================================================================

public sealed class DevelopersForm : Form
{
    sealed record Dev(string Surname, string Name, Color A, Color B);

    // имена — как дал Мади (каз. написание); для английского — латиницей
    static readonly Dev[] DevsCyr =
    {
        new("Насиғожин", "Мади", Color.FromArgb(0, 150, 255), Color.FromArgb(0, 200, 170)),
        new("Бейсембек", "Тұмар", Color.FromArgb(255, 120, 60), Color.FromArgb(255, 190, 40)),
        new("Жұмабай", "Арнұр", Color.FromArgb(150, 90, 255), Color.FromArgb(230, 90, 200)),
        new("Разиньков", "Артем", Color.FromArgb(40, 190, 90), Color.FromArgb(160, 220, 50)),
    };
    static readonly Dev[] DevsLat =
    {
        new("Nasigozhin", "Madi", DevsCyr[0].A, DevsCyr[0].B),
        new("Beisembek", "Tumar", DevsCyr[1].A, DevsCyr[1].B),
        new("Zhumabay", "Arnur", DevsCyr[2].A, DevsCyr[2].B),
        new("Razinkov", "Artem", DevsCyr[3].A, DevsCyr[3].B),
    };

    // кто что делал — со слов Мади; порядок как в DevsCyr (Мади, Тұмар, Арнұр, Артем).
    // Арнұр и Артем — оба главные разработчики АКЖТ и Алгабаса, «вместе с …» не пишем.
    static string[][] Works(string lang) => lang switch
    {
        "kk" => new[]
        {
            new[] { "АКЖТ және Алгабас станцияларын жетілдіру", "«Сызба бойынша станция» — json файлынан станция", "«Сұрыптау станциясы» — сұрыптау төбесі", "ДНЦ — бас диспетчер" },
            new[] { "Жасанды интеллект — бағдарламалардағы ЖИ-көмекші" },
            new[] { "Негізгі бағдарлама — АКЖТ және Алгабас станциялары", "Оқытушы бағдарламасы" },
            new[] { "Негізгі бағдарлама — АКЖТ және Алгабас станциялары", "Станциялар редакторы — станцияны текшелерден құру", "Оқытушы бағдарламасы", "ДНЦ — бас диспетчер" },
        },
        "en" => new[]
        {
            new[] { "Improving the АКЖТ and Algabas stations", "«Station from scheme» — a station drawn from a json file", "«Marshalling yard» — the hump yard", "Dispatcher (ДНЦ) — the chief dispatcher" },
            new[] { "Artificial intelligence — the AI assistant in the programs" },
            new[] { "Main program — the АКЖТ and Algabas stations", "The instructor program" },
            new[] { "Main program — the АКЖТ and Algabas stations", "Station editor — building stations from blocks", "The instructor program", "Dispatcher (ДНЦ) — the chief dispatcher" },
        },
        _ => new[]
        {
            new[] { "Доработка станций АКЖТ и Алгабас", "«Станция по схеме» — станция из json-файла", "«Сортировочная» — сортировочная горка", "ДНЦ — главный диспетчер" },
            new[] { "Искусственный интеллект — ИИ-помощник в программах" },
            new[] { "Основная программа — станции АКЖТ и Алгабас", "Программа преподавателя" },
            new[] { "Основная программа — станции АКЖТ и Алгабас", "Редактор станций — станция из кубиков", "Программа преподавателя", "ДНЦ — главный диспетчер" },
        },
    };

    // title, role, made, city, close, authorsTitle, hint (над карточками), hover (на карточке), panelTitle
    static string[] Texts(string lang) => lang switch
    {
        "kk" => new[] { "Әзірлеушілер", "әзірлеуші", "ЦАТЭК студенттері АКЖТ колледжі үшін жасаған", "Алматы, 2026 жыл", "Жабу", "Бағдарламаны жасағандар",
                        "Толығырақ білу үшін есімді басыңыз", "Толығырақ — басыңыз", "Не істеді" },
        "en" => new[] { "Developers", "developer", "Made by ЦАТЭК students for the АКЖТ college", "Almaty, 2026", "Close", "The program was made by",
                        "Click a name to learn more", "Details — click", "What they did" },
        _ => new[] { "Разработчики", "разработчик", "Сделано студентами ЦАТЭК для колледжа АКЖТ", "Алматы, 2026 год", "Закрыть", "Программу сделали",
                     "Нажмите на имя, чтобы узнать подробнее", "Подробнее — нажмите", "Что делал(а)" },
    };

    /// <summary>Кнопка «Разработчики» на нужном языке (для верхних полос программ).</summary>
    public static string ButtonText(string lang) => Texts(lang)[0];

    public static void Open(IWin32Window owner, string appName, string lang)
    {
        using var f = new DevelopersForm(appName, lang ?? "ru");
        if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
    }

    // ---------------- окно ----------------
    const int W = 720, H = 610, HeadH = 170;
    static readonly Color Bg = Color.FromArgb(18, 22, 30), Card = Color.FromArgb(30, 36, 48), Line = Color.FromArgb(58, 66, 82);

    readonly string app;
    readonly string[] tx;
    readonly Dev[] devs;
    readonly string[][] works;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    readonly Rectangle[] cards = new Rectangle[4];
    readonly Rectangle panel;   // панель «что делал» — поверх карточек
    Rectangle btnClose, btnX, btnPanelX;
    int hover = -1;          // карточка под мышью
    bool hoverClose, hoverX, hoverPanelX;
    int shown = -1;          // чья панель открыта (-1 — никакой)
    float anim;              // 0..1 — появление панели
    bool closing;            // панель уходит
    double train;            // положение поезда, пикселей
    Point dragFrom;
    bool dragging;

    DevelopersForm(string appName, string lang)
    {
        app = appName ?? "";
        tx = Texts(lang);
        devs = lang == "en" ? DevsLat : DevsCyr;
        works = Works(lang);
        Tag = "nolang";
        Text = tx[0];
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(W, H);
        BackColor = Bg;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        using (var p = Rounded(new Rectangle(0, 0, W, H), 18)) Region = new Region(p);

        int cw = (W - 40 * 2 - 20) / 2, ch = 92, top = HeadH + 52;
        for (int i = 0; i < 4; i++)
            cards[i] = new Rectangle(40 + (i % 2) * (cw + 20), top + (i / 2) * (ch + 16), cw, ch);
        panel = Rectangle.FromLTRB(40, HeadH + 14, W - 40, cards[3].Bottom);
        btnPanelX = new Rectangle(panel.Right - 44, panel.Y + 12, 30, 30);
        btnClose = new Rectangle(W / 2 - 80, H - 62, 160, 40);
        btnX = new Rectangle(W - 46, 12, 32, 32);

        timer.Tick += (_, _) =>
        {
            train += 2.2; if (train > W + 260) train = -40;
            bool moving = (shown >= 0 && !closing && anim < 1) || closing;
            if (shown >= 0 && !closing && anim < 1) anim = Math.Min(1, anim + 0.12f);
            if (closing) { anim = Math.Max(0, anim - 0.15f); if (anim == 0) { closing = false; shown = -1; } }
            if (moving) Invalidate(); else Invalidate(new Rectangle(0, 0, W, HeadH));
        };
        Shown += (_, _) => timer.Start();
        FormClosed += (_, _) => timer.Dispose();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Escape && shown >= 0 && !closing) { HidePanel(); return; }
            if (e.KeyCode is Keys.Escape or Keys.Enter) Close();
        };
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }   // CS_DROPSHADOW — тень вокруг окна
    }

    bool PanelOpen => shown >= 0 && !closing;
    void ShowPanel(int i) { shown = i; closing = false; anim = 0; hover = -1; Invalidate(); }
    void HidePanel() { if (shown >= 0) { closing = true; Invalidate(); } }

    // ---------------- мышь ----------------
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) { Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y); return; }
        int h = PanelOpen ? -1 : Array.FindIndex(cards, c => c.Contains(e.Location));
        bool hc = btnClose.Contains(e.Location), hx = btnX.Contains(e.Location), hp = PanelOpen && btnPanelX.Contains(e.Location);
        if (h != hover || hc != hoverClose || hx != hoverX || hp != hoverPanelX) { hover = h; hoverClose = hc; hoverX = hx; hoverPanelX = hp; Invalidate(); }
        Cursor = hc || hx || hp || h >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1; hoverClose = hoverX = hoverPanelX = false; Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (btnClose.Contains(e.Location) || btnX.Contains(e.Location)) { Close(); return; }
        if (PanelOpen)
        {
            // «✕» на панели или клик мимо неё — панель закрывается
            if (btnPanelX.Contains(e.Location) || !panel.Contains(e.Location)) HidePanel();
            if (e.Y < HeadH) { dragging = true; dragFrom = e.Location; }
            return;
        }
        int c = Array.FindIndex(cards, r => r.Contains(e.Location));
        if (c >= 0) { ShowPanel(c); Cursor = Cursors.Default; return; }
        if (e.Y < HeadH) { dragging = true; dragFrom = e.Location; }
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }

    // ---------------- рисование ----------------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Bg);
        DrawHeader(g);

        using (var f = new Font("Segoe UI", 10.5f, FontStyle.Bold))
        using (var b = new SolidBrush(Color.FromArgb(140, 160, 190)))
            g.DrawString(tx[5].ToUpperInvariant(), f, b, 40, HeadH + 20);
        using (var f = new Font("Segoe UI", 9f, FontStyle.Italic))
        using (var b = new SolidBrush(Color.FromArgb(110, 125, 150)))
        using (var sf = new StringFormat { Alignment = StringAlignment.Far })
            g.DrawString(tx[6], f, b, new RectangleF(W / 2, HeadH + 22, W / 2 - 40, 20), sf);

        for (int i = 0; i < 4; i++) DrawCard(g, i);
        DrawFooter(g);
        if (shown >= 0) DrawPanel(g, shown);

        using var pen = new Pen(Color.FromArgb(70, 90, 120), 1.5f);
        using var border = Rounded(new Rectangle(0, 0, W - 1, H - 1), 18);
        g.DrawPath(pen, border);
    }

    void DrawHeader(Graphics g)
    {
        var r = new Rectangle(0, 0, W, HeadH);
        var clip = g.Clip;
        g.SetClip(r);
        using (var lg = new LinearGradientBrush(r, Color.FromArgb(10, 60, 130), Color.FromArgb(0, 140, 150), 15f))
            g.FillRectangle(lg, r);
        // мягкие круги-«огни» на фоне
        using (var gb = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
        {
            g.FillEllipse(gb, W - 210, -90, 260, 260);
            g.FillEllipse(gb, -70, 40, 180, 180);
        }

        // пути: шпалы и два рельса
        int ry = HeadH - 34;
        using (var sl = new Pen(Color.FromArgb(90, 20, 30, 40), 5))
            for (int x = -10; x < W + 10; x += 16) g.DrawLine(sl, x, ry - 4, x + 4, ry + 14);
        using (var rail = new Pen(Color.FromArgb(200, 210, 225, 235), 2.2f))
        {
            g.DrawLine(rail, 0, ry, W, ry);
            g.DrawLine(rail, 0, ry + 10, W, ry + 10);
        }
        DrawTrain(g, (float)train, ry);
        g.Clip = clip;

        // светофор справа
        int sx = W - 70, sy = 54;
        using (var mast = new Pen(Color.FromArgb(220, 30, 34, 40), 3)) g.DrawLine(mast, sx + 9, sy + 40, sx + 9, ry);
        using (var head = new SolidBrush(Color.FromArgb(230, 25, 28, 34)))
        using (var hp = Rounded(new Rectangle(sx, sy, 18, 40), 8))
            g.FillPath(head, hp);
        bool blink = (Environment.TickCount / 600) % 2 == 0;
        using (var gr = new SolidBrush(Color.FromArgb(60, 255, 120))) g.FillEllipse(gr, sx + 3, sy + 4, 12, 12);
        using (var ye = new SolidBrush(blink ? Color.FromArgb(255, 210, 60) : Color.FromArgb(70, 60, 30))) g.FillEllipse(ye, sx + 3, sy + 22, 12, 12);

        using (var ft = new Font("Segoe UI", 26f, FontStyle.Bold))
            g.DrawString(tx[0], ft, Brushes.White, 36, 22);
        using (var fa = new Font("Segoe UI", 12f))
        using (var ba = new SolidBrush(Color.FromArgb(215, 235, 255)))
            g.DrawString(app, fa, ba, new RectangleF(40, 72, W - 160, 26), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

        // ✕
        DrawX(g, btnX, hoverX, 255);
    }

    static void DrawX(Graphics g, Rectangle b, bool hv, int alpha)
    {
        using (var xb = new SolidBrush(Color.FromArgb(alpha * (hv ? 90 : 35) / 255, 255, 255, 255)))
            g.FillEllipse(xb, b);
        using var xp = new Pen(Color.FromArgb(alpha, 255, 255, 255), 2f);
        int c = b.Width * 10 / 32;
        g.DrawLine(xp, b.X + c, b.Y + c, b.Right - c, b.Bottom - c);
        g.DrawLine(xp, b.Right - c, b.Y + c, b.X + c, b.Bottom - c);
    }

    static void DrawTrain(Graphics g, float x, int ry)
    {
        // справа налево: локомотив впереди (движение вправо), три вагона за ним
        float y = ry - 22, h = 22;
        Color[] cars = { Color.FromArgb(200, 60, 50), Color.FromArgb(70, 110, 170), Color.FromArgb(60, 140, 90) };
        float cx = x - 4 * 62;
        foreach (var c in cars)
        {
            using var b = new SolidBrush(c);
            using var cp = Rounded(new RectangleF(cx, y + 2, 56, h - 4), 3);
            g.FillPath(b, cp);
            Wheels(g, cx, 56, ry);
            cx += 62;
        }
        // локомотив
        using (var lb = new SolidBrush(Color.FromArgb(250, 200, 40)))
        using (var lp = Rounded(new RectangleF(cx, y - 4, 70, h + 2), 5))
            g.FillPath(lb, lp);
        using (var cab = new SolidBrush(Color.FromArgb(40, 60, 80)))
            g.FillRectangle(cab, cx + 50, y, 14, 8);
        using (var st = new SolidBrush(Color.FromArgb(30, 30, 30)))
            g.FillRectangle(st, cx + 4, y + 8, 40, 3);
        Wheels(g, cx, 70, ry);
        using (var lamp = new SolidBrush(Color.FromArgb(255, 255, 220))) g.FillEllipse(lamp, cx + 64, y + 10, 5, 5);
    }

    static void Wheels(Graphics g, float x, float w, int ry)
    {
        using var b = new SolidBrush(Color.FromArgb(25, 25, 30));
        g.FillEllipse(b, x + 6, ry - 6, 9, 9);
        g.FillEllipse(b, x + w - 15, ry - 6, 9, 9);
    }

    static void Avatar(Graphics g, Rectangle av, Dev d, float fontSize, int alpha = 255)
    {
        using (var lg = new LinearGradientBrush(av, Color.FromArgb(alpha, d.A), Color.FromArgb(alpha, d.B), 45f)) g.FillEllipse(lg, av);
        using (var ring = new Pen(Color.FromArgb(alpha * 120 / 255, 255, 255, 255), 2f)) g.DrawEllipse(ring, av);
        string ini = d.Surname[..1] + d.Name[..1];
        using var fi = new Font("Segoe UI", fontSize, FontStyle.Bold);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var wb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
        g.DrawString(ini, fi, wb, av, sf);
    }

    void DrawCard(Graphics g, int i)
    {
        var r = cards[i];
        var d = devs[i];
        bool hv = hover == i;
        if (hv) r.Offset(0, -3);
        using (var sh = new SolidBrush(Color.FromArgb(hv ? 90 : 60, 0, 0, 0)))
        using (var sp = Rounded(new Rectangle(r.X + 2, r.Y + 5, r.Width, r.Height), 14))
            g.FillPath(sh, sp);
        using (var cp = Rounded(r, 14))
        {
            using (var cb = new SolidBrush(hv ? Color.FromArgb(38, 46, 62) : Card)) g.FillPath(cb, cp);
            using (var pen = new Pen(hv ? d.A : Line, hv ? 2f : 1f)) g.DrawPath(pen, cp);
        }
        // полоска цвета слева
        using (var lg = new LinearGradientBrush(new Rectangle(r.X, r.Y, 6, r.Height), d.A, d.B, 90f))
        using (var strip = Rounded(new Rectangle(r.X + 10, r.Y + 16, 4, r.Height - 32), 2))
            g.FillPath(lg, strip);

        // кружок с инициалами
        var av = new Rectangle(r.X + 26, r.Y + (r.Height - 58) / 2, 58, 58);
        Avatar(g, av, d, 17f);

        int tx0 = av.Right + 16;
        using (var fs = new Font("Segoe UI", 15f, FontStyle.Bold))
            g.DrawString(d.Surname, fs, Brushes.White, tx0, r.Y + 16);
        using (var fn = new Font("Segoe UI", 12.5f))
        using (var bn = new SolidBrush(Color.FromArgb(200, 210, 225)))
            g.DrawString(d.Name, fn, bn, tx0 + 1, r.Y + 44);
        // внизу: «разработчик», а при наведении — «Подробнее — нажмите»; справа стрелка
        using (var fr = new Font("Segoe UI", 8.5f, hv ? FontStyle.Bold : FontStyle.Regular))
        using (var br = new SolidBrush(d.A))
            g.DrawString(hv ? tx[7] : tx[1], fr, br, tx0 + 2, r.Y + 68);
        using (var ap = new Pen(hv ? d.A : Color.FromArgb(80, 92, 112), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            int ax = r.Right - 26 + (hv ? 4 : 0), ay = r.Y + r.Height / 2;
            g.DrawLine(ap, ax - 5, ay - 7, ax + 2, ay);
            g.DrawLine(ap, ax + 2, ay, ax - 5, ay + 7);
        }
    }

    // панель «что делал» — выезжает поверх карточек
    void DrawPanel(Graphics g, int i)
    {
        float k = anim;
        int al = (int)(255 * k);
        var d = devs[i];
        // затемнение под панелью
        using (var dim = new SolidBrush(Color.FromArgb((int)(170 * k), Bg)))
            g.FillRectangle(dim, 0, HeadH, W, H - HeadH);

        var r = panel;
        r.Offset(0, (int)((1 - k) * 24));
        using (var sh = new SolidBrush(Color.FromArgb((int)(110 * k), 0, 0, 0)))
        using (var sp = Rounded(new Rectangle(r.X + 3, r.Y + 7, r.Width, r.Height), 16))
            g.FillPath(sh, sp);
        using (var cp = Rounded(r, 16))
        {
            using (var cb = new SolidBrush(Color.FromArgb(al, 32, 40, 54))) g.FillPath(cb, cp);
            // цветная полоса сверху — цвета человека
            var clip = g.Clip;
            g.SetClip(cp, CombineMode.Intersect);
            using (var lg = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, 6), Color.FromArgb(al, d.A), Color.FromArgb(al, d.B), 0f))
                g.FillRectangle(lg, r.X, r.Y, r.Width, 6);
            g.Clip = clip;
            using (var pen = new Pen(Color.FromArgb(al, d.A), 2f)) g.DrawPath(pen, cp);
        }

        var av = new Rectangle(r.X + 26, r.Y + 24, 64, 64);
        Avatar(g, av, d, 19f, al);
        int tx0 = av.Right + 18;
        using (var fs = new Font("Segoe UI", 17f, FontStyle.Bold))
        using (var wb = new SolidBrush(Color.FromArgb(al, 255, 255, 255)))
            g.DrawString(d.Surname + " " + d.Name, fs, wb, tx0, r.Y + 26);
        using (var fr = new Font("Segoe UI", 10f, FontStyle.Bold))
        using (var br = new SolidBrush(Color.FromArgb(al, d.A)))
            g.DrawString(tx[8].ToUpperInvariant(), fr, br, tx0 + 2, r.Y + 62);

        var bx = btnPanelX; bx.Offset(0, r.Y - panel.Y);
        DrawX(g, bx, hoverPanelX, al);

        using (var pen = new Pen(Color.FromArgb(al, Line))) g.DrawLine(pen, r.X + 26, r.Y + 102, r.Right - 26, r.Y + 102);
        float y = r.Y + 114;
        using var wf = new Font("Segoe UI", 11.5f);
        using var tb = new SolidBrush(Color.FromArgb(al, 225, 232, 244));
        using var dot = new SolidBrush(Color.FromArgb(al, d.A));
        foreach (var w in works[i])
        {
            g.FillEllipse(dot, r.X + 34, y + 8, 8, 8);
            var sz = g.MeasureString(w, wf, r.Width - 90);
            g.DrawString(w, wf, tb, new RectangleF(r.X + 52, y, r.Width - 90, sz.Height + 2));
            y += sz.Height + 4;
        }
    }

    void DrawFooter(Graphics g)
    {
        int y = cards[3].Bottom + 26;
        using (var pen = new Pen(Line)) g.DrawLine(pen, 40, y, W - 40, y);
        using var sf = new StringFormat { Alignment = StringAlignment.Center };
        using (var fm = new Font("Segoe UI", 12.5f, FontStyle.Bold))
        using (var bm = new SolidBrush(Color.FromArgb(235, 240, 248)))
            g.DrawString(tx[2], fm, bm, new RectangleF(20, y + 12, W - 40, 26), sf);
        using (var fc = new Font("Segoe UI", 10.5f))
        using (var bc = new SolidBrush(Color.FromArgb(140, 160, 190)))
            g.DrawString(tx[3], fc, bc, new RectangleF(20, y + 40, W - 40, 22), sf);

        using (var bp = Rounded(btnClose, 20))
        {
            using (var lg = new LinearGradientBrush(btnClose, hoverClose ? Color.FromArgb(30, 150, 255) : Color.FromArgb(0, 110, 210),
                                                    hoverClose ? Color.FromArgb(0, 200, 190) : Color.FromArgb(0, 150, 160), 0f))
                g.FillPath(lg, bp);
        }
        using var fb = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var sc = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(tx[4], fb, Brushes.White, btnClose, sc);
    }

    static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---------------- снимок для проверки (без показа на экране) ----------------
    /// <summary>Рисует окно в картинку: DevelopersForm.Shot("file.png", "Сортировочная", "kk").
    /// person 0..3 — с открытой панелью этого человека, hoverCard — подсветить карточку.</summary>
    public static void Shot(string file, string appName, string lang, int person = -1, int hoverCard = -1)
    {
        using var f = new DevelopersForm(appName, lang ?? "ru");
        f.train = 330;
        f.hover = hoverCard;
        if (person >= 0) { f.shown = person; f.anim = 1; }
        using var bmp = new Bitmap(W, H);
        using (var g = Graphics.FromImage(bmp))
            f.OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, W, H)));
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }
}
