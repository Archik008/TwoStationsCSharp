using StationApp.Core;

namespace TeacherApp;

// =====================================================================
//  ОКНО ПРЕПОДАВАТЕЛЯ  [ДОБАВЛЕНО 20.10.2026, этап 2]
//   «Занятие»  — станции на связи, кто за каким пультом, урок, пуск/пауза/ускорение,
//                экран выбранной станции, «Забрать управление»;
//   «Студенты» — список, пароли, разрешить студентам добавлять себя;
//   «Уроки»    — деловые игры (задание, поезда, время);
//   «Отчёты»   — фильтры, отчёты студента, подробный отчёт, сводная ведомость.
// =====================================================================

public class TeacherForm : Form
{
    static readonly Font F = new("Segoe UI", 10f);
    static readonly Font FB = new("Segoe UI", 10f, FontStyle.Bold);

    // --- занятие ---
    readonly Label lblConn = new() { AutoSize = true, Font = FB, Margin = new Padding(4, 8, 12, 0) };
    readonly ComboBox cbLesson = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    readonly Label lblLessonInfo = new() { AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = Color.DimGray };
    readonly Label lblClock = new() { AutoSize = true, Font = new Font("Segoe UI", 18f, FontStyle.Bold), Margin = new Padding(12, 0, 0, 0) };
    readonly Button btnBegin = Btn("Начать урок"), btnRun = Btn("▶ Пуск"), btnPause = Btn("❚❚ Пауза"), btnFinish = Btn("Закончить урок");
    readonly NumericUpDown numSpeed = new() { Minimum = 1, Maximum = 10, Value = 1, Width = 50 };
    readonly DataGridView gridSt = Grid();
    readonly Button btnAssign = Btn("Назначить студента…"), btnTake = Btn("Забрать управление");
    readonly Button btnFaults = Btn("Неисправности…");   // [ДОБАВЛЕНО 20.10.2026, этап 3] экспресс-вводные
    readonly Button btnBig = Btn("Экран на весь экран…");  // [ДОБАВЛЕНО 21.10.2026] экран станции в отдельном большом окне
    readonly Dictionary<string, StationScreenWindow> bigWins = new();
    readonly Dictionary<string, FaultsWindow> faultWins = new();
    readonly ListBox log = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
    readonly ScreenView screen = new() { Dock = DockStyle.Fill };
    readonly Label lblScreen = new() { Dock = DockStyle.Top, Height = 26, Font = FB, Padding = new Padding(4, 4, 0, 0) };
    string screenId;
    bool awaiting;
    long requestedAt, nextAt;

    // --- студенты ---
    readonly DataGridView gridStud = Grid();
    readonly CheckBox chkAllowAdd = new() { Text = "Разрешить студентам добавлять себя", AutoSize = true, Margin = new Padding(12, 8, 0, 0) };

