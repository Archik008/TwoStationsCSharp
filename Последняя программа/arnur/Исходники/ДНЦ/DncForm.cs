using StationApp.Core;

namespace DncApp;

// =====================================================================
//  ГЛАВНОЕ ОКНО ДНЦ
//   слева — сервер, станции на связи и их схемы («Добавить →»), сколько схем можно открыть (N), режим станций, журнал;
//   [ДОБАВЛЕНО 18.11.2026] станция подключилась к серверу — её схемы добавляются в ленту сами (кроме убранных ✕);
//   режим интерфейса («Медеуский» / «Павлодарский») ДНЦ держит одинаковым у всех станций;
//   справа — лента схем в линию слева направо во всю высоту окна (прокрутка вбок, колесо мыши — тоже вбок);
//   [ИЗМЕНЕНО 18.11.2026] лента — «сквозная» (Ribbon.cs): общий масштаб, пути соседних станций стыкуются,
//   перегон, нарисованный у двух станций, показывается один раз; порядок по стыкам (или как расставил диспетчер);
//   или одна станция, раскрытая на всё окно для управления (ControlView).
//   Какие схемы в ленте и в каком порядке — запоминается (Settings).
// =====================================================================

public class DncForm : Form
{
    static readonly Font F = new("Segoe UI", 10f);
    static readonly Color SideBg = Color.FromArgb(232, 236, 240);

