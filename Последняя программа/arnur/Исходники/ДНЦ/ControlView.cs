namespace DncApp;

// =====================================================================
//  УПРАВЛЕНИЕ СТАНЦИЯМИ  [ИЗМЕНЕНО 18.11.2026]
//   • управлять можно одной станцией («Управлять» на её схеме — у АКЖТ сразу обе схемы, АКЖТ и пост: это
//     одна программа) или всем участком («Управлять участком» — все станции на связи);
//   • сверху — та же лента станций, что в обзоре, со скроллом; схемы станций под управлением обведены:
//     мышь над ними уходит на станцию точкой чертежа (t_input scheme, dx, dy) — схема чёткая, как в обзоре;
//     начало маршрута на одной схеме, конец на другой схеме той же программы — станция соберёт маршрут сама;
//   • СКВОЗНОЙ МАРШРУТ: светофор отправления на одной станции, путь приёма на другой — ДНЦ сам меняет направление
//     общего перегона (если надо; согласие соседа даёт сам), задаёт маршрут отправления и маршрут приёма;
//   • снизу — копия пульта и панель «Маршруты и перегоны» той схемы, по которой кликнули последней;
//   • на станции открылось окно-вопрос или меню (или нажато «Показать всё окно станции») — вместо ленты снимок
//     всего окна этой станции, чтобы на вопрос можно было ответить.
//  Пока открыт этот вид — управление пультами станций у ДНЦ (t_take), дежурные на станциях не нажимают.
// =====================================================================

public class ControlView : Panel
{
    /// <summary>Станции под управлением (Id на сервере).</summary>
    public readonly HashSet<string> Stations;
    /// <summary>Станция и схема, чьи кнопки пульта и маршруты показаны снизу.</summary>
    public string StationId { get; private set; }
    public string SchemeName { get; private set; }

