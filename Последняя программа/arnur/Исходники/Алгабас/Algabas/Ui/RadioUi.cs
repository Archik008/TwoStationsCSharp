using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  ОКНА ПОЕЗДА: НОМЕР И ИНДЕКС, РАДИОСВЯЗЬ, ПРЕДУПРЕЖДЕНИЯ И «ОКНА»  [ДОБАВЛЕНО 23.10.2026, этап 4]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Логика — Core/Radio.cs.
//   • ПКМ по поезду: «Номер и индекс…», «Вызвать по радиосвязи…», «Предупреждение на поезд…», «Вручить бланк…»;
//   • ПКМ по участку: «Предупреждение / окно…», отмена действующих, номер поезда на макете и радиосвязь с ним.
// =====================================================================

public static class RadioUi
{
    static readonly Dictionary<int, RadioForm> radios = new();

    public static void AddTrainItems(ContextMenuStrip m, VirtualTrain t, Control owner)
    {
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Номер и индекс…", null, (_, _) => { using var f = new TrainInfoForm(t); f.ShowDialog(owner.FindForm()); });
        m.Items.Add("Вызвать по радиосвязи…", null, (_, _) => OpenRadio(t, t.Number, owner));
        m.Items.Add("Предупреждение на поезд…", null, (_, _) => AskTrainWarning(t, t.Number, owner));
        m.Items.Add("Вручить бланк…", null, (_, _) => JournalsUi.OpenBlanks(owner, t.Number));
        // [ДОБАВЛЕНО 23.10.2026, этап 7] маневровая работа, натурный лист, восстановительный поезд
        m.Items.Add(t.Shunting ? "Маневровая работа…" : "Расформировать, маневровая работа…", null, (_, _) => Shunt(t, owner));
        if (t.Consist.Count > 0) m.Items.Add("Натурный лист…", null, (_, _) => ShuntUi.OpenShunt(t, owner, true));
        if (t.Derailed) m.Items.Add("Вызвать восстановительный поезд…", null, (_, _) => ShuntUi.AskRecovery(t, owner));
    }

