namespace StationApp.Yard;

// =====================================================================
//  МОДЕЛЬ СОРТИРОВОЧНОЙ СТАНЦИИ И ГОРКИ  [ДОБАВЛЕНО 12.11.2026]
//
//  Порядок работы (как в техпроцессе сортировочной станции):
//   1) поезд приходит в парк приёма; поездной локомотив отцепляется и уходит в депо;
//   2) осмотр ПТО и коммерческий осмотр, отпуск тормозов, сортировочный листок (какой отцеп на какой путь);
//   3) горочный локомотив (ТЭМ18) заходит в хвост состава;
//   4) ДСПГ задаёт маршрут надвига и открывает горочный светофор: состав надвигается на горку,
//      на подъёме перед горбом расцепщик расцепляет вагоны по листку, отцепы перевалив горб скатываются сами;
//   5) стрелки горки переводит ГАЦ по маршрутам из накопителя (режим «ГАЦ» — из листка, «ручной» — кнопками путей);
//   6) скорость отцепов регулируют замедлители 1ТП и 2ТП (ступени 1–4) или АРС (автоматически);
//   7) на путях сортировочного парка копятся составы; набрался состав — его выводят в парк отправления,
//      прицепляют поездной локомотив, опробуют тормоза и отправляют.
//
//  Показания горочного светофора (Инструкция по сигнализации РК, п. 99):
//   зелёный — роспуск с установленной скоростью; жёлтый с зелёным — с промежуточной; жёлтый — с уменьшенной;
//   красный — стой, роспуск запрещён; красный с белой буквой «Н» — осадить вагоны с горки в парк приёма.
//   Скорости роспуска устанавливает руководство станции — здесь 6 / 4,5 / 3 км/ч (меняются ниже: PushSpeed).
//
//  Нарушения (как в техпроцессе горки): удар при сцеплении > 5 км/ч, недокат (окно > 50 м), нагон отцепа
//  в стрелочной зоне, отцеп не на свой путь, стрелка переведена под отцепом, отцеп остановился в стрелочной зоне
//  или на тормозной позиции, роспуск запрещённого вагона (отметки «Л», «Е»), выход за предельный столбик.
//
//  Варианты работы (кнопка вверху окна):
//   «Только горка» — приём, осмотр, подача и возврат горочного локомотива, вывод составов в парк отправления
//                    и отправление идут сами; студент — надвиг и роспуск;
//   «Горка + маневры» — горочный локомотив под состав, его возврат и вывод составов — делает студент.
//   [ДОБАВЛЕНО 26.11.2026]
//   «Учебный» — студент делает всё сам: принимает поезд на путь парка приёма, подаёт и возвращает горочный локомотив,
//               надвиг, маршруты отцепам (вручную), торможение замедлителями (без АРС), тормозные башмаки на путях,
//               вывод состава, заказ поездного локомотива и отправление. Программа проверяет каждый шаг и ставит оценку;
//   «Экзамен»   — то же без подсказок; «Демонстрация» — всё делает автопилот и пишет в журнал, что и почему.
// =====================================================================

/// <summary>[ДОБАВЛЕНО 26.11.2026] Вариант работы (кнопки вверху окна).</summary>
public enum YardMode { Hump, HumpShunt, Training, Demo, Exam }

public enum Aspect { Red, Yellow, YellowGreen, Green, RedN }

public enum ArrState { Free, Arriving, Uncoupling, Inspection, WaitLoco, LocoComing, Ready, Pushing }
public enum HumpLocoState { Standby, ToTrain, Coupled, AtHorb, Returning }
public enum DepState { Free, Filling, WaitLoco, LocoComing, BrakeTest, Ready, Leaving }   // Ready — только в «Учебном» / «Экзамене»: ждёт команды «Отправить»

public sealed class Train
{
    public int No, Arr;
    public LocoKind TrainLoco;
    /// <summary>Все отцепы поезда — строки сортировочного листка.</summary>
    public readonly List<Cut> AllCuts = new();
    /// <summary>Отцепы, ещё стоящие в составе (первый — ближе к горке).</summary>
    public readonly List<Cut> Cuts = new();
    /// <summary>Голова состава (первого отцепа или локомотива, если вагонов нет), скорость надвига.</summary>
    public double Head, V;
    /// <summary>Горочный локомотив прицеплен к хвосту.</summary>
    public bool HumpLoco;
    /// <summary>Спуск вагонов с горки локомотивом (запрещённые к роспуску) и возврат локомотива после него.</summary>
    public bool Escort, Returning;
    public readonly Dictionary<int, bool> Passed = new();
    public int Lo = 1, Hi = Lay.Tracks;
    public double ReadySince;
    public int WagonCount => AllCuts.Sum(c => c.Wagons.Count);
    public double CutsLength => Cuts.Sum(c => c.Length);
    /// <summary>Передняя точка горочного локомотива (он за последним вагоном).</summary>
    public double LocoFront => Head - CutsLength;
    public string Name => "№" + No;
}

public sealed class ArrTrack
{
    public int No;
    public ArrState State;
    public Train Train;
    public double Until;
}

public sealed class DepTrack
{
    public int No;
    public DepState State;
    public readonly List<Wagon> Wagons = new();
    public int From, TrainNo;
    public double Until;
    public LocoKind Loco;
    /// <summary>[26.11.2026] Нарушение «задержка» уже записано (не повторять).</summary>
    public bool Late;
}

/// <summary>Движение «сценой» по ломаной схемы (приём, уход в депо, заезд горочного локомотива, вывод состава…).</summary>
public sealed class Mover
{
    public PointF[] Pts = Array.Empty<PointF>();
    /// <summary>Положение головы — точек схемы от начала ломаной; скорость — точек/с.</summary>
    public double Pos, Speed = 14;
    public readonly List<(float len, LocoKind loco, Wagon wagon)> Vehicles = new();
    public Action Done;
    public bool Finished;
    public double Total
    {
        get { double t = 0; for (int i = 1; i < Pts.Length; i++) t += Dist(Pts[i - 1], Pts[i]); return t; }
    }
    public static double Dist(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    public PointF At(double d, out float angle)
    {
        angle = 0;
        if (Pts.Length == 0) return PointF.Empty;
        if (d <= 0) { angle = Ang(Pts[0], Pts[Math.Min(1, Pts.Length - 1)]); return Pts[0]; }
        for (int i = 1; i < Pts.Length; i++)
        {
            double l = Dist(Pts[i - 1], Pts[i]);
            if (d <= l || i == Pts.Length - 1)
            {
                float t = l <= 0 ? 0 : (float)Math.Min(1, d / l);
                angle = Ang(Pts[i - 1], Pts[i]);
                return new(Pts[i - 1].X + (Pts[i].X - Pts[i - 1].X) * t, Pts[i - 1].Y + (Pts[i].Y - Pts[i - 1].Y) * t);
            }
            d -= l;
        }
        return Pts[^1];
    }
    static float Ang(PointF a, PointF b) => (float)Math.Atan2(b.Y - a.Y, b.X - a.X);
}

public sealed class Violation
{
    public double T;
    public string Text = "";
    public int Penalty;
}

public sealed class YardSim
{
    // =================================================================
    //  НАСТРОЙКИ
    // =================================================================
    /// <summary>Скорость надвига по показаниям горочного светофора, м/с: Ж, Ж+З, З.</summary>
    public static readonly double[] PushSpeed = { 0, 3 / 3.6, 4.5 / 3.6, 6 / 3.6 };
    /// <summary>Скорость, с которой вагоны должны подходить к стоящим на пути (не больше 5 км/ч), м/с.</summary>
    public const double CoupleSpeed = 0.75, MaxCouple = 5 / 3.6;
    /// <summary>Норма состава для вывода в парк отправления: пути 1–6 и пути 7–8 (сборный, ремонт).</summary>
    public const int NormMain = 8, NormSmall = 5;   // [ИЗМЕНЕНО 17.11.2026] поезда короче — нормы меньше (было 15 и 10)
    // [ИЗМЕНЕНО 24.11.2026] назначения и нормы путей — у горки (Lay.Cur): у встроенной прежние, у горки из файла — свои
    public static string[] TrackNames => Lay.Cur.TrackNames;
    public static int Norm(int k) => Lay.Cur.Norm(k);