    readonly Panel side = new() { Dock = DockStyle.Left, Width = 280, BackColor = SideBg, Padding = new Padding(8) };
    readonly TextBox txtServer = new() { Width = 170 };
    readonly Label lblStatus = new() { AutoSize = true, MaximumSize = new Size(260, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 2, 3, 8) };
    readonly ListBox lstSchemes = new() { Height = 220, IntegralHeight = false };
    readonly Button btnAdd = new() { Text = "Добавить в окно →", AutoSize = true };
    readonly NumericUpDown numMax = new() { Minimum = 1, Maximum = 30, Width = 56 };
    readonly Label lblCount = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    readonly Label lblZoom = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    readonly ListBox lstLog = new() { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Consolas", 8.5f), HorizontalScrollbar = true };

    readonly Panel main = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 44, 52) };
    readonly FlowLayoutPanel strip = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
        BackColor = Color.FromArgb(40, 44, 52), Padding = new Padding(4, 4, 4, 0), AllowDrop = true
    };
    /// <summary>Последний расчёт ленты: масштаб, при котором лента во всю высоту, и данные для «Все в окно».</summary>
    float kFit = 0.5f, w1;
    int gapsPx;
    readonly Label lblEmpty = new()
    {
        Dock = DockStyle.Fill, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 13f), TextAlign = ContentAlignment.MiddleCenter,
        Text = "Схем в окне нет.\nСтанции появятся здесь сами, как только подключатся к серверу."
    };
    readonly Button btnSide = new() { Text = "◀", Width = 22, Dock = DockStyle.Left, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 206, 212) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    readonly ToolTip tips = new();
    ControlView control;

    record SchemeEntry(string Station, string Scheme, string Text)
    {
        public override string ToString() => Text;
    }

    public DncForm()
    {
        Text = "ДНЦ — схемы станций";
        Font = F;
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize = new Size(Math.Min(800, wa.Width), Math.Min(500, wa.Height));
        Size = new Size(Math.Min(1600, wa.Width), Math.Min(900, wa.Height));
        // [ИЗМЕНЕНО 19.11.2026] как у АКЖТ: место окна задаётся здесь, до показа (PlaceOnScreens в конце конструктора),
        // без «по центру» и «развернуть» — раньше окно ставилось на два монитора уже после Load и выходило не на весь экран
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        // окно уже показано — если что-то его сдвинуло или обрезало, поставить на мониторы ещё раз
        Shown += (_, _) => { if (spanning && Bounds != spanBounds) ApplySpan(); };

        BuildSide();
        btnSide.FlatAppearance.BorderSize = 0;
        btnSide.Click += (_, _) => { side.Visible = !side.Visible; btnSide.Text = side.Visible ? "◀" : "▶"; };
        tips.SetToolTip(btnSide, "Спрятать / показать список станций (больше места схемам)");
        main.Controls.Add(strip);
        main.Controls.Add(lblEmpty);
        main.Controls.Add(consentBox);   // добавлена после ленты — встаёт сверху над ней
        Controls.Add(main);
        Controls.Add(btnSide);
        Controls.Add(side);

        strip.Resize += (_, _) => LayoutTiles();
        strip.DragOver += (_, e) => e.Effect = e.Data!.GetDataPresent(typeof(SchemeTile)) ? DragDropEffects.Move : DragDropEffects.None;
        strip.DragDrop += (_, e) => DropTile(e);

        foreach (var (st, sc) in Settings.Tiles.ToList()) AddTile(st, sc, save: false);
        UpdateEmpty();

        Net.Log += AddLog;
        Net.Changed += RefreshAll;
        Net.InfoReceived += id => { AutoAdd(id); OrderByJoins(); RefreshTiles(); };
        Net.ConsentAsked += AskConsent;
        Net.ConsentEnded += (st, id) => DropConsent(st, id);
        Net.Start(Settings.Server);
        timer.Tick += (_, _) => TickTiles();
        timer.Start();
        RefreshAll();
        PlaceOnScreens(true);   // [ДОБАВЛЕНО 19.11.2026] два монитора рядом — окно на оба, как у АКЖТ
    }

    // =================================================================
    //  ЛЕВАЯ ПАНЕЛЬ
    // =================================================================
    void BuildSide()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = SideBg };
        Label Title(string t) => new() { Text = t, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(3, 8, 3, 3) };

        // [ДОБАВЛЕНО 19.11.2026] окно: на два монитора / на один, свернуть, выход (без рамки у окна их нет)
        var winRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        Button WinBtn(string t, Action a, string tip)
        {
            var b = new Button { Text = t, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(2, 2, 2, 2), MinimumSize = new Size(30, 26) };
            b.Click += (_, _) => a();
            tips.SetToolTip(b, tip);
            winRow.Controls.Add(b);
            return b;
        }
        btnScreens = WinBtn("Два монитора", () => PlaceOnScreens(!spanning), "Окно на оба монитора (как у АКЖТ) или на один. Клавиша F11 — то же");
        WinBtn("Свернуть", () => WindowState = FormWindowState.Minimized, "Свернуть окно ДНЦ");
        WinBtn("Выход", Close, "Закрыть программу ДНЦ (управление станциям вернётся)");
        flow.Controls.Add(winRow);

        flow.Controls.Add(Title("Сервер связи"));
        var srv = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        txtServer.Text = Settings.Server;
        var bConnect = new Button { Text = "Подключить", AutoSize = true };
        bConnect.Click += (_, _) =>
        {
            Settings.Server = txtServer.Text.Trim();
            Settings.Save();
            Net.Start(Settings.Server);
        };
        tips.SetToolTip(txtServer, "Адрес ПК, где работает сервер PromServer (обычно ПК преподавателя), например 192.168.1.5");
        srv.Controls.Add(txtServer);
        srv.Controls.Add(bConnect);
        flow.Controls.Add(srv);
        flow.Controls.Add(lblStatus);

        flow.Controls.Add(Title("Схемы станций на связи"));
        lstSchemes.Width = 262;
        lstSchemes.DoubleClick += (_, _) => AddSelected();
        tips.SetToolTip(lstSchemes, "Двойной клик или «Добавить в окно →» — схема появится справа в ленте");
        flow.Controls.Add(lstSchemes);
        btnAdd.Click += (_, _) => AddSelected();
        flow.Controls.Add(btnAdd);

        var maxRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        maxRow.Controls.Add(new Label { Text = "Можно открыть схем:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        numMax.Value = Settings.Max;
        numMax.ValueChanged += (_, _) =>
        {
            Settings.Max = (int)numMax.Value;
            Settings.Save();
            maxWarned = false;
            foreach (var st in Net.Stations.Values.Where(s => s.Online)) AutoAdd(st.Id);   // место появилось — добавить ожидающие
            RefreshAll();
        };
        maxRow.Controls.Add(numMax);
        flow.Controls.Add(maxRow);
        flow.Controls.Add(lblCount);

        // масштаб ленты: схема во всю высоту окна или меньше (чтобы все станции поместились по ширине)
        flow.Controls.Add(Title("Масштаб схем"));
        var zoomRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        Button Small(string t, Action a) { var b = new Button { Text = t, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(30, 26), Margin = new Padding(2, 3, 2, 3) }; b.Click += (_, _) => a(); return b; }
        zoomRow.Controls.Add(Small("−", () => SetZoom(Settings.Zoom / 1.15f)));
        zoomRow.Controls.Add(lblZoom);
        zoomRow.Controls.Add(Small("+", () => SetZoom(Settings.Zoom * 1.15f)));
        var bFit = Small("Все в окно", FitAll);
        tips.SetToolTip(bFit, "Уменьшить схемы так, чтобы все поместились в окно по ширине. Ctrl + колесо мыши — тоже масштаб");
        zoomRow.Controls.Add(bFit);
        flow.Controls.Add(zoomRow);
        var bOrder = new Button { Text = "Порядок по стыкам путей", AutoSize = true, Margin = new Padding(2, 3, 2, 3) };
        bOrder.Click += (_, _) => { Settings.ManualOrder = false; OrderByJoins(); SaveTiles(); };
        tips.SetToolTip(bOrder, "Расставить схемы по стыкам путей (АКЖТ → путевой пост → Алгабас). " +
                                "Пока порядок не меняли вручную, он и так ставится сам");
        flow.Controls.Add(bOrder);
        // [ДОБАВЛЕНО 18.11.2026] управление всем участком: все станции на связи, сквозные маршруты между ними
        var bArea = new Button { Text = "Управлять участком", AutoSize = true, Margin = new Padding(2, 8, 2, 3),
                                 FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(219, 0, 105), ForeColor = Color.White };
        bArea.FlatAppearance.BorderSize = 0;
        bArea.Click += (_, _) => OpenArea();
        tips.SetToolTip(bArea, "Забрать управление у всех станций в ленте: маршруты на любой станции, сквозные маршруты " +
                               "между станциями (светофор отправления на одной, путь приёма на другой), смена направления перегонов");
        flow.Controls.Add(bArea);
        lblZoom.Text = $"{Settings.Zoom * 100:0}%";
        flow.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(262, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 3),
            Text = "Порядок — перетащите схему за заголовок или ◀ ▶. «Управлять» — станция на всё окно, управление у ДНЦ."
        });

        // [ДОБАВЛЕНО 18.11.2026] режим интерфейса — один у всех станций
        flow.Controls.Add(Title("Режим станций"));
        var rbWork = new RadioButton { Text = "Медеуский (рабочий)", AutoSize = true, Checked = Settings.Mode != "Classic", Margin = new Padding(3, 2, 3, 0) };
        var rbClassic = new RadioButton { Text = "Павлодарский", AutoSize = true, Checked = Settings.Mode == "Classic", Margin = new Padding(3, 2, 3, 0) };
        rbWork.CheckedChanged += (_, _) => { if (rbWork.Checked) Net.SetModeAll("Work"); };
        rbClassic.CheckedChanged += (_, _) => { if (rbClassic.Checked) Net.SetModeAll("Classic"); };
        tips.SetToolTip(rbWork, "Как АРМ ДСП станции Медеу: крупная схема, пульт полосой — у всех станций сразу");
        tips.SetToolTip(rbClassic, "Как тренажёр Павлодарского колледжа (пульт «Енисей») — у всех станций сразу");
        flow.Controls.Add(rbWork);
        flow.Controls.Add(rbClassic);
        flow.Controls.Add(Title("Журнал"));

        side.Controls.Add(lstLog);
        side.Controls.Add(flow);
    }

    void AddLog(string m)
    {
        lstLog.Items.Add(m);
        while (lstLog.Items.Count > 300) lstLog.Items.RemoveAt(0);
        lstLog.TopIndex = lstLog.Items.Count - 1;
    }

    void RefreshAll()
    {
        if (IsDisposed) return;
        lblStatus.Text = Net.StatusText;
        lblStatus.ForeColor = StationApp.Core.DispatchLink.IsConnected ? Color.FromArgb(0, 120, 50) : Color.FromArgb(190, 30, 30);

        var sel = lstSchemes.SelectedItem as SchemeEntry;
        var entries = new List<SchemeEntry>();
        foreach (var st in Net.Stations.Values.Where(s => s.Online).OrderBy(s => s.Title))
        {
            if (!st.InfoReceived) { entries.Add(new SchemeEntry(st.Id, "", $"{st.Title} — ждём ответа…")); continue; }
            if (st.Schemes.Count == 0) { entries.Add(new SchemeEntry(st.Id, "", $"{st.Title} — схем нет (старая версия?)")); continue; }
            foreach (var sc in st.Schemes)
            {
                bool shown = Tiles.Any(t => t.StationId == st.Id && t.SchemeName == sc.Name);
                bool hidden = Settings.Hidden.Contains(Settings.Key(st.Id, sc.Name));
                string name = st.Schemes.Count > 1 ? $"{sc.Name}  ({st.Title})" : sc.Name;
                entries.Add(new SchemeEntry(st.Id, sc.Name, name + (shown ? "   ✓ в окне" : hidden ? "   — убрана" : "")));
            }
        }
        if (!entries.Select(e => e.Text).SequenceEqual(lstSchemes.Items.Cast<SchemeEntry>().Select(e => e.Text)))
        {
            lstSchemes.BeginUpdate();
            lstSchemes.Items.Clear();
            foreach (var e in entries) lstSchemes.Items.Add(e);
            var again = entries.FirstOrDefault(e => sel != null && e.Station == sel.Station && e.Scheme == sel.Scheme);
            if (again != null) lstSchemes.SelectedItem = again;
            lstSchemes.EndUpdate();
        }
        if (entries.Count == 0 && lstSchemes.Items.Count > 0) lstSchemes.Items.Clear();

        int n = Tiles.Count();
        lblCount.Text = $"Схем в окне: {n} из {Settings.Max}";
        lblCount.ForeColor = n >= Settings.Max ? Color.FromArgb(190, 30, 30) : Color.Black;
        btnAdd.Enabled = n < Settings.Max;
        RefreshTiles();
    }

    void RefreshTiles()
    {
        foreach (var t in Tiles) t.RefreshState();
        LayoutTiles();
    }

    // =================================================================
    //  ЛЕНТА СХЕМ
    // =================================================================
    IEnumerable<SchemeTile> Tiles => strip.Controls.OfType<SchemeTile>();

    void AddSelected()
    {
        if (lstSchemes.SelectedItem is not SchemeEntry e || e.Scheme.Length == 0) return;
        if (Tiles.Any(t => t.StationId == e.Station && t.SchemeName == e.Scheme))
        {
            AddLog($"Схема «{e.Scheme}» уже в окне");
            return;
        }
        if (Tiles.Count() >= Settings.Max)
        {
            MessageBox.Show(this, $"В окне уже {Settings.Max} схем — это предел. Уберите схему (✕) или увеличьте «Можно открыть схем».",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Settings.Hidden.Remove(Settings.Key(e.Station, e.Scheme));   // вернули вручную — снова добавляется сама
        AddTile(e.Station, e.Scheme, save: true);
    }

    bool maxWarned;

    /// <summary>[ДОБАВЛЕНО 18.11.2026] Станция ответила, какие у неё схемы, — добавить в ленту те, которых там нет
    /// (кроме убранных диспетчером), пока не набралось N.</summary>
    void AutoAdd(string id)
    {
        if (IsDisposed || Net.Get(id) is not { Online: true } st) return;
        foreach (var sc in st.Schemes)
        {
            if (Tiles.Any(t => t.StationId == id && t.SchemeName == sc.Name)) continue;
            if (Settings.Hidden.Contains(Settings.Key(id, sc.Name))) continue;
            if (Tiles.Count() >= Settings.Max)
            {
                if (!maxWarned) AddLog($"Схема «{sc.Name}» не добавлена: в окне уже {Settings.Max} схем (можно увеличить слева)");
                maxWarned = true;
                return;
            }
            AddTile(id, sc.Name, save: true);
            AddLog($"Добавлена схема: {sc.Name}");
        }
    }

    void AddTile(string station, string scheme, bool save)
    {
        var t = new SchemeTile(station, scheme);
        t.ControlClicked += x => { if (x.Controlled) CloseControl(); else OpenControl(x); };
        t.Intercept = (x, act, dx, dy, btn) => control?.OnTileInput(x, act, dx, dy, btn) ?? false;
        t.CloseClicked += x =>
        {
            Settings.Hidden.Add(Settings.Key(x.StationId, x.SchemeName));   // сама больше не добавится
            strip.Controls.Remove(x);
            x.Dispose();
            SaveTiles();
        };
        t.MoveClicked += (x, d) =>
        {
            int i = strip.Controls.GetChildIndex(x) + d;
            if (i < 0 || i >= strip.Controls.Count) return;
            strip.Controls.SetChildIndex(x, i);
            Settings.ManualOrder = true;   // порядок задал диспетчер — сам больше не меняется (кнопка «Порядок по стыкам»)
            SaveTiles();
        };
        t.View.FreeWheel += ScrollStrip;
        AllowDropDeep(t);
        strip.Controls.Add(t);
        if (save) SaveTiles();
        LayoutTiles();
        UpdateEmpty();
    }

    /// <summary>Перетаскивание работает над любой частью плитки (картинка, заголовок) — передаём ленте.</summary>
    void AllowDropDeep(Control c)
    {
        c.AllowDrop = true;
        c.DragOver += (_, e) => e.Effect = e.Data!.GetDataPresent(typeof(SchemeTile)) ? DragDropEffects.Move : DragDropEffects.None;
        c.DragDrop += (_, e) => DropTile(e);
        foreach (Control ch in c.Controls) AllowDropDeep(ch);
    }

    void DropTile(DragEventArgs e)
    {
        if (e.Data!.GetData(typeof(SchemeTile)) is not SchemeTile moving) return;
        var p = strip.PointToClient(new Point(e.X, e.Y));
        // куда встать: перед первой плиткой, середина которой правее курсора
        var others = Tiles.Where(t => t != moving).ToList();
        int idx = others.FindIndex(t => t.Left + t.Width / 2 > p.X);
        if (idx < 0) idx = others.Count;
        strip.Controls.SetChildIndex(moving, idx);
        Settings.ManualOrder = true;
        SaveTiles();
    }

    void SaveTiles()
    {
        Settings.Tiles.Clear();
        Settings.Tiles.AddRange(Tiles.Select(t => (t.StationId, t.SchemeName)));
        Settings.Save();
        UpdateEmpty();
        RefreshAll();
    }

    void UpdateEmpty()
    {
        bool none = !Tiles.Any();
        lblEmpty.Visible = none && control == null;
        if (control == null) strip.Visible = !none;   // при управлении ленту показывает / прячет ControlView
    }

    /// <summary>Высота ленты под плитки (минус полоса прокрутки).</summary>
    int FullHeight => strip.ClientSize.Height - strip.Padding.Vertical - SystemInformation.HorizontalScrollBarHeight - 2;

    /// <summary>[ИЗМЕНЕНО 18.11.2026] Сквозная лента (Ribbon): общий масштаб k для всех схем — при Zoom = 1 лента
    /// во всю высоту окна; схемы встык там, где пути стыкуются, по высоте — сдвиг, чтобы пути легли в линию.</summary>
    void LayoutTiles()
    {
        int availH = FullHeight - SchemeTile.HeadH;
        if (availH < 60) return;
        var tiles = Tiles.ToList();
        var (list, W1, H1, gaps, unknown) = Ribbon.Compute(tiles);
        kFit = H1 > 0 ? availH / H1 : 0.5f;
        w1 = W1;
        gapsPx = gaps * Ribbon.Gap + unknown * Ribbon.UnknownW;
        float k = kFit * Settings.Zoom;
        int stripH = H1 > 0 ? (int)Math.Ceiling(H1 * k) : (int)(availH * Settings.Zoom);
        strip.SuspendLayout();
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            var m = new Padding(i == 0 || p.JoinedLeft ? 0 : Ribbon.Gap, 0, 0, 0);
            if (p.Tile.Margin != m) p.Tile.Margin = m;
            int w = p.Known ? Math.Max(8, (int)Math.Round((p.X1 - p.X0) * k)) : Ribbon.UnknownW;
            var size = new Size(w, stripH + SchemeTile.HeadH);
            if (p.Tile.Size != size) p.Tile.Size = size;
            if (p.Known) p.Tile.Place(k, p.X0, p.X1, (int)Math.Round(p.Top * k), stripH);
            else p.Tile.Place(0, 0, 0, 0, stripH);
        }
        strip.ResumeLayout();
        // лента стала короче (масштаб меньше) — прокрутка не должна остаться за её концом (пустое место)
        int maxX = Math.Max(0, strip.DisplayRectangle.Width - strip.ClientSize.Width);
        if (-strip.AutoScrollPosition.X > maxX) strip.AutoScrollPosition = new Point(maxX, 0);
    }

    /// <summary>Порядок по стыкам путей, пока диспетчер не расставил схемы сам.</summary>
    void OrderByJoins()
    {
        if (Settings.ManualOrder) return;
        var tiles = Tiles.ToList();
        var order = Ribbon.AutoOrder(tiles);
        if (order.SequenceEqual(tiles)) return;
        strip.SuspendLayout();
        for (int i = 0; i < order.Count; i++) strip.Controls.SetChildIndex(order[i], i);
        strip.ResumeLayout();
        Settings.Tiles.Clear();
        Settings.Tiles.AddRange(Tiles.Select(t => (t.StationId, t.SchemeName)));
        Settings.Save();
        LayoutTiles();
    }

    void SetZoom(float z)
    {
        z = Math.Clamp(z, 0.05f, 1f);
        if (Math.Abs(z - Settings.Zoom) < 0.001f) return;
        Settings.Zoom = z;
        Settings.Save();
        lblZoom.Text = $"{z * 100:0}%";
        LayoutTiles();
    }

    /// <summary>«Все в окно»: общий масштаб такой, чтобы вся лента поместилась по ширине.</summary>
    void FitAll()
    {
        LayoutTiles();
        if (w1 <= 0 || kFit <= 0) return;
        int free = strip.ClientSize.Width - strip.Padding.Horizontal - gapsPx - 4;
        SetZoom(free / w1 / kFit);
    }

    void ScrollStrip(int delta)
    {
        if ((ModifierKeys & Keys.Control) != 0) { SetZoom(Settings.Zoom * (delta > 0 ? 1.1f : 1 / 1.1f)); return; }
        int x = -strip.AutoScrollPosition.X - delta;
        strip.AutoScrollPosition = new Point(Math.Max(0, x), 0);
    }

    void TickTiles()
    {
        if (WindowState == FormWindowState.Minimized || !strip.Visible) return;   // [ИЗМЕНЕНО 18.11.2026] и при управлении
        var view = strip.ClientRectangle;
        foreach (var t in Tiles) t.Tick(view.IntersectsWith(t.Bounds));
    }

    // =================================================================
    //  УПРАВЛЕНИЕ: станция на всё окно
    // =================================================================
    /// <summary>[ИЗМЕНЕНО 18.11.2026] Управление: лента станций остаётся сверху (со скроллом), кнопки пульта — снизу.</summary>
    /// <summary>«Управлять» на схеме: эта станция (у АКЖТ — обе её схемы, это одна программа).</summary>
    void OpenControl(SchemeTile t)
    {
        if (t.Station is not { Online: true }) return;
        StartControl(new[] { t.StationId }, t);
        AddLog($"Управление станцией: {t.Station.Title}");
    }

    /// <summary>[ДОБАВЛЕНО 18.11.2026] «Управлять участком»: все станции в ленте, что на связи.</summary>
    void OpenArea()
    {
        var ids = Tiles.Select(x => x.StationId).Distinct().Where(id => Net.Get(id) is { Online: true }).ToList();
        if (ids.Count == 0) { AddLog("Управлять нечем: станций на связи в ленте нет"); return; }
        var first = Tiles.First(x => ids.Contains(x.StationId));
        StartControl(ids, first);
        AddLog($"Управление участком: {string.Join(", ", ids.Select(id => Net.Get(id)?.Title ?? id))}");
    }

    void StartControl(IEnumerable<string> stations, SchemeTile active)
    {
        CloseControl();
        strip.Visible = true;
        control = new ControlView(stations, active.StationId, active.SchemeName, strip) { Dock = DockStyle.Fill };
        control.BackClicked += CloseControl;
        control.MirrorSized += () => BeginInvoke(() => { if (control == null) return; LayoutTiles(); strip.PerformLayout(); ScrollTo(active); });
        main.Controls.Add(control);
        control.BringToFront();
        foreach (var x in Tiles) { x.Controlled = control.Stations.Contains(x.StationId); x.RefreshState(); }
        UpdateEmpty();
        // лента стала ниже (снизу кнопки) — масштаб и ширины плиток другие; прокручиваем, когда раскладка устоялась
        BeginInvoke(() => { if (control == null) return; LayoutTiles(); strip.PerformLayout(); ScrollTo(active); });
    }

    void CloseControl()
    {
        if (control == null) return;
        control.Release();
        control.ReturnRibbon(main);
        main.Controls.Remove(control);
        control.Dispose();
        control = null;
        foreach (var x in Tiles) { x.Controlled = false; x.RefreshState(); }
        UpdateEmpty();
        LayoutTiles();
    }

    // =================================================================
    //  [ДОБАВЛЕНО 18.11.2026] СОГЛАСИЕ НА СМЕНУ НАПРАВЛЕНИЯ: пока ДНЦ на связи, станции спрашивают его
    // =================================================================
    readonly FlowLayoutPanel consentBox = new()
    {
        Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        BackColor = Color.FromArgb(255, 236, 160), Visible = false, Padding = new Padding(6, 4, 6, 4)
    };
    readonly Dictionary<(string, int), Control> consentRows = new();

    void AskConsent(ConsentAsk a)
    {
        if (IsDisposed) return;
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        var lbl = new Label
        {
            AutoSize = true, MaximumSize = new Size(Math.Max(400, consentBox.Width - 360), 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Text = $"{a.Title} — станция «{a.StationName}»: " + a.Text.Replace("\n\n", "  ").Replace("\n", " "), Margin = new Padding(2, 6, 8, 2)
        };
        Button Btn(string t, Color c, bool ok)
        {
            var b = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = c, ForeColor = Color.White, Margin = new Padding(4, 2, 0, 2) };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => { Net.Consent(a, ok, ok ? "" : "ДНЦ отказал"); DropConsent(a.Station, a.Id); };
            return b;
        }
        var timerLbl = new Label { AutoSize = true, Margin = new Padding(8, 8, 2, 2), ForeColor = Color.FromArgb(150, 60, 0) };
        row.Controls.AddRange(new Control[] { lbl, Btn("Дать согласие", Color.FromArgb(0, 140, 60), true), Btn("Отказать", Color.FromArgb(190, 30, 30), false), timerLbl });
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        t.Tick += (_, _) =>
        {
            long left = (a.Until - Environment.TickCount64) / 1000;
            if (left < 0 || row.IsDisposed) { t.Dispose(); DropConsent(a.Station, a.Id); return; }
            timerLbl.Text = $"осталось {left} с";
        };
        t.Start();
        row.Disposed += (_, _) => t.Dispose();
        consentRows[(a.Station, a.Id)] = row;
        consentBox.Controls.Add(row);
        consentBox.Visible = true;
        AddLog($"Вопрос: {a.Title} — станция «{a.StationName}»");
    }

    void DropConsent(string station, int id)
    {
        if (!consentRows.Remove((station, id), out var row)) return;
        consentBox.Controls.Remove(row);
        row.Dispose();
        consentBox.Visible = consentRows.Count > 0;
    }

    /// <summary>Прокрутить ленту так, чтобы плитка была посередине окна.</summary>
    void ScrollTo(SchemeTile t)
    {
        int abs = t.Left - strip.AutoScrollPosition.X;
        strip.AutoScrollPosition = new Point(Math.Max(0, abs + t.Width / 2 - strip.ClientSize.Width / 2), 0);
    }

    // =================================================================
    //  [ДОБАВЛЕНО 19.11.2026] ОКНО НА ДВА МОНИТОРА — как у АКЖТ
    // =================================================================
    bool spanning;
    Button btnScreens;

    /// <summary>Второй монитор стоит слева или справа от основного (одной полосой) — окно без рамки на рабочие области
    /// обоих (без панели задач), лента схем идёт через оба. Иначе (или по кнопке «Один монитор») — развёрнуто на одном.</summary>
    /// <summary>Монитор: весь экран, рабочая область (без панели задач), основной ли.</summary>
    record Mon(Rectangle Bounds, Rectangle Work, bool Primary);

    /// <summary>Мониторы. Для проверки без второго монитора — переменная окружения DNC_FAKE_SCREENS
    /// ("x,y,w,h;x,y,w,h", первый — основной; панель задач 40 px внизу каждого).</summary>
    static List<Mon> Monitors()
    {
        var fake = Environment.GetEnvironmentVariable("DNC_FAKE_SCREENS");
        if (!string.IsNullOrWhiteSpace(fake))
            return fake.Split(';').Select((t, i) =>
            {
                var v = t.Split(',').Select(int.Parse).ToArray();
                var r = new Rectangle(v[0], v[1], v[2], v[3]);
                return new Mon(r, new Rectangle(r.X, r.Y, r.Width, r.Height - 40), i == 0);
            }).ToList();
        return Screen.AllScreens.Select(s => new Mon(s.Bounds, s.WorkingArea, s.Primary)).ToList();
    }

    void PlaceOnScreens(bool twoScreens)
    {
        var screens = Monitors();
        var primary = screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        var second = screens.Where(s => s != primary)
                            .OrderBy(s => Math.Abs(s.Bounds.Left - primary.Bounds.Right) + Math.Abs(s.Bounds.Right - primary.Bounds.Left))
                            .FirstOrDefault();
        bool sideBySide = second != null && (second.Bounds.Left == primary.Bounds.Right || second.Bounds.Right == primary.Bounds.Left);
        if (twoScreens && sideBySide)
        {
            spanning = true;
            var l = primary.Bounds.Left < second.Bounds.Left ? primary : second;
            var r = l == primary ? second : primary;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            int top = Math.Max(l.Work.Top, r.Work.Top);
            int bottom = Math.Min(l.Work.Bottom, r.Work.Bottom);   // чтобы окно не уходило под панель задач
            spanBounds = new Rectangle(l.Work.Left, top, r.Work.Right - l.Work.Left, bottom - top);
            Bounds = spanBounds;
            ApplySpan();
        }
        else
        {
            if (twoScreens && !sideBySide && screens.Count > 1)
                AddLog("Второй монитор не стоит рядом с основным (слева или справа) — окно на одном мониторе");
            spanning = false;
            spanBounds = Rectangle.Empty;
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
        }
        if (btnScreens != null)
        {
            btnScreens.Text = spanning ? "Один монитор" : "Два монитора";
            btnScreens.Enabled = spanning || sideBySide;
        }
        LayoutTiles();
    }

    /// <summary>Где окно на двух мониторах (пусто — не растянуто).</summary>
    Rectangle spanBounds;

    /// <summary>[ДОБАВЛЕНО 19.11.2026] Поставить окно на оба монитора напрямую (SetWindowPos): WinForms сама обрезает размер
    /// окна по SystemInformation.MaxWindowTrackSize — на мониторах с разным масштабом окно выходило не на весь экран.</summary>
    void ApplySpan()
    {
        if (!spanning || spanBounds.IsEmpty || !IsHandleCreated) return;
        SetWindowPos(Handle, IntPtr.Zero, spanBounds.X, spanBounds.Y, spanBounds.Width, spanBounds.Height, 0x0004 | 0x0010);   // без Z-порядка и активации
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplySpan();
    }

    /// <summary>[ДОБАВЛЕНО 19.11.2026] Windows ограничивает размер окна своим «максимумом» (на мониторах с разным
    /// масштабом он бывает меньше двух экранов) — окно на два монитора обрезалось. Пока окно растянуто,
    /// отвечаем Windows, что максимум — оба монитора целиком.</summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WM_GETMINMAXINFO = 0x24;
        if (m.Msg != WM_GETMINMAXINFO || !spanning || spanBounds.IsEmpty) return;
        var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(m.LParam);
        mmi.MaxSize = new Point(spanBounds.Width, spanBounds.Height);
        mmi.MaxPosition = Point.Empty;
        mmi.MaxTrackSize = new Point(Math.Max(mmi.MaxTrackSize.X, spanBounds.Width), Math.Max(mmi.MaxTrackSize.Y, spanBounds.Height));
        System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, m.LParam, false);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11)
        {
            PlaceOnScreens(!spanning);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CloseControl();   // вернуть управление станции
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Dispose();
        DispatchLink.Stop(true);
        base.OnFormClosed(e);
    }
}