    /// <summary>Маневровая работа с поездом: поезд сначала расформировывают (становится маневровым составом).</summary>
    public static void Shunt(VirtualTrain t, Control owner)
    {
        if (!t.Shunting)
        {
            if (MessageBox.Show(owner.FindForm(), $"Расформировать поезд №{t.Number}? Он станет маневровым составом (локомотив и вагоны).",
                                "Расформирование", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Shunting.Disband(t);
        }
        ShuntUi.OpenShunt(t, owner);
    }

    public static void AddSectionItems(ContextMenuStrip m, TrackSection sec, Control owner)
    {
        m.Items.Add(new ToolStripSeparator());
        var line = Radio.LineOf(sec);
        m.Items.Add("Предупреждение / окно…", null, (_, _) => { using var f = new RestrictionForm(sec, line); f.ShowDialog(owner.FindForm()); });
        var active = Radio.ActiveOn("P:" + sec.Id).Concat(line != null ? Radio.ActiveOn("L:" + line.Id) : Enumerable.Empty<Restriction>()).ToList();
        foreach (var r in active)
        {
            var rr = r;
            m.Items.Add($"Отменить: {r.Text}", null, (_, _) => Radio.Cancel(rr));
        }
        if (Shunting.PnpOf(sec) is { } pnp)   // [ДОБАВЛЕНО 23.10.2026, этап 7] тупик ПНП
        {
            var sub = new ToolStripMenuItem($"ПНП «{pnp.Name}»");
            ShuntUi.AddPnpItems(sub.DropDownItems, pnp, owner);
            m.Items.Add(sub);
        }
        if (sec.HwOcc && sec.TrainCount == 0)
        {
            m.Items.Add(sec.HwTrainNo > 0 ? $"Поезд на макете №{sec.HwTrainNo}: изменить номер…" : "Номер поезда на макете…", null, (_, _) => AskHwNumber(sec, owner));
            if (sec.HwTrainNo > 0)
            {
                int n = sec.HwTrainNo;
                m.Items.Add($"Вызвать по радиосвязи поезд №{n} (макет)…", null, (_, _) => OpenRadio(null, n, owner));
            }
        }
    }

    /// <summary>Добавка к подсказке об участке.</summary>
    public static string SectionHint(TrackSection s) =>
        (s.Window ? ", «ОКНО» — путь закрыт для работ" : "") +
        (s.Warning ? $", предупреждение: пасс. {s.WarnPass}, груз. {s.WarnFreight} км/ч" : "") +
        (s.HwTrainNo > 0 ? $", поезд на макете №{s.HwTrainNo}" : "");

    /// <summary>На схеме: предупреждение — оранжевый пунктир под участком, «окно» — красный крест.</summary>
    public static void DrawRestrictions(Graphics g, IEnumerable<(string id, PointF[] pts)> pieces, Interlocking il)
    {
        using var warn = new Pen(Color.FromArgb(255, 160, 0), 3f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var win = new Pen(Color.FromArgb(230, 30, 30), 3.5f);
        foreach (var (id, pts) in pieces)
        {
            if (pts.Length < 2 || !il.Sections.TryGetValue(id, out var s) || (!s.Window && !s.Warning)) continue;
            if (s.Warning) g.DrawLines(warn, pts.Select(p => new PointF(p.X, p.Y + 10)).ToArray());
            if (s.Window)
            {
                var m = new PointF((pts[0].X + pts[^1].X) / 2, (pts[0].Y + pts[^1].Y) / 2);
                g.DrawLine(win, m.X - 12, m.Y - 12, m.X + 12, m.Y + 12);
                g.DrawLine(win, m.X - 12, m.Y + 12, m.X + 12, m.Y - 12);
            }
        }
    }

    /// <summary>Добавка к подсказке о поезде: категория, индекс, отметки.</summary>
    public static string TrainHint(VirtualTrain t) => $", {t.Category}{(t.Index.Length > 0 ? ", индекс " + t.Index : "")}{Radio.Marks(t)}";

    public static void OpenRadio(VirtualTrain t, int number, Control owner)
    {
        if (!radios.TryGetValue(number, out var f) || f.IsDisposed)
        {
            radios[number] = f = new RadioForm(t, number);
            f.Show(owner.FindForm());
        }
        f.BringToFront();
        f.Activate();
    }

    public static void AskTrainWarning(VirtualTrain t, int number, Control owner)
    {
        using var f = new TrainWarningForm(t, number);
        f.ShowDialog(owner.FindForm());
    }

    static void AskHwNumber(TrackSection sec, Control owner)
    {
        var s = Ask(owner.FindForm(), "Поезд на макете", $"Номер настоящего поезда на участке {sec.Label} (0 — снять):",
                    sec.HwTrainNo > 0 ? sec.HwTrainNo.ToString() : "");
        if (s == null) return;
        if (!int.TryParse(s.Trim(), out var n) || n < 0) { MessageBox.Show(owner.FindForm(), "Номер поезда — целое число.", "Поезд на макете"); return; }
        Radio.SetHwNumber(sec, n);
    }

    /// <summary>Простой ввод строки (null — отказ).</summary>
    public static string Ask(IWin32Window owner, string title, string prompt, string value)
    {
        using var f = new Form
        {
            Text = title, Font = new Font("Segoe UI", 10f), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, ClientSize = new Size(420, 120),
        };
        var lbl = new Label { Text = prompt, AutoSize = false, Location = new Point(12, 10), Size = new Size(396, 40) };
        var txt = new TextBox { Text = value, Location = new Point(12, 52), Width = 396 };
        var ok = new Button { Text = "Принять", DialogResult = DialogResult.OK, Location = new Point(222, 84), Size = new Size(90, 30) };
        var no = new Button { Text = "Отказ", DialogResult = DialogResult.Cancel, Location = new Point(318, 84), Size = new Size(90, 30) };
        f.Controls.AddRange(new Control[] { lbl, txt, ok, no });
        f.AcceptButton = ok;
        f.CancelButton = no;
        return f.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
    }

    internal static Button Btn(string text, int w = 170) => new() { Text = text, Width = w, Height = 30, Margin = new Padding(3) };

    internal static VirtualTrain Train(int number) => ModelHost.Model.Trains.Find(number);
}

// ---------------------------------------------------------------------
//  Номер и индекс поезда (рис. 13 части 1)
// ---------------------------------------------------------------------
class TrainInfoForm : Form
{
    public TrainInfoForm(VirtualTrain t)
    {
        Text = $"Поезд №{t.Number}" + (t.Index.Length > 0 ? $" {t.Index}" : "");
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 300);

        var parts = (t.Index + "--").Split('-');
        var num = new TextBox { Text = t.Number.ToString(), Location = new Point(20, 34), Width = 80 };
        var i1 = new TextBox { Text = parts[0], Location = new Point(140, 34), Width = 70, MaxLength = 4 };
        var i2 = new TextBox { Text = parts[1], Location = new Point(230, 34), Width = 50, MaxLength = 3 };
        var i3 = new TextBox { Text = parts[2], Location = new Point(300, 34), Width = 70, MaxLength = 4 };
        Controls.Add(new Label { Text = "Номер", AutoSize = true, Location = new Point(18, 10) });
        Controls.Add(new Label { Text = "Индекс (ст. формирования — № состава — ст. назначения)", AutoSize = true, Location = new Point(138, 10) });
        Controls.Add(new Label { Text = "—", AutoSize = true, Location = new Point(212, 37) });
        Controls.Add(new Label { Text = "—", AutoSize = true, Location = new Point(282, 37) });
        Controls.AddRange(new Control[] { num, i1, i2, i3 });

        Controls.Add(new Label { Text = "Категория", AutoSize = true, Location = new Point(18, 76) });
        var cat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(140, 72), Width = 230 };
        cat.Items.AddRange(Radio.Categories);
        cat.SelectedItem = Radio.Categories.Contains(t.Category) ? t.Category : "грузовой";
        Controls.Add(cat);

        var g = new GroupBox { Text = "Особые отметки", Location = new Point(18, 108), Size = new Size(352, 140) };
        var lng = new CheckBox { Text = "Длинносоставный", Checked = t.Long, AutoSize = true, Location = new Point(12, 24) };
        var heavy = new CheckBox { Text = "Тяжеловесный", Checked = t.Heavy, AutoSize = true, Location = new Point(12, 52) };
        var over = new CheckBox { Text = "Негабаритный", Checked = t.Oversize, AutoSize = true, Location = new Point(12, 80) };
        var wag = new NumericUpDown { Minimum = 0, Maximum = 200, Value = Math.Clamp(t.Wagons, 0, 200), Location = new Point(200, 106), Width = 70 };
        g.Controls.AddRange(new Control[] { lng, heavy, over, new Label { Text = "Вагонов в составе", AutoSize = true, Location = new Point(12, 108) }, wag });
        Controls.Add(g);

        var ok = new Button { Text = "Принять", Location = new Point(380, 120), Size = new Size(80, 32) };
        var no = new Button { Text = "Отказ", DialogResult = DialogResult.Cancel, Location = new Point(380, 160), Size = new Size(80, 32) };
        Controls.AddRange(new Control[] { ok, no });
        AcceptButton = ok;
        CancelButton = no;
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(num.Text.Trim(), out var n)) { MessageBox.Show(this, "Номер поезда — целое число.", Text); return; }
            if (n != t.Number && Radio.WrongParity(n, t.Dir) &&
                MessageBox.Show(this, $"Поезд едет в {(t.Dir == Dir.Right ? "чётном" : "нечётном")} направлении, а номер {n} — {(n % 2 == 0 ? "чётный" : "нечётный")}.\n" +
                                      "Чётные номера — чётному направлению, нечётные — нечётному. Всё равно присвоить?",
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string idx = string.Join("-", new[] { i1.Text, i2.Text, i3.Text }.Select(x => x.Trim())).Trim('-');
            var err = Radio.SetInfo(t, n, idx, cat.SelectedItem?.ToString() ?? "грузовой", lng.Checked, heavy.Checked, over.Checked, (int)wag.Value);
            if (err != null) { MessageBox.Show(this, "Не принято: " + err, Text); return; }
            DialogResult = DialogResult.OK;
        };
    }
}

// ---------------------------------------------------------------------
//  Поездная радиосвязь «Поезд №…» (рис. 8 части 2)
// ---------------------------------------------------------------------
class RadioForm : Form
{
    readonly int number;
    VirtualTrain train;
    readonly Label lNum = new() { AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Location = new Point(10, 22) };
    readonly Label lSpeed = new() { AutoSize = true, Location = new Point(10, 24) };
    readonly Label lIndex = new() { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label lWhere = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    readonly ListBox talk = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    readonly Button bStop, bGo;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    int talkCount = -1;

    /// <summary>[для этапа 7] Панель кнопок управления — сюда добавляются команды маневровой работы.</summary>
    public readonly FlowLayoutPanel Commands = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };

