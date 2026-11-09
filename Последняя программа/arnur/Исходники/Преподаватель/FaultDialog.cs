using System.Text.Json;

namespace TeacherApp;

// =====================================================================
//  НЕИСПРАВНОСТИ («Экспресс-вводные»)  [ДОБАВЛЕНО 20.10.2026, этап 3]
//   • FaultsWindow  — неисправности выбранной станции: что задано, ждёт ли условия, действует, устранена;
//                     «Снять неисправность» — преподаватель устраняет её сам;
//   • ExpressDialog — окно «Экспресс-вводная», как в тренажёре ДСП (инструкция преподавателя, раздел 2):
//                     объект, неисправность (объекты ЭЦ / подвижные единицы), условие возникновения, начало.
//  Список объектов и видов неисправностей присылает сама станция (s_objects), см. Core/Faults.cs станций.
// =====================================================================

public class FaultsWindow : Form
{
    readonly StationRt st;
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
    readonly Label info = new() { Dock = DockStyle.Top, Height = 58, Padding = new Padding(6), ForeColor = Color.DimGray };

    public FaultsWindow(StationRt station)
    {
        st = station;
        Text = $"Неисправности — {st.Name}";
        Font = new Font("Segoe UI", 10f);
        Size = new Size(1000, 460);
        StartPosition = FormStartPosition.CenterParent;

        list.Columns.Add("№", 50);
        list.Columns.Add("Неисправность", 260);
        list.Columns.Add("Объект", 170);
        list.Columns.Add("Условие", 300);
        list.Columns.Add("Состояние", 180);
        info.Text = "Студент неисправность не видит — он должен сам понять, что случилось, записать в журнал ДУ-46 и сообщить ШН и ДНЦ.\n" +
                    "Устраняет «виртуальный работник» по докладу, который подписывает студент, или вы — кнопкой «Снять неисправность».";

        var bAdd = new Button { Text = "Экспресс-вводная…", AutoSize = true, Margin = new Padding(4) };
        var bDel = new Button { Text = "Снять неисправность", AutoSize = true, Margin = new Padding(4) };
        var bClose = new Button { Text = "Закрыть", AutoSize = true, Margin = new Padding(4) };
        bAdd.Click += (_, _) =>
        {
            if (!Classroom.Active) { MessageBox.Show(this, "Неисправности задаются только во время урока. Начните урок."); return; }
            if (!st.Online) { MessageBox.Show(this, "Станция не на связи."); return; }
            using var d = new ExpressDialog(st);
            d.ShowDialog(this);
        };
        bDel.Click += (_, _) =>
        {
            if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not FaultRow f) { MessageBox.Show(this, "Выберите неисправность в списке."); return; }
            if (f.State is "fixed" or "removed" or "gone") { MessageBox.Show(this, "Эта неисправность уже не действует."); return; }
            Classroom.RemoveFault(st, f);
        };
        bClose.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { bAdd, bDel, bClose });

        Controls.Add(list);
        Controls.Add(info);
        Controls.Add(buttons);

        Classroom.Changed += Fill;
        FormClosed += (_, _) => Classroom.Changed -= Fill;
        Fill();
    }

    void Fill()
    {
        if (IsDisposed) return;
        string sel = list.SelectedItems.Count > 0 ? (list.SelectedItems[0].Tag as FaultRow)?.Id : null;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var f in st.Faults)
        {
            string label = f.Label + (f.Train > 0 && !f.Label.StartsWith("поезд") ? $" (поезд №{f.Train})" : "");
            var it = new ListViewItem(new[] { f.Id.TrimStart('f'), f.Name, label, f.Cond,
                                              f.StateText + (f.Activated >= 0 ? $" (с {Reports.Clock(f.Activated)})" : "") }) { Tag = f };
            it.ForeColor = f.State switch
            {
                "active" => Color.DarkRed,
                "fixing" => Color.DarkOrange,
                "wait" => Color.FromArgb(20, 60, 150),
                _ => Color.Gray
            };
            if (f.Id == sel) it.Selected = true;
            list.Items.Add(it);
        }
        list.EndUpdate();
    }
}

