using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  ОКНА ЖУРНАЛОВ ДУ-2, ДУ-47, ДУ-58, ДУ-60 И БЛАНКОВ  [ДОБАВЛЕНО 23.10.2026, этап 4]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Логика — Core/Journals.cs.
//  Кнопка «Журналы» в верхней полосе окна → меню: журналы, бланки, радиосвязь, предупреждения.
//  Журнал: таблица записей слева, форма справа. «Новая запись» → заполнить → «Сохранить».
//  Выбрать запись в таблице → исправить → «Сохранить» (исправление помечается). Удалять нельзя.
// =====================================================================

public static class JournalsUi
{
    static readonly Dictionary<string, Form> open = new();

    /// <summary>Меню кнопки «Журналы».</summary>
    public static void ShowMenu(Control anchor)
    {
        var m = new ContextMenuStrip();
        foreach (var d in Journals.Defs)
        {
            var code = d.Code;
            m.Items.Add(d.Title, null, (_, _) => OpenJournal(anchor, code));
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Бланки…", null, (_, _) => OpenBlanks(anchor, 0));
        var radio = new ToolStripMenuItem("Поездная радиосвязь");
        var sim = ModelHost.Model.Trains;
        foreach (var t in sim.Trains.Where(t => !t.Leaving).OrderBy(t => t.Number))
        {
            var tt = t;
            radio.DropDownItems.Add($"Поезд №{t.Number} ({t.Category}) — {t.Head?.Label}", null, (_, _) => RadioUi.OpenRadio(tt, tt.Number, anchor));
        }
        foreach (var n in Radio.HwNumbers)
        {
            int nn = n;
            radio.DropDownItems.Add($"Поезд №{n} (макет) — {Radio.HwWhere(n)}", null, (_, _) => RadioUi.OpenRadio(null, nn, anchor));
        }
        if (radio.DropDownItems.Count == 0) radio.DropDownItems.Add(new ToolStripMenuItem("поездов на станции нет") { Enabled = false });
        m.Items.Add(radio);
        m.Items.Add("Предупреждения и «окна»…", null, (_, _) => Show(anchor, "restrictions", () => new RestrictionsWindow()));
        m.Items.Add("ДУ-46 — журнал осмотра…", null, (_, _) => Du46Ui.Open());
        m.Show(anchor, new Point(0, anchor.Height));
    }

    public static void OpenJournal(Control owner, string code) => Show(owner, code, () => new JournalWindow(Journals.Def(code)));

    public static void OpenBlanks(Control owner, int train)
    {
        var f = (BlanksWindow)Show(owner, "blanks", () => new BlanksWindow());
        if (train > 0) f.SelectTrain(train);
    }

    static Form Show(Control owner, string key, Func<Form> make)
    {
        if (!open.TryGetValue(key, out var f) || f.IsDisposed)
        {
            open[key] = f = make();
            f.Show(owner.FindForm());
        }
        if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
        f.BringToFront();
        f.Activate();
        return f;
    }

    /// <summary>Поле формы по описанию: TextBox / ComboBox / CheckBox.</summary>
    internal static Control Editor(JField f)
    {
        switch (f.Kind)
        {
            case "multi": return new TextBox { Multiline = true, Height = 90, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            case "check": return new CheckBox { Text = f.Label, AutoSize = true };
            case "choice":
            {
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
                cb.Items.AddRange(Journals.ChoicesOf(f).Cast<object>().ToArray());
                return cb;
            }
            case "time": return new TextBox { Width = 70, PlaceholderText = "ч:мин" };
            default: return new TextBox { Dock = DockStyle.Fill };
        }
    }

    internal static string Get(Control c) => c is CheckBox ch ? (ch.Checked ? "1" : "") : c.Text.Trim();

    internal static void Set(Control c, string v)
    {
        if (c is CheckBox ch) ch.Checked = v == "1";
        else c.Text = v ?? "";
    }

    /// <summary>Таблица «подпись — поле» для формы журнала или бланка.</summary>
    internal static TableLayoutPanel FieldTable(IEnumerable<JField> fields, Dictionary<string, Control> editors, int labelWidth)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var f in fields)
        {
            var ed = Editor(f);
            if (f.ReadOnly && ed is TextBox tb) tb.ReadOnly = true;
            editors[f.Key] = ed;
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (f.Kind == "check") { t.Controls.Add(ed, 1, row); continue; }
            t.Controls.Add(new Label { Text = f.Label, AutoSize = false, Width = labelWidth - 6, Height = f.Kind == "multi" ? 48 : 34,
                                       TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            t.Controls.Add(ed, 1, row);
        }
        return t;
    }

    internal static Button Btn(string text, int w = 0) => Du46Ui.Btn(text, w);
}

// ---------------------------------------------------------------------
//  Журнал (ДУ-2, ДУ-47, ДУ-58, ДУ-60): таблица + форма
// ---------------------------------------------------------------------
class JournalWindow : Form
{
    readonly JournalDef def;
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false };
    readonly Dictionary<string, Control> ed = new();
    readonly Label mode = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(3, 8, 3, 3) };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkGreen, Margin = new Padding(3, 8, 3, 3) };
    JEntry editing;

    public JournalWindow(JournalDef d)
    {
        def = d;
        Text = d.Title;
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(1280, 720);
        MinimumSize = new Size(960, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 0) };
        top.Controls.Add(new Label { Text = $"Станция: {DispatchLink.StationName}    ДСП: {Training.Student?.Name ?? "—"}", AutoSize = true,
                                     Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 6, 24, 3) });
        top.Controls.Add(new Label { Text = d.Hint, AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(3, 6, 3, 3), ForeColor = Color.DimGray });

        list.Columns.Add("№", 40);
        foreach (var f in d.Fields.Where(f => f.Width > 0)) list.Columns.Add(f.Label, f.Width);
        list.Columns.Add("Исправления / откуда", 220);
        list.SelectedIndexChanged += (_, _) => { if (list.SelectedItems.Count == 1) LoadEntry((JEntry)list.SelectedItems[0].Tag); };

        // --- форма справа ---
        var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
        var form = JournalsUi.FieldTable(d.Fields, ed, 170);
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        head.Controls.Add(mode);
        if (d.Templates.Length > 0)
        {
            var cbT = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
            cbT.Items.Add("(заготовка текста…)");
            foreach (var (name, _) in d.Templates) cbT.Items.Add(name);
            cbT.SelectedIndex = 0;
            cbT.SelectedIndexChanged += (_, _) =>
            {
                if (cbT.SelectedIndex <= 0) return;
                string line = ed.TryGetValue("line", out var lc) ? lc.Text : "";
                ed["text"].Text = Journals.Fill(d.Templates[cbT.SelectedIndex - 1].text, line);
                cbT.SelectedIndex = 0;
                ed["text"].Focus();
            };
            head.Controls.Add(cbT);
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var bNew = JournalsUi.Btn("Новая запись");
        var bSave = JournalsUi.Btn(d.Code == "ДУ-47" ? "Сохранить / передать" : "Сохранить");
        bNew.Click += (_, _) => New();
        bSave.Click += (_, _) => Save();
        buttons.Controls.AddRange(new Control[] { bNew, bSave });
        if (d.Code == "ДУ-60")
        {
            var bCancel = JournalsUi.Btn("Отменить выделенное");
            bCancel.Click += (_, _) => { if (editing != null) Journals.CancelWarning(editing); else Status("Выберите предупреждение в таблице.", true); };
            var bFill = JournalsUi.Btn("По выданному предупреждению…");
            bFill.Click += (_, _) => FillFromRestriction();
            buttons.Controls.AddRange(new Control[] { bCancel, bFill });
        }
        buttons.Controls.Add(status);
        right.Controls.Add(buttons);
        right.Controls.Add(form);
        right.Controls.Add(head);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        Shown += (_, _) => { try { split.SplitterDistance = (int)(split.Width * 0.58); } catch { } };
        split.Panel1.Controls.Add(list);
        split.Panel2.Controls.Add(right);
        Controls.Add(split);
        Controls.Add(top);

        Journals.Changed += Refill;
        FormClosed += (_, _) => Journals.Changed -= Refill;
        Refill();
        New();
    }

    void New()
    {
        editing = null;
        list.SelectedItems.Clear();
        foreach (var f in def.Fields) JournalsUi.Set(ed[f.Key], f.Default?.Invoke() ?? "");
        mode.Text = "Новая запись";
        Status("", false);
    }

    void LoadEntry(JEntry e)
    {
        editing = e;
        foreach (var f in def.Fields) JournalsUi.Set(ed[f.Key], e[f.Key]);
        mode.Text = $"Запись №{e.No} (исправление)";
    }

    Dictionary<string, string> Values() => def.Fields.ToDictionary(f => f.Key, f => JournalsUi.Get(ed[f.Key]));

    void Save()
    {
        var v = Values();
        if (v.Values.All(x => x.Length == 0)) { Status("Запись пустая.", true); return; }
        if (editing == null)
        {
            var e = Journals.Add(def.Code, v);
            Status(def.Code == "ДУ-47" ? $"Телефонограмма №{e["outNo"]} передана, ждите ответа." : $"Запись №{e.No} внесена.", false);
            New();
        }
        else
        {
            Journals.Update(def.Code, editing, v);
            Status($"Запись №{editing.No} исправлена.", false);
        }
    }

    void FillFromRestriction()
    {
        var act = Radio.Restrictions.Where(r => !r.IsWindow).ToList();
        if (act.Count == 0) { Status("Предупреждений не выдавалось.", true); return; }
        var m = new ContextMenuStrip();
        foreach (var r in act)
        {
            var rr = r;
            m.Items.Add(r.Text, null, (_, _) =>
            {
                New();
                JournalsUi.Set(ed["start"], Training.Fmt(rr.From)[..5]);
                JournalsUi.Set(ed["place"], rr.ObjLabel + (rr.Place.Length > 0 ? ", " + rr.Place : ""));
                JournalsUi.Set(ed["speed"], rr.SpeedPass == rr.SpeedFreight ? rr.SpeedPass.ToString() : $"{rr.SpeedPass}/{rr.SpeedFreight}");
                JournalsUi.Set(ed["end"], rr.Until < 0 ? "до отмены" : Training.Fmt(rr.Until)[..5]);
                ed["got"].Focus();
            });
        }
        m.Show(Cursor.Position);
    }

    void Status(string text, bool bad)
    {
        status.ForeColor = bad ? Color.DarkRed : Color.DarkGreen;
        status.Text = text;
    }

    void Refill()
    {
        if (IsDisposed) return;
        var sel = editing;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var e in Journals.Entries[def.Code])
        {
            var cells = new List<string> { e.No.ToString() };
            cells.AddRange(def.Fields.Where(f => f.Width > 0).Select(f => e[f.Key].Replace("\r\n", " ").Replace('\n', ' ')));
            cells.Add(string.Join("; ", e.Edits.Prepend(e.Source).Where(s => s.Length > 0)));
            var it = new ListViewItem(cells.ToArray()) { Tag = e };
            if (e.Source.Length > 0) it.ForeColor = Color.FromArgb(20, 60, 150);
            if (e.Edits.Count > 0) it.BackColor = Color.FromArgb(255, 250, 225);
            if (def.Code == "ДУ-60" && e["cancel"].Length > 0) it.ForeColor = Color.Gray;
            list.Items.Add(it);
            if (e == sel) it.Selected = true;
        }
        if (list.Items.Count > 0) list.EnsureVisible(list.Items.Count - 1);
        list.EndUpdate();
    }
}

