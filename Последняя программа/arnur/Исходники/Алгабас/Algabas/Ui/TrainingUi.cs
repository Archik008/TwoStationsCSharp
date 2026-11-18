using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  ЗАНЯТИЕ — окно станции  [ДОБАВЛЕНО 20.10.2026, этап 2]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//   • TrainingBar — полоса в верхней строке: урок, студент («Вход»), игровое время, «Задание»;
//   • вход студента, окно задания;
//   • снимок экрана для преподавателя и его мышь / клавиатура, когда он забрал управление;
//   • пока время на паузе или управление у преподавателя — мышь и клавиатура студента не действуют.
//   • [ДОБАВЛЕНО 18.11.2026] программа «ДНЦ»: схема, нарисованная заново в нужном размере (чётко), список кнопок
//     пульта и их нажатие, мышь над снимком схемы (см. Core/Training.cs, сообщения t_info, t_scheme, t_click).
// =====================================================================

public class TrainingBar : FlowLayoutPanel
{
    readonly Label lblLesson, lblStudent, lblClock;
    readonly Button btnLogin, btnTask;
    readonly Button btnSched;   // [ДОБАВЛЕНО 22.10.2026, этап 5] расписание урока
    readonly Form main;
    TaskWindow taskWin;

    public TrainingBar(Form main, Color back)
    {
        this.main = main;
        AutoSize = true;
        WrapContents = false;
        BackColor = back;
        Margin = new Padding(8, 0, 0, 0);
        lblLesson = Lbl(true);
        lblStudent = Lbl(false);
        btnLogin = Btn("Вход", () => new LoginDialog().ShowDialog(main));
        lblClock = Lbl(true);
        btnTask = Btn("Задание", ShowTask);
        Controls.AddRange(new Control[] { lblLesson, lblStudent, btnLogin, lblClock, btnTask });
        btnSched = Btn("Расписание", () => ScheduleUi.Open(main));   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        Controls.Add(btnSched);

        Training.Changed += Refresh2;
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        t.Tick += (_, _) => Refresh2();
        t.Start();
        Disposed += (_, _) => { t.Dispose(); Training.Changed -= Refresh2; };

        TrainingInput.Attach(main, this);
        Du46Ui.Attach(main);   // [ДОБАВЛЕНО 20.10.2026, этап 3] доклады работников и кнопки со счётчиком
        Refresh2();
    }

    static Label Lbl(bool bold) => new()
    {
        AutoSize = true, ForeColor = Color.White, Margin = new Padding(6, 9, 0, 0),
        Font = new Font("Segoe UI", 10f, bold ? FontStyle.Bold : FontStyle.Regular)
    };

    static Button Btn(string text, Action click)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(240, 240, 255),
                             Margin = new Padding(6, 4, 0, 0), Font = new Font("Segoe UI", 9f) };
        b.Click += (_, _) => click();
        return b;
    }

    void ShowTask()
    {
        if (taskWin == null || taskWin.IsDisposed) taskWin = new TaskWindow();
        taskWin.Show(main);
        taskWin.BringToFront();
    }

    void Refresh2()
    {
        if (IsDisposed) return;
        bool show = Training.TeacherOnline || Training.Active || Training.ControlBy == Training.AdminId;   // [ИЗМЕНЕНО 18.11.2026]
        Visible = show;
        if (!show) return;

        if (Training.TeacherControl)
        {
            // [ИЗМЕНЕНО 09.11.2026] подписи полосы — через LangUi.SetText: на kk / en не мигают при каждом обновлении
            LangUi.SetText(lblLesson, "УПРАВЛЕНИЕ У " + Training.ControlByName.ToUpperInvariant());   // [ИЗМЕНЕНО 18.11.2026] или у ДНЦ
            lblLesson.ForeColor = Color.FromArgb(255, 120, 120);
        }
        else
        {
            LangUi.SetText(lblLesson, Training.Active ? $"Урок: {Training.Lesson?.Name}" : "Преподаватель на связи");
            lblLesson.ForeColor = Color.White;
        }
        LangUi.SetText(lblStudent, "Студент: " + (Training.Student?.ToString() ?? "—"));
        LangUi.SetText(btnLogin, Training.Student == null ? "Вход" : "Сменить");
        lblClock.Visible = btnTask.Visible = Training.Active;
        btnSched.Visible = Training.Active && Schedule.Data != null;   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        LangUi.SetText(lblClock, Training.Running ? $"{Training.ClockText}  ×{Training.Speed}" : $"{Training.ClockText}  ПАУЗА");
        lblClock.ForeColor = Training.Running ? Color.FromArgb(160, 255, 160) : Color.FromArgb(255, 210, 90);
    }
}