    public RadioForm(VirtualTrain t, int n)
    {
        train = t;
        hw = t == null;
        number = n;
        Text = $"Поезд №{n}" + (t == null ? " (макет)" : "");
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Size = new Size(430, 720);
        MinimumSize = new Size(400, 560);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(6) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var gNum = new GroupBox { Text = "Номер", Dock = DockStyle.Fill };
        var bEdit = new Button { Text = "Изменить", Location = new Point(110, 18), Size = new Size(110, 30), Enabled = t != null };
        bEdit.Click += (_, _) => { if (train != null) { using var f = new TrainInfoForm(train); f.ShowDialog(this); } };
        gNum.Controls.AddRange(new Control[] { lNum, bEdit });
        root.Controls.Add(gNum, 0, 0);
        var gSpeed = new GroupBox { Text = "Скорость", Dock = DockStyle.Fill };
        gSpeed.Controls.Add(lSpeed);
        root.Controls.Add(gSpeed, 1, 0);
        var gIdx = new GroupBox { Text = "Индекс", Dock = DockStyle.Fill };
        gIdx.Controls.Add(lIndex);
        root.Controls.Add(gIdx, 0, 1);
        root.SetColumnSpan(gIdx, 2);

        var gCmd = new GroupBox { Text = "Управление", Dock = DockStyle.Fill };
        bStop = RadioUi.Btn("Остановка");
        bGo = RadioUi.Btn("Продолжить движение");
        var bOrder = RadioUi.Btn("Указание…");
        var bBack = RadioUi.Btn("Осаживание");
        var bRepair = RadioUi.Btn("Ремонт");
        var bShort = RadioUi.Btn("Сократить стоянку");
        var bWarn = RadioUi.Btn("Предупреждение…");
        var bWhere = RadioUi.Btn("Где находитесь?");
        var bBlank = RadioUi.Btn("Вручить бланк…");
        bStop.Click += (_, _) => Radio.Stop(T, number);
        bGo.Click += (_, _) => Radio.Go(T, number);
        bOrder.Click += (_, _) => AskOrder();
        bBack.Click += (_, _) => Radio.BackUp(T, number);
        bRepair.Click += (_, _) => Radio.Repair(T, number);
        bShort.Click += (_, _) => Radio.Shorten(T, number);
        bWarn.Click += (_, _) => RadioUi.AskTrainWarning(T, number, this);
        bWhere.Click += (_, _) => Radio.AskWhere(T, number);
        bBlank.Click += (_, _) => JournalsUi.OpenBlanks(this, number);
        Commands.Controls.AddRange(new Control[] { bStop, bGo, bOrder, bBack, bRepair, bShort, bWarn, bWhere, bBlank });
        if (t != null)   // [ДОБАВЛЕНО 23.10.2026, этап 7] маневровая работа (рис. 15–16 части 3), восстановительный поезд
        {
            var bShunt = RadioUi.Btn("Маневровая работа…");
            var bList = RadioUi.Btn("Натурный лист…");
            var bRec = RadioUi.Btn("Восст. поезд…");
            bShunt.Click += (_, _) => { if (T != null) RadioUi.Shunt(T, this); };
            bList.Click += (_, _) => { if (T != null) ShuntUi.OpenShunt(T, this, true); };
            bRec.Click += (_, _) => { if (T != null) ShuntUi.AskRecovery(T, this); };
            Commands.Controls.AddRange(new Control[] { bShunt, bList, bRec });
        }
        gCmd.Controls.Add(Commands);
        root.Controls.Add(gCmd, 0, 2);
        root.SetColumnSpan(gCmd, 2);

        var gWhere = new GroupBox { Text = "Поезд находится", Dock = DockStyle.Fill };
        gWhere.Controls.Add(lWhere);
        root.Controls.Add(gWhere, 0, 3);
        root.SetColumnSpan(gWhere, 2);

        var gTalk = new GroupBox { Text = "Переговоры", Dock = DockStyle.Fill };
        gTalk.Controls.Add(talk);
        root.Controls.Add(gTalk, 0, 4);
        root.SetColumnSpan(gTalk, 2);

        timer.Tick += (_, _) => Refill();
        timer.Start();
        FormClosed += (_, _) => timer.Dispose();
        Refill();
    }

