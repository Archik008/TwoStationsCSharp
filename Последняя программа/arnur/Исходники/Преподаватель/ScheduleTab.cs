using System.ComponentModel;
using System.Globalization;
using StationApp.Core;

namespace TeacherApp;

// =====================================================================
//  ВКЛАДКА «РАСПИСАНИЕ»  [ДОБАВЛЕНО 22.10.2026, этап 5]
//  Учебные расписания (данные\schedules.json): слева список, справа поезда — строка на поезд,
//  столбцы по пунктам (АКЖТ приб./отпр./путь, пост — проследование, Алгабас приб./отпр./путь).
//  Изменения сохраняются сразу, ошибки показываются внизу, там же — нормативный график.
//  Урок ссылается на расписание: «Уроки» → «5. Расписание».
// =====================================================================

public static class ScheduleTab
{
    static readonly (string id, string name)[] Origins = SchedPoints.Origins;

    public static TabPage Build(Form owner, Action lessonsChanged)
    {
        var page = new TabPage("Расписание");
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var txtName = new TextBox { Dock = DockStyle.Top };
        var txtDesc = new TextBox { Dock = DockStyle.Top, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window,
            SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnEnter
        };
        var errors = new ListBox { Dock = DockStyle.Fill, ForeColor = Color.DarkRed, IntegralHeight = false, HorizontalScrollbar = true };
        var preview = new GidView { Dock = DockStyle.Fill, Mode = GidMode.Normative, Font = new Font("Segoe UI", 9f) };

        // ---- столбцы ----
        var cCat = new DataGridViewComboBoxColumn { Name = "cat", HeaderText = "Категория", FlatStyle = FlatStyle.Flat };
        cCat.Items.AddRange(SchedPoints.Categories);
        var cOrigin = new DataGridViewComboBoxColumn { Name = "orig", HeaderText = "Откуда", FlatStyle = FlatStyle.Flat };
        cOrigin.Items.AddRange(Origins.Select(o => o.name).ToArray());
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = "№", FillWeight = 50 });
        grid.Columns.Add(cCat);
        grid.Columns.Add(cOrigin);
        cOrigin.FillWeight = 150;
        (string name, string header, int w)[] cols =
        {
            ("turn", "Оборот из №", 55), ("aa", "АКЖТ приб.", 55), ("ad", "АКЖТ отпр.", 55), ("at", "АКЖТ путь", 45),
            ("p", "Пост", 55), ("ga", "Алгабас приб.", 60), ("gd", "Алгабас отпр.", 60), ("gt", "Алгабас путь", 50),
            ("to", "Куда", 130), ("note", "Примечание", 120),
        };
        foreach (var (n, h, w) in cols) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = h, FillWeight = w });
        grid.Columns["to"].ReadOnly = true;
        grid.Columns["to"].DefaultCellStyle.ForeColor = Color.DimGray;
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        ScheduleData cur = null;
        bool loading = false;

        void Save(bool refillList = false)
        {
            Db.SaveSchedules();
            if (cur != null)
            {
                errors.Items.Clear();
                var err = cur.Validate();
                foreach (var e in err) errors.Items.Add(e);
                if (err.Count == 0) errors.Items.Add("Ошибок нет.");
                errors.ForeColor = err.Count == 0 ? Color.DarkGreen : Color.DarkRed;
                preview.Record = new GidRecord { GameId = "plan_" + cur.Id, Schedule = cur };
                preview.Invalidate();
            }
            if (refillList) FillList(cur);
        }

        object[] RowValues(SchedTrain t)
        {
            var a = t.Stop(SchedPoints.Akzht);
            var p = t.Stop(SchedPoints.Post);
            var g = t.Stop(SchedPoints.Algabas);
            return new object[]
            {
                t.Number.ToString(), SchedPoints.Categories.Contains(t.Category) ? t.Category : null,
                Origins.FirstOrDefault(o => o.id == t.Origin).name, t.TurnFrom > 0 ? t.TurnFrom.ToString() : "",
                a?.Arr ?? "", a?.Dep ?? "", a?.Track ?? "", p?.Dep ?? p?.Arr ?? "", g?.Arr ?? "", g?.Dep ?? "", g?.Track ?? "",
                t.Destination, t.Note
            };
        }

        void FillGrid()
        {
            loading = true;
            grid.Rows.Clear();
            if (cur != null)
                foreach (var t in cur.Trains)
                {
                    int i = grid.Rows.Add(RowValues(t));
                    grid.Rows[i].Tag = t;
                }
            txtName.Text = cur?.Name ?? "";
            txtDesc.Text = cur?.Description ?? "";
            grid.Enabled = txtName.Enabled = txtDesc.Enabled = cur != null;
            loading = false;
            Save();
        }

        void FillList(ScheduleData select)
        {
            loading = true;
            list.Items.Clear();
            foreach (var s in Db.Schedules) list.Items.Add(s);
            loading = false;
            list.SelectedItem = select ?? (list.Items.Count > 0 ? list.Items[0] : null);
            if (list.SelectedItem == null) { cur = null; FillGrid(); }
        }

        string Cell(DataGridViewRow r, string col) => (r.Cells[col].Value?.ToString() ?? "").Trim();

        void ReadRow(DataGridViewRow r)
        {
            if (r.Tag is not SchedTrain t) return;
            t.Number = int.TryParse(Cell(r, "n"), out var n) ? n : 0;
            t.Category = Cell(r, "cat") is { Length: > 0 } c ? c : "грузовой";
            t.Origin = Origins.FirstOrDefault(o => o.name == Cell(r, "orig")).id ?? SchedPoints.FromTunnelAkzht;
            t.TurnFrom = int.TryParse(Cell(r, "turn"), out var tf) ? tf : 0;
            t.Note = Cell(r, "note");
            t.Stops.Clear();
            if (Cell(r, "aa").Length + Cell(r, "ad").Length > 0)
                t.Stops.Add(new SchedStop { Station = SchedPoints.Akzht, Arr = Cell(r, "aa"), Dep = Cell(r, "ad"), Track = Cell(r, "at") });
            if (Cell(r, "p").Length > 0)
                t.Stops.Add(new SchedStop { Station = SchedPoints.Post, Arr = Cell(r, "p"), Dep = Cell(r, "p") });
            if (Cell(r, "ga").Length + Cell(r, "gd").Length > 0)
                t.Stops.Add(new SchedStop { Station = SchedPoints.Algabas, Arr = Cell(r, "ga"), Dep = Cell(r, "gd"), Track = Cell(r, "gt") });
            loading = true;
            r.Cells["to"].Value = t.Destination;
            loading = false;
        }

        grid.CellValueChanged += (_, e) =>
        {
            if (loading || e.RowIndex < 0) return;
            ReadRow(grid.Rows[e.RowIndex]);
            Save();
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        list.SelectedIndexChanged += (_, _) =>
        {
            if (loading) return;
            cur = list.SelectedItem as ScheduleData;
            FillGrid();
        };
        txtName.TextChanged += (_, _) =>
        {
            if (loading || cur == null) return;
            cur.Name = txtName.Text;
            Db.SaveSchedules();
            int i = list.SelectedIndex;
            loading = true;
            if (i >= 0) list.Items[i] = cur;   // обновить подпись
            loading = false;
        };
        txtName.Leave += (_, _) => lessonsChanged();
        txtDesc.TextChanged += (_, _) => { if (!loading && cur != null) { cur.Description = txtDesc.Text; Db.SaveSchedules(); } };

        // ---- кнопки ----
        Button B(string t, Action a) { var b = new Button { Text = t, AutoSize = true, Margin = new Padding(3) }; b.Click += (_, _) => a(); return b; }
        var bNew = B("Новое", () => { var s = new ScheduleData(); Db.Schedules.Add(s); Save(true); FillList(s); lessonsChanged(); });
        var bCopy = B("Копировать", () =>
        {
            if (cur == null) return;
            var s = System.Text.Json.JsonSerializer.Deserialize<ScheduleData>(System.Text.Json.JsonSerializer.Serialize(cur))!;
            s.Id = Guid.NewGuid().ToString("N")[..8];
            s.Name += " (копия)";
            Db.Schedules.Add(s);
            Save(true);
            FillList(s);
            lessonsChanged();
        });
        var bDel = B("Удалить", () =>
        {
            if (cur == null) return;
            var used = Db.Lessons.Where(l => l.ScheduleId == cur.Id).Select(l => l.Name).ToList();
            string q = $"Удалить расписание «{cur.Name}»?" + (used.Count > 0 ? $"\nОно указано в уроках: {string.Join(", ", used)} — там станет «без расписания»." : "");
            if (MessageBox.Show(owner, q, "Расписание", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var l in Db.Lessons.Where(l => l.ScheduleId == cur.Id)) l.ScheduleId = "";
            Db.SaveLessons();
            Db.Schedules.Remove(cur);
            cur = null;
            Save(true);
            lessonsChanged();
        });
        var bDefault = B("Добавить учебные", () =>
        {
            foreach (var s in DefaultSchedules.Build())
                if (Db.Schedules.All(x => x.Id != s.Id)) Db.Schedules.Add(s);
            Save(true);
            lessonsChanged();
        });
        var bAddTrain = B("Добавить поезд", () =>
        {
            if (cur == null) return;
            int n = cur.Trains.Count == 0 ? 2002 : cur.Trains.Max(t => t.Number) + 2;
            var t = new SchedTrain { Number = n, Category = "грузовой", Origin = SchedPoints.FromTunnelAkzht };
            cur.Trains.Add(t);
            loading = true;
            int i = grid.Rows.Add(RowValues(t));
            grid.Rows[i].Tag = t;
            loading = false;
            grid.CurrentCell = grid.Rows[i].Cells["aa"];
            Save();
        });
        var bDelTrain = B("Удалить поезд", () =>
        {
            if (cur == null || grid.CurrentRow?.Tag is not SchedTrain t) return;
            cur.Trains.Remove(t);
            grid.Rows.Remove(grid.CurrentRow);
            Save();
        });
        var bSort = B("Упорядочить по времени", () =>
        {
            if (cur == null) return;
            cur.Trains = cur.Trains.OrderBy(t => t.FirstSec).ToList();
            FillGrid();
        });

        // ---- раскладка ----
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(list);
        var lb = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        lb.Controls.AddRange(new Control[] { bNew, bCopy, bDel, bDefault });
        left.Controls.Add(lb);

        var head = new Panel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(4) };
        head.Controls.Add(txtDesc);
        head.Controls.Add(new Label { Text = "Описание:", Dock = DockStyle.Top, Height = 20 });
        head.Controls.Add(txtName);
        head.Controls.Add(new Label { Text = "Название расписания:", Dock = DockStyle.Top, Height = 20 });
        var help = new Label
        {
            Dock = DockStyle.Top, Height = 54, ForeColor = Color.DimGray, Padding = new Padding(4, 2, 4, 0),
            Text = "Время — игровое ЧЧ:ММ. Чётные номера идут АКЖТ → пост → Алгабас (или тоннель II), нечётные — обратно. На посту — время проследования.\n" +
                   "Проследование станции без остановки — одинаковые приб. и отпр. Конечная — пустое отпр. Оборот — поезд «Оборот из №» разворачивается на станции.\n" +
                   "Пассажирские и пригородные не уходят раньше расписания. Опоздание отправления по вине станции больше 5 мин — критическое событие."
        };
        var trains = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        trains.Controls.AddRange(new Control[] { bAddTrain, bDelTrain, bSort });

        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        rightSplit.Panel1.Controls.Add(grid);
        rightSplit.Panel1.Controls.Add(trains);
        rightSplit.Panel1.Controls.Add(help);
        rightSplit.Panel1.Controls.Add(head);
        var bottom = new SplitContainer { Dock = DockStyle.Fill };
        bottom.Panel1.Controls.Add(errors);
        bottom.Panel1.Controls.Add(new Label { Text = "Проверка:", Dock = DockStyle.Top, Height = 20 });
        bottom.Panel2.Controls.Add(preview);
        bottom.Panel2.Controls.Add(new Label { Text = "Нормативный график (колесо — масштаб, наведение — сведения):", Dock = DockStyle.Top, Height = 20 });
        rightSplit.Panel2.Controls.Add(bottom);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(rightSplit);
        page.Controls.Add(split);
        owner.Shown += (_, _) =>
        {
            split.SplitterDistance = 320;
            rightSplit.SplitterDistance = Math.Max(200, rightSplit.Height - 300);
            bottom.SplitterDistance = Math.Min(420, bottom.Width / 3);
        };

        FillList(null);
        return page;
    }
}

