namespace TeacherApp;

// =====================================================================
//  ЭКРАН СТАНЦИИ В ОТДЕЛЬНОМ ОКНЕ  [ДОБАВЛЕНО 21.10.2026]
//  Окно открывается развёрнутым, F11 — на весь экран без рамки. Станция присылает кадр шириной с это окно,
//  то есть в полном разрешении. «Часть окна»: у АКЖТ окно растянуто на два монитора — можно смотреть
//  только «АКЖТ» или только «Путевой пост», тогда эта половина занимает весь экран преподавателя.
//  «Забрать управление» — мышь и клавиатура над картинкой управляют пультом студента.
// =====================================================================

public class StationScreenWindow : Form
{
    readonly StationRt st;
    readonly string tag = "big" + Guid.NewGuid().ToString("N")[..6];
    readonly ScreenView view = new() { Dock = DockStyle.Fill, Placeholder = "Ожидание экрана станции…" };
    readonly FlowLayoutPanel bar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, BackColor = Color.FromArgb(35, 60, 80), Padding = new Padding(4, 2, 4, 2) };
    readonly Label lbl = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(6, 8, 16, 0) };
    readonly Button bTake = Btn("Забрать управление"), bFull = Btn("На весь экран (F11)");
    readonly FlowLayoutPanel partsBox = new() { AutoSize = true, WrapContents = false, Margin = new Padding(12, 0, 0, 0) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };

    /// <summary>Какая часть окна станции (null — всё окно) и её название.</summary>
    RectangleF? region;
    string regionName = "Всё окно";
    bool userChose;
    List<(string name, RectangleF rect)> parts = new();
    bool awaiting;
    long requestedAt, nextAt;
    FormWindowState before;

    public StationScreenWindow(StationRt station)
    {
        st = station;
        Font = new Font("Segoe UI", 10f);
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        BackColor = Color.FromArgb(40, 44, 52);

        bTake.Click += (_, _) => Classroom.SetTake(st, !st.Take);
        bFull.Click += (_, _) => ToggleFull();
        var bClose = Btn("Закрыть");
        bClose.Click += (_, _) => Close();
        bar.Controls.AddRange(new Control[] { lbl, bTake, partsBox, bFull, bClose });
        Controls.Add(view);
        Controls.Add(bar);
        FillParts();

        view.Input += d => Classroom.Input(st, d);
        Classroom.Screen += OnFrame;
        Classroom.Changed += RefreshState;
        timer.Tick += (_, _) => Loop();
        timer.Start();
        FormClosed += (_, _) =>
        {
            timer.Dispose();
            Classroom.Screen -= OnFrame;
            Classroom.Changed -= RefreshState;
        };
        RefreshState();
    }

    public StationRt Station => st;

    static Button Btn(string t) => new()
    {
        Text = t, AutoSize = true, Margin = new Padding(4, 3, 0, 3), FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(240, 240, 255), Font = new Font("Segoe UI", 9.5f)
    };

    // ---- F11 / Esc: на весь экран без рамки и обратно (Esc уходит на станцию, если управление у вас) ----
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11) { ToggleFull(); return true; }
        if (keyData == Keys.Escape && FormBorderStyle == FormBorderStyle.None && !view.InputEnabled) { ToggleFull(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void ToggleFull()
    {
        if (FormBorderStyle != FormBorderStyle.None)
        {
            before = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;     // иначе развёрнутое окно без рамки не закрывает панель задач
            WindowState = FormWindowState.Maximized;
            bFull.Text = "Обычное окно (F11)";
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = before == FormWindowState.Minimized ? FormWindowState.Maximized : before;
            bFull.Text = "На весь экран (F11)";
        }
        nextAt = 0;
    }

    // ---- части окна станции ----
    void FillParts()
    {
        partsBox.Controls.Clear();
        if (parts.Count == 0) { region = null; regionName = "Всё окно"; return; }
        partsBox.Controls.Add(new Label { Text = "Показать:", AutoSize = true, ForeColor = Color.White, Margin = new Padding(0, 8, 2, 0) });
        foreach (var (name, rect) in parts.Prepend(("Всё окно", RectangleF.Empty)))
        {
            var b = Btn(name);
            bool on = name == regionName;
            b.BackColor = on ? Color.FromArgb(52, 152, 219) : Color.FromArgb(240, 240, 255);
            b.ForeColor = on ? Color.White : Color.Black;
            var (n, r) = (name, rect);
            b.Click += (_, _) => { userChose = true; regionName = n; region = r.IsEmpty ? null : r; nextAt = 0; awaiting = false; FillParts(); };
            partsBox.Controls.Add(b);
        }
    }

    void RefreshState()
    {
        if (IsDisposed) return;
        string who = st.Student?.ToString() ?? "студент не вошёл";
        Text = $"Экран станции — {st.Name} ({who})";
        lbl.Text = $"{st.Name}: {who}" + (st.Online ? "" : " — НЕТ СВЯЗИ");
        bTake.Enabled = st.Online;
        bTake.Text = st.Take ? "Вернуть управление студенту" : "Забрать управление";
        bTake.BackColor = st.Take ? Color.FromArgb(255, 170, 160) : Color.FromArgb(240, 240, 255);
        view.InputEnabled = st is { Take: true, Online: true };
        bar.BackColor = view.InputEnabled ? Color.FromArgb(150, 40, 30) : Color.FromArgb(35, 60, 80);
    }

    // ---- кадры: следующий запрашивается, когда пришёл предыдущий ----
    void Loop()
    {
        if (!st.Online || WindowState == FormWindowState.Minimized) return;
        long now = Environment.TickCount64;
        if (awaiting && now - requestedAt < 4000) return;
        if (now < nextAt) return;
        awaiting = true;
        requestedAt = now;
        // ширина кадра = ширина окна: станция присылает картинку 1:1 (или меньше, если её экран меньше)
        Classroom.RequestScreen(st, Math.Clamp(view.Width, 640, 3840), region, tag);
    }

    void OnFrame(string id, ScreenFrame f)
    {
        if (id != st.Id || f.Tag != tag) return;
        awaiting = false;
        nextAt = Environment.TickCount64 + (view.InputEnabled ? 100 : 300);
        try { view.SetImage(Image.FromStream(new MemoryStream(Convert.FromBase64String(f.Jpg)))); } catch { }
        // станция сообщила свои части окна (у АКЖТ — две схемы); изменились — перестроить кнопки
        var names = string.Join("|", f.Parts.Select(p => p.name + p.rect));
        if (names != string.Join("|", parts.Select(p => p.name + p.rect)))
        {
            parts = f.Parts.ToList();
            // у АКЖТ окно на двух мониторах: сразу показываем первую схему крупно (переключить — кнопками вверху)
            if (!userChose && regionName == "Всё окно" && parts.Count > 0) regionName = parts[0].name;
            var cur = parts.FirstOrDefault(p => p.name == regionName);
            if (cur.name == null) { regionName = "Всё окно"; region = null; }
            else region = cur.rect;
            FillParts();
        }
    }
}
