namespace DncApp;

// =====================================================================
//  ПЛИТКА ОБЗОРА: одна схема одной станции. Заголовок (название, связь, ◀ ▶, «Управлять», «✕»)
//  и кусок схемы, который ей отвела лента (Ribbon): масштаб общий для всех, по X — до стыка с соседом,
//  по высоте — сдвиг, чтобы пути соседей легли в линию. Станция рисует этот кусок заново ровно в этот
//  размер (t_scheme) — поэтому он чёткий при любом мониторе. Порядок — перетаскиванием за заголовок или ◀ ▶.
// =====================================================================

public class SchemeTile : Panel
{
    public const int HeadH = 34;
    static readonly Color HeadBg = Color.FromArgb(35, 60, 80);

    public readonly string StationId, SchemeName;
    readonly Panel head = new() { Dock = DockStyle.Top, Height = HeadH, BackColor = HeadBg };
    readonly Label lbl = new()
    {
        Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Cursor = Cursors.SizeAll, Padding = new Padding(4, 0, 0, 0)
    };
    readonly Button bLeft, bRight, bCtl, bClose;
    public readonly ImageView View = new() { Dock = DockStyle.Fill, Placeholder = "Ожидание схемы…" };
    readonly string tag = "tile" + Guid.NewGuid().ToString("N")[..6];
    readonly ToolTip tips = new();

    bool awaiting;
    long requestedAt, nextAt;
    /// <summary>Что отвела лента: масштаб (пикселей на единицу чертежа) и кусок чертежа по X.</summary>
    float k, x0, x1;
    Point dragFrom;

    public event Action<SchemeTile> ControlClicked, CloseClicked;
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Этой схемой сейчас управляет ДНЦ: мышь над ней уходит на станцию, кадры чаще.</summary>
    public bool Controlled;
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Вид управления смотрит клик раньше станции (активная схема, сквозной маршрут):
    /// (плитка, act, dx, dy, кнопка) → true — клик забран, станции не отправлять.</summary>
    public Func<SchemeTile, string, double, double, string, bool> Intercept;
    string inAct, inBtn;
    double inDx, inDy;
    public event Action<SchemeTile, int> MoveClicked;

    public SchemeTile(string stationId, string schemeName)
    {
        StationId = stationId;
        SchemeName = schemeName;
        BackColor = Color.FromArgb(40, 44, 52);
        Padding = new Padding(0);
        Margin = new Padding(0, 0, 6, 0);

        bLeft = HeadButton("◀", "Сдвинуть влево");
        bRight = HeadButton("▶", "Сдвинуть вправо");
        bCtl = HeadButton("Управлять", "Раскрыть станцию на всё окно: кнопки пульта и схема, управление у ДНЦ");
        bClose = HeadButton("✕", "Убрать схему из окна");
        bLeft.Click += (_, _) => MoveClicked?.Invoke(this, -1);
        bRight.Click += (_, _) => MoveClicked?.Invoke(this, +1);
        bCtl.Click += (_, _) => ControlClicked?.Invoke(this);
        bClose.Click += (_, _) => CloseClicked?.Invoke(this);
        bCtl.BackColor = Color.FromArgb(219, 0, 105);
        bCtl.ForeColor = Color.White;

        // все кнопки — слева: плитка широкая, правый край часто за краем окна
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(3, 3, 0, 0), BackColor = HeadBg };
        left.Controls.AddRange(new Control[] { bLeft, bRight, bCtl, bClose });
        head.Controls.Add(lbl);
        head.Controls.Add(left);
        Controls.Add(View);
        Controls.Add(head);
        tips.SetToolTip(lbl, "Потяните за заголовок, чтобы поменять порядок схем");

