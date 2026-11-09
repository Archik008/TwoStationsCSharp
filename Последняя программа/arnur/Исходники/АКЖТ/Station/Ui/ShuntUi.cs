using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  ОКНА МАНЕВРОВОЙ РАБОТЫ, ПНП И ВОССТАНОВИТЕЛЬНОГО ПОЕЗДА  [ДОБАВЛЕНО 23.10.2026, этап 7]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Логика — Core/Shunting.cs, Core/Recovery.cs.
//   • «Маневровая работа» (рис. 17–21 части 3): вагоны состава, башмак, закреплён / не закреплён, осмотр,
//     расцеп / сцеп, формирование; «Натурный лист» — то же окно без кнопок;
//   • ПНП: «Положение на ПНП» (фронты), «Задание на маневровую работу» (к подаче / к уборке), журнал сообщений;
//   • «Вызов восстановительного поезда к поезду №…».
// =====================================================================

public static class ShuntUi
{
    static readonly Dictionary<string, Form> open = new();

    static void Show(Control owner, string key, Func<Form> make)
    {
        if (!open.TryGetValue(key, out var f) || f.IsDisposed)
        {
            open[key] = f = make();
            f.Show(owner.FindForm());
        }
        if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
        f.BringToFront();
        f.Activate();
    }

    public static void OpenShunt(VirtualTrain t, Control owner, bool readOnly = false) =>
        Show(owner, $"shunt{t.Number}{readOnly}", () => new ShuntForm(t, readOnly));

    public static void OpenPnp(Pnp p, Control owner) => Show(owner, "pnp" + p.Name, () => new PnpWindow(p));
    public static void OpenPnpLog(Pnp p, Control owner) => Show(owner, "pnplog" + p.Name, () => new PnpLogWindow(p));

    public static void OpenPnpTask(Pnp p, Control owner)
    {
        if (p.Loco == null) { MessageBox.Show(owner.FindForm(), $"На ПНП «{p.Name}» нет локомотива. Подайте маневровый состав в тупик ПНП.", "ПНП"); return; }
        using var f = new PnpTaskForm(p);
        f.ShowDialog(owner.FindForm());
    }

    public static void AskRecovery(VirtualTrain victim, Control owner)
    {
        using var f = new RecoveryForm(victim);
        f.ShowDialog(owner.FindForm());
    }

    /// <summary>Меню ПНП (кнопка на пульте, ПКМ по тупику ПНП).</summary>
    public static void AddPnpItems(ToolStripItemCollection items, Pnp p, Control owner)
    {
        items.Add(p.Notified ? "Уведомление отправлено" : "Уведомление о маневровой работе", null, (_, _) =>
        {
            if (p.Notified) return;
            if (MessageBox.Show(owner.FindForm(), "Уведомить ПНП о предстоящей маневровой работе на их территории?", $"ПНП «{p.Name}»",
                                MessageBoxButtons.YesNo) == DialogResult.Yes) Shunting.Notify(p);
        });
        items.Add("Положение на ПНП…", null, (_, _) => OpenPnp(p, owner));
        items.Add(new ToolStripMenuItem("Маневровая работа на ПНП…", null, (_, _) => OpenPnpTask(p, owner)) { Enabled = p.Loco != null });
        items.Add("Журнал сообщений ПНП…", null, (_, _) => OpenPnpLog(p, owner));
    }

