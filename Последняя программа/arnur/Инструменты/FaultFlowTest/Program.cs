using System.Reflection;
using System.Text.Json;
using StationApp.Core;
using StationApp.Model;

// =====================================================================
//  [ДОБАВЛЕНО 20.10.2026, этап 3] Тест без окон: ядро Алгабаса + занятие + неисправности.
//  Сообщения преподавателя подаются прямо в Training.OnEvent, время шагает вручную (×10).
//  Запуск: dotnet run --project tools\FaultFlowTest   (печатает OK / ОШИБКА по каждой проверке)
//  Внимание: обнуляет счётчики кнопок в %LOCALAPPDATA%\StationAlgabas\counters.txt (как начало урока).
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }

var onEvent = typeof(Training).GetMethod("OnEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
void Teacher(object data) => onEvent.Invoke(null, new object[] { Training.TeacherId, JsonSerializer.SerializeToElement(data) });

ModelHost.Start();
var il = ModelHost.Model.IL;
var sim = ModelHost.Model.Trains;
int commands = 0;
il.SwitchCommand += _ => commands++;
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

Teacher(new
{
    kind = "t_session", active = true, gameId = "test", running = true, speed = 10, take = false, clock = 28800,
    student = new { id = "s1", name = "Тестов Тест", group = "Т-1" },
    lesson = new { id = "L", name = "Тест", passScore = 70, startClock = "08:00" },
});
Check(Training.Active && Training.Running, "занятие началось, время идёт");

Fault Last() => Faults.List[^1];
void AddFault(string kind, string obj, string cond = "none", string state = "") =>
    Teacher(new { kind = "t_fault_add", fault = new { kind, obj, cond, condState = state, trainFilter = "none", trainNumber = 0, start = -1 } });

// ---------- 1. Потеря контроля стрелки: ДУ-46 → ШН и ДНЦ → доклад → подписать ----------
var sw1 = il.Switches.Values.First();
AddFault("sw_nocontrol", "W:" + sw1.Id);
var f1 = Last();
Step(2);
Check(f1.State == "active" && !sw1.Controlled, $"потеря контроля: стрелка {sw1.Label} без контроля");
Du46.Write($"Стрелка {sw1.Label} не имеет контроля", "W:" + sw1.Id);
Du46.Notify("ШН", "W:" + sw1.Id);
Du46.Notify("ДНЦ", "W:" + sw1.Id);
Check(f1.State == "fixing" && f1.Worker == "ШН", "сообщили ШН — неисправность «устраняется»");
WorkerMessage report = null;
for (int i = 0; i < 120 && report == null; i++) { Step(10); report = messages.FirstOrDefault(m => m.IsReport && !m.Done); }
Check(messages.Any(m => !m.IsReport && m.Who == "ШН"), "ШН прибыл и прислал сообщение о причине");
Check(report != null, $"ШН прислал доклад через {(Training.Clock - f1.ActivatedAt) / 60:0.0} игровых мин");
if (report != null) Du46.Answer(report, true);
Step(3);
Check(f1.State == "fixed" && sw1.Controlled, "доклад подписан — неисправность устранена, стрелка с контролем");

// ---------- 2. Не переводится в «минус»: привод не получает команду, после снятия — получает ----------
var sw2 = il.Switches.Values.Skip(1).First(s => s.Plus && !s.Locked && !s.Sections.Any(x => x.Occupied));
AddFault("sw_nominus", "W:" + sw2.Id);
var f2 = Last();
Step(1);
commands = 0;
il.ToggleSwitch(sw2);
Step(5);
Check(!sw2.Controlled && !sw2.Plus && commands == 0, $"стрелка {sw2.Label} в «минус» не дошла, команда на макет не ушла");
il.ToggleSwitch(sw2);
Step(5);
Check(sw2.Controlled && sw2.Plus && commands == 1, "обратно в «плюс» переводится и получает контроль");
il.ToggleSwitch(sw2);
Step(5);
commands = 0;
Teacher(new { kind = "t_fault_remove", id = f2.Id });
Step(5);
Check(f2.State == "removed" && sw2.Controlled && !sw2.Plus && commands == 1, "преподаватель снял — команда ушла, стрелка дошла в «минус»");
il.ToggleSwitch(sw2);
Step(5);

// ---------- 3. Ложная занятость по условию «переход в занят» не возникает сама; «без условия» — сразу ----------
var sec = il.Sections.Values.First(s => !s.Occupied && il.Switches.Values.Any(w => w.Sections.Contains(s)));
AddFault("sec_occ", "P:" + sec.Id, "to", "занят");
var f3a = Last();
Step(3);
Check(f3a.State == "wait" && !sec.Occupied, $"участок {sec.Label}: «при переходе в занят» — ждёт условия");
sec.ManualOcc = true; Step(2); sec.ManualOcc = false; Step(2);
Check(f3a.State == "active" && sec.Occupied && !sec.ManualOcc, "занятость пришла и ушла — осталась ложная занятость");

// ---------- 4. Кнопка со счётчиком: ВК при ложной занятости ----------
var swOnSec = il.Switches.Values.First(w => w.Sections.Contains(sec));
int presses = Du46.Presses.Count;
bool aux = il.ToggleSwitch(swOnSec, aux: true);
Check(aux && Du46.Presses.Count == presses + 1 && Du46.Counter("ВК") == 1, "вспомогательный перевод: счётчик ВК = 1, нажатие ждёт записи");
Du46.RecordPress(Du46.Presses[^1], "ВК при ложной занятости.", "P:" + sec.Id);
Check(Du46.Unrecorded.Count() == 0 && Du46.Records[^1].Kind == "кнопка", "нажатие записано в ДУ-46");
Step(5);
il.ToggleSwitch(swOnSec, aux: true);
Step(5);
Du46.RecordPress(Du46.Presses[^1], "ВК обратно.", "");
Teacher(new { kind = "t_fault_remove", id = f3a.Id });
Step(2);
Check(!sec.Occupied, "ложная занятость снята преподавателем");

// ---------- 5. Самопроизвольное перекрытие открытого светофора ----------
var route = il.Routes.FirstOrDefault(r => !r.Shunting && il.CheckRoute(r) == null && r.Start.Kind == SignalKind.Controlled);
if (route != null)
{
    il.SetRoute(route);
    Step(40);
    bool open = AspectInfo.IsPermissive(route.Start.Aspect);
    AddFault("sig_drop", "S:" + route.Start.Id);
    var f5 = Last();
    Step(3);
    Check(open && f5.Triggered && !AspectInfo.IsPermissive(route.Start.Aspect) && route.Active,
          $"маршрут {route.Name}: светофор {route.Start.Label} был открыт и перекрылся, маршрут остался");
    Teacher(new { kind = "t_fault_remove", id = f5.Id });
    Step(3);
    Check(AspectInfo.IsPermissive(route.Start.Aspect), "после снятия светофор снова открыт");
    il.CancelRoute(route);
    Step(3);
}
else Check(false, "нет маршрута для проверки перекрытия");

// ---------- 6. Перегон: направление не меняется ----------
var line = il.Lines.FirstOrDefault();
if (line != null)
{
    AddFault("line_nodir", "L:" + line.Id);
    Step(2);
    var before = line.Current;
    bool changed = il.ChangeDirection(line);
    Check(!changed && line.Current == before, $"{line.Title}: смена направления не прошла");
    Teacher(new { kind = "t_fault_remove", id = Last().Id });
    Step(2);
}

// ---------- 7. Поезд: неисправность двигателя ----------
var place = il.Sections.Values.First(s => !s.Occupied && s.LockedBy == null && !il.Switches.Values.Any(w => w.Sections.Contains(s)));
var train = sim.Spawn(place, Dir.Left);
AddFault("tr_engine", "T:" + train.Number);
var f7 = Last();
Step(2);
var head = train.Head;
Step(30);
Check(f7.State == "active" && train.FaultEngine && train.Head == head, $"поезд №{train.Number} стоит (двигатель)");
Check(messages.Any(m => m.Who == "Машинист"), "машинист сообщил по радиосвязи");

// ---------- 8. Конец урока: оценка ----------
Step(200);
Teacher(new { kind = "t_stop" });
foreach (var e in Training.Events) Console.WriteLine($"        событие: −{e.Penalty} {e.Text}");
Check(!Training.Events.Any(e => e.Text.Contains($"стрелка {sw1.Label})")), "за устранённую по правилам стрелку штрафа нет");
Check(!Training.Events.Any(e => e.Text.Contains("Ложная занятость")), "ложная занятость, снятая быстрее 3 игровых минут, не оценивается");
Check(Training.Events.Any(e => e.Code == "notify" && e.Text.Contains("двигателя")), "о неисправности двигателя не сообщено ДНЦ — штраф");
Check(!Training.Events.Any(e => e.Code == "du46_btn"), "нажатия кнопок записаны — штрафа нет");
Check(!Training.Active, $"урок закончен, оценка {Training.Score}");
Console.WriteLine($"\nДУ-46: {Du46.Records.Count} записей");
foreach (var r in Du46.Records) Console.WriteLine($"   {Training.Fmt(r.Clock)} {r.Who,-4} {r.Name,-10} {r.ObjLabel,-14} {r.Text}");
// ---------- снимки окон (аргумент: папка для PNG) ----------
if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    var asm = typeof(Du46).Assembly;
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
    Form Make(string type, params object[] a) => (Form)Activator.CreateInstance(asm.GetType("StationApp.Ui." + type)!, a)!;
    var rep = new WorkerMessage { Who = "ШН", Name = "Петров", Text = "Неисправность стрелки 2 устранена. Стрелка проверена, работает нормально. ШН Петров", IsReport = true };
    Messages(rep);
    Shot(Make("Du46Window"), "du46");
    Shot(Make("WorkerMessageForm", rep), "report");
    Shot(Make("CounterRecordForm", new CounterPress { Label = "ПС Ч", What = "пригласительный сигнал на светофоре Ч", Value = 3 }), "counter");
    // [21.10.2026, этап 6] всё окно Алгабаса 1920×1080 — колонка «Пульт ДСП»
    var station = (Form)Activator.CreateInstance(asm.GetType("StationApp.Algabas.AlgabasForm")!)!;
    station.WindowState = FormWindowState.Normal;
    station.Size = new Size(1920, 1080);
    Shot(station, "algabas_form");
}

static void Messages(WorkerMessage m) => Du46.Messages.Add(m);

Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
return fails;
