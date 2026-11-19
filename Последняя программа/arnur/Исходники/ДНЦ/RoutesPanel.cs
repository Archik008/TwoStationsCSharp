namespace DncApp;

// =====================================================================
//  МАРШРУТЫ И ПЕРЕГОНЫ — панель ДНЦ при управлении  [ДОБАВЛЕНО 18.11.2026]
//   • маршруты выбранной схемы (той, по которой кликнули последней): кнопка = маршрут, цвет = состояние
//     (свободен / устанавливается / открыт / поезд в маршруте / разделка); клик — задать или отменить (t_route);
//     «Сбросить набор» — снять выбранный светофор начала на всех станциях под управлением;
//   • перегоны станций под управлением: направление и «Сменить направление» (t_dir). Общий с соседом перегон —
//     одной строкой: согласие соседа ДНЦ даёт сам (Net.ExpectConsent);
//   • [ДОБАВЛЕНО 19.11.2026] виртуальные поезда этой схемы: поезд от соседней станции, поставить поезд на путь
//     (головой влево / вправо, маневровый), убрать все поезда станции (t_train);
//     ОТПРАВИТЬ поезд с пути на соседнюю станцию: если надо — смена направления перегона (согласие соседа ДНЦ даёт сам),
//     поезд на путь (если его там нет), маршрут отправления; «принять у соседа» — маршрут приёма у соседней станции
//     на первый свободный путь (если она под управлением ДНЦ).
// =====================================================================

public class RoutesPanel : Panel
{
    static readonly Color Free = Color.FromArgb(232, 232, 232), Setting = Color.FromArgb(255, 235, 120),
                          Open = Color.FromArgb(120, 220, 140), Train = Color.FromArgb(255, 200, 80), Releasing = Color.FromArgb(255, 160, 90);