    /// <summary>ПКМ по отцепленным вагонам.</summary>
    public static ContextMenuStrip WagonGroupMenu(VirtualTrain g, Control owner)
    {
        var m = new ContextMenuStrip();
        int have = g.Consist.Count(w => w.Shoe), need = Shunting.ShoesNeeded(g.Consist.Count);
        m.Items.Add(new ToolStripMenuItem($"Вагоны без локомотива на {g.Head?.Label}: {g.Consist.Count}, башмаков {have} из {need}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Натурный лист, тормозные башмаки…", null, (_, _) => OpenShunt(g, owner));
        return m;
    }

    /// <summary>Надпись ПНП у тупика и две лампы: локомотив на ПНП (белая — стоит, синяя — работа), вагоны к уборке (жёлтая).</summary>
    public static void DrawPnp(Graphics g, IEnumerable<(string id, PointF[] pts)> pieces, Font font)
    {
        foreach (var p in Shunting.Pnps)
        {
            var pts = pieces.FirstOrDefault(x => x.id == p.Section.Id).pts;
            if (pts == null || pts.Length < 2) continue;
            var m = new PointF((pts[0].X + pts[^1].X) / 2, (pts[0].Y + pts[^1].Y) / 2);
            string text = L.T($"ПНП «{p.Name}»");
            var size = g.MeasureString(text, font);
            var r = new RectangleF(m.X - size.Width / 2 - 6, m.Y + 16, size.Width + 12, size.Height + 4);
            using var pen = new Pen(Color.FromArgb(60, 60, 60), 1.5f);
            using var back = new SolidBrush(Color.FromArgb(245, 245, 235));
            using var tb = new SolidBrush(Color.FromArgb(40, 40, 40));
            g.FillRectangle(back, r);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            g.DrawString(text, font, tb, r.X + 6, r.Y + 2);
            var loco = p.Loco == null ? Color.FromArgb(90, 90, 90) : p.Working ? Color.FromArgb(60, 110, 255) : Color.White;
            var ready = p.HasReady ? Color.FromArgb(255, 210, 0) : Color.FromArgb(90, 90, 90);
            foreach (var (c, dx) in new[] { (loco, -10f), (ready, 10f) })
            {
                using var b = new SolidBrush(c);
                g.FillEllipse(b, r.X + r.Width / 2 + dx - 6, r.Y - 15, 12, 12);
                g.DrawEllipse(pen, r.X + r.Width / 2 + dx - 6, r.Y - 15, 12, 12);
            }
        }
    }

    internal static Button Btn(string text, int w = 0) => Du46Ui.Btn(text, w);
}

// ---------------------------------------------------------------------
//  Маневровая работа / натурный лист (рис. 16–21 части 3)
// ---------------------------------------------------------------------
class ShuntForm : Form
{
    readonly VirtualTrain train;
    readonly bool readOnly;
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
    readonly Label info = new() { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 4, 8, 0) };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkGreen, Margin = new Padding(8, 9, 0, 0) };
    readonly List<Button> actions = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 700 };
    string last = "";

    public ShuntForm(VirtualTrain t, bool ro)
    {
        train = t;
        readOnly = ro;
        Text = ro ? $"Натурный лист: поезд №{t.Number}" : t.NoLoco ? $"Вагоны без локомотива на {t.Head?.Label}" : $"Маневровые работы для подвижной единицы №{t.Number}";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(780, 600);
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        foreach (var (c, w) in new[] { ("№", 40), ("Номер вагона", 110), ("Род", 100), ("Груз", 90), ("Назначение", 220), ("Башмак", 70) })
            list.Columns.Add(c, w);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        if (!ro)
        {
            Add(bar, "Башмак", () =>
            {
                var sel = Selected();
                if (sel.Count == 0) return "выберите вагон(ы)";
                foreach (var w in sel) Shunting.ToggleShoe(train, w);
                return null;
            });
            Add(bar, "Закреплён", () => Ok(Shunting.Report(train, true)));
            Add(bar, "Не закреплён", () => Ok(Shunting.Report(train, false)));
            if (!t.NoLoco)
            {
                Add(bar, "Осмотр", () => { Shunting.Inspect(train); return Ok("Состав предъявлен к осмотру — ждите сообщения осмотрщика (ПТО)"); });
                Add(bar, "Расцеп (от выбранного)", () =>
                {
                    int i = list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] : -1;
                    if (i >= 0 && MessageBox.Show(this, $"Отцепить вагоны с {i + 1}-го по {train.Consist.Count}-й ({train.Consist.Count - i} ваг.)? Они останутся на {train.Cells[^1].Label}.",
                                                  Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return "";
                    return Shunting.Uncouple(train, i);
                });
                Add(bar, "Сцеп", () => Shunting.Couple(train));
                Add(bar, "Формирование…", () =>
                {
                    using (var f = new TrainInfoForm(train)) if (f.ShowDialog(this) != DialogResult.OK) return "";
                    return Shunting.Form(train) ?? Ok($"Сформирован поезд №{train.Number}. Предъявите его к осмотру («Осмотр»).");
                });
            }
        }
        bar.Controls.Add(status);
        Controls.Add(list);
        Controls.Add(bar);
        Controls.Add(info);
        timer.Tick += (_, _) => Refill();
        timer.Start();
        FormClosed += (_, _) => timer.Dispose();
        Refill();
    }

    string Ok(string text) { status.ForeColor = Color.DarkGreen; status.Text = text; return null; }

    void Add(FlowLayoutPanel bar, string text, Func<string> act)
    {
        var b = ShuntUi.Btn(text);
        b.Click += (_, _) =>
        {
            var err = act();
            if (err == null) { Refill(true); return; }
            if (err.Length == 0) return;
            status.ForeColor = Color.DarkRed;
            status.Text = err;
        };
        actions.Add(b);
        bar.Controls.Add(b);
    }

    List<Wagon> Selected() => list.SelectedItems.Cast<ListViewItem>().Select(i => (Wagon)i.Tag).ToList();

    void Refill(bool force = false)
    {
        if (IsDisposed) return;
        bool gone = !ModelHost.Model.Trains.Trains.Contains(train);
        int have = train.Consist.Count(w => w.Shoe), need = Shunting.ShoesNeeded(train.Consist.Count);
        // каждый кусок — свой шаблон перевода (склеенная строка переводилась бы плохо)
        string who = train.NoLoco ? L.F("Вагоны без локомотива на {0}", train.Head?.Label)
                   : L.F(train.Shunting ? "Маневровый №{0} на {1}" : "Поезд №{0} на {1}", train.Number, train.Head?.Label);
        info.Text = gone ? L.T("Подвижной единицы больше нет на станции (уехала или сцеплена с локомотивом).")
            : who + (train.Waiting.Length > 0 ? " — " + L.T(train.Waiting) : "") + "\n" + Shunting.Summary(train.Consist, true) + ". " +
              L.F("Тормозных башмаков: {0}, норма {1} (1 на {2} ваг.)", have, need, Shunting.NormWagonsPerShoe) +
              (train.Inspected ? L.T(". Осмотрен ПТО") : train.InspectAsked ? L.T(". Предъявлен к осмотру") : "");
        foreach (var b in actions) b.Enabled = !gone;
        string sig = string.Join(",", train.Consist.Select(w => w.Number + w.Shoe + w.Done)) + info.Text;
        if (!force && sig == last) return;
        last = sig;
        var sel = Selected();
        list.BeginUpdate();
        list.Items.Clear();
        int i = 0;
        foreach (var w in train.Consist)
        {
            var it = new ListViewItem(new[] { (++i).ToString(), w.Number, Shunting.KindTitle(w.Kind), w.Loaded ? "гружёный" : "порожний", w.State, w.Shoe ? "Б" : "" }) { Tag = w };
            if (w.Shoe) it.ForeColor = Color.DarkRed;
            else if (w.Dest.Length > 0 && !w.Done) it.ForeColor = Color.FromArgb(20, 60, 150);
            else if (w.Done) it.ForeColor = Color.DarkGreen;
            it.Selected = sel.Contains(w);
            list.Items.Add(it);
        }
        list.EndUpdate();
    }
}

