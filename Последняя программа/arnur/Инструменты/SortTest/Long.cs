using StationApp.Yard;
static class Long
{
    /// <summary>Журнал смены (авто, seed 1): что и когда происходило.</summary>
    public static void Log(double minutes)
    {
        var s = new YardSim(1) { Ars = true, AutoOps = true };
        s.Log += (t, bad) => Console.WriteLine($"{s.Clock:HH:mm:ss} {(bad ? "!! " : "")}{t}");
        for (int i = 0; i < minutes * 60 * 20; i++)
        {
            s.Tick(0.05);
            if (s.Pushing == null)
            {
                var r = s.Arr.Skip(1).FirstOrDefault(a => a.State == ArrState.Ready);
                if (r != null) { s.SetPushRoute(r.No); s.SetAspect(Aspect.YellowGreen); }
            }
            var t0 = s.Pushing;
            if (t0 != null && t0.Cuts.Count > 0 && t0.Cuts.All(c => c.Forbidden) && !t0.Escort && t0.Cuts[0].Head > -25)
            { s.SetAspect(Aspect.Red); if (t0.V == 0) { s.StartEscort(); s.SetAspect(Aspect.Yellow); } }
        }
    }

    public static void Run(bool auto, int seed, double minutes)
    {
        var s = new YardSim(seed) { Ars = true, AutoOps = auto };
        int errs = 0;
        var refs = new Dictionary<string, int>();
        s.Log += (t, bad) => { if (t.StartsWith("Нельзя")) { errs++; refs[t] = refs.GetValueOrDefault(t) + 1; } };
        for (int i = 0; i < minutes * 60 * 20; i++)
        {
            s.Tick(0.05);
            if (s.Pushing == null)
            {
                var r = s.Arr.Skip(1).FirstOrDefault(a => a.State == ArrState.Ready);
                if (r != null) { s.SetPushRoute(r.No); s.SetAspect(Aspect.YellowGreen); }
                if (!auto)
                {
                    if (s.Loco == HumpLocoState.AtHorb) s.ReturnHumpLoco();
                    var w = s.Arr.Skip(1).FirstOrDefault(a => a.State == ArrState.WaitLoco);
                    if (w != null && s.Loco == HumpLocoState.Standby) s.SendHumpLoco(w.No);
                    for (int k = 1; k <= Lay.Tracks; k++) if (s.OnTrack(k).Sum(c => c.Wagons.Count) >= YardSim.Norm(k) && !s.ShuntBusy) s.FormTrain(k);
                }
            }
            var t0 = s.Pushing;
            if (t0 != null && t0.Cuts.Count > 0 && t0.Cuts.All(c => c.Forbidden) && !t0.Escort && t0.Cuts[0].Head > -25)
            { s.SetAspect(Aspect.Red); if (t0.V == 0) { s.StartEscort(); s.SetAspect(Aspect.Yellow); } }
        }
        Console.WriteLine($"=== долгий {(auto ? "авто" : "маневры")} seed {seed}, {minutes} мин: оценка {s.Grade} ({s.Score}), нарушений {s.Violations.Count}, распущено {s.Rolled} ваг./{s.CutsRolled} отц., свой путь {s.CorrectCuts}, отправлено {s.Departed}, отказов {errs}");
        foreach (var g in s.Violations.GroupBy(v => v.Text.Split(':')[0].Split(' ')[0])) Console.WriteLine($"   {g.Key}: {g.Count()}");
        Console.Write("  пути:"); for (int k = 1; k <= Lay.Tracks; k++) Console.Write($" {k}:{s.OnTrack(k).Sum(c => c.Wagons.Count)}"); Console.WriteLine();
        if (s.Pushing != null) Console.WriteLine($"  надвиг: {s.Pushing.Name} V={s.Pushing.V:0.00} head={s.Pushing.Head:0} cuts={s.Pushing.Cuts.Count} escort={s.Pushing.Escort} ret={s.Pushing.Returning} aspect={s.Hump} first={(s.Pushing.Cuts.Count > 0 ? s.Pushing.Cuts[0].Mark + " " + s.Pushing.Cuts[0].Head.ToString("0") : "")}");
        foreach (var v in s.Violations) Console.WriteLine("     " + v.Text);
        foreach (var r in refs.OrderByDescending(x => x.Value).Take(4)) Console.WriteLine($"  отказ x{r.Value}: {r.Key}");
        foreach (var c in s.Cuts.Where(c => c.Active && c.State != CutState.Stopped)) Console.WriteLine($"  активный: {c.No} {c.State} head={c.Head:0.0} V={c.V:0.000} lo{c.Lo}-{c.Hi} track{c.Track} W0={c.W0:0.00} n={c.Wagons.Count}");
        Console.Write("  отправление:"); for (int d = 1; d <= Lay.DepTracks; d++) Console.Write($" О{d}:{s.Dep[d].State}"); Console.WriteLine($" shuntBusy={s.ShuntBusy}");
        Console.Write("  парк приёма:"); for (int a = 1; a <= Lay.ArrTracks; a++) Console.Write($" П{a}:{s.Arr[a].State}"); Console.WriteLine($"  локомотив {s.Loco}, сцен {s.Movers.Count}");
    }
}