// ---------------------------------------------------------------------
//  Вход студента
// ---------------------------------------------------------------------
class LoginDialog : Form
{
    readonly ComboBox cbGroup = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    readonly ListBox list = new() { Width = 340, Height = 260 };
    readonly TextBox pwd = new() { Width = 340, UseSystemPasswordChar = true };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = Color.DarkRed };

    public LoginDialog()
    {
        Text = "Вход студента";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10f);

        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(12), WrapContents = false };
        p.Controls.Add(new Label { Text = "Группа:", AutoSize = true });
        p.Controls.Add(cbGroup);
        p.Controls.Add(new Label { Text = "Выберите себя:", AutoSize = true });
        p.Controls.Add(list);
        p.Controls.Add(new Label { Text = "Пароль (если задан преподавателем):", AutoSize = true });
        p.Controls.Add(pwd);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        var ok = new Button { Text = "Войти", AutoSize = true };
        var add = new Button { Text = "Меня нет в списке…", AutoSize = true, Enabled = Training.AllowAdd };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(add);
        p.Controls.Add(buttons);
        p.Controls.Add(status);
        Controls.Add(p);
        AcceptButton = ok;

        var groups = Training.Students.Select(s => s.Group).Distinct().OrderBy(g => g).ToList();
        cbGroup.Items.Add("(все)");
        cbGroup.Items.AddRange(groups.ToArray());
        cbGroup.SelectedIndexChanged += (_, _) => Fill();
        cbGroup.SelectedIndex = 0;
        if (!Training.TeacherOnline) status.Text = "Нет связи с программой преподавателя.";
        else if (Training.Students.Count == 0) status.Text = "Список студентов пуст. Попросите преподавателя добавить вас.";
        if (!Training.AllowAdd) new ToolTip().SetToolTip(add, "Добавлять себя запрещено преподавателем");

        ok.Click += (_, _) =>
        {
            if (list.SelectedItem is not StudentInfo s) { status.Text = "Выберите себя в списке."; return; }
            status.ForeColor = Color.DimGray;
            status.Text = "Проверка…";
            Training.Login(s.Id, pwd.Text);
        };
        add.Click += (_, _) =>
        {
            using var r = new RegisterDialog();
            if (r.ShowDialog(this) == DialogResult.OK)
            {
                status.Text = "Регистрация…";
                Training.Register(r.FullName, r.Group, r.Password);
            }
        };
        Training.LoginResult += OnResult;
        FormClosed += (_, _) => Training.LoginResult -= OnResult;
    }

    void Fill()
    {
        list.Items.Clear();
        string g = cbGroup.SelectedIndex > 0 ? (string)cbGroup.SelectedItem : null;
        foreach (var s in Training.Students.Where(s => g == null || s.Group == g).OrderBy(s => s.Name)) list.Items.Add(s);
    }

    void OnResult(bool ok, string msg)
    {
        if (ok) { DialogResult = DialogResult.OK; Close(); return; }
        status.ForeColor = Color.DarkRed;
        status.Text = msg;
    }
}

class RegisterDialog : Form
{
    readonly TextBox name = new() { Width = 320 }, group = new() { Width = 320 }, pass = new() { Width = 320, UseSystemPasswordChar = true };
    public string FullName => name.Text.Trim();
    public string Group => group.Text.Trim();
    public string Password => pass.Text;

    public RegisterDialog()
    {
        Text = "Новый студент";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10f);
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(12), WrapContents = false };
        p.Controls.Add(new Label { Text = "ФИО:", AutoSize = true });
        p.Controls.Add(name);
        p.Controls.Add(new Label { Text = "Группа:", AutoSize = true });
        p.Controls.Add(group);
        p.Controls.Add(new Label { Text = "Пароль (можно пустой):", AutoSize = true });
        p.Controls.Add(pass);
        var ok = new Button { Text = "Зарегистрироваться", AutoSize = true };
        ok.Click += (_, _) =>
        {
            if (FullName.Length < 3 || Group.Length == 0) { MessageBox.Show(this, "Введите ФИО и группу."); return; }
            DialogResult = DialogResult.OK;
        };
        p.Controls.Add(ok);
        Controls.Add(p);
        AcceptButton = ok;
    }
}

// ---------------------------------------------------------------------
//  Задание на игру
// ---------------------------------------------------------------------
class TaskWindow : Form
{
    readonly Label info = new() { AutoSize = true, MaximumSize = new Size(520, 0), Font = new Font("Segoe UI", 10.5f) };
    readonly ListBox events = new() { Width = 520, Height = 200, HorizontalScrollbar = true };

    public TaskWindow()
    {
        Text = "Задание на игру";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = true;
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(12), WrapContents = false };
        p.Controls.Add(info);
        p.Controls.Add(new Label { Text = "Нарушения:", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
        p.Controls.Add(events);
        Controls.Add(p);
        Training.Changed += Fill;
        FormClosed += (_, _) => Training.Changed -= Fill;
        Fill();
    }

    void Fill()
    {
        if (IsDisposed) return;
        var l = Training.Lesson;
        if (!Training.Active || l == null) { info.Text = "Занятие не идёт."; events.Items.Clear(); return; }
        string Need(int need, int done) => need > 0 ? $"{done} из {need}" : $"{done} (не требуется)";
        info.Text =
            $"Урок: {l.Name}\n\n{l.Description}\n\n" +
            $"Принято поездов: {Need(l.Receive, Training.Received)}\n" +
            $"Отправлено поездов: {Need(l.Depart, Training.Departed)}\n" +
            $"Критические события: {Training.CriticalCount} (допустимо {l.MaxCritical})\n" +
            $"Неправильные действия: {Training.WrongCount}\n" +
            $"Оценка сейчас: {Training.Score} из 100 (зачёт от {l.PassScore})" +
            (l.Minutes > 0 ? $"\nДлительность урока: {l.Minutes} мин игрового времени" : "");
        events.BeginUpdate();
        events.Items.Clear();
        foreach (var e in Training.Events)
            events.Items.Add($"{Training.Fmt(e.Clock)}  {(e.Critical ? "КРИТ." : "ошибка")}  −{e.Penalty}  {e.Text}");
        events.EndUpdate();
    }
}

// ---------------------------------------------------------------------
//  Снимок экрана, ввод преподавателя, блокировка ввода студента
// ---------------------------------------------------------------------
static class TrainingInput
{
    static Form main;
    static Control bar;
    static Rectangle shotBounds;
    static long allowKeysUntil;
    static long lastHint;

    public static void Attach(Form form, Control trainingBar)
    {
        main = form;
        bar = trainingBar;
        Training.CaptureScreen = Capture;
        Training.InjectInput = Inject;
        Training.AdminInfo = AdminInfo;        // [ДОБАВЛЕНО 18.11.2026] программа «ДНЦ»
        Training.RenderScheme = RenderScheme;
        Training.ClickUi = ClickUi;
        Training.AdminCommand = AdminCommand;
        Training.TakeOver = () =>
        {
            if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Maximized;
            main.Activate();
        };
        Application.AddMessageFilter(new Blocker());
    }

