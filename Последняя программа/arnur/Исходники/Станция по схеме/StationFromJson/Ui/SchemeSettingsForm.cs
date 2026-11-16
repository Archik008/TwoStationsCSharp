using System.Text.Json;
using StationApp.Core;
using StationApp.Model;
using StationApp.Scheme;
using TC = StationEditor.Model.Core;

namespace StationApp.Ui;

// =====================================================================
//  ОКНО «НАСТРОЙКА»  [ДОБАВЛЕНО 13.11.2026]
//  Свои маршруты (клик по схеме: n1 → n2 → участки, стрелки, светофоры), автомаршруты (вкл / выкл, имена),
//  светофоры (имя, вид), стрелки (номер, начальное положение), времена. Пишет «<схема>.settings.json»
//  (Model/SchemeSettings.cs) и перезапускает станцию — как кнопка «Схема…». Окно не модальное:
//  пока оно открыто, клики по схеме идут в запись маршрута (RailSchemePanel.EditClick).
// =====================================================================

sealed class SchemeSettingsForm : Form
{
    readonly JsonLayout lay = ModelHost.Model.Layout;
    readonly Interlocking il = ModelHost.Model.IL;
    readonly RailSchemePanel scheme;
    readonly Action<string> restart;
    readonly SchemeSettings work;            // рабочая копия: в файл — только по «Сохранить»

    // ---- свои маршруты ----
    enum Pick { None, Start, End, Body }
    Pick pick = Pick.None;
    CustomRoute cur;                         // маршрут, который сейчас правится
    string autoName;                         // имя, данное по кликам (меняется, пока его не правили руками)
    readonly ListBox lbRoutes = new() { IntegralHeight = false };
    readonly TextBox tbName = new();
    readonly CheckBox cbShunt = new() { Text = "маневровый (лунно-белый)", AutoSize = true };
    readonly CheckBox cbOff = new() { Text = "выключен", AutoSize = true };
    readonly Label lStart = new() { AutoSize = true }, lEnd = new() { AutoSize = true };
    readonly ListBox lbSecs = new() { IntegralHeight = false };
    readonly DataGridView gSw = Grid(), gAsp = Grid();
    readonly Label lPick = new() { AutoSize = false, Dock = DockStyle.Top, Height = 44, Padding = new Padding(6), ForeColor = Color.FromArgb(120, 50, 0), BackColor = Color.FromArgb(255, 246, 200) };
    readonly Button bRecord = new() { Text = "Записать на схеме…", AutoSize = true };
    bool loading;

    static readonly string[] Positions = { "плюс", "минус" };
    const string AutoAspect = "авто (как считает программа)";

