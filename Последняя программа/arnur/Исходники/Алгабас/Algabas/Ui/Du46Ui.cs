using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  ОКНА ЖУРНАЛА ОСМОТРА ДУ-46  [ДОБАВЛЕНО 20.10.2026, этап 3]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//   • Du46Window — «Устранение неисправности»: запись в журнал, «Сообщить» причастным,
//     доклады на подпись, нажатия кнопок со счётчиком без записи, весь журнал;
//   • WorkerMessageForm — сообщение («Закрыть») или доклад («Подписать» / «Закрыть») работника;
//   • CounterRecordForm — «Оформление записи» после нажатия кнопки со счётчиком.
//  Открывается: кнопка «ДУ-46» в верхней полосе, ПКМ по объекту → «Неисправность…».
// =====================================================================

public static class Du46Ui
{
    static Form main;
    static Du46Window window;
    static int stack;

    public static void Attach(Form mainForm)
    {
        if (main != null) return;
        main = mainForm;
        Du46.MessageArrived += m => Show(new WorkerMessageForm(m));
        Du46.CounterPressed += p => Show(new CounterRecordForm(p));
    }

    static void Show(Form f)
    {
        if (main == null || main.IsDisposed) return;
        // окна не перекрывают друг друга целиком: каждое следующее чуть ниже и правее
        var c = main.WindowState == FormWindowState.Minimized ? main.RestoreBounds : main.Bounds;
        int k = stack++ % 6;
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(c.X + (c.Width - f.Width) / 2 + k * 24, c.Y + (c.Height - f.Height) / 3 + k * 24);
        f.FormClosed += (_, _) => stack = Math.Max(0, stack - 1);
        f.Show(main);
    }

    /// <summary>Открыть журнал (с выбранным объектом, если задан).</summary>
    public static void Open(string obj = null)
    {
        if (main == null) return;
        if (window == null || window.IsDisposed) { window = new Du46Window(); window.Show(main); }
        if (obj != null) window.Select(obj);
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.BringToFront();
        window.Activate();
    }

    /// <summary>Все объекты для выбора: «Светофор Ч», «Стрелка 5/7»…</summary>
    public static List<ObjItem> AllObjects() =>
        new[] { "signal", "switch", "section", "line", "train" }
            .SelectMany(k => Faults.Objects(k).Select(o => new ObjItem(o.id, Cap(k == "train" ? "поезд " + o.label : Faults.Label(o.id)))))
            .ToList();

    public static string Cap(string s) => s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s;

    internal static Button Btn(string text, int w = 0) => w > 0
        ? new Button { Text = text, Width = w, Height = 30, Margin = new Padding(3) }
        : new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(3) };
}

public record ObjItem(string Id, string Text)
{
    public override string ToString() => Text;
}