    static Rectangle Bounds()
    {
        var mb = main.WindowState == FormWindowState.Minimized ? main.RestoreBounds : main.Bounds;
        var b = Rectangle.Intersect(mb, SystemInformation.VirtualScreen);
        return b.Width > 0 && b.Height > 0 ? b : Screen.PrimaryScreen!.Bounds;
    }

    /// <summary>[ИЗМЕНЕНО 21.10.2026] region — часть окна (доли 0..1): её снимаем в полном разрешении,
    /// и мышь преподавателя считается от неё же (shotBounds).</summary>
    static (string, int, int)? Capture(int maxW, (double x, double y, double w, double h)? region)
    {
        try
        {
            var b = Bounds();
            var crop = new Rectangle(Point.Empty, b.Size);
            if (region is { } rg && rg.w > 0.01 && rg.h > 0.01)
                crop = Rectangle.Intersect(crop, new Rectangle((int)(rg.x * b.Width), (int)(rg.y * b.Height),
                                                               (int)Math.Ceiling(rg.w * b.Width), (int)Math.Ceiling(rg.h * b.Height)));
            if (crop.Width < 16 || crop.Height < 16) crop = new Rectangle(Point.Empty, b.Size);
            shotBounds = new Rectangle(b.X + crop.X, b.Y + crop.Y, crop.Width, crop.Height);
            using var full = new Bitmap(b.Width, b.Height);
            using (var g = Graphics.FromImage(full))
            {
                // окна программы (главное, диалоги, открытые меню) снизу вверх — даже если их перекрыло чужое окно
                g.Clear(Color.Black);
                foreach (var win in OwnWindows())
                {
                    if (!GetWindowRect(win, out var r)) continue;
                    var wr = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    if (wr.Width <= 0 || wr.Height <= 0 || !wr.IntersectsWith(b)) continue;
                    using var wb = new Bitmap(wr.Width, wr.Height);
                    using (var wg = Graphics.FromImage(wb))
                    {
                        var hdc = wg.GetHdc();
                        bool ok = PrintWindow(win, hdc, 2);   // PW_RENDERFULLCONTENT
                        wg.ReleaseHdc(hdc);
                        if (!ok) continue;
                    }
                    g.DrawImage(wb, wr.X - b.X, wr.Y - b.Y);
                }
                if (main.WindowState == FormWindowState.Minimized)
                    TextRenderer.DrawText(g, L.T("Окно станции свёрнуто студентом"),   // [ДОБАВЛЕНО 22.10.2026, этап 8]
                         new Font("Segoe UI", 28f), new Rectangle(Point.Empty, b.Size),
                                          Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            double k = Math.Min(1.0, Math.Clamp(maxW, 320, 3840) / (double)crop.Width);
            int w = Math.Max(1, (int)(crop.Width * k)), h = Math.Max(1, (int)(crop.Height * k));
            using var small = new Bitmap(w, h);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = k >= 0.999 ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
                                                 : System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(full, new Rectangle(0, 0, w, h), crop, GraphicsUnit.Pixel);
            }
            var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var prm = new EncoderParameters(1);
            // в полном размере — чётче (мелкий текст пульта), в уменьшенном просмотре хватает и 65
            prm.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, k >= 0.999 ? 80L : 65L);
            using var ms = new MemoryStream();
            small.Save(ms, enc, prm);
            return (Convert.ToBase64String(ms.ToArray()), w, h);
        }
        catch { return null; }   // экран заблокирован и т.п. — кадр пропускаем
    }

    // =================================================================
    //  [ДОБАВЛЕНО 18.11.2026] ПРОГРАММА «ДНЦ»
    // =================================================================

    /// <summary>Панели станций в окне (у АКЖТ две: «Станция АКЖТ» и путевой пост), по порядку.</summary>
    /// <summary>[ИЗМЕНЕНО 18.11.2026] Из реестра StationPane.All, а не по окну: у АКЖТ кнопка «Вид» убирает вторую панель
    /// из окна — раньше ДНЦ терял эту схему (плитка без данных, картинка сплющивалась).</summary>
    static List<StationPane> Panes() => StationPane.All.Where(p => !p.IsDisposed).ToList();

    static bool OnScreen(StationPane p) => p.Visible && p.FindForm() != null;

    /// <summary>Кнопки и надписи пульта последнего списка ui → нажатие от ДНЦ по id.</summary>
    static readonly Dictionary<string, Control> uiMap = new();