// ---------------------------------------------------------------------
//  Бланки (рис. 51 части 2): создать, заполнить, вручить машинисту
// ---------------------------------------------------------------------
class BlanksWindow : Form
{
    readonly ComboBox cbKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false };
    readonly ComboBox cbTrain = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 260 };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkGreen, Margin = new Padding(8, 9, 0, 0) };

    public BlanksWindow()
    {
        Text = "Бланки";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(900, 520);
        MinimumSize = new Size(760, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        foreach (var d in Journals.BlankDefs) cbKind.Items.Add(d.Title);
        cbKind.SelectedIndex = 0;
        var bCreate = JournalsUi.Btn("Создать бланк");
        bCreate.Click += (_, _) => Edit(null, Journals.BlankDefs[cbKind.SelectedIndex]);
        var bOpen = JournalsUi.Btn("Открыть / изменить");
        bOpen.Click += (_, _) => { if (Selected is { } b) Edit(b, b.Def); };
        list.DoubleClick += (_, _) => { if (Selected is { } b) Edit(b, b.Def); };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        top.Controls.AddRange(new Control[] { cbKind, bCreate, bOpen });
        var hint = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 0, 8, 0), ForeColor = Color.DimGray };
        cbKind.SelectedIndexChanged += (_, _) => hint.Text = Journals.BlankDefs[cbKind.SelectedIndex].Hint;
        hint.Text = Journals.BlankDefs[0].Hint;

        list.Columns.Add("Бланк", 120);
        list.Columns.Add("Время", 60);
        list.Columns.Add("Вручён", 110);
        list.Columns.Add("Содержание", 560);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        var bHand = JournalsUi.Btn("Вручить машинисту");
        bHand.Click += (_, _) => Hand();
        var bClose = JournalsUi.Btn("Закрыть окно");
        bClose.Click += (_, _) => Close();
        bottom.Controls.AddRange(new Control[] { new Label { Text = "Поезд №", AutoSize = true, Margin = new Padding(3, 9, 3, 3) }, cbTrain, bHand, bClose, status });

        Controls.Add(list);
        Controls.Add(hint);
        Controls.Add(top);
        Controls.Add(bottom);
        FillTrains();
        cbTrain.DropDown += (_, _) => FillTrains();
        Journals.Changed += Refill;
        FormClosed += (_, _) => Journals.Changed -= Refill;
        Refill();
    }

    Blank Selected => list.SelectedItems.Count == 1 ? (Blank)list.SelectedItems[0].Tag : null;

    void FillTrains()
    {
        string keep = cbTrain.Text;
        cbTrain.Items.Clear();
        foreach (var t in ModelHost.Model.Trains.Trains.Where(t => !t.Leaving).OrderBy(t => t.Number)) cbTrain.Items.Add($"{t.Number} — {t.Head?.Label}");
        foreach (var n in Radio.HwNumbers) cbTrain.Items.Add($"{n} — макет");
        cbTrain.Text = keep;
    }

    public void SelectTrain(int n)
    {
        FillTrains();
        cbTrain.Text = cbTrain.Items.Cast<string>().FirstOrDefault(s => s.StartsWith(n + " ")) ?? n.ToString();
    }

    void Edit(Blank b, BlankDef d)
    {
        int.TryParse(new string(cbTrain.Text.TakeWhile(char.IsDigit).ToArray()), out var train);
        using var f = new BlankForm(d, b, train);
        f.ShowDialog(this);
    }

    void Hand()
    {
        var b = Selected;
        if (b == null) { Status("Выберите бланк в списке.", true); return; }
        if (!int.TryParse(new string(cbTrain.Text.Trim().TakeWhile(char.IsDigit).ToArray()), out var n)) { Status("Укажите поезд.", true); return; }
        var t = ModelHost.Model.Trains.Find(n);
        if (t == null && !Radio.HwNumbers.Contains(n)) { Status($"Поезда №{n} нет на станции.", true); return; }
        var err = Journals.Hand(b, t, n);
        if (err != null) Status(err, true);
        else Status($"{b.Title} вручён машинисту поезда №{n}.", false);
    }

    void Status(string text, bool bad)
    {
        status.ForeColor = bad ? Color.DarkRed : Color.DarkGreen;
        status.Text = text;
    }

    void Refill()
    {
        if (IsDisposed) return;
        var sel = Selected;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var b in Journals.Blanks)
        {
            var it = new ListViewItem(new[] { b.Title, Training.Fmt(b.Clock)[..5], b.HandedTo > 0 ? $"поезду №{b.HandedTo}" : "не вручён", Journals.BlankText(b) }) { Tag = b };
            if (b.HandedTo > 0) it.ForeColor = Color.DimGray;
            list.Items.Add(it);
            if (b == sel) it.Selected = true;
        }
        list.EndUpdate();
    }
}