    // =================================================================
    //  СОСТОЯНИЕ
    // =================================================================
    public double T;
    public DateTime Clock => new DateTime(2026, 1, 1, 8, 0, 0).AddSeconds(T);
    /// <summary>true — «Только горка» (остальное само), false — «Горка + маневры».</summary>
    public bool AutoOps = true;
    /// <summary>true — маршруты отцепов из листка (ГАЦ), false — ручной набор кнопками путей.</summary>
    public bool GacMode = true;
    public bool Ars;
    public Aspect Hump = Aspect.Red;
    public Train Pushing, ListokTrain;
    public readonly List<Edge> Edges;
    public readonly Switch[] Switches;
    public readonly Retarder[] Retarders;
    public readonly ArrTrack[] Arr = Enumerable.Range(0, Lay.ArrTracks + 1).Select(i => new ArrTrack { No = i }).ToArray();
    public readonly DepTrack[] Dep = Enumerable.Range(0, Lay.DepTracks + 1).Select(i => new DepTrack { No = i }).ToArray();
    public readonly List<Cut> Cuts = new();
    public readonly List<Mover> Movers = new();
    public readonly List<Violation> Violations = new();
    public HumpLocoState Loco = HumpLocoState.Standby;
    public Train LocoTrain;
    /// <summary>Где стоит горочный локомотив после роспуска (s передней точки и путь парка приёма).</summary>
    public double LocoS = Lay.LocoLimit;
    public int LocoArr = 1;
    public bool ShuntBusy;
    public Cut SelectedCut;
    public int Rolled, CutsRolled, Departed, CorrectCuts;
    /// <summary>[ДОБАВЛЕНО 26.11.2026] Поездов распущено до конца (для заданий).</summary>
    public int TrainsRolled;
    /// <summary>[26.11.2026] Составов выведено в парк отправления.</summary>
    public int Formed;

    // ---- [ДОБАВЛЕНО 26.11.2026] варианты «Учебный», «Экзамен», «Демонстрация» ----
    public YardMode Mode = YardMode.Hump;
    /// <summary>«Учебный» и «Экзамен»: всё делает студент, программа проверяет.</summary>
    public bool Strict => Mode is YardMode.Training or YardMode.Exam;
    /// <summary>Поезд на подходе: ждёт, пока студент назначит путь парка приёма (только Strict).</summary>
    public Train Approaching;
    double approachSince;
    bool approachLate;
    /// <summary>Тормозной башмак на сортировочном пути k (только Strict): лежит в 12 м от упора.</summary>
    public readonly bool[] Shoe = new bool[Lay.Tracks + 1];
    public const double ShoeGap = 12, ShoeMaxSpeed = 12 / 3.6;
    public static double ShoeS => Lay.SEnd - ShoeGap;
    readonly bool[] shoeWarned = new bool[Lay.Tracks + 1], overflowWarned = new bool[Lay.Tracks + 1];
    /// <summary>В каждом поезде — вагон, запрещённый к роспуску (для задания).</summary>
    public bool ForceForbidden;
    /// <summary>Автопилот («Демонстрация»).</summary>
    public bool Autopilot;
    public int Score => Math.Max(0, 100 - Violations.Sum(v => v.Penalty));
    public string Grade => Score >= 90 ? "5" : Score >= 75 ? "4" : Score >= 60 ? "3" : "2";

    /// <summary>Запись журнала (текст по-русски; violation — нарушение / отказ).</summary>
    public event Action<string, bool> Log;
    public event Action Changed;

    readonly Random rnd;
    int nextTrainNo = 2001, nextDepNo = 3001, nextCutNo = 1;
    double nextArrival;

    /// <param name="setup">[26.11.2026] вариант и настройки до первого поезда (ApplyMode, ForceForbidden)</param>
    public YardSim(int seed, Action<YardSim> setup = null)
    {
        rnd = new Random(seed);
        Edges = Lay.MakeEdges(out Switches, out Retarders);
        setup?.Invoke(this);
        // начало смены: поезд в парке приёма уже осмотрен, горочный локомотив прицеплен; второй — на подходе
        var t = MakeTrain(1);
        t.TrainLoco = null;
        PlaceInPark(t);
        Arr[1].State = ArrState.Ready;
        t.HumpLoco = true;
        Loco = HumpLocoState.Coupled;
        LocoTrain = t;
        ListokTrain = t;
        nextArrival = 40;
        if (Strict)
        {
            // учебный: состав осмотрен, но горочный локомотив ещё на стоянке — подаёт студент
            Arr[1].State = ArrState.WaitLoco;
            t.HumpLoco = false;
            Loco = HumpLocoState.Standby;
            LocoTrain = null;
        }
    }

    /// <summary>[ДОБАВЛЕНО 26.11.2026] Включить вариант работы. «Учебный» и «Экзамен» — АРС выключена, маршруты вручную.</summary>
    public void ApplyMode(YardMode m)
    {
        Mode = m;
        AutoOps = m is YardMode.Hump or YardMode.Demo;
        Autopilot = m == YardMode.Demo;
        if (Strict) { Ars = false; foreach (var r in Retarders) r.Step = 0; SetGac(false); }
        if (m == YardMode.Demo) { Ars = true; SetGac(true); }
    }

    void Say(string ru, bool bad = false) => Log?.Invoke(ru, bad);

    void Violate(string ru, int penalty)
    {
        Violations.Add(new Violation { T = T, Text = ru, Penalty = penalty });
        Say("НАРУШЕНИЕ: " + ru + $" (−{penalty})", true);
    }

    /// <summary>Отказ команды (не нарушение): сообщение студенту и ИИ-помощнику.</summary>
    public bool Refuse(string ru)
    {
        Say("Нельзя: " + ru, true);
        return false;
    }

    // =================================================================
    //  ПОЕЗДА И ВАГОНЫ
    // =================================================================
    Wagon MakeWagon(WagonKind k, int dest)
    {
        bool loaded = k.Mark == "Л" || rnd.NextDouble() < Lay.Cur.LoadedShare(dest);
        var w = new Wagon { Kind = k, Loaded = loaded, Dest = dest };
        w.Mass = k.Tare + (loaded ? k.MaxLoad * (0.75 + 0.25 * rnd.NextDouble()) : 0);
        w.W0 = (loaded ? k.W0Loaded : k.W0Empty) * (0.85 + 0.3 * rnd.NextDouble());
        w.Number = k.Digit + rnd.Next(1000000, 9999999).ToString();
        return w;
    }

    WagonKind KindFor(int dest) => Lay.Cur.KindFor(dest, rnd);

    Train MakeTrain(int arr)
    {
        var t = new Train { No = nextTrainNo, Arr = arr, TrainLoco = Rolling.Train[rnd.Next(Rolling.Train.Length)] };
        nextTrainNo += 2;
        // [ИЗМЕНЕНО 17.11.2026] поезд 6–10 вагонов (было 22–30): вагоны на схеме крупнее, роспуск нагляднее
        bool forb = rnd.NextDouble() < 0.45 || ForceForbidden;
        int target = rnd.Next(6, 11) - (forb ? 1 : 0), count = 0;
        while (count < target)
        {
            int dest = Lay.Cur.RandomDest(rnd);
            if (t.AllCuts.Count > 0 && t.AllCuts[^1].Dest == dest) continue;
            int n = rnd.NextDouble() < 0.55 ? 1 : rnd.NextDouble() < 0.7 ? 2 : 3;
            n = Math.Min(n, target - count);
            var c = new Cut { No = nextCutNo++, Dest = dest, TrainNo = t.No };
            var kind = KindFor(dest);
            for (int i = 0; i < n; i++) c.Wagons.Add(MakeWagon(rnd.NextDouble() < 0.7 ? kind : KindFor(dest), dest));
            t.AllCuts.Add(c);
            count += n;
        }
        // вагоны, запрещённые к роспуску с горки, ставят в хвост состава: их спускают с локомотивом
        if (forb)
        {
            var k = Rolling.Forbidden[rnd.Next(Rolling.Forbidden.Length)];
            int dest = Lay.Cur.ForbiddenDest(k, rnd);
            var c = new Cut { No = nextCutNo++, Dest = dest, TrainNo = t.No };
            c.Wagons.Add(MakeWagon(k, dest));
            t.AllCuts.Add(c);
        }
        // номера отцепов — по порядку в листке
        for (int i = 0; i < t.AllCuts.Count; i++) t.AllCuts[i].No = i + 1;
        foreach (var c in t.AllCuts) c.Route = GacMode ? c.Dest : 0;
        t.Cuts.AddRange(t.AllCuts);
        return t;
    }

    void PlaceInPark(Train t)
    {
        t.Head = Lay.SArrFront - (t.TrainLoco?.Length ?? 0) - 1;
        LayoutTrain(t);
        Arr[t.Arr].Train = t;
    }

    void LayoutTrain(Train t)
    {
        double h = t.Head;
        foreach (var c in t.Cuts)
        {
            c.Head = h;
            c.V = t.V;
            h -= c.Length;
        }
    }

    // =================================================================
    //  КОМАНДЫ ДСПГ
    // =================================================================
    public bool SetAspect(Aspect a)
    {
        if (a != Aspect.Red && Pushing == null) return Refuse("сначала задайте маршрут надвига (кнопка «Надвиг с пути…»)");
        if (a == Aspect.RedN && Pushing != null && Pushing.Escort) return Refuse("идёт спуск вагонов с локомотивом — осаживать нельзя");
        if (Hump == a) return true;
        Hump = a;
        Say("Горочный светофор: " + AspectName(a));
        Changed?.Invoke();
        return true;
    }