    /// <summary>Поезд на макете (номер по датчикам) — его ведут руками, команды только записываются.</summary>
    bool Hw => hw;
    bool hw;

    /// <summary>Виртуальный поезд: тот же объект, пока он на схеме (номер могли сменить — тогда ищем по номеру).</summary>
    VirtualTrain T
    {
        get
        {
            if (hw) return null;
            if (train == null || !ModelHost.Model.Trains.Trains.Contains(train) || train.Leaving) train = RadioUi.Train(number);
            return train;
        }
    }

    void Refill()
    {
        var t = T;
        bool gone = !Hw && t == null;
        foreach (Control c in Commands.Controls) c.Enabled = !gone;
        lNum.Text = t?.Number.ToString() ?? number.ToString();
        lSpeed.Text = t != null ? $"{Radio.SpeedNow(t)} км/ч" : "—";
        lIndex.Text = Hw ? "поезд на макете (ведут руками)" : t == null ? "" : (t.Index.Length > 0 ? t.Index : "не задан") + $"   {t.Category}{Radio.Marks(t)}";
        // построчно: каждая строка — свой шаблон перевода
        lWhere.Text = gone ? "поезда нет на станции" : t != null ? string.Join("\n", Radio.Where(t).Split('\n').Select(L.T))
                                                    : $"Ст. {DispatchLink.StationName}, {Radio.HwWhere(number)}";
        if (!gone)
        {
            bStop.Enabled = t == null || !t.StopOrder;
            bGo.Enabled = t == null || t.StopOrder || t.Waiting.Length > 0;
        }
        var lines = Radio.Talk(number);
        if (lines.Count != talkCount)
        {
            talkCount = lines.Count;
            talk.BeginUpdate();
            talk.Items.Clear();
            foreach (var l in lines) talk.Items.Add(l);
            if (talk.Items.Count > 0) talk.TopIndex = talk.Items.Count - 1;
            talk.EndUpdate();
        }
    }

