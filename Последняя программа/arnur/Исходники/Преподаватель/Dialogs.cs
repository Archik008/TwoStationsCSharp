namespace TeacherApp;

// Небольшие окна: ввод строки, связь, выбор студента, сводная ведомость.

static class Prompt
{
    /// <summary>Ввод строки. null — отмена.</summary>
    public static string Ask(IWin32Window owner, string title, string text, string value, bool password = false)
    {
        using var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                                 StartPosition = FormStartPosition.CenterParent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                 Font = new Font("Segoe UI", 10f) };
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(12), WrapContents = false };
        var tb = new TextBox { Text = value, Width = 380, UseSystemPasswordChar = password };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        p.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(380, 0) });
        p.Controls.Add(tb);
        p.Controls.Add(ok);
        f.Controls.Add(p);
        f.AcceptButton = ok;
        return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
    }
}

class ServerDialog : Form
{
    public ServerDialog()
    {
        Text = "Связь со станциями";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10f);
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(12), WrapContents = false };
        var chk = new CheckBox { Text = "Запускать сервер связи на этом ПК (PromServer.exe из папки ..\\Сервер)", AutoSize = true, Checked = Db.Settings.StartServer };
        var addr = new TextBox { Text = Db.Settings.Server, Width = 300 };
        p.Controls.Add(chk);
        p.Controls.Add(new Label { Text = "Адрес сервера (на этом ПК — 127.0.0.1):", AutoSize = true });
        p.Controls.Add(addr);
        p.Controls.Add(new Label { Text = $"Адреса этого ПК для программ станций: {Classroom.LocalAddresses()}\n" +
                                          "На ПК станций: кнопка «Связь…» → этот адрес → «Подключить».", AutoSize = true, ForeColor = Color.DimGray });
        var ok = new Button { Text = "Сохранить и переподключиться", AutoSize = true };
        ok.Click += (_, _) =>
        {
            Db.Settings.StartServer = chk.Checked;
            Db.Settings.Server = addr.Text.Trim().Length > 0 ? addr.Text.Trim() : "127.0.0.1";
            Db.SaveSettings();
            DialogResult = DialogResult.OK;
        };
        p.Controls.Add(ok);
        Controls.Add(p);
    }
}

class PickStudentDialog : Form
{
    public Student Picked;

    public PickStudentDialog(string station)
    {
        Text = $"Студент за пультом «{station}»";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10f);
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(12), WrapContents = false };
        var list = new ListBox { Width = 380, Height = 320 };
        foreach (var s in Db.Students.OrderBy(s => s.Group).ThenBy(s => s.Name)) list.Items.Add(s);
        var ok = new Button { Text = "Назначить", AutoSize = true };
        var none = new Button { Text = "Снять студента", AutoSize = true };
        ok.Click += (_, _) => { if (list.SelectedItem is Student s) { Picked = s; DialogResult = DialogResult.OK; } };
        list.DoubleClick += (_, _) => ok.PerformClick();
        none.Click += (_, _) => { Picked = null; DialogResult = DialogResult.OK; };
        p.Controls.Add(list);
        var b = new FlowLayoutPanel { AutoSize = true };
        b.Controls.Add(ok);
        b.Controls.Add(none);
        p.Controls.Add(b);
        Controls.Add(p);
    }
}

/// <summary>Сводная ведомость: наименование, группа, дата, руководитель занятий; печать (HTML) и Excel (CSV).</summary>
class SummaryDialog : Form
{
    readonly List<GameResult> rows;
    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window
    };

    public SummaryDialog(List<GameResult> rows, string group)
    {
        this.rows = rows;
        Text = "Сводная ведомость";
        Font = new Font("Segoe UI", 10f);
        Size = new Size(1100, 650);
        StartPosition = FormStartPosition.CenterParent;

        var title = new TextBox { Text = "Сводная ведомость по работе на тренажёре ДСП", Width = 420 };
        var grp = new TextBox { Text = group, Width = 120 };
        var date = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        var leader = new TextBox { Text = Db.Settings.LeaderName, Width = 300 };
        var addAll = new Button { Text = "Добавить всем", AutoSize = true };
        var addSel = new Button { Text = "Добавить для выделенных", AutoSize = true };
        var delSel = new Button { Text = "Удалить у выделенных", AutoSize = true };
        var print = new Button { Text = "Печать", AutoSize = true };
        var excel = new Button { Text = "Excel…", AutoSize = true };

        void SetLeader(IEnumerable<GameResult> which, string name)
        {
            foreach (var r in which) { r.Leader = name; Db.SaveResult(r); }
            if (name.Length > 0) { Db.Settings.LeaderName = name; Db.SaveSettings(); }
            Fill();
        }
        IEnumerable<GameResult> Sel() => grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (GameResult)r.Tag);
        addAll.Click += (_, _) => SetLeader(rows, leader.Text.Trim());
        addSel.Click += (_, _) => SetLeader(Sel().ToList(), leader.Text.Trim());
        delSel.Click += (_, _) => SetLeader(Sel().ToList(), "");
        print.Click += (_, _) => Reports.Summary(title.Text, grp.Text, date.Value, rows);
        excel.Click += (_, _) =>
        {
            using var sfd = new SaveFileDialog { Filter = "Excel (CSV)|*.csv", FileName = $"ведомость_{grp.Text}_{date.Value:yyyy-MM-dd}.csv" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            Reports.SummaryCsv(sfd.FileName, title.Text, grp.Text, date.Value, rows);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        };

        foreach (var (n, h) in new[] { ("n", "ФИО"), ("g", "Группа"), ("l", "Урок"), ("d", "Дата"), ("s", "Баллы"), ("v", "Итог"), ("r", "Руководитель") })
            grid.Columns.Add(n, h);
        Controls.Add(grid);
        Controls.Add(Line(new Label { Text = "Руководитель занятий:", AutoSize = true, Margin = new Padding(4, 8, 0, 0) }, leader, addAll, addSel, delSel, print, excel));
        Controls.Add(Line(new Label { Text = "Наименование:", AutoSize = true, Margin = new Padding(4, 8, 0, 0) }, title,
                          new Label { Text = "Группа:", AutoSize = true, Margin = new Padding(4, 8, 0, 0) }, grp,
                          new Label { Text = "Дата составления:", AutoSize = true, Margin = new Padding(4, 8, 0, 0) }, date));
        Fill();
    }

    static FlowLayoutPanel Line(params Control[] c)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        p.Controls.AddRange(c);
        return p;
    }

    void Fill()
    {
        grid.Rows.Clear();
        foreach (var r in rows)
        {
            int i = grid.Rows.Add(r.StudentName, r.StudentGroup, r.LessonName, r.Started.ToString("dd.MM.yyyy"), r.Score, r.Verdict, r.Leader);
            grid.Rows[i].Tag = r;
        }
    }
}