        // перетаскивание за заголовок
        lbl.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) dragFrom = e.Location; };
        lbl.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - dragFrom.X) + Math.Abs(e.Y - dragFrom.Y) < 8) return;
            DoDragDrop(this, DragDropEffects.Move);
        };
        View.DoubleClick += (_, _) => { if (!Controlled) ControlClicked?.Invoke(this); };   // при управлении двойной клик — по схеме
        // мышь над схемой при управлении — точкой чертежа: картинка = кусок чертежа x0..x1 во всю его высоту
        View.MakeInput = (act, x, y, btn, delta) =>
        {
            (inAct, inBtn, inDx, inDy) = (act, btn, x0 + x * (x1 - x0), y * (Info?.Dh ?? 0));
            return new { kind = "t_input", act, scheme = SchemeName, dx = inDx, dy = inDy, btn, delta };
        };
        View.WheelAlwaysFree = true;
        View.Input += d =>
        {
            string act = inAct;
            inAct = null;
            if (act != null && Intercept?.Invoke(this, act, inDx, inDy, inBtn) == true) return;
            Net.Input(StationId, d);
        };

        Net.SchemeReceived += OnScheme;
        Disposed += (_, _) => Net.SchemeReceived -= OnScheme;
        RefreshState();
    }

    static Button HeadButton(string text, string tip)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(240, 240, 255), Font = new Font("Segoe UI", 9f), Margin = new Padding(3, 0, 0, 0),
            Padding = new Padding(2, 0, 2, 0), MinimumSize = new Size(28, 26)
        };
        b.FlatAppearance.BorderSize = 0;
        new ToolTip().SetToolTip(b, tip);
        return b;
    }

    public StationRt Station => Net.Get(StationId);

    /// <summary>Что станция сообщила об этой схеме (размер чертежа, общие участки); null — ещё не знаем.</summary>
    public SchemeInfo Info => Station?.Scheme(SchemeName);

    /// <summary>Место в ленте: масштаб k, кусок чертежа x0..x1, верх картинки в плитке (px), высота полосы картинок (px).</summary>
    public void Place(float scale, float from, float to, int top, int stripH)
    {
        if (Math.Abs(scale - k) > 1e-4 || Math.Abs(from - x0) > 0.01 || Math.Abs(to - x1) > 0.01) nextAt = 0;   // перерисовать сразу
        (k, x0, x1) = (scale, from, to);
        if (scale <= 0)
        {
            // [ДОБАВЛЕНО 18.11.2026] о схеме ничего не известно — не растягивать старую картинку в узкую заглушку
            if (View.Image != null) View.SetImage(null);
            View.PlaceTop = -1;
            View.Placeholder = Station is { Online: true } ? "Схема сейчас недоступна" : View.Placeholder;
            View.Invalidate();
            return;
        }
        View.PlaceTop = Math.Max(0, top);
        View.PlaceHeight = Info is { Dh: > 0 } inf ? (int)Math.Round(inf.Dh * scale) : stripH;
        View.Invalidate();
    }

    public void RefreshState()
    {
        if (IsDisposed) return;
        var st = Station;
        string name = SchemeName;
        if (st != null && st.Title != SchemeName && st.Schemes.Count > 1) name = $"{SchemeName}  ({st.Title})";
        string state = st == null || !st.Online ? "  — нет связи"
                     : st.InfoReceived && st.Scheme(SchemeName) == null ? "  — такой схемы на станции нет"
                     : st.Control ? "  — управление у ДНЦ" : "";
        lbl.Text = name + state;
        lbl.ForeColor = st is { Online: true } ? Color.White : Color.FromArgb(255, 150, 140);
        bCtl.Enabled = st is { Online: true };
        bCtl.Text = Controlled ? "Вернуть" : "Управлять";
        View.InputEnabled = Controlled && st is { Online: true, Control: true };
        View.Placeholder = st is { Online: true } ? "Ожидание схемы…" : $"Станция «{st?.Title ?? StationId}» не на связи";
        if (st is not { Online: true }) View.SetImage(null);
        View.Invalidate();
    }

    /// <summary>Шаг цикла кадров (обзор зовёт раз в 100 мс). visible — плитка видна в ленте.</summary>
    public void Tick(bool visible)
    {
        if (Station is not { Online: true } || k <= 0 || x1 <= x0 || View.Width < 20) return;
        long now = Environment.TickCount64;
        if (awaiting && now - requestedAt < 4000) return;
        if (now < nextAt) return;
        awaiting = true;
        requestedAt = now;
        nextAt = now + (Controlled ? 120 : visible ? 300 : 3000);   // управляемая — часто, за краем ленты — редко
        Net.RequestScheme(StationId, SchemeName, k, x0, x1, tag);
    }

    void OnScheme(string station, string scheme, string t, Image img)
    {
        if (station != StationId || t != tag) return;   // кадр другой плитки
        if (IsDisposed) { img.Dispose(); return; }
        awaiting = false;
        View.SetImage(img);
    }
}
