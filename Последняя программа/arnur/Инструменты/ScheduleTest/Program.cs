using System.Reflection;
using System.Text.Json;
using StationApp.Core;
using StationApp.Model;

// =====================================================================
//  [ДОБАВЛЕНО 22.10.2026, этап 5] Тест без окон: поезда по расписанию на ядре АКЖТ + пост.
//  Урок с учебным расписанием №1, Алгабаса нет на связи. «Робот-ДСП» задаёт маршруты поездам,
//  которые стоят у светофора, на путь по расписанию. Проверяется: появление поездов с номерами по времени,
//  стоянка пассажирского до времени отправления, оборот / поезд от Алгабаса на границе, события ГИД,
//  опоздание отправления → критическое событие, скорость поездов из урока.
//  Запуск: dotnet run --project tools\ScheduleTest
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }

var onEvent = typeof(Training).GetMethod("OnEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
void Teacher(object data) => onEvent.Invoke(null, new object[] { Training.TeacherId, JsonSerializer.SerializeToElement(data) });

ModelHost.Start(false);
ModelHost.AutoNeighbours = true;     // Алгабаса нет — его входной «открывается сам»
var M = ModelHost.Model;
var il = M.IL;
var sim = M.Trains;
var log = new List<string>();
il.Log += (m, _) => log.Add($"{Training.ClockText} {m}");

var sched = DefaultSchedules.Build().First(s => s.Id == "uch1");
var hold = new HashSet<int> { 6001 };   // этому поезду робот откроет выходной только в 08:52 — опоздание
bool robot = true;

void Step(double gameSeconds)
{
    double end = Training.Clock + gameSeconds;
    while (Training.Clock < end)
    {
        if (robot) Robot();
        double mdt = Training.ModelDt(0.1);
        il.Tick(mdt);
        sim.Tick(mdt);
        Training.Tick(0.1);
        // тик ModelHost: соседние светофоры открыты
        foreach (var sig in il.Signals.Values)
            if (sig.Kind == SignalKind.Neighbor && !sig.NeighborOpen) sig.NeighborOpen = true;
    }
}

TrackSection Place(string station, string track) =>
    (station == SchedPoints.Akzht ? M.Akzht.View.TrainPlaces : M.Post.View.TrainPlaces)
        .FirstOrDefault(p => p.name == "Путь " + track).section;

void Robot()
{
    foreach (var t in sim.Trains.Where(t => !t.Leaving && !t.Shunting).ToList())
    {
        var cand = il.Routes.Where(r => !r.Shunting && !r.Active && r.Start.Approach == t.Head && r.Start.Dir == t.Dir).ToList();
        if (cand.Count == 0) continue;
        var st = sched.Find(t.Number)?.Stop(SchedPoints.Akzht);
        // на пути АКЖТ робот отправляет по расписанию (конечные — не отправляет), №6001 — с опозданием
        if (M.Akzht.View.TrainPlaces.Any(p => p.section == t.Head) && st != null)
        {
            if (st.DepSec is not double dep || Training.Clock < dep) continue;
            if (hold.Contains(t.Number) && Training.Clock < SchedTime.Parse("08:52")) continue;
        }
        var want = st != null ? Place(SchedPoints.Akzht, st.Track) : null;
        var r = cand.FirstOrDefault(r => r.Dest != null && r.Dest == want && il.CheckRoute(r) == null)
                ?? cand.FirstOrDefault(r => r.Dest != null && M.Akzht.View.TrainPlaces.All(p => p.section != r.Dest) && il.CheckRoute(r) == null)
                ?? cand.FirstOrDefault(r => r.ExitSection != null && il.CheckRoute(r) == null);
        if (r != null) il.SetRoute(r);
    }
}

Teacher(new
{
    kind = "t_session", active = true, gameId = "sched", running = true, speed = 10, take = false, clock = 28800,
    student = new { id = "s1", name = "Тестов Тест", group = "Т-1" },
    lesson = new { id = "L", name = "Расписание", passScore = 70, startClock = "08:00", schedule = sched, scheduleLeadMin = 1, trainStepSec = 12 },
});
Check(Training.Active && Schedule.Data?.Id == "uch1" && sim.StepSeconds == 12, "урок с расписанием №1 начался, поезд проходит участок за 12 с");

Step(3 * 60 + 50);   // 08:03:50
Check(sim.Find(2002) == null, "в 08:03:50 поезда №2002 ещё нет (появится за 1 мин до 08:05)");
Step(20);
Check(sim.Find(2002) != null && log.Any(l => l.StartsWith("08:04:0") && l.Contains("№2002") && l.Contains(M.Akzht.CHU.Label)),
      $"в 08:04 №2002 появился у Ч АКЖТ (участок {M.Akzht.CHU.Label})");

Step(5 * 60);    // 08:09
var arr2002 = Schedule.Events.FirstOrDefault(e => e.Number == 2002 && e.Station == SchedPoints.Akzht && e.Kind == "arr");
Check(arr2002 != null && arr2002.Track == "4", $"№2002 прибыл на АКЖТ, путь {arr2002?.Track} (по ТРА 4) в {(arr2002 != null ? SchedTime.FmtSec(arr2002.Clock) : "—")}");

Step(15 * 60);   // 08:24
var dep6002 = Schedule.Events.FirstOrDefault(e => e.Number == 6002 && e.Station == SchedPoints.Akzht && e.Kind == "dep");
var arr6002 = Schedule.Events.FirstOrDefault(e => e.Number == 6002 && e.Station == SchedPoints.Akzht && e.Kind == "arr");
Check(arr6002 != null && dep6002 != null && dep6002.Clock >= SchedTime.Parse("08:18"),
      $"пригородный №6002: прибыл {(arr6002 != null ? SchedTime.FmtSec(arr6002.Clock) : "—")}, ушёл {(dep6002 != null ? SchedTime.FmtSec(dep6002.Clock) : "—")} — не раньше 08:18");
Check(log.Any(l => l.Contains("стоянка по расписанию")), "стоянка по расписанию видна в журнале");
Check(Schedule.Events.Any(e => e.Number == 2002 && e.Station == SchedPoints.Post && e.Kind == "pass"), "№2002 проследовал пост");

// №2001 (оборот на Алгабасе) — Алгабаса нет, поезд появляется на Б12 сам, когда направление перегона «←»
Step(3 * 60);   // 08:27
il.ChangeDirection(M.Post.Line);
Step(60);
Check(M.Post.Line.Current == Dir.Left, "направление перегона пост — Алгабас сменено на «←»");
Step(3 * 60);   // 08:31
Check(sim.Find(2001) != null || Schedule.Events.Any(e => e.Number == 2001), "№2001 от Алгабаса появился на границе (Б12) сам");

Step(30 * 60);  // 09:01
var dep2001 = Schedule.Events.FirstOrDefault(e => e.Number == 2001 && e.Station == SchedPoints.Akzht && e.Kind == "dep");
Check(dep2001 != null, $"№2001 отправился с АКЖТ в {(dep2001 != null ? SchedTime.FmtSec(dep2001.Clock) : "—")}");
var late = Training.Events.FirstOrDefault(e => e.Code == "late");
Check(late != null && late.Critical && late.Text.Contains("6001"), $"опоздание №6001 — критическое событие: {late?.Text}");
Check(Training.Events.Count(e => e.Code == "late") == 1, "других опозданий нет: " + string.Join(" | ", Training.Events.Where(e => e.Code == "late").Select(e => e.Text)));
var t3001 = Schedule.Events.FirstOrDefault(e => e.Number == 3001 && e.Kind == "arr");

// снимок окна «Расписание» и события ГИД — в папку из аргумента (для tools\Stage5Shots)
if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    File.WriteAllText(Path.Combine(args[0], "gid_events.json"), JsonSerializer.Serialize(Schedule.Events));
    var owner = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
    owner.Show();
    StationApp.Ui.ScheduleUi.Open(owner);
    Application.DoEvents();
    var w = Application.OpenForms.Cast<Form>().First(f => f.GetType().Name == "ScheduleWindow");
    w.StartPosition = FormStartPosition.Manual;
    w.Location = new Point(-4000, -4000);
    Application.DoEvents();
    using var bmp = new Bitmap(w.Width, w.Height);
    w.DrawToBitmap(bmp, new Rectangle(Point.Empty, w.Size));
    bmp.Save(Path.Combine(args[0], "station_schedule.png"));
    Console.WriteLine("снимок: station_schedule.png");
}
Check(t3001 != null && t3001.Track == "5", "хозяйственный №3001 прибыл на путь 5 (конечная)");

Console.WriteLine();
Console.WriteLine("События ГИД:");
foreach (var e in Schedule.Events) Console.WriteLine($"  №{e.Number,-5} {e.Station,-8} {e.Kind,-4} {SchedTime.FmtSec(e.Clock)} {e.Track}");

Teacher(new { kind = "t_stop" });
Check(!Training.Active && sim.StepSeconds == 2, "урок закончен, скорость поездов вернулась (2 с)");

Console.WriteLine();
Console.WriteLine(fails == 0 ? "ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"ОШИБОК: {fails}");
if (fails > 0) foreach (var l in log.TakeLast(60)) Console.WriteLine("  " + l);
return fails == 0 ? 0 : 1;
