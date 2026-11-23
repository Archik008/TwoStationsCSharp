using System.Diagnostics;
using StationApp.Core;
using StationApp.Yard;

namespace StationApp.Ui;

// =====================================================================
//  ОКНО «СОРТИРОВОЧНАЯ» — ПУЛЬТ ДЕЖУРНОГО ПО ГОРКЕ  [ДОБАВЛЕНО 12.11.2026]
//  Слева — схема и вид горки сбоку (Ui\YardView.cs), под ними журнал.
//  Справа [ПЕРЕДЕЛАНО 17.11.2026] — «Что делать сейчас» (подсказка и главная кнопка), затем шаги по порядку работы:
//  1. Надвиг, 2. Горочный светофор, 3. Торможение (АРС или три кнопки), 4. Маршруты (ГАЦ или вручную),
//  сортировочный листок, маневры (только «Горка + маневры»), «Дополнительно» (свёрнуто), оценка.
//  Подписи, которые меняются по таймеру, ставятся через LangUi.SetText (иначе на kk / en мигают).
// =====================================================================

public sealed class YardForm : Form
{
    /// <summary>Кнопки и подписи внутри групп — обычным шрифтом (у GroupBox — жирный заголовок).</summary>
    static readonly Font BtnFont = new("Segoe UI", 9.5f);
    YardSim sim;
    readonly YardView view = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    double speed = 1;
    int seed = Environment.TickCount;

