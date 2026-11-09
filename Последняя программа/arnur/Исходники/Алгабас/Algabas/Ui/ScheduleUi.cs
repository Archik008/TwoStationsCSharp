using System.Diagnostics;
using System.Net;
using System.Text;
using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  ОКНО «РАСПИСАНИЕ» НА СТАНЦИИ  [ДОБАВЛЕНО 22.10.2026, этап 5]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Кнопка «Расписание» — в полосе занятия (TrainingBar),
//  видна, когда в уроке есть расписание. Таблица: №, категория, пункт, приб., отпр., стоянка, путь по ТРА,
//  факт и отклонение. Сортировка — щелчок по заголовку, фильтры по пункту и категории, печать — в браузере.
// =====================================================================

public static class ScheduleUi
{
    static ScheduleWindow window;

    public static void Open(Form owner)
    {
        if (window == null || window.IsDisposed) { window = new ScheduleWindow(); window.Show(owner); }
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.BringToFront();
        window.Activate();
    }

    /// <summary>Открыть файл в браузере, назначенном для ссылок http (у .html на ПК может не быть программы).</summary>
    public static void OpenInBrowser(string path)
    {
        string uri = new Uri(path).AbsoluteUri;
        try
        {
            using var choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = choice?.GetValue("ProgId") as string;
            using var cmdKey = progId == null ? null : Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
            var cmd = cmdKey?.GetValue("") as string;
            if (!string.IsNullOrEmpty(cmd) && cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                Process.Start(new ProcessStartInfo(cmd[1..end], "\"" + uri + "\"") { UseShellExecute = false });
                return;
            }
        }
        catch { }
        try { Process.Start(new ProcessStartInfo("msedge", "\"" + uri + "\"") { UseShellExecute = true }); }
        catch { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    }
}

class ScheduleWindow : Form
{
    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window, AllowUserToResizeRows = false
    };
    readonly ComboBox cbPoint = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    readonly ComboBox cbCat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    readonly Label lblName = new() { AutoSize = true, Margin = new Padding(4, 8, 16, 0) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    string shownSchedule;

    const string AllPoints = "все пункты", AllCats = "все категории";

    public ScheduleWindow()
    {
        Text = "Расписание движения поездов";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(1180, 620);
        MinimumSize = new Size(900, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        lblName.Font = new Font(Font, FontStyle.Bold);

        (string name, string header, int w)[] cols =
        {
            ("n", "№", 55), ("cat", "Категория", 95), ("pt", "Пункт", 100), ("from", "Откуда", 140), ("to", "Куда", 150),
            ("arr", "Приб.", 55), ("dep", "Отпр.", 55), ("dw", "Стоянка", 80), ("tr", "Путь (ТРА)", 70),
            ("farr", "Факт приб.", 70), ("fdep", "Факт отпр.", 70), ("ftr", "Путь факт.", 65), ("dev", "Откл., мин", 70), ("st", "Состояние", 95),
        };
        foreach (var (name, header, w) in cols)
        {
            int i = grid.Columns.Add(name, header);
            grid.Columns[i].FillWeight = w;
            grid.Columns[i].SortMode = DataGridViewColumnSortMode.Automatic;
        }
        grid.Columns["n"].ValueType = typeof(int);
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        var bPrint = new Button { Text = "Печать…", AutoSize = true, Margin = new Padding(12, 3, 3, 3) };
        bPrint.Click += (_, _) => Print();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
        top.Controls.AddRange(new Control[]
        {
            lblName, new Label { Text = "Пункт:", AutoSize = true, Margin = new Padding(4, 8, 0, 0) }, cbPoint,
            new Label { Text = "Категория:", AutoSize = true, Margin = new Padding(12, 8, 0, 0) }, cbCat, bPrint,
            new Label { Text = "щелчок по заголовку столбца — сортировка", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 8, 0, 0) },
        });
        Controls.Add(grid);
        Controls.Add(top);

        cbPoint.Items.Add(AllPoints);
        foreach (var p in Schedule.Stations) cbPoint.Items.Add(SchedPoints.Name(p));
        cbPoint.SelectedIndex = 0;
        cbCat.Items.Add(AllCats);
        cbCat.Items.AddRange(SchedPoints.Categories);
        cbCat.SelectedIndex = 0;
        cbPoint.SelectedIndexChanged += (_, _) => Fill(true);
        cbCat.SelectedIndexChanged += (_, _) => Fill(true);

        timer.Tick += (_, _) => Fill(false);
        timer.Start();
        Disposed += (_, _) => timer.Dispose();
        Fill(true);
    }

    /// <summary>Строки расписания по фильтрам: поезд × пункт этой программы.</summary>
    List<(SchedTrain t, SchedStop s)> Rows()
    {
        var data = Schedule.Data;
        if (data == null) return new();
        var mine = Schedule.Stations.ToHashSet();
        string pt = cbPoint.SelectedIndex > 0 ? Schedule.Stations.ElementAt(cbPoint.SelectedIndex - 1) : null;
        string cat = cbCat.SelectedIndex > 0 ? (string)cbCat.SelectedItem : null;
        return data.Trains.Where(t => cat == null || t.Category == cat)
                   .SelectMany(t => t.Ordered.Where(s => mine.Contains(s.Station) && (pt == null || s.Station == pt)).Select(s => (t, s)))
                   .OrderBy(x => x.s.FirstSec ?? 0).ToList();
    }

    static string Dwell(SchedTrain t, SchedStop s)
    {
        if (s.Pass) return "без остановки";
        if (s.Dwell is double d) return $"{d / 60:0} мин";
        return s.ArrSec == null ? (t.Origin == SchedPoints.Turn ? $"оборот №{t.TurnFrom}" : "формирование") : "конечная";
    }

    static string From(SchedTrain t, SchedStop s)
    {
        var ord = t.Ordered;
        int i = ord.IndexOf(s);
        if (i > 0) return SchedPoints.Name(ord[i - 1].Station);
        return t.Origin == SchedPoints.Turn ? $"оборот №{t.TurnFrom}" : SchedPoints.OriginName(t.Origin);
    }

    static string To(SchedTrain t, SchedStop s)
    {
        var ord = t.Ordered;
        int i = ord.IndexOf(s);
        return i < ord.Count - 1 ? SchedPoints.Name(ord[i + 1].Station) : t.Destination;
    }

    static object[] Values(SchedTrain t, SchedStop s)
    {
        var (fa, fd) = Schedule.Fact(t.Number, s.Station);
        string dev = "";
        if (fd is double d && s.DepSec is double pd) dev = SchedTime.Dev(d, pd);
        else if (fa is double a && s.ArrSec is double pa) dev = SchedTime.Dev(a, pa);
        return new object[]
        {
            t.Number, t.Category, SchedPoints.Name(s.Station), From(t, s), To(t, s), s.Arr, s.Dep, Dwell(t, s),
            s.Station == SchedPoints.Post ? "" : s.Track,
            fa is double x ? SchedTime.FmtSec(x) : "", fd is double y && !(s.Pass && fa == fd) ? SchedTime.FmtSec(y) : "",
            Schedule.FactTrack(t.Number, s.Station), dev, Schedule.State(t.Number, s.Station),
        };
    }

    void Fill(bool rebuild)
    {
        if (IsDisposed) return;
        var data = Schedule.Data;
        lblName.Text = data == null ? "Расписания нет: его задаёт преподаватель в уроке" :
                       $"{data.Name}   ·   время {Training.ClockText}";
        if (data?.Id != shownSchedule) { shownSchedule = data?.Id; rebuild = true; }
        var rows = Rows();
        if (rebuild || grid.Rows.Count != rows.Count)
        {
            var sortCol = grid.SortedColumn;
            var order = grid.SortOrder;
            grid.Rows.Clear();
            foreach (var (t, s) in rows)
            {
                int i = grid.Rows.Add(Values(t, s));
                grid.Rows[i].Tag = (t, s);
            }
            if (sortCol != null && order != SortOrder.None)
                grid.Sort(sortCol, order == SortOrder.Ascending ? System.ComponentModel.ListSortDirection.Ascending
                                                                : System.ComponentModel.ListSortDirection.Descending);
        }
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.Tag is not ValueTuple<SchedTrain, SchedStop> tag) continue;
            var v = Values(tag.Item1, tag.Item2);
            for (int c = 9; c < v.Length; c++)
                if (!Equals(r.Cells[c].Value, v[c])) r.Cells[c].Value = v[c];
            string dev = (string)v[12];
            r.Cells[12].Style.ForeColor = dev.StartsWith("+") && int.TryParse(dev[1..], out var m) && m > 5 ? Color.DarkRed : Color.Black;
            string state = (string)v[13];
            r.DefaultCellStyle.BackColor = state switch
            {
                "на станции" => Color.FromArgb(255, 250, 215),
                "в пути" => Color.FromArgb(230, 242, 255),
                "отправлен" or "проследовал" => Color.FromArgb(236, 236, 236),
                _ => Color.White
            };
        }
    }

    void Print()
    {
        var data = Schedule.Data;
        if (data == null) { MessageBox.Show(this, "Расписания нет."); return; }
        static string H(string s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder("<!doctype html><html><head><meta charset=utf-8><title>Расписание</title><style>" +
            "body{font-family:Segoe UI,Arial,sans-serif;margin:24px}h1{font-size:18px}table{border-collapse:collapse;width:100%;font-size:13px}" +
            "th,td{border:1px solid #999;padding:3px 6px;text-align:left}th{background:#e8eef4}@media print{.noprint{display:none}}</style></head><body>" +
            "<div class=noprint><button onclick='window.print()'>Печать</button></div>");
        sb.Append($"<h1>Расписание движения поездов — {H(DispatchLink.StationName)}</h1><p>{H(data.Name)}. {H(data.Description)}</p><table><tr>");
        foreach (DataGridViewColumn c in grid.Columns) sb.Append($"<th>{H(c.HeaderText)}</th>");
        sb.Append("</tr>");
        foreach (DataGridViewRow r in grid.Rows)
        {
            sb.Append("<tr>");
            foreach (DataGridViewCell c in r.Cells) sb.Append($"<td>{H(c.Value?.ToString())}</td>");
            sb.Append("</tr>");
        }
        sb.Append($"</table><p>Сформировано {DateTime.Now:dd.MM.yyyy HH:mm}, игровое время {Training.ClockText}</p></body></html>");
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "расписание_" + DispatchLink.StationId + ".html");
            File.WriteAllText(path, L.Html(sb.ToString()), Encoding.UTF8);   // [ДОБАВЛЕНО 22.10.2026, этап 8] на выбранном языке
            ScheduleUi.OpenInBrowser(path);
        }
        catch (Exception ex) { MessageBox.Show(this, "Не удалось открыть: " + ex.Message); }
    }
}