    readonly FlowLayoutPanel bar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Color.FromArgb(150, 40, 30), Padding = new Padding(4, 3, 4, 3) };
    readonly Label lbl = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Margin = new Padding(10, 7, 16, 0) };
    readonly Label lblStatus = new() { AutoSize = true, ForeColor = Color.FromArgb(255, 235, 150), Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(10, 8, 6, 0) };
    readonly Button bBack, bWhole;
    readonly Panel bottom = new() { Dock = DockStyle.Bottom, Height = 260 };
    readonly UiMirror mirror = new() { Dock = DockStyle.Fill };
    readonly RoutesPanel routes = new() { Dock = DockStyle.Right, Width = 440 };
    // граница между лентой и низом — её можно потянуть мышью (тогда высота низа больше не подбирается сама)
    readonly Splitter split = new() { Dock = DockStyle.Bottom, Height = 6, BackColor = Color.FromArgb(90, 96, 104), MinExtra = 120, MinSize = 120 };
    bool userSized;
    readonly Panel middle = new() { Dock = DockStyle.Fill };
    /// <summary>Лента станций из главного окна — пока открыт этот вид, она живёт здесь.</summary>
    readonly Control ribbon;
    /// <summary>Снимок всего окна станции — когда там окно-вопрос / меню или выбрано «всё окно».</summary>
    readonly ImageView shot = new() { Dock = DockStyle.Fill, Visible = false, Placeholder = "Ожидание снимка окна станции…" };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    readonly string tagBase = "ctl" + Guid.NewGuid().ToString("N")[..6];
    int seq;
    bool awaiting, wholeWindow, throughBusy;
    string shotStation;
    long requestedAt, nextAt, nextInfo;

    public event Action BackClicked;
    /// <summary>Высота низа впервые подобрана (лента стала ниже) — окну пора прокрутить ленту к станции.</summary>
    public event Action MirrorSized;
    bool sizedOnce;

    public ControlView(IEnumerable<string> stations, string activeStation, string activeScheme, Control ribbonStrip)
    {
        Stations = stations.ToHashSet();
        BackColor = Color.FromArgb(40, 44, 52);

        bBack = BarButton("← Вернуть управление, к обзору");
        bBack.Click += (_, _) => BackClicked?.Invoke();
        bWhole = BarButton("Показать всё окно станции");
        bWhole.Click += (_, _) => { wholeWindow = !wholeWindow; nextAt = 0; RefreshState(); };
        bar.Controls.AddRange(new Control[] { bBack, lbl, bWhole, lblStatus });

        ribbon = ribbonStrip;
        middle.Controls.Add(shot);
        middle.Controls.Add(ribbon);   // забираем ленту из главного окна (ReturnRibbon — отдать обратно)
        bottom.Controls.Add(mirror);
        bottom.Controls.Add(routes);
        Controls.Add(middle);
        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(bar);
        split.SplitterMoved += (_, _) => userSized = true;

        mirror.Clicked += (id, right) => Net.Click(StationId, id, right);
        routes.Status += Say;
        shot.Input += d => { if (shotStation != null) Net.Input(shotStation, d); };
        Net.ScreenReceived += OnScreen;
        Net.InfoReceived += OnInfo;
        timer.Tick += (_, _) => Loop();
        timer.Start();
        Disposed += (_, _) =>
        {
            timer.Dispose();
            Net.ScreenReceived -= OnScreen;
            Net.InfoReceived -= OnInfo;
        };

        foreach (var id in Stations) { Net.Take(id, true); Net.RequestInfo(id, false); }
        SetActive(activeStation, activeScheme);
    }

    static Button BarButton(string t)
    {
        var b = new Button
        {
            Text = t, AutoSize = true, Margin = new Padding(4, 2, 0, 2), FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(240, 240, 255), Font = new Font("Segoe UI", 9.5f)
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    /// <summary>Вернуть управление станциям (при выходе из вида и закрытии программы).</summary>
    public void Release() { foreach (var id in Stations) Net.Take(id, false); }

    /// <summary>Отдать ленту обратно главному окну (перед закрытием вида).</summary>
    public void ReturnRibbon(Control host)
    {
        ribbon.Visible = true;
        host.Controls.Add(ribbon);
        ribbon.BringToFront();
    }

    /// <summary>Кнопки пульта и маршруты снизу — этой схемы (по ней кликнули последней).</summary>
    public void SetActive(string station, string scheme)
    {
        if (station == StationId && scheme == SchemeName) return;
        StationId = station;
        SchemeName = scheme;
        routes.StationId = station;
        routes.SchemeName = scheme;
        routes.Stations = Stations;
        nextInfo = 0;
        if (Net.Get(station) is { Ui: not null } st) mirror.SetItems(st.Ui.Where(u => u.P == scheme).ToList(), st.Bg);
        routes.RefreshData();
        RefreshState();
    }

    public void Say(string text)
    {
        if (IsDisposed) return;
        lblStatus.Text = text;
        Net.Say(text);
    }

    StationRt Shown => Net.Get(StationId);

    /// <summary>Станция, у которой открыто окно-вопрос / меню (сначала та, чьи кнопки снизу).</summary>
    string PopupStation => Shown is { Popup: true } ? StationId : Stations.FirstOrDefault(id => Net.Get(id) is { Popup: true });

    bool ShowShot => wholeWindow || PopupStation != null;

    void RefreshState()
    {
        if (IsDisposed) return;
        var names = Stations.Select(id => Net.Get(id)?.Title ?? id).ToList();
        bool all = Stations.All(id => Net.Get(id) is { Online: true, Control: true });
        string who = all ? "управление у ДНЦ" : "забираю управление…";
        lbl.Text = (Stations.Count > 1 ? $"Участок ({string.Join(", ", names)})" : names.FirstOrDefault() ?? "") + $": {who};  кнопки — {SchemeName}";
        bWhole.Text = wholeWindow ? "Показать ленту схем" : "Показать всё окно станции";
        bar.BackColor = all ? Color.FromArgb(150, 40, 30) : Color.FromArgb(35, 60, 80);
        shotStation = wholeWindow ? StationId : PopupStation;
        shot.InputEnabled = shotStation != null && Net.Get(shotStation) is { Online: true, Control: true };
        // лента или снимок всего окна станции
        bool showShot = ShowShot;
        if (shot.Visible != showShot)
        {
            shot.Visible = showShot;
            ribbon.Visible = !showShot;
            if (showShot) { shot.BringToFront(); if (!wholeWindow) Say($"На станции «{Net.Get(shotStation)?.Title}» открыто окно-вопрос — ответьте в нём"); }
        }
    }

    void OnInfo(string id)
    {
        if (IsDisposed || !Stations.Contains(id)) return;
        var st = Net.Get(id);
        if (!st.Control && st.Online) Net.Take(id, true);   // станция перезапустилась — забрать снова
        if (id == StationId)
        {
            if (st.Ui != null) mirror.SetItems(st.Ui.Where(u => u.P == SchemeName).ToList(), st.Bg);
            routes.RefreshData();
            // низ — по кнопкам пульта (не ниже 220), но не больше 50 % высоты, остальное — ленте (пока границу не тянули)
            if (!userSized)
            {
                int want = Math.Clamp(mirror.PreferredHeightFor(Math.Max(200, Width - routes.Width)), 220, Math.Max(220, (int)(Height * 0.5)));
                if (bottom.Height != want) bottom.Height = want;
                if (!sizedOnce) { sizedOnce = true; MirrorSized?.Invoke(); }
            }
        }
        else routes.RefreshData();   // перегоны соседей тоже показаны
        RefreshState();
    }

    void Loop()
    {
        long now = Environment.TickCount64;
        if (now >= nextInfo)
        {
            nextInfo = now + 500;
            foreach (var id in Stations) Net.RequestInfo(id, id == StationId, id == StationId);
        }
        string sid = shotStation;
        if (!ShowShot || sid == null || shot.Width < 60 || Net.Get(sid) is not { Online: true }) return;
        if (awaiting && now - requestedAt < 4000) return;
        if (now < nextAt) return;
        awaiting = true;
        requestedAt = now;
        nextAt = now + 200;
        Net.RequestScreen(sid, Math.Clamp(shot.Width, 640, 3840), new RectangleF(0, 0, 1, 1), tagBase + "_" + (++seq));
    }

    void OnScreen(string id, string tag, Image img)
    {
        if (!tag.StartsWith(tagBase)) return;
        if (IsDisposed) { img.Dispose(); return; }
        awaiting = false;
        shot.Frame = new RectangleF(0, 0, 1, 1);
        shot.SetImage(img);
    }

    // =================================================================
    //  МЫШЬ НАД СХЕМОЙ В ЛЕНТЕ: какая схема активна; сквозной маршрут между станциями
    // =================================================================
    bool swallowUp;

    /// <summary>Клик по схеме станции под управлением. true — клик забран (сквозной маршрут), станции не отправлять.</summary>
    public bool OnTileInput(SchemeTile t, string act, double dx, double dy, string btn)
    {
        if (!Stations.Contains(t.StationId)) return true;
        if (act == "down") SetActive(t.StationId, t.SchemeName);
        if (btn != "left" || act is not ("down" or "up")) return false;
        if (act == "up" && swallowUp) { swallowUp = false; return true; }
        // на другой станции под управлением начат набор маршрута — этот клик = путь приёма сквозного маршрута
        var from = Stations.Where(id => id != t.StationId).Select(Net.Get).FirstOrDefault(s => s is { Online: true, Sel.Count: > 0 });
        if (from == null || act != "down") return false;
        swallowUp = true;
        if (!throughBusy) _ = Through(from, t, dx, dy);
        return true;
    }

    /// <summary>Сквозной маршрут: отправление на станции a (от выбранного светофора) на общий перегон, приём на станции
    /// схемы bt до точки (dx, dy). Направление перегона, если нужно, меняет ДНЦ (согласие соседа — сам).</summary>
    async Task Through(StationRt a, SchemeTile bt, double dx, double dy)
    {
        throughBusy = true;
        try
        {
            var b = bt.Station;
            var wires = a.Lines.Where(l => l.Wire.Length > 0).Select(l => l.Wire)
                         .Intersect(b.Lines.Where(l => l.Wire.Length > 0).Select(l => l.Wire)).ToList();
            if (wires.Count == 0) { Say($"Между «{a.Title}» и «{b.Title}» нет общего однопутного перегона — сквозной маршрут не задать"); return; }
            string err = "";
            foreach (var wire in wires)
            {
                var dep = await Net.Call(a.Id, "t_depart", new() { ["wire"] = wire });
                if (!dep.Ok) { err = dep.Reason; continue; }
                var arr = await Net.Call(b.Id, "t_arrive", new() { ["wire"] = wire, ["scheme"] = bt.SchemeName, ["dx"] = dx, ["dy"] = dy });
                if (!arr.Ok) { Say("Сквозной маршрут: " + arr.Reason); return; }
                var line = a.Lines.First(l => l.Wire == wire);
                Say($"Сквозной маршрут: {dep.Name}  +  {arr.Name}");
                if (dep.DirOk != true)
                {
                    Say($"Сквозной маршрут: меняю направление «{line.Title}»…");
                    Net.ExpectConsent(wire);
                    var d = await Net.Direction(a.Id, line.Id);
                    if (!d.Ok) { Say($"Сквозной маршрут: направление «{line.Title}» не сменить — {(d.Reason.Length > 0 ? d.Reason : "станция отказала")}"); return; }
                    bool turned = false;
                    for (int k = 0; k < 50 && !turned; k++)
                    {
                        await Task.Delay(500);
                        var chk = await Net.Call(a.Id, "t_depart", new() { ["wire"] = wire });
                        if (!chk.Ok) { Say("Сквозной маршрут: " + chk.Reason); return; }
                        turned = chk.DirOk == true;
                    }
                    if (!turned) { Say($"Сквозной маршрут: направление «{line.Title}» не сменилось за 25 с"); return; }
                }
                var r1 = await Net.Route(a.Id, dep.I, true);
                if (!r1.Ok) { Say($"Сквозной маршрут: отправление «{dep.Name}» не задано — {(r1.Reason.Length > 0 ? r1.Reason : "станция отказала")}"); return; }
                var r2 = await Net.Route(b.Id, arr.I, true);
                if (!r2.Ok) { Say($"Сквозной маршрут: отправление задано, приём «{arr.Name}» — нет: {(r2.Reason.Length > 0 ? r2.Reason : "станция отказала")}"); return; }
                Say($"Задан сквозной маршрут: {dep.Name} → {arr.Name}");
                return;
            }
            Say("Сквозной маршрут: " + (err.Length > 0 ? err : "маршрута отправления нет"));
        }
        finally
        {
            Net.Unselect(a.Id);   // набор на станции отправления больше не нужен
            throughBusy = false;
        }
    }
}

// =====================================================================
//  КОПИЯ ПУЛЬТА СТАНЦИИ: группы (строки и колонки пульта) — рамками, в них кнопки, галочки и надписи
//  в том же порядке, с теми же цветами, что на станции. Список меняется — перестраиваем, иначе только обновляем.
// =====================================================================

public class UiMirror : FlowLayoutPanel
{
    string shape = "";
    Color paneBg;
    readonly HashSet<string> boxed = new();   // надписи с собственным фоном (лампы пульта)
    readonly Dictionary<string, Control> map = new();
    readonly Dictionary<string, UiItem> last = new();
    /// <summary>Нажата копия: id на станции, правой кнопкой.</summary>
    public event Action<string, bool> Clicked;

    public UiMirror()
    {
        WrapContents = true;
        AutoScroll = true;
        FlowDirection = FlowDirection.LeftToRight;
        Padding = new Padding(4);
        Height = 0;
    }

    public int PreferredHeightFor(int width) =>
        Controls.Count == 0 ? 0 : GetPreferredSize(new Size(Math.Max(100, width), 0)).Height + 6;

    public void SetItems(List<UiItem> items, Color bg)
    {
        if (BackColor != bg) BackColor = bg;
        paneBg = bg;
        string sig = string.Join("|", items.Select(i => i.Id + i.T + i.G + (i.Nl ? "n" : "")));
        if (sig != shape) { shape = sig; Rebuild(items); return; }
        SuspendLayout();
        foreach (var it in items)
            if (map.TryGetValue(it.Id, out var c)) Apply(c, it);
        ResumeLayout();
    }

    void Rebuild(List<UiItem> items)
    {
        SuspendLayout();
        foreach (Control c in Controls) c.Dispose();
        Controls.Clear();
        map.Clear();
        last.Clear();
        boxed.Clear();
        foreach (var grp in items.GroupBy(i => i.G))
        {
            var list = grp.ToList();
            int gw = Math.Clamp(list[0].Gw > 0 ? list[0].Gw : 400, 160, 1600);
            var box = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                MaximumSize = new Size(gw, 0), Margin = new Padding(3), Padding = new Padding(4, 2, 4, 2),
                BackColor = ControlPaint.Dark(BackColor, 0.05f)
            };
            Control prev = null;
            foreach (var it in list)
            {
                if (it.Nl && prev != null) box.SetFlowBreak(prev, true);
                var c = Make(it, gw);
                map[it.Id] = c;
                Apply(c, it);
                box.Controls.Add(c);
                prev = c;
            }
            Controls.Add(box);
        }
        ResumeLayout();
    }

    Control Make(UiItem it, int gw)
    {
        Control c = it.T switch
        {
            "b" => new Button { FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Margin = new Padding(2), AutoEllipsis = true },
            "c" => new CheckBox { AutoSize = true, AutoCheck = false, Margin = new Padding(3, 5, 3, 3) },
            _ => new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 3) }
        };
        if (c is Button b)
        {
            b.FlatAppearance.BorderSize = 0;
            b.Size = new Size(Math.Clamp(it.W, 30, gw - 10), Math.Clamp(it.H, 20, 34));
        }
        if (c is Label l)
        {
            l.MaximumSize = new Size(gw - 10, 0);
            if (Color.FromArgb(it.Bg).A == 255 && Color.FromArgb(255, Color.FromArgb(it.Bg)) != paneBg)
            {
                // на станции это «лампа»: надпись с фоном фиксированного размера
                boxed.Add(it.Id);
                l.AutoSize = false;
                l.Size = new Size(Math.Clamp(it.W, 20, gw - 10), Math.Clamp(it.H, 16, 34));
                l.TextAlign = ContentAlignment.MiddleCenter;
                l.Margin = new Padding(2);
            }
        }
        // компактнее, чем на станции: кнопкам сверху отдаём меньше места, схеме — больше
        c.Font = new Font("Bahnschrift", Math.Clamp(it.F, 7f, 12f), it.Bold ? FontStyle.Bold : FontStyle.Regular);
        string id = it.Id;
        c.Click += (_, _) => Clicked?.Invoke(id, false);
        c.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) Clicked?.Invoke(id, true); };
        return c;
    }

    void Apply(Control c, UiItem it)
    {
        if (last.TryGetValue(it.Id, out var was) && was.Text == it.Text && was.Bg == it.Bg && was.Fg == it.Fg &&
            was.En == it.En && was.Chk == it.Chk) return;
        last[it.Id] = it;
        if (c.Text != it.Text) c.Text = it.Text;
        var fg = Color.FromArgb(255, Color.FromArgb(it.Fg));
        if (c.ForeColor != fg) c.ForeColor = fg;
        if (c is Button || boxed.Contains(it.Id))
        {
            var bg = Color.FromArgb(255, Color.FromArgb(it.Bg));
            if (c.BackColor != bg) c.BackColor = bg;
        }
        if (c.Enabled != it.En) c.Enabled = it.En;
        if (c is CheckBox cb && cb.Checked != it.Chk) cb.Checked = it.Chk;
    }
}