    // --- уроки ---
    readonly ListBox lstLessons = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly PropertyGrid prop = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true };

    // --- отчёты ---
    readonly TextBox fName = new() { Width = 180 };
    readonly ComboBox fGroup = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly ComboBox fStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly ComboBox fDate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly DateTimePicker fFrom = new() { Width = 110, Format = DateTimePickerFormat.Short }, fTo = new() { Width = 110, Format = DateTimePickerFormat.Short };
    readonly DataGridView gridRep = Grid(), gridGames = Grid();

    public TeacherForm()
    {
        Text = "Преподаватель — тренажёр ДСП (АКЖТ, Алгабас)";
        Font = F;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 700);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(SessionTab());
        tabs.TabPages.Add(StudentsTab());
        tabs.TabPages.Add(LessonsTab());
        tabs.TabPages.Add(ReportsTab());
        tabs.TabPages.Add(ScheduleTab.Build(this, () => SaveLessons(lstLessons.SelectedItem as Lesson, save: false)));   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        tabs.TabPages.Add(GidTab.Build(this));   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedIndex == 3) FillReports(); };
        Controls.Add(tabs);

        Classroom.Log += m => { log.Items.Add(m); log.TopIndex = Math.Max(0, log.Items.Count - 1); };
        Classroom.Changed += RefreshSession;
        Classroom.Screen += OnScreen;
        var t = new System.Windows.Forms.Timer { Interval = 100 };
        t.Tick += (_, _) => ScreenLoop();
        t.Start();
        var t2 = new System.Windows.Forms.Timer { Interval = 500 };
        t2.Tick += (_, _) => RefreshSession();
        t2.Start();

        Classroom.Start();
        Classroom.Say($"Данные: {Db.Dir}");
        Classroom.Say($"Адрес для программ станций (кнопка «Связь…» на станции): {Classroom.LocalAddresses()}");
        RefreshSession();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (Classroom.Active && MessageBox.Show(this, "Урок не закончен. Выйти? Результаты этого урока не сохранятся.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        { e.Cancel = true; return; }
        DispatchLink.Stop(true);
        Classroom.StopServer();
        base.OnFormClosing(e);
    }

    static Button Btn(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(4), Padding = new Padding(4, 2, 4, 2) };

    static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window, AllowUserToResizeRows = false
    };

    static FlowLayoutPanel Row(params Control[] c)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4) };
        foreach (var x in c) { if (x is Label l && l.Margin == new Padding(3)) l.Margin = new Padding(4, 8, 0, 0); p.Controls.Add(x); }
        return p;
    }

    static Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(4, 8, 0, 0) };

    // =================================================================
    //  ЗАНЯТИЕ
    // =================================================================

    TabPage SessionTab()
    {
        var page = new TabPage("Занятие");
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        Shown += (_, _) => split.SplitterDistance = Math.Min(680, split.Width / 2);
        page.Controls.Add(split);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        split.Panel1.Controls.Add(left);

        var bConn = Btn("Связь…");
        bConn.Click += (_, _) => { using var d = new ServerDialog(); if (d.ShowDialog(this) == DialogResult.OK) Classroom.Reconnect(); };
        left.Controls.Add(Row(lblConn, bConn, StationApp.Ui.LangUi.Selector()));   // [ДОБАВЛЕНО 22.10.2026, этап 8] язык (уходит и станциям)

        var lessonBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Top, WrapContents = false, Padding = new Padding(4) };
        lessonBox.Controls.Add(Row(L("Урок:"), cbLesson));
        lessonBox.Controls.Add(lblLessonInfo);
        left.Controls.Add(lessonBox);
        FillLessonCombo();
        cbLesson.SelectedIndexChanged += (_, _) => ShowLessonInfo();

        btnBegin.Click += (_, _) => BeginLesson();
        btnRun.Click += (_, _) => Classroom.SetRunning(true);
        btnPause.Click += (_, _) => Classroom.SetRunning(false);
        btnFinish.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Закончить урок и собрать результаты со станций?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                Classroom.Finish();
        };
        numSpeed.ValueChanged += (_, _) => Classroom.SetSpeed((int)numSpeed.Value);
        left.Controls.Add(Row(btnBegin, btnRun, btnPause, L("Ускорение ×"), numSpeed, btnFinish, lblClock));

        gridSt.Columns.Add("st", "Станция (роль ДСП)");
        gridSt.Columns.Add("on", "Связь");
        gridSt.Columns.Add("stud", "Студент");
        gridSt.Columns.Add("r", "Принято");
        gridSt.Columns.Add("d", "Отправлено");
        gridSt.Columns.Add("c", "Крит.");
        gridSt.Columns.Add("w", "Ошибки");
        gridSt.Columns.Add("s", "Баллы");
        gridSt.Columns.Add("t", "Управление");
        gridSt.Columns[2].FillWeight = 220;
        gridSt.Columns[0].FillWeight = 160;
        gridSt.SelectionChanged += (_, _) => SelectStation();
        left.Controls.Add(gridSt);

        btnAssign.Click += (_, _) => AssignStudent();
        btnTake.Click += (_, _) =>
        {
            var st = SelectedStation();
            if (st != null) Classroom.SetTake(st, !st.Take);
        };
        btnFaults.Click += (_, _) => OpenFaults();
        btnBig.Click += (_, _) => OpenBigScreen();
        btnBig.Font = FB;
        gridSt.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenBigScreen(); };
        screen.DoubleClick += (_, _) => { if (!screen.InputEnabled) OpenBigScreen(); };
        left.Controls.Add(Row(btnBig, btnAssign, btnTake, btnFaults));
        left.Controls.Add(log);

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(screen);
        right.Controls.Add(lblScreen);
        split.Panel2.Controls.Add(right);
        screen.Input += d => { var st = SelectedStation(); if (st != null) Classroom.Input(st, d); };
        return page;
    }

    void FillLessonCombo()
    {
        var sel = (cbLesson.SelectedItem as Lesson)?.Id;
        cbLesson.Items.Clear();
        foreach (var l in Db.Lessons.OrderBy(l => l.Level)) cbLesson.Items.Add(l);
        cbLesson.SelectedItem = Db.Lessons.FirstOrDefault(l => l.Id == sel);
        if (cbLesson.SelectedIndex < 0 && cbLesson.Items.Count > 0) cbLesson.SelectedIndex = 0;
        ShowLessonInfo();
    }

    void ShowLessonInfo()
    {
        if (cbLesson.SelectedItem is not Lesson l) { lblLessonInfo.Text = ""; return; }
        lblLessonInfo.Text = $"{l.Description}\nЗадание: принять {l.Receive}, отправить {l.Depart}; крит. событий не больше {l.MaxCritical}; " +
                             $"зачёт от {l.PassScore}. Поездов подаёт программа: {l.AutoTrains}" +
                             (l.AutoTrains > 0 ? $" (каждые {l.EveryMin} мин)" : "") +
                             $". Начало {l.StartClock}" + (l.Minutes > 0 ? $", {l.Minutes} мин." : ".");
    }

    void BeginLesson()
    {
        if (cbLesson.SelectedItem is not Lesson l) return;
        var online = Classroom.Online.ToList();
        if (online.Count == 0) { MessageBox.Show(this, "Ни одной станции на связи. На ПК станций: «Связь…» → адрес этого ПК."); return; }
        if (Classroom.Active && MessageBox.Show(this, "Урок уже идёт. Начать заново? Текущие результаты не сохранятся.", Text,
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var noStudent = online.Where(s => s.Student == null).Select(s => s.Name).ToList();
        if (noStudent.Count > 0 && MessageBox.Show(this, $"Студент не вошёл: {string.Join(", ", noStudent)}.\nВсё равно начать?", Text,
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        numSpeed.Value = 1;
        Classroom.SetSpeed(1);
        Classroom.Begin(l);
    }

    StationRt SelectedStation() =>
        gridSt.SelectedRows.Count > 0 && gridSt.SelectedRows[0].Tag is string id && Classroom.Stations.TryGetValue(id, out var st) ? st : null;

    /// <summary>[ДОБАВЛЕНО 21.10.2026] Экран выбранной станции в отдельном окне на весь экран (одно окно на станцию).</summary>
    void OpenBigScreen()
    {
        var st = SelectedStation();
        if (st == null) { MessageBox.Show(this, "Выберите станцию в таблице."); return; }
        if (!bigWins.TryGetValue(st.Id, out var w) || w.IsDisposed)
        {
            bigWins[st.Id] = w = new StationScreenWindow(st);
            w.FormClosed += (_, _) => { nextAt = 0; awaiting = false; };
            w.Show();
        }
        if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Maximized;
        w.Activate();
    }

    bool BigOpen(StationRt st) => st != null && bigWins.TryGetValue(st.Id, out var w) && !w.IsDisposed && w.Visible;

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправности выбранной станции (одно окно на станцию).</summary>
    void OpenFaults()
    {
        var st = SelectedStation();
        if (st == null) { MessageBox.Show(this, "Выберите станцию в таблице."); return; }
        if (!faultWins.TryGetValue(st.Id, out var w) || w.IsDisposed)
        {
            faultWins[st.Id] = w = new FaultsWindow(st);
            w.Show(this);
        }
        w.BringToFront();
    }

    void AssignStudent()
    {
        var st = SelectedStation();
        if (st == null) { MessageBox.Show(this, "Выберите станцию в таблице."); return; }
        using var d = new PickStudentDialog(st.Name);
        if (d.ShowDialog(this) == DialogResult.OK)
            Classroom.SetStudent(st, d.Picked, d.Picked == null ? "студент снят преподавателем" : $"преподаватель назначил {d.Picked}");
    }

    void RefreshSession()
    {
        if (IsDisposed) return;
        // [ИЗМЕНЕНО 09.11.2026] подписи — через LangUi.SetText (как у станций): раньше на kk / en они раз в 0,5 с
        // ставились русскими и тут же переводились обратно — мигали, и раскрытый список языков закрывался
        StationApp.Ui.LangUi.SetText(lblConn, DispatchLink.IsConnected ? $"Сервер на связи ({Db.Settings.Server}). Адрес для станций: {Classroom.LocalAddresses()}"
                                                : $"Нет связи с сервером: {DispatchLink.LastError}");
        lblConn.ForeColor = DispatchLink.IsConnected ? Color.DarkGreen : Color.DarkRed;

        bool active = Classroom.Active;
        btnRun.Enabled = active && !Classroom.Running;
        btnPause.Enabled = active && Classroom.Running;
        btnFinish.Enabled = active;
        StationApp.Ui.LangUi.SetText(btnBegin, active ? "Начать заново" : "Начать урок");
        StationApp.Ui.LangUi.SetText(lblClock, active ? $"{Reports.Clock(Classroom.Clock)}:{(int)Classroom.Clock % 60:00}  {(Classroom.Running ? "×" + Classroom.Speed : "пауза")}" : "");
        lblClock.ForeColor = Classroom.Running ? Color.DarkGreen : Color.DarkOrange;

        var sel = SelectedStation()?.Id;
        var list = Classroom.Stations.Values.OrderBy(s => s.Name).ToList();
        while (gridSt.Rows.Count > list.Count) gridSt.Rows.RemoveAt(gridSt.Rows.Count - 1);
        while (gridSt.Rows.Count < list.Count) gridSt.Rows.Add();
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            var row = gridSt.Rows[i];
            row.Tag = s.Id;
            object[] v = { s.Name, s.Online ? "на связи" : "нет", s.Student?.ToString() ?? "— не вошёл —",
                           s.Received, s.Departed, s.Critical, s.Wrong, s.Score,
                           s.Take ? "У ПРЕПОДАВАТЕЛЯ" : s.ResultReceived ? "результат получен" : "студент" };
            for (int c = 0; c < v.Length; c++) if (!Equals(row.Cells[c].Value, v[c])) row.Cells[c].Value = v[c];
            row.DefaultCellStyle.ForeColor = s.Online ? Color.Black : Color.Gray;
            row.DefaultCellStyle.BackColor = s.Take ? Color.MistyRose : Color.White;
        }
        if (sel != null)
            foreach (DataGridViewRow r in gridSt.Rows) if ((string)r.Tag == sel && !r.Selected) r.Selected = true;

        var st = SelectedStation();
        btnTake.Enabled = st is { Online: true };
        StationApp.Ui.LangUi.SetText(btnTake, st is { Take: true } ? "Вернуть управление студенту" : "Забрать управление");
        btnAssign.Enabled = st != null;
        btnBig.Enabled = st is { Online: true };
        screen.InputEnabled = st is { Take: true, Online: true };
        StationApp.Ui.LangUi.SetText(lblScreen, st == null ? "Экран станции" :
            $"Экран: {st.Name} — {(st.Online ? (st.Take ? "управляете вы (мышь и клавиатура над картинкой)" : "только просмотр") : "нет связи")}" +
            (st.Online ? "   ·   двойной клик по таблице — экран на весь экран" : ""));
        lblScreen.ForeColor = screen.InputEnabled ? Color.OrangeRed : Color.Black;
    }

    // ---- экран выбранной станции: следующий кадр запрашивается, когда пришёл предыдущий ----
    void SelectStation()
    {
        var st = SelectedStation();
        if (st?.Id == screenId) return;
        screenId = st?.Id;
        awaiting = false;
        nextAt = 0;
        screen.Clear();
        screen.Placeholder = st == null ? "Выберите станцию в таблице" : "Ожидание экрана станции…";
    }

    void ScreenLoop()
    {
        SelectStation();
        var st = SelectedStation();
        if (st is not { Online: true } || !Visible) return;
        // открыт большой экран этой станции — маленький просмотр не грузит сеть
        if (BigOpen(st))
        {
            if (screen.Placeholder != BigText) { screen.Placeholder = BigText; screen.Clear(); }
            return;
        }
        if (screen.Placeholder == BigText) screen.Placeholder = "Ожидание экрана станции…";
        long now = Environment.TickCount64;
        if (awaiting && now - requestedAt < 4000) return;
        if (now < nextAt) return;
        awaiting = true;
        requestedAt = now;
        Classroom.RequestScreen(st, Math.Clamp(screen.Width, 800, 1920), null, "mini");
    }

    const string BigText = "Экран этой станции открыт в отдельном окне";

    void OnScreen(string id, ScreenFrame f)
    {
        if (id != screenId || f.Tag is not ("mini" or "")) return;
        string jpg = f.Jpg;
        awaiting = false;
        nextAt = Environment.TickCount64 + (screen.InputEnabled ? 150 : 400);
        try
        {
            var bytes = Convert.FromBase64String(jpg);
            screen.SetImage(Image.FromStream(new MemoryStream(bytes)));
        }
        catch { }
    }

    // =================================================================
    //  СТУДЕНТЫ
    // =================================================================

    TabPage StudentsTab()
    {
        var page = new TabPage("Студенты");
        gridStud.Columns.Add("n", "ФИО");
        gridStud.Columns.Add("g", "Группа");
        gridStud.Columns.Add("p", "Пароль");
        gridStud.Columns.Add("c", "Добавлен");
        gridStud.Columns[0].FillWeight = 250;
        gridStud.MultiSelect = true;
        page.Controls.Add(gridStud);

        var add = Btn("Добавить…");
        var edit = Btn("Изменить…");
        var del = Btn("Удалить");
        var pwd = Btn("Пароль…");
        add.Click += (_, _) => EditStudent(null);
        edit.Click += (_, _) => { var s = SelStudents().FirstOrDefault(); if (s != null) EditStudent(s); };
        gridStud.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditStudent((Student)gridStud.Rows[e.RowIndex].Tag); };
        del.Click += (_, _) =>
        {
            var sel = SelStudents().ToList();
            if (sel.Count == 0 || MessageBox.Show(this, $"Удалить студентов: {sel.Count}? Их отчёты сохранятся.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Db.Students.RemoveAll(sel.Contains);
            SaveStudents();
        };
        pwd.Click += (_, _) =>
        {
            var sel = SelStudents().ToList();
            if (sel.Count == 0) return;
            var p = Prompt.Ask(this, "Пароль", $"Новый пароль для: {string.Join(", ", sel.Select(s => s.Name))}\n(пусто — без пароля)", "", true);
            if (p == null) return;
            foreach (var s in sel) s.SetPassword(p);
            SaveStudents();
        };
        chkAllowAdd.Checked = Db.Settings.AllowAdd;
        chkAllowAdd.CheckedChanged += (_, _) => { Db.Settings.AllowAdd = chkAllowAdd.Checked; Db.SaveSettings(); Classroom.BroadcastStudents(); };
        page.Controls.Add(Row(add, edit, del, pwd, chkAllowAdd));
        FillStudents();
        return page;
    }

    IEnumerable<Student> SelStudents() => gridStud.SelectedRows.Cast<DataGridViewRow>().Select(r => (Student)r.Tag);

    void FillStudents()
    {
        gridStud.Rows.Clear();
        foreach (var s in Db.Students.OrderBy(s => s.Group).ThenBy(s => s.Name))
        {
            int i = gridStud.Rows.Add(s.Name, s.Group, s.HasPassword ? "задан" : "—", s.Created.ToString("dd.MM.yyyy"));
            gridStud.Rows[i].Tag = s;
        }
    }

    void SaveStudents()
    {
        Db.SaveStudents();
        FillStudents();
        Classroom.BroadcastStudents();
    }

    void EditStudent(Student s)
    {
        var name = Prompt.Ask(this, "Студент", "ФИО:", s?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var group = Prompt.Ask(this, "Студент", "Группа:", s?.Group ?? Db.Students.LastOrDefault()?.Group ?? "");
        if (group == null) return;
        if (s == null)
        {
            s = new Student { Id = Guid.NewGuid().ToString("N")[..10] };
            var p = Prompt.Ask(this, "Студент", "Пароль (пусто — без пароля):", "", true);
            s.SetPassword(p);
            Db.Students.Add(s);
        }
        s.Name = name.Trim();
        s.Group = group.Trim();
        SaveStudents();
    }

    // =================================================================
    //  УРОКИ
    // =================================================================

    TabPage LessonsTab()
    {
        var page = new TabPage("Уроки");
        var split = new SplitContainer { Dock = DockStyle.Fill };
        Shown += (_, _) => split.SplitterDistance = 380;
        split.Panel1.Controls.Add(lstLessons);
        split.Panel2.Controls.Add(prop);
        page.Controls.Add(split);
        var add = Btn("Новый урок");
        var copy = Btn("Копировать");
        var del = Btn("Удалить");
        add.Click += (_, _) => { var l = new Lesson(); Db.Lessons.Add(l); SaveLessons(l); };
        copy.Click += (_, _) =>
        {
            if (lstLessons.SelectedItem is not Lesson src) return;
            var l = System.Text.Json.JsonSerializer.Deserialize<Lesson>(System.Text.Json.JsonSerializer.Serialize(src))!;
            l.Id = Guid.NewGuid().ToString("N")[..8];
            l.Name += " (копия)";
            Db.Lessons.Add(l);
            SaveLessons(l);
        };
        del.Click += (_, _) =>
        {
            if (lstLessons.SelectedItem is not Lesson l || Db.Lessons.Count <= 1) return;
            if (MessageBox.Show(this, $"Удалить урок «{l.Name}»?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Db.Lessons.Remove(l);
            SaveLessons(null);
        };
        page.Controls.Add(Row(add, copy, del, L("Изменения сохраняются сразу. Файл: данные\\lessons.json")));
        lstLessons.SelectedIndexChanged += (_, _) => prop.SelectedObject = lstLessons.SelectedItem;
        prop.PropertyValueChanged += (_, _) => SaveLessons(prop.SelectedObject as Lesson);
        SaveLessons(null, save: false);
        return page;
    }

    void SaveLessons(Lesson select, bool save = true)
    {
        if (save) Db.SaveLessons();
        lstLessons.Items.Clear();
        foreach (var l in Db.Lessons.OrderBy(l => l.Level)) lstLessons.Items.Add(l);
        lstLessons.SelectedItem = select ?? (lstLessons.Items.Count > 0 ? lstLessons.Items[0] : null);
        FillLessonCombo();
    }

    // =================================================================
    //  ОТЧЁТЫ
    // =================================================================

    TabPage ReportsTab()
    {
        var page = new TabPage("Отчёты");
        fStatus.Items.AddRange(new object[] { "все", "играет", "не играет" });
        fDate.Items.AddRange(new object[] { "за всё время", "сегодня", "за текущий месяц", "за период" });
        fStatus.SelectedIndex = 0;
        fDate.SelectedIndex = 0;
        fFrom.Value = DateTime.Today.AddDays(-7);
        fFrom.Enabled = fTo.Enabled = false;
        fDate.SelectedIndexChanged += (_, _) => { fFrom.Enabled = fTo.Enabled = fDate.SelectedIndex == 3; FillReports(); };
        foreach (var c in new Control[] { fName, fGroup, fStatus, fFrom, fTo })
        {
            if (c is ComboBox cb) cb.SelectedIndexChanged += (_, _) => FillReports();
            else if (c is DateTimePicker dp) dp.ValueChanged += (_, _) => FillReports();
            else c.TextChanged += (_, _) => FillReports();
        }

        gridRep.Columns.Add("n", "ФИО");
        gridRep.Columns.Add("g", "Группа");
        gridRep.Columns.Add("s", "Статус");
        gridRep.Columns.Add("c", "Занятий");
        gridRep.Columns.Add("l", "Последнее");
        gridRep.Columns.Add("a", "Средний балл");
        gridRep.Columns.Add("z", "Зачётов");
        gridRep.Columns[0].FillWeight = 220;
        gridRep.SelectionChanged += (_, _) => FillGames();

        gridGames.Columns.Add("d", "Дата");
        gridGames.Columns.Add("l", "Урок");
        gridGames.Columns.Add("st", "Станция");
        gridGames.Columns.Add("du", "Длительность");
        gridGames.Columns.Add("r", "Принято");
        gridGames.Columns.Add("o", "Отправлено");
        gridGames.Columns.Add("c", "Крит.");
        gridGames.Columns.Add("w", "Ошибки");
        gridGames.Columns.Add("sc", "Баллы");
        gridGames.Columns.Add("v", "Итог");
        gridGames.Columns[1].FillWeight = 200;
        gridGames.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Reports.Detailed((GameResult)gridGames.Rows[e.RowIndex].Tag); };

        var bPrint = Btn("Печать списка");
        var bStud = Btn("Отчёты студента (печать)");
        var bDetail = Btn("Детально");
        var bSummary = Btn("Подготовить сводную ведомость…");
        bPrint.Click += (_, _) => Reports.StudentList(ReportRows(), FilterText());
        bStud.Click += (_, _) => { var s = SelReportStudent(); if (s != null) Reports.StudentGames(s, Games(s)); };
        bDetail.Click += (_, _) =>
        {
            if (gridGames.SelectedRows.Count > 0) Reports.Detailed((GameResult)gridGames.SelectedRows[0].Tag);
            else MessageBox.Show(this, "Выберите занятие в нижней таблице.");
        };
        bSummary.Click += (_, _) =>
        {
            var rows = ReportRows().SelectMany(r => r.games).OrderBy(g => g.StudentGroup).ThenBy(g => g.StudentName).ThenBy(g => g.Started).ToList();
            using var d = new SummaryDialog(rows, fGroup.SelectedIndex > 0 ? (string)fGroup.SelectedItem : "");
            d.ShowDialog(this);
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        Shown += (_, _) => split.SplitterDistance = Math.Max(150, split.Height / 2);
        split.Panel1.Controls.Add(gridRep);
        split.Panel2.Controls.Add(gridGames);
        split.Panel2.Controls.Add(new Label { Text = "Занятия выбранного студента (двойной щелчок — подробный отчёт):", Dock = DockStyle.Top, Height = 22 });
        page.Controls.Add(split);
        page.Controls.Add(Row(bPrint, bStud, bDetail, bSummary));
        page.Controls.Add(Row(L("ФИО:"), fName, L("Группа:"), fGroup, L("Статус:"), fStatus, L("Дата:"), fDate, fFrom, L("—"), fTo));
        return page;
    }

    bool InPeriod(GameResult g) => fDate.SelectedIndex switch
    {
        1 => g.Started.Date == DateTime.Today,
        2 => g.Started.Year == DateTime.Today.Year && g.Started.Month == DateTime.Today.Month,
        3 => g.Started.Date >= fFrom.Value.Date && g.Started.Date <= fTo.Value.Date,
        _ => true
    };

    List<GameResult> Games(Student s) => Db.Results.Where(g => g.Student?.Id == s.Id && InPeriod(g)).OrderByDescending(g => g.Started).ToList();

    static bool Playing(Student s) => Classroom.Active && Classroom.Online.Any(st => st.Student?.Id == s.Id);

    string FilterText() =>
        $"ФИО: {(fName.Text.Length > 0 ? fName.Text : "все")}, группа: {fGroup.SelectedItem ?? "все"}, статус: {fStatus.SelectedItem}, период: {fDate.SelectedItem}" +
        (fDate.SelectedIndex == 3 ? $" {fFrom.Value:dd.MM.yyyy}–{fTo.Value:dd.MM.yyyy}" : "");

    /// <summary>Как в тренажёре: в списке только те, кто хотя бы раз работал (или работает сейчас).</summary>
    List<(Student s, List<GameResult> games, bool playing)> ReportRows()
    {
        string g = fGroup.SelectedIndex > 0 ? (string)fGroup.SelectedItem : null;
        return Db.Students
            .Where(s => fName.Text.Length == 0 || s.Name.Contains(fName.Text, StringComparison.OrdinalIgnoreCase))
            .Where(s => g == null || s.Group == g)
            .Select(s => (s, games: Games(s), playing: Playing(s)))
            .Where(r => r.games.Count > 0 || r.playing)
            .Where(r => fStatus.SelectedIndex == 0 || (fStatus.SelectedIndex == 1) == r.playing)
            .OrderBy(r => r.s.Group).ThenBy(r => r.s.Name).ToList();
    }

    bool filling;

    void FillReports()
    {
        if (filling) return;
        filling = true;
        try { FillReportsCore(); } finally { filling = false; }
    }

    void FillReportsCore()
    {
        var groups = Db.Students.Select(s => s.Group).Distinct().OrderBy(x => x).ToList();
        var curGroup = fGroup.SelectedItem as string;
        if (fGroup.Items.Count != groups.Count + 1)
        {
            fGroup.Items.Clear();
            fGroup.Items.Add("все");
            fGroup.Items.AddRange(groups.ToArray());
            fGroup.SelectedItem = curGroup != null && groups.Contains(curGroup) ? curGroup : "все";
        }
        var sel = SelReportStudent()?.Id;
        gridRep.Rows.Clear();
        foreach (var (s, games, playing) in ReportRows())
        {
            int i = gridRep.Rows.Add(s.Name, s.Group, playing ? "играет" : "не играет", games.Count,
                games.Count > 0 ? games.Max(x => x.Ended).ToString("dd.MM.yyyy HH:mm") : "—",
                games.Count > 0 ? games.Average(x => x.Score).ToString("0") : "—", games.Count(x => x.Passed));
            gridRep.Rows[i].Tag = s;
            if (s.Id == sel) gridRep.Rows[i].Selected = true;
        }
        FillGames();
    }

    Student SelReportStudent() => gridRep.SelectedRows.Count > 0 ? gridRep.SelectedRows[0].Tag as Student : null;

    void FillGames()
    {
        gridGames.Rows.Clear();
        var s = SelReportStudent();
        if (s == null) return;
        foreach (var g in Games(s))
        {
            int i = gridGames.Rows.Add(g.Started.ToString("dd.MM.yyyy HH:mm"), g.LessonName, g.StationName, Reports.Dur(g.Duration),
                $"{g.Received}/{g.TaskReceive}", $"{g.Departed}/{g.TaskDepart}", g.CriticalCount, g.WrongCount, g.Score, g.Verdict);
            gridGames.Rows[i].Tag = g;
            gridGames.Rows[i].DefaultCellStyle.ForeColor = g.Passed ? Color.DarkGreen : Color.DarkRed;
        }
    }
}
