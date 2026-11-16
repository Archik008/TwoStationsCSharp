using System.Diagnostics;
using StationApp.Core;
using StationApp.Yard;

namespace StationApp.Ui;

// =====================================================================
//  ОКНО «СОРТИРОВОЧНАЯ» — ПУЛЬТ ДЕЖУРНОГО ПО ГОРКЕ  [ДОБАВЛЕНО 12.11.2026]
//  Слева — схема и вид горки сбоку (Ui\YardView.cs), под ними журнал.
//  Справа — горочный светофор и надвиг, замедлители и АРС, маршруты отцепов (накопитель ГАЦ),
//  сортировочный листок, маневры, оценка.
//  Подписи, которые меняются по таймеру, ставятся через LangUi.SetText (иначе на kk / en мигают).
// =====================================================================

public sealed class YardForm : Form
{
    YardSim sim;
    readonly YardView view = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    double speed = 1;
    int seed = Environment.TickCount;

    // верх
    readonly Label lblClock = new() { AutoSize = true, ForeColor = Color.FromArgb(150, 210, 255), Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(10, 9, 6, 0) };
    readonly ComboBox cbVariant = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Margin = new Padding(6, 7, 6, 0) };
    readonly ComboBox cbMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Margin = new Padding(6, 7, 6, 0) };
    readonly List<Button> speedButtons = new();
    Button btnAi;
    Ai.AiChatWindow aiWindow;

    // правая панель
    readonly Label lblAspect = new() { AutoSize = false, Height = 22, Dock = DockStyle.Top, ForeColor = Color.White };
    readonly Label lblPush = new() { AutoSize = false, Height = 40, Dock = DockStyle.Top, ForeColor = Color.Gainsboro };
    readonly Button[] pushBtns = new Button[5];
    readonly CheckBox chkArs = new() { Text = "АРС — автоматическое регулирование скорости", AutoSize = true, ForeColor = Color.White };
    readonly Button[,] retBtns = new Button[3, 5];
    readonly Label[] retInfo = new Label[3];
    readonly Button[] routeBtns = new Button[9];
    readonly Label lblNext = new() { AutoSize = false, Height = 36, Dock = DockStyle.Top, ForeColor = Color.Gainsboro };
    readonly Label lblListok = new() { AutoSize = false, Height = 20, Dock = DockStyle.Top, ForeColor = Color.White };
    readonly ListView listok = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Top, Height = 214, Tag = "nolang", HideSelection = false, BackColor = Color.FromArgb(24, 28, 33), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.FixedSingle };
    readonly Button btnEscort = new(), btnLocoTo = new(), btnLocoBack = new(), btnForm = new(), btnCompact = new(), btnStuck = new();
    readonly Label lblScore = new() { AutoSize = false, Height = 58, Dock = DockStyle.Top, ForeColor = Color.White };
    readonly ListBox journal = new() { Dock = DockStyle.Bottom, Height = 128, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 17, BackColor = Color.FromArgb(18, 21, 25), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly ToolTip tips = new();
    readonly List<(string t, string text, bool bad)> log = new();
    static readonly string[] ListokCols = { "№", "Ваг.", "Род", "т", "Ход", "Путь", "Марш.", "Отм.", "Состояние" };

    static readonly Color Panel = Color.FromArgb(30, 35, 41), Grp = Color.FromArgb(36, 42, 50);

    public YardForm()
    {
        Text = L.T("Сортировочная — пульт дежурного по горке (ДСПГ)");
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1600, 980);
        MinimumSize = new Size(1100, 700);
        WindowState = FormWindowState.Maximized;
        BackColor = Panel;
        KeyPreview = true;
        Font = new Font("Segoe UI", 9.5f);

        BuildTop();
        BuildRight();
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(view);
        left.Controls.Add(journal);
        Controls.Add(left);
        left.BringToFront();

        journal.DrawItem += DrawJournal;
        view.Pointed += id => Ai.AiStation.Point(id);
        view.TrackMenu += ShowTrackMenu;
        view.Clicked += RefreshPanel;
        timer.Tick += (_, _) => OnTimer();
        L.Changed += OnLang;
        FormClosed += (_, _) => { timer.Stop(); L.Changed -= OnLang; };
        NewShift();
        timer.Start();
    }

    // =================================================================
    //  ПОСТРОЕНИЕ ОКНА
    // =================================================================
    Button Btn(string text, Action click, int w = 0, Color? back = null, Color? fore = null)
    {
        var b = new Button
        {
            Text = text, FlatStyle = FlatStyle.Flat, ForeColor = fore ?? Color.White, BackColor = back ?? Color.FromArgb(55, 64, 76),
            AutoSize = w == 0, Height = 30, Margin = new Padding(3), UseVisualStyleBackColor = false,
        };
        if (w > 0) b.Width = w;
        b.FlatAppearance.BorderColor = Color.FromArgb(80, 90, 104);
        b.Click += (_, _) => { click(); RefreshPanel(); };
        return b;
    }

    void BuildTop()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(22, 26, 31), WrapContents = true, Padding = new Padding(4, 0, 4, 4) };
        top.Controls.Add(new Label { Text = "Сортировочная — пульт ДСПГ", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(6, 9, 6, 0) });
        top.Controls.Add(lblClock);
        cbVariant.Items.AddRange(new object[] { "Вариант: только горка", "Вариант: горка + маневры" });
        cbVariant.SelectedIndex = 0;
        cbVariant.SelectedIndexChanged += (_, _) =>
        {
            if (sim == null || sim.AutoOps == (cbVariant.SelectedIndex == 0)) return;   // перевод списка тоже «меняет» выбор
            sim.AutoOps = cbVariant.SelectedIndex == 0;
            Note(sim.AutoOps ? "Вариант «Только горка»: приём, осмотр, горочный локомотив и вывод составов — сами" : "Вариант «Горка + маневры»: горочный локомотив под состав, его возврат и вывод составов — делаете вы", false);
            RefreshPanel();
        };
        tips.SetToolTip(cbVariant, "Только горка — всё до и после горки делается само. Горка + маневры — вы сами подаёте горочный локомотив, возвращаете его и выводите составы в парк отправления");
        top.Controls.Add(cbVariant);
        cbMode.Items.AddRange(new object[] { "Маршруты: ГАЦ (из листка)", "Маршруты: ручной набор" });
        cbMode.SelectedIndex = 0;
        cbMode.SelectedIndexChanged += (_, _) => { sim.SetGac(cbMode.SelectedIndex == 0); RefreshPanel(); };
        tips.SetToolTip(cbMode, "ГАЦ — маршруты отцепов ставятся сами из сортировочного листка. Ручной — перед каждым отцепом нажмите кнопку его пути");
        top.Controls.Add(cbMode);
        foreach (var (t, v) in new[] { ("⏸", 0.0), ("×1", 1.0), ("×2", 2.0), ("×4", 4.0) })
        {
            var b = Btn(t, () => { speed = v; }, 40);
            b.Tag = v;
            speedButtons.Add(b);
            top.Controls.Add(b);
        }
        tips.SetToolTip(speedButtons[0], "Пауза");
        top.Controls.Add(Btn("Новая смена", () =>
        {
            if (MessageBox.Show(L.T("Начать новую смену? Оценка и журнал обнулятся."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { seed = Environment.TickCount; NewShift(); }
        }));
        top.Controls.Add(Btn("Инструкция", OpenGuide));
        var lang = LangUi.Selector();
        top.Controls.Add(lang);
        btnAi = Btn("ИИ", () => ShowAi(aiWindow == null || !aiWindow.Visible), 60, Color.FromArgb(0, 105, 190));
        tips.SetToolTip(btnAi, "ИИ-помощник: вопросы про горку, роспуск, светофоры, замедлители");
        top.Controls.Add(btnAi);
        Controls.Add(top);
    }

    GroupBox Group(string text, int height)
    {
        return new GroupBox { Text = text, Dock = DockStyle.Top, Height = height, ForeColor = Color.FromArgb(150, 200, 255), BackColor = Grp, Padding = new Padding(6, 4, 6, 4) };
    }

    FlowLayoutPanel Row(int h = 36) => new() { Dock = DockStyle.Top, Height = h, WrapContents = false, Margin = Padding.Empty };

    void BuildRight()
    {
        var right = new Panel { Dock = DockStyle.Right, Width = 480, AutoScroll = true, BackColor = Panel, Padding = new Padding(4) };
        var items = new List<Control>();

        // ---- горочный светофор ----
        var g1 = Group("Горочный светофор Г и надвиг", 182);
        var r1 = Row();
        var defs = new (string t, Aspect a, Color c, Color f)[]
        {
            ("Красный", Aspect.Red, Color.FromArgb(170, 35, 30), Color.White),
            ("Жёлтый", Aspect.Yellow, Color.FromArgb(200, 160, 20), Color.Black),
            ("Ж + З", Aspect.YellowGreen, Color.FromArgb(140, 170, 40), Color.Black),
            ("Зелёный", Aspect.Green, Color.FromArgb(30, 150, 60), Color.White),
            ("К + «Н»", Aspect.RedN, Color.FromArgb(120, 30, 60), Color.White),
        };
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            pushBtns[i] = Btn(d.t, () => sim.SetAspect(d.a), 84, d.c, d.f);
            r1.Controls.Add(pushBtns[i]);
        }
        tips.SetToolTip(pushBtns[0], "Стой! Роспуск запрещён (клавиша Esc)");
        tips.SetToolTip(pushBtns[1], "Роспуск с уменьшенной скоростью (3 км/ч)");
        tips.SetToolTip(pushBtns[2], "Роспуск с промежуточной скоростью (4,5 км/ч)");
        tips.SetToolTip(pushBtns[3], "Роспуск с установленной скоростью (6 км/ч)");
        tips.SetToolTip(pushBtns[4], "Красный и белая буква «Н» — осадить вагоны с горки в парк приёма");
        var r2 = Row();
        r2.Controls.Add(new Label { Text = "Маршрут надвига с пути:", AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(3, 9, 3, 0) });
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            int aa = a;
            var b = Btn("П" + a, () => sim.SetPushRoute(aa), 46);
            tips.SetToolTip(b, $"Задать маршрут надвига с пути П{a} парка приёма на горку");
            r2.Controls.Add(b);
        }
        var r3 = Row();
        btnEscort.Text = "Спустить с локомотивом";
        btnEscort.Click += (_, _) => { sim.StartEscort(); RefreshPanel(); };
        Style(btnEscort);
        tips.SetToolTip(btnEscort, "Вагоны с отметкой «Л» (люди) или «Е» (газ) распускать с горки нельзя: остановите надвиг перед горбом и спустите их с локомотивом");
        r3.Controls.Add(btnEscort);
        g1.Controls.Add(lblPush);
        g1.Controls.Add(lblAspect);
        g1.Controls.Add(r3);
        g1.Controls.Add(r2);
        g1.Controls.Add(r1);
        items.Add(g1);

        // ---- замедлители ----
        var g2 = Group("Замедлители (тормозные позиции)", 178);
        chkArs.Dock = DockStyle.Top;
        chkArs.CheckedChanged += (_, _) => { sim.SetArs(chkArs.Checked); RefreshPanel(); };
        tips.SetToolTip(chkArs, "АРС сама тормозит отцепы до рекомендуемой скорости. Без неё — ступени 1–4 и «О» (оттормозить) вручную");
        for (int i = 2; i >= 0; i--)
        {
            int ri = i;
            var row = Row(38);
            row.Controls.Add(new Label { Text = new[] { "1ТП", "2ТП-А", "2ТП-Б" }[i], Width = 54, ForeColor = Color.White, Margin = new Padding(3, 10, 0, 0) });
            for (int s = 0; s <= 4; s++)
            {
                int ss = s;
                var b = Btn(s == 0 ? "О" : s.ToString(), () => sim.SetRetarder(ri, ss), 32);
                retBtns[i, s] = b;
                row.Controls.Add(b);
            }
            retInfo[i] = new Label { AutoSize = false, Width = 200, Height = 32, ForeColor = Color.Gainsboro, Margin = new Padding(4, 2, 0, 0) };
            row.Controls.Add(retInfo[i]);
            g2.Controls.Add(row);
        }
        g2.Controls.Add(chkArs);
        items.Add(g2);

        // ---- маршруты отцепов ----
        var g3 = Group("Маршрут отцепа (накопитель ГАЦ)", 158);
        var rr2 = Row(44);
        var rr1 = Row(44);
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            int kk = k;
            var b = Btn($"{k}", () => sim.RouteButton(kk), 52);
            b.Height = 38;
            b.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            tips.SetToolTip(b, $"Путь {k} — {YardSim.TrackNames[k]} (клавиша {k})");
            routeBtns[k] = b;
            (k <= 4 ? rr1 : rr2).Controls.Add(b);
        }
        var undo = Btn("Сбросить последний", () => sim.UndoRoute());
        rr2.Controls.Add(undo);
        g3.Controls.Add(lblNext);
        g3.Controls.Add(rr2);
        g3.Controls.Add(rr1);
        items.Add(g3);

        // ---- листок ----
        var g4 = Group("Сортировочный листок", 262);
        foreach (var c in ListokCols) listok.Columns.Add(L.T(c));
        int[] widths = { 26, 34, 72, 40, 42, 38, 44, 36, 128 };
        for (int i = 0; i < widths.Length; i++) listok.Columns[i].Width = widths[i];
        listok.SelectedIndexChanged += (_, _) =>
        {
            if (listok.SelectedItems.Count == 1 && listok.SelectedItems[0].Tag is Cut c) sim.SelectedCut = c.State == CutState.InTrain ? c : null;
        };
        tips.SetToolTip(listok, "Выберите отцеп и нажмите кнопку пути — изменить его маршрут");
        g4.Controls.Add(listok);
        g4.Controls.Add(lblListok);
        items.Add(g4);

        // ---- маневры ----
        var g5 = Group("Маневры", 116);
        var m1 = Row(); var m2 = Row();
        foreach (var (b, t, tip) in new[]
        {
            (btnLocoTo, "Горочный локомотив под состав…", "Горочный локомотив по обходному пути в хвост состава (вариант «Горка + маневры»)"),
            (btnLocoBack, "Вернуть горочный локомотив", "После роспуска — с горба на стоянку (вариант «Горка + маневры»)"),
            (btnForm, "Вывести состав…", "Набранный состав с сортировочного пути — в парк отправления (вариант «Горка + маневры»)"),
            (btnCompact, "Сжать путь…", "Закрыть «окна» после недоката: вагоны на пути осаживаются к концу пути"),
            (btnStuck, "Протолкнуть отцеп", "Отцеп остановился в стрелочной зоне — протолкнуть его на путь (сначала остановите роспуск)"),
        })
        {
            b.Text = t;
            Style(b);
            tips.SetToolTip(b, tip);
        }
        btnLocoTo.Click += (_, _) => Menu(btnLocoTo, Enumerable.Range(1, Lay.ArrTracks).Where(a => sim.Arr[a].State == ArrState.WaitLoco).Select(a => ("П" + a, (Action)(() => sim.SendHumpLoco(a)))));
        btnLocoBack.Click += (_, _) => { sim.ReturnHumpLoco(); RefreshPanel(); };
        btnForm.Click += (_, _) => Menu(btnForm, Enumerable.Range(1, Lay.Tracks).Where(k => sim.OnTrack(k).Any()).Select(k => ($"Путь {k} — {sim.OnTrack(k).Sum(c => c.Wagons.Count)} ваг.", (Action)(() => sim.FormTrain(k)))));
        btnCompact.Click += (_, _) => Menu(btnCompact, Enumerable.Range(1, Lay.Tracks).Where(k => sim.OnTrack(k).Any()).Select(k => ($"Путь {k}", (Action)(() => sim.CompactTrack(k)))));
        btnStuck.Click += (_, _) => { sim.PushStuck(); RefreshPanel(); };
        m1.Controls.Add(btnLocoTo); m1.Controls.Add(btnLocoBack);
        m2.Controls.Add(btnForm); m2.Controls.Add(btnCompact); m2.Controls.Add(btnStuck);
        g5.Controls.Add(m2);
        g5.Controls.Add(m1);
        items.Add(g5);

        var g6 = Group("Оценка смены", 82);
        g6.Controls.Add(lblScore);
        items.Add(g6);

        // Dock = Top складывает снизу вверх: добавляем в обратном порядке
        for (int i = items.Count - 1; i >= 0; i--) right.Controls.Add(items[i]);
        Controls.Add(right);
    }

    void Style(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.ForeColor = Color.White;
        b.BackColor = Color.FromArgb(55, 64, 76);
        b.AutoSize = true;
        b.Height = 30;
        b.Margin = new Padding(3);
        b.FlatAppearance.BorderColor = Color.FromArgb(80, 90, 104);
    }

    void Menu(Control at, IEnumerable<(string text, Action act)> choices)
    {
        var list = choices.ToList();
        if (list.Count == 0) { Note("Нельзя: нет подходящих путей", true); return; }
        var m = new ContextMenuStrip();
        foreach (var (t, a) in list) m.Items.Add(L.T(t), null, (_, _) => { a(); RefreshPanel(); });
        m.Show(at, new Point(0, at.Height));
    }

    void ShowTrackMenu(int k, Point p)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(L.T($"Путь {k}: маршрут отцепу"), null, (_, _) => sim.RouteButton(k));
        m.Items.Add(L.T("Сжать путь (закрыть окна)"), null, (_, _) => sim.CompactTrack(k));
        var f = m.Items.Add(L.T("Вывести состав в парк отправления"), null, (_, _) => sim.FormTrain(k));
        f.Enabled = !sim.AutoOps;
        if (sim.AutoOps) f.ToolTipText = L.T("В варианте «Только горка» составы выводятся сами");
        m.Show(view, p);
    }

    // =================================================================
    //  СМЕНА, ТАЙМЕР
    // =================================================================
    void NewShift()
    {
        sim = new YardSim(seed);
        sim.AutoOps = cbVariant.SelectedIndex <= 0;
        sim.GacMode = cbMode.SelectedIndex <= 0;
        sim.Ars = chkArs.Checked;
        if (!sim.GacMode) foreach (var c in sim.ListokTrain.AllCuts) c.Route = 0;
        sim.Log += (t, bad) =>
        {
            if (IsDisposed) return;
            Note(t, bad);
        };
        view.Sim = sim;
        Ai.AiStation.Sim = sim;
        log.Clear();
        journal.Items.Clear();
        Note("Смена началась. Поезд на пути П1 готов к роспуску: задайте маршрут надвига «П1» и откройте горочный светофор", false);
        RefreshPanel();
    }

    void Note(string text, bool bad)
    {
        var t = sim?.Clock.ToString("HH:mm:ss") ?? "";
        log.Add((t, text, bad));
        Ai.AiStation.Note(text, bad);
        journal.Items.Add(log.Count - 1);
        if (journal.Items.Count > 400) { journal.Items.RemoveAt(0); }
        journal.TopIndex = Math.Max(0, journal.Items.Count - journal.ClientSize.Height / journal.ItemHeight);
    }

    void DrawJournal(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        e.DrawBackground();
        int i = (int)journal.Items[e.Index];
        var (t, text, bad) = log[i];
        var c = bad ? Color.FromArgb(255, 120, 105) : Color.Gainsboro;
        TextRenderer.DrawText(e.Graphics, t + "  " + Tr(text), journal.Font, e.Bounds, c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    static readonly System.Text.RegularExpressions.Regex RePrefix = new(@"^(НАРУШЕНИЕ|Нельзя): (.*?)( \(−\d+\))?$", System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>Перевод строки журнала: «НАРУШЕНИЕ: …» / «Нельзя: …» — приставку, текст и баллы отдельно (иначе шаблон не совпадёт).</summary>
    static string Tr(string ru)
    {
        var m = RePrefix.Match(ru);
        return m.Success ? L.T(m.Groups[1].Value) + ": " + L.T(m.Groups[2].Value) + m.Groups[3].Value : L.T(ru);
    }

    int tick;
    void OnTimer()
    {
        if (speed > 0) sim.Tick(0.05 * speed);
        view.Invalidate();
        if (++tick % 4 == 0) RefreshPanel();
    }

    void OnLang()
    {
        for (int i = 0; i < ListokCols.Length; i++) listok.Columns[i].Text = L.T(ListokCols[i]);
        listok.Items.Clear();
        Text = L.T("Сортировочная — пульт дежурного по горке (ДСПГ)");
        journal.Invalidate();
        RefreshPanel();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (ActiveControl is TextBoxBase) return base.ProcessCmdKey(ref msg, key);
        if (key == Keys.Escape) { sim.SetAspect(Aspect.Red); RefreshPanel(); return true; }
        if (key >= Keys.D1 && key <= Keys.D8) { sim.RouteButton(key - Keys.D0); RefreshPanel(); return true; }
        if (key >= Keys.NumPad1 && key <= Keys.NumPad8) { sim.RouteButton(key - Keys.NumPad0); RefreshPanel(); return true; }
        if (key == Keys.F1) { OpenGuide(); return true; }
        if (key == Keys.Space) { speed = speed == 0 ? 1 : 0; return true; }
        return base.ProcessCmdKey(ref msg, key);
    }

    // =================================================================
    //  ОБНОВЛЕНИЕ ПАНЕЛИ
    // =================================================================
    static string Kmh(double v) => (v * 3.6).ToString("0.0");

    void RefreshPanel()
    {
        if (sim == null || IsDisposed) return;
        LangUi.SetText(lblClock, sim.Clock.ToString("HH:mm:ss"));
        foreach (var b in speedButtons) b.BackColor = (double)b.Tag == speed ? Color.FromArgb(0, 105, 190) : Color.FromArgb(55, 64, 76);

        // светофор и надвиг
        LangUi.SetText(lblAspect, "Г: " + YardSim.AspectName(sim.Hump));
        lblAspect.ForeColor = sim.Hump switch { Aspect.Green => Color.FromArgb(90, 230, 120), Aspect.YellowGreen => Color.FromArgb(200, 230, 90), Aspect.Yellow => Color.Gold, _ => Color.FromArgb(255, 110, 100) };
        for (int i = 0; i < 5; i++) pushBtns[i].FlatAppearance.BorderSize = (int)sim.Hump == new[] { 0, 1, 2, 3, 4 }[i] ? 3 : 1;
        for (int i = 0; i < 5; i++) pushBtns[i].FlatAppearance.BorderColor = (int)sim.Hump == i ? Color.White : Color.FromArgb(80, 90, 104);
        string push;
        if (sim.Pushing is Train t)
        {
            int left = t.Cuts.Sum(c => c.Wagons.Count);
            push = t.Escort
                ? (t.Returning ? "Горочный локомотив возвращается на горб" : $"Спуск с локомотивом: {left} ваг., {Kmh(t.V)} км/ч")
                : $"Надвиг поезда {t.Name} с пути П{t.Arr}: {Kmh(t.V)} км/ч, в составе осталось {left} ваг.";
            if (!t.Escort && t.Cuts.Count > 0 && t.Cuts.All(c => c.Forbidden)) push += ". Впереди вагоны «не с горки» — остановите надвиг и «Спустить с локомотивом»!";
        }
        else
        {
            var ready = sim.Arr.Skip(1).Where(a => a.State == ArrState.Ready).Select(a => "П" + a.No).ToList();
            push = ready.Count > 0 ? "Готовы к роспуску: " + string.Join(", ", ready) + " — задайте маршрут надвига" : "Нет состава, готового к роспуску. Горочный локомотив: " + LocoText();
        }
        LangUi.SetText(lblPush, push);
        btnEscort.Enabled = sim.Pushing != null && !sim.Pushing.Escort;

        // замедлители
        for (int i = 0; i < 3; i++)
        {
            var r = sim.Retarders[i];
            for (int s = 0; s <= 4; s++)
            {
                var b = retBtns[i, s];
                b.BackColor = r.Step == s ? (s == 0 ? Color.FromArgb(90, 110, 130) : Color.FromArgb(220, 200 - s * 30, 40)) : Color.FromArgb(55, 64, 76);
                b.ForeColor = r.Step == s && s > 0 ? Color.Black : Color.White;
                b.Enabled = !sim.Ars;
            }
            string info = r.Cut == null ? "—"
                : (r.CutOn ? $"отцеп {r.Cut.No}: {Kmh(r.Cut.V)} км/ч" : $"подходит {r.Cut.No}: {Kmh(r.Cut.V)} км/ч") + $"\nрек. на выходе {Kmh(r.Target)} км/ч";
            LangUi.SetText(retInfo[i], info);
            retInfo[i].ForeColor = r.CutOn && r.Cut.V > r.Target + 0.4 ? Color.FromArgb(255, 170, 90) : Color.Gainsboro;
        }

        // маршруты
        var lt = sim.ListokTrain;
        var next = lt?.AllCuts.FirstOrDefault(c => c.State == CutState.InTrain && c.Route == 0);
        string nx = sim.SelectedCut != null && sim.SelectedCut.State == CutState.InTrain
            ? $"Выбран отцеп {sim.SelectedCut.No} (по листку — путь {sim.SelectedCut.Dest}): нажмите кнопку пути"
            : next != null ? $"Следующий без маршрута: отцеп {next.No} — по листку путь {next.Dest}" : sim.GacMode ? "ГАЦ: маршруты всех отцепов — из листка" : "Маршруты всем отцепам заданы";
        LangUi.SetText(lblNext, nx);
        for (int k = 1; k <= Lay.Tracks; k++)
            routeBtns[k].BackColor = (sim.SelectedCut?.Dest ?? next?.Dest) == k ? Color.FromArgb(60, 100, 150) : Color.FromArgb(55, 64, 76);

        RefreshListok();

        // маневры
        bool man = !sim.AutoOps;
        btnLocoTo.Enabled = man && sim.Loco == HumpLocoState.Standby;
        btnLocoBack.Enabled = man && sim.Loco == HumpLocoState.AtHorb;
        btnForm.Enabled = man;
        btnStuck.Enabled = sim.Cuts.Any(c => c.State == CutState.Stopped && c.Head < Lay.STrack);

        LangUi.SetText(lblScore, $"Оценка: {sim.Grade} ({sim.Score} баллов из 100) · нарушений: {sim.Violations.Count}\n" +
                                 $"Распущено: {sim.Rolled} ваг., {sim.CutsRolled} отцепов (на свой путь — {sim.CorrectCuts})\nОтправлено поездов: {sim.Departed}");
        lblScore.ForeColor = sim.Score >= 90 ? Color.FromArgb(120, 230, 140) : sim.Score >= 75 ? Color.Gold : Color.FromArgb(255, 120, 100);
    }

    string LocoText() => sim.Loco switch
    {
        HumpLocoState.Standby => L.T("на стоянке"),
        HumpLocoState.ToTrain => L.T("идёт в хвост состава"),
        HumpLocoState.Coupled => L.T("прицеплен к составу"),
        HumpLocoState.AtHorb => L.T("на горбу"),
        _ => L.T("возвращается"),
    };

    Train shownTrain;
    void RefreshListok()
    {
        var t = sim.ListokTrain;
        LangUi.SetText(lblListok, t == null ? "Листка нет — нет поезда" : $"Поезд {t.Name} · {t.WagonCount} ваг. · отцепов {t.AllCuts.Count}");
        if (t != shownTrain) { listok.Items.Clear(); shownTrain = t; }
        if (t == null) return;
        listok.BeginUpdate();
        try
        {
            for (int i = 0; i < t.AllCuts.Count; i++)
            {
                var c = t.AllCuts[i];
                string[] cells =
                {
                    c.No.ToString(), c.Wagons.Count.ToString(), c.KindsText, c.Mass.ToString("0"),
                    L.T(c.Runner switch { Runner.Good => "хор.", Runner.Bad => "плох.", _ => "ср." }),
                    c.Dest.ToString(), c.Route > 0 ? c.Route.ToString() : "—", c.Mark, CutStatus(c),
                };
                ListViewItem it;
                if (i < listok.Items.Count) it = listok.Items[i];
                else
                {
                    it = new ListViewItem(cells) { Tag = c, UseItemStyleForSubItems = true };
                    listok.Items.Add(it);
                }
                for (int j = 0; j < cells.Length; j++) if (it.SubItems[j].Text != cells[j]) it.SubItems[j].Text = cells[j];
                Color fc = c.Forbidden ? Color.FromArgb(255, 120, 100)
                    : c.State == CutState.InTrain ? (c == t.Cuts.FirstOrDefault() ? Color.White : Color.Gainsboro)
                    : c.Track > 0 && c.Wagons.Any(w => w.Dest != c.Track) ? Color.FromArgb(255, 120, 100) : Color.FromArgb(120, 130, 140);
                if (it.ForeColor != fc) it.ForeColor = fc;
                Color bc = c == sim.SelectedCut ? Color.FromArgb(70, 70, 30) : c == t.Cuts.FirstOrDefault() && !t.Escort ? Color.FromArgb(35, 55, 80) : listok.BackColor;
                if (it.BackColor != bc) it.BackColor = bc;
            }
        }
        finally { listok.EndUpdate(); }
    }

    string CutStatus(Cut c)
    {
        string s = c.State switch
        {
            CutState.InTrain => sim.ListokTrain?.Cuts.FirstOrDefault() == c ? "первый к горбу" : "в составе",
            CutState.Rolling => c.Head < Lay.STrack ? $"скатывается {Kmh(c.V)} км/ч" : $"на пути {c.Track}, {Kmh(c.V)} км/ч",
            CutState.Stopped => c.Head < Lay.STrack ? "стоит в стрелочной зоне!" : $"на пути {c.Track}",
            CutState.Forced => "двигают локомотивом",
            CutState.Merged => c.Track > 0 ? $"сцеплен, путь {c.Track}" : "сцеплен",
            CutState.Gone => "выведен в парк отправления",
            _ => "",
        };
        return L.T(s);
    }

    /// <summary>Служебное: окно за краем экрана, роспуск с АРС на «Ж + З» — через seconds секунд снимок в файл.</summary>
    public void Shot(string path, double seconds)
    {
        timer.Stop();
        WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-6000, 0, 1600, 960);
        ShowInTaskbar = false;
        Show();
        chkArs.Checked = true;
        sim.SetPushRoute(1);
        sim.SetAspect(Aspect.YellowGreen);
        for (double t = 0; t < seconds; t += 0.05)
        {
            sim.Tick(0.05);
            var p = sim.Pushing;
            if (p != null && p.Cuts.Count > 0 && p.Cuts.All(c => c.Forbidden) && !p.Escort && p.Cuts[0].Head > -25)
            { sim.SetAspect(Aspect.Red); if (p.V == 0) { sim.StartEscort(); sim.SetAspect(Aspect.Yellow); } }
        }
        RefreshPanel();
        Application.DoEvents();
        LangUi.Apply(this, true);
        Application.DoEvents();
        using var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // =================================================================
    //  ИИ, ИНСТРУКЦИЯ
    // =================================================================
    void ShowAi(bool show)
    {
        if (IsDisposed) return;
        if (!show) { aiWindow?.Hide(); return; }
        if (aiWindow == null || aiWindow.IsDisposed)
        {
            aiWindow = new Ai.AiChatWindow();
            aiWindow.Unread += () => { if (!IsDisposed) btnAi.Text = "ИИ ●"; };
            aiWindow.PlaceNear(this);
        }
        if (!aiWindow.Visible) aiWindow.Show(this);
        aiWindow.Activate();
        btnAi.Text = "ИИ";
    }

    void OpenGuide()
    {
        // [ДОБАВЛЕНО 12.11.2026] на казахском — казахская инструкция (если её нет рядом — русская)
        var kk = Path.Combine(AppContext.BaseDirectory, "НҰСҚАУЛЫҚ.txt");
        var path = L.Current == "kk" && File.Exists(kk) ? kk : Path.Combine(AppContext.BaseDirectory, "ИНСТРУКЦИЯ.txt");
        if (!File.Exists(path)) { MessageBox.Show(L.T("Файл инструкции не найден") + "\n" + path, Text); return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
    }
}