    public static string AspectName(Aspect a) => a switch
    {
        Aspect.Green => "зелёный — роспуск с установленной скоростью",
        Aspect.YellowGreen => "жёлтый и зелёный — роспуск с промежуточной скоростью",
        Aspect.Yellow => "жёлтый — роспуск с уменьшенной скоростью",
        Aspect.RedN => "красный и «Н» — осадить вагоны с горки",
        _ => "красный — роспуск запрещён",
    };

    public bool SetPushRoute(int a)
    {
        var at = Arr[a];
        if (Pushing != null) return Refuse($"уже идёт надвиг поезда {Pushing.Name} — дождитесь конца роспуска");
        if (at.State != ArrState.Ready || at.Train == null)
            return Refuse(at.State switch
            {
                ArrState.Free => $"путь П{a} свободен — надвигать нечего",
                ArrState.Inspection => $"на пути П{a} идёт осмотр состава (ПТО) — надвигать рано",
                ArrState.WaitLoco or ArrState.LocoComing => $"к составу на пути П{a} не прицеплен горочный локомотив",
                _ => $"состав на пути П{a} не готов к роспуску",
            });
        Pushing = at.Train;
        ListokTrain = Pushing;
        at.State = ArrState.Pushing;
        SelectedCut = null;
        Say($"Задан маршрут надвига с пути П{a}: поезд {Pushing.Name}, {Pushing.WagonCount} ваг., отцепов {Pushing.AllCuts.Count}. Маневровый М{a} — лунно-белый");
        Changed?.Invoke();
        return true;
    }

    public void SetRetarder(int i, int step)
    {
        if (Ars) { Refuse("включена АРС — замедлители управляются автоматически (снимите галочку «АРС»)"); return; }
        Retarders[i].Step = Math.Clamp(step, 0, 4);
    }

    public void SetArs(bool on)
    {
        if (Ars == on) return;
        Ars = on;
        if (!on) foreach (var r in Retarders) r.Step = 0;
        Say(on ? "АРС включена: замедлители тормозят сами до рекомендуемой скорости" : "АРС выключена: замедлители — вручную (ступени 1–4, «О» — оттормозить)");
    }

    public void SetGac(bool gac)
    {
        if (GacMode == gac) return;
        GacMode = gac;
        if (gac) foreach (var t in Trains()) foreach (var c in t.AllCuts) if (c.Route == 0 && c.State == CutState.InTrain) c.Route = c.Dest;
        // [ДОБАВЛЕНО 17.11.2026] ручной набор — маршруты, которые ГАЦ уже взяла из листка, стираются: набираете вы
        if (!gac) foreach (var t in Trains()) foreach (var c in t.AllCuts) if (c.State == CutState.InTrain && !(t.Escort && t.Cuts.Contains(c))) c.Route = 0;
        Say(gac ? "Маршруты отцепов: ГАЦ — из сортировочного листка" : "Маршруты отцепов: ручной набор — кнопка пути перед каждым отцепом");
    }

    IEnumerable<Train> Trains() => Arr.Select(a => a.Train).Concat(new[] { Pushing }).Where(t => t != null).Distinct();

    /// <summary>Кнопка пути 1–8: маршрут выбранному отцепу или следующему без маршрута.</summary>
    public bool RouteButton(int k)
    {
        var t = ListokTrain;
        if (t == null) return Refuse("нет поезда в листке");
        var c = SelectedCut != null && t.AllCuts.Contains(SelectedCut) && SelectedCut.State == CutState.InTrain ? SelectedCut
              : t.AllCuts.FirstOrDefault(x => x.State == CutState.InTrain && x.Route == 0);
        if (c == null) return Refuse(GacMode ? "все отцепы уже с маршрутом (ГАЦ). Чтобы изменить — выберите отцеп в листке" : "у всех отцепов маршрут уже задан");
        c.Route = k;
        Say($"Отцеп {c.No}: маршрут на путь {k}" + (k != c.Dest ? $" (по листку — путь {c.Dest}!)" : ""));
        SelectedCut = null;
        return true;
    }

    public bool UndoRoute()
    {
        var t = ListokTrain;
        var c = t?.AllCuts.LastOrDefault(x => x.State == CutState.InTrain && x.Route != 0);
        if (c == null) return Refuse("нечего сбрасывать");
        c.Route = 0;
        Say($"Отцеп {c.No}: маршрут сброшен");
        return true;
    }

    public bool ThrowSwitch(int no)
    {
        var sw = Switches[no];
        if (sw.Throwing) return Refuse($"стрелка {no} ещё переводится");
        if (Occupied(sw.Section)) return Refuse($"стрелка {no}: стрелочный участок {sw.Section.Id} занят — переводить нельзя");
        StartThrow(sw, !sw.Plus);
        Say($"Стрелка {no} переводится {(sw.ThrowTo ? "в плюс" : "в минус")} (на пути {sw.Range(sw.ThrowTo)})");
        return true;
    }

    void StartThrow(Switch sw, bool to)
    {
        sw.ThrowTo = to;
        sw.ThrowLeft = 0.6;
    }

    /// <summary>«Спустить с локомотивом»: вагоны впереди состава не распускаются, а спускаются локомотивом на свой путь.</summary>
    public bool StartEscort()
    {
        var t = Pushing;
        if (t == null || t.Cuts.Count == 0) return Refuse("нет состава на горке");
        if (t.Escort) return true;
        var front = t.Cuts[0];
        if (front.Head - front.Length / 2 > 0) return Refuse("первый отцеп уже переваливает горб");
        int rest = t.Cuts.Sum(c => c.Wagons.Count);
        if (rest > 6) return Refuse($"в составе ещё {rest} вагонов — с локомотивом спускают только оставшиеся запрещённые к роспуску вагоны (не больше 6)");
        if (front.Route == 0) return Refuse($"отцеп {front.No}: сначала задайте маршрут (кнопка пути)");
        t.Escort = true;
        // все оставшиеся отцепы идут одной группой на путь первого
        Say($"Спуск с локомотивом: {rest} ваг. на путь {front.Route}, не быстрее 5 км/ч");
        return true;
    }

    /// <summary>Протолкнуть остановившийся в стрелочной зоне (на замедлителе) отцеп на путь.</summary>
    public bool PushStuck()
    {
        var c = Cuts.Where(x => x.State == CutState.Stopped && x.Head < Lay.STrack).OrderByDescending(x => x.Head).FirstOrDefault();
        if (c == null) return Refuse("нет отцепов, остановившихся в стрелочной зоне");
        if (Pushing != null && Pushing.V > 0.05 && !Pushing.Escort) return Refuse("сначала остановите надвиг (красный на горочном светофоре)");
        c.State = CutState.Forced;
        c.ForcedV = 1.7;
        Say($"Отцеп {c.No} проталкивается маневровым локомотивом на путь (6 км/ч)");
        return true;
    }

    /// <summary>Сжать (осадить) вагоны на пути k — закрыть «окна» после недоката.</summary>
    public bool CompactTrack(int k)
    {
        var on = OnTrack(k).ToList();
        if (on.Count == 0) return Refuse($"путь {k} свободен");
        if (Cuts.Any(c => c.State == CutState.Rolling && c.Lo <= k && c.Hi >= k)) return Refuse($"на путь {k} скатывается отцеп — подождите");
        if (on.Count == 1 && Lay.SEnd - on[0].Head < 30) return Refuse($"на пути {k} окон нет");
        foreach (var c in on) { c.State = CutState.Forced; c.ForcedV = 1.7; }
        Say($"Путь {k}: вагоны осаживаются (сжимаются) к концу пути маневровым локомотивом");
        return true;
    }

    public bool SendHumpLoco(int a)
    {
        if (Loco != HumpLocoState.Standby) return Refuse(Loco switch
        {
            HumpLocoState.AtHorb => "горочный локомотив на горбу — сначала верните его («Вернуть горочный локомотив»)",
            HumpLocoState.Coupled => "горочный локомотив прицеплен к другому составу",
            _ => "горочный локомотив занят (в пути)",
        });
        var at = Arr[a];
        if (at.State != ArrState.WaitLoco) return Refuse(at.State == ArrState.Inspection ? $"на пути П{a} ещё идёт осмотр" : $"на пути П{a} нет состава, ждущего горочный локомотив");
        var t = at.Train;
        at.State = ArrState.LocoComing;
        Loco = HumpLocoState.ToTrain;
        LocoTrain = t;
        var tail = Lay.At(t.LocoFront - 1, 1, a);
        var m = new Mover { Pts = Lay.HumpLocoToTail(a, tail.X), Speed = 22 };
        m.Vehicles.Add(((float)(Rolling.TEM18.Length * Lay.SceneScale) * 1.4f, Rolling.TEM18, null));
        m.Done = () =>
        {
            t.HumpLoco = true;
            Loco = HumpLocoState.Coupled;
            at.State = ArrState.Ready;
            t.ReadySince = T;
            if (ListokTrain == null || ListokTrain.Cuts.Count == 0) ListokTrain = t;
            Say($"Горочный локомотив прицеплен к составу {t.Name} на пути П{a} — состав готов к роспуску");
        };
        Movers.Add(m);
        Say($"Горочный локомотив ТЭМ18 идёт по обходному пути в хвост состава {t.Name} (путь П{a})");
        return true;
    }