// ---------------------------------------------------------------------
//  Положение на ПНП (рис. 4 и 11 части 3)
// ---------------------------------------------------------------------
class PnpWindow : Form
{
    readonly Pnp pnp;
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    readonly Label head = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };

    public PnpWindow(Pnp p)
    {
        pnp = p;
        Text = $"Положение на ПНП «{p.Name}» ст. {DispatchLink.StationName}";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(820, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        foreach (var (c, w) in new[] { ("Фронт", 110), ("Вместимость", 90), ("Выгрузка / погрузка", 230), ("Окончание операций", 130), ("К уборке", 220) })
            list.Columns.Add(c, w);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        var chk = new CheckBox { Text = "Сообщать по готовности вагонов к уборке", Checked = p.ReportReady, AutoSize = true, Margin = new Padding(3, 8, 12, 3) };
        chk.CheckedChanged += (_, _) => p.ReportReady = chk.Checked;
        var bNotice = ShuntUi.Btn("Уведомление");
        bNotice.Click += (_, _) => { if (!p.Notified) Shunting.Notify(p); };
        var bWork = ShuntUi.Btn("Маневровая работа…");
        bWork.Click += (_, _) => ShuntUi.OpenPnpTask(p, this);
        var bLog = ShuntUi.Btn("Журнал");
        bLog.Click += (_, _) => ShuntUi.OpenPnpLog(p, this);
        var bClose = ShuntUi.Btn("Закрыть");
        bClose.Click += (_, _) => Close();
        bottom.Controls.AddRange(new Control[] { chk, bNotice, bWork, bLog, bClose });
        Controls.Add(list);
        Controls.Add(head);
        Controls.Add(bottom);
        timer.Tick += (_, _) => { Refill(); bWork.Enabled = p.Loco != null; bNotice.Enabled = !p.Notified; };
        timer.Start();
        FormClosed += (_, _) => timer.Dispose();
        Refill();
        bWork.Enabled = p.Loco != null;
        bNotice.Enabled = !p.Notified;
    }

    void Refill()
    {
        if (IsDisposed) return;
        var p = pnp;
        head.Text = $"Локомотив на ПНП: {(p.Loco == null ? "нет" : $"№{p.Loco.Number}" + (p.Working ? $", маневровая работа до {Training.Fmt(p.WorkUntil)[..5]}" : ""))}" +
                    $"    Уведомление: {(p.Notified ? "отправлено" : "нет")}    Подано {p.Supplied}, убрано {p.Removed}";
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var f in p.Fronts)
        {
            var under = f.Wagons.Where(w => !w.Done).ToList();
            var ready = f.Ready.ToList();
            list.Items.Add(new ListViewItem(new[]
            {
                f.Title, $"{f.Wagons.Count} из {PnpFront.Capacity}",
                under.Count == 0 ? "—" : $"{under.Count} ваг.: {string.Join(", ", under.Select(w => w.Loaded ? "выгрузка" : "погрузка").Distinct())}",
                f.Busy ? Training.Fmt(f.OpUntil)[..5] : "—",
                ready.Count == 0 ? "—" : Shunting.Summary(ready, true),
            }) { ForeColor = ready.Count > 0 ? Color.DarkGoldenrod : under.Count > 0 ? Color.FromArgb(20, 60, 150) : Color.Black });
        }
        list.EndUpdate();
    }
}