// ---------------------------------------------------------------------
//  Журнал осмотра ДУ-46 («Устранение неисправности»)
// ---------------------------------------------------------------------
class Du46Window : Form
{
    readonly ComboBox cbObj = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    readonly TextBox txt = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    readonly CheckedListBox workers = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    readonly ListBox pending = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    readonly ListView journal = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
    readonly CheckBox onlyObj = new() { Text = "только по выбранному объекту", AutoSize = true };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkGreen, Margin = new Padding(8, 9, 0, 0) };

    public Du46Window()
    {
        Text = "Журнал осмотра ДУ-46 — устранение неисправности";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(1000, 720);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        // --- станция и объект ---
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        top.Controls.Add(new Label { Text = $"Станция: {DispatchLink.StationName}", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 7, 24, 0) });
        top.Controls.Add(new Label { Text = "Объект (неисправность):", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        top.Controls.Add(cbObj);
        root.Controls.Add(top, 0, 0);
        root.SetColumnSpan(top, 2);

        // --- текст записи ---
        var gText = new GroupBox { Text = "Текст записи", Dock = DockStyle.Fill };
        var tText = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        tText.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tText.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bNew = Du46Ui.Btn("Новая запись");
        var bWrite = Du46Ui.Btn("Занести в журнал");
        bNew.Click += (_, _) => { txt.Clear(); txt.Focus(); };
        bWrite.Click += (_, _) => WriteEntry();
        var rowText = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        rowText.Controls.AddRange(new Control[] { bNew, bWrite });
        tText.Controls.Add(txt, 0, 0);
        tText.Controls.Add(rowText, 0, 1);
        gText.Controls.Add(tText);
        root.Controls.Add(gText, 0, 1);

        // --- работники служб ---
        var gWork = new GroupBox { Text = "Работники служб (отметьте и нажмите «Сообщить»)", Dock = DockStyle.Fill };
        var tWork = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        tWork.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tWork.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach (var w in Du46.Workers) workers.Items.Add($"{w.code} — {w.title} ({w.name})");
        var bNotify = Du46Ui.Btn("Сообщить");
        bNotify.Click += (_, _) => NotifyChecked();
        var rowWork = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        rowWork.Controls.AddRange(new Control[] { bNotify, status });
        tWork.Controls.Add(workers, 0, 0);
        tWork.Controls.Add(rowWork, 0, 1);
        gWork.Controls.Add(tWork);
        root.Controls.Add(gWork, 1, 1);

        // --- журнал и ожидающие ---
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        Shown += (_, _) => { try { split.SplitterDistance = Math.Max(80, split.Height / 3); } catch { } };
        var gPend = new GroupBox { Text = "Ожидают ДСП: доклады на подпись, нажатия кнопок со счётчиком без записи", Dock = DockStyle.Fill };
        var tPend = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        tPend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tPend.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var pendBtns = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill };
        var bSign = Du46Ui.Btn("Подписать / записать…", 190);
        var bReject = Du46Ui.Btn("Закрыть (не подписывать)", 190);
        bSign.Click += (_, _) => Act(true);
        bReject.Click += (_, _) => Act(false);
        pending.DoubleClick += (_, _) => Act(true);
        pendBtns.Controls.AddRange(new Control[] { bSign, bReject });
        tPend.Controls.Add(pending, 0, 0);
        tPend.Controls.Add(pendBtns, 1, 0);
        gPend.Controls.Add(tPend);
        split.Panel1.Controls.Add(gPend);

        var gJ = new GroupBox { Text = "Журнал осмотра ДУ-46: записи и порядок действий", Dock = DockStyle.Fill };
        journal.Columns.Add("Время", 70);
        journal.Columns.Add("Должность", 80);
        journal.Columns.Add("Фамилия", 110);
        journal.Columns.Add("Объект", 140);
        journal.Columns.Add("Описание", 560);
        var jTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        jTop.Controls.Add(onlyObj);
        gJ.Controls.Add(journal);
        gJ.Controls.Add(jTop);
        split.Panel2.Controls.Add(gJ);
        root.Controls.Add(split, 0, 2);
        root.SetColumnSpan(split, 2);

        var bClose = Du46Ui.Btn("Закрыть", 120);
        bClose.Click += (_, _) => Close();
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        bottom.Controls.Add(bClose);
        root.Controls.Add(bottom, 0, 3);
        root.SetColumnSpan(bottom, 2);

        FillObjects();
        cbObj.SelectedIndexChanged += (_, _) => FillJournal();
        onlyObj.CheckedChanged += (_, _) => FillJournal();
        Du46.Changed += Refill;
        FormClosed += (_, _) => Du46.Changed -= Refill;
        Refill();
    }

    string Obj => cbObj.SelectedItem is ObjItem o ? o.Id : "";

    void FillObjects()
    {
        var keep = Obj;
        cbObj.BeginUpdate();
        cbObj.Items.Clear();
        cbObj.Items.Add(new ObjItem("", "(объект не указан)"));
        foreach (var o in Du46Ui.AllObjects()) cbObj.Items.Add(o);
        cbObj.EndUpdate();
        Select(keep);
    }

    public void Select(string obj)
    {
        var item = cbObj.Items.Cast<ObjItem>().FirstOrDefault(o => o.Id == obj);
        if (item == null && !string.IsNullOrEmpty(obj))
        {
            FillObjects2(obj);
            item = cbObj.Items.Cast<ObjItem>().FirstOrDefault(o => o.Id == obj);
        }
        cbObj.SelectedItem = item ?? cbObj.Items[0];
    }

    /// <summary>Объекта нет в списке (например, поезд появился позже) — перечитать.</summary>
    void FillObjects2(string obj)
    {
        cbObj.Items.Clear();
        cbObj.Items.Add(new ObjItem("", "(объект не указан)"));
        foreach (var o in Du46Ui.AllObjects()) cbObj.Items.Add(o);
    }

    void WriteEntry()
    {
        if (txt.Text.Trim().Length == 0) { Status("Введите текст записи.", true); txt.Focus(); return; }
        Du46.Write(txt.Text, Obj);
        txt.Clear();
        Status("Запись занесена в журнал.", false);
    }

    void NotifyChecked()
    {
        var codes = workers.CheckedIndices.Cast<int>().Select(i => Du46.Workers[i].code).ToList();
        if (codes.Count == 0) { Status("Отметьте, кому сообщить.", true); return; }
        foreach (var c in codes) Du46.Notify(c, Obj);
        for (int i = 0; i < workers.Items.Count; i++) workers.SetItemChecked(i, false);
        Status($"Сообщено: {string.Join(", ", codes)}.", false);
    }

    void Status(string text, bool bad)
    {
        status.ForeColor = bad ? Color.DarkRed : Color.DarkGreen;
        status.Text = text;
    }

    void Act(bool sign)
    {
        switch (pending.SelectedItem)
        {
            case PendingItem { Report: { } m }:
                Du46.Answer(m, sign);
                break;
            case PendingItem { Press: { } p }:
                if (sign) new CounterRecordForm(p).Show(this);
                else Status("Нажатие кнопки со счётчиком нужно записать в журнал.", true);
                break;
            default:
                Status("Выберите доклад или нажатие в списке.", true);
                break;
        }
    }

    record PendingItem(WorkerMessage Report, CounterPress Press)
    {
        public override string ToString() => Report != null
            ? $"{Training.Fmt(Report.Clock)}  Доклад {Report.From}: {Report.Text}"
            : $"{Training.Fmt(Press.Clock)}  Кнопка {Press.Label} (показание {Press.Value}): {Press.What} — не записано";
    }

    void Refill()
    {
        if (IsDisposed) return;
        var sel = pending.SelectedItem as PendingItem;
        pending.BeginUpdate();
        pending.Items.Clear();
        foreach (var m in Du46.Pending) pending.Items.Add(new PendingItem(m, null));
        foreach (var p in Du46.Unrecorded) pending.Items.Add(new PendingItem(null, p));
        if (sel != null)
            pending.SelectedItem = pending.Items.Cast<PendingItem>().FirstOrDefault(x => x.Report == sel.Report && x.Press == sel.Press);
        pending.EndUpdate();
        FillJournal();
    }

    void FillJournal()
    {
        journal.BeginUpdate();
        journal.Items.Clear();
        string obj = Obj;
        foreach (var r in Du46.Records.Where(r => !onlyObj.Checked || obj.Length == 0 || r.Obj == obj))
        {
            var it = new ListViewItem(new[] { Training.Fmt(r.Clock), r.Who, r.Name, r.ObjLabel, r.Text });
            if (r.Kind is "доклад" or "сообщение") it.ForeColor = Color.FromArgb(20, 60, 150);
            if (r.Kind == "не подписан") it.ForeColor = Color.DarkRed;
            journal.Items.Add(it);
        }
        if (journal.Items.Count > 0) journal.EnsureVisible(journal.Items.Count - 1);
        journal.EndUpdate();
    }
}