    public bool ReturnHumpLoco()
    {
        if (Loco != HumpLocoState.AtHorb) return Refuse(Loco == HumpLocoState.Standby ? "горочный локомотив уже на стоянке" : "горочный локомотив не на горбу");
        Loco = HumpLocoState.Returning;
        var m = new Mover { Pts = Lay.HumpLocoHome(Lay.At(LocoS, 1, LocoArr)), Speed = 22 };
        m.Vehicles.Add(((float)(Rolling.TEM18.Length * Lay.SceneScale) * 1.4f, Rolling.TEM18, null));
        m.Done = () => { Loco = HumpLocoState.Standby; Say("Горочный локомотив на стоянке у горки"); };
        Movers.Add(m);
        Say("Горочный локомотив возвращается на стоянку");
        return true;
    }

    /// <summary>Вывести вагоны с пути k в парк отправления.</summary>
    public bool FormTrain(int k, bool auto = false)
    {
        var on = OnTrack(k).ToList();
        if (on.Count == 0) return auto ? false : Refuse($"путь {k} свободен");
        if (ShuntBusy) return auto ? false : Refuse("маневровый локомотив парка отправления занят");
        if (Cuts.Any(c => c.Active && c.State != CutState.Stopped && c.Lo <= k && c.Hi >= k)) return auto ? false : Refuse($"на путь {k} идёт отцеп — подождите");
        if (Pushing?.Escort == true && Pushing.Cuts.Count > 0 && Pushing.Cuts[0].Route == k) return auto ? false : Refuse($"на путь {k} спускаются вагоны с локомотивом");
        var d = Dep.Skip(1).FirstOrDefault(x => x.State == DepState.Free);
        if (d == null) return auto ? false : Refuse("в парке отправления нет свободного пути");
        // [ДОБАВЛЕНО 26.11.2026] учебный: сначала снять башмак; неполный состав — замечание
        if (Strict && Shoe[k]) return Refuse($"на пути {k} лежит тормозной башмак — сначала снимите его");
        int nw = on.Sum(c => c.Wagons.Count);
        if (Strict && nw < Norm(k)) Violate($"состав с пути {k} выведен неполным: {nw} ваг. при норме {Norm(k)}", 2);
        shoeWarned[k] = overflowWarned[k] = false;
        var wagons = on.OrderByDescending(c => c.Head).SelectMany(c => c.Wagons).ToList();
        foreach (var c in on) c.State = CutState.Gone;
        d.State = DepState.Filling;
        d.Late = false;
        d.From = k;
        d.TrainNo = nextDepNo;
        nextDepNo += 2;
        ShuntBusy = true;
        Formed++;
        var pts = Lay.ToDeparture(k, d.No);
        double headPos = (on.Max(c => c.Head) - Lay.STrack) / (Lay.SEnd - Lay.STrack) * Mover.Dist(pts[0], pts[1]);
        var m = new Mover { Pts = pts, Speed = 16, Pos = headPos };
        m.Vehicles.Add(((float)(Rolling.TEM18.Length * Lay.SceneScale), Rolling.TEM18, null));
        foreach (var w in wagons) m.Vehicles.Add(((float)(w.Length * Lay.SceneScale), null, w));
        m.Done = () =>
        {
            d.Wagons.Clear();
            d.Wagons.AddRange(wagons);
            d.State = DepState.WaitLoco;
            d.Until = T + 15;
            ShuntBusy = false;
            Say($"Состав с пути {k} ({wagons.Count} ваг., на {TrackNames[k]}) выведен на путь О{d.No} парка отправления");
        };
        Movers.Add(m);
        Say($"Маневровый локомотив выводит {wagons.Count} ваг. с пути {k} в парк отправления (путь О{d.No})");
        return true;
    }

    // =================================================================
    //  ЗАНЯТОСТЬ
    // =================================================================
    public IEnumerable<Cut> OnTrack(int k) => Cuts.Where(c => c.State is CutState.Stopped or CutState.Forced && c.Track == k && c.Tail >= Lay.STrack - 1);

    public IEnumerable<(double s0, double s1, int lo, int hi)> Bodies()
    {
        foreach (var c in Cuts) if (c.Active) yield return (c.Tail, c.Head, c.Lo, c.Hi);
        if (Pushing != null)
        {
            foreach (var c in Pushing.Cuts) yield return (c.Tail, c.Head, Pushing.Lo, Pushing.Hi);
            if (Pushing.HumpLoco) yield return (Pushing.LocoFront - Rolling.TEM18.Length, Pushing.LocoFront, Pushing.Lo, Pushing.Hi);
        }
        if (Loco == HumpLocoState.AtHorb) yield return (LocoS - Rolling.TEM18.Length, LocoS, 1, Lay.Tracks);
    }

    public bool Occupied(Edge e) => Bodies().Any(b => b.s1 > e.S0 && b.s0 < e.S1 && b.lo <= e.Hi && b.hi >= e.Lo);

    // =================================================================
    //  ШАГ МОДЕЛИ
    // =================================================================
    public void Tick(double dt)
    {
        while (dt > 1e-6)
        {
            double h = Math.Min(dt, 0.05);
            Step(h);
            dt -= h;
        }
    }

    void Step(double dt)
    {
        T += dt;
        foreach (var sw in Switches.Skip(1))
            if (sw.Throwing && (sw.ThrowLeft -= dt) <= 0) { sw.ThrowLeft = 0; sw.Plus = sw.ThrowTo; }
        StepPush(dt);
        StepCuts(dt);
        Collisions();
        Gac();
        StepRetarders();
        StepOps(dt);
        if (Strict) StepChecks();
        if (Autopilot) Pilot();
        StepMovers(dt);
    }

    // ---- надвиг ----
    void StepPush(double dt)
    {
        var t = Pushing;
        if (t == null) return;
        double target = Hump switch
        {
            Aspect.Green => PushSpeed[3],
            Aspect.YellowGreen => PushSpeed[2],
            Aspect.Yellow => PushSpeed[1],
            Aspect.RedN => -0.8,
            _ => 0,
        };
        // пока первый вагон не дошёл до горочного светофора, состав подтягивают быстрее (до 9 км/ч)
        if (target > 0 && !t.Escort && t.Head < -22) target = Math.Max(target, 2.5);   // [ИЗМЕНЕНО 17.11.2026] до −22 м (было −75): роспуск начинается быстрее
        if (t.Escort) target = t.Returning ? -1.5 : Hump is Aspect.Red or Aspect.RedN ? 0 : Math.Min(target, 1.2);
        double acc = Math.Abs(target) > Math.Abs(t.V) && Math.Sign(target) == Math.Sign(t.V == 0 ? target : t.V) ? 0.15 : 0.3;
        if (t.V < target) t.V = Math.Min(target, t.V + acc * dt);
        else if (t.V > target) t.V = Math.Max(target, t.V - acc * dt);

        double oldHead = t.Head;
        double nh = t.Head + t.V * dt;
        if (!t.Escort)
        {
            // горочный локомотив через горб не идёт
            double locoFront = nh - t.CutsLength;
            if (locoFront > Lay.LocoLimit) { nh = Lay.LocoLimit + t.CutsLength; t.V = 0; }
            // осаживать дальше конца парка приёма нельзя
            if (nh - t.CutsLength - Rolling.TEM18.Length < Lay.SArrTail + 5) { nh = Lay.SArrTail + 5 + t.CutsLength + Rolling.TEM18.Length; t.V = 0; }
        }
        t.Head = nh;
        if (t.Escort) PassSwitches(t.Passed, ref t.Lo, ref t.Hi, oldHead, t.Head, null);
        LayoutTrain(t);

        // путь парка приёма освободился, когда хвост (локомотив) ушёл за горловину
        var at = Arr[t.Arr];
        if (at.Train == t && t.LocoFront - Rolling.TEM18.Length > Lay.SArrFront + 5)
        {
            at.Train = null;
            at.State = ArrState.Free;
            Say($"Путь П{t.Arr} парка приёма свободен");
        }

        if (t.Escort) { StepEscort(t); return; }

        // расцепка: отцеп, перевалив горб (центр за вершиной), уходит сам
        while (t.Cuts.Count > 0)
        {
            var c = t.Cuts[0];
            if (c.Head - c.Length / 2 < 0.5 || t.V < 0) break;
            t.Cuts.RemoveAt(0);
            c.State = CutState.Rolling;
            c.V = t.V;
            c.Lo = 1; c.Hi = Lay.Tracks;
            Cuts.Add(c);
            CutsRolled++;
            Rolled += c.Wagons.Count;
            if (c.Forbidden)
                Violate($"отцеп {c.No} распущен с горки, а в листке отметка «{c.Mark}» ({c.Wagons[0].Kind.Name}) — такие вагоны спускают только с локомотивом", 15);
            Say($"Отцеп {c.No} ({c.Wagons.Count} ваг., {c.KindsText}) пошёл с горки" + (c.Route > 0 ? $" → путь {c.Route}" : " — маршрут не задан!"));
            t.Head = t.Cuts.Count > 0 ? t.Cuts[0].Head : c.Tail;   // вагонов нет — голова = локомотив
        }
        if (t.Cuts.Count == 0) FinishPush(t);
    }

