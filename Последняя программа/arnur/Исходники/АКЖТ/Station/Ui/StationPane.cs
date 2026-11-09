using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  ПАНЕЛЬ ПУЛЬТА ОДНОЙ СХЕМЫ (АКЖТ или путевой пост).
//  [ИЗМЕНЕНО 06.10.2026] Раньше это было отдельное окно (StationForm). Теперь это
//  "половина" общего окна MainForm: окно растянуто на два монитора, слева АКЖТ,
//  справа пост (или наоборот — по расположению мониторов). Общая для обеих половин
//  верхняя полоса (порт Arduino, «Упрощённый», «Проверка», «Убрать всё») — в MainForm.
//  Что показывать (схему, маршруты, стрелки, участки, кнопки имитации)
//  берётся из StationView. Логику ведёт общая модель (ModelHost).
//  [ИЗМЕНЕНО 28.10.2026] Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса (у Алгабаса панель одна — на всё окно
//  StationForm). Раскладка — по режиму интерфейса (Ui/UiSettings.cs, кнопка «Параметры»).
// =====================================================================

class StationPane : UserControl
{
    // [ИЗМЕНЕНО 28.10.2026] фон, кнопки и заголовки — по палитре режима интерфейса (Ui/SchemePalette.cs)
    static Color Bg => RailSchemePanel.Bg;
    static readonly Color Pink = Color.FromArgb(219, 0, 105);
    static Color BtnLight => SchemePalette.Current.Button;
    static readonly Color ModeOn = Color.FromArgb(52, 152, 219);
    static readonly Color ModeOff = Color.FromArgb(128, 128, 128);
    static readonly Color RouteOpen = Color.FromArgb(120, 220, 140);
    // [ИЗМЕНЕНО 12.10.2026] открытый маневровый — тем же цветом, что поездной (было голубым)
    static readonly Color RouteShuntOpen = RouteOpen;
    static readonly Color RouteSet = Color.FromArgb(200, 240, 200);
    static readonly Color RouteTrain = Color.FromArgb(255, 235, 120);
    static readonly Color RouteReleasing = Color.FromArgb(255, 170, 80);
    static readonly Color OccBtn = Color.FromArgb(225, 60, 60);
    static readonly Color LockBtn = Color.FromArgb(255, 235, 120);
    static Color TitleColor => SchemePalette.Current.Title;

    protected readonly StationView View;
    protected static Interlocking IL => ModelHost.Model.IL;
    protected static TrainSim Trains => ModelHost.Model.Trains;
    protected readonly RailSchemePanel Scheme;

    bool shuntMode;
    bool dirty = true;

    Button btnTrain, btnShunt;
    ListBox lstLog;
    Label lblTrains;
    Label lblCounters;   // [ДОБАВЛЕНО 20.10.2026, этап 3] счётчики ИР, ВК, ВСН
    Action panelRefresh; // [ДОБАВЛЕНО 21.10.2026, этап 6] кнопки пульта (Ui/PanelUi.cs)
    FlowLayoutPanel routesList;
    FlowLayoutPanel headerButtons;
    readonly List<(CheckBox box, Func<bool> get)> toggles = new();
    CheckBox chkAuto;
    readonly Dictionary<RailRoute, Button> routeBtns = new();
    readonly Dictionary<RailSwitch, Button> swBtns = new();
    readonly Dictionary<TrackSection, Button> secBtns = new();
    readonly Dictionary<RailSignal, Button> invBtns = new();
    readonly Dictionary<DirectionLine, Label> dirLabels = new();
    readonly ToolTip tips = new();
    // [ДОБАВЛЕНО 28.10.2026] режимы интерфейса: окно половины перестраивается при смене режима / панелей
    TableLayoutPanel root;
    readonly List<Button> extraHeader = new();       // кнопки, добавленные снаружи (Депо) — переживают перестройку
    readonly List<LogItem> logHistory = new();       // журнал хранится и когда его колонка скрыта
    Label lblMsg;                                    // последняя строка журнала (когда колонка журнала скрыта)
    Label lblClock;                                  // часы (панель «Название станции и часы»)
    ClassicPanel classic;
    int classicRow;
    readonly System.Windows.Forms.Timer clockTimer = new() { Interval = 1000 };

    record LogItem(string Text, LogKind Kind);

    readonly bool sectionsOnLeft, showLog;

    /// <summary>Куда отправлять свои сообщения, если у половины нет журнала (пост → журнал АКЖТ).</summary>
    public Action<string, LogKind> LogForward;

