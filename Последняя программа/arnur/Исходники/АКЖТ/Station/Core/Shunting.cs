using System.Text.Json;

namespace StationApp.Core;

// =====================================================================
//  МАНЕВРОВАЯ РАБОТА С ВАГОНАМИ И ПУТИ НЕОБЩЕГО ПОЛЬЗОВАНИЯ (ПНП)  [ДОБАВЛЕНО 23.10.2026, этап 7]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Окна — Ui/ShuntUi.cs. ПНП программы — Model/ScheduleSetup.cs.
//
//  Как в тренажёре ДСП (часть 3 и часть 2, раздел 11):
//   • у грузового поезда натурный лист: вагоны (номер — первая цифра род: 2 крытый, 4 платформа, 6 полувагон,
//     7 цистерна, 9 прочий), гружёные / порожние, местные (на ПНП этой станции) и транзитные;
//   • радиосвязь → «Расформировать» (поезд становится маневровым составом) → «Маневровая работа…»:
//     расцепить (вагоны остаются на пути без локомотива), сцепить (маневровый подъехал к вагонам),
//     тормозные башмаки под вагоны, доклад «Закреплён / Не закреплён», «Формирование…» (снова поезд),
//     «Осмотр» (предъявить к осмотру — ПТО). Вагоны без локомотива, не закреплённые по норме, через 2 мин —
//     критическое событие (возможен уход вагонов). Движение с башмаком под вагоном — неправильное действие;
//   • ПНП (тупик станции): уведомление, заезд маневрового с вагонами, «Задание на маневровую работу»
//     (к подаче / к уборке), 5 фронтов по роду вагонов по 5 вагонов, погрузка / выгрузка по времени,
//     журнал сообщений ПНП, лампы «локомотив на ПНП» и «вагоны к уборке»;
//   • критерии урока: подано вагонов на ПНП, убрано, отправлено местных вагонов.
// =====================================================================

/// <summary>Грузовой фронт ПНП: род вагонов, вагоны на нём, когда кончится операция.</summary>
public class PnpFront
{
    public string Kind = "";
    public string Title = "";
    public const int Capacity = 5;
    public readonly List<Wagon> Wagons = new();
    /// <summary>Игровое время окончания погрузки / выгрузки (−1 — операция не идёт).</summary>
    public double OpUntil = -1;
    public bool Busy => OpUntil >= 0;
    public IEnumerable<Wagon> Ready => Wagons.Where(w => w.Done);
}

public class Pnp
{
    public string Name = "";
    public TrackSection Section;
    public readonly List<PnpFront> Fronts = Shunting.Kinds.Select(k => new PnpFront { Kind = k.code, Title = k.title }).ToList();
    public bool Notified;
    /// <summary>Сообщать о готовности вагонов к уборке.</summary>
    public bool ReportReady = true;
    public VirtualTrain Loco;
    /// <summary>Идёт маневровая работа на ПНП (до WorkUntil).</summary>
    public double WorkUntil = -1;
    public bool Working => WorkUntil >= 0;
    public readonly List<Wagon> ToSupply = new(), ToRemove = new();
    public readonly List<string[]> Log = new();
    public int Supplied, Removed, Processed;
    public bool HasReady => Fronts.Any(f => f.Ready.Any());
}

public static class Shunting
{
    public static readonly (string code, string title, char digit)[] Kinds =
    {
        ("кр", "крытые", '2'), ("пл", "платформы", '4'), ("пв", "полувагоны", '6'), ("цс", "цистерны", '7'), ("пр", "прочие", '9'),
    };

    /// <summary>Норма закрепления: вагонов на один тормозной башмак (на станционных путях, площадка).</summary>
    public const int NormWagonsPerShoe = 10;
    /// <summary>Сколько игровых секунд вагоны могут стоять незакреплёнными (пока составитель закрепляет).</summary>
    public const double SecureGrace = 120;

    static Interlocking il;
    static TrainSim sim;
    static readonly Random rnd = new();

