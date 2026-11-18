namespace DncApp;

// =====================================================================
//  МАРШРУТЫ И ПЕРЕГОНЫ — панель ДНЦ при управлении  [ДОБАВЛЕНО 18.11.2026]
//   • маршруты выбранной схемы (той, по которой кликнули последней): кнопка = маршрут, цвет = состояние
//     (свободен / устанавливается / открыт / поезд в маршруте / разделка); клик — задать или отменить (t_route);
//     «Сбросить набор» — снять выбранный светофор начала на всех станциях под управлением;
//   • перегоны станций под управлением: направление и «Сменить направление» (t_dir). Общий с соседом перегон —
//     одной строкой: согласие соседа ДНЦ даёт сам (Net.ExpectConsent).
// =====================================================================

public class RoutesPanel : Panel
{
    static readonly Color Free = Color.FromArgb(232, 232, 232), Setting = Color.FromArgb(255, 235, 120),
                          Open = Color.FromArgb(120, 220, 140), Train = Color.FromArgb(255, 200, 80), Releasing = Color.FromArgb(255, 160, 90);

    readonly Label lblTitle = new() { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.White, Padding = new Padding(4, 5, 0, 0) };
    readonly FlowLayoutPanel tools = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(2) };
    readonly FlowLayoutPanel routes = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(2) };
    readonly FlowLayoutPanel lines = new() { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2, 4, 2, 4) };
    readonly RadioButton rbTrain, rbShunt, rbAll;
    readonly ToolTip tips = new();
    readonly Dictionary<int, Button> routeBtns = new();
    string routesShape = "", linesShape = "";

    /// <summary>Что показывать: станция и схема (маршруты), станции под управлением (перегоны).</summary>
    public string StationId = "", SchemeName = "";
    public IReadOnlyCollection<string> Stations = Array.Empty<string>();
    /// <summary>Сообщение для строки состояния окна (результат команды).</summary>
    public event Action<string> Status;

    public RoutesPanel()
    {
        BackColor = Color.FromArgb(52, 58, 66);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        rbTrain = Radio("Поездные", true);
        rbShunt = Radio("Маневровые", false);
        rbAll = Radio("Все", false);
        var bUnsel = new Button { Text = "Сбросить набор", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(219, 0, 105), ForeColor = Color.White, Margin = new Padding(8, 2, 2, 2) };
        bUnsel.FlatAppearance.BorderSize = 0;
        bUnsel.Click += (_, _) => { foreach (var id in Stations) Net.Unselect(id); Status?.Invoke("Набор маршрута сброшен"); };
        tips.SetToolTip(bUnsel, "Снять выбранный светофор начала маршрута (на всех станциях под управлением)");
        tools.Controls.AddRange(new Control[] { rbTrain, rbShunt, rbAll, bUnsel });
        Controls.Add(routes);
        Controls.Add(lines);
        Controls.Add(tools);
        Controls.Add(lblTitle);
    }

    RadioButton Radio(string t, bool on)
    {
        var r = new RadioButton { Text = t, AutoSize = true, Checked = on, ForeColor = Color.White, Margin = new Padding(4, 4, 2, 2) };
        r.CheckedChanged += (_, _) => { if (r.Checked) { routesShape = ""; RefreshData(); } };
        return r;
    }

    /// <summary>Обновить по последним данным станций (зовёт вид управления при каждом s_info).</summary>
    public void RefreshData()
    {
        if (IsDisposed) return;
        var st = Net.Get(StationId);
        lblTitle.Text = $"Маршруты: {SchemeName}";
        var list = (st?.Routes ?? new List<RouteInfo>())
            .Where(r => r.P == SchemeName || r.P.Length == 0)
            .Where(r => rbAll.Checked || (rbShunt.Checked ? r.Sh : !r.Sh)).ToList();
        string shape = StationId + "|" + string.Join(",", list.Select(r => r.I));
        if (shape != routesShape)
        {
            routesShape = shape;
            routes.SuspendLayout();
            foreach (Control c in routes.Controls) c.Dispose();
            routes.Controls.Clear();
            routeBtns.Clear();
            foreach (var r in list)
            {
                var b = new Button { Width = 196, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = Color.Black, TextAlign = ContentAlignment.MiddleLeft,
                                     Margin = new Padding(2), AutoEllipsis = true, UseVisualStyleBackColor = false };
                b.FlatAppearance.BorderSize = 0;
                int i = r.I;
                b.Click += async (_, _) =>
                {
                    var cur = Net.Get(StationId)?.Routes?.FirstOrDefault(x => x.I == i);
                    if (cur == null) return;
                    bool on = !cur.Active;
                    var res = await Net.Route(StationId, i, on);
                    Status?.Invoke(res.Ok ? $"{(on ? "Задан" : "Отменён")} маршрут {res.Name}" + (res.Reason.Length > 0 ? $" ({res.Reason})" : "")
                                          : $"Маршрут {cur.N} не {(on ? "задан" : "отменён")}: {(res.Reason.Length > 0 ? res.Reason : "станция отказала")}");
                };
                routeBtns[i] = b;
                routes.Controls.Add(b);
            }
            routes.ResumeLayout();
        }
        foreach (var r in list)
        {
            if (!routeBtns.TryGetValue(r.I, out var b)) continue;
            if (b.Text != r.N) b.Text = r.N;
            var c = r.St switch { "setting" => Setting, "open" => Open, "train" => Train, "releasing" => Releasing, _ => Free };
            if (b.BackColor != c) b.BackColor = c;
            string tip = r.St switch
            {
                "setting" => "Устанавливается — клик: отменить", "open" => "Открыт — клик: отменить", "train" => "Поезд в маршруте",
                "releasing" => "Идёт разделка", _ => string.IsNullOrEmpty(r.Why) ? "Свободен — клик: задать" : "Сейчас не задать: " + r.Why
            };
            if (tips.GetToolTip(b) != tip) tips.SetToolTip(b, tip);
        }
        RefreshLines();
    }

    /// <summary>Перегоны станций под управлением; общий перегон двух станций — одной строкой.</summary>
    void RefreshLines()
    {
        var items = new List<(string station, LineInfo line)>();
        foreach (var id in Stations)
            foreach (var ln in Net.Get(id)?.Lines ?? new List<LineInfo>())
                if (ln.Wire.Length == 0 || !items.Any(x => x.line.Wire == ln.Wire)) items.Add((id, ln));
        string shape = string.Join("|", items.Select(x => x.station + x.line.Id + x.line.Text + x.line.Sync + x.line.Aux));
        if (shape == linesShape) return;
        linesShape = shape;
        lines.SuspendLayout();
        foreach (Control c in lines.Controls) c.Dispose();
        lines.Controls.Clear();
        lines.Controls.Add(new Label { Text = "Перегоны", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.White });
        foreach (var (station, ln) in items)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            string state = ln.Sync ? "  — согласование…" : ln.Aux ? "  — вспомогательная смена…" : "";
            row.Controls.Add(new Label
            {
                Text = $"{ln.Title}: {ln.Arrow} {ln.Text}{state}", AutoSize = true, MaximumSize = new Size(Math.Max(200, Width - 190), 0),
                ForeColor = Color.White, Margin = new Padding(2, 6, 6, 2)
            });
            var b = new Button { Text = "Сменить направление", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(240, 240, 255), ForeColor = Color.Black, Enabled = !ln.Sync && !ln.Aux };
            b.FlatAppearance.BorderSize = 0;
            var (sid, lid, wire, title) = (station, ln.Id, ln.Wire, ln.Title);
            b.Click += async (_, _) =>
            {
                if (wire.Length > 0) Net.ExpectConsent(wire);   // соседа спросят — согласие даёт ДНЦ, то есть сразу «да»
                var res = await Net.Direction(sid, lid);
                Status?.Invoke(res.Ok ? $"{title}: смена направления запущена" : $"{title}: направление не сменить — {(res.Reason.Length > 0 ? res.Reason : "станция отказала")}");
            };
            row.Controls.Add(b);
            lines.Controls.Add(row);
        }
        lines.ResumeLayout();
    }
}