    /// <param name="sectionsOnLeft">список «Занятость участков» слева от схемы (иначе справа)</param>
    /// <param name="showLog">показывать журнал; без него свои сообщения уходят в LogForward</param>
    public StationPane(StationView view, bool sectionsOnLeft = false, bool showLog = true)
    {
        View = view;
        this.sectionsOnLeft = sectionsOnLeft;
        this.showLog = showLog;
        BackColor = Bg;
        Font = new Font("Bahnschrift", 10f);

        // без отступов: край схемы — ровно край половины окна (стык двух мониторов)
        Scheme = new RailSchemePanel(view) { Dock = DockStyle.Fill, Margin = new Padding(0), ShuntMode = () => shuntMode };
        BuildUi();

        // общий журнал модели (АКЖТ + пост) показывает та половина, у которой есть журнал
        if (showLog) ModelHost.Log += AddLog;
        ModelHost.Changed += OnModelChanged;
        UiSettings.Changed += Rebuild;
        clockTimer.Tick += (_, _) => UpdateClock();
        clockTimer.Start();
        HandleDestroyed += (_, _) =>
        {
            if (showLog) ModelHost.Log -= AddLog;
            ModelHost.Changed -= OnModelChanged;
            UiSettings.Changed -= Rebuild;
            clockTimer.Dispose();
        };
        RefreshUi();
    }

    /// <summary>[ДОБАВЛЕНО 28.10.2026] Сменился режим интерфейса или набор панелей — собрать половину заново.
    /// Схема, кнопки заголовка «снаружи» и журнал сохраняются.</summary>
    void Rebuild()
    {
        if (IsDisposed) return;
        SuspendLayout();
        Scheme.Parent?.Controls.Remove(Scheme);
        foreach (var b in extraHeader) b.Parent?.Controls.Remove(b);
        var old = root;
        Controls.Remove(old);
        old?.Dispose();
        routeBtns.Clear(); swBtns.Clear(); secBtns.Clear(); invBtns.Clear(); dirLabels.Clear(); toggles.Clear();
        lstLog = null; lblMsg = null; lblClock = null; classic = null; panelRefresh = null; routesList = null; btnTrain = btnShunt = null; chkAuto = null; lblTrains = null;
        BackColor = Bg;
        Scheme.BackColor = Bg;
        try
        {
            BuildUi();
            foreach (var b in extraHeader) headerButtons.Controls.Add(b);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            AddLog("Ошибка перестройки окна: " + ex.Message, LogKind.Refuse);
        }
        finally { ResumeLayout(true); }
        RefreshUi();
        Scheme.Invalidate();
    }

    /// <summary>Как прижать схему по горизонтали (см. RailSchemePanel.HAlign).</summary>
    public StringAlignment SchemeAlign
    {
        get => Scheme.HAlign;
        set { if (Scheme.HAlign != value) { Scheme.HAlign = value; Scheme.Invalidate(); } }
    }

    /// <summary>Упрощённый вид схемы (переключается сразу на обеих половинах из MainForm).</summary>
    public bool Simple
    {
        get => Scheme.Simple;
        set { Scheme.Simple = value; Scheme.Invalidate(); }
    }

    /// <summary>Добавить розовую кнопку в заголовок этой половины (например «Депо»).</summary>
    public Button AddHeaderButton(string text, Action click)
    {
        var b = PinkButton(text, click);
        headerButtons.Controls.Add(b);
        extraHeader.Add(b);
        return b;
    }

    public void ClearSelection() => Scheme.ClearSelection();

    /// <summary>Схемы этих половин рисуются в общем масштабе — пути на одном уровне.</summary>
    public static void SameTrackLevel(params StationPane[] panes)
    {
        var group = panes.Select(p => p.Scheme).ToList();
        foreach (var s in group) s.LevelGroup = group;
    }

