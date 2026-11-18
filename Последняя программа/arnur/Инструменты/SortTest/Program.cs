using StationApp.Yard;

// [ДОБАВЛЕНО 12.11.2026] Сценарии роспуска на модели горки: печатает отцепы, скорости сцепления и нарушения.
static class P
{
    static void Run(string title, bool ars, bool gac, Aspect asp, int seed, bool escort = true, double minutes = 25, bool verbose = false)
    {
        var s = new YardSim(seed) { Ars = ars, GacMode = gac };
        if (!gac) foreach (var c in s.ListokTrain.AllCuts) c.Route = 0;
        var log = new List<string>();
        s.Log += (t, bad) => log.Add($"{s.Clock:HH:mm:ss} {(bad ? "!! " : "")}{t}");
        s.SetPushRoute(1);
        s.SetAspect(asp);
        var t0 = s.ListokTrain;
        for (int i = 0; i < minutes * 60 * 20; i++)
        {
            s.Tick(0.05);
            // запрещённые: перед горбом — красный и спуск с локомотивом
            if (escort && s.Pushing == t0 && t0.Cuts.Count > 0 && t0.Cuts.All(c => c.Forbidden) && !t0.Escort && t0.Cuts[0].Head > -25)
            { s.SetAspect(Aspect.Red); if (t0.V == 0) { s.StartEscort(); s.SetAspect(Aspect.Yellow); } }
        }
        Console.WriteLine($"=== {title}: оценка {s.Grade} ({s.Score}), нарушений {s.Violations.Count}, распущено {s.Rolled} ваг. / {s.CutsRolled} отц., на свой путь {s.CorrectCuts}, отправлено {s.Departed}");
        foreach (var v in s.Violations) Console.WriteLine($"   - {v.Text} (−{v.Penalty})");
        if (verbose) foreach (var l in log) Console.WriteLine("     " + l);
        for (int k = 1; k <= 8; k++) Console.Write($" {k}:{s.OnTrack(k).Sum(c => c.Wagons.Count)}");
        Console.WriteLine();
    }

    static void Physics()
    {
        // один вагон без торможения: скорость по точкам
        foreach (var (name, w0, mass) in new[] { ("хороший 1,1", 1.1, 90.0), ("плохой 3,5", 3.5, 24.0) })
        {
            var s = new YardSim(1);
            var c = new Cut { No = 99, Dest = 5, Route = 5, State = CutState.Rolling, Head = 8, V = 1.67 };
            c.Wagons.Add(new Wagon { Kind = Rolling.Covered, Loaded = true, Mass = mass, W0 = w0, Dest = 5 });
            s.Cuts.Add(c);
            var marks = new[] { 30.0, 62, 95, 128, 186, 300, 400, 440, 500 };
            int mi = 0;
            Console.Write($"{name}: ");
            for (int i = 0; i < 20 * 600 && c.State != CutState.Stopped; i++)
            {
                s.Tick(0.05);
                while (mi < marks.Length && c.Head >= marks[mi]) Console.Write($"s{marks[mi++]:0}={c.V * 3.6:0.0} ");
            }
            Console.WriteLine($"стоп s={c.Head:0}");
        }
    }

    static void Main(string[] a)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (a.Contains("-L")) { for (int sd = 1; sd <= 6; sd++) { Long.Run(true, sd, 60); Long.Run(false, sd + 10, 60); } return; }
        if (a.Contains("-l")) { Long.Run(true, 3, 90); Long.Run(false, 4, 90); return; }
        Physics(); Dbg.Ars(1.1, 90, 1); if (a.Contains("-d")) return;
        Run("АРС, ГАЦ, зелёный", true, true, Aspect.Green, 7, verbose: a.Contains("-v"));
        Run("АРС, ГАЦ, жёлтый", true, true, Aspect.Yellow, 7);
        Run("без торможения, ГАЦ, зелёный", false, true, Aspect.Green, 7);
        Run("АРС, ручной без маршрутов", true, false, Aspect.Yellow, 7);
        Run("АРС, ГАЦ, без спуска запрещённых", true, true, Aspect.Yellow, 7, escort: false);
        for (int sd = 1; sd <= 5; sd++) Run($"АРС, ГАЦ, Ж+З, seed {sd}", true, true, Aspect.YellowGreen, sd * 13);
    }
}