    public static readonly List<Pnp> Pnps = new();
    public static event Action Changed;

    // ---- урок ----
    public static int PlanSupply, PlanRemove, PlanLocalDepart;
    public static double MinPerWagon = 5;
    /// <summary>Сколько местных вагонов (на ПНП этой станции) в каждом грузовом поезде.</summary>
    public static int LocalWagons = 4;
    public static int LocalDeparted;
    public static int Supplied => Pnps.Sum(p => p.Supplied);
    public static int Removed => Pnps.Sum(p => p.Removed);

    static int groupNo = 90000;
    static readonly Dictionary<VirtualTrain, double> unsecuredSince = new();
    static readonly HashSet<VirtualTrain> unsecuredReported = new();
    static readonly Dictionary<VirtualTrain, TrackSection> lastHead = new();

    public static void Init(Interlocking interlocking, TrainSim trains)
    {
        il = interlocking;
        sim = trains;
        sim.Spawned += OnSpawned;
        sim.Exited += OnExited;
        il.Ticked += Tick;
    }

    /// <summary>ПНП программы (Model/ScheduleSetup.cs): название и тупик, на котором он.</summary>
    public static void AddPnp(string name, TrackSection section) => Pnps.Add(new Pnp { Name = name, Section = section });

    public static Pnp PnpOf(TrackSection s) => Pnps.FirstOrDefault(p => p.Section == s);

    // =================================================================
    //  ВАГОНЫ
    // =================================================================

    public static string KindTitle(string code) => Kinds.FirstOrDefault(k => k.code == code).title ?? code;

    static Wagon NewWagon(string kind, string dest)
    {
        char d = Kinds.First(k => k.code == kind).digit;
        string num = d + string.Concat(Enumerable.Range(0, 7).Select(_ => rnd.Next(10)));
        return new Wagon { Number = num, Kind = kind, Loaded = rnd.Next(2) == 0, Dest = dest };
    }

    /// <summary>Грузовой поезд при появлении получает натурный лист: местные вагоны — в голове, за локомотивом.</summary>
    static void OnSpawned(VirtualTrain t) => EnsureConsist(t);

    /// <summary>Натурный лист грузового поезда, если его ещё нет (появился, стал грузовым в «Номер и индекс»).</summary>
    public static void EnsureConsist(VirtualTrain t)
    {
        if (t.Shunting || t.NoLoco || t.Consist.Count > 0 || t.Category != "грузовой") return;
        int total = t.Wagons > 0 ? t.Wagons : rnd.Next(20, 41);
        var pnp = Pnps.FirstOrDefault();
        int local = pnp == null ? 0 : Math.Min(LocalWagons, total);
        for (int i = 0; i < local; i++) t.Consist.Add(NewWagon(Kinds[rnd.Next(Kinds.Length)].code, pnp.Name));
        for (int i = local; i < total; i++) t.Consist.Add(NewWagon(Kinds[rnd.Next(Kinds.Length)].code, ""));
        t.Wagons = t.Consist.Count;
    }

    static void OnExited(VirtualTrain t, TrackSection from, TrackStep step)
    {
        int n = t.Consist.Count(w => w.Done);
        if (n > 0 && Training.Active)
        {
            LocalDeparted += n;
            Training.Note($"Поезд №{t.Number} отправлен с местными вагонами после грузовых операций: {n}");
        }
        if (t.FormedHere && !t.Inspected && !t.Shunting)
            Training.AddEvent(false, "pto", $"Поезд №{t.Number}, сформированный на станции, отправлен без осмотра состава (ПТО)", 5);
        Changed?.Invoke();
    }

