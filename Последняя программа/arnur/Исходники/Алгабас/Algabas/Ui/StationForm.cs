using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  ОКНО ПУЛЬТА — одно на все станции.
//  Что показывать (схему, маршруты, стрелки, участки, кнопки имитации)
//  берётся из StationView. Логику ведёт общая модель (ModelHost), окно
//  только показывает её и передаёт команды дежурного.
//  [ИЗМЕНЕНО 28.10.2026] Окно = верхняя полоса (порт Arduino, связь, датчики, кнопки) +
//  панель станции Ui/StationPane.cs — та же, что у половин окна АКЖТ (файл одинаковый в обеих программах).
//  Поэтому режимы интерфейса («Параметры»: инженерный, учебный, рабочий, павлодарский) — как у АКЖТ.
// =====================================================================

class StationForm : Form
{
    static Color Bg => RailSchemePanel.Bg;
    static readonly Color Pink = Color.FromArgb(219, 0, 105);
    static Color TitleColor => SchemePalette.Current.Title;

    protected readonly StationView View;
    protected static Interlocking IL => ModelHost.Model.IL;
    protected static TrainSim Trains => ModelHost.Model.Trains;
    protected readonly StationPane Pane;

    /// <summary>Второе окно (АКЖТ ↔ пост) — для кнопки переключения.</summary>
    public StationForm Other;
    /// <summary>Текст кнопки перехода во второе окно.</summary>
    protected string OtherButtonText = "";
    /// <summary>Главное окно: при закрытии завершает программу. Остальные только прячутся.</summary>
    protected bool IsMainWindow;

    bool reallyClose;

    readonly Panel topBar;
    readonly Label lblArduino;
    readonly Label lblServer;
    readonly Label lblSensors;
    readonly ComboBox cmbPort;
    bool updatingPorts;
    readonly Button btnSimple, btnParams, btnDu46, btnJournals, btnTest;
    // [ДОБАВЛЕНО 06.11.2026] ИИ-помощник (папка Ai, как у АКЖТ): окно вопросов, кнопка «ИИ» вверху
    readonly Button btnAi;
    Ai.AiChatWindow aiWindow;
    readonly FlowLayoutPanel topRight;
    readonly ToolTip tips = new();
    SchemePalette shownPalette = SchemePalette.Current;