// ---------------------------------------------------------------------
//  Заполнение бланка (рис. 52–58 части 2)
// ---------------------------------------------------------------------
class BlankForm : Form
{
    public BlankForm(BlankDef d, Blank b, int train)
    {
        Text = b?.Title ?? d.Title;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Size = new Size(620, 560);

        var ed = new Dictionary<string, Control>();
        var root = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var head = new Label
        {
            Dock = DockStyle.Top, Height = 54, Font = new Font(Font, FontStyle.Bold),
            Text = $"{d.Title}\nСтанция {DispatchLink.StationName}    {(b != null ? $"№{b.No}" : "№ (присвоится при сохранении)")}    {Training.Fmt(b?.Clock ?? Du46.Now)[..5]}",
        };
        var form = JournalsUi.FieldTable(d.Fields, ed, 230);
        var fixedText = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(560, 0), Text = d.Fixed, Padding = new Padding(0, 8, 0, 8) };
        var dsp = new Label { Dock = DockStyle.Top, Height = 28, Text = $"ДСП {Training.Student?.Name ?? ""}" };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var bSave = JournalsUi.Btn("Сохранить", 110);
        var bCancel = JournalsUi.Btn("Отмена", 110);
        buttons.Controls.AddRange(new Control[] { bSave, bCancel });
        if (d.Code == "ДУ-61")
        {
            var bFill = JournalsUi.Btn("Вписать действующие предупреждения");
            bFill.Click += (_, _) => ed["rows"].Text = string.Join(Environment.NewLine,
                Radio.ActiveAll.Where(r => !r.IsWindow).Select(r => $"{r.ObjLabel}{(r.Place.Length > 0 ? ", " + r.Place : "")}; {r.Period}; {r.SpeedText}; —"));
            buttons.Controls.Add(bFill);
        }
        root.Controls.Add(buttons);
        root.Controls.Add(dsp);
        root.Controls.Add(fixedText);
        root.Controls.Add(form);
        root.Controls.Add(head);
        Controls.Add(root);