    /// <summary>Ответ s_info: схемы (название, ширина/высота рисунка, где на экране), окно-вопрос, кнопки пульта.</summary>
    static Dictionary<string, object> AdminInfo(JsonElement d)
    {
        bool ui = d.TryGetProperty("ui", out var ue) && ue.ValueKind == JsonValueKind.True;
        bool wantRoutes = d.TryGetProperty("routes", out var re) && re.ValueKind == JsonValueKind.True;
        var b = Bounds();
        var schemes = new List<object>();
        var items = ui ? new List<object>() : null;
        if (ui) uiMap.Clear();
        int pi = 0;
        foreach (var pane in Panes())
        {
            var sc = pane.SchemeControl;
            var g = pane.SchemeView.Geo;
            double x = 0, y = 0, w = 0, h = 0;
            if (OnScreen(pane) && sc.Visible && sc.Width > 0)
            {
                var dr = Rectangle.Round(sc.DrawnRect);
                dr.Inflate(6, 6);
                dr.Intersect(sc.ClientRectangle);
                var sr = sc.RectangleToScreen(dr);
                sr.Intersect(b);
                if (sr.Width > 16 && sr.Height > 16)
                    (x, y, w, h) = ((sr.X - b.X) / (double)b.Width, (sr.Y - b.Y) / (double)b.Height, sr.Width / (double)b.Width, sr.Height / (double)b.Height);
            }
            // общие с соседом участки на этой схеме (их концы на чертеже) и схемы одной программы в общей системе координат
            var sharedIds = StationApp.Model.ModelHost.Dispatch?.SharedSectionIds.ToList() ?? new();
            var shared = g.Pieces.Where(pc => pc.Pts.Length >= 2)
                .SelectMany(pc => sharedIds.Where(s => s.id == pc.SectionId)
                    .Select(s => new { n = s.wire, x1 = pc.Pts[0].X, y1 = pc.Pts[0].Y, x2 = pc.Pts[^1].X, y2 = pc.Pts[^1].Y }))
                .ToList();
            var grp = sc.LevelGroup is { Count: >= 2 } lg && lg.Contains(sc) ? lg : null;
            string lvl = grp == null ? "" : "lvl" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(grp);
            int lvlpos = grp?.IndexOf(sc) ?? 0;
            schemes.Add(new { name = pane.SchemeView.Title, aspect = g.DesignW / g.DesignH, dw = g.DesignW, dh = g.DesignH,
                              shared, lvl, lvlpos, x, y, w, h });
            if (ui) CollectUi(pane, pi, items);   // и у спрятанной «Видом» схемы АКЖТ: её кнопки есть и нажимаются
            pi++;
        }
        return new Dictionary<string, object>
        {
            ["schemes"] = schemes, ["popup"] = HasPopup(), ["bg"] = main.BackColor.ToArgb(), ["ui"] = items,
            ["mode"] = UiSettings.Mode.ToString(),   // режим интерфейса — ДНЦ держит его одинаковым у всех станций
            ["sel"] = SelInfo(), ["lines"] = LinesInfo(), ["routes"] = wantRoutes ? RoutesInfo() : null
        };
    }

    // =================================================================
    //  [ДОБАВЛЕНО 18.11.2026] МАРШРУТЫ, ПЕРЕГОНЫ, СКВОЗНОЙ МАРШРУТ — ДЛЯ ДНЦ
    // =================================================================
    static Interlocking IL => StationApp.Model.ModelHost.Model.IL;

    /// <summary>Выбранные светофоры начала маршрута (набор начат): схема и светофор.</summary>
    static List<object> SelInfo() => Panes().Where(p => p.SchemeControl.Selected != null)
        .Select(p => (object)new { p = p.SchemeView.Title, s = p.SchemeControl.Selected.Label }).ToList();

    static string Wire(DirectionLine ln) =>
        StationApp.Model.ModelHost.Dispatch?.SharedLines.FirstOrDefault(x => x.line == ln).wire ?? "";

    /// <summary>Перегоны станции: направление, общий ли с соседом (wire), идёт ли согласование.</summary>
    static List<object> LinesInfo()
    {
        var seen = new HashSet<DirectionLine>();
        var list = new List<object>();
        foreach (var pane in Panes())
            foreach (var ln in pane.SchemeView.Lines)
                if (seen.Add(ln))
                    list.Add(new
                    {
                        id = ln.Id, title = ln.Title, dir = ln.Current.ToString(),
                        text = ln.Current == Dir.Right ? ln.RightName : ln.LeftName, arrow = ln.DirText(ln.Current),
                        wire = Wire(ln), sync = ln.Syncing, aux = ln.AuxRunning, p = pane.SchemeView.Title
                    });
        return list;
    }

    /// <summary>Все маршруты станции по порядку (№ — индекс в IL.Routes): схема, вид, состояние, можно ли задать сейчас.</summary>
    static List<object> RoutesInfo()
    {
        var panes = Panes();
        return IL.Routes.Select((r, i) => (object)new
        {
            i, n = r.Name, sh = r.Shunting, p = panes.FirstOrDefault(p => p.SchemeView.Routes.Contains(r))?.SchemeView.Title ?? "",
            st = r.Releasing ? "releasing" : r.TrainEntered ? "train" : r.Active ? (r.Setting ? "setting" : "open") : "free",
            why = r.Active ? null : r.Precondition?.Invoke()
        }).ToList();
    }

    /// <summary>Выполнить действие станции и собрать, что она сказала в журнал с отказом (причина для ДНЦ).</summary>
    static (bool ok, string reason) Run(Func<bool> act)
    {
        var said = new List<string>();
        void Grab(string m, LogKind k) { if (k == LogKind.Refuse) said.Add(m); }
        StationApp.Model.ModelHost.Log += Grab;
        try { return (act(), string.Join("; ", said)); }
        finally { StationApp.Model.ModelHost.Log -= Grab; }
    }

