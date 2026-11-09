using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

namespace StationApp;

// =====================================================================
//  ГЛАВНОЕ ОКНО: АКЖТ + ПУТЕВОЙ ПОСТ В ОДНОМ ОКНЕ
//  [ИЗМЕНЕНО 06.10.2026] Раньше было два отдельных окна (АКЖТ и пост).
//  Теперь окно одно:
//    * два монитора — окно растягивается на оба (без рамки), половина АКЖТ ровно на
//      основном мониторе, половина поста — на втором;
//    * один монитор — окно развёрнуто, обе половины рядом; кнопка «Вид» показывает
//      обе / только АКЖТ / только пост.
//  Верхняя полоса общая: порт Arduino, датчики, «Упрощённый» (сразу для обеих схем),
//  «Проверка», «Убрать всё», «Выход». В каждой половине — свои маршруты, стрелки, имитация.
// =====================================================================

class MainForm : Form
{
    static readonly Color Bg = RailSchemePanel.Bg;
    static readonly Color Pink = Color.FromArgb(219, 0, 105);
    static readonly Color TitleColor = Color.FromArgb(30, 45, 45);

    static Interlocking IL => ModelHost.Model.IL;

    readonly StationPane akzht, post;
    readonly TableLayoutPanel body;
    readonly Label lblArduino, lblSensors, lblServer;
    readonly ComboBox cmbPort;
    readonly Button btnSimple, btnView;
    readonly ToolTip tips = new();
    bool updatingPorts;
    bool spanning;              // окно растянуто на два монитора
    int viewMode;               // 0 — обе половины, 1 — только АКЖТ, 2 — только пост
    bool akzhtFirst = true;     // АКЖТ в левой колонке

