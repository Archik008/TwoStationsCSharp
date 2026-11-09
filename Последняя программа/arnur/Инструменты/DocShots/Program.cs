using System.Reflection;
using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

// =====================================================================
//  [ДОБАВЛЕНО 23.10.2026] Снимки окон станции АКЖТ для инструкций (готовая_программа\Инструкции\img):
//  главное окно с поездами, маршрутом, предупреждением, вагонами и ПНП; журналы, бланки, радиосвязь,
//  маневровая работа, ПНП, ДУ-46 — на русском, казахском и английском.
//  Запуск: dotnet run --project tools\DocShots <папка>
//  Окна преподавателя: dotnet run --project tools\Stage5Shots <папка> ru|kk|en all
// =====================================================================

var dir = Path.GetFullPath(args.Length > 0 ? args[0] : "docshots");
Directory.CreateDirectory(dir);
LangUi.Install();
ModelHost.Start(false);
var il = ModelHost.Model.IL;
var sim = ModelHost.Model.Trains;
var A = ModelHost.Model.Akzht;

void Run(double sec)
{
    for (double t = 0; t < sec; t += 0.1) { il.Tick(0.1); sim.Tick(0.1); }
}

// ---- обстановка для картинки ----
var rIn = il.Routes.First(r => !r.Shunting && r.Start.Has("yellow1") && r.Dest == A.P3T);
il.SetRoute(rIn);
Run(4);
var t1 = sim.Spawn(rIn.Start.Approach, rIn.Start.Dir);
Radio.SetInfo(t1, 2002, "6762-076-6762", "грузовой", true, false, false, 0);
var t2 = sim.Spawn(A.P5T, Dir.Left);
Radio.SetInfo(t2, 3401, "1226-012-0369", "грузовой", false, true, false, 0);
Shunting.Disband(t2);
Radio.Stop(t2, 3401);
Shunting.Uncouple(t2, 4);
var grp = sim.Trains.First(t => t.NoLoco);
foreach (var w in grp.Consist.Take(Shunting.ShoesNeeded(grp.Consist.Count))) w.Shoe = true;
Radio.Set(new Restriction { Kind = "warning", Obj = "P:" + A.P1T.Id, SpeedPass = 25, SpeedFreight = 25, Place = "1 км пк 3" });
Run(6);
var pnp = Shunting.Pnps.First();
pnp.Fronts[1].Wagons.Add(new Wagon { Number = "41234567", Kind = "пл", Loaded = true, Dest = pnp.Name });
pnp.Fronts[1].OpUntil = Du46.Now + 900;
pnp.Fronts[2].Wagons.Add(new Wagon { Number = "61234567", Kind = "пв", Dest = pnp.Name, Done = true });
pnp.Notified = true;
Journals.Add("ДУ-2", new() { ["point"] = "АКЖТ", ["no"] = "2001", ["index"] = "6762-076-6762", ["arr"] = "08:05", ["track"] = "1", ["dep"] = "08:12", ["wagons"] = "56" });
Journals.Add("ДУ-47", new() { ["line"] = il.Lines[0].Title, ["time"] = "08:10", ["text"] = "Прошу принять поезд №2004. ДСП ст. АКЖТ" });
Journals.SaveBlank(null, new() { ["kind"] = "ДУ-54", ["train"] = "2002", ["from"] = "3", ["signal"] = "выходного", ["first"] = "2" });
Du46.Write("Стрелка 5/7 не имеет контроля положения", "W:" + il.Switches.Values.First().Id);
Radio.Stop(t1, 2002);
Radio.Go(t1, 2002);

var main = (Form)Activator.CreateInstance(typeof(ModelHost).Assembly.GetType("StationApp.MainForm")!, true)!;
main.StartPosition = FormStartPosition.Manual;
main.Show();
main.WindowState = FormWindowState.Normal;
main.Bounds = new Rectangle(-4000, -4000, 1920, 1040);

var asm = typeof(Journals).Assembly;
Form Make(string type, params object[] a) =>
    (Form)Activator.CreateInstance(asm.GetType("StationApp.Ui." + type)!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, a, null)!;

