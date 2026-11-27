using System.Diagnostics;
using StationApp.Core;
using StationApp.Yard;

using Aspect = StationApp.Yard.Aspect;
using Wagon = StationApp.Yard.Wagon;

namespace StationApp.Ui;

// =====================================================================
//  ОКНО «СОРТИРОВОЧНАЯ» — ПУЛЬТ ДЕЖУРНОГО ПО ГОРКЕ  [ДОБАВЛЕНО 12.11.2026]
//  Слева — схема и вид горки сбоку (Ui\YardView.cs), под ними журнал.
//  Справа [ПЕРЕДЕЛАНО 17.11.2026] — «Что делать сейчас» (подсказка и главная кнопка), затем шаги по порядку работы:
//  1. Надвиг, 2. Горочный светофор, 3. Торможение (АРС или три кнопки), 4. Маршруты (ГАЦ или вручную),
//  сортировочный листок, маневры (только «Горка + маневры»), «Дополнительно» (свёрнуто), оценка.
//  Подписи, которые меняются по таймеру, ставятся через LangUi.SetText (иначе на kk / en мигают).
//  [ДОБАВЛЕНО 26.11.2026] Под верхней строкой — варианты кнопками (Только горка, Горка + маневры, Учебный, Демонстрация,
//  Экзамен) и «Задания…» (урок: цели, время, итог в файл). В «Учебном» и «Экзамене» справа — «Приём, башмаки,
//  отправление»; в «Экзамене» подсказок нет; в «Демонстрации» пульт ведёт автопилот.
// =====================================================================

public sealed class YardForm : Form
{
    // [ДОБАВЛЕНО 24.11.2026] ИИ-помощник и инструкция — через крючки: «Сортировочная» подключает свой ИИ (Program.cs),
    // «Станция по схеме» (горка из файла редактора) — без ИИ: у неё свой помощник для станций ДСП
    public static Action<YardSim> AiSim;
    public static Action<string> AiPoint;
    public static Action<string, bool> AiNote;
    public static bool AiEnabled = true;
    /// <summary>Где искать ИНСТРУКЦИЯ.txt (по умолчанию — рядом с программой).</summary>
    public static string GuideDir = AppContext.BaseDirectory;
    /// <summary>Свои кнопки в верхней строке (перед «Инструкция»): текст и что делать.</summary>
    public static readonly List<(string Text, Action<Form> Click)> TopButtons = new();
    /// <summary>Что сказать в журнале в начале смены (замечания к горке из файла).</summary>
    public static readonly List<string> StartNotes = new();
    /// <summary>[26.11.2026] Вариант при запуске (служебное: --mode для снимков).</summary>
    public static YardMode StartupMode = YardMode.Hump;
    /// <summary>[26.11.2026] Задание при запуске (Sortirovka.exe --task exam — сразу урок; удобно преподавателю).</summary>
    public static YardTask StartupTask;
    /// <summary>Снимок без окна: итог задания не показывается окном (только файл).</summary>
    bool headless;
    /// <summary>Файл итога последнего задания.</summary>
    public string LastReport;
    /// <summary>Служебное: снимок окна «Задания» в файл.</summary>
    public static string TasksShot;
    public static int TasksShotIndex;
    public void ShotTasks() { CreateControl(); OpenTasks(); }

    /// <summary>Кнопки и подписи внутри групп — обычным шрифтом (у GroupBox — жирный заголовок).</summary>
    static readonly Font BtnFont = new("Segoe UI", 9.5f);
    YardSim sim;
    readonly YardView view = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    double speed = 1;
    int seed = Environment.TickCount;

