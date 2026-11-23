using StationApp.Core;
using StationApp.Model;
using Ed = StationEditor.Model;

namespace StationApp;

// [ДОБАВЛЕНО 18.11.2026] Проверка маршрутов, светофоров и стрелок «Станции по схеме» (см. SchemeCheck.csproj).
static class Program
{
    // то же, что у программы (нужно её окнам, которые подключены в проект)
    public static string StationsDir => AppContext.BaseDirectory;
    public static string AskFile(IWin32Window owner) => null;
    public static void SaveLast(string path) { }

    static int problems;

    static void Bad(string text) { problems++; Console.WriteLine("   !! " + text); }

    [STAThread]
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        bool verbose = args.Contains("-v");
        // снимок окна: --shot файл.station.json снимок.png "маршрут1;маршрут2" [ширина высота]
        int si = Array.IndexOf(args, "--shot");
        if (si >= 0) { Shot(args[si + 1], args[si + 2], args.Length > si + 3 ? args[si + 3] : "", args.Length > si + 5 ? new Size(int.Parse(args[si + 4]), int.Parse(args[si + 5])) : new Size(1600, 950)); return; }
        var files = args.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            foreach (var d in new[] { @"Исходники\Станция по схеме\Станции", @"готовая_программа\Редактор_05_11\Станции", @"готовая_программа\Станция по схеме\Станции" })
            {
                var p = Path.Combine(root, d);
                if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*.station.json"));
            }
            var auto = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationEditor", "autosave.station.json");
            if (File.Exists(auto)) files.Add(auto);
        }
        foreach (var f in files) Check(f, verbose);
        Console.WriteLine($"\nИТОГО замечаний: {problems}");
    }

    static RailModel Build(string f)
    {
        var m = new RailModel(f);
        return m;
    }

    static void Check(string f, bool verbose)
    {
        Console.WriteLine($"\n===== {Path.GetFileName(f)}  ({Path.GetDirectoryName(f)?.Split('\\').LastOrDefault()})");
        RailModel m;
        try { m = Build(f); }
        catch (Exception ex) { Bad("не строится: " + ex.Message); return; }
        var L = m.Layout;
        var il = m.IL;
        Console.WriteLine($"   участков {il.Sections.Count}, стрелок {il.Switches.Count}, светофоров {il.Signals.Count}, маршрутов: поездных {L.TrainRoutes}, маневровых {L.ShuntRoutes}");
        foreach (var n in L.Notes) Console.WriteLine("   · " + n);

        // ---- светофоры ----
        foreach (var s in il.Signals.Values)
        {
            if (s.Kind != SignalKind.Controlled) continue;
            int n = L.View.Routes.Count(r => r.Start == s);
            if (n == 0) Bad($"светофор {s.Label} ({s.Title}) открывается дежурным, а маршрутов от него нет");
            if (s.At == null) Bad($"светофор {s.Label}: клетка без участка");
        }
        // ---- стрелки ----
        foreach (var sw in il.Switches.Values)
        {
            if (sw.Sections.Count == 0) Bad($"стрелка {sw.Label}: нет стрелочного участка — занятость не контролируется, перевод под поездом возможен");
            if (!L.View.Routes.Any(r => r.Switches.Any(x => x.sw == sw))) Console.WriteLine($"   · стрелка {sw.Label} не входит ни в один маршрут");
        }

        // ---- одинаковые маршруты (клик n1 → n2 берёт первый) ----
        foreach (var g in L.View.Routes.GroupBy(r => (r.Start, r.Dest, r.Shunting)).Where(g => g.Count() > 1))
        {
            var first = Ui.RouteFinder.Find(L.View.Routes, g.Key.Start, (object)g.Key.Dest, g.Key.Shunting);
            var best = g.OrderBy(r => r.Switches.Count(x => !x.plus)).ThenBy(r => r.Sections.Count).First();
            string txt = string.Join(" | ", g.Select(r => $"{r.Name}: {Sw(r)}"));
            if (first != best) Bad($"клик {g.Key.Start.Label} → {g.Key.Dest?.Label} ставит «{first.Name}» ({Sw(first)}), а короче/прямее «{best.Name}» ({Sw(best)}). Все: {txt}");
            else if (verbose) Console.WriteLine($"   · варианты: {txt}");
        }

        // ---- каждый маршрут: задать, дождаться, проверить ----
        foreach (var r0 in L.View.Routes.ToList())
        {
            m = Build(f);
            L = m.Layout; il = m.IL;
            var r = L.View.Routes.First(x => x.Id == r0.Id);
            // однопутный перегон: направление — под маршрут (иначе отказ «смените направление» — это не ошибка схемы)
            foreach (var line in il.Lines) if (r.Precondition != null && line.DependentRoutes.Contains(r)) line.Current = r.Start.Dir;
            var logl = new List<string>();
            il.Log += (msg, k) => logl.Add(msg);
            var why = il.CheckRoute(r);
            if (why != null) { Bad($"{Kind(r)} {r.Name}: не задаётся на пустой станции — {why}"); continue; }
            il.SetRoute(r);
            for (int i = 0; i < 300; i++) { il.Tick(0.1); }
            il.ComputeAspects();
            if (!r.Active) { Bad($"{Kind(r)} {r.Name}: после задания не активен. Журнал: {string.Join(" / ", logl)}"); continue; }
            if (r.Start.Aspect is Aspect.Red or Aspect.Dark)
                Bad($"{Kind(r)} {r.Name}: светофор {r.Start.Label} не открылся (показание {r.Start.Aspect}); стрелки: {Sw(r)}");
            else if (verbose) Console.WriteLine($"   ok {Kind(r)} {r.Name}: {r.Start.Label}={r.Start.Aspect}, конец {r.End?.Label ?? "—"}, стрелки {Sw(r)}");
            if (!r.Shunting && r.End == null && r.ExitSection == null)
                Console.WriteLine($"   · {Kind(r)} {r.Name}: нет светофора конца — показание как на закрытый следующий");

            // по стрелкам: куда поедет поезд от светофора
            var walk = new List<TrackSection>();
            var cur = r.Start.At;
            var dir = r.Start.Dir;
            string end = null;
            for (int i = 0; i < 60; i++)
            {
                var st = L.Next(cur, dir);
                if (st == null) { end = "тупик пути (стрелка не так или разрыв)"; break; }
                if (st.Exit) { end = "уход за край"; break; }
                if (st.Buffer) { end = "упор"; break; }
                cur = st.Section; dir = st.Dir;
                walk.Add(cur);
                if (cur == r.Dest || (r.ExitSection != null && cur == r.ExitSection)) { end = "ok"; break; }
            }
            var need = r.Sections.ToHashSet();
            var got = walk.Where(s => s != r.Dest && s != r.ExitSection).ToHashSet();
            if (end != "ok")
                Bad($"{Kind(r)} {r.Name}: по стрелкам поезд не доходит до {r.Dest?.Label ?? r.ExitSection?.Label} — {end}; прошёл: {string.Join(", ", walk.Take(8).Select(s => s.Label))}; участки маршрута: {string.Join(", ", r.Sections.Select(s => s.Label))}; стрелки {Sw(r)}; от {r.Start.At?.Label}");
            else if (!got.SetEquals(need.Where(s => s != r.Dest)))
                Bad($"{Kind(r)} {r.Name}: участки маршрута [{string.Join(", ", need.Select(s => s.Label))}] ≠ путь по стрелкам [{string.Join(", ", walk.Select(s => s.Label))}]");
        }
    }

    static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => All(x).Prepend(x));

    static void Shot(string file, string png, string routes, Size size)   // [23.11.2026] + размер окна
    {
        Application.EnableVisualStyles();
        ModelHost.Start(file);
        var il = ModelHost.Model.IL;
        foreach (var name in routes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (name.Trim().StartsWith("?")) continue;
            var r = il.Routes.FirstOrDefault(x => x.Name == name.Trim());
            if (r == null) { Console.WriteLine("нет маршрута " + name); continue; }
            foreach (var line in il.Lines) if (line.DependentRoutes.Contains(r)) line.Current = r.Start.Dir;
            il.SetRoute(r);
        }
        for (int i = 0; i < 60; i++) il.Tick(0.1);
        var f = new Ui.SchemeStationForm { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(-5000, 0, size.Width, size.Height), ShowInTaskbar = false, WindowState = FormWindowState.Normal };
        f.Show();
        for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(30); }
        // [23.11.2026] «?Н1» в списке маршрутов — нажать светофор начала (видно, что мигает зелёным: фаза мигания — «горит»)
        foreach (var name in routes.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Trim().StartsWith("?")))
        {
            var sig = il.Signals.Values.FirstOrDefault(x => x.Label == name.Trim()[1..]);
            var panel = All(f).OfType<RailSchemePanel>().FirstOrDefault();
            if (sig == null || panel == null) { Console.WriteLine("нет светофора " + name); continue; }
            panel.SelectSignal(sig);
            typeof(ModelHost).GetProperty("BlinkPhase")!.SetValue(null, true);
            panel.Refresh();
        }
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(png);
        f.Close();
        Console.WriteLine("снимок: " + png);
    }

    static string Kind(RailRoute r) => r.Shunting ? "маневровый" : "поездной";
    static string Sw(RailRoute r) => r.Switches.Count == 0 ? "без стрелок" : string.Join(" ", r.Switches.Select(x => x.sw.Label + (x.plus ? "+" : "−")));
}
