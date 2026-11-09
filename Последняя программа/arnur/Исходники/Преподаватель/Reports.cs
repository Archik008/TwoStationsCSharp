using System.Diagnostics;
using System.Net;
using System.Text;

namespace TeacherApp;

// =====================================================================
//  ОТЧЁТЫ — HTML-страницы (открываются в браузере: печать Ctrl+P, сохранить Ctrl+S)
//  и сводная ведомость в Excel (CSV с разделителем «;» — Excel открывает сразу).
// =====================================================================

public static class Reports
{
    static string H(string s) => WebUtility.HtmlEncode(s ?? "");
    public static string Clock(double sec) { var t = TimeSpan.FromSeconds(Math.Max(0, sec) % 86400); return $"{t.Hours:00}:{t.Minutes:00}"; }
    public static string Dur(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} ч {t.Minutes} мин" : $"{t.Minutes} мин {t.Seconds} с";

    const string Css = @"<style>
body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#111}
h1{font-size:20px;margin:0 0 4px} h2{font-size:16px;margin:18px 0 6px}
.meta{color:#555;margin-bottom:12px}
table{border-collapse:collapse;width:100%;font-size:13px;margin-bottom:12px}
th,td{border:1px solid #999;padding:4px 6px;text-align:left;vertical-align:top}
th{background:#e8eef4}
.ok{color:#0a6b2a;font-weight:bold}.bad{color:#b00020;font-weight:bold}
.crit{color:#b00020}.log{font-family:Consolas,monospace;font-size:12px;white-space:pre-wrap;border:1px solid #ccc;padding:8px;max-height:none}
.sign{margin-top:32px}
@media print{.noprint{display:none}}
</style>";

    static string Page(string title, string body) =>
        $"<!doctype html><html lang=ru><head><meta charset=utf-8><title>{H(title)}</title>{Css}</head><body>" +
        $"<div class=noprint style='margin-bottom:12px'><button onclick='window.print()'>Печать</button></div>{body}</body></html>";

    static void Open(string name, string html)
    {
        var path = Path.Combine(Db.ReportsDir, name + ".html");
        File.WriteAllText(path, StationApp.Core.L.Html(html), Encoding.UTF8);   // [ДОБАВЛЕНО 22.10.2026, этап 8] отчёт на выбранном языке
        OpenInBrowser(path);
    }

    /// <summary>Открыть файл в браузере, назначенном для ссылок http (у .html на ПК может не быть программы —
    /// тогда Windows спрашивает «Чем открыть»). Не вышло — как обычно, через Windows; совсем нет — Edge.</summary>
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

    static string Verdict(GameResult r) => r.Passed ? "<span class=ok>зачёт</span>" : "<span class=bad>незачёт</span>";

    /// <summary>Список студентов (вкладка «Отчёты», кнопка «Печать списка»).</summary>
    public static void StudentList(IEnumerable<(Student s, List<GameResult> games, bool playing)> rows, string filter)
    {
        var sb = new StringBuilder($"<h1>Список студентов</h1><div class=meta>{H(filter)} · сформирован {DateTime.Now:dd.MM.yyyy HH:mm}</div>");
        sb.Append("<table><tr><th>№</th><th>ФИО</th><th>Группа</th><th>Статус</th><th>Занятий</th><th>Последнее</th><th>Средний балл</th><th>Зачётов</th></tr>");
        int i = 0;
        foreach (var (s, g, playing) in rows)
            sb.Append($"<tr><td>{++i}</td><td>{H(s.Name)}</td><td>{H(s.Group)}</td><td>{(playing ? "играет" : "не играет")}</td><td>{g.Count}</td>" +
                      $"<td>{(g.Count > 0 ? g.Max(x => x.Ended).ToString("dd.MM.yyyy HH:mm") : "—")}</td>" +
                      $"<td>{(g.Count > 0 ? g.Average(x => x.Score).ToString("0") : "—")}</td><td>{g.Count(x => x.Passed)}</td></tr>");
        sb.Append("</table>");
        Open("список_студентов", Page("Список студентов", sb.ToString()));
    }

    /// <summary>Статистика студента за всё время.</summary>
    public static void StudentGames(Student s, List<GameResult> games)
    {
        var sb = new StringBuilder($"<h1>Отчёты: {H(s.Name)}</h1><div class=meta>Группа {H(s.Group)} · занятий: {games.Count} · " +
                                   $"зачётов: {games.Count(g => g.Passed)} · средний балл: {(games.Count > 0 ? games.Average(g => g.Score).ToString("0") : "—")}</div>");
        sb.Append("<table><tr><th>Дата</th><th>Урок</th><th>Станция</th><th>Длительность</th><th>Принято</th><th>Отправлено</th>" +
                  "<th>Крит. события</th><th>Ошибки</th><th>Баллы</th><th>Итог</th></tr>");
        foreach (var g in games.OrderBy(g => g.Ended))
            sb.Append($"<tr><td>{g.Started:dd.MM.yyyy HH:mm}</td><td>{H(g.LessonName)}</td><td>{H(g.StationName)}</td><td>{Dur(g.Duration)}</td>" +
                      $"<td>{g.Received} / {g.TaskReceive}</td><td>{g.Departed} / {g.TaskDepart}</td><td>{g.CriticalCount}</td><td>{g.WrongCount}</td>" +
                      $"<td>{g.Score}</td><td>{Verdict(g)}</td></tr>");
        sb.Append("</table>");
        Open("студент_" + s.Id, Page("Отчёты " + s.Name, sb.ToString()));
    }

    /// <summary>Подробный отчёт по одной деловой игре.</summary>
    public static void Detailed(GameResult g)
    {
        var sb = new StringBuilder($"<h1>Отчёт по деловой игре «{H(g.LessonName)}»</h1>");
        sb.Append($"<table><tr><th>ФИО</th><td>{H(g.StudentName)}</td><th>Группа</th><td>{H(g.StudentGroup)}</td></tr>" +
                  $"<tr><th>Станция (роль)</th><td>ДСП — {H(g.StationName)}</td><th>Дата</th><td>{g.Started:dd.MM.yyyy}</td></tr>" +
                  $"<tr><th>Начало — конец</th><td>{g.Started:HH:mm} — {g.Ended:HH:mm} ({Dur(g.Duration)})</td>" +
                  $"<th>Игровое время</th><td>{Clock(g.ClockStart)} — {Clock(g.ClockEnd)}</td></tr>" +
                  $"<tr><th>Принято поездов</th><td>{g.Received} (задание {g.TaskReceive})</td><th>Отправлено поездов</th><td>{g.Departed} (задание {g.TaskDepart})</td></tr>" +
                  $"<tr><th>Оценка</th><td><b>{g.Score}</b> из 100</td><th>Итог</th><td>{Verdict(g)}</td></tr>" +
                  (g.Leader.Length > 0 ? $"<tr><th>Руководитель</th><td colspan=3>{H(g.Leader)}</td></tr>" : "") + "</table>");

        sb.Append($"<h2>Критические события: {g.CriticalCount}</h2>");
        sb.Append(EventsTable(g.Events.Where(e => e.Critical)));
        sb.Append($"<h2>Неправильные действия: {g.WrongCount}</h2>");
        sb.Append(EventsTable(g.Events.Where(e => !e.Critical)));
        int shortRecv = Math.Max(0, g.TaskReceive - g.Received), shortDep = Math.Max(0, g.TaskDepart - g.Departed);
        if (shortRecv + shortDep > 0)
            sb.Append($"<p class=crit>Задание не выполнено: не принято {shortRecv}, не отправлено {shortDep} (−10 баллов за каждый поезд).</p>");
        // [ДОБАВЛЕНО 20.10.2026, этап 3] неисправности и журнал осмотра ДУ-46
        if (g.Faults.Count > 0)
        {
            sb.Append($"<h2>Неисправности (экспресс-вводные): {g.Faults.Count}</h2><table><tr><th>Неисправность</th><th>Объект</th>" +
                      "<th>Условие</th><th>Возникла</th><th>Окончена</th><th>Итог</th></tr>");
            foreach (var f in g.Faults)
                sb.Append($"<tr><td>{H(f.Name)}</td><td>{H(f.Label)}</td><td>{H(f.Cond)}</td><td>{(f.Activated >= 0 ? Clock(f.Activated) : "—")}</td>" +
                          $"<td>{(f.Ended >= 0 ? Clock(f.Ended) : "—")}</td><td>{H(f.State)}</td></tr>");
            sb.Append("</table>");
            foreach (var f in g.Faults.Where(f => f.Log.Count > 0))
                sb.Append($"<div class=log style='margin-bottom:6px'><b>{H(f.Name)} — {H(f.Label)}</b>\n{H(string.Join("\n", f.Log))}</div>");
        }
        sb.Append($"<h2>Журнал осмотра ДУ-46 ({g.Du46.Count} записей)</h2>");
        if (g.Du46.Count == 0) sb.Append("<p>записей нет</p>");
        else
        {
            sb.Append("<table><tr><th>Время</th><th>Должность</th><th>Фамилия</th><th>Объект</th><th>Запись / действие</th></tr>");
            foreach (var r in g.Du46)
                sb.Append($"<tr><td>{Clock(r.Clock)}</td><td>{H(r.Who)}</td><td>{H(r.Name)}</td><td>{H(r.ObjLabel)}</td><td>{H(r.Text)}</td></tr>");
            sb.Append("</table>");
        }
        // [ДОБАВЛЕНО 23.10.2026, этап 4] журналы ДУ-2/47/58/60, бланки, предупреждения, радиосвязь, автопроверка
        foreach (var d in g.Docs)
        {
            sb.Append($"<h2>{H(d.Title)} ({d.Rows.Count})</h2>");
            if (d.Rows.Count == 0) { sb.Append("<p>записей нет</p>"); continue; }
            sb.Append("<table><tr>" + string.Concat(d.Columns.Select(c => $"<th>{H(c)}</th>")) + "</tr>");
            foreach (var r in d.Rows)
            {
                bool bad = r.Any(c => c.Contains("НЕ ") || c.Contains("нет записи"));
                sb.Append($"<tr{(bad ? " class=crit" : "")}>" + string.Concat(r.Select(c => $"<td style='white-space:pre-wrap'>{H(c)}</td>")) + "</tr>");
            }
            sb.Append("</table>");
        }
        sb.Append(GidReport.Section(g));   // [ДОБАВЛЕНО 22.10.2026, этап 5] ГИД, выполнение расписания, показатели
        sb.Append($"<h2>Журнал действий ({g.Journal.Count} записей)</h2><div class=log>{H(string.Join("\n", g.Journal))}</div>");
        sb.Append("<div class=sign>Преподаватель: ____________________ &nbsp;&nbsp; Студент: ____________________</div>");
        Open($"игра_{g.Started:yyyyMMdd_HHmm}_{g.Station}", Page("Отчёт " + g.StudentName, sb.ToString()));
    }

    static string EventsTable(IEnumerable<GameEvent> ev)
    {
        var list = ev.ToList();
        if (list.Count == 0) return "<p>нет</p>";
        var sb = new StringBuilder("<table><tr><th>Игровое время</th><th>Что произошло</th><th>Штраф</th></tr>");
        foreach (var e in list) sb.Append($"<tr><td>{Clock(e.Clock)}</td><td>{H(e.Text)}</td><td>−{e.Penalty}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    /// <summary>Сводная ведомость.</summary>
    public static void Summary(string title, string group, DateTime date, List<GameResult> rows)
    {
        var sb = new StringBuilder($"<h1>{H(title)}</h1><div class=meta>Группа: {H(group)} · дата составления: {date:dd.MM.yyyy}</div>");
        sb.Append("<table><tr><th>№</th><th>ФИО</th><th>Группа</th><th>Урок</th><th>Станция</th><th>Дата</th><th>Принято</th><th>Отправлено</th>" +
                  "<th>Крит.</th><th>Ошибки</th><th>Баллы</th><th>Итог</th><th>Руководитель</th></tr>");
        int i = 0;
        foreach (var g in rows)
            sb.Append($"<tr><td>{++i}</td><td>{H(g.StudentName)}</td><td>{H(g.StudentGroup)}</td><td>{H(g.LessonName)}</td><td>{H(g.StationName)}</td>" +
                      $"<td>{g.Started:dd.MM.yyyy}</td><td>{g.Received}/{g.TaskReceive}</td><td>{g.Departed}/{g.TaskDepart}</td><td>{g.CriticalCount}</td>" +
                      $"<td>{g.WrongCount}</td><td>{g.Score}</td><td>{Verdict(g)}</td><td>{H(g.Leader)}</td></tr>");
        sb.Append($"</table><p>Всего: {rows.Count}, зачёт: {rows.Count(r => r.Passed)}, незачёт: {rows.Count(r => !r.Passed)}</p>");
        sb.Append("<div class=sign>Руководитель занятий: ____________________</div>");
        Open("сводная_ведомость", Page(title, sb.ToString()));
    }

    public static void SummaryCsv(string path, string title, string group, DateTime date, List<GameResult> rows)
    {
        static string C(string s) => "\"" + StationApp.Core.L.T(s ?? "").Replace("\"", "\"\"") + "\"";   // [ДОБАВЛЕНО 22.10.2026, этап 8] перевод
        var sb = new StringBuilder();
        sb.AppendLine(C(title));
        sb.AppendLine($"{C("Группа")};{C(group)};{C("Дата")};{C(date.ToString("dd.MM.yyyy"))}");
        sb.AppendLine(string.Join(";", "№;ФИО;Группа;Урок;Станция;Дата;Принято;Задание приём;Отправлено;Задание отпр.;Крит. события;Ошибки;Баллы;Итог;Руководитель"
                                     .Split(';').Select(StationApp.Core.L.T)));   // [ДОБАВЛЕНО 22.10.2026, этап 8]
        int i = 0;
        foreach (var g in rows)
            sb.AppendLine(string.Join(";", ++i, C(g.StudentName), C(g.StudentGroup), C(g.LessonName), C(g.StationName), g.Started.ToString("dd.MM.yyyy"),
                g.Received, g.TaskReceive, g.Departed, g.TaskDepart, g.CriticalCount, g.WrongCount, g.Score, C(g.Verdict), C(g.Leader)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }
}