    // верх
    readonly Label lblClock = new() { AutoSize = true, ForeColor = Color.FromArgb(150, 210, 255), Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(10, 9, 6, 0) };
    // [ДОБАВЛЕНО 26.11.2026] варианты и задания
    YardMode mode = YardMode.Hump;
    readonly Dictionary<YardMode, Button> modeBtns = new();
    static readonly (YardMode m, string text, string tip)[] Variants =
    {
        (YardMode.Hump, "Только горка", "Приём, осмотр, горочный локомотив, вывод и отправление составов — сами; вы — надвиг и роспуск"),
        (YardMode.HumpShunt, "Горка + маневры", "Вы ещё подаёте горочный локомотив под состав, возвращаете его и выводите составы в парк отправления"),
        (YardMode.Training, "Учебный", "Всё делаете сами: приём поезда, горочный локомотив, надвиг, маршруты, торможение, башмаки, вывод и отправление. Программа проверяет каждый шаг и подсказывает"),
        (YardMode.Demo, "Демонстрация", "Всё делает автопилот, в журнале — что и почему. Смотрите, как должно быть"),
        (YardMode.Exam, "Экзамен", "Как «Учебный», но без подсказок; оценка — по нарушениям"),
    };
    YardTask task;
    bool taskOver;
    string student = "";
    Button btnTasks, btnTaskEnd;
    readonly Label lblTask = new() { AutoSize = true, ForeColor = Color.Gold, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(8, 9, 6, 0), MaximumSize = new Size(900, 0) };
    bool arsLocked, gacLocked, syncing;
    // приём, башмаки, отправление (Учебный / Экзамен)
    GroupBox gOps;
    readonly Label lblApproach = new() { AutoSize = false, Height = 22, Dock = DockStyle.Top, ForeColor = Color.White, Font = BtnFont };
    readonly Button[] accBtns = new Button[Lay.ArrTracks + 1];
    readonly Button[] shoeBtns = new Button[Lay.Tracks + 1];
    readonly Label[] depInfo = new Label[Lay.DepTracks + 1];
    readonly Button[] depBtns = new Button[Lay.DepTracks + 1];
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
    // [ИЗМЕНЕНО 24.11.2026] столько строк, сколько тормозных позиций у горки (у встроенной — 3), кнопок путей — сколько путей
    readonly Button[,] retBtns = new Button[Lay.Cur.RetDefs.Count, 3];
    readonly Label[] retInfo = new Label[Lay.Cur.RetDefs.Count];
    readonly RadioButton rbGac = new() { Text = "Сами по листку (ГАЦ)", AutoSize = true, ForeColor = Color.White, Checked = true, Margin = new Padding(3, 6, 10, 0) };
    readonly RadioButton rbManual = new() { Text = "Вручную (кнопки путей)", AutoSize = true, ForeColor = Color.White, Margin = new Padding(3, 6, 3, 0) };
    readonly Button[] routeBtns = new Button[Lay.Tracks + 1];
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
        if (Lay.Cur.Title.Length > 0) Tag = "keeptext";   // название горки из файла не переводится (заголовок переводит OnLang)
        Text = Lay.Cur.Title.Length > 0 ? Lay.Cur.Title + " — " + L.T("пульт дежурного по горке (ДСПГ)") : L.T("Сортировочная — пульт дежурного по горке (ДСПГ)");
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1600, 980);
        MinimumSize = new Size(1100, 700);
        WindowState = FormWindowState.Maximized;
        BackColor = Panel;
        KeyPreview = true;
        Font = new Font("Segoe UI", 9.5f);

        mode = StartupMode;
        task = StartupTask;
        BuildTop();
        BuildRight();
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(view);
        left.Controls.Add(journal);
        Controls.Add(left);
        left.BringToFront();

        journal.DrawItem += DrawJournal;
        view.Pointed += id => AiPoint?.Invoke(id);
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
        top.Controls.Add(new Label { Text = Lay.Cur.Title.Length > 0 ? Lay.Cur.Title : "Сортировочная — пульт ДСПГ", Tag = Lay.Cur.Title.Length > 0 ? "nolang" : null, AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(6, 9, 6, 0) });
        top.Controls.Add(lblClock);
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
        foreach (var (text, click) in TopButtons) { var f = this; top.Controls.Add(Btn(text, () => click(f))); }   // [ДОБАВЛЕНО 24.11.2026] «Станция по схеме»: «Схема…»
        top.Controls.Add(Btn("Инструкция", OpenGuide));
        // [ДОБАВЛЕНО 26.11.2026] окно «Разработчики» — кто сделал программу (общее для всех программ)
        var btnDevs = Btn("Разработчики", () => StationApp.Common.DevelopersForm.Open(this, Text, L.Current));
        tips.SetToolTip(btnDevs, "Кто сделал программу");
        top.Controls.Add(btnDevs);
        var lang = LangUi.Selector();
        top.Controls.Add(lang);
        btnAi = Btn("ИИ", () => ShowAi(aiWindow == null || !aiWindow.Visible), 60, Color.FromArgb(0, 105, 190));
        tips.SetToolTip(btnAi, "ИИ-помощник: вопросы про горку, роспуск, светофоры, замедлители");
        btnAi.Visible = AiEnabled;
        top.Controls.Add(btnAi);

        // [ДОБАВЛЕНО 26.11.2026] варианты кнопками и задания
        var strip = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(28, 33, 40), WrapContents = true, Padding = new Padding(4, 0, 4, 2) };
        strip.Controls.Add(new Label { Text = "Вариант:", AutoSize = true, ForeColor = Color.Gainsboro, Font = BtnFont, Margin = new Padding(6, 10, 2, 0) });
        foreach (var (m, t, tip) in Variants)
        {
            var mm = m;
            var b = Btn(t, () => ChooseMode(mm));
            tips.SetToolTip(b, tip);
            modeBtns[m] = b;
            strip.Controls.Add(b);
        }
        strip.Controls.Add(new Label { Text = "", Width = 14, Margin = Padding.Empty });
        btnTasks = Btn("Задания…", OpenTasks, 0, Color.FromArgb(125, 85, 20));
        tips.SetToolTip(btnTasks, "Выбрать задание (урок): что сделать, за сколько минут, итог — оценка и файл для преподавателя");
        strip.Controls.Add(btnTasks);
        btnTaskEnd = Btn("Закончить задание", () => FinishTask(true));
        btnTaskEnd.Visible = false;
        strip.Controls.Add(btnTaskEnd);
        strip.Controls.Add(lblTask);
        Controls.Add(strip);        // Dock = Top: добавленный позже — выше, верхняя строка остаётся сверху
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

        // ---- [ДОБАВЛЕНО 26.11.2026] приём, башмаки, отправление («Учебный», «Экзамен») ----
        int shoeRows = (Lay.Tracks + 9) / 10;
        gOps = steps[0] = Group("Приём, тормозные башмаки, отправление", 66 + 40 + 22 + 38 * shoeRows + 36 * Lay.DepTracks);
        var ra = Row(40);
        ra.Controls.Add(new Label { Text = "Принять на:", AutoSize = true, ForeColor = Color.Gainsboro, Font = BtnFont, Margin = new Padding(3, 11, 3, 0) });
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            int aa = a;
            var b = Btn("П" + a, () => sim.AcceptTrain(aa), 58);
            b.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            tips.SetToolTip(b, $"Принять поезд на подходе на путь П{a} парка приёма");
            accBtns[a] = b;
            ra.Controls.Add(b);
        }
        var rs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38 * shoeRows, WrapContents = true, Margin = Padding.Empty };
        // подпись — отдельной строкой: кнопки всех путей (до 10) влезают в одну строку на любом языке
        var lblShoes = new Label { Text = "Башмаки на путях:", AutoSize = false, Height = 22, Dock = DockStyle.Top, ForeColor = Color.Gainsboro, Font = BtnFont, Padding = new Padding(3, 4, 0, 0) };
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            int kk = k;
            var b = Btn(k.ToString(), () => sim.SetShoe(kk, !sim.Shoe[kk]), 34);
            b.Tag = "nolang";
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            tips.SetToolTip(b, $"Путь {k}: уложить / снять тормозной башмак. На свободный путь башмак кладут до первого отцепа; перед выводом состава — снимают");
            shoeBtns[k] = b;
            rs.Controls.Add(b);
        }
        var depRows = new List<Control>();
        for (int d = 1; d <= Lay.DepTracks; d++)
        {
            int dd = d;
            var row = Row(36);
            depInfo[d] = new Label { AutoSize = false, Width = 268, Height = 32, ForeColor = Color.Gainsboro, Font = BtnFont, Margin = new Padding(3, 8, 0, 0) };
            depBtns[d] = Btn("", () => { if (sim.Dep[dd].State == DepState.Ready) sim.DepartTrain(dd); else sim.OrderTrainLoco(dd); }, 170);
            depBtns[d].Font = BtnFont;
            row.Controls.Add(depInfo[d]);
            row.Controls.Add(depBtns[d]);
            depRows.Add(row);
        }
        for (int i = depRows.Count - 1; i >= 0; i--) gOps.Controls.Add(depRows[i]);
        gOps.Controls.Add(rs);
        gOps.Controls.Add(lblShoes);
        gOps.Controls.Add(ra);
        gOps.Controls.Add(lblApproach);
        gOps.Visible = false;
        items.Add(gOps);

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
        int nRet = Lay.Cur.RetDefs.Count;
        var g3 = steps[3] = Group("Шаг 3. Торможение отцепов (замедлители)", 84 + 46 * nRet);
        chkArs.Dock = DockStyle.Top;
        chkArs.CheckedChanged += (_, _) => { if (syncing) return; sim.SetArs(chkArs.Checked); RefreshPanel(); };
        tips.SetToolTip(chkArs, "Галочка стоит — замедлители тормозят сами. Снимите — тормозите кнопками; жёлтая рамка подсказывает, какую нажать");
        for (int i = nRet - 1; i >= 0; i--)
        {
            int ri = i;
            var row = Row(46);
            row.Controls.Add(new Label { Text = Lay.Cur.RetDefs[i].Name, Tag = "nolang", Width = 50, ForeColor = Color.White, Font = BtnFont, Margin = new Padding(3, 12, 0, 0) });
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
        g3.Controls.Add(retLegend = new Label { Text = RetLegend(), Tag = Lay.Cur.Builtin ? null : "nolang", Dock = DockStyle.Top, Height = 18, ForeColor = Color.FromArgb(150, 157, 166), Font = new Font("Segoe UI", 8.5f) });
        g3.Controls.Add(chkArs);
        items.Add(g3);

        // ---- шаг 4: маршруты ----
        int routeRows = (Lay.Tracks + 3) / 4;
        var g4 = steps[4] = Group("Шаг 4. На какой путь идёт отцеп (маршрут)", 140 + 48 * routeRows);
        var mode = Row(30);
        rbGac.Font = BtnFont; rbManual.Font = BtnFont;
        mode.Controls.Add(rbGac);
        mode.Controls.Add(rbManual);
        rbGac.CheckedChanged += (_, _) => { if (!syncing && sim != null && sim.GacMode != rbGac.Checked) { sim.SetGac(rbGac.Checked); RefreshPanel(); } };
        tips.SetToolTip(rbGac, "ГАЦ сама берёт маршруты из сортировочного листка и переводит стрелки");
        tips.SetToolTip(rbManual, "Перед каждым отцепом нажмите кнопку его пути (или клавишу 1–8). Стрелки всё равно переведёт ГАЦ");
        var rows = Enumerable.Range(0, routeRows).Select(_ => Row(48)).ToArray();
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            int kk = k;
            var b = Btn("", () => sim.RouteButton(kk), 100);
            b.Tag = "nolang";                 // подпись «номер · направление» ставит RouteTexts (переводится по частям)
            b.Height = 42;
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            tips.SetToolTip(b, $"Путь {k} — {YardSim.TrackNames[k]} (клавиша {k})");
            routeBtns[k] = b;
            rows[(k - 1) / 4].Controls.Add(b);
        }
        var rr3 = Row(36);
        var undo = Btn("Сбросить последний маршрут", () => sim.UndoRoute());
        undo.Font = BtnFont;
        rr3.Controls.Add(undo);
        g4.Controls.Add(lblNext);
        g4.Controls.Add(rr3);
        for (int i = rows.Length - 1; i >= 0; i--) g4.Controls.Add(rows[i]);
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

    /// <summary>«1ТП — для всех путей, 2ТП-А — для путей 1–4, …» по тормозным позициям горки.</summary>
    /// <remarks>У горки из файла — склеенная строка: части переводятся сразу (и заново при смене языка, OnLang).</remarks>
    static string RetLegend()
    {
        var g = Lay.Cur;
        if (g.Builtin) return "1ТП — для всех путей, 2ТП-А — для путей 1–4, 2ТП-Б — для путей 5–8";
        return string.Join(", ", g.RetDefs.Select(r => r.Lo == 1 && r.Hi == g.Tracks ? L.T($"{r.Name} — для всех путей")
                                                    : r.Lo == r.Hi ? L.T($"{r.Name} — для пути {r.Lo}") : L.T($"{r.Name} — для путей {r.Lo}–{r.Hi}")));
    }
    Label retLegend;

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
        if (sim.Strict)
            m.Items.Add(L.T(sim.Shoe[k] ? "Снять тормозной башмак" : "Уложить тормозной башмак"), null, (_, _) => sim.SetShoe(k, !sim.Shoe[k]));
        if (mode == YardMode.Demo) foreach (ToolStripItem it in m.Items) it.Enabled = false;
        m.Show(view, p);
    }

    // =================================================================
    //  СМЕНА, ТАЙМЕР
    // =================================================================
    void NewShift()
    {
        // [ИЗМЕНЕНО 26.11.2026] вариант и ограничения задания; у задания — свой набор поездов (seed)
        if (task != null) mode = task.Mode;
        var m = mode;
        sim = new YardSim(task?.Seed ?? seed, s => { s.ApplyMode(m); s.ForceForbidden = task?.Forbidden ?? false; });
        bool? ars = sim.Strict ? false : m == YardMode.Demo ? true : task?.Ars;
        bool? gac = sim.Strict ? false : m == YardMode.Demo ? true : task?.Gac;
        arsLocked = ars != null;
        gacLocked = gac != null;
        if (!sim.Strict && m != YardMode.Demo)
        {
            sim.GacMode = gac ?? rbGac.Checked;
            sim.Ars = ars ?? chkArs.Checked;
            if (!sim.GacMode) foreach (var c in sim.ListokTrain.AllCuts) c.Route = 0;
        }
        syncing = true;
        chkArs.Checked = sim.Ars;
        rbGac.Checked = sim.GacMode; rbManual.Checked = !sim.GacMode;
        syncing = false;
        chkArs.Enabled = !arsLocked;
        rbGac.Enabled = rbManual.Enabled = !gacLocked;
        sim.Log += (t, bad) =>
        {
            if (IsDisposed) return;
            Note(t, bad);
        };
        view.Sim = sim;
        AiSim?.Invoke(sim);
        log.Clear();
        journal.Items.Clear();
        Note(m switch
        {
            YardMode.Training => "Смена началась (вариант «Учебный»): всё делаете вы — состав на пути П1 осмотрен, подайте под него горочный локомотив. Подсказки — в жёлтом блоке справа",
            YardMode.Exam => "Смена началась (вариант «Экзамен»): всё делаете вы, подсказок нет. Состав на пути П1 осмотрен и ждёт горочный локомотив",
            YardMode.Demo => "Смена началась (вариант «Демонстрация»): горку ведёт автопилот — смотрите схему и журнал",
            _ => "Смена началась. Поезд на пути П1 готов к роспуску. Делайте то, что написано в жёлтом блоке «Что делать сейчас» справа",
        }, false);
        foreach (var n in StartNotes) Note(n, true);
        if (task != null) Note($"Задание «{task.Title}» на {task.Minutes} мин. {task.Text}", false);
        RefreshPanel();
    }

    void Note(string text, bool bad)
    {
        var t = sim?.Clock.ToString("HH:mm:ss") ?? "";
        log.Add((t, text, bad));
        AiNote?.Invoke(text, bad);
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
        if (!Lay.Cur.Builtin && retLegend != null) retLegend.Text = RetLegend();
        Text = Lay.Cur.Title.Length > 0 ? Lay.Cur.Title + " — " + L.T("пульт дежурного по горке (ДСПГ)") : L.T("Сортировочная — пульт дежурного по горке (ДСПГ)");
        journal.Invalidate();
        RefreshPanel();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (ActiveControl is TextBoxBase) return base.ProcessCmdKey(ref msg, key);
        if (key == Keys.Escape) { sim.SetAspect(Aspect.Red); RefreshPanel(); return true; }
        if (key >= Keys.D1 && key <= Keys.D9 && key - Keys.D0 <= Lay.Tracks) { sim.RouteButton(key - Keys.D0); RefreshPanel(); return true; }
        if (key >= Keys.NumPad1 && key <= Keys.NumPad9 && key - Keys.NumPad0 <= Lay.Tracks) { sim.RouteButton(key - Keys.NumPad0); RefreshPanel(); return true; }
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
        // [ДОБАВЛЕНО 26.11.2026] варианты, задание, «Экзамен» (без подсказок), «Демонстрация» (пульт у автопилота)
        bool hints = mode != YardMode.Exam;
        foreach (var (m, b) in modeBtns)
        {
            b.BackColor = m == mode ? Color.FromArgb(0, 105, 190) : Color.FromArgb(55, 64, 76);
            b.Enabled = task == null || taskOver || m == mode;
        }
        RefreshTask();
        bool demo = mode == YardMode.Demo;
        for (int i = 1; i <= 4; i++) if (steps[i].Enabled == demo) steps[i].Enabled = !demo;
        if (gMan.Enabled == demo) { gMan.Enabled = !demo; gExtra.Enabled = !demo; }
        if (btnAi != null) btnAi.Enabled = mode != YardMode.Exam;
        RefreshOps();

        var (now, btn, act, step, urgent) = hints ? Advice()
            : ("Экзамен: подсказок нет. Работайте сами — программа следит за каждым шагом и ставит оценку", (string)null, (Action)null, -1, false);
        if (demo) (now, btn, act, step, urgent) = ("Демонстрация: горку ведёт автопилот — в журнале написано, что он делает и почему. Ускорить время — «×2», «×4»", null, null, -1, false);
        LangUi.SetText(lblNow, now);
        // заранее: следующий за первым отцеп — «Л» / «Е»
        var pp = sim.Pushing;
        string warn = pp != null && !pp.Escort && pp.Cuts.Count > 1 && pp.Cuts[1].Forbidden && !pp.Cuts[0].Forbidden
            ? $"Внимание: за отцепом {pp.Cuts[0].No} идёт «{pp.Cuts[1].Mark}» (его нельзя с горки) — как только отцеп {pp.Cuts[0].No} уйдёт, нажмите «СТОП»" : "";
        LangUi.SetText(lblWarn, warn);
        if (!hints || demo) warn = "";
        lblWarn.Visible = warn != "";
        nowPanel.Height = warn != "" ? 170 : 134;
        nowAct = act;
        btnNow.Visible = btn != null;
        if (btn != null) LangUi.SetText(btnNow, btn);
        bool blink = Environment.TickCount / 400 % 2 == 0;
        nowPanel.BackColor = urgent ? (blink ? Color.FromArgb(120, 30, 25) : Color.FromArgb(80, 24, 20)) : Color.FromArgb(58, 50, 18);
        btnNow.BackColor = urgent ? Color.FromArgb(255, 120, 90) : Color.Gold;
        for (int i = 0; i <= 4; i++)
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
        for (int i = 0; i < sim.Retarders.Length && i < retInfo.Length; i++)
        {
            var r = sim.Retarders[i];
            int cur = r.Step == 0 ? 0 : r.Step <= 2 ? 1 : 2;
            int adv = r.Advice == 0 ? 0 : r.Advice <= 2 ? 1 : 2;
            bool hint = hints && !sim.Ars && r.CutOn && adv != cur;
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
        string nx = !hints ? "Маршрут — кнопкой пути перед каждым отцепом (клавиши 1–8)"
            : sim.SelectedCut != null && sim.SelectedCut.State == CutState.InTrain
            ? $"Выбран отцеп {sim.SelectedCut.No} (по листку — путь {sim.SelectedCut.Dest}): нажмите кнопку пути"
            : next != null ? $"Следующий без маршрута: отцеп {next.No} — по листку путь {next.Dest}. Нажмите кнопку «{next.Dest}»"
            : sim.GacMode ? "ГАЦ ставит маршруты сама. Чтобы изменить — выберите отцеп в листке и нажмите путь" : "Маршруты всем отцепам заданы";
        LangUi.SetText(lblNext, nx);
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            bool hot = hints && (sim.SelectedCut?.Dest ?? next?.Dest) == k;
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

        // [ДОБАВЛЕНО 26.11.2026] «Учебный»: приём, отправление, башмаки
        if (sim.Strict)
        {
            if (sim.Approaching != null)
            {
                var f = sim.Arr.Skip(1).FirstOrDefault(a => a.State == ArrState.Free && a.Train == null && !(sim.Pushing?.Arr == a.No && a.Train == sim.Pushing));
                if (f != null) return ($"Поезд {sim.Approaching.Name} ({sim.Approaching.WagonCount} ваг.) подходит к станции. Примите его на свободный путь парка приёма", $"Принять на П{f.No}", () => sim.AcceptTrain(f.No), 0, true);
            }
            var dr = sim.Dep.Skip(1).FirstOrDefault(d => d.State == DepState.Ready);
            if (dr != null) return ($"Поезд №{dr.TrainNo} на пути О{dr.No} готов (тормоза опробованы) — отправьте его", $"Отправить с О{dr.No}", () => sim.DepartTrain(dr.No), 0, false);
            var dw = sim.Dep.Skip(1).FirstOrDefault(d => d.State == DepState.WaitLoco);
            if (dw != null) return ($"Состав выведен на путь О{dw.No} — закажите под него поездной локомотив", $"Локомотив на О{dw.No}", () => sim.OrderTrainLoco(dw.No), 0, false);
            if (p != null && !p.Escort && p.Cuts.Count > 0)
            {
                var c = p.Cuts[0];
                int k = c.Route > 0 ? c.Route : c.Dest;
                if (!sim.Shoe[k] && !sim.Cuts.Any(x => x.Active && x.Track == k))
                    return ($"Отцеп {c.No} пойдёт на свободный путь {k} — уложите на него тормозной башмак, иначе вагоны не закреплены", $"Башмак на путь {k}", () => sim.SetShoe(k, true), 0, true);
            }
            for (int k = 1; k <= Lay.Tracks; k++)
                if (sim.Shoe[k] && sim.OnTrack(k).Sum(c => c.Wagons.Count) >= YardSim.Norm(k) && !sim.ShuntBusy && sim.Dep.Skip(1).Any(d => d.State == DepState.Free))
                { int kk = k; return ($"На пути {k} набран состав. Снимите тормозной башмак — потом выведите состав в парк отправления", $"Снять башмак с пути {k}", () => sim.SetShoe(kk, false), 0, false); }
        }

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

    // =================================================================
    //  [ДОБАВЛЕНО 26.11.2026] ПРИЁМ, БАШМАКИ, ОТПРАВЛЕНИЕ; ВАРИАНТЫ; ЗАДАНИЯ
    // =================================================================
    void RefreshOps()
    {
        bool on = sim.Strict;
        if (gOps.Visible != on) gOps.Visible = on;
        if (!on) return;
        var ap = sim.Approaching;
        LangUi.SetText(lblApproach, ap != null ? $"Поезд {ap.Name} ({ap.WagonCount} ваг.) у входного светофора — на какой путь принять:" : "Поездов на подходе нет");
        lblApproach.ForeColor = ap != null ? Color.Gold : Color.Gainsboro;
        for (int a = 1; a <= Lay.ArrTracks; a++)
        {
            bool free = sim.Arr[a].State == ArrState.Free && sim.Arr[a].Train == null;
            accBtns[a].Enabled = ap != null && free;
            accBtns[a].BackColor = ap != null && free ? Color.FromArgb(30, 140, 60) : Color.FromArgb(55, 64, 76);
        }
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            shoeBtns[k].BackColor = sim.Shoe[k] ? Color.FromArgb(230, 130, 30) : Color.FromArgb(55, 64, 76);
            shoeBtns[k].ForeColor = sim.Shoe[k] ? Color.Black : Color.White;
        }
        for (int d = 1; d <= Lay.DepTracks; d++)
        {
            var x = sim.Dep[d];
            string st = x.State switch
            {
                DepState.Free => "свободен",
                DepState.Filling => "выводят состав",
                DepState.WaitLoco => "состав ждёт поездной локомотив",
                DepState.LocoComing => "поездной локомотив идёт",
                DepState.BrakeTest => "опробование тормозов",
                DepState.Ready => "готов к отправлению",
                _ => "отправляется",
            };
            LangUi.SetText(depInfo[d], $"О{d}: " + L.T(st) + (x.State != DepState.Free && x.TrainNo > 0 ? $" · №{x.TrainNo}, {x.Wagons.Count} ваг." : ""));
            bool ready = x.State == DepState.Ready, wait = x.State == DepState.WaitLoco;
            LangUi.SetText(depBtns[d], ready ? "▶ Отправить" : "Заказать локомотив");
            depBtns[d].Enabled = ready || wait;
            depBtns[d].BackColor = ready ? Color.FromArgb(30, 140, 60) : wait ? Color.FromArgb(40, 120, 200) : Color.FromArgb(55, 64, 76);
        }
    }

    static string ModeName(YardMode m) => Variants.First(v => v.m == m).text;

    void ChooseMode(YardMode m)
    {
        if (task != null && !taskOver) { Note("Нельзя: идёт задание — сначала закончите его (кнопка «Закончить задание»)", true); return; }
        if (m == mode && task == null) return;
        // «Только горка» ⇄ «Горка + маневры» — на ходу, как раньше; остальные — новой сменой
        if (task == null && mode is YardMode.Hump or YardMode.HumpShunt && m is YardMode.Hump or YardMode.HumpShunt)
        {
            mode = m;
            sim.ApplyMode(m);
            Note(sim.AutoOps ? "Вариант «Только горка»: приём, осмотр, горочный локомотив и вывод составов — сами" : "Вариант «Горка + маневры»: горочный локомотив под состав, его возврат и вывод составов — делаете вы", false);
            RefreshPanel();
            return;
        }
        if (sim.T > 30 && task == null &&
            MessageBox.Show(L.T($"Начать новую смену в варианте «{ModeName(m)}»? Оценка и журнал обнулятся."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        task = null;
        taskOver = false;
        mode = m;
        seed = Environment.TickCount;
        speed = 1;
        NewShift();
    }

    void RefreshTask()
    {
        btnTaskEnd.Visible = task != null && !taskOver;
        btnTasks.Enabled = task == null || taskOver;
        if (task == null) { LangUi.SetText(lblTask, ""); return; }
        if (!taskOver)
        {
            double left = task.Minutes * 60 - sim.T;
            if (task.Reached(sim)) { FinishTask(false); return; }
            if (left <= 0) { FinishTask(false); return; }
            var ts = TimeSpan.FromSeconds(Math.Max(0, left));
            LangUi.SetText(lblTask, $"Задание «{task.Title}» · осталось {(int)ts.TotalMinutes}:{ts.Seconds:00} · " + string.Join(" · ", task.Goals(sim).Select(g => (g.Done ? "✔ " : "") + g.Text)));
            lblTask.ForeColor = left < 120 ? Color.FromArgb(255, 140, 110) : Color.Gold;
        }
    }

    /// <summary>Конец урока: итог окном и файлом.</summary>
    void FinishTask(bool byUser)
    {
        if (task == null || taskOver) return;
        if (byUser && MessageBox.Show(L.T("Закончить задание сейчас? Невыполненные цели — не засчитаются."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        taskOver = true;
        speed = 0;
        string grade = task.Grade(sim);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(L.T("Сортировочная — итог задания"));
        sb.AppendLine(L.T("Ученик: ") + (student.Length > 0 ? student : L.T("(не указан)")));
        sb.AppendLine(L.T("Задание: ") + L.T(task.Title));
        sb.AppendLine(L.T("Вариант: ") + L.T(ModeName(task.Mode)));
        var used = TimeSpan.FromSeconds(Math.Min(sim.T, task.Minutes * 60));
        sb.AppendLine(L.T($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}, время на задание {task.Minutes} мин, затрачено {(int)used.TotalMinutes}:{used.Seconds:00}"));
        if (byUser) sb.AppendLine(L.T("Задание закончено досрочно (кнопкой)"));
        sb.AppendLine();
        sb.AppendLine(L.T("Цели:"));
        foreach (var (t, done) in task.Goals(sim)) sb.AppendLine((done ? "  [✔] " : "  [ ] ") + L.T(t));
        sb.AppendLine();
        sb.AppendLine(L.T($"Нарушений: {sim.Violations.Count}"));
        foreach (var v in sim.Violations)
            sb.AppendLine($"  {new DateTime(2026, 1, 1, 8, 0, 0).AddSeconds(v.T):HH:mm:ss}  {L.T(v.Text)} (−{v.Penalty})");
        sb.AppendLine();
        sb.AppendLine(L.T($"Распущено: {sim.TrainsRolled} поездов, {sim.Rolled} ваг., {sim.CutsRolled} отцепов (на свой путь — {sim.CorrectCuts}); отправлено поездов: {sim.Departed}"));
        sb.AppendLine(L.T($"Баллы: {sim.Score} из 100"));
        sb.AppendLine(L.T($"ОЦЕНКА: {grade}") + (task.GoalsMet(sim) ? "" : " — " + L.T("цели задания не выполнены")));
        string text = sb.ToString();
        string file = null;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Сортировочная — результаты");
            Directory.CreateDirectory(dir);
            string who = string.Concat((student.Length > 0 ? student : "ученик").Split(Path.GetInvalidFileNameChars()));
            file = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd HH-mm} {who} — {task.Id}.txt");
            File.WriteAllText(file, text, System.Text.Encoding.UTF8);
        }
        catch { file = null; }
        Note($"Задание «{task.Title}» окончено: оценка {grade}", grade == "2");
        RefreshPanel();
        LastReport = file;
        if (!headless) ShowText(L.T("Итог задания"), text + (file != null ? "\n" + L.T("Сохранено: ") + file : ""));
    }

    void ShowText(string title, string text)
    {
        using var f = new Form { Text = title, Size = new Size(760, 560), StartPosition = FormStartPosition.CenterParent, BackColor = Panel, Font = BtnFont, Tag = "nolang" };
        var tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), BackColor = Color.FromArgb(24, 28, 33), ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f), BorderStyle = BorderStyle.None };
        var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(0, 105, 190) };
        f.Controls.Add(tb);
        f.Controls.Add(ok);
        f.AcceptButton = ok;
        f.ShowDialog(this);
    }

    /// <summary>Окно «Задания»: список, описание, фамилия ученика, «Начать».</summary>
    void OpenTasks()
    {
        using var f = new Form { Text = L.T("Задания — выберите урок"), Size = new Size(820, 560), StartPosition = FormStartPosition.CenterParent, BackColor = Panel, Font = BtnFont, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Tag = "nolang" };
        var list = new ListBox { Dock = DockStyle.Left, Width = 300, BackColor = Color.FromArgb(24, 28, 33), ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f), BorderStyle = BorderStyle.None, IntegralHeight = false };
        foreach (var t in YardTask.All) list.Items.Add(L.T(t.Title));
        var info = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 35, 41), ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f), BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(6), BackColor = Color.FromArgb(22, 26, 31) };
        bottom.Controls.Add(new Label { Text = L.T("Ученик (фамилия, имя):"), AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(3, 9, 3, 0) });
        var name = new TextBox { Width = 260, Text = student, Margin = new Padding(3, 6, 12, 0) };
        bottom.Controls.Add(name);
        var start = new Button { Text = L.T("▶ Начать задание"), AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.Black, BackColor = Color.Gold, DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = L.T("Отмена"), AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(55, 64, 76), DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(start);
        bottom.Controls.Add(cancel);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var t = YardTask.All[list.SelectedIndex];
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(t.Title)).AppendLine();
            sb.AppendLine(L.T(t.Text)).AppendLine();
            sb.AppendLine(L.T("Вариант: ") + L.T(ModeName(t.Mode)));
            sb.AppendLine(L.T($"Время: {t.Minutes} мин (модельного времени; ускорять можно)"));
            if (t.Ars != null && !(t.Mode is YardMode.Training or YardMode.Exam)) sb.AppendLine(L.T(t.Ars == true ? "АРС: включена" : "АРС: выключена — торможение вручную"));
            if (t.Gac != null && !(t.Mode is YardMode.Training or YardMode.Exam)) sb.AppendLine(L.T(t.Gac == true ? "Маршруты: ГАЦ по листку" : "Маршруты: вручную"));
            sb.AppendLine(L.T("Цели:"));
            if (t.Trains > 0) sb.AppendLine("  • " + L.T($"распустить поездов: {t.Trains}"));
            if (t.Forms > 0) sb.AppendLine("  • " + L.T($"вывести составов в парк отправления: {t.Forms}"));
            if (t.Departs > 0) sb.AppendLine("  • " + L.T($"отправить поездов: {t.Departs}"));
            if (t.MaxViolations >= 0) sb.AppendLine("  • " + L.T($"нарушений не больше {t.MaxViolations}"));
            sb.AppendLine().AppendLine(L.T("Итог (цели, нарушения, оценка) сохранится в «Документы\\Сортировочная — результаты»."));
            info.Text = sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
            start.Enabled = true;
        };
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) { f.DialogResult = DialogResult.OK; f.Close(); } };
        f.Controls.Add(info);
        f.Controls.Add(list);
        f.Controls.Add(bottom);
        f.AcceptButton = start;
        f.CancelButton = cancel;
        list.SelectedIndex = 0;
        if (TasksShot != null)      // служебное: снимок окна заданий (выбрано TasksShotIndex)
        {
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-6000, 0); f.ShowInTaskbar = false;
            f.Show(); list.SelectedIndex = TasksShotIndex; name.Text = "Иванов Иван"; LangUi.Apply(f, true); Application.DoEvents();
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(TasksShot, System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        if (f.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        student = name.Text.Trim();
        task = YardTask.All[list.SelectedIndex];
        taskOver = false;
        speed = 1;
        NewShift();
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
        headless = true;
        WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-6000, 0, 1600, 960);
        ShowInTaskbar = false;
        Show();
        if (sim.Strict) sim.SendHumpLoco(1);       // [26.11.2026] «Учебный»: подать локомотив, дальше — как студент
        else if (mode != YardMode.Demo)
        {
            chkArs.Checked = !manual;
            if (manual) rbManual.Checked = true;
            sim.SetPushRoute(1);
            if (manual) for (int k = 0; k < 2; k++) sim.RouteButton(sim.ListokTrain.AllCuts.First(c => c.Route == 0).Dest);
            sim.SetAspect(Aspect.YellowGreen);
        }
        for (double t = 0; t < seconds; t += 0.05)
        {
            sim.Tick(0.05);
            if ((int)(t * 20) % 10 == 0) RefreshTask();
            if (sim.Strict)
            {
                if (sim.Pushing == null && sim.Arr[1].State == ArrState.Ready) { sim.SetPushRoute(1); sim.SetAspect(Aspect.YellowGreen); }
                foreach (var c in sim.Pushing?.Cuts.Take(1) ?? Enumerable.Empty<Cut>()) if (c.Route == 0) { if (!sim.Cuts.Any(x => x.Active && x.Track == c.Dest)) sim.SetShoe(c.Dest, true); sim.RouteButton(c.Dest); }
                for (int r = 0; r < sim.Retarders.Length; r++) sim.SetRetarder(r, sim.Retarders[r].CutOn ? sim.Retarders[r].Advice : 0);
                if (sim.Approaching != null && sim.Arr.Skip(1).FirstOrDefault(x => x.State == ArrState.Free && x.Train == null) is ArrTrack fa) sim.AcceptTrain(fa.No);
            }
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
        var own = Path.Combine(GuideDir, L.Current switch { "kk" => "НҰСҚАУЛЫҚ.txt", "en" => "INSTRUCTION.txt", _ => "ИНСТРУКЦИЯ.txt" });
        var path = File.Exists(own) ? own : Path.Combine(GuideDir, "ИНСТРУКЦИЯ.txt");
        if (!File.Exists(path)) { MessageBox.Show(L.T("Файл инструкции не найден") + "\n" + path, Text); return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
    }
}
