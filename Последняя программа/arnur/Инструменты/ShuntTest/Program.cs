using System.Reflection;
using System.Text.Json;
using StationApp.Core;
using StationApp.Model;

// =====================================================================
//  [ДОБАВЛЕНО 23.10.2026, этап 7] Тест без окон: натурный лист, расформирование, расцепка и закрепление,
//  ПНП (уведомление, заезд, подача, погрузка / выгрузка, уборка), сцепка, формирование, осмотр ПТО,
//  отправление местных вагонов, восстановительный поезд (ядро АКЖТ + пост).
//  Запуск: dotnet run --project tools\ShuntTest [папка для снимков окон]
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }

var onEvent = typeof(Training).GetMethod("OnEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
void Teacher(object data) => onEvent.Invoke(null, new object[] { Training.TeacherId, JsonSerializer.SerializeToElement(data) });

ModelHost.Start(false);
var il = ModelHost.Model.IL;
var sim = ModelHost.Model.Trains;
var A = ModelHost.Model.Akzht;
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

Teacher(new
{
    kind = "t_session", active = true, gameId = "shunt", running = true, speed = 10, take = false, clock = 28800,
    student = new { id = "s1", name = "Тестов Тест", group = "Т-1" },
    lesson = new { id = "L", name = "Маневры", passScore = 70, startClock = "08:00", trainStepSec = 2,
                   planSupply = 3, planRemove = 3, planLocalDepart = 3, localWagons = 3, pnpMinPerWagon = 1,
                   recoveryArriveMin = 1, recoveryWorkMin = 2 },
});
var pnp = Shunting.Pnps.Single();
Check(Training.Active && pnp.Section == A.P6T, $"урок начался, ПНП «{pnp.Name}» — тупик {pnp.Section.Label}");

// ---------- 1. Грузовой поезд с натурным листом на пути 3 ----------
var toPnp = il.Routes.First(r => r.Shunting && r.Dest == A.P6T && r.Start.Approach == A.P3T);
var tr = sim.Spawn(A.P3T, toPnp.Start.Dir);
Radio.SetInfo(tr, 3401, "", "грузовой", false, false, false, 0);
var local = tr.Consist.Where(w => w.Dest == pnp.Name).ToList();
Check(tr.Consist.Count >= 20 && local.Count == 3 && tr.Consist.Take(3).All(w => w.Dest == pnp.Name),
      $"поезд №3401: {Shunting.Summary(tr.Consist)}; местных на ПНП — 3, в голове");
Check(tr.Consist.All(w => w.Number.Length == 8 && w.Number[0] == Shunting.Kinds.First(k => k.code == w.Kind).digit), "номера вагонов: 8 цифр, первая — род вагона");

// ---------- 2. Расформирование, расцепка, закрепление ----------
Shunting.Disband(tr);
Check(tr.Shunting, "«Расформировать»: поезд стал маневровым составом");
Radio.Stop(tr, tr.Number);
Check(Shunting.Uncouple(tr, 3) == null && tr.Consist.Count == 3, "расцепка: у локомотива 3 местных вагона");
var group = sim.Trains.Single(t => t.NoLoco);
Check(group.Head == A.P3T && group.Consist.Count >= 17, $"на {group.Head?.Label} остались {group.Consist.Count} ваг. без локомотива");
Step(130);
Check(Training.Events.Any(e => e.Code == "secure" && e.Critical), "вагоны не закреплены 2 мин — критическое событие");
int need = Shunting.ShoesNeeded(group.Consist.Count);
foreach (var w in group.Consist.Take(need)) Shunting.ToggleShoe(group, w);
Shunting.Report(group, true);
Check(!Training.Events.Any(e => e.Code == "secure" && !e.Critical), $"закреплено {need} башмаками по норме — доклад верный");

// ---------- 3. Заезд на ПНП, подача ----------
Shunting.Notify(pnp);
Radio.Go(tr, tr.Number);
il.SetRoute(toPnp);
bool entered = StepUntil(() => pnp.Loco == tr, 120);
Check(entered && !Training.Events.Any(e => e.Code == "pnp"), $"маневровый №{tr.Number} заехал на ПНП по маршруту {toPnp.Name} (после уведомления)");
StepUntil(() => tr.Waiting.Length > 0, 60);
Check(Shunting.StartWork(pnp, Shunting.CanSupply(pnp).ToList(), new()) == null && pnp.Working, "задание на маневровую работу: подать 3 местных вагона");
bool worked = StepUntil(() => !pnp.Working, 900);
var ask = messages.LastOrDefault(m => m.Who == "Составитель" && m.IsReport);
Check(worked && pnp.Supplied == 3 && tr.Consist.Count == 0 && pnp.Fronts.Sum(f => f.Wagons.Count) == 3, "вагоны поданы на грузовые фронты, у локомотива вагонов нет");
Check(ask != null && tr.StopOrder, "составитель: «работа закончена, разрешаете выезд?» — локомотив ждёт");

// ---------- 4. Погрузка / выгрузка, уборка ----------
bool done = StepUntil(() => pnp.Fronts.All(f => !f.Busy) && pnp.HasReady, 600);
Check(done && pnp.Processed == 3 && messages.Any(m => m.Who == "ПНП"), "грузовые операции закончены, ПНП сообщил о готовности к уборке");
Check(Shunting.StartWork(pnp, new(), Shunting.CanRemove(pnp).ToList()) == null, "задание: убрать 3 вагона");
StepUntil(() => !pnp.Working, 900);
Check(pnp.Removed == 3 && tr.Consist.Count == 3 && tr.Consist.All(w => w.Done), "вагоны убраны с фронтов в состав");
Du46.Answer(messages.Last(m => m.Who == "Составитель" && m.IsReport), true);
Check(!tr.StopOrder, "ДСП разрешил выезд на станцию");

// ---------- 5. Возврат, сцепка, формирование, осмотр ----------
var back = il.Routes.First(r => r.Shunting && r.Start.Approach == A.P6T && r.Dest == A.P3T);
if (tr.Dir != back.Start.Dir) sim.Reverse(tr);
StepUntil(() => !toPnp.Active, 60);
il.SetRoute(back);
bool near = StepUntil(() => Shunting.GroupNear(tr) != null && tr.Waiting.Length > 0, 200);
Check(near && tr.Waiting.Contains("сцепить"), $"маневровый подъехал к вагонам: «{tr.Waiting}»");
foreach (var w in group.Consist.Where(w => w.Shoe).ToList()) Shunting.ToggleShoe(group, w);
Check(Shunting.Couple(tr) == null && !sim.Trains.Any(t => t.NoLoco) && tr.Consist.Count >= 20, $"сцепка: {Shunting.Summary(tr.Consist)}");
Check(Shunting.Form(tr) == null && !tr.Shunting && tr.FormedHere, "«Формирование»: снова поезд");
Shunting.Inspect(tr);
bool insp = StepUntil(() => tr.Inspected, 400);
Check(insp && messages.Any(m => m.Who == "ПТО"), "осмотрщик ПТО осмотрел состав");

// ---------- 6. Отправление: местные вагоны ушли ----------
var exit = il.Routes.FirstOrDefault(r => !r.Shunting && r.Start.Approach == A.P3T && r.ExitSection != null);
if (exit != null)
{
    StepUntil(() => !back.Active, 60);
    if (tr.Dir != exit.Start.Dir) sim.Reverse(tr);
    var line = il.Lines.FirstOrDefault(l => l.Sections.Contains(exit.ExitSection));
    if (il.CheckRoute(exit) != null && line != null) { il.ChangeDirection(line); Step(20); }
    il.SetRoute(exit);
    bool gone = StepUntil(() => !sim.Trains.Contains(tr) || tr.Leaving, 400);
    StepUntil(() => !sim.Trains.Contains(tr), 100);
    Check(gone && Shunting.LocalDeparted == 3, $"поезд №{tr.Number} отправлен по маршруту {exit.Name}: местных вагонов отправлено {Shunting.LocalDeparted}");
    Check(!Training.Events.Any(e => e.Code == "pto"), "состав был осмотрен — нарушения ПТО нет");
}
else Check(false, "нет маршрута отправления с пути 3");
Check(!Training.Events.Any(e => e.Code == "shoe"), "башмаки изъяты до движения — нарушения нет");

// ---------- 7. Восстановительный поезд ----------
var entry = Schedule.EntryPoints.First();
var site = sim.Peek(entry.at, entry.dir).Section;
var v = sim.Spawn(site, entry.dir);
Radio.SetInfo(v, 2410, "", "грузовой", false, false, false, 0);
Teacher(new { kind = "t_fault_add", fault = new { kind = "tr_derail", obj = "T:2410", cond = "none", condState = "", trainFilter = "none", trainNumber = 0, start = -1 } });
Step(2);
Check(v.Derailed && messages.Any(m => m.Who == "Машинист" && m.Text.Contains("Сход")), $"сход поезда №2410 на {site.Label}: машинист сообщил");
Du46.Notify("ПД", "T:2410");
Du46.Notify("ДНЦ", "T:2410");
StepUntil(() => messages.Any(m => m.Who == "ПД" && m.Text.Contains("восстановительный")), 600);
Check(messages.Any(m => m.Who == "ПД" && m.Text.Contains("восстановительный")), "ПД прибыл: нужен восстановительный поезд");
Check(Recovery.Call(v) == null && Recovery.Calls.Count == 1, "восстановительный поезд вызван");
bool came = StepUntil(() => Recovery.Calls[0].State == "работа", 200);
Check(came && sim.Find(8002)?.Category == "восстановительный", $"восстановительный поезд №8002 прибыл к месту схода ({entry.at.Label})");
bool fixedOk = StepUntil(() => Recovery.Calls[0].State == "готово", 300);
Check(fixedOk && !v.Derailed && Faults.List.Any(f => f.Kind == "tr_derail" && f.State == "fixed"), "работы закончены: сход ликвидирован, неисправность устранена");
Check(messages.Any(m => m.Who == "НВП" && m.IsReport && m.Text.Contains("40 км/ч")), "доклад НВП: поезду — скорость не более 40 км/ч");
Radio.WarnTrain(v, 2410, 40);
Check(v.SpeedLimit == 40, "ДСП выдал предупреждение на поезд №2410: 40 км/ч");

// ---------- 8. Конец урока ----------
Teacher(new { kind = "t_stop" });
foreach (var e in Training.Events) Console.WriteLine($"        событие: −{e.Penalty} [{e.Code}] {e.Text}");
Check(Shunting.PlanPenalty() == 0, $"план выполнен: подано {Shunting.Supplied}, убрано {Shunting.Removed}, отправлено местных {Shunting.LocalDeparted}");
var docs = Shunting.ReportDocs();
Check(docs.Count == 2 && pnp.Log.Any(r => r[2].StartsWith("Заезд")), "отчёт: задание на игру и журнал сообщений ПНП");

// ---------- снимки окон ----------
if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    var asm = typeof(Shunting).Assembly;
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
    var t2 = sim.Spawn(A.P5T, Dir.Left);
    Radio.SetInfo(t2, 3404, "1226-012-0369", "грузовой", false, true, false, 0);
    Shunting.Disband(t2);
    t2.Consist[2].Shoe = true;
    pnp.Fronts[1].Wagons.Add(new Wagon { Number = "41234567", Kind = "пл", Loaded = true, Dest = pnp.Name });
    pnp.Fronts[1].OpUntil = Du46.Now + 600;
    pnp.Fronts[2].Wagons.Add(new Wagon { Number = "61234567", Kind = "пв", Dest = pnp.Name, Done = true });
    Shot(Make("ShuntForm", t2, false), "shunt");
    Shot(Make("ShuntForm", t2, true), "consist");
    Shot(Make("PnpWindow", pnp), "pnp");
    pnp.Loco = t2;
    Shot(Make("PnpTaskForm", pnp), "pnp_task");
    Shot(Make("PnpLogWindow", pnp), "pnp_log");
    Shot(Make("RecoveryForm", v), "recovery");
    Shot(Make("RadioForm", t2, t2.Number), "radio7");
}

Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
return fails;
