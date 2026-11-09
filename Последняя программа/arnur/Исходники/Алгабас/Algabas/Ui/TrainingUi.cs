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
        bool show = Training.TeacherOnline || Training.Active;
        Visible = show;
        if (!show) return;

        if (Training.TeacherControl)
        {
            lblLesson.Text = "УПРАВЛЕНИЕ У ПРЕПОДАВАТЕЛЯ";
            lblLesson.ForeColor = Color.FromArgb(255, 120, 120);
        }
        else
        {
            lblLesson.Text = Training.Active ? $"Урок: {Training.Lesson?.Name}" : "Преподаватель на связи";
            lblLesson.ForeColor = Color.White;
        }
        lblStudent.Text = "Студент: " + (Training.Student?.ToString() ?? "—");
        btnLogin.Text = Training.Student == null ? "Вход" : "Сменить";
        lblClock.Visible = btnTask.Visible = Training.Active;
        btnSched.Visible = Training.Active && Schedule.Data != null;   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        lblClock.Text = Training.Running ? $"{Training.ClockText}  ×{Training.Speed}" : $"{Training.ClockText}  ПАУЗА";
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

        var b = shotBounds.Width > 0 ? shotBounds : Bounds();
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
                Hint("Управление у преподавателя — ваши нажатия не действуют", m.Msg);
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