    void AskOrder()
    {
        var t = T;
        var sigs = ModelHost.Model.IL.Signals.Values.Where(s => s.Kind != SignalKind.Neighbor && s.Remote == null && (t == null || s.Dir == t.Dir))
                                                     .Select(s => s.Label).Distinct().OrderBy(s => s).ToList();
        string near = t?.Head == null ? "" : ModelHost.Model.Trains.Peek(t.Head, t.Dir)?.Signal?.Label ?? "";
        int no = Journals.Entries["ДУ-58"].Select(e => int.TryParse(new string(e["no"].Where(char.IsDigit).ToArray()), out var x) ? x : 0).DefaultIfEmpty(0).Max() + 1;
        using var f = new OrderForm(number, sigs, near, no);
        if (f.ShowDialog(this) == DialogResult.OK)
            Radio.Order(T, number, f.Signal, f.No, f.OrderText);
    }
}

// ---------------------------------------------------------------------
//  «Указание»: регистрируемый приказ на проследование запрещающего сигнала
// ---------------------------------------------------------------------
class OrderForm : Form
{
    readonly ComboBox cbSig = new() { DropDownStyle = ComboBoxStyle.DropDown, Location = new Point(200, 48), Width = 120 };
    readonly NumericUpDown nNo = new() { Minimum = 1, Maximum = 9999, Location = new Point(200, 14), Width = 80 };
    readonly TextBox txt = new() { Multiline = true, Location = new Point(12, 110), Size = new Size(476, 110), ScrollBars = ScrollBars.Vertical };
    public string Signal => cbSig.Text.Trim();
    public int No => (int)nNo.Value;
    public string OrderText => txt.Text.Trim();