    void StepEscort(Train t)
    {
        if (t.Returning)
        {
            if (t.Head <= Lay.LocoLimit) { t.V = 0; t.Head = Lay.LocoLimit; FinishPush(t); }
            return;
        }
        var front = t.Cuts[0];
        // впереди вагоны? — сцепиться
        var ahead = Cuts.Where(c => c.Active && c.Tail > t.Head - 1 && Overlap(c.Lo, c.Hi, t.Lo, t.Hi)).OrderBy(c => c.Tail).FirstOrDefault();
        bool end = t.Head >= Lay.SEnd - 0.5;
        if ((ahead != null && ahead.Tail - t.Head <= 0.3) || end)
        {
            var wagons = t.Cuts.SelectMany(c => c.Wagons).ToList();
            if (ahead != null && !end)
            {
                ahead.Wagons.AddRange(wagons);
                if (ahead.State == CutState.Rolling) ahead.State = CutState.Stopped;
            }
            else
            {
                var c = new Cut { No = front.No, Dest = front.Dest, Route = front.Route, TrainNo = t.No, State = CutState.Stopped, Head = t.Head, Lo = t.Lo, Hi = t.Hi, Track = t.Lo == t.Hi ? t.Lo : 0, StopChecked = true };
                c.Wagons.AddRange(wagons);
                foreach (var p in t.Passed) c.Passed[p.Key] = p.Value;
                Cuts.Add(c);
            }
            int track = t.Lo == t.Hi ? t.Lo : 0;
            foreach (var c in t.Cuts) { c.State = CutState.Merged; c.Track = track; }
            CheckTrack(front, track);
            Rolled += wagons.Count;
            CutsRolled += t.Cuts.Count;
            t.Cuts.Clear();
            TrainsRolled++;
            t.Returning = true;
            t.Head = t.LocoFront;
            Say($"Вагоны спущены с локомотивом на путь {track} и сцеплены. Горочный локомотив возвращается на горб");
        }
    }

    void FinishPush(Train t)
    {
        Pushing = null;
        Hump = Aspect.Red;
        Loco = HumpLocoState.AtHorb;
        LocoS = t.Head;
        LocoArr = t.Arr;
        LocoTrain = null;
        var at = Arr[t.Arr];
        if (at.Train == t) { at.Train = null; at.State = ArrState.Free; }
        if (!t.Escort) TrainsRolled++;      // со спуском локомотивом — засчитан, когда вагоны сцеплены на пути
        Say($"Роспуск поезда {t.Name} окончен. Горочный светофор — красный. Горочный локомотив на горбу");
        if (AutoOps) returnAt = T + 8;
        Changed?.Invoke();
    }
    double returnAt = -1;

    // ---- свободное скатывание отцепов ----
    static bool Overlap(int a0, int a1, int b0, int b1) => a0 <= b1 && b0 <= a1;

    double Resist(Cut c)
    {
        double s = c.Head - c.Length / 2;
        double w = c.W0 + 0.03 * c.V * c.V;
        if (Lay.InSwitchZone(s)) w += Lay.SwitchExtra;
        return w;
    }

    double MeanGrade(Cut c) => (Lay.Grade(c.Head) + Lay.Grade(c.Head - c.Length / 2) + Lay.Grade(c.Tail)) / 3;

    double Brake(Cut c)
    {
        double b = 0;
        foreach (var r in Retarders)
        {
            if (r.Step == 0 || !Overlap(r.Lo, r.Hi, c.Lo, c.Hi)) continue;
            double ov = Math.Min(c.Head, r.S1) - Math.Max(c.Tail, r.S0);
            if (ov > 0) b += Lay.StepBrake[r.Step] * Math.Min(1, ov / c.Length);
        }
        return b;
    }

    void StepCuts(double dt)
    {
        foreach (var c in Cuts.ToList())
        {
            if (!c.Active) continue;
            double old = c.Head;
            if (c.State == CutState.Forced)
            {
                c.V = c.ForcedV;
                c.Head += c.V * dt;
            }
            else
            {
                double i = MeanGrade(c), w = Resist(c), b = Brake(c);
                if (c.State == CutState.Stopped)
                {
                    if (i - w - b - 1.5 > 0) { c.State = CutState.Rolling; c.V = 0.02; c.StopChecked = false; }
                    else continue;
                }
                double a = Lay.G * (i - w - b) / 1000;
                c.V += a * dt;
                if (c.ShoeStop > 0) c.V -= 1.2 * dt;            // [26.11.2026] юзом на тормозном башмаке
                if (c.V <= 0)
                {
                    c.V = 0;
                    c.State = CutState.Stopped;
                    OnStop(c);
                    continue;
                }
                c.Head += c.V * dt;
                if (Strict && ShoeHit(c)) continue;
            }
#if SORTTEST
            if (Lay.Cur.Builtin && old - c.Length < 120 && c.Head - c.Length >= 120) { var rr = Retarders[c.Lo <= 4 ? 1 : 2]; Console.WriteLine($"      [выход 2ТП] t={T:0} {c.No} v={c.V * 3.6:0.0} цель={TargetSpeed(rr, c) * 3.6:0.0} ступень={rr.Step} n={c.Wagons.Count}"); }
#endif
            PassSwitches(c.Passed, ref c.Lo, ref c.Hi, old, c.Head, c);
            if (c.Track == 0 && c.Lo == c.Hi && c.Tail > Lay.STrack - 30 && c.Head >= Lay.STrack)
            {
                c.Track = c.Lo;
                CheckTrack(c, c.Track);
                // [ДОБАВЛЕНО 26.11.2026] учебный: на свободный путь — только с башмаком (иначе вагоны не закреплены)
                if (Strict && !Shoe[c.Track] && !shoeWarned[c.Track] && !Cuts.Any(x => x != c && x.Active && x.Track == c.Track))
                {
                    shoeWarned[c.Track] = true;
                    Violate($"отцеп {c.No} пошёл на свободный путь {c.Track} без тормозного башмака — вагоны не закреплены", 2);
                }
            }
            if (c.Head >= Lay.SEnd)
            {
                c.Head = Lay.SEnd;
                if (c.V > MaxCouple && c.State == CutState.Rolling)
                    Violate($"отцеп {c.No} подошёл к концу пути {c.Track} со скоростью {c.V * 3.6:0.0} км/ч — выход за предельный столбик", 5);
                c.V = 0;
                c.State = CutState.Stopped;
                c.StopChecked = true;
            }
        }
    }

    void CheckTrack(Cut c, int track)
    {
        var wrong = c.Wagons.Where(w => w.Dest != track).ToList();
        if (wrong.Count == 0) { CorrectCuts++; return; }
        Violate($"отцеп {c.No} пришёл на путь {track}, а по листку — путь {string.Join(", ", wrong.Select(w => w.Dest).Distinct())}", 5);
    }

    /// <summary>Голова прошла остряки стрелки — дальше отцеп идёт туда, куда она лежит.</summary>
    void PassSwitches(Dictionary<int, bool> passed, ref int lo, ref int hi, double s0, double s1, Cut c)
    {
        foreach (var sw in Switches.Skip(1))
        {
            if (passed.ContainsKey(sw.No) || lo < sw.Lo || hi > sw.Hi || !(s0 < sw.S && s1 >= sw.S)) continue;
            bool plus = sw.Plus;
            if (sw.Throwing)
                Violate($"стрелка {sw.No} переводилась, когда на неё зашёл {(c != null ? "отцеп " + c.No : "состав")} — перевод под вагоном (возможен сход)", 10);
            if (c != null && c.Route == 0 && sw.No == 1)
                Violate($"отцеп {c.No} пошёл без маршрута — стрелки лежат как попало", 3);
#if SORTTEST
            if (c != null && c.Route >= sw.Lo && c.Route <= sw.Hi && sw.Toward(c.Route) != plus)
                Console.WriteLine($"      [мимо] t={T:0} отцеп {c.No} route{c.Route} стрелка {sw.No} plus={plus} throwing={sw.Throwing} занята={Occupied(sw.Section)} кто: " +
                    string.Join(",", Cuts.Where(x => x != c && x.Active && x.Head > sw.Section.S0 && x.Tail < sw.Section.S1 && x.Lo <= sw.Hi && x.Hi >= sw.Lo).Select(x => $"{x.No}[{x.Tail:0}-{x.Head:0} v{x.V * 3.6:0.0}]")) + $" сам v{c.V * 3.6:0.0}");
#endif
            passed[sw.No] = plus;
            // [ИЗМЕНЕНО 24.11.2026] куда ведёт положение — у стрелки (у горки из файла «плюс» может вести и вниз)
            if (plus) { lo = Math.Max(lo, sw.PlusLo); hi = Math.Min(hi, sw.PlusHi); }
            else { lo = Math.Max(lo, sw.MinusLo); hi = Math.Min(hi, sw.MinusHi); }
        }
    }