    static object AdminCommand(string kind, JsonElement d)
    {
        string S(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int I(string n) => d.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : -1;
        double D(string n) => d.TryGetProperty(n, out var v) && v.TryGetDouble(out var x) ? x : 0;
        bool B(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
        string tag = S("tag");
        object Res(bool ok, string reason, object more = null) =>
            new { kind = "s_result", op = kind, tag, ok, reason, i = more?.GetType().GetProperty("i")?.GetValue(more),
                  name = more?.GetType().GetProperty("name")?.GetValue(more) as string,
                  dirOk = more?.GetType().GetProperty("dirOk")?.GetValue(more) as bool? };

        switch (kind)
        {
            case "t_route":
            {
                int i = I("i");
                if (i < 0 || i >= IL.Routes.Count) return Res(false, "нет такого маршрута");
                var r = IL.Routes[i];
                if (B("on"))
                {
                    if (r.Active) return Res(true, "уже задан", new { i, name = r.Name });
                    var (ok, why) = Run(() => IL.SetRoute(r));
                    return Res(ok, why, new { i, name = r.Name });
                }
                if (!r.Active) return Res(true, "не задан", new { i, name = r.Name });
                var (ok2, why2) = Run(() => { IL.CancelRoute(r); return true; });
                return Res(ok2, why2, new { i, name = r.Name });
            }
            case "t_dir":
            {
                var ln = Panes().SelectMany(p => p.SchemeView.Lines).FirstOrDefault(l => l.Id == S("line"));
                if (ln == null) return Res(false, "нет такого перегона");
                var (ok, why) = Run(() => IL.ChangeDirection(ln, B("aux")));
                return Res(ok, why);
            }
            case "t_unselect":
                foreach (var p in Panes().Where(p => p.SchemeControl.Selected != null)) p.SchemeControl.ClearSelection();
                return null;
            case "t_depart":
            {
                var ln = StationApp.Model.ModelHost.Dispatch?.SharedLines.FirstOrDefault(x => x.wire == S("wire")).line;
                var sel = Panes().Select(p => p.SchemeControl.Selected).FirstOrDefault(s => s != null);
                if (ln == null) return Res(false, "у станции нет такого перегона");
                if (sel == null) return Res(false, "не выбран светофор начала");
                var r = IL.Routes.FirstOrDefault(r => r.Start == sel && !r.Shunting && r.ExitSection != null && ln.Sections.Contains(r.ExitSection));
                if (r == null) return Res(false, $"от {sel.Label} нет поездного маршрута на перегон «{ln.Title}»");
                return Res(true, "", new { i = IL.Routes.IndexOf(r), name = r.Name, dirOk = r.Precondition?.Invoke() == null });
            }
            case "t_arrive":
            {
                var ln = StationApp.Model.ModelHost.Dispatch?.SharedLines.FirstOrDefault(x => x.wire == S("wire")).line;
                var pane = Panes().FirstOrDefault(p => p.SchemeView.Title == S("scheme"));
                if (ln == null || pane == null) return Res(false, "у станции нет такого перегона или схемы");
                var hitObj = pane.SchemeControl.HitDesign((float)D("dx"), (float)D("dy"));
                if (hitObj == null) return Res(false, "в этом месте схемы нет пути или светофора");
                var c = IL.Routes.Where(r => !r.Shunting && r.Start?.Approach != null && ln.Sections.Contains(r.Start.Approach)).ToList();
                var r = c.FirstOrDefault(r => Equals(r.Dest, hitObj) || Equals(r.End, hitObj))
                     ?? c.FirstOrDefault(r => hitObj is TrackSection s && (r.Sections.Contains(s) || r.ExitSection == s));
                string what = hitObj is RailSignal sg ? sg.Label : ((TrackSection)hitObj).Label;
                if (r == null) return Res(false, $"с перегона «{ln.Title}» нет маршрута приёма до {what}");
                return Res(true, "", new { i = IL.Routes.IndexOf(r), name = r.Name });
            }
        }
        return null;
    }

    /// <summary>Кнопки, галочки и надписи панели станции по порядку. Группы — строки панели (заголовок, пульт ДСП полосой,
    /// строка под схемой…), а в строках из нескольких колонок (низ окна, «Занятость участков») — каждая колонка.</summary>
    static void CollectUi(StationPane pane, int pi, List<object> items)
    {
        var scheme = pane.SchemeControl;
        string paneName = pane.SchemeView.Title;
        int n = 0, gi = 0;
        Control lastParent = null;
        bool breakAfter = false;

        void Item(Control c, int group, int gw)
        {
            string t = c switch { CheckBox => "c", ButtonBase => "b", Label => "l", _ => null };
            if (t == null) return;
            string text = c.Text.Replace("\r", "").Trim();
            if (t == "l" && text.Length == 0) return;
            bool nl = breakAfter;
            // сменился контейнер (ряд кнопок, колонка) — с новой строки, кроме рядов, идущих друг за другом в строку (пульт полосой)
            if (lastParent != null && c.Parent != lastParent)
                nl |= !(lastParent.Parent is FlowLayoutPanel lp && lp.FlowDirection == FlowDirection.LeftToRight && !lp.GetFlowBreak(lastParent));
            lastParent = c.Parent;
            breakAfter = c.Parent is FlowLayoutPanel fp && fp.GetFlowBreak(c);
            string id = $"{pi}.{n++}";
            uiMap[id] = c;
            items.Add(new
            {
                p = paneName, id, g = $"{pi}.{group}", gw, t, text, w = c.Width, h = c.Height,
                bg = c.BackColor.ToArgb(), fg = c.ForeColor.ToArgb(), en = c.Enabled,
                chk = c is CheckBox cb && cb.Checked, nl, f = Math.Round(c.Font.SizeInPoints, 1), bold = c.Font.Bold
            });
        }

        void Walk(Control c, int group, int gw)
        {
            foreach (Control ch in c.Controls)
            {
                if (!ch.Visible || ch == scheme) continue;
                if (ch is ButtonBase or Label) Item(ch, group, gw);
                else if (ch is not (ListBox or ComboBox or NumericUpDown or TextBoxBase)) Walk(ch, group, gw);
            }
        }

        static bool IsBox(Control c) => c is ScrollableControl && c is not RailSchemePanel;

        foreach (Control root in pane.Controls)
        {
            if (!root.Visible) continue;
            foreach (Control row in root.Controls.Cast<Control>().Where(r => r.Visible)
                                       .OrderBy(r => root is TableLayoutPanel t ? t.GetRow(r) : 0))
            {
                var cols = row is TableLayoutPanel tr ? row.Controls.Cast<Control>().Where(c => c.Visible && IsBox(c))
                                                           .OrderBy(c => tr.GetColumn(c)).ToList() : new List<Control>();
                if (cols.Count >= 2 && row is TableLayoutPanel tl)
                {
                    // [ИЗМЕНЕНО 18.11.2026] все ячейки по порядку (строка, колонка): контейнер — своя группа, но надпись перед ним
                    // в той же строке таблицы («ПОЕЗДНЫЕ», «СТРЕЛКИ» у пульта «Енисей») — в одной группе с его кнопками, в строку
                    int cur = -1, curRow = -1;
                    bool afterLabel = false;
                    foreach (var c in row.Controls.Cast<Control>().Where(c => c.Visible && c != scheme)
                                         .OrderBy(c => tl.GetRow(c)).ThenBy(c => tl.GetColumn(c)))
                    {
                        int r = tl.GetRow(c);
                        bool join = afterLabel && r == curRow && IsBox(c);
                        if (!join) cur = gi++;
                        lastParent = null;
                        breakAfter = false;
                        if (IsBox(c)) { Walk(c, cur, join ? tl.Width : c.Width); afterLabel = false; }
                        else if (c is ButtonBase or Label) { Item(c, cur, tl.Width); afterLabel = c is Label; }
                        curRow = r;
                    }
                }
                else
                {
                    lastParent = null; breakAfter = false;
                    if (row is ButtonBase or Label) Item(row, gi, row.Width);
                    else Walk(row, gi, row.Width);
                    gi++;
                }
            }
        }
    }

    /// <summary>ДНЦ нажал копию кнопки (надписи) пульта: нажимаем настоящую — как мышью.</summary>
    static void ClickUi(string id, bool right)
    {
        if (!uiMap.TryGetValue(id, out var c) || c.IsDisposed || !c.Visible || !c.Enabled) return;
        var pt = LParam(new Point(c.Width / 2, c.Height / 2));
        if (right)
        {
            SendMessage(c.Handle, WM_RBUTTONDOWN, (IntPtr)MK_RBUTTON, pt);
            SendMessage(c.Handle, WM_RBUTTONUP, IntPtr.Zero, pt);
            return;
        }
        if (c is CheckBox cb) { cb.Checked = !cb.Checked; return; }
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                       .Invoke(c, new object[] { EventArgs.Empty });
    }

    /// <summary>На станции открыто окно-вопрос (MessageBox, диалог) или меню — ДНЦ показываем всё окно.</summary>
    static bool HasPopup()
    {
        foreach (var h in OwnWindows())
        {
            if (h == main.Handle) continue;
            var c = Control.FromHandle(h);
            if (c is ToolStripDropDown) return true;
            if (c is Form f) { if (f.Modal) return true; continue; }
            if (c == null)
            {
                var sb = new System.Text.StringBuilder(64);
                GetClassName(h, sb, sb.Capacity);
                if (sb.ToString() == "#32770") return true;   // MessageBox и системные диалоги
            }
        }
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int max);

    /// <summary>Невидимые копии схем — рисуют схему заново в размере, который просит ДНЦ (чётко при любом масштабе).</summary>
    static readonly Dictionary<string, RailSchemePanel> shadows = new();

    /// <summary>Кусок схемы name по X от x0 до x1 (единицы чертежа) во всю высоту чертежа, в масштабе k пикселей на единицу.
    /// Картинка — ровно этот прямоугольник чертежа, без полей: ДНЦ ставит куски соседних станций встык.</summary>
    static (string, int, int, float)? RenderScheme(JsonElement d)
    {
        try
        {
            double Num(string n, double def) => d.TryGetProperty(n, out var v) && v.TryGetDouble(out var x) ? x : def;
            string name = d.TryGetProperty("name", out var ne) ? ne.GetString() ?? "" : "";
            var pane = Panes().FirstOrDefault(p => p.SchemeView.Title == name);
            if (pane == null) return null;
            var geo = pane.SchemeView.Geo;
            if (!shadows.TryGetValue(name, out var sh) || sh.IsDisposed)
                shadows[name] = sh = new RailSchemePanel(pane.SchemeView);
            sh.CopyLook(pane.SchemeControl);   // выбранный светофор, упрощённый вид — как на настоящей схеме
            sh.BackColor = pane.SchemeControl.BackColor;
            float k = (float)Math.Clamp(Num("k", 0.5), 0.02, 4);
            if (geo.DesignW * k > 9000) k = 9000 / geo.DesignW;
            if (geo.DesignH * k > 3000) k = 3000 / geo.DesignH;
            float x0 = (float)Math.Clamp(Num("x0", 0), 0, geo.DesignW), x1 = (float)Math.Clamp(Num("x1", geo.DesignW), x0, geo.DesignW);
            int w = Math.Max(1, (int)Math.Round(geo.DesignW * k));
            // ширина = чертёж, высота с запасом: масштаб берётся по ширине; снизу строка подсказки — она отрезается
            var size = new Size(w, (int)Math.Ceiling(geo.DesignH * k) + 90);
            if (sh.Size != size) sh.Size = size;
            using var full = new Bitmap(size.Width, size.Height);
            sh.DrawToBitmap(full, new Rectangle(Point.Empty, size));
            var dr = sh.DrawnRect;
            float s = dr.Width / geo.DesignW;   // настоящий масштаб рисунка (≈ k)
            var cut = Rectangle.Round(new RectangleF(dr.X + x0 * s, dr.Y, (x1 - x0) * s, geo.DesignH * s));
            cut.Intersect(new Rectangle(Point.Empty, size));
            if (cut.Width < 4 || cut.Height < 4) return null;
            using var crop = full.Clone(cut, full.PixelFormat);
            using var ms = new MemoryStream();
            crop.Save(ms, ImageFormat.Png);   // PNG: тонкие линии и подписи без размытия
            return (Convert.ToBase64String(ms.ToArray()), cut.Width, cut.Height, s);
        }
        catch { return null; }
    }

    static bool swallowUp;

    /// <summary>[ДОБАВЛЕНО 18.11.2026] Маршрут от светофора from до того, что кликнул ДНЦ: светофор конца, путь назначения,
    /// участок маршрута или первый за ним; иначе — маршрут, чей выход на перегон лежит на том же пути, что кликнутый участок.</summary>
    static RailRoute RouteTo(RailSignal from, object hit)
    {
        var rs = IL.Routes.Where(x => x.Start == from).OrderBy(x => x.Shunting).ToList();
        var r = rs.FirstOrDefault(x => Equals(x.End, hit) || Equals(x.Dest, hit))
             ?? rs.FirstOrDefault(x => hit is TrackSection s && (x.Sections.Contains(s) || x.ExitSection == s));
        if (r != null || hit is not TrackSection hs || Track(hs) is not { } h || SignalX(from) is not float sx) return r;
        // тот же путь (та же высота) и дальше выхода маршрута по ходу: от светофора начала через выход — к кликнутому участку
        return rs.FirstOrDefault(x => !x.Shunting && x.ExitSection != null && Track(x.ExitSection) is { } e &&
                                      Math.Abs(e.y - h.y) < 3 && Math.Sign(e.x - sx) == Math.Sign(h.x - e.x) && Math.Abs(h.x - e.x) > 1);
    }

    /// <summary>Прямой горизонтальный кусок участка на чертеже: середина по X и высота. У схем одной программы в общей
    /// системе координат (АКЖТ и пост) высоты общие, а X каждой схемы отсчитывается от её левого края — пост стоит
    /// вплотную справа от АКЖТ, поэтому к его X прибавляется ширина чертежа АКЖТ (StripX).</summary>
    static (float x, float y)? Track(TrackSection s)
    {
        foreach (var p in Panes())
            foreach (var pc in p.SchemeView.Geo.Pieces)
                if (pc.SectionId == s.Id && pc.Pts.Length >= 2 && Math.Abs(pc.Pts[0].Y - pc.Pts[^1].Y) < 1)
                    return (StripX(p) + (pc.Pts[0].X + pc.Pts[^1].X) / 2, pc.Pts[0].Y);
        return null;
    }

    static float? SignalX(RailSignal s)
    {
        foreach (var p in Panes())
            foreach (var sg in p.SchemeView.Geo.Signals)
                if (sg.SignalId == s.Id) return StripX(p) + sg.Foot.X;
        return null;
    }

    /// <summary>Сдвиг чертежа схемы в общей полосе схем программы: сумма ширин схем левее неё в группе (LevelGroup).</summary>
    static float StripX(StationPane pane)
    {
        var grp = pane.SchemeControl.LevelGroup;
        if (grp == null || !grp.Contains(pane.SchemeControl)) return 0;
        float x = 0;
        foreach (var g in grp.TakeWhile(g => g != pane.SchemeControl))
            x += Panes().FirstOrDefault(p => p.SchemeControl == g)?.SchemeView.Geo.DesignW ?? 0;
        return x;
    }

    /// <summary>Видимые окна верхнего уровня нашей программы, снизу вверх по Z-порядку.</summary>
    static List<IntPtr> OwnWindows()
    {
        var list = new List<IntPtr>();
        uint me = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && GetWindowThreadProcessId(h, out var pid) != 0 && pid == me) list.Add(h);
            return true;
        }, IntPtr.Zero);
        list.Reverse();   // EnumWindows — сверху вниз
        return list;
    }