void Snap(Form f, string name, bool close = true)
{
    if (!f.Visible)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.Show();
    }
    for (int i = 0; i < 25; i++) { Application.DoEvents(); Thread.Sleep(40); }
    using var bmp = new Bitmap(f.Width, f.Height);
    f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
    bmp.Save(Path.Combine(dir, name + ".png"));
    Console.WriteLine("снимок: " + name);
    if (close) f.Close();
}

// [ДОБАВЛЕНО 28.10.2026] dotnet run --project Инструменты\DocShots <папка> ai — окно ЖИ-помощника с вопросами
if (args.Length > 1 && args[1] == "ai")
{
    StationApp.Ai.AiStation.Install();
    var w = (Form)Activator.CreateInstance(typeof(ModelHost).Assembly.GetType("StationApp.Ai.AiChatWindow")!, true)!;
    w.StartPosition = FormStartPosition.Manual;
    w.Location = new Point(-4000, -4000);
    w.Size = new Size(620, 640);
    w.Show();
    var ask = w.GetType().GetMethod("Ask")!;
    for (int i = 0; i < 60; i++) { Application.DoEvents(); Thread.Sleep(50); }
    foreach (var q in new[] { "маршрутты қалай жасайды?", "Н-нан 3 жолға маршрут", "как отменить маршрут", "5/7 стрелкасы қандай" })
    {
        ask.Invoke(w, new object[] { q });
        for (int i = 0; i < 60; i++) { Application.DoEvents(); Thread.Sleep(50); }
    }
    Snap(w, "ai_window", true);
    main.Close();
    ModelHost.Stop();
    return;
}

// [ДОБАВЛЕНО 28.10.2026] dotnet run --project Инструменты\DocShots <папка> modes — главное окно во всех режимах интерфейса
if (args.Length > 1 && args[1] == "modes")
{
    var was = UiSettings.Mode;
    foreach (var m in Enum.GetValues<UiMode>())
    {
        UiSettings.SetMode(m);
        Snap(main, $"mode_{m}", false);
        if (Environment.GetEnvironmentVariable("DUMP") == "1")
        {
            void Dump(Control c, int d) { Console.WriteLine(new string(' ', d * 2) + c.GetType().Name + " " + c.Bounds + " vis=" + c.Visible + " '" + (c.Text.Length > 20 ? c.Text[..20] : c.Text) + "'"); if (d < 4) foreach (Control ch in c.Controls) Dump(ch, d + 1); }
            Dump(main, 0);
        }
    }
    UiSettings.SetMode(was);
    main.Close();
    ModelHost.Stop();
    Console.WriteLine("ГОТОВО");
    return;
}

foreach (var lang in new[] { "ru", "kk", "en" })
{
    L.Set(lang);
    Snap(main, $"station_{lang}", false);
    Snap(Make("JournalWindow", Journals.Def("ДУ-2")), $"du2_{lang}");
    Snap(Make("JournalWindow", Journals.Def("ДУ-47")), $"du47_{lang}");
    Snap(Make("BlanksWindow"), $"blanks_{lang}");
    Snap(Make("BlankForm", Journals.BlankDefs.First(d => d.Code == "ДУ-54"), null, 2002), $"du54_{lang}");
    Snap(Make("RadioForm", t1, 2002), $"radio_{lang}");
    Snap(Make("TrainInfoForm", t1), $"traininfo_{lang}");
    Snap(Make("RestrictionForm", A.P1T, null), $"restriction_{lang}");
    Snap(Make("ShuntForm", t2, false), $"shunt_{lang}");
    Snap(Make("PnpWindow", pnp), $"pnp_{lang}");
    pnp.Loco = t2;
    Snap(Make("PnpTaskForm", pnp), $"pnptask_{lang}");
    pnp.Loco = null;
    Snap(Make("Du46Window"), $"du46_{lang}");
}
L.Set("ru");
main.Close();
ModelHost.Stop();
Console.WriteLine("ГОТОВО");