    readonly Label lblTitle = new() { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.White, Padding = new Padding(4, 5, 0, 0) };
    readonly FlowLayoutPanel tools = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(2) };
    readonly FlowLayoutPanel routes = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(2) };
    // [ИЗМЕНЕНО 19.11.2026] две колонки: слева маршруты во всю высоту, справа «Поезда» и «Перегоны» (с прокруткой) —
    // раньше поезда и перегоны стояли под маршрутами и сжимали список маршрутов до нуля
    const int RightW = 330;
    readonly Panel right = new() { Dock = DockStyle.Right, Width = RightW, AutoScroll = true };
    readonly FlowLayoutPanel lines = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2, 4, 2, 4) };
    readonly FlowLayoutPanel trains = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(2, 4, 2, 4), BackColor = Color.FromArgb(44, 70, 96) };
    string trainsShape = "";
    Label lblTrains;
    ComboBox cmbPlace;
    CheckBox chkShunt, chkAccept;
    ComboBox cmbDepart;
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
        right.Controls.Add(lines);
        right.Controls.Add(trains);    // добавлена последней — встаёт сверху колонки
        Controls.Add(routes);
        Controls.Add(tools);
        Controls.Add(lblTitle);
        Controls.Add(right);           // колонка справа во всю высоту панели
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
        RefreshTrains();
    }

    /// <summary>[ДОБАВЛЕНО 19.11.2026] Отправить поезд с пути на соседнюю станцию: направление перегона (если надо — меняет
    /// ДНЦ, согласие соседа даёт сам), поезд на путь и маршрут отправления; приём у соседа, если она под управлением ДНЦ.
    /// Результат — строкой (Btn пишет его в полосу состояния).</summary>
    async Task<CallResult> Depart(DepartInfo dep, bool accept)
    {
        string station = StationId;
        var st = Net.Get(station);
        if (st == null) return new CallResult(false, "станция не на связи");
        if (!dep.DirOk && dep.Wire.Length > 0)
        {
            var ln = st.Lines.FirstOrDefault(l => l.Wire == dep.Wire);
            if (ln == null) return new CallResult(false, "не найден перегон для смены направления");
            Status?.Invoke($"Отправление: меняю направление «{ln.Title}»…");
            Net.ExpectConsent(dep.Wire);
            var d = await Net.Direction(station, ln.Id);
            if (!d.Ok) return new CallResult(false, $"направление «{ln.Title}» не сменить — {(d.Reason.Length > 0 ? d.Reason : "станция отказала")}");
            bool turned = false;
            for (int k = 0; k < 50 && !turned; k++)
            {
                await Task.Delay(500);
                turned = Net.Get(station)?.Vt?.Departures.FirstOrDefault(x => x.R == dep.R)?.DirOk == true;
            }
            if (!turned) return new CallResult(false, $"направление «{ln.Title}» не сменилось за 25 с");
        }
        var r = await Net.Train(station, "depart", dep.P, dep.I, r: dep.R);
        if (!r.Ok) return r;
        string done = $"Отправлен поезд: {r.Name}" + (r.Reason.Length > 0 ? $" ({r.Reason})" : "");
        if (!accept || dep.Wire.Length == 0) return new CallResult(true, "", Name: done);
        // приём у соседней станции, у которой тот же перегон
        var nb = Net.Stations.Values.FirstOrDefault(s => s.Id != station && s.Online && s.Lines.Any(l => l.Wire == dep.Wire));
        if (nb == null) return new CallResult(true, "", Name: done + "; соседняя станция не на связи — приём задаст её дежурный");
        if (!Stations.Contains(nb.Id))
            return new CallResult(true, "", Name: done + $"; приём на «{nb.Title}» — она не под управлением ДНЦ («Управлять участком»)");
        var a = await Net.Train(nb.Id, "accept", wire: dep.Wire);
        return new CallResult(true, "", Name: done + (a.Ok ? $"; приём на «{nb.Title}»: {a.Name}" : $"; приём на «{nb.Title}» не задан — {a.Reason}"));
    }

    /// <summary>[ДОБАВЛЕНО 19.11.2026] Виртуальные поезда схемы: от соседей, на путь, убрать все.</summary>
    void RefreshTrains()
    {
        var st = Net.Get(StationId);
        var vt = st?.Vt;
        var spawns = vt?.Spawns.Where(x => x.P == SchemeName).ToList() ?? new();
        var places = vt?.Places.Where(x => x.P == SchemeName).ToList() ?? new();
        // [ДОБАВЛЕНО 19.11.2026] маршруты станция прислала, а поезда — нет: программа станции собрана старым кодом
        // (без команды t_train) — кнопкам поездов не из чего взяться, пишем, что делать
        string state = vt != null ? "ok" : st?.Routes != null ? "old" : "wait";
        string shape = state + StationId + SchemeName + string.Join("|", spawns.Select(x => x.Text)) + "#" + string.Join("|", places.Select(x => x.Name)) +
                       "#" + string.Join("|", vt?.Departures.Where(x => x.P == SchemeName).Select(x => x.R) ?? Enumerable.Empty<int>());
        if (shape != trainsShape)
        {
            trainsShape = shape;
            trains.SuspendLayout();
            foreach (Control c in trains.Controls) c.Dispose();
            trains.Controls.Clear();
            lblTrains = new Label { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(2, 4, 8, 2) };
            if (state != "ok")
            {
                trains.Controls.Add(lblTrains);
                trains.SetFlowBreak(lblTrains, true);
                trains.Controls.Add(new Label
                {
                    AutoSize = true, MaximumSize = new Size(RightW - 30, 0), Margin = new Padding(2, 2, 2, 4),
                    ForeColor = state == "old" ? Color.FromArgb(255, 200, 120) : Color.Gainsboro,
                    Text = state == "old"
                        ? "Программа этой станции собрана старым кодом и не принимает виртуальные поезда от ДНЦ. " +
                          "Обновите и пересоберите программы станций (собрать_всё.bat) — тогда здесь появятся кнопки поездов."
                        : "Ожидание ответа станции…"
                });
                trains.ResumeLayout();
                lblTrains.Text = $"Поезда: {SchemeName}";
                return;
            }
            trains.Controls.Add(lblTrains);
            trains.SetFlowBreak(lblTrains, true);
            Button Btn(string t, Func<Task<CallResult>> act, string ok)
            {
                var b = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(240, 240, 255), ForeColor = Color.Black, Margin = new Padding(2) };
                b.FlatAppearance.BorderSize = 0;
                b.Click += async (_, _) =>
                {
                    var r = await act();
                    Status?.Invoke(r.Ok ? (ok.Length > 0 ? ok + (r.Name.Length > 0 ? $": {r.Name}" : "") : r.Name)
                                        : $"Поезд: {(r.Reason.Length > 0 ? r.Reason : "станция отказала")}");
                };
                trains.Controls.Add(b);
                return b;
            }
            foreach (var (p, i, text) in spawns) Btn(text, () => Net.Train(StationId, "spawn", p, i), "Поезд отправлен");
            if (trains.Controls.Count > 1) trains.SetFlowBreak(trains.Controls[^1], true);
            if (places.Count > 0)
            {
                cmbPlace = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(2, 4, 2, 2) };
                foreach (var pl in places) cmbPlace.Items.Add(pl.Name);
                cmbPlace.SelectedIndex = 0;
                trains.Controls.Add(cmbPlace);
                int Sel() => places[Math.Max(0, cmbPlace.SelectedIndex)].I;
                Btn("← поезд", () => Net.Train(StationId, "put", SchemeName, Sel(), left: true, shunt: chkShunt.Checked), "Поезд поставлен");
                Btn("поезд →", () => Net.Train(StationId, "put", SchemeName, Sel(), left: false, shunt: chkShunt.Checked), "Поезд поставлен");
                chkShunt = new CheckBox { Text = "маневровый", AutoSize = true, ForeColor = Color.White, Margin = new Padding(4, 7, 2, 2) };
                trains.Controls.Add(chkShunt);
                trains.SetFlowBreak(chkShunt, true);
                tips.SetToolTip(cmbPlace, "Путь, на который поставить виртуальный поезд (занятый — с пометкой)");
            }
            else cmbPlace = null;
            // ---- отправить поезд на соседнюю станцию ----
            var departs = vt.Departures.Where(x => x.P == SchemeName).ToList();
            if (departs.Count > 0)
            {
                var lbl = new Label { Text = "Отправить:", AutoSize = true, ForeColor = Color.White, Margin = new Padding(2, 7, 2, 2) };
                trains.Controls.Add(lbl);
                cmbDepart = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = RightW - 40, DropDownWidth = 420, Margin = new Padding(2, 4, 2, 2) };
                foreach (var d in departs)
                    cmbDepart.Items.Add($"{places.FirstOrDefault(x => x.I == d.I).Name}: {d.Name}");
                cmbDepart.SelectedIndex = 0;
                trains.Controls.Add(cmbDepart);
                trains.SetFlowBreak(cmbDepart, true);
                tips.SetToolTip(cmbDepart, "С какого пути и каким маршрутом отправить поезд на перегон. Поезда на пути нет — он появится головой к выходу");
                chkAccept = new CheckBox { Text = "принять у соседней станции", AutoSize = true, Checked = true, ForeColor = Color.White, Margin = new Padding(4, 7, 2, 2) };
                tips.SetToolTip(chkAccept, "Сразу задать на соседней станции маршрут приёма на свободный путь (если она под управлением ДНЦ)");
                var bGo = Btn("Отправить поезд", () => Depart(departs[Math.Max(0, cmbDepart.SelectedIndex)], chkAccept.Checked), "");
                bGo.BackColor = Color.FromArgb(120, 220, 140);
                trains.Controls.Add(chkAccept);
                trains.SetFlowBreak(chkAccept, true);
            }
            else cmbDepart = null;
            var bClear = Btn("Убрать все поезда", () => Net.Train(StationId, "clear"), "Поезда станции убраны");
            bClear.BackColor = Color.FromArgb(255, 210, 200);
            trains.ResumeLayout();
        }
        if (lblTrains != null) lblTrains.Text = $"Поезда: {SchemeName}" + (vt != null ? $"  (сейчас на станции: {vt.Count})" : "");
        if (cmbPlace != null)
            for (int k = 0; k < places.Count && k < cmbPlace.Items.Count; k++)
            {
                string t = places[k].Name + (places[k].Busy ? " (занят)" : "");
                if ((string)cmbPlace.Items[k] != t) cmbPlace.Items[k] = t;
            }
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
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(RightW - 24, 0), Margin = new Padding(0, 2, 0, 2) };
            string state = ln.Sync ? "  — согласование…" : ln.Aux ? "  — вспомогательная смена…" : "";
            row.Controls.Add(new Label
            {
                Text = $"{ln.Title}: {ln.Arrow} {ln.Text}{state}", AutoSize = true, MaximumSize = new Size(RightW - 40, 0),
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
