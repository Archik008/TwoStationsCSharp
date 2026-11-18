namespace StationApp.Yard;

// =====================================================================
//  ГЕОМЕТРИЯ УЧЕБНОЙ СОРТИРОВОЧНОЙ СТАНЦИИ  [ДОБАВЛЕНО 12.11.2026]
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

/// <summary>Горочная стрелка: «плюс» — к путям Lo…Mid, «минус» — к Mid+1…Hi.</summary>
public sealed class Switch
{
    public int No;
    public double S;
    public int Lo, Hi, Mid;
    public Edge Section;
    public bool Plus = true;
    /// <summary>Перевод идёт (осталось секунд) и куда.</summary>
    public double ThrowLeft;
    public bool ThrowTo;
    public bool Throwing => ThrowLeft > 0;
    public bool Toward(int track) => track <= Mid;
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

public static class Lay
{
    // ---- главные отметки, м ----
    public const double Horb = 0, STrack = 186, SEnd = 500, SArrFront = -150, SArrTail = -450, SPushTrack = -100;
    /// <summary>Горочный светофор Г — в начале подъёма к горбу.</summary>
    public const double SSignal = -60;
    /// <summary>Горочный локомотив дальше этой точки не заходит (кроме спуска вагонов с локомотивом).</summary>
    public const double LocoLimit = -3;
    public const double TrackLength = SEnd - STrack;
    public const int Tracks = 8, ArrTracks = 4, DepTracks = 4;
    /// <summary>Тормозное действие ступени замедлителя, ‰ (эквивалентный уклон) при полном вагоне на замедлителе.</summary>
    public static readonly double[] StepBrake = { 0, 15, 30, 45, 60 };
    /// <summary>Добавочное сопротивление в стрелках и кривых, ‰.</summary>
    public const double SwitchExtra = 1.5;
    /// <summary>g с учётом инерции вращающихся частей, м/с².</summary>
    public const double G = 9.3;

    public static double Grade(double s) =>
        s < -60 ? 0 : s < 0 ? -10 : s < 30 ? 45 : s < 62 ? 14 : s < 82 ? 8 : s < 128 ? 9 : s < STrack ? 1.5 : s < SEnd - 60 ? 0.6 : -2.5;

    static readonly double[] elev = BuildElev();
    const double ElevFrom = -1200, ElevStep = 1;
    static double[] BuildElev()
    {
        int n = (int)((SEnd + 50 - ElevFrom) / ElevStep) + 1;
        var z = new double[n];
        // высота отсчитывается от горба (0), вниз — минус
        int i0 = (int)(-ElevFrom / ElevStep);
        for (int i = i0 + 1; i < n; i++) z[i] = z[i - 1] - Grade(ElevFrom + (i - 1) * ElevStep) * ElevStep / 1000;
        for (int i = i0 - 1; i >= 0; i--) z[i] = z[i + 1] + Grade(ElevFrom + i * ElevStep) * ElevStep / 1000;
        return z;
    }
    /// <summary>Высота пути над горбом (м, ниже — минус).</summary>
    public static double Elev(double s)
    {
        double f = (s - ElevFrom) / ElevStep;
        int i = Math.Clamp((int)f, 0, elev.Length - 2);
        double t = Math.Clamp(f - i, 0, 1);
        return elev[i] + (elev[i + 1] - elev[i]) * t;
    }

    public static bool InSwitchZone(double s) => s is >= 62 and < 82 or >= 128 and < STrack;

    // ---- рельсовые цепи, стрелки, замедлители (новый набор на каждую смену — у стрелок и замедлителей есть состояние) ----
    public static List<Edge> MakeEdges(out Switch[] sw, out Retarder[] rt)
    {
        var e = new List<Edge>();
        sw = new Switch[8];
        Edge E(string id, double a, double b, int lo, int hi) { var x = new Edge { Id = id, S0 = a, S1 = b, Lo = lo, Hi = hi }; e.Add(x); return x; }
        Switch S(int no, Edge sec, double s, int lo, int hi)
        {
            var x = new Switch { No = no, S = s, Lo = lo, Hi = hi, Mid = (lo + hi) / 2, Section = sec };
            sec.Sw = x;
            return x;
        }
        E("ВГ", 0, 30, 1, 8);
        E("1ТП", 30, 62, 1, 8);
        sw[1] = S(1, E("1СП", 62, 82, 1, 8), 72, 1, 8);
        E("2ТП-А", 82, 128, 1, 4);
        E("2ТП-Б", 82, 128, 5, 8);
        sw[2] = S(2, E("2СП", 128, 146, 1, 4), 137, 1, 4);
        sw[3] = S(3, E("3СП", 128, 146, 5, 8), 137, 5, 8);
        for (int q = 0; q < 4; q++)
        {
            int lo = 1 + q * 2;
            E($"{lo}-{lo + 1}", 146, 168, lo, lo + 1);
            sw[4 + q] = S(4 + q, E($"{4 + q}СП", 168, STrack, lo, lo + 1), 177, lo, lo + 1);
        }
        for (int k = 1; k <= Tracks; k++) E(k + "П", STrack, SEnd, k, k);
        rt = new[]
        {
            new Retarder { Name = "1ТП", S0 = 34, S1 = 58, Lo = 1, Hi = 8 },
            new Retarder { Name = "2ТП-А", S0 = 95, S1 = 120, Lo = 1, Hi = 4 },
            new Retarder { Name = "2ТП-Б", S0 = 95, S1 = 120, Lo = 5, Hi = 8 },
        };
        return e;
    }

