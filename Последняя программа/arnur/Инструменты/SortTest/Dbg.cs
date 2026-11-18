using StationApp.Yard;
static class Dbg
{
    public static void Ars(double w0, double mass, int n)
    {
        var s = new YardSim(1) { Ars = true };
        var c = new Cut { No = 99, Dest = 5, Route = 5, State = CutState.Rolling, Head = 8, V = 1.67 };
        for (int i = 0; i < n; i++) c.Wagons.Add(new Wagon { Kind = Rolling.Covered, Loaded = true, Mass = mass, W0 = w0, Dest = 5 });
        s.Cuts.Add(c);
        double last = -100;
        for (int i = 0; i < 20 * 600 && c.State != CutState.Stopped; i++)
        {
            s.Tick(0.05);
            if (c.Head - last >= 6 && c.Head < 200 || c.Head - last > 60) { last = c.Head;
                Console.WriteLine($"  s={c.Head,6:0} v={c.V*3.6,5:0.0} 1ТП st{s.Retarders[0].Step} tg{s.Retarders[0].Target*3.6:0.0}  2ТП st{s.Retarders[2].Step} tg{s.Retarders[2].Target*3.6:0.0}"); }
        }
        Console.WriteLine($"  стоп s={c.Head:0} v={c.V*3.6:0.0}");
    }
}