// ---------------------------------------------------------------------
//  Задание на маневровую работу на ПНП (рис. 8 части 3)
// ---------------------------------------------------------------------
class PnpTaskForm : Form
{
    readonly CheckedListBox supply = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    readonly CheckedListBox remove = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    readonly Label total = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    record Item(Wagon W, string Text) { public override string ToString() => Text; }

    public PnpTaskForm(Pnp p)
    {
        Text = $"Задание на маневровую работу на ПНП «{p.Name}» — локомотив №{p.Loco.Number}";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(860, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(6) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var gS = new GroupBox { Text = "К подаче (местные вагоны в составе)", Dock = DockStyle.Fill };
        gS.Controls.Add(supply);
        var gR = new GroupBox { Text = "К уборке (вагоны на грузовых фронтах)", Dock = DockStyle.Fill };
        gR.Controls.Add(remove);
        root.Controls.Add(gS, 0, 0);
        root.Controls.Add(gR, 1, 0);
        root.Controls.Add(total, 0, 1);
        root.SetColumnSpan(total, 2);

        foreach (var w in Shunting.CanSupply(p))
            supply.Items.Add(new Item(w, $"{w.Number}  {Shunting.KindTitle(w.Kind)}, {(w.Loaded ? "гружёный — выгрузка" : "порожний — погрузка")}"), true);
        foreach (var w in Shunting.CanRemove(p))
            remove.Items.Add(new Item(w, $"{w.Number}  {Shunting.KindTitle(w.Kind)}, {(w.Done ? "операция закончена" : "под операцией")}"), w.Done);
        supply.ItemCheck += (_, _) => BeginInvoke(Sum);
        remove.ItemCheck += (_, _) => BeginInvoke(Sum);
        Sum();

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var bGo = ShuntUi.Btn("Выполнить маневровую работу");
        bGo.Enabled = !p.Working;
        bGo.Click += (_, _) =>
        {
            var err = Shunting.StartWork(p, Checked(supply), Checked(remove));
            if (err != null) { MessageBox.Show(this, "Нельзя: " + err, Text); return; }
            Close();
        };
        var bList = ShuntUi.Btn("Показать натурный лист");
        bList.Click += (_, _) => ShuntUi.OpenShunt(p.Loco, this, true);
        var bPos = ShuntUi.Btn("Показать положение");
        bPos.Click += (_, _) => ShuntUi.OpenPnp(p, this);
        var bWait = ShuntUi.Btn("Ожидать дальнейших указаний");
        bWait.Click += (_, _) => Close();
        bottom.Controls.AddRange(new Control[] { bList, bPos, bGo, bWait });
        if (p.Working) bottom.Controls.Add(new Label { Text = $"Идёт маневровая работа, окончание в {Training.Fmt(p.WorkUntil)[..5]}", AutoSize = true, Margin = new Padding(8, 9, 0, 0) });
        root.Controls.Add(bottom, 0, 2);
        root.SetColumnSpan(bottom, 2);
    }

    static List<Wagon> Checked(CheckedListBox b) => b.CheckedItems.Cast<Item>().Select(i => i.W).ToList();

    void Sum()
    {
        var s = Checked(supply);
        var r = Checked(remove);
        int fronts = s.Concat(r).Select(w => w.Kind).Distinct().Count();
        total.Text = L.F("К подаче: {0}.   К уборке: {1}.   Время маневровой работы ≈ {2} мин",
                         Shunting.Summary(s, true), Shunting.Summary(r, true), fronts * 3 + s.Count + r.Count);
    }
}

// ---------------------------------------------------------------------
//  Журнал сообщений ПНП (рис. 13 части 3)
// ---------------------------------------------------------------------
class PnpLogWindow : Form
{
    public PnpLogWindow(Pnp p)
    {
        Text = $"Журнал сообщений ПНП «{p.Name}» ст. {DispatchLink.StationName}";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(860, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        foreach (var (c, w) in new[] { ("№", 40), ("Время", 60), ("Сообщение", 400), ("Вагоны", 300) }) list.Columns.Add(c, w);
        var sum = new Label { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(8, 6, 0, 0) };
        Controls.Add(list);
        Controls.Add(sum);
        void Fill()
        {
            if (IsDisposed) return;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var r in p.Log) list.Items.Add(new ListViewItem(r));
            list.EndUpdate();
            sum.Text = $"Вагонов: прошли грузовые операции {p.Processed}, подано {p.Supplied}, убрано {p.Removed}";
        }
        Shunting.Changed += Fill;
        FormClosed += (_, _) => Shunting.Changed -= Fill;
        Fill();
    }
}

// ---------------------------------------------------------------------
//  Вызов восстановительного поезда (рис. 22 части 2)
// ---------------------------------------------------------------------
class RecoveryForm : Form
{
    public RecoveryForm(VirtualTrain victim)
    {
        Text = $"Вызов восстановительного поезда к поезду №{victim.Number}";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(760, 300);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        foreach (var (c, w) in new[] { ("Номер поезда", 100), ("К поезду", 80), ("Место", 90), ("Вызван", 70), ("Подход", 70), ("Положение", 260) }) list.Columns.Add(c, w);
        foreach (var r in Recovery.ReportRows()) list.Items.Add(new ListViewItem(r));
        if (list.Items.Count == 0)
            list.Items.Add(new ListViewItem(new[] { "8002", $"№{victim.Number}", victim.Head?.Label ?? "", "—", $"через {Recovery.ArriveMin:0} мин", "за пределами участка" }));
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        var bCall = ShuntUi.Btn("Вызов");
        bCall.Click += (_, _) =>
        {
            if (MessageBox.Show(this, $"Вызвать восстановительный поезд к поезду №{victim.Number}?", "Подтверждение", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var err = Recovery.Call(victim);
            if (err != null) MessageBox.Show(this, "Нельзя: " + err, Text);
            else Close();
        };
        var bNo = ShuntUi.Btn("Отказ");
        bNo.Click += (_, _) => Close();
        bottom.Controls.AddRange(new Control[] { bCall, bNo });
        Controls.Add(list);
        Controls.Add(bottom);
    }
}