    public SchemeSettingsForm(Form owner, Action<string> restart)
    {
        this.restart = restart;
        Owner = owner;
        scheme = FindScheme(owner);
        work = JsonSerializer.Deserialize<SchemeSettings>(JsonSerializer.Serialize(lay.Set)) ?? new SchemeSettings();
        Text = "Настройка станции — " + lay.Name;
        StartPosition = FormStartPosition.Manual;
        var sc = Screen.FromControl(owner).WorkingArea;
        Size = new Size(Math.Min(820, sc.Width / 2), Math.Min(760, sc.Height - 40));
        Location = new Point(sc.Right - Width - 10, sc.Top + 20);
        Font = new Font("Segoe UI", 9f);
        ShowInTaskbar = false;
        MinimumSize = new Size(640, 520);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(RoutesPage());
        tabs.TabPages.Add(AutoPage());
        tabs.TabPages.Add(SignalsPage());
        tabs.TabPages.Add(SwitchesPage());
        tabs.TabPages.Add(TimesPage());

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var bSave = new Button { Text = "Сохранить и перезапустить станцию", AutoSize = true, BackColor = Color.FromArgb(60, 110, 160), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var bClose = new Button { Text = "Закрыть без сохранения", AutoSize = true };
        bSave.Click += (_, _) => SaveAndRestart();
        bClose.Click += (_, _) => Close();
        var lFile = new Label { Text = "Файл: " + SchemeSettings.PathFor(ModelHost.Model.FilePath), AutoSize = false, Width = 330, Height = 56, ForeColor = Color.DimGray, AutoEllipsis = true };
        bottom.Controls.AddRange(new Control[] { bSave, bClose, lFile });
        Controls.Add(tabs);
        Controls.Add(bottom);

        if (scheme != null) scheme.EditClick = OnSchemeClick;
        FormClosed += (_, _) => { if (scheme != null) { scheme.EditClick = null; scheme.DraftSections = null; scheme.DraftSignals = null; scheme.Invalidate(); } };
        RefreshRouteList();
        if (work.Routes.Count > 0) lbRoutes.SelectedIndex = 0; else ShowRoute(null);
    }

    static RailSchemePanel FindScheme(Control c)
    {
        if (c is RailSchemePanel p) return p;
        foreach (Control x in c.Controls) if (FindScheme(x) is { } r) return r;
        return null;
    }

    static DataGridView Grid() => new()
    {
        AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BackgroundColor = Color.White,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        EditMode = DataGridViewEditMode.EditOnEnter, BorderStyle = BorderStyle.FixedSingle,
    };

    static DataGridViewComboBoxColumn Combo(string head, IEnumerable<string> items, int fill = 100)
    {
        var c = new DataGridViewComboBoxColumn { HeaderText = head, FlatStyle = FlatStyle.Flat, FillWeight = fill, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton };
        c.Items.AddRange(items.Cast<object>().ToArray());
        return c;
    }

    static DataGridViewTextBoxColumn TextCol(string head, bool ro, int fill = 100) => new() { HeaderText = head, ReadOnly = ro, FillWeight = fill };

    static Label Hint(string text) => new() { Text = text, AutoSize = false, Dock = DockStyle.Top, Height = 58, ForeColor = Color.DimGray, Padding = new Padding(4) };

    // =================================================================
    //  ИМЕНА ОБЪЕКТОВ
    // =================================================================
    string SigName(string key)
    {
        if (key == null) return "—";
        var s = lay.SignalByKey(key);
        var cell = lay.SignalList.FirstOrDefault(x => x.Key == key).Cell;
        return (s?.Label ?? "?") + (cell != null ? $" ({cell})" : "");
    }

    string SwName(string key) => lay.SwitchByKey(key)?.Label ?? key;

    /// <summary>Светофоры для списка показаний: «имя (клетка)» → ключ.</summary>
    Dictionary<string, string> SignalChoices() =>
        lay.SignalList.Where(x => lay.SignalByKey(x.Key) != null).ToDictionary(x => SigName(x.Key), x => x.Key);

    // =================================================================
    //  ВКЛАДКА «СВОИ МАРШРУТЫ»
    // =================================================================
    TabPage RoutesPage()
    {
        var page = new TabPage("Свои маршруты") { Padding = new Padding(6) };
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 220, FixedPanel = FixedPanel.Panel1 };

        // слева: список и кнопки
        var lb = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, FlowDirection = FlowDirection.LeftToRight };
        var bNew = new Button { Text = "Новый", AutoSize = true };
        var bCopy = new Button { Text = "Копия", AutoSize = true };
        var bDel = new Button { Text = "Удалить", AutoSize = true };
        bNew.Click += (_, _) => NewRoute();
        bCopy.Click += (_, _) =>
        {
            if (cur == null) return;
            var c = JsonSerializer.Deserialize<CustomRoute>(JsonSerializer.Serialize(cur));
            c.Name += " (копия)";
            work.Routes.Add(c);
            RefreshRouteList();
            lbRoutes.SelectedIndex = work.Routes.Count - 1;
        };
        bDel.Click += (_, _) =>
        {
            if (cur == null) return;
            if (MessageBox.Show(this, $"Удалить свой маршрут «{cur.Name}»?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            work.Routes.Remove(cur);
            StopPick();
            RefreshRouteList();
            if (work.Routes.Count > 0) lbRoutes.SelectedIndex = 0; else ShowRoute(null);
        };
        lb.Controls.AddRange(new Control[] { bNew, bCopy, bDel });
        lbRoutes.Dock = DockStyle.Fill;
        lbRoutes.SelectedIndexChanged += (_, _) => { if (!loading) { StopPick(); ShowRoute(lbRoutes.SelectedIndex >= 0 ? work.Routes[lbRoutes.SelectedIndex] : null); } };
        split.Panel1.Controls.Add(lbRoutes);
        split.Panel1.Controls.Add(lb);
        var lh = Hint("Клик n1 → n2 на схеме задаёт свой маршрут вместо найденных автоматически.");
        lh.Height = 74;
        split.Panel1.Controls.Add(lh);

        // справа: правка маршрута
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string head, Control c, float h = 0)
        {
            t.RowStyles.Add(h > 0 ? new RowStyle(SizeType.Absolute, h) : new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = head, AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c);
        }
        tbName.TextChanged += (_, _) => { if (cur != null && !loading) { cur.Name = tbName.Text; RefreshRouteList(); } };
        cbShunt.CheckedChanged += (_, _) => { if (cur != null && !loading) cur.Shunt = cbShunt.Checked; };
        cbOff.CheckedChanged += (_, _) => { if (cur != null && !loading) { cur.Off = cbOff.Checked ? true : null; RefreshRouteList(); } };
        var flags = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        flags.Controls.AddRange(new Control[] { cbShunt, cbOff });

        var startRow = new FlowLayoutPanel { AutoSize = true };
        var bStart = new Button { Text = "указать на схеме", AutoSize = true };
        bStart.Click += (_, _) => StartPick(Pick.Start);
        startRow.Controls.AddRange(new Control[] { lStart, bStart });
        var endRow = new FlowLayoutPanel { AutoSize = true };
        var bEnd = new Button { Text = "указать на схеме", AutoSize = true };
        var bNoEnd = new Button { Text = "нет (клик по пути)", AutoSize = true };
        bEnd.Click += (_, _) => StartPick(Pick.End);
        bNoEnd.Click += (_, _) => { if (cur == null) return; cur.End = null; ShowRoute(cur); };
        endRow.Controls.AddRange(new Control[] { lEnd, bEnd, bNoEnd });

        var secRow = new FlowLayoutPanel { AutoSize = true };
        var bFromAuto = new Button { Text = "взять из автомаршрута n1 → n2", AutoSize = true };
        var bSecClear = new Button { Text = "очистить", AutoSize = true };
        bFromAuto.Click += (_, _) => FillFromAuto(true);
        bSecClear.Click += (_, _) => { if (cur == null) return; cur.Sections.Clear(); ShowRoute(cur); };
        secRow.Controls.AddRange(new Control[] { bRecord, bFromAuto, bSecClear });
        bRecord.Click += (_, _) => { if (pick == Pick.Body) StopPick(); else StartPick(Pick.Body); };

        gSw.Columns.Add(TextCol("Стрелка", true));
        gSw.Columns.Add(Combo("Положение в маршруте", Positions));
        gSw.CellValueChanged += (_, e) => { if (!loading && e.RowIndex >= 0) ReadSwitchGrid(); };
        gSw.CurrentCellDirtyStateChanged += (_, _) => { if (gSw.IsCurrentCellDirty) gSw.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        var bSwFromSecs = new Button { Text = "стрелки отмеченных участков (как стоят сейчас)", AutoSize = true };
        var bSwDel = new Button { Text = "убрать стрелку", AutoSize = true };
        bSwFromSecs.Click += (_, _) => SwitchesFromSections();
        bSwDel.Click += (_, _) => { if (cur == null || gSw.CurrentRow == null) return; cur.Switches.Remove((string)gSw.CurrentRow.Tag); ShowRoute(cur); };
        var swBtns = new FlowLayoutPanel { AutoSize = true };
        swBtns.Controls.AddRange(new Control[] { bSwFromSecs, bSwDel });

        gAsp.Columns.Add(Combo("Светофор", Array.Empty<string>(), 110));
        gAsp.Columns.Add(Combo("Показание, пока маршрут открыт", new[] { AutoAspect }.Concat(SchemeSettings.Aspects.Select(a => a.Name)), 120));
        gAsp.CellValueChanged += (_, e) => { if (!loading && e.RowIndex >= 0) ReadAspectGrid(); };
        gAsp.CurrentCellDirtyStateChanged += (_, _) => { if (gAsp.IsCurrentCellDirty) gAsp.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        gAsp.DataError += (_, e) => e.ThrowException = false;
        gSw.DataError += (_, e) => e.ThrowException = false;
        var bAspAdd = new Button { Text = "добавить светофор", AutoSize = true };
        var bAspDel = new Button { Text = "убрать", AutoSize = true };
        bAspAdd.Click += (_, _) =>
        {
            if (cur == null) return;
            var free = SignalChoices().Values.FirstOrDefault(k => !cur.Aspects.ContainsKey(k));
            if (free != null) { cur.Aspects[free] = "red"; ShowRoute(cur); }
        };
        bAspDel.Click += (_, _) => { if (cur == null || gAsp.CurrentRow == null) return; cur.Aspects.Remove((string)gAsp.CurrentRow.Tag); ShowRoute(cur); };
        var aspBtns = new FlowLayoutPanel { AutoSize = true };
        aspBtns.Controls.AddRange(new Control[] { bAspAdd, bAspDel });

        Row("Имя", tbName);
        Row("", flags);
        Row("Начало n1", startRow);
        Row("Конец n2", endRow);
        Row("Участки\n(по порядку,\nгорят жёлтым)", lbSecs, 110);
        Row("", secRow);
        Row("Стрелки", gSw, 120);
        Row("", swBtns);
        Row("Светофоры", gAsp, 120);
        Row("", aspBtns);

        split.Panel2.Controls.Add(t);
        split.Panel2.Controls.Add(lPick);
        page.Controls.Add(split);
        return page;
    }

    void RefreshRouteList()
    {
        loading = true;
        int sel = lbRoutes.SelectedIndex;
        lbRoutes.BeginUpdate();
        lbRoutes.Items.Clear();
        foreach (var r in work.Routes)
            lbRoutes.Items.Add((string.IsNullOrWhiteSpace(r.Name) ? "(без имени)" : r.Name) + (r.Off == true ? "  — выкл" : ""));
        lbRoutes.EndUpdate();
        if (sel >= 0 && sel < lbRoutes.Items.Count) lbRoutes.SelectedIndex = sel;
        loading = false;
    }

    void NewRoute()
    {
        var r = new CustomRoute { Name = "" };
        work.Routes.Add(r);
        RefreshRouteList();
        lbRoutes.SelectedIndex = work.Routes.Count - 1;
        StartPick(Pick.Start);
    }

    void ShowRoute(CustomRoute r)
    {
        cur = r;
        loading = true;
        bool on = r != null;
        foreach (Control c in new Control[] { tbName, cbShunt, cbOff, lbSecs, gSw, gAsp, bRecord }) c.Enabled = on;
        tbName.Text = r?.Name ?? "";
        cbShunt.Checked = r?.Shunt == true;
        cbOff.Checked = r?.Off == true;
        lStart.Text = SigName(r?.Start);
        lEnd.Text = r == null ? "—" : r.End == null ? "нет — второй клик по пути назначения (последний участок)" : SigName(r.End);
        lbSecs.Items.Clear();
        if (r != null)
            for (int i = 0; i < r.Sections.Count; i++)
                lbSecs.Items.Add($"{i + 1}. {r.Sections[i]}" + (i == r.Sections.Count - 1 ? "  — путь назначения" : ""));

        gSw.Rows.Clear();
        if (r != null)
            foreach (var (k, plus) in r.Switches)
            {
                int i = gSw.Rows.Add(SwName(k), plus ? Positions[0] : Positions[1]);
                gSw.Rows[i].Tag = k;
            }

        var choices = SignalChoices();
        var col = (DataGridViewComboBoxColumn)gAsp.Columns[0];
        col.Items.Clear();
        col.Items.AddRange(choices.Keys.Cast<object>().ToArray());
        gAsp.Rows.Clear();
        if (r != null)
            foreach (var (k, a) in r.Aspects)
            {
                var name = SigName(k);
                if (!col.Items.Contains(name)) col.Items.Add(name);
                int i = gAsp.Rows.Add(name, SchemeSettings.Aspects.FirstOrDefault(x => x.Id == a).Name ?? AutoAspect);
                gAsp.Rows[i].Tag = k;
            }
        loading = false;
        UpdateDraft();
        UpdatePickText();
    }

    void ReadSwitchGrid()
    {
        if (cur == null) return;
        foreach (DataGridViewRow row in gSw.Rows)
            if (row.Tag is string k) cur.Switches[k] = (string)row.Cells[1].Value != Positions[1];
        UpdateDraft();
    }

    void ReadAspectGrid()
    {
        if (cur == null) return;
        var choices = SignalChoices();
        var map = new Dictionary<string, string>();
        foreach (DataGridViewRow row in gAsp.Rows)
        {
            var sn = row.Cells[0].Value as string;
            var an = row.Cells[1].Value as string;
            var key = sn != null && choices.TryGetValue(sn, out var k) ? k : row.Tag as string;
            if (key == null) continue;
            row.Tag = key;
            map[key] = an == null || an == AutoAspect ? "auto" : SchemeSettings.Aspects.First(x => x.Name == an).Id;
        }
        cur.Aspects = map;
        UpdateDraft();
    }

    /// <summary>Отметить на схеме: участки маршрута — жёлтым, n1, n2 и светофоры с показаниями — в рамке.</summary>
    void UpdateDraft()
    {
        if (scheme == null) return;
        if (cur == null) { scheme.DraftSections = null; scheme.DraftSignals = null; scheme.Invalidate(); return; }
        scheme.DraftSections = cur.Sections.Select(lay.SectionByName).Where(s => s != null).Select(s => s.Id).ToHashSet();
        scheme.DraftSignals = new[] { cur.Start, cur.End }.Concat(cur.Aspects.Keys).Select(lay.SignalByKey).Where(s => s != null).ToHashSet();
        scheme.Invalidate();
    }

    // ---- запись на схеме ----
    void StartPick(Pick p)
    {
        if (cur == null) return;
        if (scheme == null) { MessageBox.Show(this, "Схема не найдена в окне станции.", Text); return; }
        pick = p;
        bRecord.Text = p == Pick.Body ? "Готово — закончить запись" : "Записать на схеме…";
        UpdatePickText();
        Owner?.Activate();
    }

    void StopPick()
    {
        pick = Pick.None;
        bRecord.Text = "Записать на схеме…";
        UpdatePickText();
    }

    void UpdatePickText()
    {
        lPick.Text = pick switch
        {
            Pick.Start => "Шаг 1. Кликните на схеме светофор НАЧАЛА маршрута (n1).",
            Pick.End => "Шаг 2. Кликните светофор КОНЦА (n2) — или кнопку «нет (клик по пути)», если маршрут задаётся кликом по пути назначения.",
            Pick.Body => "Шаг 3. Кликайте участки ПО ПОРЯДКУ от n1 (повторный клик — убрать), стрелки (+ → − → убрать), " +
                         "светофоры — добавить в показания. Затем «Готово».",
            _ => cur == null ? "Нажмите «Новый», чтобы записать свой маршрут кликами по схеме." : "",
        };
        lPick.Visible = lPick.Text.Length > 0;
    }

    /// <summary>Клик по схеме, пока идёт запись. true — клик забран.</summary>
    bool OnSchemeClick(object o)
    {
        if (cur == null || pick == Pick.None) return false;
        switch (pick)
        {
            case Pick.Start when o is RailSignal s:
                if (s.Kind != SignalKind.Controlled) { il.Say($"{s.Label}: свой маршрут начинается только со светофора, который открывает дежурный", LogKind.Refuse); return true; }
                cur.Start = lay.KeyOf(s);
                cur.Shunt = s.IsShuntOnly;
                if (!cur.Aspects.ContainsKey(cur.Start)) cur.Aspects[cur.Start] = "auto";
                pick = Pick.End;
                break;
            case Pick.End when o is RailSignal s:
                cur.End = lay.KeyOf(s);
                pick = Pick.Body;
                if (cur.Sections.Count == 0) FillFromAuto(false);
                break;
            case Pick.End when o is TrackSection es:
            {
                // клик по пути вместо n2 — маршрут без светофора конца, этот участок — первый в списке
                cur.End = null;
                pick = Pick.Body;
                var n = lay.NameOf(es);
                if (n != null && !cur.Sections.Contains(n)) cur.Sections.Add(n);
                break;
            }
            case Pick.Body when o is TrackSection sec:
            {
                var n = lay.NameOf(sec);
                if (n == null) return true;
                if (!cur.Sections.Remove(n)) cur.Sections.Add(n);
                break;
            }
            case Pick.Body when o is RailSwitch sw:
            {
                // + → − → убрать; стрелка на схеме переводится, чтобы было видно путь
                var k = lay.KeyOf(sw);
                if (!cur.Switches.TryGetValue(k, out var plus)) cur.Switches[k] = sw.Plus;
                else if (plus) cur.Switches[k] = false;
                else cur.Switches.Remove(k);
                if (cur.Switches.TryGetValue(k, out var want) && sw.Plus != want && !sw.Locked) il.ToggleSwitch(sw);
                break;
            }
            case Pick.Body when o is RailSignal s:
            {
                var k = lay.KeyOf(s);
                if (k == cur.Start) return true;
                if (!cur.Aspects.Remove(k)) cur.Aspects[k] = s.Kind == SignalKind.Controlled ? (s.IsShuntOnly ? "white" : "yellow") : "auto";
                break;
            }
            default:
                return pick != Pick.Body || o is not RailSwitch;   // в шагах 1–2 чужие клики просто гасим
        }
        // имя по n1 и n2 (или пути назначения) — пока его не меняли руками
        if (cur.Start != null && (string.IsNullOrWhiteSpace(cur.Name) || cur.Name == autoName))
            cur.Name = autoName = lay.SignalByKey(cur.Start)?.Label + " → " +
                                  (cur.End != null ? lay.SignalByKey(cur.End)?.Label : cur.Sections.LastOrDefault() ?? "…");
        RefreshRouteList();
        ShowRoute(cur);
        if (pick == Pick.Body) bRecord.Text = "Готово — закончить запись";
        return true;
    }

    /// <summary>Участки и стрелки — из автомаршрута с тем же n1 и n2 (или путём назначения), если такой найден.</summary>
    void FillFromAuto(bool loud)
    {
        if (cur?.Start == null) return;
        var s = lay.SignalByKey(cur.Start);
        var e = lay.SignalByKey(cur.End);
        var dest = lay.SectionByName(cur.Sections.LastOrDefault());
        var r = il.Routes.FirstOrDefault(x => x.FixedAspects == null && x.Start == s && (e != null ? x.End == e : x.Dest == dest));
        if (r == null)
        {
            if (loud) MessageBox.Show(this, "Автомаршрута с таким началом и концом нет — отметьте участки и стрелки кликами.", Text);
            return;
        }
        cur.Sections = r.Sections.Concat(r.ExitSection != null ? new[] { r.ExitSection } : Array.Empty<TrackSection>())
                                 .Select(lay.NameOf).Where(n => n != null).Distinct().ToList();
        cur.Switches = r.Switches.ToDictionary(x => lay.KeyOf(x.sw), x => x.plus);
        cur.Shunt = r.Shunting;
        ShowRoute(cur);
    }

    void SwitchesFromSections()
    {
        if (cur == null) return;
        var secs = cur.Sections.Select(lay.SectionByName).Where(x => x != null).ToHashSet();
        foreach (var sw in il.Switches.Values.Where(sw => sw.Sections.Any(secs.Contains)))
            cur.Switches[lay.KeyOf(sw)] = sw.Plus;
        ShowRoute(cur);
    }

    // =================================================================
    //  ВКЛАДКА «АВТОМАРШРУТЫ»
    // =================================================================
    readonly DataGridView gAuto = Grid();
    readonly CheckBox cbAuto = new() { Text = "Автомаршруты (найдены по схеме автоматически)", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(4) };

    TabPage AutoPage()
    {
        var page = new TabPage("Автомаршруты") { Padding = new Padding(6) };
        cbAuto.Checked = work.AutoOn;
        gAuto.Dock = DockStyle.Fill;
        gAuto.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Вкл", FillWeight = 25 });
        gAuto.Columns.Add(TextCol("Вид", true, 40));
        gAuto.Columns.Add(TextCol("Маршрут по схеме", true, 110));
        gAuto.Columns.Add(TextCol("Своё имя", false, 100));
        gAuto.Columns.Add(TextCol("Стрелки", true, 90));
        foreach (var a in lay.AutoList)
        {
            work.Auto.TryGetValue(a.Key, out var o);
            int i = gAuto.Rows.Add(o?.Off != true, a.Shunt ? "манёвр." : "поездной", a.Name + (a.Replaced ? "  [заменён своим]" : ""), o?.Name ?? "", a.Switches);
            gAuto.Rows[i].Tag = a.Key;
            if (a.Replaced) gAuto.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
        }
        cbAuto.CheckedChanged += (_, _) => gAuto.Enabled = cbAuto.Checked;
        gAuto.Enabled = cbAuto.Checked;
        page.Controls.Add(gAuto);
        page.Controls.Add(cbAuto);
        page.Controls.Add(Hint("Выключенные и заменённые своими маршрутами автомаршруты на станции не задаются. " +
                               "Снимите галочку вверху — останутся только свои маршруты."));
        return page;
    }

    // =================================================================
    //  ВКЛАДКА «СВЕТОФОРЫ»
    // =================================================================
    readonly DataGridView gSig = Grid();
    static readonly string[] Kinds = { "entry", "exit", "route", "shunt", "block", "other", "hump", "humprep" };
    List<string> kindNames;

    TabPage SignalsPage()
    {
        var page = new TabPage("Светофоры") { Padding = new Padding(6) };
        gSig.Dock = DockStyle.Fill;
        kindNames = TC.SigKinds.Select(k => k.Name + (Kinds.Contains(k.Id) ? "" : " — не показывается")).ToList();
        gSig.Columns.Add(TextCol("Клетка", true, 45));
        gSig.Columns.Add(TextCol("Имя по схеме", true, 60));
        gSig.Columns.Add(TextCol("Своё имя", false, 60));
        gSig.Columns.Add(Combo("Вид", kindNames, 110));
        foreach (var s in lay.SignalList)
        {
            work.Signals.TryGetValue(s.Key, out var o);
            var kid = !string.IsNullOrWhiteSpace(o?.Kind) ? o.Kind : s.Kind;
            int ki = Array.FindIndex(TC.SigKinds, k => k.Id == kid);
            int i = gSig.Rows.Add(s.Cell, s.Name, o?.Name ?? "", ki >= 0 ? kindNames[ki] : kindNames[^1]);
            gSig.Rows[i].Tag = s.Key;
        }
        gSig.DataError += (_, e) => e.ThrowException = false;
        page.Controls.Add(gSig);
        page.Controls.Add(Hint("Своё имя — на схеме и в маршрутах. Вид определяет огни и поведение: входной, выходной, маршрутный, " +
                               "маневровый, проходной, соседний. Предупредительный, повторительный и др. программа станции пока не показывает."));
        return page;
    }

    // =================================================================
    //  ВКЛАДКА «СТРЕЛКИ»
    // =================================================================
    readonly DataGridView gSwitch = Grid();
    static readonly string[] StartPos = { "как в схеме", "плюс", "минус" };

    TabPage SwitchesPage()
    {
        var page = new TabPage("Стрелки") { Padding = new Padding(6) };
        gSwitch.Dock = DockStyle.Fill;
        gSwitch.Columns.Add(TextCol("Стрелки схемы", true, 60));
        gSwitch.Columns.Add(TextCol("Номер по схеме", true, 50));
        gSwitch.Columns.Add(TextCol("Номер на пульте", false, 50));
        gSwitch.Columns.Add(Combo("Начальное положение", StartPos, 70));
        foreach (var s in lay.SwitchList)
        {
            work.Switches.TryGetValue(s.Key, out var o);
            int i = gSwitch.Rows.Add(s.Tiles, s.Label, o?.Label ?? "",
                o?.Minus == null ? StartPos[0] : o.Minus == true ? StartPos[2] : StartPos[1]);
            gSwitch.Rows[i].Tag = s.Key;
        }
        gSwitch.DataError += (_, e) => e.ThrowException = false;
        page.Controls.Add(gSwitch);
        page.Controls.Add(Hint($"Номер на пульте — на кнопке стрелки (у одиночной — и на схеме). Начальное положение — в каком стоит при запуске " +
                               $"(в схеме сейчас: {string.Join(", ", lay.SwitchList.Select(x => x.Label + (x.Minus ? "−" : "+")))})."));
        return page;
    }

    // =================================================================
    //  ВКЛАДКА «ВРЕМЕНА»
    // =================================================================
    readonly List<NumericUpDown> times = new();

    TabPage TimesPage()
    {
        var page = new TabPage("Времена") { Padding = new Padding(10) };
        var def = new Interlocking();
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        foreach (var f in TimeSettings.Fields)
        {
            var n = new NumericUpDown { Minimum = 0, Maximum = 600, DecimalPlaces = 1, Increment = 0.5m, Width = 80, Value = (decimal)(f.Get(work.Times) ?? f.Def(def)) };
            times.Add(n);
            t.Controls.Add(new Label { Text = f.Title + ", с", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            t.Controls.Add(n);
            t.Controls.Add(new Label { Text = $"по умолчанию {f.Def(def):0.#}", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 6, 0, 0) });
        }
        page.Controls.Add(t);
        page.Controls.Add(Hint("Времена зависимостей этой станции. На макете их укорачивают, чтобы на занятии не ждать " +
                               "(в жизни искусственная разделка поездного маршрута ~3 мин, маневрового ~1 мин)."));
        return page;
    }

    // =================================================================
    //  СОХРАНИТЬ
    // =================================================================
    void SaveAndRestart()
    {
        gAuto.EndEdit(); gSig.EndEdit(); gSwitch.EndEdit(); gSw.EndEdit(); gAsp.EndEdit();
        var bad = work.Routes.Where(r => r.Off != true && (r.Start == null || r.Sections.Count == 0)).Select(r => r.Name).ToList();
        if (bad.Count > 0 && MessageBox.Show(this, "У маршрутов без начала или без участков: " + string.Join(", ", bad) +
                "\nони не будут работать. Всё равно сохранить?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        // автомаршруты
        work.AutoRoutes = cbAuto.Checked ? null : false;
        work.Auto.Clear();
        foreach (DataGridViewRow row in gAuto.Rows)
        {
            var name = (row.Cells[3].Value as string)?.Trim();
            bool on = row.Cells[0].Value is true;
            if (!on || !string.IsNullOrEmpty(name))
                work.Auto[(string)row.Tag] = new AutoRouteSetting { Off = on ? null : true, Name = string.IsNullOrEmpty(name) ? null : name };
        }
        // светофоры
        work.Signals.Clear();
        foreach (DataGridViewRow row in gSig.Rows)
        {
            var key = (string)row.Tag;
            var def = lay.SignalList.First(x => x.Key == key);
            var name = (row.Cells[2].Value as string)?.Trim();
            var kn = row.Cells[3].Value as string;
            int ki = kn == null ? -1 : kindNames.IndexOf(kn);
            var kind = ki >= 0 ? TC.SigKinds[ki].Id : def.Kind;
            var o = new SignalSetting
            {
                Name = string.IsNullOrEmpty(name) || name == def.Name ? null : name,
                Kind = kind == def.Kind ? null : kind,
            };
            if (!o.IsEmpty) work.Signals[key] = o;
        }
        // стрелки
        work.Switches.Clear();
        foreach (DataGridViewRow row in gSwitch.Rows)
        {
            var key = (string)row.Tag;
            var def = lay.SwitchList.First(x => x.Key == key);
            var label = (row.Cells[2].Value as string)?.Trim();
            var pos = row.Cells[3].Value as string;
            var o = new SwitchSetting
            {
                Label = string.IsNullOrEmpty(label) || label == def.Label ? null : label,
                Minus = pos == StartPos[1] ? (def.Minus ? false : null) : pos == StartPos[2] ? (def.Minus ? null : true) : null,
            };
            if (!o.IsEmpty) work.Switches[key] = o;
        }
        // времена
        var d = new Interlocking();
        for (int i = 0; i < TimeSettings.Fields.Length; i++)
        {
            var f = TimeSettings.Fields[i];
            double v = (double)times[i].Value;
            f.Set(work.Times, Math.Abs(v - f.Def(d)) < 1e-9 ? null : v);
        }
        // свои маршруты: «авто» у показания — то же, что не задано
        foreach (var r in work.Routes)
            foreach (var k in r.Aspects.Where(p => p.Value == "auto").Select(p => p.Key).ToList()) r.Aspects.Remove(k);

        try { work.Save(ModelHost.Model.FilePath); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось записать настройку:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Close();
        restart(ModelHost.Model.FilePath);
    }
}
