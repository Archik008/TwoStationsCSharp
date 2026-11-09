using System.Text.Json;
using TeacherApp;

// =====================================================================
//  [ДОБАВЛЕНО 22.10.2026, этап 5] Снимки вкладок преподавателя «Расписание» и «ГИД» и раздел ГИД подробного отчёта.
//  События поездов — из tools\ScheduleTest (gid_events.json в той же папке).
//  Запуск: dotnet run --project tools\ScheduleTest <папка>, затем dotnet run --project tools\Stage5Shots <папка>
//  Данные преподавателя — в папке «данные» рядом с exe этого инструмента (не в готовая_программа).
// =====================================================================

var dir = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
Directory.CreateDirectory(dir);
// [ДОБАВЛЕНО 22.10.2026, этап 8] второй аргумент — язык (kk / en): снимки всех вкладок окна преподавателя на этом языке
string lang = args.Length > 1 ? args[1] : "ru";
StationApp.Ui.LangUi.Install();
StationApp.Ui.LangUi.TranslateProperties(typeof(Lesson));
StationApp.Core.L.Set(lang);
Db.Load();
if (lang != "ru" || args.Contains("all"))   // [ИЗМЕНЕНО 23.10.2026] «all» — все вкладки и для русского (снимки для инструкций)
{
    var tf = new TeacherForm { WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), Size = new Size(1500, 900) };
    tf.Show();
    var tabsT = tf.Controls.OfType<TabControl>().First();
    for (int i = 0; i < tabsT.TabCount; i++)
    {
        tabsT.SelectedIndex = i;
        for (int k = 0; k < 20; k++) { Application.DoEvents(); Thread.Sleep(50); }
        using var b = new Bitmap(tf.Width, tf.Height);
        tf.DrawToBitmap(b, new Rectangle(Point.Empty, tf.Size));
        b.Save(Path.Combine(dir, $"teacher_{lang}_{i}.png"));
        Console.WriteLine($"снимок: teacher_{lang}_{i}");
    }
    tf.Dispose();
    StationApp.Core.DispatchLink.Stop(true);
    return;
}
Console.WriteLine($"расписаний: {Db.Schedules.Count}, уроков: {Db.Lessons.Count}, ошибок в учебных: " +
                  string.Join("; ", Db.Schedules.SelectMany(s => s.Validate())));

var lesson = Db.Lessons.First(l => l.ScheduleId == "uch1");
Gid.Begin("shots", lesson, 8 * 3600);
var evFile = Path.Combine(dir, "gid_events.json");
if (File.Exists(evFile))
    foreach (var e in JsonDocument.Parse(File.ReadAllText(evFile)).RootElement.EnumerateArray())
        Gid.Add(JsonSerializer.SerializeToElement(new { ev = e }));
Console.WriteLine($"событий ГИД: {Gid.Current.Events.Count}");

var form = new Form { Size = new Size(1500, 900), StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), Font = new Font("Segoe UI", 10f) };
var tabs = new TabControl { Dock = DockStyle.Fill };
tabs.TabPages.Add(ScheduleTab.Build(form, () => { }));
tabs.TabPages.Add(GidTab.Build(form));
form.Controls.Add(tabs);
form.Show();
Application.DoEvents();

void Shot(int tab, string name)
{
    tabs.SelectedIndex = tab;
    for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(50); }
    using var bmp = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
    bmp.Save(Path.Combine(dir, name + ".png"));
    Console.WriteLine("снимок: " + name);
}

Shot(0, "teacher_schedule");
Shot(1, "teacher_gid");

var html = "<!doctype html><html><head><meta charset=utf-8><style>table{border-collapse:collapse}td,th{border:1px solid #999;padding:3px}</style></head><body>" +
           GidReport.Section(new GameResult { GameId = "shots", Station = "akzht", ClockStart = 8 * 3600 }) + "</body></html>";
File.WriteAllText(Path.Combine(dir, "report_gid.html"), html);
File.WriteAllBytes(Path.Combine(dir, "report_gid.png"), Convert.FromBase64String(GidPainter.Png(Gid.Current)));
Console.WriteLine("отчёт: report_gid.html, report_gid.png");