        foreach (var f in d.Fields) JournalsUi.Set(ed[f.Key], b != null ? b[f.Key] : f.Default?.Invoke() ?? "");
        if (b == null && train > 0 && ed.TryGetValue("train", out var tr)) tr.Text = train.ToString();
        if (b?.HandedTo > 0) { bSave.Enabled = false; Text += " — вручён, изменить нельзя"; }

        bSave.Click += (_, _) =>
        {
            var v = d.Fields.ToDictionary(f => f.Key, f => JournalsUi.Get(ed[f.Key]));
            v["kind"] = d.Code;
            Journals.SaveBlank(b, v);
            DialogResult = DialogResult.OK;
        };
        bCancel.Click += (_, _) => Close();
    }
}

// ---------------------------------------------------------------------
//  Действующие предупреждения и «окна» (отмена)
// ---------------------------------------------------------------------
class RestrictionsWindow : Form
{
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };

    public RestrictionsWindow()
    {
        Text = "Предупреждения и «окна»";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(980, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        foreach (var (c, w) in new[] { ("№", 40), ("Вид", 110), ("Объект", 220), ("Место", 130), ("Скорость / служба", 150), ("Период", 160), ("Состояние", 120) })
            list.Columns.Add(c, w);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        var bCancel = JournalsUi.Btn("Отменить выделенное");
        bCancel.Click += (_, _) => { foreach (ListViewItem it in list.SelectedItems) Radio.Cancel((Restriction)it.Tag); };
        bottom.Controls.Add(bCancel);
        bottom.Controls.Add(new Label { Text = "Установить: ПКМ по участку или перегону на схеме → «Предупреждение / окно…»", AutoSize = true, Margin = new Padding(12, 9, 0, 0), ForeColor = Color.DimGray });
        Controls.Add(list);
        Controls.Add(bottom);
        Radio.Changed += Refill;
        FormClosed += (_, _) => Radio.Changed -= Refill;
        Refill();
    }

    void Refill()
    {
        if (IsDisposed) return;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var r in Radio.Restrictions)
        {
            var it = new ListViewItem(new[] { r.No.ToString(), r.IsWindow ? "окно" : "предупреждение", r.ObjLabel, r.Place, r.IsWindow ? r.Service : r.SpeedText,
                                              r.Period, r.Active ? "действует" : "отменено" }) { Tag = r };
            if (!r.Active) it.ForeColor = Color.Gray;
            else if (r.IsWindow) it.ForeColor = Color.DarkRed;
            list.Items.Add(it);
        }
        list.EndUpdate();
    }
}