// ---------------------------------------------------------------------
//  Сообщение / доклад работника (как рис. 4–5 части 2)
// ---------------------------------------------------------------------
class WorkerMessageForm : Form
{
    readonly WorkerMessage msg;

    public WorkerMessageForm(WorkerMessage m)
    {
        msg = m;
        Text = DispatchLink.StationName.StartsWith("Станция") ? DispatchLink.StationName : $"Станция {DispatchLink.StationName}";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ControlBox = !m.IsReport;           // доклад закрывается только кнопкой: «Подписать» или «Закрыть»
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(480, 230);

        var g = new GroupBox { Text = (m.Who == "Машинист" ? "Радиосвязь: " : "Докладывает ") + m.From, Location = new Point(10, 8), Size = new Size(460, 168) };
        var t = new TextBox { Multiline = true, ReadOnly = true, Text = m.Text, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window };
        g.Controls.Add(t);
        Controls.Add(g);

        var bClose = new Button { Text = "Закрыть", Size = new Size(110, 32), Location = new Point(360, 186) };
        bClose.Click += (_, _) => { if (msg.IsReport) Du46.Answer(msg, false); else msg.Done = true; Close(); };
        Controls.Add(bClose);
        if (m.IsReport)
        {
            var bSign = new Button { Text = "Подписать", Size = new Size(110, 32), Location = new Point(10, 186) };
            bSign.Click += (_, _) => { Du46.Answer(msg, true); Close(); };
            Controls.Add(bSign);
            AcceptButton = bSign;
        }
        else AcceptButton = bClose;
        Shown += (_, _) => { t.SelectionLength = 0; ActiveControl = (Control)AcceptButton; };
        if (m.IsReport) System.Media.SystemSounds.Exclamation.Play();
        else System.Media.SystemSounds.Asterisk.Play();

        // доклад подписали в окне журнала — это окно больше не нужно
        Du46.Changed += CloseIfDone;
        FormClosed += (_, _) => Du46.Changed -= CloseIfDone;
    }