    public OrderForm(int train, List<string> signals, string near, int no)
    {
        Text = $"Указание машинисту поезда №{train}";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 270);
        nNo.Value = Math.Clamp(no, 1, 9999);
        cbSig.Items.AddRange(signals.Cast<object>().ToArray());
        cbSig.Text = near;
        Controls.Add(new Label { Text = "Номер приказа", AutoSize = true, Location = new Point(12, 16) });
        Controls.Add(nNo);
        Controls.Add(new Label { Text = "Запрещающий светофор", AutoSize = true, Location = new Point(12, 51) });
        Controls.Add(cbSig);
        Controls.Add(new Label { Text = "Текст регистрируемого приказа (запишется в ДУ-58):", AutoSize = true, Location = new Point(12, 86) });
        Controls.Add(txt);
        void Fill() => txt.Text = $"Разрешаю поезду №{train} проследовать запрещающий сигнал светофора {cbSig.Text} ст. {DispatchLink.StationName}. " +
                                  $"ДСП {Training.Student?.Name ?? ""}";
        Fill();
        cbSig.TextChanged += (_, _) => Fill();
        var ok = new Button { Text = "Передать", Location = new Point(296, 230), Size = new Size(94, 32) };
        var cancel = new Button { Text = "Отказ", DialogResult = DialogResult.Cancel, Location = new Point(396, 230), Size = new Size(94, 32) };
        ok.Click += (_, _) =>
        {
            if (Signal.Length == 0 || OrderText.Length == 0) { MessageBox.Show(this, "Укажите светофор и текст приказа.", Text); return; }
            if (MessageBox.Show(this, "Дежурный обязан лично убедиться, что маршрут приготовлен, стрелки заперты и путь свободен.\nПередать приказ?",
                                "Ответственная команда", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                DialogResult = DialogResult.OK;
        };
        Controls.AddRange(new Control[] { ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

// ---------------------------------------------------------------------
//  Предупреждение на поезд (рис. 19 части 1)
// ---------------------------------------------------------------------
class TrainWarningForm : Form
{
    public TrainWarningForm(VirtualTrain t, int number)
    {
        Text = "Ввод предупреждения на поезд";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 200);
        Controls.Add(new Label { Text = $"Поезд №{number}" + (t?.Index.Length > 0 ? $", индекс {t.Index}" : ""), AutoSize = true, Location = new Point(12, 12) });
        Controls.Add(new Label { Text = "Скорость не более, км/ч (0 — без ограничения)", AutoSize = true, Location = new Point(12, 48) });
        var sp = new NumericUpDown { Minimum = 0, Maximum = 120, Increment = 5, Value = t?.SpeedLimit ?? 0, Location = new Point(340, 45), Width = 80 };
        Controls.Add(sp);
        var act = Radio.ActiveAll.Count(r => !r.IsWindow);
        Controls.Add(new Label
        {
            Text = act > 0 ? $"Машинисту будут сообщены и действующие предупреждения ({act})." : "Других действующих предупреждений нет.",
            AutoSize = false, Size = new Size(416, 44), Location = new Point(12, 82),
        });
        var ok = new Button { Text = "Принять", Location = new Point(236, 156), Size = new Size(94, 32) };
        var no = new Button { Text = "Отказ", DialogResult = DialogResult.Cancel, Location = new Point(336, 156), Size = new Size(94, 32) };
        ok.Click += (_, _) => { Radio.WarnTrain(t, number, (int)sp.Value); DialogResult = DialogResult.OK; };
        Controls.AddRange(new Control[] { ok, no });
        AcceptButton = ok;
        CancelButton = no;
    }
}

// ---------------------------------------------------------------------
//  Ввод предупреждения / «окна» на участок или перегон (рис. 16–17 части 1)
// ---------------------------------------------------------------------
class RestrictionForm : Form
{
    public RestrictionForm(TrackSection sec, DirectionLine line)
    {
        Text = "Ввод предупреждения / окна";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 470);

        var win = new CheckBox { Text = "Ввести данные для окна (путь закрыт для работ)", AutoSize = true, Location = new Point(12, 12) };
        var service = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 40), Width = 300, Enabled = false };
        service.Items.AddRange(Radio.Services);
        service.SelectedIndex = 0;
        Controls.Add(win);
        Controls.Add(new Label { Text = "Служба", AutoSize = true, Location = new Point(28, 43) });
        Controls.Add(service);

        var gPlace = new GroupBox { Text = "Место действия", Location = new Point(12, 76), Size = new Size(446, 120) };
        var rSec = new RadioButton { Text = $"Участок {sec.Label}", AutoSize = true, Location = new Point(12, 24), Checked = line == null };
        var rLine = new RadioButton { Text = line != null ? $"Весь перегон: {line.Title}" : "Весь перегон", AutoSize = true, Location = new Point(12, 50),
                                      Enabled = line != null, Checked = line != null };
        var km = new TextBox { Location = new Point(190, 80), Width = 240 };
        gPlace.Controls.AddRange(new Control[] { rSec, rLine, new Label { Text = "Км, пикет, путь", AutoSize = true, Location = new Point(12, 83) }, km });
        Controls.Add(gPlace);

        var gSpeed = new GroupBox { Text = "Скорость, км/ч", Location = new Point(12, 204), Size = new Size(446, 90) };
        var sp = new NumericUpDown { Minimum = 5, Maximum = 120, Increment = 5, Value = 25, Location = new Point(140, 24), Width = 70 };
        var sf = new NumericUpDown { Minimum = 5, Maximum = 120, Increment = 5, Value = 25, Location = new Point(340, 24), Width = 70 };
        var all = new CheckBox { Text = "для всех поездов одинаково", Checked = true, AutoSize = true, Location = new Point(12, 58) };
        sp.ValueChanged += (_, _) => { if (all.Checked) sf.Value = sp.Value; };
        all.CheckedChanged += (_, _) => { sf.Enabled = !all.Checked; if (all.Checked) sf.Value = sp.Value; };
        sf.Enabled = false;
        gSpeed.Controls.AddRange(new Control[] { new Label { Text = "пассажирские", AutoSize = true, Location = new Point(12, 27) }, sp,
                                                 new Label { Text = "грузовые", AutoSize = true, Location = new Point(250, 27) }, sf, all });
        Controls.Add(gSpeed);

        var gTime = new GroupBox { Text = "Период действия", Location = new Point(12, 302), Size = new Size(446, 110) };
        var rUntilCancel = new RadioButton { Text = "До отмены", AutoSize = true, Checked = true, Location = new Point(12, 26) };
        var rUntil = new RadioButton { Text = "Окончание (ч:мин)", AutoSize = true, Location = new Point(12, 58) };
        var until = new TextBox { Text = Training.Fmt(Du46.Now + 3600)[..5], Location = new Point(190, 56), Width = 70 };
        gTime.Controls.AddRange(new Control[] { rUntilCancel, rUntil, until });
        Controls.Add(gTime);

        win.CheckedChanged += (_, _) => { service.Enabled = win.Checked; gSpeed.Enabled = !win.Checked; };

        var ok = new Button { Text = "Принять", Location = new Point(262, 426), Size = new Size(94, 32) };
        var no = new Button { Text = "Отказ", DialogResult = DialogResult.Cancel, Location = new Point(362, 426), Size = new Size(94, 32) };
        ok.Click += (_, _) =>
        {
            double end = -1;
            if (rUntil.Checked)
            {
                if (!TimeSpan.TryParse(until.Text.Trim(), out var ts)) { MessageBox.Show(this, "Время окончания — в виде ЧЧ:ММ.", Text); return; }
                end = ts.TotalSeconds;
                while (end <= Du46.Now) end += 86400;
            }
            var r = new Restriction
            {
                Kind = win.Checked ? "window" : "warning",
                Obj = rLine.Checked && line != null ? "L:" + line.Id : "P:" + sec.Id,
                Place = km.Text.Trim(),
                SpeedPass = win.Checked ? 0 : (int)sp.Value,
                SpeedFreight = win.Checked ? 0 : (int)sf.Value,
                Service = win.Checked ? service.SelectedItem?.ToString() ?? "" : "",
                Until = end,
            };
            Radio.Set(r);
            DialogResult = DialogResult.OK;
        };
        Controls.AddRange(new Control[] { ok, no });
        AcceptButton = ok;
        CancelButton = no;
    }
}