public class ExpressDialog : Form
{
    readonly StationRt st;
    readonly Label lblWait = new() { AutoSize = true, ForeColor = Color.DarkOrange, Text = "Запрашиваю у станции список объектов…" };
    readonly ComboBox cbKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly ComboBox cbObj = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    readonly TabControl tabs = new() { Width = 560, Height = 190 };
    readonly TabPage tabEc = new("Объекты ЭЦ"), tabTr = new("Подвижные единицы");
    readonly FlowLayoutPanel ecList = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true };
    readonly FlowLayoutPanel trList = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true };
    readonly ComboBox cbCond = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    readonly ComboBox cbTrain = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly NumericUpDown numTrain = new() { Minimum = 1, Maximum = 99999, Width = 80, Visible = false };
    readonly CheckBox chkNow = new() { Text = "Немедленно", AutoSize = true, Checked = true };
    readonly DateTimePicker time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, Width = 100, Enabled = false };
    readonly Button bOk = new() { Text = "Принять", Width = 110, Height = 32, Enabled = false };

    record Item(string Id, string Text) { public override string ToString() => Text; }
    record Kind(string Code, string Name, string Target);

    static readonly (string target, string title, string arr)[] Targets =
    {
        ("signal", "Светофор", "signals"), ("switch", "Стрелка", "switches"), ("section", "Участок", "sections"),
        ("line", "Перегон", "lines"), ("train", "Поезд", "trains"),
    };

    List<Kind> kinds = new();
    Dictionary<string, string[]> states = new();
    string chosen;

    public ExpressDialog(StationRt station)
    {
        st = station;
        Text = "Экспресс-вводная";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Padding = new Padding(12) };
        p.Controls.Add(new Label { Text = $"Станция: {st.Name}", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        p.Controls.Add(lblWait);
        p.Controls.Add(Row(L("Объект:"), cbKind, cbObj));

        tabEc.Controls.Add(ecList);
        tabTr.Controls.Add(trList);
        tabs.TabPages.Add(tabEc);
        tabs.TabPages.Add(tabTr);
        p.Controls.Add(L("Выбор неисправности:"));
        p.Controls.Add(tabs);

        var gCond = new GroupBox { Text = "Условия возникновения неисправности", AutoSize = true, Padding = new Padding(8), MinimumSize = new Size(560, 0) };
        var pc = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        pc.Controls.Add(Row(L("Объекты ЭЦ:"), cbCond));
        pc.Controls.Add(Row(L("Подвижные единицы:"), cbTrain, numTrain));
        gCond.Controls.Add(pc);
        p.Controls.Add(gCond);

        var gStart = new GroupBox { Text = "Начало ожидания условия (игровое время)", AutoSize = true, Padding = new Padding(8), MinimumSize = new Size(560, 0) };
        var startRow = Row(chkNow, time);
        startRow.Dock = DockStyle.Fill;
        gStart.Controls.Add(startRow);
        p.Controls.Add(gStart);

        var bCancel = new Button { Text = "Отказ", Width = 110, Height = 32, DialogResult = DialogResult.Cancel };
        bOk.Click += (_, _) => Accept();
        p.Controls.Add(Row(bOk, bCancel));
        Controls.Add(p);
        CancelButton = bCancel;

        foreach (var (_, title, _) in Targets) cbKind.Items.Add(title);
        cbTrain.Items.AddRange(new object[]
        {
            new Item("none", "нет"), new Item("any", "любая подвижная единица"), new Item("number", "поезд с номером"),
            new Item("train", "поездной"), new Item("shunt", "маневровый"),
            new Item("pass", "пассажирский"), new Item("freight", "грузовой"),   // [ДОБАВЛЕНО 23.10.2026, этап 4] категория поезда
        });
        cbTrain.SelectedIndex = 0;
        cbTrain.SelectedIndexChanged += (_, _) => numTrain.Visible = ((Item)cbTrain.SelectedItem).Id == "number";
        cbKind.SelectedIndexChanged += (_, _) => FillObjects();
        cbObj.SelectedIndexChanged += (_, _) => bOk.Enabled = cbObj.SelectedItem != null && chosen != null;
        chkNow.CheckedChanged += (_, _) => time.Enabled = !chkNow.Checked;
        time.Value = DateTime.Today.AddSeconds(Classroom.Clock % 86400);

        st.Objects = null;
        Classroom.RequestObjects(st);
        Classroom.Changed += OnChanged;
        FormClosed += (_, _) => Classroom.Changed -= OnChanged;
    }

    static Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(3, 7, 3, 0) };

    static FlowLayoutPanel Row(params Control[] c)
    {
        var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        r.Controls.AddRange(c);
        return r;
    }

    JsonElement Objects => st.Objects!.Value;

    void OnChanged()
    {
        if (IsDisposed || st.Objects == null || kinds.Count > 0) return;
        try
        {
            kinds = Objects.GetProperty("kinds").Deserialize<List<Kind>>(Db.Wire) ?? new();
            states = Objects.GetProperty("states").Deserialize<Dictionary<string, string[]>>(Db.Wire) ?? new();
        }
        catch { lblWait.Text = "Станция прислала неполные данные. Закройте окно и попробуйте ещё раз."; return; }
        lblWait.Visible = false;
        foreach (var k in kinds.Where(k => k.Target == "train"))
            trList.Controls.Add(Radio(k));
        cbKind.SelectedIndex = 0;
    }

    RadioButton Radio(Kind k)
    {
        var r = new RadioButton { Text = k.Name, AutoSize = true, Tag = k.Code };
        r.CheckedChanged += (_, _) =>
        {
            if (!r.Checked) return;
            chosen = k.Code;
            // одна неисправность на обе вкладки
            foreach (var other in ecList.Controls.Cast<RadioButton>().Concat(trList.Controls.Cast<RadioButton>()))
                if (other != r) other.Checked = false;
            bOk.Enabled = cbObj.SelectedItem != null;
        };
        return r;
    }

    string Target => cbKind.SelectedIndex >= 0 ? Targets[cbKind.SelectedIndex].target : "";

    void FillObjects()
    {
        var (target, _, arr) = Targets[cbKind.SelectedIndex];
        cbObj.Items.Clear();
        if (Objects.TryGetProperty(arr, out var a))
            foreach (var o in a.EnumerateArray())
                cbObj.Items.Add(new Item(o.GetProperty("id").GetString(), o.GetProperty("label").GetString()));
        if (cbObj.Items.Count > 0) cbObj.SelectedIndex = 0;

        // неисправности объектов ЭЦ — по виду объекта; у поезда — только подвижной единицы
        chosen = null;
        ecList.Controls.Clear();
        foreach (var k in kinds.Where(k => k.Target == target && target != "train")) ecList.Controls.Add(Radio(k));
        foreach (RadioButton r in trList.Controls) r.Checked = false;
        tabEc.Text = target == "train" ? "Объекты ЭЦ (нет)" : "Объекты ЭЦ";
        tabs.SelectedTab = target == "train" ? tabTr : tabEc;

        cbCond.Items.Clear();
        cbCond.Items.Add(new Item("none|", "нет"));
        if (states.TryGetValue(target, out var sts))
        {
            foreach (var s in sts) cbCond.Items.Add(new Item("state|" + s, $"состояние <{s}>"));
            foreach (var s in sts) cbCond.Items.Add(new Item("to|" + s, $"переход в состояние <{s}>"));
            foreach (var s in sts) cbCond.Items.Add(new Item("from|" + s, $"переход из состояния <{s}>"));
        }
        cbCond.SelectedIndex = 0;
        cbTrain.Enabled = target != "train";
        if (target == "train") cbTrain.SelectedIndex = 0;
        bOk.Enabled = false;
        if (cbObj.Items.Count == 0) lblWait.Text = target == "train" ? "На станции нет виртуальных поездов." : "Таких объектов нет.";
        lblWait.Visible = cbObj.Items.Count == 0;
    }

    void Accept()
    {
        if (cbObj.SelectedItem is not Item obj || chosen == null) return;
        var cond = ((Item)cbCond.SelectedItem).Id.Split('|');
        var tf = ((Item)cbTrain.SelectedItem).Id;
        double start = chkNow.Checked ? -1 : time.Value.TimeOfDay.TotalSeconds;
        var kind = kinds.First(k => k.Code == chosen);
        Classroom.AddFault(st, new
        {
            kind = chosen, obj = obj.Id, cond = cond[0], condState = cond[1],
            trainFilter = tf, trainNumber = (int)numTrain.Value, start,
        }, $"{kind.Name}, {Targets[cbKind.SelectedIndex].title.ToLower()} {obj.Text}");
        DialogResult = DialogResult.OK;
        Close();
    }
}