    /// <summary>Самое глубокое окно программы под точкой экрана (чужие окна поверх не мешают).</summary>
    static IntPtr OwnWindowAt(Point screen)
    {
        var tops = OwnWindows();
        for (int i = tops.Count - 1; i >= 0; i--)
        {
            if (!GetWindowRect(tops[i], out var r) || !Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).Contains(screen)) continue;
            var h = tops[i];
            while (true)
            {
                var pt = screen;
                ScreenToClient(h, ref pt);
                var c = ChildWindowFromPointEx(h, pt, 1 | 4);   // CWP_SKIPINVISIBLE | CWP_SKIPTRANSPARENT
                if (c == IntPtr.Zero || c == h) return h;
                h = c;
            }
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    static IntPtr Topmost(Point screen) => WindowFromPoint(screen);
    [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr h, Point p, uint flags);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    struct RECT { public int Left, Top, Right, Bottom; }

    // ---- WinAPI ----
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref Point p);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetCapture();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204,
              WM_RBUTTONUP = 0x205, WM_MOUSEWHEEL = 0x20A, MK_LBUTTON = 1, MK_RBUTTON = 2;

    static IntPtr LParam(Point p) => (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));

    static void Inject(JsonElement d)
    {
        string act = d.TryGetProperty("act", out var a) ? a.GetString() ?? "" : "";
        if (act == "text")
        {
            if (Form.ActiveForm == null) main.Activate();   // SendKeys пишет в активное окно
            // клавиатура: SendKeys — как настоящие нажатия; пропускаем их через блокировку
            string text = d.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (text.Length == 0) return;
            allowKeysUntil = Environment.TickCount64 + 500;
            try { SendKeys.SendWait(text); } catch { }
            return;
        }

        // [ДОБАВЛЕНО 18.11.2026] ДНЦ кликает по нарисованной схеме: точка чертежа → прямо в панель схемы
        if (d.TryGetProperty("scheme", out var sne) && sne.GetString() is { Length: > 0 } sname)
        {
            var pane = Panes().FirstOrDefault(p => p.SchemeView.Title == sname);
            if (pane == null) return;
            var sc = pane.SchemeControl;
            double D(string n) => d.TryGetProperty(n, out var v) && v.TryGetDouble(out var r) ? r : 0;
            var lp = LParam(sc.DesignToClient((float)D("dx"), (float)D("dy")));
            bool rb = d.TryGetProperty("btn", out var rbe) && rbe.GetString() == "right";
            // [ДОБАВЛЕНО 18.11.2026] Второй клик набора от ДНЦ. Начало могло быть выбрано на другой схеме этой программы
            // (АКЖТ ↔ путевой пост — своя схема так не умеет, выбор у каждой схемы свой), а концом ДНЦ считает не только
            // светофор конца и путь назначения, но и участок внутри маршрута или на том же пути за его выходом
            // (например, блок-участок перегона за выходным светофором) — как на диспетчерском экране.
            var selPane = Panes().FirstOrDefault(p => p.SchemeControl.Selected != null);
            if (act is "down" or "up" && !rb && selPane != null)
            {
                if (act == "up") { if (swallowUp) { swallowUp = false; return; } }
                else
                {
                    var hitObj = sc.HitDesign((float)D("dx"), (float)D("dy"));
                    var r = hitObj == null ? null : RouteTo(selPane.SchemeControl.Selected, hitObj);
                    if (r != null || (selPane != pane && hitObj is RailSignal or TrackSection))
                    {
                        selPane.SchemeControl.SelectTarget(r == null ? hitObj : (object)r.Dest ?? r.End);   // нет маршрута — станция так и скажет
                        swallowUp = true;
                        return;
                    }
                }
            }
            switch (act)
            {
                case "move": SendMessage(sc.Handle, WM_MOUSEMOVE, IntPtr.Zero, lp); break;
                case "down":
                    SendMessage(sc.Handle, WM_MOUSEMOVE, IntPtr.Zero, lp);
                    SendMessage(sc.Handle, rb ? WM_RBUTTONDOWN : WM_LBUTTONDOWN, (IntPtr)(rb ? MK_RBUTTON : MK_LBUTTON), lp);
                    break;
                case "up": SendMessage(sc.Handle, rb ? WM_RBUTTONUP : WM_LBUTTONUP, IntPtr.Zero, lp); break;
            }
            return;
        }

        var b = shotBounds.Width > 0 ? shotBounds : Bounds();
        // [ДОБАВЛЕНО 18.11.2026] ДНЦ присылает рамку (доли окна), от которой считать x, y: у него несколько снимков разных частей
        if (d.TryGetProperty("rw", out var rwe) && rwe.TryGetDouble(out var rw) && rw > 0.01)
        {
            var wb = Bounds();
            double R(string n) => d.TryGetProperty(n, out var v) && v.TryGetDouble(out var r) ? r : 0;
            b = new Rectangle(wb.X + (int)(R("rx") * wb.Width), wb.Y + (int)(R("ry") * wb.Height),
                              Math.Max(1, (int)(rw * wb.Width)), Math.Max(1, (int)(R("rh") * wb.Height)));
        }
        double x = d.TryGetProperty("x", out var xe) ? xe.GetDouble() : 0, y = d.TryGetProperty("y", out var ye) ? ye.GetDouble() : 0;
        var screen = new Point(b.X + (int)(x * b.Width), b.Y + (int)(y * b.Height));
        var cap = GetCapture();
        var hwnd = cap != IntPtr.Zero ? cap : OwnWindowAt(screen);
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != Environment.ProcessId) return;   // чужие окна не трогаем
        var client = screen;
        ScreenToClient(hwnd, ref client);
        bool right = d.TryGetProperty("btn", out var be) && be.GetString() == "right";

        switch (act)
        {
            case "move": SendMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, LParam(client)); break;
            case "down":
                SendMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, LParam(client));
                SendMessage(hwnd, right ? WM_RBUTTONDOWN : WM_LBUTTONDOWN, (IntPtr)(right ? MK_RBUTTON : MK_LBUTTON), LParam(client));
                break;
            case "up":
            {
                SendMessage(hwnd, right ? WM_RBUTTONUP : WM_LBUTTONUP, IntPtr.Zero, LParam(client));
                // WinForms засчитывает нажатие кнопки, только если её окно сверху под курсором.
                // Окно станции перекрыто (например, другой программой) — нажимаем кнопку напрямую.
                if (!right && Control.FromHandle(hwnd) is ButtonBase bb && bb.Enabled && Topmost(screen) != hwnd)
                    typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                                   .Invoke(bb, new object[] { EventArgs.Empty });
                break;
            }
            case "wheel":
                int delta = d.TryGetProperty("delta", out var de) ? de.GetInt32() : 0;
                SendMessage(hwnd, WM_MOUSEWHEEL, (IntPtr)(delta << 16), LParam(screen));
                break;
        }
    }

    /// <summary>Отсекает мышь и клавиатуру студента (настоящий ввод идёт через очередь сообщений;
    /// ввод преподавателя — SendMessage, мимо фильтра).</summary>
    class Blocker : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            bool mouse = m.Msg is >= 0x201 and <= 0x20E;
            bool keys = m.Msg is >= 0x100 and <= 0x109;
            if (!mouse && !keys) return false;

            if (Training.TeacherControl)
            {
                if (keys && Environment.TickCount64 < allowKeysUntil) return false;
                Hint($"Управление у {Training.ControlByName} — ваши нажатия не действуют", m.Msg);   // [ИЗМЕНЕНО 18.11.2026]
                return true;
            }
            if (Training.Paused && mouse)
            {
                var c = Control.FromChildHandle(m.HWnd);
                if (c == null || c.FindForm() != main) return false;   // окна входа, задания и т.п.
                for (var p = c; p != null; p = p.Parent) if (p == bar) return false;
                Hint("Игровое время на паузе — дождитесь пуска преподавателем", m.Msg);
                return true;
            }
            return false;
        }

        static void Hint(string text, int msg)
        {
            if (msg is not (0x201 or 0x204 or 0x100)) return;
            if (Environment.TickCount64 - lastHint < 3000) return;
            lastHint = Environment.TickCount64;
            StationApp.Model.ModelHost.Model.IL.Say("[Занятие] " + text, LogKind.Refuse);
        }
    }
}