    /// <summary>«2/1 кр, 0/5 пл, всего 8» (гружёных / порожних по роду). show — для окна: на языке интерфейса
    /// (в журналы и отчёт — по-русски, переводится при показе).</summary>
    public static string Summary(IEnumerable<Wagon> wagons, bool show = false)
    {
        var l = wagons.ToList();
        if (l.Count == 0) return show ? L.T("вагонов нет") : "вагонов нет";
        var parts = Kinds.Select(k => (k, n: l.Count(w => w.Kind == k.code), loaded: l.Count(w => w.Kind == k.code && w.Loaded)))
                         .Where(x => x.n > 0).Select(x => $"{x.loaded}/{x.n - x.loaded} {(show ? L.T(x.k.code) : x.k.code)}");
        return show ? L.F("{0}, всего {1}", string.Join(", ", parts), l.Count) : $"{string.Join(", ", parts)}, всего {l.Count}";
    }

    // =================================================================
    //  РАСФОРМИРОВАНИЕ, ФОРМИРОВАНИЕ, ОСМОТР
    // =================================================================

    /// <summary>Радиосвязь «Расформировать»: поезд становится маневровым составом.</summary>
    public static void Disband(VirtualTrain t)
    {
        if (t.Shunting) return;
        sim.SetShunting(t, true);
        t.FormedHere = t.Inspected = t.InspectAsked = false;
        Training.Note($"Поезд №{t.Number} расформирован — маневровый состав, {Summary(t.Consist)}");
        Changed?.Invoke();
    }

    /// <summary>«Формирование»: маневровый состав снова поезд (номер и индекс — окно «Номер и индекс»).</summary>
    public static string Form(VirtualTrain t)
    {
        if (t.NoLoco) return "вагоны без локомотива — сначала сцепите с локомотивом";
        if (t.Consist.Any(w => w.Shoe)) return "под вагонами тормозные башмаки — изъять";
        sim.SetShunting(t, false);
        t.FormedHere = true;
        t.Inspected = t.InspectAsked = false;
        Training.Note($"Сформирован поезд №{t.Number}: {Summary(t.Consist)}");
        Changed?.Invoke();
        return null;
    }

    /// <summary>«Осмотр»: предъявить состав к осмотру — осмотрщик (ПТО) осматривает и сообщает.</summary>
    public static void Inspect(VirtualTrain t)
    {
        if (t.InspectAsked) return;
        t.InspectAsked = true;
        double at = Du46.Now + rnd.Next(180, 361);
        inspections.Add((at, t));
        Training.Note($"Состав №{t.Number} предъявлен к осмотру (ПТО)");
        Changed?.Invoke();
    }

    static readonly List<(double at, VirtualTrain t)> inspections = new();

    // =================================================================
    //  СЦЕПКА, РАСЦЕПКА, БАШМАКИ
    // =================================================================

    /// <summary>Отцепленные вагоны рядом с локомотивом: на том же участке или на следующем по ходу.</summary>
    public static VirtualTrain GroupNear(VirtualTrain loco)
    {
        if (loco.Head == null) return null;
        var ahead = sim.Peek(loco.Head, loco.Dir)?.Section;
        var behind = sim.Peek(loco.Cells[^1], loco.Dir == Dir.Right ? Dir.Left : Dir.Right)?.Section;
        return sim.Trains.FirstOrDefault(g => g.NoLoco && g != loco &&
                                              (g.Cells.Intersect(loco.Cells).Any() || g.Cells.Contains(ahead) || g.Cells.Contains(behind)));
    }

    /// <summary>Сцепить локомотив с вагонами рядом.</summary>
    public static string Couple(VirtualTrain loco)
    {
        if (loco.NoLoco) return "это вагоны без локомотива";
        var g = GroupNear(loco);
        if (g == null) return "рядом нет отцепленных вагонов — подъезжайте вплотную";
        loco.Consist.AddRange(g.Consist);
        foreach (var c in g.Cells)
        {
            if (loco.Cells.Contains(c)) { c.TrainCount = Math.Max(0, c.TrainCount - 1); continue; }
            loco.Cells.Insert(0, c);   // вагоны впереди: состав занимает и их участок
        }
        sim.Trains.Remove(g);
        unsecuredSince.Remove(g);
        loco.Wagons = loco.Consist.Count;
        loco.Waiting = "";
        il.Say($"Маневровый №{loco.Number}: сцеплен с вагонами ({g.Consist.Count}) — {Summary(loco.Consist)}", LogKind.Train);
        Training.Note($"Сцепка: №{loco.Number} + {g.Consist.Count} ваг. на {g.Head?.Label}");
        il.ComputeAspects();
        il.NotifyChanged();
        Changed?.Invoke();
        return null;
    }