    public MainForm()
    {
        Text = "Станция АКЖТ + путевой пост — ЭКСПЕРИМЕНТАЛЬНАЯ ВЕРСИЯ (Lab)";   // [LAB]
        BackColor = Bg;
        Font = new Font("Bahnschrift", 10f);
        MinimumSize = new Size(1200, 780);
        StartPosition = FormStartPosition.Manual;

        bool depot = ModelHost.Model.Akzht.Depot;
        // [ИЗМЕНЕНО 08.10.2026] у АКЖТ список участков слева, у поста — справа: схемы стыкуются по границе
        // мониторов. Журнал один — у АКЖТ, в нём и сообщения поста.
        akzht = new StationPane(ModelHost.Model.Akzht.View, sectionsOnLeft: true) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        post = new StationPane(ModelHost.Model.Post.View, showLog: false) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        post.LogForward = akzht.AddLog;
        akzht.AddHeaderButton(depot ? "Депо: показано" : "Депо: скрыто", ToggleDepot);
        StationPane.SameTrackLevel(akzht, post);   // пути I и II обеих схем — на одной высоте экрана

        // ---------- общая верхняя полоса ----------
        var top = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Bg };
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(10, 10, 0, 0) };
        lblArduino = new Label
        {
            AutoSize = true, BackColor = Color.White, ForeColor = Color.Red,
            Font = new Font("Bahnschrift", 10f, FontStyle.Bold), Padding = new Padding(3), Margin = new Padding(3, 3, 12, 3)
        };
        cmbPort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, DropDownWidth = 360, Margin = new Padding(3, 4, 12, 3) };
        cmbPort.DropDown += (_, _) => RefreshPorts();
        cmbPort.SelectedIndexChanged += (_, _) => { if (!updatingPorts) ArduinoLink.Connect(ArduinoLink.PortFromItem(cmbPort.SelectedItem?.ToString())); };
        tips.SetToolTip(cmbPort, "Порт Arduino АКЖТ (одна плата на АКЖТ и путевой пост)");
        left.Controls.Add(lblArduino);
        left.Controls.Add(cmbPort);

        // [ДОБАВЛЕНО 12.10.2026] связь диспетчеров (сервер PromServer)
        lblServer = new Label
        {
            AutoSize = true, BackColor = Color.White, Cursor = Cursors.Hand,
            Font = new Font("Bahnschrift", 10f, FontStyle.Bold), Padding = new Padding(3), Margin = new Padding(3, 3, 6, 3)
        };
        lblServer.Click += (_, _) => OpenServerDialog();
        tips.SetToolTip(lblServer, "Связь с Алгабасом через сервер PromServer. Клик — настроить.");
        left.Controls.Add(lblServer);
        var bServer = PinkButton("Связь…", OpenServerDialog);
        bServer.Margin = new Padding(0, 3, 12, 3);
        left.Controls.Add(bServer);
        // [LAB] заметная метка: это не рабочая программа
        left.Controls.Add(new Label
        {
            Text = "ЭКСПЕРИМЕНТ", AutoSize = true, BackColor = Color.FromArgb(255, 196, 110), ForeColor = Color.Black,
            Font = new Font("Bahnschrift", 10f, FontStyle.Bold), Padding = new Padding(3), Margin = new Padding(0, 3, 12, 3)
        });

        lblSensors = new Label
        {
            Dock = DockStyle.Bottom, Height = 22, AutoEllipsis = true, ForeColor = TitleColor,
            Font = new Font("Bahnschrift", 9.5f, FontStyle.Bold), Padding = new Padding(16, 2, 0, 0)
        };
        tips.SetToolTip(lblSensors, "Датчики занятости с макета. '0' от Arduino = участок занят.\n" +
                                    "Если тут «данных нет» — проверьте прошивку AKZHT_Main и провода к пинам 10 и 8.");

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 10, 10, 0) };
        btnSimple = PinkButton("Включить упрощённый", ToggleSimple);
        btnView = PinkButton("Вид: обе схемы", CycleView);
        tips.SetToolTip(btnView, "Обе схемы / только АКЖТ / только путевой пост (удобно, если монитор один)");
        right.Controls.Add(btnView);
        right.Controls.Add(btnSimple);
        right.Controls.Add(PinkButton("Проверка", RunTest));
        right.Controls.Add(PinkButton("Убрать всё", ResetAll));
        right.Controls.Add(PinkButton("Выход", Close));

        top.Controls.Add(right);
        top.Controls.Add(left);
        top.Controls.Add(lblSensors);

        // ---------- две половины ----------
        body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Bg, Margin = new Padding(0), Padding = new Padding(0) };
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        Controls.Add(body);
        Controls.Add(top);

        PlaceOnScreens();

        ModelHost.Changed += OnModelChanged;
        ArduinoLink.StatusChanged += OnArduinoStatus;
        DispatchLink.StatusChanged += RefreshServer;
        DispatchLink.PeersChanged += RefreshServer;
        RefreshPorts();
        OnArduinoStatus();
        RefreshServer();
        RefreshTop();

        akzht.AddLog($"Пульт запущен: АКЖТ и путевой пост в одном окне ({(spanning ? "на двух мониторах" : "на одном мониторе")}, " +
                     $"{(depot ? "вариант С ДЕПО" : "вариант БЕЗ ДЕПО")}).", LogKind.Info);
        akzht.AddLog("Левая горловина АКЖТ (Ч, Н1–Н5, М2–М10, стрелки 2/4…16) на макете физически отсутствует — работает только на экране.", LogKind.Aux);
        akzht.AddLog("Связь с Алгабасом — кнопка «Связь…» вверху: светофоры Ч и 2 Алгабаса приходят от Алгабаса, НА и 1 поста уходят к Алгабасу.", LogKind.Aux);
        // [ДОБАВЛЕНО 12.10.2026] запрос Алгабаса на смену направления — окном «Дать согласие / Отказать»
        ModelHost.Dispatch.AskConsent = r => DirectionConsentDialog.Ask(this, r);
        DispatchLink.StartFromSettings();
    }

    // =================================================================
    //  СВЯЗЬ ДИСПЕТЧЕРОВ
    // =================================================================
    void RefreshServer()
    {
        if (IsDisposed) return;
        var d = ModelHost.Dispatch;
        if (lblServer.Text != d.StatusText) lblServer.Text = d.StatusText;
        var c = d.StatusLevel switch { 2 => Color.FromArgb(0, 140, 60), 1 => Color.Red, _ => Color.Gray };
        if (lblServer.ForeColor != c) lblServer.ForeColor = c;
    }

    void OpenServerDialog() => ServerDialog.Open(this, ModelHost.Dispatch.PeerName);

    // =================================================================
    //  РАЗМЕЩЕНИЕ: один или два монитора
    // =================================================================
    void PlaceOnScreens()
    {
        var screens = Screen.AllScreens;
        var primary = Screen.PrimaryScreen ?? screens[0];
        var second = screens.Where(s => !s.Primary)
                            .OrderBy(s => Math.Abs(s.Bounds.Left - primary.Bounds.Right) + Math.Abs(s.Bounds.Right - primary.Bounds.Left))
                            .FirstOrDefault();

        // второй монитор должен стоять слева или справа от основного (одной полосой)
        bool sideBySide = second != null &&
                          (second.Bounds.Left == primary.Bounds.Right || second.Bounds.Right == primary.Bounds.Left);
        if (sideBySide)
        {
            spanning = true;
            akzhtFirst = primary.Bounds.Left < second.Bounds.Left;     // АКЖТ — на основном мониторе
            var l = akzhtFirst ? primary : second;
            var r = akzhtFirst ? second : primary;
            FormBorderStyle = FormBorderStyle.None;
            int top = Math.Min(l.WorkingArea.Top, r.WorkingArea.Top);
            int bottom = Math.Min(l.WorkingArea.Bottom, r.WorkingArea.Bottom);   // чтобы окно не уходило под панель задач
            Bounds = new Rectangle(l.WorkingArea.Left, top, r.WorkingArea.Right - l.WorkingArea.Left, bottom - top);
            body.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, l.WorkingArea.Right - l.WorkingArea.Left);
            body.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);
        }
        else
        {
            spanning = false;
            var wa = primary.WorkingArea;
            Location = new Point(wa.Left + 40, wa.Top + 40);
            Size = new Size(Math.Min(1600, wa.Width - 80), Math.Min(950, wa.Height - 80));
            WindowState = FormWindowState.Maximized;
        }
        ApplyView();
    }

    void ApplyView()
    {
        body.SuspendLayout();
        body.Controls.Clear();
        var first = akzhtFirst ? akzht : post;
        var secondPane = akzhtFirst ? post : akzht;
        if (viewMode == 0)
        {
            body.ColumnCount = 2;
            if (!spanning)
            {
                body.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 50);
                body.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 50);
            }
            body.Controls.Add(first, 0, 0);
            body.Controls.Add(secondPane, 1, 0);
            akzht.Visible = post.Visible = true;
            // АКЖТ слева — прижимаем её схему вправо, пост влево: пути переходят с монитора на монитор
            akzht.SchemeAlign = akzhtFirst ? StringAlignment.Far : StringAlignment.Center;
            post.SchemeAlign = akzhtFirst ? StringAlignment.Near : StringAlignment.Center;
        }
        else
        {
            var only = viewMode == 1 ? akzht : post;
            body.ColumnCount = 1;
            body.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
            body.Controls.Add(only, 0, 0);
            only.Visible = true;
            only.SchemeAlign = StringAlignment.Center;
        }
        body.ResumeLayout();
        btnView.Text = viewMode switch { 0 => "Вид: обе схемы", 1 => "Вид: только АКЖТ", _ => "Вид: только пост" };
    }

    void CycleView()
    {
        viewMode = (viewMode + 1) % 3;
        if (spanning && viewMode == 0) { PlaceOnScreens(); return; }   // вернуть точное деление по мониторам
        ApplyView();
    }

    // =================================================================
    //  КНОПКИ ВЕРХНЕЙ ПОЛОСЫ
    // =================================================================
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

    void ToggleSimple()
    {
        bool v = !akzht.Simple;
        akzht.Simple = v;          // упрощённый вид — сразу у обеих схем
        post.Simple = v;
        RefreshTop();
    }

    void ResetAll()
    {
        ModelHost.Model.Trains.RemoveAll();
        IL.ResetAll();
        akzht.ClearSelection();
        post.ClearSelection();
    }

    void RunTest()
    {
        akzht.AddLog("Проверка ламп (1,5 с)", LogKind.Info);
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

    void ToggleDepot()
    {
        bool depot = ModelHost.Model.Akzht.Depot;
        var res = MessageBox.Show(this,
            (depot ? "Скрыть депо (путь 7, стрелки 9/11, светофор М9)?" : "Показать депо (путь 7, стрелки 9/11, светофор М9)?") +
            "\n\nДепо на макете пока нет — это проектная часть.\n" +
            "Программа перезапустится, все маршруты и поезда будут сброшены.",
            "Вариант схемы", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (res != DialogResult.OK) return;
        ModelHost.SaveDepotSetting(!depot);
        ModelHost.Stop();
        Application.Restart();
    }

    void RefreshTop()
    {
        string s = akzht.Simple ? "Выключить упрощённый" : "Включить упрощённый";
        if (btnSimple.Text != s) btnSimple.Text = s;
        string sens = ModelHost.SensorStatusText;
        if (lblSensors.Text != sens) lblSensors.Text = sens;
        RefreshServer();     // «направление не совпадает» зависит и от нашего направления
    }

    void OnModelChanged()
    {
        if (IsDisposed || WindowState == FormWindowState.Minimized) return;
        RefreshTop();
    }

    // =================================================================
    //  ARDUINO
    // =================================================================
    void OnArduinoStatus()
    {
        if (IsDisposed) return;
        bool ok = ArduinoLink.IsConnected;
        lblArduino.Text = ok ? $"Arduino: {ArduinoLink.PortName}" : "Arduino: нет соединения";
        lblArduino.ForeColor = ok ? Color.FromArgb(0, 140, 60) : Color.Red;
        RefreshPorts();
    }

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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ModelHost.Changed -= OnModelChanged;
        ArduinoLink.StatusChanged -= OnArduinoStatus;
        DispatchLink.StatusChanged -= RefreshServer;
        DispatchLink.PeersChanged -= RefreshServer;
        base.OnFormClosed(e);
    }
}