    void OnStop(Cut c)
    {
        if (c.StopChecked) return;
        c.StopChecked = true;
        if (c.Head < Lay.STrack)
        {
            bool onRet = Retarders.Any(r => r.Step > 0 && Overlap(r.Lo, r.Hi, c.Lo, c.Hi) && c.Head > r.S0 && c.Tail < r.S1);
            if (onRet) Violate($"отцеп {c.No} остановлен на тормозной позиции — слишком сильное торможение", 3);
            else Violate($"отцеп {c.No} остановился в стрелочной зоне (s = {c.Head:0} м) — «Протолкнуть отцеп»", 5);
            return;
        }
        double point = Lay.SEnd;
        var ahead = Cuts.Where(x => x != c && x.Active && x.Track == c.Track && x.Tail >= c.Head - 0.5).OrderBy(x => x.Tail).FirstOrDefault();
        if (ahead != null) point = ahead.Tail;
        double gap = point - c.Head;
        // на пустом пути первый отцеп может не докатиться до конца — это не ошибка, если не очень далеко
        if (gap > (ahead != null ? 50 : 100)) Violate($"недокат: отцеп {c.No} остановился на пути {c.Track}, не дойдя {gap:0} м до {(ahead != null ? "вагонов" : "конца пути")} — «окно»", 2);
        else Say($"Отцеп {c.No} остановился на пути {c.Track}" + (gap > 1 ? $" (окно {gap:0} м)" : ""));
    }

    void Collisions()
    {
        bool again = true;
        while (again)
        {
            again = false;
            var act = Cuts.Where(c => c.Active).OrderBy(c => c.Head).ToList();
            for (int i = 0; i < act.Count && !again; i++)
                for (int j = i + 1; j < act.Count && !again; j++)
                {
                    Cut a = act[i], b = act[j];          // a — сзади (ниже отметка), b — впереди
                    if (!Overlap(a.Lo, a.Hi, b.Lo, b.Hi) || a.Head < b.Tail) continue;
                    if (a.Head > b.Head) continue;
                    double rel = (a.V - b.V) * 3.6;
                    bool forced = a.State == CutState.Forced || b.State == CutState.Forced;
                    if (!forced)
                    {
                        if (b.Head < Lay.STrack && a.Head < Lay.STrack)
                            Violate($"нагон: отцеп {a.No} догнал отцеп {b.No} в стрелочной зоне — оба пойдут на один путь", 5);
                        else if (rel > 5)
                            Violate($"удар: отцеп {a.No} сцепился с вагонами на пути {Math.Max(a.Track, b.Track)} со скоростью {rel:0.0} км/ч (не больше 5)", rel > 8 ? 10 : 5);
                    }
#if SORTTEST
                    if (!forced && rel > 5) Console.WriteLine($"      [удар] t={T:0} a{a.No} v={a.V * 3.6:0.0} s={a.Head:0} lo{a.Lo}-{a.Hi} route{a.Route} W0={a.W0:0.0} n={a.Wagons.Count} | b{b.No} v={b.V * 3.6:0.0} {b.State} tail={b.Tail:0} track{b.Track} n={b.Wagons.Count}");
#endif
                    double m1 = a.Mass, m2 = b.Mass;
                    b.V = (m1 * a.V + m2 * b.V) / (m1 + m2);
                    b.Wagons.AddRange(a.Wagons);
                    if (b.Track == 0 && a.Track > 0) b.Track = a.Track;
                    a.State = CutState.Merged;
                    if (b.State == CutState.Forced && a.State != CutState.Forced) { }
                    if (forced) { b.State = CutState.Stopped; b.V = 0; b.StopChecked = true; }
                    else if (b.V < 0.05) { b.V = 0; b.State = CutState.Stopped; b.StopChecked = true; }
                    else b.State = CutState.Rolling;
                    if (!forced && rel <= 5) Say($"Отцеп {a.No} сцепился с вагонами ({Math.Max(rel, 0):0.0} км/ч)");
                    again = true;
                }
        }
        Cuts.RemoveAll(c => c.State is CutState.Merged or CutState.Gone);
    }

    // ---- ГАЦ: стрелка переводится под маршрут ближайшего подходящего отцепа ----
    void Gac()
    {
        foreach (var sw in Switches.Skip(1))
        {
            if (sw.Throwing || Occupied(sw.Section)) continue;
            int route = 0;
            double best = double.NegativeInfinity;
            foreach (var c in Cuts)
                if (c.Active && c.Head < sw.S && c.Head > best && c.Lo >= sw.Lo && c.Hi <= sw.Hi && !c.Passed.ContainsKey(sw.No))
                { best = c.Head; route = c.Route; }
            if (Pushing != null && Pushing.Cuts.Count > 0 && Pushing.Head < sw.S && Pushing.Head > best && Pushing.Lo >= sw.Lo && Pushing.Hi <= sw.Hi)
            { best = Pushing.Head; route = Pushing.Cuts[0].Route; }
            if (route < sw.Lo || route > sw.Hi) continue;
            bool want = sw.Toward(route);
            if (sw.Plus != want) StartThrow(sw, want);
        }
    }

    // ---- замедлители: что на них, рекомендуемая скорость, АРС ----
    void StepRetarders()
    {
        foreach (var r in Retarders)
        {
            Cut on = null, near = null;
            double bestOv = 0, nearHead = double.NegativeInfinity;
            foreach (var c in Cuts)
            {
                if (!c.Active || !Overlap(r.Lo, r.Hi, c.Lo, c.Hi)) continue;
                double ov = Math.Min(c.Head, r.S1) - Math.Max(c.Tail, r.S0);
                if (ov > bestOv) { bestOv = ov; on = c; }
                if (c.Head < r.S0 && c.Head > nearHead && c.State == CutState.Rolling) { nearHead = c.Head; near = c; }
            }
            if (on == null && near == null && Pushing != null && Pushing.Cuts.Count > 0 && !Pushing.Escort && r.Lo == 1 && r.Hi == Lay.Tracks) near = Pushing.Cuts[0];
            r.Cut = on ?? near;
            r.CutOn = on != null;
            r.Target = r.Cut != null ? TargetSpeed(r, r.Cut) : 0;
            r.Advice = 0;
            if (on == null || on.State == CutState.Forced) { if (Ars) r.Step = 0; continue; }
            // самая слабая ступень, с которой отцеп к выходу с замедлителя (когда сойдёт хвост) будет не быстрее рекомендуемой
            // [ИЗМЕНЕНО 17.11.2026] считается и без АРС — подсказка студенту «тормози сильно / слабо / отпусти»
            if (on.V <= r.Target) { if (Ars) r.Step = 0; continue; }
            // полная сила — пока голова на замедлителе, дальше (сходит хвост) — примерно половина
            double left = Math.Max(1, r.S1 - Math.Max(on.Head, r.S0) + 0.5 * Math.Min(on.Length, r.S1 - r.S0));
            double aim = r.Target - 0.1;
            double frac = Math.Min(1, Math.Min(on.Length, r.S1 - r.S0) / on.Length);
            double free = MeanGrade(on) - Resist(on);
            int step = 4;
            for (int n = 1; n <= 4; n++)
            {
                double a = Lay.G * (Lay.StepBrake[n] * frac - free) / 1000;
                if (on.V * on.V - 2 * a * left <= aim * aim) { step = n; break; }
            }
            r.Advice = step;
            if (Ars) r.Step = step;
        }
    }

    /// <summary>Какая скорость нужна отцепу, когда голова в точке from, чтобы голова дошла до точки to со скоростью vEnd.
    /// Считается назад от to по той же физике, что и движение (уклон под отцепом, сопротивление, стрелки), без торможения.</summary>
    double SpeedNeeded(Cut c, double from, double to, double vEnd)
    {
        double v2 = vEnd * vEnd, len = c.Length;
        for (double s = to; s > from; s -= 1)
        {
            double i = (Lay.Grade(s) + Lay.Grade(s - len / 2) + Lay.Grade(s - len)) / 3;
            double w = c.W0 + 0.03 * v2 + (Lay.InSwitchZone(s - len / 2) ? Lay.SwitchExtra : 0);
            v2 += 2 * Lay.G * (w - i) / 1000;
            if (v2 < 0.04) v2 = 0.04;
        }
        return Math.Sqrt(v2);
    }

