using System.Net;
using System.Text;
using StationApp.Core;

namespace TeacherApp;

// =====================================================================
//  ВКЛАДКА «ГИД»  [ДОБАВЛЕНО 22.10.2026, этап 5]
//  График текущего занятия строится сам по сообщениям станций (s_train_event); можно открыть прошлое занятие.
//  Режимы: нормативный график / ГИД / ГИД на нормативном. Внизу — показатели работы (скорости, график).
// =====================================================================

public static class GidTab
{
    public static TabPage Build(Form owner)
    {
        var page = new TabPage("ГИД");
        var view = new GidView { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9f) };
        var cbGame = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
        var rbN = new RadioButton { Text = "Нормативный график", AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
        var rbA = new RadioButton { Text = "ГИД", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
        var rbB = new RadioButton { Text = "ГИД на нормативном", AutoSize = true, Checked = true, Margin = new Padding(6, 6, 0, 0) };
        var bFit = new Button { Text = "Весь урок", AutoSize = true };
        var bPrint = new Button { Text = "Печать / в браузер", AutoSize = true };
        var bPng = new Button { Text = "Сохранить картинку…", AutoSize = true };
        var numL1 = new NumericUpDown { Minimum = 0.5m, Maximum = 100, DecimalPlaces = 1, Increment = 0.5m, Width = 60, Value = (decimal)Db.Settings.LenAkzhtPost };
        var numL2 = new NumericUpDown { Minimum = 0.5m, Maximum = 100, DecimalPlaces = 1, Increment = 0.5m, Width = 60, Value = (decimal)Db.Settings.LenPostAlgabas };
        var stats = new DataGridView
        {
            Dock = DockStyle.Bottom, Height = 190, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window, SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        var lblTotal = new Label { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(4, 4, 0, 0), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        foreach (var (n, h) in new[] { ("n", "№"), ("c", "Категория"), ("r", "Маршрут"), ("km", "км"), ("t", "В пути, мин"), ("s", "Стоянки, мин"),
                                       ("v1", "Техническая, км/ч"), ("v2", "Участковая, км/ч"), ("v3", "Участковая по графику"), ("o", "Отправления по графику") })
            stats.Columns.Add(n, h);
        stats.Columns["r"].FillWeight = 200;
        stats.Columns["o"].FillWeight = 160;
        stats.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        stats.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        static Label Lb(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(8, 7, 0, 0) };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        top.Controls.AddRange(new Control[] { Lb("Занятие:"), cbGame, rbN, rbA, rbB, bFit, bPrint, bPng,
                                              Lb("Перегоны, км: АКЖТ — пост"), numL1, Lb("пост — Алгабас"), numL2 });
        var hint = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray, Padding = new Padding(6, 2, 0, 0),
                               Text = "Колесо мыши — масштаб, перетаскивание или ← → — прокрутка, + / − — масштаб, Home — весь урок, наведение на нитку — сведения о поезде" };
        page.Controls.Add(view);
        page.Controls.Add(hint);
        page.Controls.Add(top);
        page.Controls.Add(lblTotal);
        page.Controls.Add(stats);

        List<GidRecord> saved = new();
        GidRecord Selected() => cbGame.SelectedIndex <= 0 ? Gid.Current : saved[cbGame.SelectedIndex - 1];

        void FillGames()
        {
            int sel = cbGame.SelectedIndex;
            saved = Gid.All().Where(r => r.GameId != Gid.Current?.GameId).ToList();
            cbGame.Items.Clear();
            cbGame.Items.Add(Gid.Current != null ? $"Текущее: {Gid.Current.LessonName} ({Gid.Current.Started:dd.MM HH:mm})" : "Текущее занятие (нет)");
            foreach (var r in saved) cbGame.Items.Add($"{r.Started:dd.MM.yyyy HH:mm} — {r.LessonName}");
            cbGame.SelectedIndex = sel >= 0 && sel < cbGame.Items.Count ? sel : 0;
        }

        void FillStats()
        {
            stats.Rows.Clear();
            var rec = Selected();
            if (rec == null) { lblTotal.Text = ""; return; }
            var rows = GidStats.Rows(rec);
            foreach (var r in rows)
                stats.Rows.Add(r.Number, r.Category, r.Route, r.Km.ToString("0.#"), r.TotalMin.ToString("0.0"), r.StopMin.ToString("0.0"),
                               GidStats.Kmh(r.Tech), GidStats.Kmh(r.Section), GidStats.Kmh(r.PlanSection), r.OnTime);
            var (tech, sect) = GidStats.Totals(rows);
            lblTotal.Text = $"Поездов на графике: {rows.Count}. Итого по участку: техническая скорость {GidStats.Kmh(tech)} км/ч, участковая {GidStats.Kmh(sect)} км/ч (игровое время)";
        }

        void Show()
        {
            view.Record = Selected();
            view.Invalidate();
            FillStats();
        }

        cbGame.DropDown += (_, _) => { var cur = cbGame.SelectedIndex; FillGames(); cbGame.SelectedIndex = Math.Min(cur, cbGame.Items.Count - 1); };
        cbGame.SelectedIndexChanged += (_, _) => Show();
        rbN.CheckedChanged += (_, _) => { if (rbN.Checked) view.Mode = GidMode.Normative; };
        rbA.CheckedChanged += (_, _) => { if (rbA.Checked) view.Mode = GidMode.Actual; };
        rbB.CheckedChanged += (_, _) => { if (rbB.Checked) view.Mode = GidMode.Both; };
        bFit.Click += (_, _) => view.Fit();
        void Lengths()
        {
            Db.Settings.LenAkzhtPost = (double)numL1.Value;
            Db.Settings.LenPostAlgabas = (double)numL2.Value;
            Db.SaveSettings();
            Show();
        }
        numL1.ValueChanged += (_, _) => Lengths();
        numL2.ValueChanged += (_, _) => Lengths();
        bPng.Click += (_, _) =>
        {
            var rec = Selected();
            if (rec == null) return;
            using var d = new SaveFileDialog { Filter = "Картинка PNG|*.png", FileName = $"ГИД_{rec.Started:yyyyMMdd_HHmm}.png" };
            if (d.ShowDialog(owner) == DialogResult.OK) File.WriteAllBytes(d.FileName, Convert.FromBase64String(GidPainter.Png(rec, 1800, 480)));
        };
        bPrint.Click += (_, _) =>
        {
            var rec = Selected();
            if (rec == null) return;
            var g = new GameResult { GameId = rec.GameId, ClockStart = rec.ClockStart, Station = "" };
            string html = $"<!doctype html><html lang=ru><head><meta charset=utf-8><title>ГИД</title><style>body{{font-family:Segoe UI,Arial,sans-serif;margin:24px}}" +
                          "table{border-collapse:collapse;width:100%;font-size:13px}th,td{border:1px solid #999;padding:3px 6px;text-align:left}th{background:#e8eef4}" +
                          ".crit{color:#b00020}@media print{.noprint{display:none}}</style></head><body><div class=noprint><button onclick='window.print()'>Печать</button></div>" +
                          $"<h1>ГИД: {WebUtility.HtmlEncode(rec.LessonName)}, {rec.Started:dd.MM.yyyy HH:mm}</h1>{GidReport.Section(g)}</body></html>";
            var path = Path.Combine(Db.ReportsDir, $"ГИД_{rec.Started:yyyyMMdd_HHmm}.html");
            File.WriteAllText(path, L.Html(html), Encoding.UTF8);   // [ДОБАВЛЕНО 22.10.2026, этап 8] на выбранном языке
            Reports.OpenInBrowser(path);
        };

        // текущее занятие обновляется само (не чаще раза в секунду)
        bool dirty = false;
        Gid.Changed += () => dirty = true;
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            if (!dirty || !page.Visible) return;
            dirty = false;
            if (cbGame.SelectedIndex <= 0) { FillGames(); Show(); }
        };
        timer.Start();
        page.Enter += (_, _) => { FillGames(); Show(); };
        FillGames();
        return page;
    }
}