    // =================================================================
    //  ЭКРАН: схема в условных точках 1600 × 630 (окно масштабирует)
    // =================================================================
    public const float W = 1600, H = 630;
    /// <summary>Полоса «вид сбоку» (профиль горки).</summary>
    public static RectangleF Profile = new(10, 448, 1580, 176);
    public const double ProfileFrom = -150, ProfileTo = 260;
    /// <summary>Высота схемы в условных точках: окно выше — полоса «вид сбоку» растёт (схема не мельчит).</summary>
    public static float Hv = H;
    public static void Fit(float aspect)
    {
        Hv = Math.Clamp(W * aspect, H, 454 + 250);
        Profile = new RectangleF(10, 448, 1580, Hv - 454);
    }

    public static float TrackY(int k) => 50 + (k - 1) * 40;
    public static float ArrY(int a) => 130 + (a - 1) * 40;
    public static float DepY(int d) => 110 + (d - 1) * 60;
    const float HorbX = 700, HorbY = 190;

    /// <summary>Точки пути с отметками s: к сортировочному пути k (s ≥ 0) …</summary>
    static readonly (double s, PointF p)[][] tree = Enumerable.Range(0, Tracks + 1).Select(k => k == 0 ? null : BuildTree(k)).ToArray();
    static (double, PointF)[] BuildTree(int k)
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
    /// <summary>… и от парка приёма (путь a) до горба (s ≤ 0).</summary>
    static readonly (double s, PointF p)[][] push = Enumerable.Range(0, ArrTracks + 1).Select(a => a == 0 ? null : BuildPush(a)).ToArray();
    static (double, PointF)[] BuildPush(int a)
    {
        float y = ArrY(a);
        return new (double, PointF)[]
        {
            (SArrTail - 40, new(132, y)), (SArrTail, new(150, y)), (SArrFront, new(520, y)), (-135, new(548, y)),
            (SPushTrack, new(585, HorbY)), (0, new(HorbX, HorbY)),
        };
    }

    static PointF Interp((double s, PointF p)[] pts, double s)
    {
        if (s <= pts[0].s) return pts[0].p;
        for (int i = 1; i < pts.Length; i++)
            if (s <= pts[i].s)
            {
                float t = (float)((s - pts[i - 1].s) / (pts[i].s - pts[i - 1].s));
                return new(pts[i - 1].p.X + (pts[i].p.X - pts[i - 1].p.X) * t, pts[i - 1].p.Y + (pts[i].p.Y - pts[i - 1].p.Y) * t);
            }
        return pts[^1].p;
    }

    /// <summary>Точка схемы: s — отметка, track — путь сортировочного парка (для s ≥ 0), arr — путь парка приёма (s &lt; 0).</summary>
    public static PointF At(double s, int track, int arr) =>
        s >= 0 ? Interp(tree[Math.Clamp(track, 1, Tracks)], s) : Interp(push[Math.Clamp(arr, 1, ArrTracks)], s);

    /// <summary>Ломаная участка s0…s1 (для рисования рельсовой цепи).</summary>
    public static PointF[] Line(double s0, double s1, int track, int arr = 2)
    {
        var src = s0 >= 0 ? tree[Math.Clamp(track, 1, Tracks)] : push[Math.Clamp(arr, 1, ArrTracks)];
        var list = new List<PointF> { At(s0, track, arr) };
        foreach (var (s, p) in src) if (s > s0 && s < s1) list.Add(p);
        list.Add(At(s1, track, arr));
        return list.ToArray();
    }