    /// <summary>Скорость отцепа в момент, когда его хвост сходит с замедлителя, чтобы он подошёл к стоящим вагонам со скоростью ~2,7 км/ч.</summary>
    public double TargetSpeed(Retarder r, Cut c)
    {
        int track = TrackOf(c);
        double point = Lay.SEnd;
        var standing = OnTrack(track).Where(x => x != c).ToList();
        if (standing.Count > 0) point = standing.Min(x => x.Tail);
        // отцепы, которые сейчас катятся на тот же путь впереди, встанут к стоящим — место займут они
        var rollingAhead = Cuts.Where(x => x != c && x.State is CutState.Rolling or CutState.Forced && x.Head > c.Head && !standing.Contains(x)
                                 && (x.Lo == x.Hi ? x.Lo == track : x.Route == track && x.Lo <= track && track <= x.Hi)).ToList();
        point -= rollingAhead.Sum(x => x.Length);
        point -= 0.5;
        double exit = r.S1 + c.Length;
        // меньше этой скорости нельзя: отцеп остановится, не войдя на свой путь
        double lowMin = SpeedNeeded(c, exit, Math.Max(Lay.STrack + c.Length + 25, exit + 1), CoupleSpeed);
        // [ИЗМЕНЕНО 24.11.2026] дальше по пути этого отцепа есть ещё тормозная позиция (как 2ТП после 1ТП) — целимся в неё;
        // нет — в стоящие вагоны (было: «1ТП — для путей 1–8, остальные — 2ТП»; у встроенной горки то же самое)
        var r2 = NextRetarder(r, track);
        if (r2 == null)
        {
            double t2 = SpeedNeeded(c, exit, Math.Max(point, exit + 1), CoupleSpeed);
            // [ДОБАВЛЕНО 17.11.2026] интервал на 2ТП: впереди на общих стрелках (до входа на путь) отцеп ближе 40 м —
            // не догонять его (пути короче, отцепы чаще одиночные — без этого нагоны и «не на свой путь»)
            var lead = Leader(c, 40);
            if (lead != null) t2 = Math.Min(t2, Math.Max(lead.V - 0.3, lowMin));
            return t2;
        }
        // 1ТП: чтобы к 2ТП отцеп пришёл с запасом на торможение, но не быстрее 15 км/ч (интервалы на стрелках)
        double need2 = TargetSpeed(r2, c);
        double low = SpeedNeeded(c, exit, r2.S0, need2);                 // меньше — не дойдёт до своего пути
        double tgt = Math.Max(SpeedNeeded(c, exit, r2.S0, need2 + 0.6), 4.2);
        // интервал: впереди в стрелочной зоне отцеп ближе 45 м — придержать, чтобы не догнать его на стрелках
        var prev = Cuts.Where(x => x != c && x.Active && x.Head > c.Head && x.Tail < Lay.STrack).OrderBy(x => x.Tail).FirstOrDefault();
        if (prev != null && prev.Tail - c.Head < 45) tgt = Math.Min(tgt, Math.Max(prev.V - 0.4, 0));
        return Math.Max(low, tgt);
    }

    /// <summary>Следующая тормозная позиция после r на пути track (null — нет).</summary>
    Retarder NextRetarder(Retarder r, int track) =>
        Retarders.Where(x => x != r && x.S0 >= r.S1 - 0.1 && x.Lo <= track && track <= x.Hi).OrderBy(x => x.S0).FirstOrDefault();

    int TrackOf(Cut c) => c.Route > 0 && c.Route >= c.Lo && c.Route <= c.Hi ? c.Route : c.Lo == c.Hi ? c.Lo : c.Dest;

    /// <summary>Ближайший отцеп впереди, с которым у c ещё общие стрелки (его хвост не вошёл на путь), ближе gap метров.</summary>
    Cut Leader(Cut c, double gap)
    {
        int tr = TrackOf(c);
        return Cuts.Where(x => x != c && x.Active && x.State != CutState.Stopped && x.Head > c.Head && x.Tail < Lay.STrack + 5
                               && x.Tail - c.Head < gap && x.Lo <= tr && tr <= x.Hi)
                   .OrderBy(x => x.Tail).FirstOrDefault();
    }

    // ---- приём, осмотр, горочный локомотив, отправление ----
    void StepOps(double dt)
    {
        // приём поезда
        if (ManualArrivalStep()) { }
        else if (T >= nextArrival)
        {
            var free = Arr.Skip(1).FirstOrDefault(a => a.State == ArrState.Free && a.Train == null && !PushOccupies(a.No));
            if (free != null) Arrive(free);
            nextArrival = T + (free != null ? 150 + rnd.Next(0, 120) : 30);
        }
        foreach (var a in Arr.Skip(1))
        {
            switch (a.State)
            {
                case ArrState.Uncoupling when T >= a.Until:
                    UncoupleTrainLoco(a);
                    break;
                case ArrState.Inspection when T >= a.Until:
                    a.State = ArrState.WaitLoco;
                    Say($"Путь П{a.No}: осмотр окончен, тормоза отпущены, сортировочный листок готов — состав ждёт горочный локомотив");
                    break;
            }
        }
        if (AutoOps)
        {
            if (Loco == HumpLocoState.Standby)
            {
                var w = Arr.Skip(1).Where(a => a.State == ArrState.WaitLoco).OrderBy(a => a.Until).FirstOrDefault();
                if (w != null) SendHumpLoco(w.No);
            }
            if (Loco == HumpLocoState.AtHorb && returnAt >= 0 && T >= returnAt) { returnAt = -1; ReturnHumpLoco(); }
            if (!ShuntBusy)
                for (int k = 1; k <= Lay.Tracks; k++)
                    if (OnTrack(k).Sum(c => c.Wagons.Count) >= Norm(k) && OnTrack(k).All(c => c.State == CutState.Stopped) && FormTrain(k, true)) break;
        }
        foreach (var d in Dep.Skip(1))
        {
            switch (d.State)
            {
                case DepState.WaitLoco when T >= d.Until && !Strict:
                    SendTrainLoco(d);
                    break;
                case DepState.BrakeTest when T >= d.Until:
                    if (Strict)
                    {
                        d.State = DepState.Ready;
                        d.Until = T;
                        Say($"Путь О{d.No}: тормоза опробованы, поезд №{d.TrainNo} готов к отправлению — отправьте его");
                    }
                    else Depart(d);
                    break;
            }
        }
    }

    void SendTrainLoco(DepTrack d)
    {
        d.State = DepState.LocoComing;
        d.Loco = Rolling.Train[rnd.Next(Rolling.Train.Length)];
        var m = new Mover { Pts = Lay.DepotToDeparture(d.No), Speed = 30 };
        m.Vehicles.Add(((float)(d.Loco.Length * Lay.SceneScale) * 1.3f, d.Loco, null));
        m.Done = () => { d.State = DepState.BrakeTest; d.Until = T + 60; Say($"Путь О{d.No}: поездной локомотив {d.Loco.Series} прицеплен, опробование автотормозов"); };
        Movers.Add(m);
    }

    // =================================================================
    //  [ДОБАВЛЕНО 26.11.2026] «УЧЕБНЫЙ» И «ЭКЗАМЕН»: ПРИЁМ, ОТПРАВЛЕНИЕ, БАШМАКИ, ПРОВЕРКИ
    // =================================================================
    /// <summary>Поезд подходит — ждёт команды «Принять на П…». true — приём в этом варианте ручной.</summary>
    bool ManualArrivalStep()
    {
        if (!Strict) return false;
        if (Approaching == null && T >= nextArrival)
        {
            Approaching = MakeTrain(0);
            approachSince = T;
            approachLate = false;
            Say($"ДСП: поезд {Approaching.Name} ({Approaching.WagonCount} ваг.) подходит к станции — назначьте путь парка приёма");
        }
        return true;
    }

    /// <summary>Принять подходящий поезд на путь a парка приёма.</summary>
    public bool AcceptTrain(int a)
    {
        if (!Strict) return Refuse("приём поездов сам — в этом варианте его делает ДСП станции");
        if (Approaching == null) return Refuse("поездов на подходе нет");
        var at = Arr[a];
        if (at.State != ArrState.Free || at.Train != null || PushOccupies(a)) return Refuse($"путь П{a} парка приёма занят");
        var t = Approaching;
        Approaching = null;
        t.Arr = a;
        Arrive(at, t);
        nextArrival = T + 150 + rnd.Next(0, 120);
        return true;
    }

    /// <summary>Заказать поездной локомотив под состав на пути d парка отправления.</summary>
    public bool OrderTrainLoco(int d)
    {
        var x = Dep[d];
        if (x.State != DepState.WaitLoco) return Refuse(x.State switch
        {
            DepState.Free => $"путь О{d} свободен",
            DepState.Filling => $"на путь О{d} ещё выводят состав",
            _ => $"локомотив на путь О{d} уже заказан",
        });
        SendTrainLoco(x);
        Say($"Заказан поездной локомотив на путь О{d} (поезд №{x.TrainNo})");
        return true;
    }

    /// <summary>Отправить готовый поезд с пути d парка отправления.</summary>
    public bool DepartTrain(int d)
    {
        var x = Dep[d];
        if (x.State != DepState.Ready) return Refuse(x.State switch
        {
            DepState.BrakeTest => $"на пути О{d} ещё идёт опробование тормозов",
            DepState.WaitLoco or DepState.LocoComing => $"на пути О{d} нет поездного локомотива",
            _ => $"на пути О{d} нет готового поезда",
        });
        Depart(x);
        return true;
    }