    /// <summary>Расцепить: вагоны начиная с index остаются на пути без локомотива.</summary>
    public static string Uncouple(VirtualTrain loco, int index)
    {
        if (loco.NoLoco) return "это вагоны без локомотива";
        if (index < 0 || index >= loco.Consist.Count) return "выберите вагон, с которого отцепить";
        if (!loco.StopOrder && loco.Waiting.Length == 0) return "состав движется — сначала остановите (радиосвязь «Остановка»)";
        var cut = loco.Consist.Skip(index).ToList();
        loco.Consist.RemoveRange(index, cut.Count);
        loco.Wagons = loco.Consist.Count;
        var at = loco.Cells[^1];
        var g = new VirtualTrain { Number = ++groupNo, Dir = loco.Dir, Shunting = true, NoLoco = true, Category = "вагоны" };
        g.Consist.AddRange(cut);
        g.Wagons = cut.Count;
        g.Cells.Add(at);
        at.TrainCount++;
        sim.Trains.Add(g);
        unsecuredSince[g] = Du46.Now;
        il.Say($"Маневровый №{loco.Number}: отцеплено {cut.Count} ваг. на {at.Label} — закрепите тормозными башмаками (норма 1 на {NormWagonsPerShoe} ваг.)", LogKind.Train);
        Training.Note($"Расцепка: от №{loco.Number} отцеплено {cut.Count} ваг. на {at.Label} ({Summary(cut)})");
        il.NotifyChanged();
        Changed?.Invoke();
        return null;
    }

    public static int ShoesNeeded(int wagons) => (int)Math.Ceiling(wagons / (double)NormWagonsPerShoe);

    public static void ToggleShoe(VirtualTrain t, Wagon w)
    {
        w.Shoe = !w.Shoe;
        Training.Note($"{(w.Shoe ? "Тормозной башмак уложен под" : "Тормозной башмак изъят из-под")} вагон {w.Number} ({(t.NoLoco ? "вагоны на " + t.Head?.Label : "состав №" + t.Number)})");
        Changed?.Invoke();
    }

    /// <summary>Доклад составителя машинисту: «Закреплён» / «Не закреплён».</summary>
    public static string Report(VirtualTrain t, bool secured)
    {
        int have = t.Consist.Count(w => w.Shoe), need = ShoesNeeded(t.Consist.Count);
        string text = secured
            ? (have >= need ? $"Состав закреплён: {have} тормозных башмака(ов), норма {need}" : $"Доложено «закреплён», но башмаков {have} при норме {need}")
            : (have == 0 ? "Закрепление снято, башмаки изъяты" : $"Доложено «не закреплён», но под вагонами ещё {have} башмака(ов)");
        Training.Note($"Доклад о закреплении ({t.Head?.Label}): {text}");
        if (secured && have < need) Training.AddEvent(false, "secure", $"Доклад о закреплении вагонов на {t.Head?.Label}: башмаков {have}, а по норме нужно {need}", 5);
        return text;
    }

    // =================================================================
    //  ПНП
    // =================================================================

    static void Log(Pnp p, string text, IEnumerable<Wagon> wagons = null)
    {
        p.Log.Add(new[] { (p.Log.Count + 1).ToString(), Training.Fmt(Du46.Now)[..5], text, wagons == null ? "" : Summary(wagons) });
        Training.Note($"ПНП «{p.Name}»: {text}");
        Changed?.Invoke();
    }

    public static void Notify(Pnp p)
    {
        if (p.Notified) return;
        p.Notified = true;
        Log(p, "Отправка уведомления о предстоящей маневровой работе");
    }