/// <summary>Выбор расписания в свойствах урока: хранится Id, показывается название.</summary>
/// <summary>[ДОБАВЛЕНО 09.11.2026] Галочка в свойствах урока: «Да / Нет» (на языке интерфейса) вместо True / False.</summary>
public class YesNoConverter : BooleanConverter
{
    static string Yes => StationApp.Core.L.T("Да");
    static string No => StationApp.Core.L.T("Нет");

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is bool b) return b ? Yes : No;
        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        if (value is string s)
        {
            s = s.Trim();
            if (s == Yes || s == "Да") return true;
            if (s == No || s == "Нет") return false;
        }
        return base.ConvertFrom(context, culture, value);
    }
}

public class ScheduleRefConverter : StringConverter
{
    const string None = "(без расписания)";
    public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) =>
        new(new[] { "" }.Concat(Db.Schedules.Select(s => s.Id)).ToList());

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is string id)
            return StationApp.Core.L.T(id.Length == 0 ? None : Db.FindSchedule(id)?.Name ?? $"(нет расписания {id})");   // [этап 8] перевод
        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        if (value is string name)
            return name == None || name == StationApp.Core.L.T(None) || name.Length == 0 ? ""   // [этап 8] показ — на выбранном языке
                 : Db.Schedules.FirstOrDefault(s => s.Name == name || StationApp.Core.L.T(s.Name) == name)?.Id ?? name;
        return base.ConvertFrom(context, culture, value);
    }
}
