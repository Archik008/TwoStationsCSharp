namespace StationApp.Yard;

// =====================================================================
//  ГЕОМЕТРИЯ УЧЕБНОЙ СОРТИРОВОЧНОЙ СТАНЦИИ  [ДОБАВЛЕНО 12.11.2026]
//  [ИЗМЕНЕНО 24.11.2026] цифры перенесены в BuiltinGeo (ниже) — данными (YardGeo.cs); Lay берёт всё из Lay.Cur,
//  и «Станция по схеме» подставляет туда горку, нарисованную в редакторе станций.
//
//  Станция односторонняя, парки последовательно (слева направо):
//    парк приёма П1–П4  →  путь надвига  →  ГОРКА  →  сортировочный парк, пути 1–8  →  парк отправления О1–О4.
//  Депо (ТЧ) и обходной путь — под парком приёма; стоянка горочного локомотива — у пути надвига.
//
//  Всё, что связано с горкой, считается в метрах вдоль пути от вершины горки (горба): s = 0 — горб,
//  до горба s < 0 (путь надвига и парк приёма), после — спускная часть, стрелочная зона и сортировочные пути.
//  Профиль (уклоны, ‰ — «тысячные», плюс — спуск по ходу роспуска) — по «Правилам и нормам проектирования
//  сортировочных устройств» для горки малой мощности, упрощённо:
//      −60…0     подъём к горбу 10‰ (на нём состав сжат — удобно расцеплять вагоны)
//        0…30    скоростной участок 45‰ (не круче 50‰)
//       30…62    1-я тормозная позиция (1ТП) на 14‰ (не положе 12‰)
//       62…82    стрелка 1 на 8‰
//       82…128   2-я тормозная позиция (2ТП-А для путей 1–4, 2ТП-Б для путей 5–8) на 9‰ (не положе 7‰)
//      128…186   стрелочная зона 1,5‰: стрелки 2, 3 и 4–7
//      186…440   сортировочные пути 0,6‰,  440…500 — встречный подъём 2,5‰ (останавливает слишком быстрые вагоны)
//  [ИЗМЕНЕНО 17.11.2026] поезда короче (6–10 ваг.), поэтому пути короче: сортировочные 314 м (было 650),
//  парк приёма 300 м (было 850) — вагоны на схеме стали крупнее.
//  Высота горки ≈ 2,6 м. Стрелки горки — 1…7, переводятся за 0,6 с (горочный привод).
// =====================================================================

/// <summary>Рельсовая цепь горки: участок s0…s1 для путей lo…hi.</summary>
public sealed class Edge
{
    public string Id = "";
    public double S0, S1;
    public int Lo, Hi;
    public Switch Sw;
}

/// <summary>Горочная стрелка: «плюс» — к путям PlusLo…PlusHi, «минус» — к MinusLo…MinusHi
/// (у встроенной горки плюс — Lo…Mid, минус — Mid+1…Hi; у горки из файла плюс может быть и снизу).</summary>
public sealed class Switch
{
    public int No;
    public double S;
    public int Lo, Hi, Mid;
    public int PlusLo, PlusHi, MinusLo, MinusHi;
    public Edge Section;
    public bool Plus = true;
    /// <summary>Перевод идёт (осталось секунд) и куда.</summary>
    public double ThrowLeft;
    public bool ThrowTo;
    public bool Throwing => ThrowLeft > 0;
    public bool Toward(int track) => track >= PlusLo && track <= PlusHi;
    /// <summary>Куда ведёт положение: «1–4», «5» …</summary>
    public string Range(bool plus) => plus ? Span(PlusLo, PlusHi) : Span(MinusLo, MinusHi);
    static string Span(int a, int b) => a == b ? a.ToString() : $"{a}–{b}";
    /// <summary>Какой путь показывает ветку «плюс» / «минус» на схеме.</summary>
    public int Track(bool plus) => plus ? PlusLo : MinusLo;
}

/// <summary>Тормозная позиция с вагонными замедлителями.</summary>
public sealed class Retarder
{
    public string Name = "";
    public double S0, S1;
    public int Lo, Hi;
    /// <summary>Ступень торможения 0 (оттормозить) … 4.</summary>
    public int Step;
    /// <summary>Отцеп на замедлителе или подходит к нему (для табло), рекомендуемая скорость выхода, м/с.</summary>
    public Cut Cut;
    public double Target;
    public bool CutOn;
    /// <summary>Какую ступень сейчас поставила бы АРС (0 — не тормозить) — подсказка при ручном торможении.</summary>
    public int Advice;
}

/// <summary>Прежние имена геометрии — всё из текущей горки Lay.Cur (встроенная или из файла).</summary>
public static class Lay
{
    /// <summary>Текущая горка. Меняется до создания YardSim и окна (YardForm).</summary>
    public static YardGeo Cur { get; private set; } = new BuiltinGeo();
    public static void Use(YardGeo g) => Cur = g;