    /// <summary>Что можно подать на ПНП из состава локомотива на ПНП: местные вагоны этого ПНП, ещё без операции.</summary>
    public static IEnumerable<Wagon> CanSupply(Pnp p) => p.Loco?.Consist.Where(w => w.Dest == p.Name && !w.Done) ?? Enumerable.Empty<Wagon>();

    /// <summary>Что можно убрать: вагоны на фронтах (готовые — по умолчанию; под операцией — тоже можно, операция прервётся).</summary>
    public static IEnumerable<Wagon> CanRemove(Pnp p) => p.Fronts.SelectMany(f => f.Wagons);

    public static PnpFront FrontOf(Pnp p, Wagon w) => p.Fronts.FirstOrDefault(f => f.Kind == w.Kind);

    /// <summary>«Выполнить маневровую работу»: подать и убрать выбранные вагоны. null — началась, иначе причина.</summary>
    public static string StartWork(Pnp p, List<Wagon> supply, List<Wagon> remove)
    {
        if (p.Loco == null) return "на ПНП нет локомотива";
        if (p.Working) return "маневровая работа уже идёт";
        if (supply.Count + remove.Count == 0) return "не выбрано ни одного вагона";
        foreach (var g in supply.GroupBy(w => w.Kind))
        {
            var f = FrontOf(p, g.First());
            int left = f.Wagons.Count - remove.Count(w => f.Wagons.Contains(w));
            if (left + g.Count() > PnpFront.Capacity) return $"фронт «{f.Title}» вмещает {PnpFront.Capacity} вагонов";
            if (f.Busy && left > 0) return $"на фронте «{f.Title}» идёт грузовая операция — подать нельзя";
        }
        p.ToSupply.Clear(); p.ToSupply.AddRange(supply);
        p.ToRemove.Clear(); p.ToRemove.AddRange(remove);
        int fronts = supply.Concat(remove).Select(w => w.Kind).Distinct().Count();
        p.WorkUntil = Du46.Now + fronts * 180 + (supply.Count + remove.Count) * 60;
        p.Loco.StopOrder = true;
        Log(p, $"Начало маневровой работы (маневровый №{p.Loco.Number}), окончание в {Training.Fmt(p.WorkUntil)[..5]}", supply.Concat(remove));
        return null;
    }

    static void FinishWork(Pnp p)
    {
        p.WorkUntil = -1;
        var loco = p.Loco;
        foreach (var w in p.ToRemove)
        {
            var f = FrontOf(p, w);
            f.Wagons.Remove(w);
            if (!f.Wagons.Any(x => !x.Done)) f.OpUntil = -1;
            if (loco != null) loco.Consist.Add(w);
        }
        p.Removed += p.ToRemove.Count;
        foreach (var g in p.ToSupply.GroupBy(w => w.Kind))
        {
            var f = FrontOf(p, g.First());
            foreach (var w in g) { loco?.Consist.Remove(w); w.Shoe = false; f.Wagons.Add(w); }
            f.OpUntil = Du46.Now + MinPerWagon * 60 * f.Wagons.Count(w => !w.Done);
            Log(p, $"Начало грузовых операций на фронте «{f.Title}», окончание в {Training.Fmt(f.OpUntil)[..5]}", f.Wagons);
        }
        p.Supplied += p.ToSupply.Count;
        if (loco != null) loco.Wagons = loco.Consist.Count;
        Log(p, "Конец маневровой работы", p.ToSupply.Concat(p.ToRemove));
        p.ToSupply.Clear();
        p.ToRemove.Clear();
        Du46.Message("Составитель", "", "", $"Маневровая работа на ПНП «{p.Name}» закончена. Разрешаете выезд на станцию?", true,
            sign: () => { if (p.Loco != null) { p.Loco.StopOrder = false; Log(p, "ДСП разрешил выезд на станцию"); } },
            reject: () => Log(p, "Локомотив остаётся на ПНП в ожидании распоряжений"), record: false);
    }