    /// <summary>Уложить / снять тормозной башмак на сортировочном пути k.</summary>
    public bool SetShoe(int k, bool on)
    {
        if (!Strict) return Refuse("тормозные башмаки — в вариантах «Учебный» и «Экзамен»");
        if (Shoe[k] == on) return true;
        if (on && Cuts.Any(c => c.Active && c.Track == k))
            return Refuse($"на пути {k} уже есть вагоны — башмак кладут на свободный путь, до первого отцепа");
        if (!on && Cuts.Any(c => c.State == CutState.Rolling && (c.Track == k || (c.Lo == c.Hi && c.Lo == k))))
            return Refuse($"на путь {k} идёт отцеп — подождите");
        Shoe[k] = on;
        if (!on) foreach (var c in Cuts.Where(c => c.Track == k)) c.ShoeStop = 0;
        Say(on ? $"Путь {k}: тормозной башмак уложен у конца пути ({ShoeGap:0} м до упора)" : $"Путь {k}: тормозной башмак снят");
        return true;
    }

    /// <summary>Отцеп наехал на башмак: тормозит юзом; быстрее 12 км/ч — сбил башмак. true — остановился.</summary>
    bool ShoeHit(Cut c)
    {
        if (c.Lo != c.Hi || !Shoe[c.Lo] || c.Head < ShoeS) return false;
        if (c.ShoeStop == 0)
        {
            if (c.V > ShoeMaxSpeed)
            {
                Shoe[c.Lo] = false;
                Violate($"отцеп {c.No} сбил тормозной башмак на пути {c.Lo} ({c.V * 3.6:0.0} км/ч, наезжать не быстрее 12)", 5);
                return false;
            }
            c.ShoeStop = ShoeS + Math.Min(ShoeGap - 1, c.V * c.V / 2.4 + 0.5);
        }
        if (c.Head < c.ShoeStop && c.V > 0) return false;
        c.Head = Math.Min(c.Head, c.ShoeStop);
        c.V = 0;
        c.State = CutState.Stopped;
        c.StopChecked = true;
        Say($"Отцеп {c.No} остановлен тормозным башмаком на пути {c.Lo}");
        return true;
    }

    /// <summary>Задержки, которые студент допустил (каждая — один раз).</summary>
    void StepChecks()
    {
        // ждать, пока все пути приёма заняты, — не вина студента: время считается, только пока есть свободный путь
        if (Approaching != null && !Arr.Skip(1).Any(a => a.State == ArrState.Free && a.Train == null && !PushOccupies(a.No))) approachSince = T;
        if (Approaching != null && !approachLate && T - approachSince > 180)
        {
            approachLate = true;
            Violate($"поезд {Approaching.Name} больше 3 минут ждёт у входного светофора, хотя есть свободный путь приёма", 3);
        }
        foreach (var d in Dep.Skip(1))
        {
            if (d.Late) continue;
            if (d.State == DepState.WaitLoco && T - d.Until > 165)
            { d.Late = true; Violate($"на пути О{d.No} состав больше 3 минут ждёт — не заказан поездной локомотив", 2); }
            else if (d.State == DepState.Ready && T - d.Until > 180)
            { d.Late = true; Violate($"поезд №{d.TrainNo} больше 3 минут готов, но не отправлен с пути О{d.No}", 3); }
        }
        for (int k = 1; k <= Lay.Tracks; k++)
        {
            int n = OnTrack(k).Sum(c => c.Wagons.Count);
            if (!overflowWarned[k] && n > Norm(k) + 4)
            { overflowWarned[k] = true; Violate($"путь {k} переполнен: {n} ваг. при норме {Norm(k)} — состав не выведен вовремя", 3); }
        }
    }

    // =================================================================
    //  [ДОБАВЛЕНО 26.11.2026] АВТОПИЛОТ («Демонстрация»): то, что в «Только горка» делает студент
    // =================================================================
    void Pilot()
    {
        var t = Pushing;
        if (t == null)
        {
            var r = Arr.Skip(1).FirstOrDefault(a => a.State == ArrState.Ready);
            if (r == null || Cuts.Any(c => c.State == CutState.Stopped && c.Head < Lay.STrack)) return;
            Say($"Автопилот: состав на пути П{r.No} готов — задаю надвиг и открываю горочный светофор (жёлтый и зелёный)");
            SetPushRoute(r.No);
            SetAspect(Aspect.YellowGreen);
            return;
        }
        // отцеп встал в стрелочной зоне — остановить роспуск и протолкнуть
        if (Cuts.Any(c => c.State == CutState.Stopped && c.Head < Lay.STrack) && !t.Escort)
        {
            if (Hump != Aspect.Red) { Say("Автопилот: отцеп остановился в стрелочной зоне — останавливаю роспуск"); SetAspect(Aspect.Red); }
            else if (t.V < 0.05) { Say("Автопилот: проталкиваю отцеп на путь"); PushStuck(); }
            return;
        }
        if (Hump == Aspect.Red && !t.Escort && t.Cuts.Count > 0 && !t.Cuts[0].Forbidden) { Say("Автопилот: продолжаю роспуск"); SetAspect(Aspect.YellowGreen); return; }
        // впереди только вагоны «Л» / «Е» — остановить перед горбом и спустить локомотивом
        if (t.Cuts.Count > 0 && !t.Escort && t.Cuts.All(c => c.Forbidden) && t.Cuts[0].Head > -25)
        {
            if (Hump != Aspect.Red) { Say($"Автопилот: впереди отцеп «{t.Cuts[0].Mark}» — его с горки распускать нельзя, останавливаю надвиг"); SetAspect(Aspect.Red); }
            else if (t.V == 0) { Say("Автопилот: спускаю эти вагоны с горки локомотивом, не быстрее 5 км/ч"); if (StartEscort()) SetAspect(Aspect.Yellow); }
        }
    }

    bool PushOccupies(int a) => Pushing != null && Pushing.Arr == a && Arr[a].Train == Pushing;

    void Arrive(ArrTrack a, Train t = null)
    {
        t ??= MakeTrain(a.No);
        a.State = ArrState.Arriving;
        a.Train = null;
        var m = new Mover { Pts = Lay.ArrivalPath(a.No), Speed = 40 };
        m.Vehicles.Add(((float)(t.TrainLoco.Length * Lay.ArrScale), t.TrainLoco, null));
        foreach (var w in t.AllCuts.SelectMany(c => c.Wagons)) m.Vehicles.Add(((float)(w.Length * Lay.ArrScale), null, w));
        m.Done = () =>
        {
            PlaceInPark(t);
            a.State = ArrState.Uncoupling;
            a.Until = T + 20;
            Say($"Поезд {t.Name} ({t.WagonCount} ваг., {t.TrainLoco.Series}) прибыл на путь П{a.No} парка приёма");
        };
        Movers.Add(m);
        Say($"ДСП: принимается поезд {t.Name} на путь П{a.No} парка приёма");
    }

    void UncoupleTrainLoco(ArrTrack a)
    {
        var t = a.Train;
        var loco = t.TrainLoco;
        t.TrainLoco = null;
        a.State = ArrState.Inspection;
        a.Until = T + 120;
        var m = new Mover { Pts = Lay.LocoToDepot(a.No), Speed = 30 };
        m.Vehicles.Add(((float)(loco.Length * Lay.SceneScale) * 1.3f, loco, null));
        Movers.Add(m);
        if (ListokTrain == null || (ListokTrain.Cuts.Count == 0 && Pushing == null)) ListokTrain = t;
        Say($"Путь П{a.No}: поездной локомотив {loco.Series} отцеплен и ушёл в депо. Осмотр ПТО и коммерческий осмотр (2 мин)");
    }

    void Depart(DepTrack d)
    {
        d.State = DepState.Leaving;
        var m = new Mover { Pts = Lay.Departure(d.No), Speed = 40, Pos = 158 };
        m.Vehicles.Add(((float)(d.Loco.Length * Lay.SceneScale), d.Loco, null));
        foreach (var w in d.Wagons) m.Vehicles.Add(((float)(w.Length * Lay.SceneScale), null, w));
        int n = d.Wagons.Count, from = d.From, no = d.TrainNo;
        d.Wagons.Clear();
        m.Done = () => { d.State = DepState.Free; };
        Movers.Add(m);
        Departed++;
        Say($"Поезд №{no} ({n} ваг.) отправлен с пути О{d.No} на {TrackNames[from]}");
    }

    void StepMovers(double dt)
    {
        foreach (var m in Movers.ToList())
        {
            double total = m.Total + m.Vehicles.Sum(v => v.len + 1) * (m.Pts.Length > 0 && m.Pts[^1].X > Lay.W ? 1 : 0);
            m.Pos += m.Speed * dt;
            if (m.Pos >= total)
            {
                m.Pos = total;
                m.Finished = true;
                Movers.Remove(m);
                m.Done?.Invoke();
            }
        }
    }
}