    public const double Horb = 0;
    public static double STrack => Cur.STrack;
    public static double SEnd => Cur.SEnd;
    public static double SArrFront => Cur.SArrFront;
    public static double SArrTail => Cur.SArrTail;
    public static double SPushTrack => Cur.SPushTrack;
    public static double SSignal => Cur.SSignal;
    public static double LocoLimit => Cur.LocoLimit;
    public static double TrackLength => Cur.TrackLength;
    public static int Tracks => Cur.Tracks;
    public static int ArrTracks => Cur.ArrTracks;
    public static int DepTracks => Cur.DepTracks;
    /// <summary>Тормозное действие ступени замедлителя, ‰ (эквивалентный уклон) при полном вагоне на замедлителе.</summary>
    public static readonly double[] StepBrake = { 0, 15, 30, 45, 60 };
    /// <summary>Добавочное сопротивление в стрелках и кривых, ‰.</summary>
    public const double SwitchExtra = 1.5;
    /// <summary>g с учётом инерции вращающихся частей, м/с².</summary>
    public const double G = 9.3;

    public static double Grade(double s) => Cur.Grade(s);
    public static double Elev(double s) => Cur.Elev(s);
    public static bool InSwitchZone(double s) => Cur.InSwitchZone(s);
    public static List<Edge> MakeEdges(out Switch[] sw, out Retarder[] rt) => Cur.MakeEdges(out sw, out rt);

    public static float W => Cur.W;
    public static float H => Cur.H;
    public static RectangleF Profile => Cur.Profile;
    public static double ProfileFrom => Cur.ProfileFrom;
    public static double ProfileTo => Cur.ProfileTo;
    public static float Hv => Cur.Hv;
    public static void Fit(float aspect) => Cur.Fit(aspect);
    public static float TrackY(int k) => Cur.TrackY(k);
    public static float ArrY(int a) => Cur.ArrY(a);
    public static float DepY(int d) => Cur.DepY(d);
    public static PointF At(double s, int track, int arr) => Cur.At(s, track, arr);
    public static PointF[] Line(double s0, double s1, int track, int arr = 2) => Cur.Line(s0, s1, track, arr);
    public static PointF[] Path(double s0, double s1, int track, int arr) => Cur.Path(s0, s1, track, arr);
    public static PointF SwitchPoint(Switch sw) => Cur.SwitchPoint(sw);
    public static PointF SignalPoint => Cur.SignalPoint;
    public static PointF DepotGate => Cur.DepotGate;
    public static PointF HumpLocoPark => Cur.HumpLocoPark;
    public static PointF[] ArrivalPath(int a) => Cur.ArrivalPath(a);
    public static PointF[] LocoToDepot(int a) => Cur.LocoToDepot(a);
    public static PointF[] HumpLocoToTail(int a, float tailX) => Cur.HumpLocoToTail(a, tailX);
    public static PointF[] HumpLocoHome(PointF from) => Cur.HumpLocoHome(from);
    public static PointF[] ToDeparture(int k, int d) => Cur.ToDeparture(k, d);
    public static PointF[] DepotToDeparture(int d) => Cur.DepotToDeparture(d);
    public static PointF[] Departure(int d) => Cur.Departure(d);
    public static float SceneScale => Cur.SceneScale;
    public static float ArrScale => Cur.ArrScale;
    public static float ProfX(double s) => Cur.ProfX(s);
    public static float ProfPxPerM => Cur.ProfPxPerM;
    public static float ProfY(double s) => Cur.ProfY(s);
}

/// <summary>Учебная станция «Сортировочной» — цифры прежние (до 24.11.2026 — константы Lay).</summary>
public sealed class BuiltinGeo : YardGeo
{
    const float HorbX = 700, HorbY = 190;