    // верх
    readonly Label lblClock = new() { AutoSize = true, ForeColor = Color.FromArgb(150, 210, 255), Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(10, 9, 6, 0) };
    readonly ComboBox cbVariant = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Margin = new Padding(6, 7, 6, 0) };
    readonly List<Button> speedButtons = new();
    Button btnAi;
    Ai.AiChatWindow aiWindow;

    // правая панель  [ПЕРЕДЕЛАНО 17.11.2026: сверху «Что делать сейчас», дальше шаги 1–4 по порядку работы,
    // редкие команды — в «Дополнительно», маневры — только в варианте «Горка + маневры»]
    readonly Label lblNow = new() { AutoSize = false, Height = 64, Dock = DockStyle.Top, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Padding = new Padding(4, 4, 4, 0) };
    readonly Label lblWarn = new() { AutoSize = false, Height = 36, Dock = DockStyle.Top, ForeColor = Color.FromArgb(255, 150, 120), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Visible = false };
    readonly Button btnNow = new();
    Action nowAct;
    readonly Panel nowPanel = new() { Dock = DockStyle.Top, Height = 134, Padding = new Padding(8, 6, 8, 8), Margin = new Padding(0, 0, 0, 6) };
    readonly GroupBox[] steps = new GroupBox[5];
    readonly Button[] arrBtns = new Button[Lay.ArrTracks + 1];
    readonly Label lblPush = new() { AutoSize = false, Height = 38, Dock = DockStyle.Top, ForeColor = Color.Gainsboro, Font = BtnFont };
    readonly Label lblAspect = new() { AutoSize = false, Height = 38, Dock = DockStyle.Top, ForeColor = Color.White, Font = BtnFont };
    readonly Button[] pushBtns = new Button[4];
    static readonly Aspect[] PushAspects = { Aspect.Red, Aspect.Yellow, Aspect.YellowGreen, Aspect.Green };
    readonly CheckBox chkArs = new() { Text = "Тормозить автоматически (АРС) — рекомендуется", AutoSize = true, ForeColor = Color.White, Checked = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    /// <summary>Ручное торможение — три кнопки: «Отпустить», «Слабо», «Сильно» (ступени 0, 2, 4).</summary>
    static readonly int[] Levels = { 0, 2, 4 };
    static readonly string[] LevelNames = { "Отпустить", "Слабо", "Сильно" };
    readonly Button[,] retBtns = new Button[3, 3];
    readonly Label[] retInfo = new Label[3];
    readonly RadioButton rbGac = new() { Text = "Сами по листку (ГАЦ)", AutoSize = true, ForeColor = Color.White, Checked = true, Margin = new Padding(3, 6, 10, 0) };
    readonly RadioButton rbManual = new() { Text = "Вручную (кнопки путей)", AutoSize = true, ForeColor = Color.White, Margin = new Padding(3, 6, 3, 0) };
    readonly Button[] routeBtns = new Button[9];
    readonly Label lblNext = new() { AutoSize = false, Height = 38, Dock = DockStyle.Top, ForeColor = Color.Gainsboro, Font = BtnFont };
    readonly Label lblListok = new() { AutoSize = false, Height = 20, Dock = DockStyle.Top, ForeColor = Color.White, Font = BtnFont };
    readonly ListView listok = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Top, Height = 214, Tag = "nolang", HideSelection = false, BackColor = Color.FromArgb(24, 28, 33), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.FixedSingle };
    readonly Button btnEscort = new(), btnBack = new(), btnLocoTo = new(), btnLocoBack = new(), btnForm = new(), btnCompact = new(), btnStuck = new();
    readonly Button btnExtra = new();
    GroupBox gExtra, gMan;
    readonly Label lblScore = new() { AutoSize = false, Height = 58, Dock = DockStyle.Top, ForeColor = Color.White, Font = BtnFont };
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
        return new GroupBox { Text = text, Dock = DockStyle.Top, Height = height, ForeColor = StepColor, BackColor = Grp, Padding = new Padding(6, 4, 6, 4), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    }

    static readonly Color StepColor = Color.FromArgb(150, 200, 255), StepHot = Color.Gold, GrpHot = Color.FromArgb(52, 50, 34);

    FlowLayoutPanel Row(int h = 36) => new() { Dock = DockStyle.Top, Height = h, WrapContents = false, Margin = Padding.Empty };


    void BuildRight()
    {
        var right = new Panel { Dock = DockStyle.Right, Width = 480, AutoScroll = true, BackColor = Panel, Padding = new Padding(4) };
        var items = new List<Control>();

        // ---- что делать сейчас ----
        nowPanel.BackColor = Color.FromArgb(58, 50, 18);
        var nowTitle = new Label { Text = "ЧТО ДЕЛАТЬ СЕЙЧАС", Dock = DockStyle.Top, Height = 20, ForeColor = Color.Gold, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        btnNow.Dock = DockStyle.Top;
        btnNow.Height = 34;
        btnNow.FlatStyle = FlatStyle.Flat;
        btnNow.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        btnNow.ForeColor = Color.Black;
        btnNow.BackColor = Color.Gold;
        btnNow.FlatAppearance.BorderColor = Color.White;
        btnNow.Click += (_, _) => { nowAct?.Invoke(); RefreshPanel(); };
        tips.SetToolTip(btnNow, "Главная кнопка: делает то, что нужно сделать сейчас (то же можно сделать и кнопками ниже)");
        nowPanel.Controls.Add(btnNow);
        nowPanel.Controls.Add(lblWarn);
        nowPanel.Controls.Add(lblNow);
        nowPanel.Controls.Add(nowTitle);
        items.Add(nowPanel);

        // ---- шаг 1: надвиг ----
        var g1 = steps[1] = Group("Шаг 1. Надвиг состава на горку", 108);
        var r1 = Row(40);
        r1.Controls.Add(new Label { Text = "С пути парка приёма:", AutoSize = true, ForeColor = Color.Gainsboro, Font = BtnFont, Margin = new Padding(3, 11, 3, 0) });
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            int aa = a;
            var b = Btn("П" + a, () => sim.SetPushRoute(aa), 58);
            b.Height = 34;
            b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            tips.SetToolTip(b, $"Задать маршрут надвига с пути П{a}: горочный локомотив покатит состав к горбу. Зелёная кнопка — состав готов");
            arrBtns[a] = b;
            r1.Controls.Add(b);
        }
        g1.Controls.Add(lblPush);
        g1.Controls.Add(r1);
        items.Add(g1);

        // ---- шаг 2: горочный светофор ----
        var g2 = steps[2] = Group("Шаг 2. Горочный светофор Г (скорость роспуска)", 108);
        var r2 = Row(40);
        var defs = new (string t, Color c, Color f, string tip)[]
        {
            ("■ СТОП", Color.FromArgb(170, 35, 30), Color.White, "Красный: стой, роспуск запрещён (клавиша Esc)"),
            ("Медленно", Color.FromArgb(200, 160, 20), Color.Black, "Жёлтый: роспуск 3 км/ч — для длинных отцепов и плохих бегунов"),
            ("Обычно", Color.FromArgb(140, 170, 40), Color.Black, "Жёлтый с зелёным: роспуск 4,5 км/ч — обычный режим"),
            ("Быстро", Color.FromArgb(30, 150, 60), Color.White, "Зелёный: роспуск 6 км/ч — только для крупных отцепов, иначе отцепы догоняют друг друга"),
        };
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            var asp = PushAspects[i];
            pushBtns[i] = Btn(d.t, () => sim.SetAspect(asp), 100, d.c, d.f);
            pushBtns[i].Height = 34;
            pushBtns[i].Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            tips.SetToolTip(pushBtns[i], d.tip);
            r2.Controls.Add(pushBtns[i]);
        }
        g2.Controls.Add(lblAspect);
        g2.Controls.Add(r2);
        items.Add(g2);

        // ---- шаг 3: торможение ----
        var g3 = steps[3] = Group("Шаг 3. Торможение отцепов (замедлители)", 222);
        chkArs.Dock = DockStyle.Top;
        chkArs.CheckedChanged += (_, _) => { sim.SetArs(chkArs.Checked); RefreshPanel(); };
        tips.SetToolTip(chkArs, "Галочка стоит — замедлители тормозят сами. Снимите — тормозите кнопками; жёлтая рамка подсказывает, какую нажать");
        for (int i = 2; i >= 0; i--)
        {
            int ri = i;
            var row = Row(46);
            row.Controls.Add(new Label { Text = new[] { "1ТП", "2ТП-А", "2ТП-Б" }[i], Width = 50, ForeColor = Color.White, Font = BtnFont, Margin = new Padding(3, 12, 0, 0) });
            for (int s = 0; s < 3; s++)
            {
                int ss = s;
                var b = Btn(LevelNames[s], () => sim.SetRetarder(ri, Levels[ss]), 76);
                b.Font = BtnFont;
                tips.SetToolTip(b, new[] { "Отпустить замедлитель — отцеп катится без торможения", "Тормозить слабо (ступень 2)", "Тормозить сильно (ступень 4)" }[s]);
                retBtns[i, s] = b;
                row.Controls.Add(b);
            }
            retInfo[i] = new Label { AutoSize = false, Width = 136, Height = 44, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 8.5f), Margin = new Padding(4, 2, 0, 0) };
            row.Controls.Add(retInfo[i]);
            g3.Controls.Add(row);
        }
        g3.Controls.Add(new Label { Text = "1ТП — для всех путей, 2ТП-А — для путей 1–4, 2ТП-Б — для путей 5–8", Dock = DockStyle.Top, Height = 18, ForeColor = Color.FromArgb(150, 157, 166), Font = new Font("Segoe UI", 8.5f) });
        g3.Controls.Add(chkArs);
        items.Add(g3);

        // ---- шаг 4: маршруты ----
        var g4 = steps[4] = Group("Шаг 4. На какой путь идёт отцеп (маршрут)", 236);
        var mode = Row(30);
        rbGac.Font = BtnFont; rbManual.Font = BtnFont;
        mode.Controls.Add(rbGac);
        mode.Controls.Add(rbManual);
        rbGac.CheckedChanged += (_, _) => { if (sim != null && sim.GacMode != rbGac.Checked) { sim.SetGac(rbGac.Checked); RefreshPanel(); } };
        tips.SetToolTip(rbGac, "ГАЦ сама берёт маршруты из сортировочного листка и переводит стрелки");
        tips.SetToolTip(rbManual, "Перед каждым отцепом нажмите кнопку его пути (или клавишу 1–8). Стрелки всё равно переведёт ГАЦ");
        var rr2 = Row(48);
        var rr1 = Row(48);
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            int kk = k;
            var b = Btn("", () => sim.RouteButton(kk), 100);
            b.Tag = "nolang";                 // подпись «номер · направление» ставит RouteTexts (переводится по частям)
            b.Height = 42;
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            tips.SetToolTip(b, $"Путь {k} — {YardSim.TrackNames[k]} (клавиша {k})");
            routeBtns[k] = b;
            (k <= 4 ? rr1 : rr2).Controls.Add(b);
        }
        var rr3 = Row(36);
        var undo = Btn("Сбросить последний маршрут", () => sim.UndoRoute());
        undo.Font = BtnFont;
        rr3.Controls.Add(undo);
        g4.Controls.Add(lblNext);
        g4.Controls.Add(rr3);
        g4.Controls.Add(rr2);
        g4.Controls.Add(rr1);
        g4.Controls.Add(mode);
        items.Add(g4);
        RouteTexts();

        // ---- листок ----
        var gL = Group("Сортировочный листок", 262);
        foreach (var c in ListokCols) listok.Columns.Add(L.T(c));
        int[] widths = { 26, 34, 72, 40, 42, 38, 44, 36, 128 };
        for (int i = 0; i < widths.Length; i++) listok.Columns[i].Width = widths[i];
        listok.Font = BtnFont;
        listok.SelectedIndexChanged += (_, _) =>
        {
            if (listok.SelectedItems.Count == 1 && listok.SelectedItems[0].Tag is Cut c) sim.SelectedCut = c.State == CutState.InTrain ? c : null;
        };
        tips.SetToolTip(listok, "Выберите отцеп и нажмите кнопку пути — изменить его маршрут");
        gL.Controls.Add(listok);
        gL.Controls.Add(lblListok);
        items.Add(gL);

        // ---- маневры (только «Горка + маневры») ----
        gMan = Group("Маневры (вариант «Горка + маневры»)", 116);
        var m1 = Row(); var m2 = Row();
        foreach (var (b, t, tip) in new[]
        {
            (btnLocoTo, "Горочный локомотив под состав…", "Горочный локомотив по обходному пути в хвост состава, который прошёл осмотр"),
            (btnLocoBack, "Вернуть горочный локомотив", "После роспуска — с горба на стоянку"),
            (btnForm, "Вывести состав…", "Набранный состав с сортировочного пути — в парк отправления"),
        })
        {
            b.Text = t;
            Style(b);
            tips.SetToolTip(b, tip);
        }
        btnLocoTo.Click += (_, _) => Menu(btnLocoTo, Enumerable.Range(1, Lay.ArrTracks).Where(a => sim.Arr[a].State == ArrState.WaitLoco).Select(a => ("П" + a, (Action)(() => sim.SendHumpLoco(a)))));
        btnLocoBack.Click += (_, _) => { sim.ReturnHumpLoco(); RefreshPanel(); };
        btnForm.Click += (_, _) => Menu(btnForm, Enumerable.Range(1, Lay.Tracks).Where(k => sim.OnTrack(k).Any()).Select(k => ($"Путь {k} — {sim.OnTrack(k).Sum(c => c.Wagons.Count)} ваг.", (Action)(() => sim.FormTrain(k)))));
        m1.Controls.Add(btnLocoTo); m1.Controls.Add(btnLocoBack);
        m2.Controls.Add(btnForm);
        gMan.Controls.Add(m2);
        gMan.Controls.Add(m1);
        gMan.Visible = false;
        items.Add(gMan);

        // ---- дополнительно (свёрнуто) ----
        btnExtra.Text = "▸ Дополнительные команды";
        Style(btnExtra);
        btnExtra.AutoSize = false;
        btnExtra.Dock = DockStyle.Top;
        btnExtra.Height = 30;
        btnExtra.TextAlign = ContentAlignment.MiddleLeft;
        btnExtra.Click += (_, _) => { gExtra.Visible = !gExtra.Visible; LangUi.SetText(btnExtra, gExtra.Visible ? "▾ Дополнительные команды" : "▸ Дополнительные команды"); };
        items.Add(btnExtra);
        gExtra = Group("Дополнительно", 116);
        var e1 = Row(); var e2 = Row();
        foreach (var (b, t, tip) in new[]
        {
            (btnEscort, "Спустить с локомотивом", "Вагоны «Л» (люди) и «Е» (газ) с горки распускать нельзя: остановите надвиг перед горбом и спустите их локомотивом"),
            (btnBack, "Осадить назад (К + «Н»)", "Красный с белой буквой «Н»: вагоны с горки — назад в парк приёма"),
            (btnStuck, "Протолкнуть отцеп", "Отцеп остановился в стрелочной зоне — протолкнуть его на путь (сначала остановите роспуск)"),
            (btnCompact, "Сжать путь…", "Закрыть «окна» после недоката: вагоны на пути осаживаются к концу пути"),
        })
        {
            b.Text = t;
            Style(b);
            tips.SetToolTip(b, tip);
        }
        btnEscort.Click += (_, _) => { if (sim.StartEscort() && sim.Hump == Aspect.Red) sim.SetAspect(Aspect.Yellow); RefreshPanel(); };
        btnBack.Click += (_, _) => { sim.SetAspect(Aspect.RedN); RefreshPanel(); };
        btnStuck.Click += (_, _) => { sim.PushStuck(); RefreshPanel(); };
        btnCompact.Click += (_, _) => Menu(btnCompact, Enumerable.Range(1, Lay.Tracks).Where(k => sim.OnTrack(k).Any()).Select(k => ($"Путь {k}", (Action)(() => sim.CompactTrack(k)))));
        e1.Controls.Add(btnEscort); e1.Controls.Add(btnBack);
        e2.Controls.Add(btnStuck); e2.Controls.Add(btnCompact);
        gExtra.Controls.Add(e2);
        gExtra.Controls.Add(e1);
        gExtra.Visible = false;
        items.Add(gExtra);

        var g6 = Group("Оценка смены", 82);
        g6.Controls.Add(lblScore);
        items.Add(g6);

        // Dock = Top складывает снизу вверх: добавляем в обратном порядке
        for (int i = items.Count - 1; i >= 0; i--) right.Controls.Add(items[i]);
        Controls.Add(right);
    }

    void RouteTexts()
    {
        for (int k = 1; k <= Lay.Tracks; k++) routeBtns[k].Text = $"{k} · " + L.T(YardSim.TrackNames[k].Split(' ')[0]);
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
        sim.GacMode = rbGac.Checked;
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
        Note("Смена началась. Поезд на пути П1 готов к роспуску. Делайте то, что написано в жёлтом блоке «Что делать сейчас» справа", false);
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
        RouteTexts();
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

        // что делать сейчас
        var (now, btn, act, step, urgent) = Advice();
        LangUi.SetText(lblNow, now);
        // заранее: следующий за первым отцеп — «Л» / «Е»
        var pp = sim.Pushing;
        string warn = pp != null && !pp.Escort && pp.Cuts.Count > 1 && pp.Cuts[1].Forbidden && !pp.Cuts[0].Forbidden
            ? $"Внимание: за отцепом {pp.Cuts[0].No} идёт «{pp.Cuts[1].Mark}» (его нельзя с горки) — как только отцеп {pp.Cuts[0].No} уйдёт, нажмите «СТОП»" : "";
        LangUi.SetText(lblWarn, warn);
        lblWarn.Visible = warn != "";
        nowPanel.Height = warn != "" ? 170 : 134;
        nowAct = act;
        btnNow.Visible = btn != null;
        if (btn != null) LangUi.SetText(btnNow, btn);
        bool blink = Environment.TickCount / 400 % 2 == 0;
        nowPanel.BackColor = urgent ? (blink ? Color.FromArgb(120, 30, 25) : Color.FromArgb(80, 24, 20)) : Color.FromArgb(58, 50, 18);
        btnNow.BackColor = urgent ? Color.FromArgb(255, 120, 90) : Color.Gold;
        for (int i = 1; i <= 4; i++)
        {
            steps[i].ForeColor = i == step ? StepHot : StepColor;
            steps[i].BackColor = i == step ? GrpHot : Grp;
        }

        // шаг 1: надвиг
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            var st = sim.Arr[a].State;
            arrBtns[a].BackColor = st == ArrState.Ready && sim.Pushing == null ? Color.FromArgb(30, 140, 60)
                : sim.Pushing?.Arr == a ? Color.FromArgb(0, 105, 190) : Color.FromArgb(55, 64, 76);
        }
        string push;
        if (sim.Pushing is Train t)
        {
            int left = t.Cuts.Sum(c => c.Wagons.Count);
            push = t.Escort
                ? (t.Returning ? "Горочный локомотив возвращается на горб" : $"Спуск с локомотивом: {left} ваг., {Kmh(t.V)} км/ч")
                : $"Надвиг поезда {t.Name} с пути П{t.Arr}: {Kmh(t.V)} км/ч, в составе осталось {left} ваг.";
        }
        else
        {
            var ready = sim.Arr.Skip(1).Where(a => a.State == ArrState.Ready).Select(a => "П" + a.No).ToList();
            push = ready.Count > 0 ? $"Готовы к роспуску (зелёные): {string.Join(", ", ready)}" : $"Нет состава, готового к роспуску. Горочный локомотив: {LocoText()}";
        }
        LangUi.SetText(lblPush, push);

        // шаг 2: светофор
        LangUi.SetText(lblAspect, $"Сейчас: {L.T(YardSim.AspectName(sim.Hump))}");
        lblAspect.ForeColor = sim.Hump switch { Aspect.Green => Color.FromArgb(90, 230, 120), Aspect.YellowGreen => Color.FromArgb(200, 230, 90), Aspect.Yellow => Color.Gold, _ => Color.FromArgb(255, 110, 100) };
        for (int i = 0; i < pushBtns.Length; i++)
        {
            bool on = sim.Hump == PushAspects[i] || (i == 0 && sim.Hump == Aspect.RedN);
            pushBtns[i].FlatAppearance.BorderSize = on ? 3 : 1;
            pushBtns[i].FlatAppearance.BorderColor = on ? Color.White : Color.FromArgb(80, 90, 104);
        }

        // шаг 3: замедлители
        for (int i = 0; i < 3; i++)
        {
            var r = sim.Retarders[i];
            int cur = r.Step == 0 ? 0 : r.Step <= 2 ? 1 : 2;
            int adv = r.Advice == 0 ? 0 : r.Advice <= 2 ? 1 : 2;
            bool hint = !sim.Ars && r.CutOn && adv != cur;
            for (int s = 0; s < 3; s++)
            {
                var b = retBtns[i, s];
                b.BackColor = cur == s ? (s == 0 ? Color.FromArgb(90, 110, 130) : Color.FromArgb(230, s == 1 ? 170 : 110, 40)) : Color.FromArgb(55, 64, 76);
                b.ForeColor = cur == s && s > 0 ? Color.Black : Color.White;
                b.Enabled = !sim.Ars;
                b.FlatAppearance.BorderSize = hint && adv == s ? 3 : 1;
                b.FlatAppearance.BorderColor = hint && adv == s ? (blink ? Color.Gold : Color.Orange) : Color.FromArgb(80, 90, 104);
            }
            string info = r.Cut == null ? "свободен"
                : hint ? $"отцеп {r.Cut.No}: {Kmh(r.Cut.V)}, нужно {Kmh(r.Target)}\nнажмите «{L.T(LevelNames[adv])}»"
                : r.CutOn ? $"отцеп {r.Cut.No}: {Kmh(r.Cut.V)} км/ч\nнужно не больше {Kmh(r.Target)}"
                : $"подходит {r.Cut.No}: {Kmh(r.Cut.V)} км/ч\nнужно не больше {Kmh(r.Target)}";
            LangUi.SetText(retInfo[i], info);
            retInfo[i].ForeColor = r.CutOn && r.Cut.V > r.Target + 0.4 ? Color.FromArgb(255, 170, 90) : Color.Gainsboro;
        }

        // шаг 4: маршруты
        if (rbGac.Checked != sim.GacMode) { rbGac.Checked = sim.GacMode; rbManual.Checked = !sim.GacMode; }
        var lt = sim.ListokTrain;
        var next = lt?.AllCuts.FirstOrDefault(c => c.State == CutState.InTrain && c.Route == 0);
        string nx = sim.SelectedCut != null && sim.SelectedCut.State == CutState.InTrain
            ? $"Выбран отцеп {sim.SelectedCut.No} (по листку — путь {sim.SelectedCut.Dest}): нажмите кнопку пути"
            : next != null ? $"Следующий без маршрута: отцеп {next.No} — по листку путь {next.Dest}. Нажмите кнопку «{next.Dest}»"
            : sim.GacMode ? "ГАЦ ставит маршруты сама. Чтобы изменить — выберите отцеп в листке и нажмите путь" : "Маршруты всем отцепам заданы";
        LangUi.SetText(lblNext, nx);
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            bool hot = (sim.SelectedCut?.Dest ?? next?.Dest) == k;
            routeBtns[k].BackColor = hot ? Color.FromArgb(40, 120, 200) : Color.FromArgb(55, 64, 76);
            routeBtns[k].FlatAppearance.BorderSize = hot ? 3 : 1;
            routeBtns[k].FlatAppearance.BorderColor = hot ? Color.White : Color.FromArgb(80, 90, 104);
        }

        RefreshListok();

        // маневры и дополнительно
        bool man = !sim.AutoOps;
        if (gMan.Visible != man) gMan.Visible = man;
        btnLocoTo.Enabled = man && sim.Loco == HumpLocoState.Standby;
        btnLocoBack.Enabled = man && sim.Loco == HumpLocoState.AtHorb;
        btnForm.Enabled = man;
        btnEscort.Enabled = sim.Pushing != null && !sim.Pushing.Escort;
        btnBack.Enabled = sim.Pushing != null && !sim.Pushing.Escort;
        btnStuck.Enabled = sim.Cuts.Any(c => c.State == CutState.Stopped && c.Head < Lay.STrack);

        LangUi.SetText(lblScore, $"Оценка: {sim.Grade} ({sim.Score} баллов из 100) · нарушений: {sim.Violations.Count}\n" +
                                 $"Распущено: {sim.Rolled} ваг., {sim.CutsRolled} отцепов (на свой путь — {sim.CorrectCuts})\nОтправлено поездов: {sim.Departed}");
        lblScore.ForeColor = sim.Score >= 90 ? Color.FromArgb(120, 230, 140) : sim.Score >= 75 ? Color.Gold : Color.FromArgb(255, 120, 100);
    }

    /// <summary>[ДОБАВЛЕНО 17.11.2026] «Что делать сейчас»: подсказка, главная кнопка, номер шага (подсвечивается), срочно ли.</summary>
    (string text, string btn, Action act, int step, bool urgent) Advice()
    {
        var p = sim.Pushing;
        Action stop = () => sim.SetAspect(Aspect.Red);
        var stuck = sim.Cuts.FirstOrDefault(c => c.State == CutState.Stopped && c.Head < Lay.STrack);
        if (stuck != null)
            return p != null && p.V > 0.05 && !p.Escort
                ? ($"Отцеп {stuck.No} остановился в стрелочной зоне! Сначала остановите роспуск", "■ СТОП (красный)", stop, 2, true)
                : ($"Отцеп {stuck.No} стоит в стрелочной зоне — протолкните его на путь", "Протолкнуть отцеп", () => sim.PushStuck(), 0, true);

        if (p == null)
        {
            var ready = sim.Arr.Skip(1).FirstOrDefault(a => a.State == ArrState.Ready && a.Train != null);
            if (ready != null)
                return ($"Поезд {ready.Train.Name} ({ready.Train.WagonCount} ваг.) на пути П{ready.No} готов. Задайте надвиг — кнопка «П{ready.No}»", $"▶ Надвиг с пути П{ready.No}", () => sim.SetPushRoute(ready.No), 1, false);
            if (!sim.AutoOps)
            {
                if (sim.Loco == HumpLocoState.AtHorb) return ("Роспуск окончен. Верните горочный локомотив на стоянку", "Вернуть горочный локомотив", () => sim.ReturnHumpLoco(), 0, false);
                var wait = sim.Arr.Skip(1).FirstOrDefault(a => a.State == ArrState.WaitLoco);
                if (wait != null && sim.Loco == HumpLocoState.Standby)
                    return ($"Состав на пути П{wait.No} осмотрен. Подайте горочный локомотив в хвост состава", $"Локомотив под состав П{wait.No}", () => sim.SendHumpLoco(wait.No), 0, false);
                for (int k = 1; k <= Lay.Tracks; k++)
                    if (sim.OnTrack(k).Sum(c => c.Wagons.Count) >= YardSim.Norm(k) && !sim.ShuntBusy)
                    { int kk = k; return ($"На пути {k} набран состав ({sim.OnTrack(k).Sum(c => c.Wagons.Count)} ваг.) — выведите его в парк отправления", $"Вывести состав с пути {k}", () => sim.FormTrain(kk), 0, false); }
            }
            return (sim.Arr.Skip(1).Any(a => a.State != ArrState.Free)
                ? "Ждите: поезд в парке приёма ещё готовится к роспуску (приём, осмотр, горочный локомотив) — смотрите подписи у путей П1–П4"
                : "Ждите: парк приёма пуст, поезд на подходе. Можно ускорить время кнопкой «×4»", null, null, 0, false);
        }

        if (p.Escort)
        {
            if (p.Returning) return ("Горочный локомотив возвращается на горб", null, null, 0, false);
            if (sim.Hump is Aspect.Red or Aspect.RedN)
                return ("Спуск с локомотивом готов. Откройте светофор — «Медленно»", "▶ Медленно (жёлтый)", () => sim.SetAspect(Aspect.Yellow), 2, false);
            return ("Вагоны спускаются с горки локомотивом (не быстрее 5 км/ч)", null, null, 0, false);
        }

        var front = p.Cuts.FirstOrDefault();
        if (front != null && front.Forbidden)
        {
            if (p.V > 0.05 || sim.Hump != Aspect.Red)
                return ($"Впереди отцеп {front.No} «{front.Mark}» ({L.T(front.Wagons[0].Kind.Name)}) — с горки его распускать нельзя! Остановите надвиг", "■ СТОП (красный)", stop, 2, true);
            return ($"Состав стоит. Отцеп {front.No} «{front.Mark}» спустите с горки локомотивом", "Спустить с локомотивом", () => { if (sim.StartEscort()) sim.SetAspect(Aspect.Yellow); }, 0, true);
        }

        if (!sim.GacMode)
        {
            var next = p.Cuts.Take(2).FirstOrDefault(c => c.Route == 0);
            if (next != null)
                return ($"Задайте маршрут отцепу {next.No}: по листку — путь {next.Dest}.", $"Путь {next.Dest} для отцепа {next.No}", () => sim.RouteButton(next.Dest), 4, true);
        }
        if (sim.Hump == Aspect.Red && p.V < 0.05)
            return ("Маршрут надвига задан. Откройте горочный светофор — состав пойдёт на горку.", "▶ Открыть: обычная скорость", () => sim.SetAspect(Aspect.YellowGreen), 2, false);
        if (!sim.Ars)
        {
            var r = sim.Retarders.FirstOrDefault(x => x.CutOn && x.Cut.V > x.Target + 0.3);
            if (r != null)
                return ($"Тормозите на {r.Name}: отцеп {r.Cut.No} идёт {Kmh(r.Cut.V)} км/ч, нужно не больше {Kmh(r.Target)}. Жмите кнопку в жёлтой рамке.", null, null, 3, true);
        }
        string run = sim.Ars ? (sim.GacMode ? "Идёт роспуск: отцепы скатываются сами, АРС тормозит, ГАЦ ставит маршруты. Следите за схемой"
                                            : "Идёт роспуск: АРС тормозит, маршруты задаёте вы. Следите за схемой")
                             : (sim.GacMode ? "Идёт роспуск: ГАЦ ставит маршруты, тормозите вы — смотрите на скорость отцепов у замедлителей"
                                            : "Идёт роспуск: маршруты и торможение — ваши. Следите за схемой");
        return (run, "■ СТОП (красный)", stop, 0, false);
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
    public void Shot(string path, double seconds, bool manual = false)
    {
        timer.Stop();
        WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-6000, 0, 1600, 960);
        ShowInTaskbar = false;
        Show();
        chkArs.Checked = !manual;
        if (manual) rbManual.Checked = true;
        sim.SetPushRoute(1);
        if (manual) for (int k = 0; k < 2; k++) sim.RouteButton(sim.ListokTrain.AllCuts.First(c => c.Route == 0).Dest);
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
        // [ИЗМЕНЕНО 23.11.2026] на английском — INSTRUCTION.txt
        var own = Path.Combine(AppContext.BaseDirectory, L.Current switch { "kk" => "НҰСҚАУЛЫҚ.txt", "en" => "INSTRUCTION.txt", _ => "ИНСТРУКЦИЯ.txt" });
        var path = File.Exists(own) ? own : Path.Combine(AppContext.BaseDirectory, "ИНСТРУКЦИЯ.txt");
        if (!File.Exists(path)) { MessageBox.Show(L.T("Файл инструкции не найден") + "\n" + path, Text); return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
    }
}