    void CloseIfDone()
    {
        if (msg.IsReport && msg.Done && !IsDisposed) BeginInvoke(Close);
    }
}

// ---------------------------------------------------------------------
//  Оформление записи после нажатия кнопки со счётчиком (рис. 10 части 2)
// ---------------------------------------------------------------------
class CounterRecordForm : Form
{
    readonly CounterPress press;
    readonly TextBox txt = new() { Location = new Point(14, 32), Width = 560 };
    readonly CheckBox chk = new() { Text = "по причине неисправности на объекте", AutoSize = true, Location = new Point(14, 70) };
    readonly ComboBox cb = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(290, 67), Width = 284 };

    public CounterRecordForm(CounterPress p)
    {
        press = p;
        Text = "Оформление записи в журнал ДУ-46";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(590, 170);

        Controls.Add(new Label { Text = "Текст записи", AutoSize = true, Location = new Point(12, 10) });
        txt.Text = $"Нажата кнопка {p.Label}: {p.What}.";
        Controls.Add(txt);
        Controls.Add(chk);

        // объекты, о которых уже есть записи в журнале, — первыми; дальше все остальные
        var known = Du46.Records.Where(r => r.Obj.Length > 0 && r.Kind == "запись").Select(r => r.Obj).Distinct().ToList();
        foreach (var id in known) cb.Items.Add(new ObjItem(id, Du46Ui.Cap(Faults.Label(id))));
        foreach (var o in Du46Ui.AllObjects().Where(o => !known.Contains(o.Id))) cb.Items.Add(o);
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        chk.Checked = known.Count > 0;
        cb.Enabled = chk.Checked;
        chk.CheckedChanged += (_, _) => cb.Enabled = chk.Checked;
        Controls.Add(cb);

        Controls.Add(new Label { Text = "Показание счётчика", AutoSize = true, Location = new Point(12, 118) });
        Controls.Add(new TextBox { Text = p.Value.ToString("0000"), ReadOnly = true, Location = new Point(150, 115), Width = 70 });
        var bOk = new Button { Text = "Занести в журнал", Size = new Size(150, 32), Location = new Point(424, 112) };
        bOk.Click += (_, _) =>
        {
            if (txt.Text.Trim().Length == 0) { txt.Focus(); return; }
            Du46.RecordPress(press, txt.Text, chk.Checked && cb.SelectedItem is ObjItem o ? o.Id : "");
            Close();
        };
        var bLater = new Button { Text = "Позже", Size = new Size(90, 32), Location = new Point(328, 112) };
        bLater.Click += (_, _) => Close();
        Controls.Add(bOk);
        Controls.Add(bLater);
        AcceptButton = bOk;
        if (p.Recorded) bOk.Enabled = false;
    }
}