    // =================================================================
    //  ТАКТ
    // =================================================================

    static void Tick(double dt)
    {
        double now = Du46.Now;
        foreach (var p in Pnps)
        {
            // заезд и выезд локомотива
            var inside = sim.Trains.FirstOrDefault(t => !t.NoLoco && !t.Leaving && t.Cells.Contains(p.Section));
            if (inside != p.Loco)
            {
                if (p.Loco != null) Log(p, $"Выезд маневрового №{p.Loco.Number} на станцию", p.Loco.Consist);
                p.Loco = inside;
                if (inside != null) Enter(p, inside);
            }
            if (p.Working && now >= p.WorkUntil) FinishWork(p);
            foreach (var f in p.Fronts.Where(f => f.Busy && now >= f.OpUntil))
            {
                f.OpUntil = -1;
                foreach (var w in f.Wagons.Where(w => !w.Done)) { w.Done = true; w.Loaded = !w.Loaded; p.Processed++; }
                Log(p, $"Завершение грузовых операций на фронте «{f.Title}»", f.Wagons);
                if (p.ReportReady)
                    Du46.Message("ПНП", p.Name, "", $"Завершены грузовые операции на ПНП «{p.Name}» на фронте «{f.Title}». Вагоны готовы к уборке.", false, record: false);
            }
        }
        foreach (var (at, t) in inspections.Where(x => now >= x.at).ToList())
        {
            inspections.Remove((at, t));
            if (!sim.Trains.Contains(t)) continue;
            t.Inspected = true;
            Du46.Message("ПТО", "осмотрщик Нурланов", "T:" + t.Number,
                $"Состав поезда №{t.Number} осмотрен, тормоза опробованы, неисправностей нет. К отправлению готов.", false, record: false);
            Training.Note($"ПТО: состав №{t.Number} осмотрен");
        }
        CheckSecured(now);
        CheckShoes();
    }

    static void Enter(Pnp p, VirtualTrain t)
    {
        Log(p, $"Заезд {(t.Shunting ? "маневрового" : "поезда")} №{t.Number}", t.Consist);
        if (!t.Shunting)
            Training.AddEvent(false, "pnp", $"Заезд на ПНП «{p.Name}» поездом №{t.Number}: заезд на ПНП — только маневровым порядком", 5);
        if (!p.Notified && t.Consist.Any(w => w.Dest == p.Name))
            Training.AddEvent(false, "pnp", $"Подача вагонов на ПНП «{p.Name}» без уведомления владельца ПНП", 5);
        if (sim.Trains.Count(x => !x.NoLoco && x.Cells.Contains(p.Section)) > 1)
            Training.AddEvent(true, "pnp_two", $"На ПНП «{p.Name}» заехал второй локомотив", 20);
    }

    /// <summary>Вагоны без локомотива, не закреплённые по норме дольше 2 мин, — критическое событие.</summary>
    static void CheckSecured(double now)
    {
        foreach (var g in sim.Trains.Where(t => t.NoLoco).ToList())
        {
            int need = ShoesNeeded(g.Consist.Count), have = g.Consist.Count(w => w.Shoe);
            if (have >= need) { unsecuredSince.Remove(g); continue; }
            if (!unsecuredSince.TryGetValue(g, out var since)) { unsecuredSince[g] = now; continue; }
            if (now - since >= SecureGrace && unsecuredReported.Add(g))
                Training.AddEvent(true, "secure", $"Вагоны ({g.Consist.Count}) на {g.Head?.Label} не закреплены по норме: башмаков {have}, нужно {need} — возможен уход вагонов", 20);
        }
    }