    /// <summary>Ломаная по рельсам от s0 до s1 (через горб — путь парка приёма arr и сортировочный путь track).
    /// По ней рисуется вагон: на кривой он изгибается вместе с путём, а не срезает угол. [ДОБАВЛЕНО 17.11.2026]</summary>
    public static PointF[] Path(double s0, double s1, int track, int arr)
    {
        var list = new List<PointF> { At(s0, track, arr) };
        foreach (var (s, _) in push[Math.Clamp(arr, 1, ArrTracks)]) if (s > s0 && s < s1 && s < 0) list.Add(At(s, track, arr));
        foreach (var (s, _) in tree[Math.Clamp(track, 1, Tracks)]) if (s > s0 && s < s1 && s >= 0) list.Add(At(s, track, arr));
        list.Add(At(s1, track, arr));
        return list.ToArray();
    }

    public static PointF SwitchPoint(Switch sw) => At(sw.S, sw.Lo, 1);
    public static PointF SignalPoint => At(SSignal, 1, 1);

    // ---- пути, которые только рисуются (движение по ним — «сценой»: приём, депо, вывод в парк отправления) ----
    public static readonly PointF DepotGate = new(470, 362);
    public static readonly PointF HumpLocoPark = new(650, 236);
    public static PointF[] ArrivalPath(int a) => new PointF[] { new(0, HorbY), new(60, HorbY), new(120, ArrY(a)), new(150, ArrY(a)), new(520, ArrY(a)) };
    public static PointF[] LocoToDepot(int a) => new PointF[] { new(520, ArrY(a)), new(548, ArrY(a)), new(580, 296), new(490, 296), DepotGate };
    public static PointF[] HumpLocoToTail(int a, float tailX) => new PointF[] { HumpLocoPark, new(612, 236), new(580, 296), new(108, 296), new(118, ArrY(a)), new(132, ArrY(a)), new(tailX, ArrY(a)) };
    public static PointF[] HumpLocoHome(PointF from) => new PointF[] { from, new(600, HorbY), new(612, 236), HumpLocoPark };
    public static PointF[] ToDeparture(int k, int d) => new PointF[] { new(1002, TrackY(k)), new(1400, TrackY(k)), new(1412, TrackY(k)), new(1430, DepY(d)), new(1588, DepY(d)) };
    public static PointF[] DepotToDeparture(int d) => new PointF[] { DepotGate, new(490, 404), new(1596, 404), new(1596, DepY(d) + 12), new(1590, DepY(d)) };
    public static PointF[] Departure(int d) => new PointF[] { new(1430, DepY(d)), new(1588, DepY(d)), new(1640, DepY(d)) };
    /// <summary>Метров на точку схемы в парке отправления и на «сценах».</summary>
    public const float SceneScale = 0.9f;
    /// <summary>Метров на точку схемы в парке приёма (для «сцены» прибытия — чтобы поезд не менял длину, встав на путь).</summary>
    public const float ArrScale = (float)(370 / (SArrFront - SArrTail));

    // ---- вид сбоку ----
    public static float ProfX(double s) => Profile.Left + 10 + (float)((s - ProfileFrom) / (ProfileTo - ProfileFrom)) * (Profile.Width - 20);
    public static float ProfPxPerM => (Profile.Width - 20) / (float)(ProfileTo - ProfileFrom);
    static double zMin => ElevView(ProfileTo);
    static double zMax => ElevView(0);
    static float ProfVScale => Math.Min(Profile.Height - 70, 150);
    /// <summary>Высоты на виде сбоку — с преувеличением (иначе горки в 2,6 м на 460 м не видно).
    /// [ИЗМЕНЕНО 17.11.2026] Переломы профиля сглажены вертикальными кривыми (как на настоящей горке),
    /// преувеличение меньше — вагоны на горбу и в «ямах» не проваливаются в рельсы и не висят над ними.</summary>
    public static float ProfY(double s) => Profile.Bottom - 22 - (float)((ElevView(s) - zMin) / (zMax - zMin)) * ProfVScale;

    const double ViewFrom = ProfileFrom - 60, ViewTo = ProfileTo + 60, Smooth = 12;
    static readonly double[] elevView = BuildView();
    static double[] BuildView()
    {
        int n = (int)(ViewTo - ViewFrom) + 1;
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = ViewFrom + i, sum = 0; int m = 0;
            for (double d = -Smooth; d <= Smooth; d += 1) { sum += Elev(s + d); m++; }
            z[i] = sum / m;
        }
        return z;
    }
    static double ElevView(double s)
    {
        double f = s - ViewFrom;
        int i = Math.Clamp((int)f, 0, elevView.Length - 2);
        double t = Math.Clamp(f - i, 0, 1);
        return elevView[i] + (elevView[i + 1] - elevView[i]) * t;
    }
}