    public BuiltinGeo()
    {
        Builtin = true;
        Grades.AddRange(new (double, double)[] { (-60, 0), (0, -10), (30, 45), (62, 14), (82, 8), (128, 9), (186, 1.5), (440, 0.6) });
        LastGrade = -2.5;
        SwitchZones.Add((62, 82));
        SwitchZones.Add((128, 186));

        void E(string id, double a, double b, int lo, int hi) => EdgeDefs.Add(new EdgeDef { Id = id, S0 = a, S1 = b, Lo = lo, Hi = hi });
        void S(int no, string id, double a, double b, double s, int lo, int hi)
        {
            int mid = (lo + hi) / 2;
            EdgeDefs.Add(new EdgeDef { Id = id, S0 = a, S1 = b, Lo = lo, Hi = hi, SwNo = no, SwS = s, PlusLo = lo, PlusHi = mid, MinusLo = mid + 1, MinusHi = hi });
        }
        E("ВГ", 0, 30, 1, 8);
        E("1ТП", 30, 62, 1, 8);
        S(1, "1СП", 62, 82, 72, 1, 8);
        E("2ТП-А", 82, 128, 1, 4);
        E("2ТП-Б", 82, 128, 5, 8);
        S(2, "2СП", 128, 146, 137, 1, 4);
        S(3, "3СП", 128, 146, 137, 5, 8);
        for (int q = 0; q < 4; q++)
        {
            int lo = 1 + q * 2;
            E($"{lo}-{lo + 1}", 146, 168, lo, lo + 1);
            S(4 + q, $"{4 + q}СП", 168, STrack, 177, lo, lo + 1);
        }
        for (int k = 1; k <= Tracks; k++) E(k + "П", STrack, SEnd, k, k);
        RetDefs.Add(new RetDef { Name = "1ТП", S0 = 34, S1 = 58, Lo = 1, Hi = 8 });
        RetDefs.Add(new RetDef { Name = "2ТП-А", S0 = 95, S1 = 120, Lo = 1, Hi = 4 });
        RetDefs.Add(new RetDef { Name = "2ТП-Б", S0 = 95, S1 = 120, Lo = 5, Hi = 8 });

        TrackNames = new[] { "", "Северная", "Восточная", "Южная", "Западная", "Заводская", "Портовая", "Сборный (местные)", "Ремонт (ВЧД)" };
        Norms = Enumerable.Range(0, Tracks + 1).Select(k => k >= 7 ? YardSim.NormSmall : YardSim.NormMain).ToArray();

        Tree = Enumerable.Range(0, Tracks + 1).Select(k => k == 0 ? null : BuildTree(k)).ToArray();
        Push = Enumerable.Range(0, ArrTracks + 1).Select(a => a == 0 ? null : BuildPush(a)).ToArray();
        DepYs = Enumerable.Range(0, DepTracks + 1).Select(d => d == 0 ? 0 : DepY(d)).ToArray();
        ProfileMarks.Add((72, "стр. 1", 0));
        ProfileMarks.Add((137, "стр. 2, 3", 0));
        ProfileMarks.Add((177, "стр. 4–7", 0));
        ProfileMarks.Add((STrack, "пути 1–8", 14));
        Finish();
    }

    public override int RandomDest(Random rnd) => rnd.NextDouble() < 0.08 ? 8 : rnd.NextDouble() < 0.12 ? 7 : rnd.Next(1, 7);
    public override WagonKind KindFor(int dest, Random rnd)
    {
        double r = rnd.NextDouble();
        return dest switch
        {
            6 => r < 0.5 ? Rolling.Container : r < 0.8 ? Rolling.Tank : Rolling.Covered,
            5 => r < 0.4 ? Rolling.Gondola : r < 0.7 ? Rolling.Flat : Rolling.Hopper,
            8 => Rolling.Normal[rnd.Next(Rolling.Normal.Length)],
            _ => Rolling.Normal[rnd.Next(Rolling.Normal.Length)],
        };
    }
    public override int ForbiddenDest(WagonKind k, Random rnd) => k == Rolling.Service ? 7 : rnd.Next(1, 7);
    public override double LoadedShare(int dest) => dest == 8 ? 0.15 : 0.62;

    public override float TrackY(int k) => 50 + (k - 1) * 40;
    public override float ArrY(int a) => 130 + (a - 1) * 40;
    public override float DepY(int d) => 110 + (d - 1) * 60;

    (double, PointF)[] BuildTree(int k)
    {
        float yAB = k <= 4 ? 110 : 270;
        float yQ = new float[] { 70, 150, 230, 310 }[(k - 1) / 2];
        float yT = TrackY(k);
        return new (double, PointF)[]
        {
            (0, new(HorbX, HorbY)), (72, new(812, HorbY)), (90, new(840, yAB)), (137, new(920, yAB)),
            (152, new(945, yQ)), (177, new(975, yQ)), (192, new(1002, yT)), (SEnd, new(1400, yT)),
        };
    }

    (double, PointF)[] BuildPush(int a)
    {
        float y = ArrY(a);
        return new (double, PointF)[]
        {
            (SArrTail - 40, new(132, y)), (SArrTail, new(150, y)), (SArrFront, new(520, y)), (-135, new(548, y)),
            (SPushTrack, new(585, HorbY)), (0, new(HorbX, HorbY)),
        };
    }

    public override PointF[] ArrivalPath(int a) => new PointF[] { new(0, HorbY), new(60, HorbY), new(120, ArrY(a)), new(150, ArrY(a)), new(520, ArrY(a)) };
    public override PointF[] LocoToDepot(int a) => new PointF[] { new(520, ArrY(a)), new(548, ArrY(a)), new(580, 296), new(490, 296), DepotGate };
    public override PointF[] HumpLocoToTail(int a, float tailX) => new PointF[] { HumpLocoPark, new(612, 236), new(580, 296), new(108, 296), new(118, ArrY(a)), new(132, ArrY(a)), new(tailX, ArrY(a)) };
    public override PointF[] HumpLocoHome(PointF from) => new PointF[] { from, new(600, HorbY), new(612, 236), HumpLocoPark };
    public override PointF[] ToDeparture(int k, int d) => new PointF[] { new(1002, TrackY(k)), new(1400, TrackY(k)), new(1412, TrackY(k)), new(1430, DepY(d)), new(1588, DepY(d)) };
    public override PointF[] DepotToDeparture(int d) => new PointF[] { DepotGate, new(490, 404), new(1596, 404), new(1596, DepY(d) + 12), new(1590, DepY(d)) };
    public override PointF[] Departure(int d) => new PointF[] { new(1430, DepY(d)), new(1588, DepY(d)), new(1640, DepY(d)) };
}