    /// <summary>Состав тронулся, а под вагоном башмак — неправильное действие, башмак сбит.</summary>
    static void CheckShoes()
    {
        foreach (var t in sim.Trains.Where(t => !t.NoLoco))
        {
            lastHead.TryGetValue(t, out var was);
            lastHead[t] = t.Head;
            if (was == null || was == t.Head || !t.Consist.Any(w => w.Shoe)) continue;
            foreach (var w in t.Consist) w.Shoe = false;
            Training.AddEvent(false, "shoe", $"Состав №{t.Number} тронулся с тормозным башмаком под вагоном (с {was.Label} на {t.Head?.Label})", 5);
        }
        foreach (var t in lastHead.Keys.Where(t => !sim.Trains.Contains(t)).ToList()) lastHead.Remove(t);
    }

    // =================================================================
    //  ПАСПОРТ (передача соседу), УРОК, ОТЧЁТ
    // =================================================================

    public static object ConsistWire(VirtualTrain t) => t.Consist.Select(w => new { n = w.Number, k = w.Kind, l = w.Loaded, d = w.Dest, done = w.Done }).ToList();

    public static void ApplyConsist(VirtualTrain t, JsonElement arr)
    {
        if (t == null || arr.ValueKind != JsonValueKind.Array) return;
        t.Consist.Clear();
        foreach (var e in arr.EnumerateArray())
        {
            string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            bool B(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            t.Consist.Add(new Wagon { Number = S("n"), Kind = S("k"), Loaded = B("l"), Dest = S("d"), Done = B("done") });
        }
        t.Wagons = t.Consist.Count;
    }

    public static void Begin(LessonTask l)
    {
        PlanSupply = l?.PlanSupply ?? 0;
        PlanRemove = l?.PlanRemove ?? 0;
        PlanLocalDepart = l?.PlanLocalDepart ?? 0;
        MinPerWagon = l != null && l.PnpMinPerWagon > 0 ? l.PnpMinPerWagon : 5;
        LocalWagons = l?.LocalWagons ?? 4;
        LocalDeparted = 0;
        groupNo = 90000;
        foreach (var p in Pnps)
        {
            foreach (var f in p.Fronts) { f.Wagons.Clear(); f.OpUntil = -1; }
            p.Notified = false;
            p.Loco = null;
            p.WorkUntil = -1;
            p.Log.Clear();
            p.Supplied = p.Removed = p.Processed = 0;
        }
        inspections.Clear();
        unsecuredSince.Clear();
        unsecuredReported.Clear();
        lastHead.Clear();
        Changed?.Invoke();
    }

    public static void End()
    {
        LocalWagons = 4;
        MinPerWagon = 5;
    }

    /// <summary>Штраф за невыполненный план маневровой работы: −2 за вагон, не больше −20.</summary>
    public static int PlanPenalty() =>
        Math.Min(20, 2 * (Math.Max(0, PlanSupply - Supplied) + Math.Max(0, PlanRemove - Removed) + Math.Max(0, PlanLocalDepart - LocalDeparted)));

    public static List<object> ReportDocs()
    {
        var docs = new List<object>();
        if (PlanSupply + PlanRemove + PlanLocalDepart + Supplied + Removed + LocalDeparted > 0)
            docs.Add(new
            {
                title = "Маневровая работа: задание на игру",
                columns = new[] { "Критерий", "План", "Факт" },
                rows = new List<string[]>
                {
                    new[] { "Подано вагонов на грузовые фронты", $"не менее {PlanSupply}", Supplied.ToString() },
                    new[] { "Убрано вагонов с грузовых фронтов", $"не менее {PlanRemove}", Removed.ToString() },
                    new[] { "Отправлено местных вагонов", $"не менее {PlanLocalDepart}", LocalDeparted.ToString() },
                },
            });
        foreach (var p in Pnps.Where(p => p.Log.Count > 0))
            docs.Add(new
            {
                title = $"Журнал сообщений ПНП «{p.Name}» (подано {p.Supplied}, убрано {p.Removed}, прошли грузовые операции {p.Processed})",
                columns = new[] { "№", "Время", "Сообщение", "Вагоны" },
                rows = p.Log.ToList(),
            });
        return docs;
    }

    public static void NotifyChanged() => Changed?.Invoke();
}