    // =================================================================
    //  ИНТЕРФЕЙС
    // =================================================================
    // [ИЗМЕНЕНО 28.10.2026] раскладка зависит от режима интерфейса (Ui/UiSettings.cs):
    //  инженерный / учебный — как раньше: схема, внизу колонки «журнал, маршруты, стрелки, пульт, имитация»;
    //  рабочий (как АРМ ДСП Медеу) и павлодарский — пульт ДСП полосой над схемой, схема крупно,
    //  под схемой ПОЕЗДНЫЕ / МАНЕВРОВЫЕ и последнее сообщение (павлодарский — кнопочный пульт «Енисей»).
    //  Каждую панель можно показать или скрыть в «Параметрах».
    void BuildUi()
    {
        bool Show(string id) => UiSettings.Show(id);
        var mode = UiSettings.Mode;
        bool strip = mode is UiMode.Work or UiMode.Classic;          // пульт ДСП — полосой над схемой
        bool logCol = showLog && Show(UiSettings.Log);
        bool classicOn = Show(UiSettings.Classic);
        bool modeBar = !Show(UiSettings.Routes) && !classicOn;      // ПОЕЗДНЫЕ / МАНЕВРОВЫЕ под схемой
        bool msgLine = showLog && !logCol;                          // журнал скрыт — одна строка сообщения

        root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 0, BackColor = Bg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // без этого колонка растягивается по самой широкой панели
        Controls.Add(root);
        void AddRow(Control c, RowStyle st)
        {
            root.RowStyles.Add(st);
            root.Controls.Add(c, 0, root.RowCount++);
        }

        // ---------- заголовок половины: название (+ часы) + «Отмена набора», «Отменить маршрут» ----------
        bool clock = Show(UiSettings.Clock);
        // название | часы (обрезаются, если не помещаются) | кнопки
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = View.Title, AutoSize = true, ForeColor = clock && mode == UiMode.Work ? Color.FromArgb(255, 222, 60) : TitleColor,
            Font = new Font("Bahnschrift", clock ? 16f : 12f, FontStyle.Bold), Padding = new Padding(12, clock ? 8 : 9, 0, 0), Margin = new Padding(0)
        };
        headerButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = new Padding(0, clock ? 10 : 5, 10, 0), Margin = new Padding(0) };
        headerButtons.Controls.Add(PinkButton("Отмена набора", Scheme.CancelSelection));   // [ДОБАВЛЕНО 20.10.2026]
        headerButtons.Controls.Add(PinkButton("Отменить маршрут", () => IL.CancelLast(View.Routes)));
        top.Controls.Add(title, 0, 0);
        if (clock)
        {
            lblClock = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(24, 8, 0, 0), Margin = new Padding(0), Font = new Font("Bahnschrift", 16f, FontStyle.Bold),
                ForeColor = mode == UiMode.Work ? Color.FromArgb(120, 240, 110) : TitleColor
            };
            tips.SetToolTip(lblClock, "Время: на занятии — игровое время урока, иначе — время компьютера");
            top.Controls.Add(lblClock, 1, 0);
            UpdateClock();
        }
        top.Controls.Add(headerButtons, 2, 0);
        AddRow(top, new RowStyle(SizeType.Absolute, clock ? 48 : 40));

        // ---------- пульт ДСП полосой (рабочий и павлодарский режимы) ----------
        FlowLayoutPanel panelCol = null;
        if (Show(UiSettings.Panel))
        {
            panelCol = PanelUi.Column(Bg);
            if (strip)
            {
                panelCol.AutoScroll = false;
                panelCol.AutoSize = true;
                panelCol.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                panelCol.Padding = new Padding(6, 2, 6, 2);
                // в полосе ряды кнопок идут друг за другом, без переноса строк
                panelRefresh = PanelUi.Build(panelCol, View, this, tips, FlatButton, c => { c.Margin = new Padding(c.Margin.Left, 2, 10, 2); return c; }, StripTitle);
                AddRow(panelCol, new RowStyle(SizeType.AutoSize));
            }
            else panelRefresh = PanelUi.Build(panelCol, View, this, tips, FlatButton, Breaker, SmallTitle);
        }

        // ---------- середина: схема + участки ----------
        // [ИЗМЕНЕНО 08.10.2026] у АКЖТ список участков слева — справа схема упирается в стык мониторов
        if (Show(UiSettings.Sections))
        {
            var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Bg, Margin = new Padding(0) };
            int schemeCol = sectionsOnLeft ? 1 : 0, secCol = 1 - schemeCol;
            mid.ColumnStyles.Add(sectionsOnLeft ? new ColumnStyle(SizeType.Absolute, 160) : new ColumnStyle(SizeType.Percent, 100));
            mid.ColumnStyles.Add(sectionsOnLeft ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Absolute, 160));
            mid.Controls.Add(Scheme, schemeCol, 0);

            var secPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, BackColor = Bg, Padding = new Padding(4)
            };
            secPanel.Controls.Add(new Label { Text = "Занятость\nучастков", AutoSize = true, Font = new Font("Bahnschrift", 10f, FontStyle.Bold), ForeColor = TitleColor });
            foreach (var sec in View.Sections)
            {
                var s = sec;
                var b = FlatButton(s.Label, 128, 24);
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Font = new Font("Bahnschrift", 9f);
                b.Click += (_, _) =>
                {
                    if (!UiSettings.Show(UiSettings.ManualOcc) && !s.ManualOcc)
                    {
                        IL.Say($"Участок {s.Label}: ручная занятость в этом режиме выключена («Параметры»)", LogKind.Refuse);
                        return;
                    }
                    s.ManualOcc = !s.ManualOcc;
                    IL.Say($"Участок {s.Label} {(s.ManualOcc ? "занят (вручную)" : "освобождён (вручную)")}");
                    IL.NotifyChanged();
                };
                tips.SetToolTip(b, s.Title + "\nклик — занять / освободить вручную");
                secBtns[s] = b;
                secPanel.Controls.Add(b);
            }
            mid.Controls.Add(secPanel, secCol, 0);
            AddRow(mid, new RowStyle(SizeType.Percent, 100));
        }
        else AddRow(Scheme, new RowStyle(SizeType.Percent, 100));

        // ---------- под схемой: ПОЕЗДНЫЕ / МАНЕВРОВЫЕ и строка сообщения ----------
        if (modeBar || msgLine)
        {
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Bg, Margin = new Padding(0), Padding = new Padding(6, 2, 6, 2), AutoSize = true };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (modeBar)
            {
                var modes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Bg };
                btnTrain = ModeButton("ПОЕЗДНЫЕ", () => SetMode(false));
                btnShunt = ModeButton("МАНЕВРОВЫЕ", () => SetMode(true));
                btnTrain.Height = btnShunt.Height = 40;
                modes.Controls.Add(btnTrain);
                modes.Controls.Add(btnShunt);
                bar.Controls.Add(modes, 0, 0);
            }
            if (msgLine)
            {
                lblMsg = new Label
                {
                    Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Bahnschrift", 11f, FontStyle.Bold),
                    ForeColor = TitleColor, Margin = new Padding(12, 0, 0, 0), MinimumSize = new Size(0, 40)
                };
                tips.SetToolTip(lblMsg, "Последнее сообщение. Весь журнал — «Параметры» → «Журнал событий»");
                var last = logHistory.LastOrDefault();
                if (last != null) ShowMsg(last);
                bar.Controls.Add(lblMsg, 1, 0);
            }
            AddRow(bar, new RowStyle(SizeType.AutoSize));
        }

        // ---------- павлодарский режим: кнопочный пульт «Енисей» ----------
        if (classicOn)
        {
            classic = new ClassicPanel(View, Scheme, SetMode, tips);
            classicRow = root.RowCount;
            AddRow(classic, new RowStyle(SizeType.Absolute, classic.NeededHeight(Math.Max(Width, 600))));
        }

        // ---------- низ: колонки ----------
        // [ИЗМЕНЕНО 08.10.2026] без журнала (пост) — имитация растягивается на свободное место
        bool routesOn = Show(UiSettings.Routes), swOn = Show(UiSettings.Switches), simOn = Show(UiSettings.Sim);
        bool panelBottom = panelCol != null && !strip;
        // перегоны, пригласительный и счётчики нужны всегда: в колонке стрелок, иначе в пульте ДСП
        bool linesInPanel = !swOn && panelCol != null;
        bool ctlOn = swOn || !linesInPanel;
        var cols = new List<(Control c, int width)>();   // width 0 — растянуть
        if (logCol) cols.Add((null, 0));
        if (routesOn) cols.Add((null, 440));
        if (ctlOn) cols.Add((null, swOn ? 340 : 300));
        if (panelBottom) cols.Add((null, 380));          // [ДОБАВЛЕНО 21.10.2026, этап 6] «Пульт ДСП»
        if (simOn) cols.Add((null, 370));
        TableLayoutPanel bottom = null;
        if (cols.Count > 0)
        {
            bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols.Count, RowCount = 1, BackColor = Bg };
            bool stretched = logCol;
            for (int i = 0; i < cols.Count; i++)
            {
                bool last = i == cols.Count - 1;
                bottom.ColumnStyles.Add(cols[i].width == 0 || (last && !stretched) ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Absolute, cols[i].width));
            }
            AddRow(bottom, new RowStyle(SizeType.Absolute, 290));
        }
        int col = 0;

        // 1) журнал (общий: сообщения и АКЖТ, и поста)
        if (logCol)
        {
            lstLog = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(225, 240, 238),
                Font = new Font("Consolas", 9.5f), IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 17,
                HorizontalScrollbar = true
            };
            lstLog.DrawItem += DrawLogItem;
            lstLog.Items.AddRange(logHistory.ToArray<object>());
            if (lstLog.Items.Count > 0) lstLog.TopIndex = lstLog.Items.Count - 1;
            bottom.Controls.Add(lstLog, col++, 0);
        }

        // 2) режим + маршруты
        if (routesOn)
        {
            var routesCol = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Bg, Padding = new Padding(6, 4, 0, 0) };
            routesCol.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            routesCol.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            routesCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var modes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            btnTrain = ModeButton("ПОЕЗДНЫЕ", () => SetMode(false));
            btnShunt = ModeButton("МАНЕВРОВЫЕ", () => SetMode(true));
            modes.Controls.Add(btnTrain);
            modes.Controls.Add(btnShunt);
            routesCol.Controls.Add(modes, 0, 0);
            routesCol.Controls.Add(SmallTitle("Маршруты (клик — задать / отменить):"), 0, 1);
            routesList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, BackColor = Bg, Margin = new Padding(0)
            };
            foreach (var r in View.Routes)
            {
                var route = r;
                var b = FlatButton(route.Name, 400, 30);
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Click += (_, _) => { if (route.Active) IL.CancelRoute(route); else IL.SetRoute(route); };
                routeBtns[route] = b;
                routesList.Controls.Add(b);
            }
            routesCol.Controls.Add(routesList, 0, 2);
            bottom.Controls.Add(routesCol, col++, 0);
        }

        // 3) стрелки, направление, пригласительный
        FlowLayoutPanel ctlCol = null;
        if (ctlOn)
        {
            ctlCol = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
                AutoScroll = true, BackColor = Bg, Padding = new Padding(6, 4, 0, 0)
            };
            if (swOn)
            {
                ctlCol.Controls.Add(Breaker(SmallTitle("Стрелки (ЛКМ — перевести, ПКМ — вспомогательный):")));
                foreach (var sw in View.Switches)
                {
                    var s = sw;
                    var b = FlatButton("", 150, 30);
                    b.TextAlign = ContentAlignment.MiddleLeft;
                    b.Click += (_, _) => IL.ToggleSwitch(s);
                    b.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) RailSchemePanel.AskAuxSwitch(this, s); };
                    tips.SetToolTip(b, $"{s.Title}\nЛКМ — перевести, ПКМ — вспомогательный перевод (при ложной занятости)");
                    swBtns[s] = b;
                    ctlCol.Controls.Add(b);
                }
            }
            bottom.Controls.Add(ctlCol, col++, 0);
        }
        if (linesInPanel && strip)
            AddLineControls(panelCol, c => { c.Margin = new Padding(c.Margin.Left, 2, 10, 2); return c; }, StripTitle);
        else AddLineControls(linesInPanel ? panelCol : ctlCol, Breaker, SmallTitle);

        // 3а) [ДОБАВЛЕНО 21.10.2026, этап 6] пульт ДСП: замыкание стрелок, автодействие, ИР, взрез, ключ-жезл, переезд
        if (panelBottom) bottom.Controls.Add(panelCol, col++, 0);

        // 4) имитация
        if (simOn) bottom.Controls.Add(BuildSim(), col++, 0);

        SetMode(shuntMode);
    }

    /// <summary>Перегоны (направление), пригласительные, счётчики ответственных команд.</summary>
    void AddLineControls(FlowLayoutPanel host, Func<Control, Control> brk, Func<string, Label> title)
    {
        foreach (var ln in View.Lines)
        {
            var line = ln;
            host.Controls.Add(brk(title(line.Title + ":")));
            var lbl = new Label { AutoSize = true, Font = new Font("Bahnschrift", 11f, FontStyle.Bold), ForeColor = TitleColor, Margin = new Padding(3, 6, 3, 3) };
            dirLabels[line] = lbl;
            host.Controls.Add(brk(lbl));
            var bDir = FlatButton("Сменить направление", 170, 30);
            bDir.Click += (_, _) => IL.ChangeDirection(line);
            var bDirAux = FlatButton("Вспом. смена…", 130, 30);
            bDirAux.Click += (_, _) =>
            {
                if (MessageBox.Show(this,
                        "ВСПОМОГАТЕЛЬНАЯ смена направления.\n\nПрименяется только при ложной занятости перегона.\n" +
                        "Дежурный обязан убедиться, что перегон свободен от поездов.",
                        "Ответственная команда", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                    IL.ChangeDirection(line, aux: true);
            };
            host.Controls.Add(bDir);
            host.Controls.Add(bDirAux);
        }
        if (View.InviteSignals.Count > 0)
        {
            host.Controls.Add(brk(title("Пригласительный:")));
            foreach (var sig in View.InviteSignals)
            {
                var s = sig;
                var b = FlatButton("ПС " + s.Label, 96, 30);
                b.Click += (_, _) =>
                {
                    if (s.InviteOn) IL.CancelInvite(s, "погашен дежурным");
                    else RailSchemePanel.AskInvite(this, s);
                };
                tips.SetToolTip(b, "Пригласительный сигнал (лунно-белый мигающий). Нужен заданный по стрелкам маршрут.\n" +
                                   "Кнопка со счётчиком: каждое нажатие записывается в журнал осмотра ДУ-46.");
                b.Width = 120;
                invBtns[s] = b;
                host.Controls.Add(b);
            }
        }
        // [ДОБАВЛЕНО 20.10.2026, этап 3] счётчики ответственных команд (как на пульте ЭЦ)
        lblCounters = new Label { AutoSize = true, Font = new Font("Consolas", 9.5f), ForeColor = TitleColor, Margin = new Padding(3, 6, 3, 3) };
        tips.SetToolTip(lblCounters, "Счётчики кнопок: ИР — искусственная разделка, ВК — вспомогательный перевод стрелки,\n" +
                                     "ВСН — вспомогательная смена направления. Каждое нажатие нужно записать в журнал ДУ-46.");
        host.Controls.Add(brk(lblCounters));
    }

    /// <summary>Колонка «Имитация (обучение)»: поставить / убрать поезда, выдержки.</summary>
    FlowLayoutPanel BuildSim()
    {
        var simCol = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
            BackColor = Bg, Padding = new Padding(6, 4, 0, 0), AutoScroll = true
        };
        simCol.Controls.Add(Breaker(SmallTitle("Имитация (обучение):")));
        foreach (var (text, action) in View.SpawnButtons)
        {
            var a = action;
            var b = FlatButton(text, 170, 30);
            b.Click += (_, _) => a();
            simCol.Controls.Add(b);
        }
        var bRemove = FlatButton("Убрать поезда", 170, 30);
        bRemove.Click += (_, _) => Trains.RemoveAll();
        simCol.Controls.Add(bRemove);

        if (View.TrainPlaces.Count > 0)
        {
            var cmbPlace = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Margin = new Padding(3, 5, 3, 3) };
            foreach (var (name, _) in View.TrainPlaces) cmbPlace.Items.Add(name);
            cmbPlace.SelectedIndex = 0;
            var bL = FlatButton("← поезд", 88, 30);
            var bR = FlatButton("поезд →", 88, 30);
            void Put(Dir d)
            {
                var sec = View.TrainPlaces[cmbPlace.SelectedIndex].section;
                Trains.Spawn(sec, d, shuntMode);   // [ИЗМЕНЕНО 20.10.2026] режим — по кнопке ПОЕЗДНЫЕ / МАНЕВРОВЫЕ
            }
            bL.Click += (_, _) => Put(Dir.Left);
            bR.Click += (_, _) => Put(Dir.Right);
            tips.SetToolTip(bL, "Поставить поезд на выбранный путь головой влево (к выходному Н).\n" +
                                "Режим поезда — как выбрано кнопкой ПОЕЗДНЫЕ / МАНЕВРОВЫЕ. Сменить режим или развернуть — ПКМ по поезду.");
            tips.SetToolTip(bR, "Поставить поезд на выбранный путь головой вправо (к выходному Ч).\n" +
                                "Режим поезда — как выбрано кнопкой ПОЕЗДНЫЕ / МАНЕВРОВЫЕ. Сменить режим или развернуть — ПКМ по поезду.");
            simCol.Controls.Add(Breaker(new Label { Text = "Поставить поезд на путь:", AutoSize = true, ForeColor = TitleColor, Margin = new Padding(3, 6, 3, 0) }));
            simCol.Controls.Add(cmbPlace);
            simCol.Controls.Add(bL);
            simCol.Controls.Add(bR);
        }

        foreach (var (text, get, set) in View.Toggles)
        {
            var s = set;
            var c = new CheckBox { Text = text, AutoSize = true, ForeColor = TitleColor };
            c.CheckedChanged += (_, _) => { if (c.Checked != get()) { s(c.Checked); IL.NotifyChanged(); } };
            toggles.Add((c, get));
            simCol.Controls.Add(Breaker(c));
        }
        chkAuto = new CheckBox { Text = "Соседи принимают автоматически", AutoSize = true, ForeColor = TitleColor, Checked = ModelHost.AutoNeighbours };
        chkAuto.CheckedChanged += (_, _) => { ModelHost.AutoNeighbours = chkAuto.Checked; ModelHost.MarkDirty(); };
        simCol.Controls.Add(Breaker(chkAuto));

        simCol.Controls.Add(new Label { Text = "с/участок:", AutoSize = true, ForeColor = TitleColor, Margin = new Padding(3, 6, 0, 0) });
        var numSpeed = new NumericUpDown { Minimum = 1, Maximum = 10, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)Trains.StepSeconds, Width = 52 };
        numSpeed.ValueChanged += (_, _) => Trains.StepSeconds = (double)numSpeed.Value;
        simCol.Controls.Add(numSpeed);
        simCol.Controls.Add(new Label { Text = "разделка, с:", AutoSize = true, ForeColor = TitleColor, Margin = new Padding(8, 6, 0, 0) });
        var numDelay = new NumericUpDown { Minimum = 3, Maximum = 180, Value = (decimal)IL.ArtificialReleaseSeconds, Width = 52 };
        numDelay.ValueChanged += (_, _) => IL.ArtificialReleaseSeconds = (double)numDelay.Value;
        tips.SetToolTip(numDelay, "Выдержка искусственной разделки поездного маршрута. В жизни ~180 с, на макете укорочено.");
        simCol.Controls.Add(numDelay);
        var numShunt = new NumericUpDown { Minimum = 1, Maximum = 60, Value = (decimal)IL.ShuntReleaseSeconds, Width = 46 };
        numShunt.ValueChanged += (_, _) => IL.ShuntReleaseSeconds = (double)numShunt.Value;
        tips.SetToolTip(numShunt, "Выдержка разделки маневрового маршрута. В жизни ~60 с.");
        simCol.Controls.Add(new Label { Text = "манёвр.:", AutoSize = true, ForeColor = TitleColor, Margin = new Padding(4, 6, 0, 0) });
        simCol.Controls.Add(numShunt);

        lblTrains = new Label { AutoSize = true, MaximumSize = new Size(345, 0), ForeColor = UiSettings.Mode == UiMode.Work ? TitleColor : Color.FromArgb(25, 45, 90) };
        simCol.Controls.Add(Breaker(lblTrains));
        return simCol;
    }

    Label StripTitle(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Bahnschrift", 9.5f, FontStyle.Bold),
        ForeColor = TitleColor, Margin = new Padding(8, 9, 2, 2)
    };

    void UpdateClock()
    {
        if (lblClock == null || lblClock.IsDisposed) return;
        var now = DateTime.Now;
        string t = Training.Active ? $"{Training.ClockText}   урок" : $"{now:HH:mm:ss}   {now:dd.MM.yyyy}";
        SetText(lblClock, t);
    }

    void ShowMsg(LogItem it)
    {
        if (lblMsg == null) return;
        SetText(lblMsg, L.T(it.Text));
        var c = it.Kind == LogKind.Refuse ? Color.FromArgb(255, 120, 110) : TitleColor;
        if (UiSettings.Mode != UiMode.Work && it.Kind == LogKind.Refuse) c = Color.FromArgb(170, 0, 0);
        if (lblMsg.ForeColor != c) lblMsg.ForeColor = c;
    }

    /// <summary>В FlowLayoutPanel с переносом — поставить элемент отдельной строкой.</summary>
    Control Breaker(Control c)
    {
        c.Margin = new Padding(c.Margin.Left, c.Margin.Top + 2, c.Margin.Right, c.Margin.Bottom);
        c.ParentChanged += (_, _) =>
        {
            if (c.Parent is FlowLayoutPanel fp)
            {
                int i = fp.Controls.GetChildIndex(c);
                if (i > 0) fp.SetFlowBreak(fp.Controls[i - 1], true);
                fp.SetFlowBreak(c, true);
            }
        };
        return c;
    }

    Label SmallTitle(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Bahnschrift", 10f, FontStyle.Bold),
        ForeColor = TitleColor, Margin = new Padding(3, 6, 3, 2)
    };

    Button FlatButton(string text, int w, int h)
    {
        var b = new Button
        {
            Text = text, Size = new Size(w, h), Margin = new Padding(2), FlatStyle = FlatStyle.Flat,
            BackColor = BtnLight, UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    Button PinkButton(string text, Action click)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
            BackColor = Pink, ForeColor = Color.White, Font = new Font("Bahnschrift", 9f, FontStyle.Bold),
            Padding = new Padding(4, 2, 4, 2), Margin = new Padding(6, 0, 0, 0), UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => click();
        return b;
    }

    Button ModeButton(string text, Action click)
    {
        var b = new Button
        {
            Text = text, Width = 200, Height = 48, FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
            Font = new Font("Bahnschrift", 12f, FontStyle.Bold), Margin = new Padding(2, 2, 6, 4), UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => click();
        return b;
    }

    // =================================================================
    //  РЕЖИМ: ПОЕЗДНЫЕ / МАНЕВРОВЫЕ
    // =================================================================
    void SetMode(bool shunt)
    {
        if (shunt && !View.Routes.Any(r => r.Shunting))
        {
            AddLog("Здесь нет маневровых светофоров — маневровые маршруты не предусмотрены.", LogKind.Refuse);
            return;
        }
        shuntMode = shunt;
        if (btnTrain != null) btnTrain.BackColor = shunt ? ModeOff : ModeOn;
        if (btnShunt != null) btnShunt.BackColor = shunt ? ModeOn : ModeOff;
        routesList?.SuspendLayout();
        foreach (var (r, b) in routeBtns) b.Visible = r.Shunting == shunt || r.Active;
        routesList?.ResumeLayout();
        Scheme.ClearSelection();
    }

    // =================================================================
    //  ОБНОВЛЕНИЕ
    // =================================================================
    void OnModelChanged()
    {
        dirty = true;
        if (!Visible || FindForm() is { WindowState: FormWindowState.Minimized }) return;
        dirty = false;
        RefreshUi();
        Scheme.Invalidate();
    }

    void RefreshUi()
    {
        bool blink = ModelHost.BlinkPhase;

        foreach (var (r, b) in routeBtns)
        {
            string state;
            Color c;
            if (!r.Active) { state = ""; c = BtnLight; }
            else if (r.Releasing) { state = $"  — {(r.CancelKind.Length > 0 ? "отмена " + r.CancelKind : "разделка")} {Math.Ceiling(r.ArtificialTimer)} с"; c = RouteReleasing; }
            else if (r.TrainEntered) { state = r.Shunting ? "  — манёвры" : "  — поезд в маршруте"; c = RouteTrain; }
            else if (r.SetupTimer > 0) { state = "  — устанавливается"; c = RouteSet; }
            else if (IL.CanOpen(r)) { state = "  — открыт"; c = r.Shunting ? RouteShuntOpen : RouteOpen; }
            else { state = "  — задан, светофор закрыт"; c = RouteSet; }
            string text = r.Name + state;
            if (b.Text != text) b.Text = text;
            if (b.BackColor != c) b.BackColor = c;
            bool vis = r.Shunting == shuntMode || r.Active;
            if (b.Visible != vis) b.Visible = vis;
        }

        foreach (var (sw, b) in swBtns)
        {
            // [ИЗМЕНЕНО 21.10.2026, этап 6] положение — как на пульте (у стрелки на макете — макета), пометки К З Л В М
            string st = sw.ShownPlus ? "+" : "−";
            string extra = sw.Cut ? " ВЗРЕЗ" : !sw.Controlled ? (sw.MoveTimer > 0 ? " перевод…" : " нет контроля") : sw.Locked ? " заперта" : "";
            string marks = PanelUi.Marks(sw);
            SetText(b, $"Стр. {sw.Label}: {st}{extra}{(marks.Length > 0 ? " [" + marks + "]" : "")}");
            SetBack(b, !sw.Controlled ? (blink ? Color.FromArgb(255, 200, 200) : BtnLight)
                        : sw.Locked ? LockBtn
                        : sw.Plus ? Color.FromArgb(180, 225, 180) : Color.FromArgb(160, 200, 235));
        }

        foreach (var (s, b) in secBtns)
        {
            SetBack(b, s.Occupied ? OccBtn : s.LockedBy != null || s.LineRoute ? LockBtn : BtnLight);
            var fc = s.Occupied ? Color.White : Color.Black;
            if (b.ForeColor != fc) b.ForeColor = fc;
            // [ИЗМЕНЕНО 20.10.2026, этап 3] подсказка «(поезд)» — только когда участок занят: при ложной свободности не выдаём её
            SetText(b, s.Label + (!s.Occupied ? "" : s.TrainCount > 0 && !s.FaultFree ? " (поезд)" : s.ManualOcc ? " (вручн.)" : s.HwOcc && !s.FaultFree ? " (датчик)" : s.RemoteOcc ? " (сосед)" : ""));
        }

        foreach (var (s, b) in invBtns)
        {
            SetBack(b, s.InviteOn ? (blink ? Color.White : Color.FromArgb(210, 210, 230)) : BtnLight);
            SetText(b, $"ПС {s.Label}  {Du46.Counter("ПС:" + s.Id):0000}");
        }
        SetText(lblCounters, $"ИР {Du46.Counter("ИР"):0000}   ВК {Du46.Counter("ВК"):0000}" +
                             string.Concat(View.Lines.Select(l => $"   ВСН {Du46.Counter("ВСН:" + l.Id):0000}")));
        panelRefresh?.Invoke();
        classic?.RefreshUi();

        foreach (var (line, lbl) in dirLabels)
            SetText(lbl, (line.Current == Dir.Right ? "→ " + line.RightName : "← " + line.LeftName) +
                         (line.AuxRunning ? $"   ВСП: {Math.Ceiling(line.AuxTimer)} с" : "") +
                         (line.Syncing ? $"   согласование: {Math.Ceiling(line.SyncTimer)} с" : ""));

        foreach (var (box, get) in toggles)
            if (box.Checked != get()) box.Checked = get();
        if (chkAuto != null && chkAuto.Checked != ModelHost.AutoNeighbours) chkAuto.Checked = ModelHost.AutoNeighbours;

        if (lblTrains != null) SetText(lblTrains, Trains.Trains.Count == 0
            ? "Поездов нет."
            : string.Join("\n", Trains.Trains.Select(t =>
                $"№{t.Number} {(t.Dir == Dir.Right ? "→" : "←")} {t.Head?.Label}{(t.Waiting.Length > 0 ? ": " + t.Waiting : "")}")));
    }

    // менять свойства только если они реально изменились — меньше мерцания и перерасчёта раскладки
    static void SetText(Control c, string t) { if (c.Text != t) c.Text = t; }
    static void SetBack(Control c, Color col) { if (c.BackColor != col) c.BackColor = col; }
    // =================================================================
    //  ЖУРНАЛ (общий для обоих окон)
    // =================================================================
    public void AddLog(string msg, LogKind kind)
    {
        if (IsDisposed) return;
        if (!showLog)
        {
            // своего журнала нет — сообщение этой половины в общий журнал (с пометкой, откуда)
            LogForward?.Invoke($"[{View.Caption}] {msg}", kind);
            return;
        }
        // [ИЗМЕНЕНО 28.10.2026] журнал хранится всегда; колонка журнала может быть скрыта — тогда строка сообщения
        var it = new LogItem($"{DateTime.Now:HH:mm:ss}  {msg}", kind);
        logHistory.Add(it);
        if (logHistory.Count > 500) logHistory.RemoveAt(0);
        ShowMsg(it);
        if (lstLog == null) return;
        lstLog.Items.Add(it);
        while (lstLog.Items.Count > 500) lstLog.Items.RemoveAt(0);
        lstLog.TopIndex = lstLog.Items.Count - 1;
    }

    void DrawLogItem(object sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= lstLog.Items.Count) return;
        var it = (LogItem)lstLog.Items[e.Index];
        var c = it.Kind switch
        {
            LogKind.Refuse => Color.FromArgb(170, 0, 0),
            LogKind.Aux => Color.FromArgb(120, 40, 140),
            LogKind.Train => Color.FromArgb(20, 60, 150),
            _ => Color.Black
        };
        using var b = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : c);
        e.Graphics.DrawString(L.T(it.Text), e.Font, b, e.Bounds.Location);   // [ДОБАВЛЕНО 22.10.2026, этап 8]
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && dirty) OnModelChanged();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // высота кнопочного пульта зависит от ширины (ряды кнопок переносятся)
        if (classic != null && root != null && classicRow < root.RowStyles.Count)
        {
            int h = classic.NeededHeight(Width);
            if ((int)root.RowStyles[classicRow].Height != h) root.RowStyles[classicRow].Height = h;
        }
        if (dirty && Visible && Scheme != null) OnModelChanged();
    }
}
