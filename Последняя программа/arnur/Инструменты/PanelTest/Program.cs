using System.Reflection;
using System.Text.Json;
using StationApp.Core;
using StationApp.Model;

// =====================================================================
//  [ДОБАВЛЕНО 21.10.2026, этап 6] Тест без окон: функции пульта ДСП на ядре АКЖТ + пост.
//  Е1 отмена с выдержкой, Е2 ИР, Е4 замыкание стрелок, Е5 колпачки, Е6 курбель и замок, Е7 выключение и макет,
//  Е8 автодействие, Е9 ключ-жезл, Е10 переезд, Е11 взрез, Е14 раздельное управление.
//  Запуск: dotnet run --project tools\PanelTest  (обнуляет счётчики в %LOCALAPPDATA%\StationAKZHT\counters.txt)
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }

var onEvent = typeof(Training).GetMethod("OnEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
void Teacher(object data) => onEvent.Invoke(null, new object[] { Training.TeacherId, JsonSerializer.SerializeToElement(data) });

ModelHost.Start(false);
var M = ModelHost.Model;
var il = M.IL;
var sim = M.Trains;
int commands = 0;
il.SwitchCommand += _ => commands++;
var messages = new List<WorkerMessage>();
Du46.MessageArrived += m => messages.Add(m);
var log = new List<string>();
il.Log += (m, _) => log.Add(m);

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

Teacher(new
{
    kind = "t_session", active = true, gameId = "panel", running = true, speed = 10, take = false, clock = 28800,
    student = new { id = "s1", name = "Тестов Тест", group = "Т-1" },
    lesson = new { id = "L", name = "Пульт", passScore = 70, startClock = "08:00", cancelFreeSec = 6, cancelTrainSec = 180,
                   cancelShuntSec = 60, irSec = 120, crankSec = 40, separateControl = false },
});
Check(Training.Active && il.ArtificialReleaseSeconds == 180 && il.IrSeconds == 120 && il.CrankSeconds == 40, "урок начался, выдержки взяты из урока (ОП 180 с, ИР 120 с, курбель 40 с)");

bool Free(RailRoute r) => il.CheckRoute(r) == null;
RailRoute Open(Func<RailRoute, bool> pick)
{
    var r = il.Routes.FirstOrDefault(x => pick(x) && Free(x));
    if (r == null) return null;
    il.SetRoute(r);
    Step(5);
    return r;
}

// ---------- Е1 отмена с выдержкой ----------
var r1 = Open(r => !r.Shunting && r.Start.Approach != null && r.Start.Kind == SignalKind.Controlled);
Check(r1 != null && il.CanOpen(r1), $"маршрут {r1?.Name} задан, светофор открыт");
il.CancelRoute(r1);
Check(r1.Releasing && r1.CancelKind == "ОС", "отмена при свободном приближении — выдержка ОС");
Step(8);
Check(!r1.Active, "через 6 с маршрут разомкнут");
il.SetRoute(r1);
Step(5);
r1.Start.Approach.ManualOcc = true;
il.CancelRoute(r1);
Check(r1.Releasing && r1.CancelKind == "ОП" && r1.ArtificialTimer > 170, "приближение занято — выдержка ОП 3 мин");
Step(100);
Check(r1.Active, "через 100 с маршрут ещё замкнут");
Step(90);
Check(!r1.Active && Training.Events.Any(e => e.Code == "cancel"), "через 3 мин разомкнут, отмена перед поездом — нарушение");
r1.Start.Approach.ManualOcc = false;

// ---------- Е2 ИР ----------
var r2 = Open(r => !r.Shunting && r.Sections.Count >= 2 && r.Start.Kind == SignalKind.Controlled);
r2.Sections[0].ManualOcc = true; Step(2); r2.Sections[0].ManualOcc = false; Step(2);
var cand = il.IrCandidates(il.Sections.Values);
Check(r2.TrainEntered && cand.Contains(r2.Sections[1]), $"маршрут {r2.Name}: «поезд» прошёл 1-ю секцию, {r2.Sections[1].Label} осталась замкнутой — кандидат ИР");
int ir0 = Du46.Counter("ИР");
il.StartIr(cand.Where(r2.Sections.Contains).ToList());
Check(Du46.Counter("ИР") == ir0 + 1, "кнопка ИР: счётчик +1");
Step(60);
Check(r2.Active, "через 60 с секции ещё замкнуты");
Step(70);
Check(!r2.Active, "через 120 с маршрут разделан");

// ---------- Е4 замыкание стрелок ----------
var even = M.Akzht.View.Switches.Where(s => Interlocking.SwitchNumber(s) % 2 == 0).ToList();
il.RegisterLockGroup("Ст. АКЖТ:ЧЗС", "ЧЗС", even);
il.SetGroupLock("Ст. АКЖТ:ЧЗС", true);
var swE = even.First(s => !s.Locked);
Check(!il.ToggleSwitch(swE) && Du46.Counter("Ст. АКЖТ:ЧЗС") == 1, $"ЧЗС нажата: стрелка {swE.Label} не переводится, счётчик 1");
il.SetGroupLock("Ст. АКЖТ:ЧЗС", false);
Check(il.ToggleSwitch(swE) && Du46.Counter("Ст. АКЖТ:ЧЗС") == 2, "ЧЗС отжата (ПКМ): переводится, счётчик 2");
Step(3); il.ToggleSwitch(swE); Step(3);

// ---------- Е5 колпачок ----------
var rc = il.Routes.First(r => !r.Shunting && r.Switches.Any(x => !x.plus) && Free(r));
var swC = rc.Switches.First(x => !x.plus).sw;
il.SetRedCap(swC, true);
Check(!il.ToggleSwitch(swC) && il.CheckRoute(rc)?.Contains("колпачок") == true, $"колпачок на {swC.Label}: не переводится, маршрут {rc.Name} не задаётся");
il.SetRedCap(swC, false);

// ---------- Е6 курбель, Е7 выключение и макет ----------
var sw6 = il.Switches.Values.First(s => !s.Locked && s.Plus && s.Controlled && !s.Sections.Any(x => x.Occupied));
int msg0 = messages.Count;
il.SetFlap(sw6, true);
Step(25);
Check(sw6.FlapDown && !sw6.Controlled && messages.Count > msg0, $"стрелка {sw6.Label}: заслонка опущена (сообщение работника), контроля нет");
Check(!il.ToggleSwitch(sw6), "с пульта не переводится");
commands = 0;
il.CrankTo(sw6, false);
Step(45);
Check(!sw6.Plus && commands == 1, "переведена курбелем в минус, привод макета получил команду");
il.SetClamp(sw6, "замок");
Step(45);
Check(sw6.Clamp == "замок" && !il.SetFlap(sw6, false), "заперта на закладку и замок; поднять заслонку нельзя, пока заперта");
il.RequestExclude(sw6);
var rep = messages.LastOrDefault(m => m.IsReport && !m.Done);
Check(rep?.Who == "ШН", "доклад ШН о выключении из централизации");
Du46.Answer(rep, true);
Step(2);
Check(sw6.Excluded && !sw6.Controlled, "подписан — стрелка выключена, контроля нет");
il.SetMaket(sw6, true);
Step(2);
Check(sw6.Controlled && sw6.ShownPlus && !sw6.Plus, "на макете «плюс»: на пульте плюс с контролем, реально — минус (опасно, ДСП проверяет «Информацией»)");
Check(il.SwitchInfo(sw6).Contains("Реально: − (минус)"), "«Информация» показывает реальное положение");
il.SetMaket(sw6, null); il.SetClamp(sw6, ""); Step(45);
Check(il.Include(sw6), "макет и замок сняты — стрелка включена в централизацию");
il.CrankTo(sw6, true); Step(45);
il.SetFlap(sw6, false); Step(25);
Check(sw6.Controlled && sw6.Plus && !sw6.FlapDown, "курбелем обратно в плюс, заслонка поднята — контроль есть");

// ---------- Е10 переезд ----------
var cr = M.Post.Crossing;
il.SetCrossingNotice(cr, true);
Step(2);
Check(cr.Closing && !cr.Closed, "извещение на переезд: закрывается (ЗП мигает)");
Step(12);
Check(cr.Closed, "через 10 с переезд закрыт");
il.SetCrossingNotice(cr, false);
Step(1);
Check(!cr.Closed, "отмена извещения — переезд открыт");

// ---------- Е9 ключ-жезл ----------
var line = M.Post.Line;
il.KeyTake(line);
Check(line.KeyOut && !il.ChangeDirection(line), "ключ-жезл изъят — смена направления запрещена");
var place = M.Akzht.View.TrainPlaces.Select(p => p.section).First(s => !s.Occupied);
var loco = sim.Spawn(place, Dir.Right);
Check(il.KeyGive(line, loco) && loco.KeyLine == line && line.KeyTrain == loco.Number, $"ключ-жезл выдан машинисту №{loco.Number}");
Check(!il.KeyPut(line), "пока ключ у машиниста — в пульт не вложить");
il.KeyBack(line, sim);
Check(il.KeyPut(line) && !line.KeyOut, "забран у машиниста и вложен в пульт");
sim.RemoveAll();

// ---------- Е11 взрез ----------
var swX = il.Switches.Values.First(s => !s.Locked && s.Controlled);
var t = sim.Spawn(il.Sections.Values.First(s => !s.Occupied && s.LockedBy == null), Dir.Left);
il.CutSwitch(swX, t);
Step(2);
var cutFault = Faults.List.LastOrDefault(f => f.Kind == "sw_cut");
Check(swX.Cut && il.CutLamp && il.Bell && !swX.Controlled, $"взрез стрелки {swX.Label}: лампа, звонок, нет контроля");
Check(Training.Events.Any(e => e.Code == "cut" && e.Critical) && cutFault?.State == "active", "взрез — критическое событие и неисправность для ШН");
il.BellOff();
Check(!il.Bell && il.CutLamp, "звонок отключён, лампа горит");
Du46.Write($"Взрез стрелки {swX.Label}", "W:" + swX.Id);
Du46.Notify("ШН", "W:" + swX.Id);
Du46.Notify("ДНЦ", "W:" + swX.Id);
WorkerMessage cutRep = null;
for (int i = 0; i < 120 && cutRep == null; i++) { Step(10); cutRep = messages.FirstOrDefault(m => m.IsReport && !m.Done && m.Obj == "W:" + swX.Id); }
if (cutRep != null) Du46.Answer(cutRep, true);
Step(5);
Check(!swX.Cut && swX.Controlled && cutFault.State == "fixed", "ШН восстановил стрелку после взреза, доклад подписан");
sim.RemoveAll();

// ---------- Е14 раздельное управление ----------
il.SeparateControl = true;
var rs = il.Routes.First(r => !r.Shunting && r.Switches.Any(x => x.sw.Plus != x.plus) && il.CheckRoute(r) is { } w && w.Contains("раздельное"));
Check(rs != null, $"раздельное управление: маршрут {rs.Name} не задаётся, пока стрелки не по маршруту");
foreach (var (sw, plus) in rs.Switches.Where(x => x.sw.Plus != x.plus)) il.ToggleSwitch(sw);
Step(5);
Check(Free(rs), "стрелки переведены вручную — маршрут задаётся");
il.SeparateControl = false;

// ---------- Е8 автодействие ----------
var ra = il.Routes.FirstOrDefault(r => !r.Shunting && !r.Diverging && r.ExitSection != null && r.Start.Approach is { Occupied: false } && Free(r));
if (ra != null)
{
    il.RegisterAutoGroup("АД-тест", "АДЧ", new[] { ra }, ra.Start.Dir);
    il.SetRoute(ra);
    il.SetAutoAction("АД-тест", true);
    Check(ra.Auto && Du46.Counter("АД-тест") == 1, $"автодействие на {ra.Name} (пломба — счётчик)");
    var tr = sim.Spawn(ra.Start.Approach, ra.Start.Dir);
    bool passed = false;
    il.TrainPassed += r => { if (r == ra) passed = true; };
    for (int i = 0; i < 60 && !passed; i++) Step(10);
    Step(3);
    Check(passed && ra.Active, "поезд проследовал — маршрут задался сам снова");
    il.SetAutoAction("АД-тест", false);
    il.CancelRoute(ra);
    Step(10);
    sim.RemoveAll();
}
else Check(false, "нет маршрута для автодействия");

Teacher(new { kind = "t_stop" });
Check(!Training.Active && il.ArtificialReleaseSeconds == 15, "урок закончен, выдержки пульта вернулись");
foreach (var e in Training.Events) Console.WriteLine($"        событие: −{e.Penalty} {e.Text}");

// ---------- снимки панелей пульта (аргумент: папка для PNG) ----------
if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    var paneType = typeof(Interlocking).Assembly.GetType("StationApp.Ui.StationPane")!;
    foreach (var (view, name, secLeft, showLog) in new[] { (M.Akzht.View, "pane_akzht", true, true), (M.Post.View, "pane_post", false, false) })
    {
        var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), Size = new Size(1920, 1080) };
        var pane = (Control)Activator.CreateInstance(paneType, view, secLeft, showLog)!;
        pane.Dock = DockStyle.Fill;
        form.Controls.Add(pane);
        form.Show();
        Application.DoEvents();
        ModelHost.MarkDirty();
        il.NotifyChanged();
        Application.DoEvents();
        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
        bmp.Save(Path.Combine(args[0], name + ".png"));
        Console.WriteLine("снимок: " + name);
        form.Close();
    }
}
Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
return fails;
