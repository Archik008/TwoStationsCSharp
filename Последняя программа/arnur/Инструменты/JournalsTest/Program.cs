using System.Reflection;
using System.Text.Json;
using StationApp.Core;
using StationApp.Model;

// =====================================================================
//  [ДОБАВЛЕНО 23.10.2026, этап 4] Тест без окон: поезд как объект, радиосвязь, бланки, журналы, предупреждения
//  и «окна», номер поезда на макете, автопроверка журналов (ядро АКЖТ + пост).
//  Запуск: dotnet run --project tools\JournalsTest [папка для снимков окон]
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }

var onEvent = typeof(Training).GetMethod("OnEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
void Teacher(object data) => onEvent.Invoke(null, new object[] { Training.TeacherId, JsonSerializer.SerializeToElement(data) });

ModelHost.Start(false);
var il = ModelHost.Model.IL;
var sim = ModelHost.Model.Trains;
var messages = new List<WorkerMessage>();
Du46.MessageArrived += m => messages.Add(m);

void Step(double gameSeconds)
{
    for (double t = 0; t < gameSeconds; t += 0.1 * Training.Speed)
    {
        double mdt = Training.ModelDt(0.1);
        il.Tick(mdt);
        sim.Tick(mdt);
        Training.Tick(0.1);
    }
}
bool StepUntil(Func<bool> cond, double max)
{
    for (double t = 0; t < max && !cond(); t += 2) Step(2);
    return cond();
}
string Hm(double clock) => Training.Fmt(clock)[..5];

Teacher(new
{
    kind = "t_session", active = true, gameId = "journals", running = true, speed = 10, take = false, clock = 28800,
    student = new { id = "s1", name = "Тестов Тест", group = "Т-1" },
    lesson = new { id = "L", name = "Журналы", passScore = 70, startClock = "08:00", trainStepSec = 2 },
});
Check(Training.Active, "урок начался");

bool Free(RailRoute r) => il.CheckRoute(r) == null;

// ---------- 1. Поезд как объект ----------
var rIn = il.Routes.First(r => !r.Shunting && r.Start.Has("yellow1") && r.Start.Approach != null && r.Dest != null && Free(r) &&
                               !il.Lines.Any(l => l.Sections.Contains(r.Dest)) &&
                               il.Routes.Any(o => !o.Shunting && o.ExitSection != null && o.Start.Approach == r.Dest && o.Start.Dir == r.Start.Dir));
var A = sim.Spawn(rIn.Start.Approach, rIn.Start.Dir);
int numA = rIn.Start.Dir == Dir.Right ? 2002 : 2001;
Check(Radio.SetInfo(A, numA, "6762-076-6762", "грузовой", true, false, false, 57) == null && A.Number == numA && A.Long && A.Wagons == A.Consist.Count && A.Wagons > 0,
      $"«Номер и индекс»: поезд №{A.Number}, индекс {A.Index}, длинносоставный, {A.Wagons} ваг. (по натурному листу)");
Check(Radio.WrongParity(numA + 1, A.Dir), "номер не по направлению распознаётся");
var B0 = sim.Spawn(il.Sections.Values.First(s => !s.Occupied && s.LockedBy == null && !rIn.Sections.Contains(s) && s != rIn.Start.Approach), Dir.Left);
Check(Radio.SetInfo(B0, numA, "", "грузовой", false, false, false, 0) != null, "номер, который уже есть на станции, не присваивается");
sim.Trains.Remove(B0); foreach (var c in B0.Cells) c.TrainCount--;

// ---------- 2. ДУ-47: согласие соседа ----------
var line = il.Lines.First();
Journals.Add("ДУ-47", new() { ["line"] = line.Title, ["time"] = Hm(Training.Clock), ["text"] = $"Прошу принять поезд №{numA}. ДСП ст. АКЖТ Тестов" });
Step(70);
Check(Journals.Entries["ДУ-47"].Count == 2 && Journals.Entries["ДУ-47"][1].Source == "входящая", $"ДУ-47: сосед ответил — «{Journals.Entries["ДУ-47"].Last()["text"]}»");
Check(Journals.Consent(numA) == !line.Sections.Any(s => s.Occupied), $"согласие на поезд №{numA} получено (перегон свободен)");

// ---------- 3. Приём по маршруту, факт прибытия ----------
il.SetRoute(rIn);
bool arrived = StepUntil(() => A.Head == rIn.Dest && A.Waiting.Length > 0, 300);
Step(10);
var arr = Schedule.Events.FirstOrDefault(e => e.Number == numA && e.Kind == "arr");
Check(arrived && arr != null, $"поезд №{numA} принят на {rIn.Dest.Label}, прибытие отмечено без расписания ({(arr != null ? Hm(arr.Clock) + ", путь " + arr.Track : "нет")})");

// ---------- 4. Радиосвязь: остановка, продолжить, осаживание ----------
Radio.Stop(A, numA);
Check(A.StopOrder && Radio.Talk(numA).Count == 2, "«Остановка»: машинист ответил, поезд стоит");
Radio.Go(A, numA);
Check(!A.StopOrder, "«Продолжить движение»");
var headBefore = A.Head;
Radio.BackUp(A, numA);
bool backed = StepUntil(() => A.StopOrder, 60);
Console.WriteLine($"        маршрут {rIn.Name}: {string.Join(",", rIn.Sections.Select(s => s.Label))}; события: {string.Join("; ", Schedule.Events.Select(e => $"{e.Number} {e.Station} {e.Kind} {Training.Fmt(e.Clock)}"))}");
Check(backed && A.Head != headBefore && A.Dir == rIn.Start.Dir, $"«Осаживание»: состав осажен на {A.Head?.Label} и стоит, направление прежнее");
Radio.Go(A, numA);
StepUntil(() => A.Head == rIn.Dest && A.Waiting.Length > 0, 120);

// ---------- 5. Предупреждение на участок: медленнее, ДУ-61 ----------
var warnSec = rIn.Dest;
Radio.Set(new Restriction { Kind = "warning", Obj = "P:" + warnSec.Id, SpeedPass = 20, SpeedFreight = 20 });
Step(1);
Check(warnSec.Warning && Math.Abs(sim.SpeedFactor(A) - 3) < 0.01, $"предупреждение 20 км/ч на {warnSec.Label}: поезд едет в 3 раза медленнее");
Journals.Add("ДУ-60", new() { ["got"] = "заявка ПД Кононов", ["start"] = Hm(Training.Clock), ["place"] = $"путь {warnSec.Label}", ["speed"] = "20", ["char"] = "ремонт пути" });

// ---------- 6. Бланк ДУ-64: проследовать запрещающий выходной ----------
var rOut = il.Routes.First(o => !o.Shunting && o.ExitSection != null && o.Start.Approach == rIn.Dest && o.Start.Dir == rIn.Start.Dir);
// выходной не открывается (неисправность), маршрут задан — отправление по бланку
Teacher(new { kind = "t_fault_add", fault = new { kind = "sig_noopen", obj = "S:" + rOut.Start.Id, cond = "none", condState = "", trainFilter = "none", trainNumber = 0, start = -1 } });
var outLine = il.Lines.FirstOrDefault(l => l.Sections.Contains(rOut.ExitSection));
if (il.CheckRoute(rOut) != null && outLine != null) { il.ChangeDirection(outLine); Step(20); }
il.SetRoute(rOut);
Step(10);
Check(rOut.Active && !AspectInfo.IsPermissive(rOut.Start.Aspect), $"маршрут {rOut.Name} задан, выходной {rOut.Start.Label} не открывается, поезд стоит у него");
var b64 = Journals.SaveBlank(null, new() { ["kind"] = "ДУ-64", ["train"] = numA.ToString(), ["loco"] = "0123", ["line"] = line.Title, ["purpose"] = "проверка" });
Check(Journals.Hand(b64, A, numA) == null && A.PermitExit == "ДУ-64", $"бланк {b64.Title} вручён машинисту поезда №{numA}");
Check(Journals.Hand(b64, A, numA) != null, "второй раз тот же бланк не вручается");
bool left = StepUntil(() => !A.Cells.Contains(rIn.Dest) || A.Leaving, 200);
Console.WriteLine($"        поезд №{numA}: {string.Join(",", A.Cells.Select(c => c.Label))}, {A.Dir}, «{A.Waiting}», permit «{A.PermitExit}», leaving {A.Leaving}");
Check(left && A.PermitExit == "", $"поезд №{numA} проследовал запрещающий {rOut.Start.Label} по бланку ДУ-64");
StepUntil(() => Schedule.Events.Any(e => e.Number == numA && e.Kind == "dep"), 200);
var dep = Schedule.Events.FirstOrDefault(e => e.Number == numA && e.Kind == "dep");
Console.WriteLine($"        {Training.ClockText} события: {string.Join("; ", Schedule.Events.Select(e => $"{e.Number} {e.Station} {e.Kind} {Training.Fmt(e.Clock)} {e.Track}"))}; поезд: {string.Join(",", A.Cells.Select(c => c.Label))} «{A.Waiting}» {A.Leaving}");
Check(dep != null, $"отправление отмечено ({(dep != null ? Hm(dep.Clock) : "нет")})");
Journals.Add("ДУ-2", new()
{
    ["point"] = "АКЖТ", ["no"] = numA.ToString(), ["arr"] = arr != null ? Hm(arr.Clock) : "", ["track"] = arr?.Track ?? "",
    ["dep"] = dep != null ? Hm(dep.Clock) : "", ["note"] = "ПР",
});

// ---------- 7. «Указание» по радиосвязи: входной не открывается, маршрут задан — приём по приказу ----------
StepUntil(() => !sim.Trains.Contains(A) || A.Leaving || !rIn.Sections.Any(s => s.Occupied), 300);
StepUntil(() => !rIn.Active && !rOut.Active, 120);
Teacher(new { kind = "t_fault_add", fault = new { kind = "sig_noopen", obj = "S:" + rIn.Start.Id, cond = "none", condState = "", trainFilter = "none", trainNumber = 0, start = -1 } });
il.SetRoute(rIn);
var B = sim.Spawn(rIn.Start.Approach, rIn.Start.Dir);
int numB = B.Number;
bool waits = StepUntil(() => B.Waiting.Contains(rIn.Start.Label), 60);
Check(waits && rIn.Active, $"маршрут {rIn.Name} задан, входной не открывается — поезд №{numB} стоит у {rIn.Start.Label}");
Radio.Order(B, numB, rIn.Start.Label, 7, $"Разрешаю поезду №{numB} проследовать запрещающий сигнал светофора {rIn.Start.Label}");
Check(Journals.Entries["ДУ-58"].Any(e => e["no"] == "7" && e.Source.Length > 0), "регистрируемый приказ №7 записан в ДУ-58 сам");
bool passed = StepUntil(() => B.Head != rIn.Start.Approach, 60);
Check(passed && B.PermitOnce == "", $"поезд №{numB} проследовал запрещающий {rIn.Start.Label} по приказу");
bool bArrived = StepUntil(() => Schedule.Events.Any(e => e.Number == numB && e.Kind == "arr"), 200);
Check(bArrived, $"поезд №{numB} принят на {B.Head?.Label} по маршруту");
Check(!Training.Events.Any(e => e.Code == "permit_route"), "маршрут был задан — нарушения «стрелки не замкнуты» нет");
Check(Training.Events.Any(e => e.Code == "du61" && e.Text.Contains($"№{numB} ")), $"поезд №{numB} вступил под предупреждение без ДУ-61 — ошибка");

// ---------- 8. «Окно» на перегоне: маршрут не задаётся, приказы ДНЦ ----------
var win = Radio.Set(new Restriction { Kind = "window", Obj = "L:" + line.Id, Service = "пути", Place = "1–2 км" });
Step(1);
var rLine = il.Routes.FirstOrDefault(o => !o.Shunting && o.ExitSection != null && line.Sections.Contains(o.ExitSection));
Check(line.Sections.All(s => s.Window) && (rLine == null || il.CheckRoute(rLine)?.Contains("окно") == true), $"«окно» на перегоне «{line.Title}»: маршрут на перегон не задаётся");
var order = messages.LastOrDefault(m => m.Who == "ДНЦ");
Check(order != null && order.Text.Contains($"Приказ №{win.OrderNo}") && !Du46.Records.Any(r => r.Text.Contains("Приказ №")), $"ДНЦ прислал приказ №{win.OrderNo} (в ДУ-46 не пишется)");
Journals.Add("ДУ-58", new() { ["no"] = win.OrderNo.ToString(), ["text"] = order?.Text ?? "" });
Radio.Cancel(win);
Step(1);
Check(!line.Sections.Any(s => s.Window) && win.CancelOrderNo > 0, $"«окно» отменено, ДНЦ прислал приказ №{win.CancelOrderNo} об открытии");

// ---------- 9. Поезд на макете: номер переходит по участкам ----------
var s1 = il.Sections.Values.First(s => !s.Occupied && sim.Peek(s, Dir.Right)?.Section is { Occupied: false } n && !n.Window);
var s2 = sim.Peek(s1, Dir.Right).Section;
s1.HwOcc = true; Step(1);
Radio.SetHwNumber(s1, 3001);
s2.HwOcc = true; Step(1);
s1.HwOcc = false; Step(1);
Check(s1.HwTrainNo == 0 && s2.HwTrainNo == 3001, $"поезд на макете №3001 перешёл с {s1.Label} на {s2.Label}");
Radio.Stop(null, 3001);
Check(Radio.Talk(3001).Count == 2, "радиосвязь с поездом на макете — только запись");
s2.HwOcc = false; Step(1);

// ---------- 10. Исправление записи ----------
var e2 = Journals.Entries["ДУ-2"][0];
Journals.Update("ДУ-2", e2, new() { ["index"] = "6762-076-6762" });
Check(e2.Edits.Count == 1 && e2["index"] == "6762-076-6762", "исправление записи ДУ-2 помечено временем");

// ---------- 11. Конец урока: автопроверка ----------
Teacher(new { kind = "t_stop" });
foreach (var c in Journals.Checks) Console.WriteLine($"        проверка: {c[0]} | {c[1]} | {c[2]}");
foreach (var e in Training.Events) Console.WriteLine($"        событие: −{e.Penalty} [{e.Code}] {e.Text}");
Check(!Training.Events.Any(e => e.Code == "du2" && e.Text.Contains($"№{numA} ")), $"ДУ-2: запись о поезде №{numA} верна");
Check(Training.Events.Any(e => e.Code == "du2" && e.Text.Contains($"№{numB} ")), $"ДУ-2: поезд №{numB} не записан — ошибка");
Check(!Training.Events.Any(e => e.Code == "du58" && e.Text.Contains($"№{win.OrderNo} ")), "приказ о закрытии записан в ДУ-58");
Check(Training.Events.Any(e => e.Code == "du58" && e.Text.Contains($"№{win.CancelOrderNo} ")), "приказ об открытии не записан — ошибка");
Check(!Training.Events.Any(e => e.Code == "du60"), "предупреждение записано в ДУ-60");
var docs = Journals.ReportDocs();
string json = JsonSerializer.Serialize(docs);
Check(docs.Count == 8 && json.Length > 500, $"отчёт: {docs.Count} таблиц, {json.Length / 1024.0:0.0} КБ");

// ---------- снимки окон (аргумент: папка для PNG) ----------
if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    var asm = typeof(Journals).Assembly;
    void Shot(Form f, string name)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.Show();
        Application.DoEvents();
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
        bmp.Save(Path.Combine(args[0], name + ".png"));
        f.Close();
        Console.WriteLine("снимок: " + name);
    }
    Form Make(string type, params object[] a) => (Form)Activator.CreateInstance(asm.GetType("StationApp.Ui." + type)!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, a, null)!;
    foreach (var d in Journals.Defs) Shot(Make("JournalWindow", d), "journal_" + d.Code);
    Shot(Make("BlanksWindow"), "blanks");
    foreach (var d in Journals.BlankDefs) Shot(Make("BlankForm", d, null, 2002), "blank_" + d.Code);
    var tr = sim.Spawn(il.Sections.Values.First(s => !s.Occupied), Dir.Right);
    Radio.SetInfo(tr, 2104, "6762-076-6762", "грузовой", true, true, false, 64);
    Radio.Stop(tr, 2104);
    Shot(Make("RadioForm", tr, 2104), "radio");
    Shot(Make("TrainInfoForm", tr), "train_info");
    Shot(Make("RestrictionForm", il.Sections.Values.First(s => line.Sections.Contains(s)), line), "restriction");
    Shot(Make("OrderForm", 2104, new List<string> { "Ч", "Н" }, "Ч", 8), "order");
    Shot(Make("RestrictionsWindow"), "restrictions");
}

Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
return fails;