    public StationForm(StationView view)
    {
        View = view;
        Text = view.Title;
        BackColor = Bg;
        Font = new Font("Bahnschrift", 10f);
        MinimumSize = new Size(1200, 780);
        Size = new Size(1600, 950);
        StartPosition = FormStartPosition.Manual;

        Pane = new StationPane(view) { Dock = DockStyle.Fill, Margin = new Padding(0) };

        // ---------- верхняя полоса ----------
        topBar = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Bg };
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(10, 10, 0, 0) };
        lblArduino = new Label
        {
            AutoSize = true, BackColor = Color.White, ForeColor = Color.Red,
            Font = new Font("Bahnschrift", 10f, FontStyle.Bold), Padding = new Padding(3), Margin = new Padding(3, 3, 12, 3)
        };
        cmbPort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, DropDownWidth = 360, Margin = new Padding(3, 4, 12, 3) };
        cmbPort.DropDown += (_, _) => RefreshPorts();
        cmbPort.SelectedIndexChanged += (_, _) => { if (!updatingPorts) ArduinoLink.Connect(ArduinoLink.PortFromItem(cmbPort.SelectedItem?.ToString())); };
        tips.SetToolTip(cmbPort, "Порт Arduino станции Алгабас");
        left.Controls.Add(lblArduino);
        left.Controls.Add(cmbPort);

        // [ДОБАВЛЕНО 12.10.2026] связь диспетчеров (сервер PromServer)
        lblServer = new Label
        {
            AutoSize = true, BackColor = Color.White, Cursor = Cursors.Hand,
            Font = new Font("Bahnschrift", 10f, FontStyle.Bold), Padding = new Padding(3), Margin = new Padding(3, 3, 6, 3)
        };
        lblServer.Click += (_, _) => OpenServerDialog();
        tips.SetToolTip(lblServer, "Связь с соседней станцией через сервер PromServer. Клик — настроить.");
        left.Controls.Add(lblServer);
        var bServer = PinkButton("Связь…", OpenServerDialog);
        bServer.Margin = new Padding(0, 3, 12, 3);
        left.Controls.Add(bServer);
        // [ДОБАВЛЕНО 20.10.2026, этап 2] полоса занятия над окном: урок, студент, игровое время (видна, когда преподаватель на связи)
        var trainingBar = new TrainingBar(this, Color.FromArgb(35, 60, 80)) { Dock = DockStyle.Top };
        Controls.Add(trainingBar);
        Load += (_, _) => trainingBar.SendToBack();   // после всех панелей — встаёт самой верхней полосой

        // строка состояния датчиков занятости (что реально прислала Arduino)
        lblSensors = new Label
        {
            Dock = DockStyle.Bottom, Height = 22, AutoEllipsis = true, ForeColor = TitleColor,
            Font = new Font("Bahnschrift", 9.5f, FontStyle.Bold), Padding = new Padding(16, 2, 0, 0)
        };
        tips.SetToolTip(lblSensors, "Датчики занятости с макета (строка \"Data:\" от платы 74HC165). '0' от Arduino = участок занят.\n" +
                                    "Если тут «данных нет» — проверьте, что выбран порт Arduino Алгабаса и плата 165 подключена.");

        // «Отмена набора» и «Отменить маршрут» — в заголовке панели станции (StationPane)
        topRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 10, 10, 0) };
        btnParams = PinkButton("Параметры", () => { using var f = new UiParamsForm(); f.ShowDialog(this); });   // [ДОБАВЛЕНО 28.10.2026]
        tips.SetToolTip(btnParams, "Режим интерфейса (инженерный, учебный, рабочий, павлодарский) и какие панели показывать");
        topRight.Controls.Add(btnParams);
        btnAi = PinkButton("ИИ", ToggleAi);   // [ДОБАВЛЕНО 06.11.2026] «ИИ» — ключ перевода: kk «ЖИ», en «AI»
        btnAi.BackColor = Color.FromArgb(0, 120, 215);
        tips.SetToolTip(btnAi, "ИИ-помощник: вопросы про программу, маршруты, светофоры и термины (қазақша / по-русски)");
        topRight.Controls.Add(btnAi);
        topRight.Controls.Add(PinkButton("Убрать всё", ResetAll));
        btnDu46 = PinkButton("ДУ-46", () => Du46Ui.Open());   // [ДОБАВЛЕНО 20.10.2026, этап 3] журнал осмотра
        topRight.Controls.Add(btnDu46);
        // [ДОБАВЛЕНО 23.10.2026, этап 4] журналы ДУ-2/47/58/60, бланки, радиосвязь, предупреждения
        btnJournals = PinkButton("Журналы ▾", () => JournalsUi.ShowMenu(btnJournals));
        topRight.Controls.Add(btnJournals);
        btnTest = PinkButton("Проверка", RunTest);
        topRight.Controls.Add(btnTest);
        btnSimple = PinkButton("Включить упрощённый", ToggleSimple);
        topRight.Controls.Add(btnSimple);
        topRight.Controls.Add(LangUi.Selector());   // [ДОБАВЛЕНО 22.10.2026, этап 8] язык
        topBar.Controls.Add(topRight);
        topBar.Controls.Add(left);
        topBar.Controls.Add(lblSensors);   // добавлен последним → прижимается к низу первым, на всю ширину

        Controls.Add(Pane);
        Controls.Add(topBar);

        ModelHost.Changed += OnModelChanged;
        UiSettings.Changed += ApplyUiMode;
        ArduinoLink.StatusChanged += OnArduinoStatus;
        DispatchLink.StatusChanged += RefreshServer;
        DispatchLink.PeersChanged += RefreshServer;

        ApplyUiMode();
        if (UiSettings.Mode == UiMode.Work) Pane.Simple = true;
        RefreshPorts();
        OnArduinoStatus();
        RefreshServer();
        RefreshTop();
    }

    /// <summary>Добавить розовую кнопку в верхнюю панель (для окон-наследников).</summary>
    protected Button AddTopButton(string text, Action click)
    {
        var b = PinkButton(text, click);
        topRight.Controls.Add(b);
        topRight.Controls.SetChildIndex(b, 0);
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

    // =================================================================
    //  РЕЖИМ ИНТЕРФЕЙСА  [ДОБАВЛЕНО 28.10.2026]
    // =================================================================
    void ApplyUiMode()
    {
        if (IsDisposed) return;
        var mode = UiSettings.Mode;
        var pal = SchemePalette.Current;
        SchemePalette.Recolor(this, shownPalette, pal);
        if (lblSensors.ForeColor != pal.Title) lblSensors.ForeColor = pal.Title;
        BackColor = pal.Bg;
        if (shownPalette != pal) Pane.Simple = mode == UiMode.Work;   // рабочий режим — схема упрощённая, как на АРМ ДСП
        shownPalette = pal;
        bool hw = UiSettings.Show(UiSettings.Hw);
        lblArduino.Visible = cmbPort.Visible = lblSensors.Visible = hw;
        topBar.Height = hw ? 74 : 50;
        btnDu46.Visible = btnJournals.Visible = UiSettings.Show(UiSettings.Journals);
        btnTest.Visible = UiSettings.Show(UiSettings.Test);
        btnParams.Text = $"Параметры: {UiSettings.ModeName(mode)}";
        // [ИЗМЕНЕНО 09.11.2026] окно ИИ-помощника само не открывается ни в одном режиме — только кнопкой «ИИ»
        RefreshTop();
        Invalidate(true);
    }

    /// <summary>[ДОБАВЛЕНО 06.11.2026] Кнопка «ИИ»: показать / спрятать окно помощника (как у АКЖТ).</summary>
    void ToggleAi() => ShowAi(aiWindow == null || !aiWindow.Visible);

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

    // =================================================================
    //  ОБНОВЛЕНИЕ ВЕРХНЕЙ ПОЛОСЫ (панель станции обновляется сама)
    // =================================================================
    void OnModelChanged()
    {
        if (IsDisposed || !Visible || WindowState == FormWindowState.Minimized) return;
        RefreshTop();
    }

    void RefreshTop()
    {
        SetText(btnSimple, Pane.Simple ? "Выключить упрощённый" : "Включить упрощённый");
        SetText(lblSensors, ModelHost.SensorStatusText);
        RefreshServer();     // «направление не совпадает» зависит и от нашего направления
    }

    static void SetText(Control c, string t) => LangUi.SetText(c, t);   // [ИЗМЕНЕНО 09.11.2026] не мигать на kk / en

    // =================================================================
    //  КНОПКИ ВЕРХНЕЙ ПАНЕЛИ
    // =================================================================
    void ResetAll()
    {
        Trains.RemoveAll();
        IL.ResetAll();
        Pane.ClearSelection();
    }

    void ToggleSimple()
    {
        Pane.Simple = !Pane.Simple;
        RefreshTop();
    }

    void RunTest()
    {
        AddLog("Проверка ламп (1,5 с)", LogKind.Info);
        IL.TestMode = true;
        ArduinoLink.LampTest = true;
        ArduinoLink.PumpLights();
        ModelHost.MarkDirty();
        var t = new System.Windows.Forms.Timer { Interval = 1500 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            IL.TestMode = false;
            ArduinoLink.LampTest = false;
            ArduinoLink.PumpLights();
            ModelHost.MarkDirty();
        };
        t.Start();
    }

    protected void ShowOther()
    {
        if (Other == null) return;
        if (!Other.Visible) Other.Show();
        if (Other.WindowState == FormWindowState.Minimized) Other.WindowState = FormWindowState.Maximized;
        Other.Activate();
    }

    // =================================================================
    //  ЖУРНАЛ — в панели станции
    // =================================================================
    protected void AddLog(string msg, LogKind kind) => Pane.AddLog(msg, kind);

    // =================================================================
    //  ARDUINO (порт общий)
    // =================================================================
    void OnArduinoStatus()
    {
        if (IsDisposed) return;
        bool ok = ArduinoLink.IsConnected;
        lblArduino.Text = ok ? $"Arduino: {ArduinoLink.PortName}" : "Arduino: нет соединения";
        lblArduino.ForeColor = ok ? Color.FromArgb(0, 140, 60) : Color.Red;
        RefreshPorts();
    }

    // =================================================================
    //  СВЯЗЬ ДИСПЕТЧЕРОВ
    // =================================================================
    void RefreshServer()
    {
        if (IsDisposed) return;
        var d = ModelHost.Dispatch;
        SetText(lblServer, d.StatusText);
        var c = d.StatusLevel switch { 2 => Color.FromArgb(0, 140, 60), 1 => Color.Red, _ => Color.Gray };
        if (lblServer.ForeColor != c) lblServer.ForeColor = c;
    }

    void OpenServerDialog() => ServerDialog.Open(this, ModelHost.Dispatch.PeerName);

    void RefreshPorts()
    {
        updatingPorts = true;
        try
        {
            string current = ArduinoLink.PortName ?? "";
            // в списке рядом с портом подпись: "COM5 — USB (Arduino)", "COM3 — Bluetooth (это НЕ Arduino)"
            var items = ArduinoLink.PortItems();
            cmbPort.Items.Clear();
            cmbPort.Items.Add("");
            foreach (var n in items) cmbPort.Items.Add(n);
            int idx = current.Length == 0 ? 0 : items.FindIndex(x => ArduinoLink.PortFromItem(x) == current) + 1;
            cmbPort.SelectedIndex = idx > 0 ? idx : 0;
        }
        finally { updatingPorts = false; }
    }

    // =================================================================
    //  ЗАКРЫТИЕ: не главное окно прячется, логика работает дальше
    // =================================================================
    public void CloseForReal()
    {
        reallyClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!IsMainWindow && !reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (IsMainWindow) Other?.CloseForReal();     // главное окно закрыли — закрываем и второе
        ModelHost.Changed -= OnModelChanged;
        UiSettings.Changed -= ApplyUiMode;
        ArduinoLink.StatusChanged -= OnArduinoStatus;
        DispatchLink.StatusChanged -= RefreshServer;
        DispatchLink.PeersChanged -= RefreshServer;
        base.OnFormClosed(e);
    }
}
