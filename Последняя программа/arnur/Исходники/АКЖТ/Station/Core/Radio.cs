using System.Text.Json;

namespace StationApp.Core;

// =====================================================================
//  ПОЕЗД КАК ОБЪЕКТ, ПОЕЗДНАЯ РАДИОСВЯЗЬ, ПРЕДУПРЕЖДЕНИЯ И «ОКНА»  [ДОБАВЛЕНО 23.10.2026, этап 4]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Окна — Ui/RadioUi.cs.
//
//  Как в тренажёре ДСП (часть 1, разделы 1.5, 1.7, 1.8; часть 2, раздел 2):
//   • у поезда номер, индекс, категория, длинносоставный / тяжеловесный / негабаритный, длина в вагонах;
//   • радиосвязь «Поезд №…»: Остановка / Продолжить, Указание (регистрируемый приказ — проследовать один
//     запрещающий светофор, запись в ДУ-58), Осаживание (на участок назад), Ремонт (неисправность двигателя),
//     Сократить стоянку, Предупреждение (скорость на поезд, сообщить действующие), Где находится;
//   • предупреждения на участок / перегон (скорость пасс. / груз.) и «окна» (путь закрыт для работ):
//     виртуальный поезд под предупреждением едет медленнее, в «окно» маршрут не задаётся и поезд не вступает
//     (кроме хозяйственного и восстановительного). «Окно» на перегоне даётся приказом ДНЦ — его номер нужно
//     записать в ДУ-58; поезд, вступивший под предупреждение без ДУ-61 / сообщения по радиосвязи, — ошибка;
//   • настоящий поезд на макете: номер присваивается занятому датчиком участку и переходит за поездом
//     на соседние занятые участки. Ему по радиосвязи можно только передать команду (запись в журнал).
// =====================================================================

/// <summary>Предупреждение или «окно».</summary>
public class Restriction
{
    public int No { get; set; }
    /// <summary>warning — предупреждение (ограничение скорости), window — «окно» (путь закрыт для работ).</summary>
    public string Kind { get; set; } = "warning";
    /// <summary>Объект: P:участок или L:перегон.</summary>
    public string Obj { get; set; } = "";
    public string ObjLabel { get; set; } = "";
    /// <summary>Километры, пикеты, путь — как ввёл ДСП.</summary>
    public string Place { get; set; } = "";
    public int SpeedPass { get; set; }
    public int SpeedFreight { get; set; }
    /// <summary>«Окно»: какая служба работает.</summary>
    public string Service { get; set; } = "";
    public double From { get; set; }
    /// <summary>До какого времени (−1 — до отмены).</summary>
    public double Until { get; set; } = -1;
    public bool Cancelled { get; set; }
    public double CancelledAt { get; set; } = -1;
    /// <summary>Номер приказа ДНЦ о закрытии перегона для «окна» (0 — не нужен).</summary>
    public int OrderNo { get; set; }
    public int CancelOrderNo { get; set; }

    public bool IsWindow => Kind == "window";
    public bool Active => !Cancelled && (Until < 0 || Du46.Now < Until);

    public string SpeedText => SpeedPass == SpeedFreight ? $"{SpeedPass} км/ч" : $"пасс. {SpeedPass}, груз. {SpeedFreight} км/ч";
    public string Text => IsWindow
        ? $"«Окно» №{No}: {ObjLabel}{(Place.Length > 0 ? ", " + Place : "")}, служба {Service}, {Period}"
        : $"Предупреждение №{No}: {ObjLabel}{(Place.Length > 0 ? ", " + Place : "")}, скорость {SpeedText}, {Period}";
    public string Period => Until < 0 ? $"с {Training.Fmt(From)[..5]} до отмены" : $"с {Training.Fmt(From)[..5]} до {Training.Fmt(Until)[..5]}";
}

public static class Radio
{
    /// <summary>Скорость, которой соответствует обычный шаг виртуального поезда (TrainSim.StepSeconds), км/ч.</summary>
    public const int NominalKmh = 60;

    static Interlocking il;
    static TrainSim sim;
    static readonly Random rnd = new();

    public static readonly List<Restriction> Restrictions = new();
    /// <summary>Переговоры по радиосвязи: номер поезда → строки «время  кто: текст».</summary>
    public static readonly Dictionary<int, List<string>> Talks = new();
    public static event Action Changed;

    static int restrictionNo, orderNo = 100;
    static readonly List<(double at, int train)> repairs = new();
    static readonly Dictionary<VirtualTrain, TrackSection> lastHead = new();
    static readonly HashSet<VirtualTrain> du61Reported = new();

    public static readonly string[] Categories = SchedPoints.Categories.Append("восстановительный").ToArray();
    public static readonly string[] Services = { "пути", "СЦБ и связи", "энергохозяйства" };

    public static void Init(Interlocking interlocking, TrainSim trains)
    {
        il = interlocking;
        sim = trains;
        sim.SpeedFactor = SpeedFactor;
        sim.Permit = Permit;
        sim.Blocked = Blocked;
        sim.Spawned += t => { t.Category = CategoryByNumber(t.Number, t.Shunting); };
        sim.BackedUp += OnBackedUp;
        sim.Exited += (t, _, _) => { t.IgnoreAutoSignals = false; t.PermitExit = t.PermitOnce = ""; };
        il.Ticked += Tick;
    }

    /// <summary>Категория по номеру поезда (как принято на ЖД): 1–998 пассажирские, 1001–3998 грузовые, 4001–4998 локомотивы,
    /// 6001–7998 пригородные, 8001–8998 хозяйственные.</summary>
    public static string CategoryByNumber(int n, bool shunting = false) => shunting ? "локомотив" : n switch
    {
        >= 100 and < 1000 => "пассажирский",   // 1–99 — номера кнопок имитации: грузовые (с вагонами), поменять можно в «Номер и индекс»
        >= 4000 and < 5000 => "локомотив",
        >= 6000 and < 8000 => "пригородный",
        >= 8000 and < 9000 => "хозяйственный",
        _ => "грузовой"
    };

    // =================================================================
    //  ПРАВИЛА ДВИЖЕНИЯ (крючки TrainSim)
    // =================================================================

    static double SpeedFactor(VirtualTrain t)
    {
        int lim = t.SpeedLimit;
        if (t.Head is { Warning: true } h)
        {
            int w = t.Passenger ? h.WarnPass : h.WarnFreight;
            if (w > 0 && (lim == 0 || w < lim)) lim = w;
        }
        return lim > 0 && lim < NominalKmh ? NominalKmh / (double)lim : 1;
    }

    /// <summary>Какую скорость показывать в окне радиосвязи.</summary>
    public static int SpeedNow(VirtualTrain t) => t.Waiting.Length > 0 ? 0 : (int)Math.Round(NominalKmh / SpeedFactor(t));

    static bool WorkTrain(VirtualTrain t) => t.Category is "хозяйственный" or "восстановительный";

    static string Blocked(VirtualTrain t, TrackSection next) =>
        next.Window && !WorkTrain(t) ? $"стоит на {t.Head?.Label}: впереди «окно» на {next.Label} (путь закрыт для работ)" : null;

    public static bool IsExit(RailSignal s) => il.Routes.Any(r => r.Start == s && r.ExitSection != null);

    static bool Permit(VirtualTrain t, TrackStep step)
    {
        var s = step.Signal;
        if (t.BackSteps > 0) return true;   // осаживание по указанию ДСП — на участок назад, светофоры встречного направления не касаются
        if (s.Kind == SignalKind.Automatic && t.IgnoreAutoSignals) return true;   // ДУ-50 / 56 / 64: по перегону не по сигналам АБ
        if (t.Shunting) return false;
        if (t.PermitExit.Length > 0 && s.Kind == SignalKind.Controlled && IsExit(s))
        {
            string kind = t.PermitExit;
            t.PermitExit = "";
            if (kind is "ДУ-50" or "ДУ-56" or "ДУ-64") t.IgnoreAutoSignals = true;
            Passed(t, step, $"по бланку {kind}");
            return true;
        }
        if (t.PermitOnce.Length > 0)
        {
            t.PermitOnce = "";
            Passed(t, step, "по регистрируемому приказу (указание по радиосвязи)");
            return true;
        }
        return false;
    }

    static void Passed(VirtualTrain t, TrackStep step, string how)
    {
        var s = step.Signal;
        il.Say($"Поезд №{t.Number} проследовал запрещающий светофор {s.Label} {how}", LogKind.Train);
        // путь за светофором должен быть приготовлен: стрелки замкнуты маршрутом, пригласительным или заперты
        var sec = step.Section;
        var loose = sec == null ? null : il.Switches.Values.FirstOrDefault(w => w.Sections.Contains(sec) && !w.Locked && w.Clamp.Length == 0);
        if (loose != null)
            Training.AddEvent(false, "permit_route",
                $"Поезд №{t.Number} пропущен {how} за светофор {s.Label}, а стрелка {loose.Label} не замкнута в маршруте и не заперта", 5);
    }

    /// <summary>Осадили на участок назад: состав стоит на этом участке (как остановившийся поезд — на одном участке),
    /// направление прежнее, ждёт указаний.</summary>
    static void OnBackedUp(VirtualTrain t)
    {
        for (int i = t.Cells.Count - 1; i >= 1; i--)
        {
            t.Cells[i].TrainCount = Math.Max(0, t.Cells[i].TrainCount - 1);
            t.Cells.RemoveAt(i);
        }
        t.Dir = t.Dir == Dir.Right ? Dir.Left : Dir.Right;
        t.Timer = 0;
        t.StopOrder = true;
        il.ComputeAspects();
        il.NotifyChanged();
        Reply(t.Number, $"Осаживание закончено, стою на {t.Head?.Label}. Жду указаний");
    }

    // =================================================================
    //  ТАКТ: предупреждения, «окна», ремонт, номера поездов на макете
    // =================================================================

    static void Tick(double dt)
    {
        Apply();
        foreach (var (at, n) in repairs.Where(r => Du46.Now >= r.at).ToList())
        {
            repairs.Remove((at, n));
            if (sim.Find(n) is { FaultEngine: true } t && Faults.FixedByCrew(n, "tr_engine"))
                Reply(n, "Неисправность локомотива устранена локомотивной бригадой, к движению готов");
        }
        CheckWarned();
        SpreadHwNumbers();
    }

    /// <summary>Флаги участков по действующим предупреждениям и «окнам».</summary>
    static void Apply()
    {
        foreach (var s in il.Sections.Values) { s.WarnPass = s.WarnFreight = 0; s.Window = false; }
        foreach (var r in Restrictions.Where(r => r.Active))
            foreach (var s in SectionsOf(r.Obj))
            {
                if (r.IsWindow) { s.Window = true; continue; }
                s.WarnPass = Min(s.WarnPass, r.SpeedPass);
                s.WarnFreight = Min(s.WarnFreight, r.SpeedFreight);
            }
        foreach (var r in Restrictions.Where(r => !r.Cancelled && r.Until >= 0 && Du46.Now >= r.Until))
        {
            r.Cancelled = true;
            r.CancelledAt = r.Until;
            il.Say($"Закончилось: {r.Text}");
            Changed?.Invoke();
        }
    }

    static int Min(int a, int b) => a == 0 ? b : b == 0 ? a : Math.Min(a, b);

    public static IEnumerable<TrackSection> SectionsOf(string obj)
    {
        if (obj.StartsWith("P:") && il.Sections.TryGetValue(obj[2..], out var s)) return new[] { s };
        if (obj.StartsWith("L:") && il.Lines.FirstOrDefault(l => l.Id == obj[2..]) is { } line) return line.Sections;
        return Array.Empty<TrackSection>();
    }

    /// <summary>Поезд вступил на участок с предупреждением, а машинист не предупреждён — ошибка (один раз на поезд).</summary>
    static void CheckWarned()
    {
        foreach (var t in sim.Trains.Where(t => !t.Leaving && !t.Shunting))
        {
            var h = t.Head;
            lastHead.TryGetValue(t, out var was);
            lastHead[t] = h;
            if (h == null || h == was || !h.Warning || t.Warned || !du61Reported.Add(t)) continue;
            Training.AddEvent(false, "du61", $"Поезд №{t.Number} вступил на участок {h.Label} с действующим предупреждением, " +
                                            "а машинисту не вручено предупреждение ДУ-61 (и не передано по радиосвязи)", 5);
        }
        foreach (var t in lastHead.Keys.Where(t => !sim.Trains.Contains(t)).ToList()) lastHead.Remove(t);
    }

    // =================================================================
    //  ПРЕДУПРЕЖДЕНИЯ И «ОКНА»
    // =================================================================

    public static Restriction Set(Restriction r)
    {
        r.No = ++restrictionNo;
        r.From = Du46.Now;
        r.ObjLabel = ObjLabel(r.Obj);
        Restrictions.Add(r);
        Apply();
        il.Say(r.IsWindow ? $"Предоставлено {r.Text}" : $"Выдано {r.Text}");
        Training.Note(r.Text + " — установлено ДСП");
        if (r.IsWindow && r.Obj.StartsWith("L:"))
        {
            r.OrderNo = ++orderNo;
            Dnc(r.Obj, $"Приказ №{r.OrderNo} {Training.Fmt(Du46.Now)[..5]}. ДСП ст. {DispatchLink.StationName}. " +
                       $"Для производства работ службы {r.Service} закрывается {r.ObjLabel}{(r.Place.Length > 0 ? $" ({r.Place})" : "")} " +
                       $"{(r.Until < 0 ? "до особого распоряжения" : $"до {Training.Fmt(r.Until)[..5]}")}. Поезда на перегон не отправлять. ДНЦ {Du46.WorkerName("ДНЦ")}");
        }
        il.NotifyChanged();
        Changed?.Invoke();
        return r;
    }

    public static void Cancel(Restriction r)
    {
        if (r.Cancelled) return;
        r.Cancelled = true;
        r.CancelledAt = Du46.Now;
        Apply();
        il.Say($"Отменено: {r.Text}");
        Training.Note($"Отменено ДСП: {r.Text}");
        if (r.IsWindow && r.OrderNo > 0)
        {
            r.CancelOrderNo = ++orderNo;
            Dnc(r.Obj, $"Приказ №{r.CancelOrderNo} {Training.Fmt(Du46.Now)[..5]}. ДСП ст. {DispatchLink.StationName}. " +
                       $"Работы закончены, {r.ObjLabel} открывается для движения поездов. ДНЦ {Du46.WorkerName("ДНЦ")}");
        }
        il.NotifyChanged();
        Changed?.Invoke();
    }

    /// <summary>Приказ ДНЦ: всплывает у ДСП, его нужно записать в журнал ДУ-58.</summary>
    static void Dnc(string obj, string text) =>
        Du46.Message("ДНЦ", Du46.WorkerName("ДНЦ"), obj, text + "\n\nЗапишите приказ в журнал диспетчерских распоряжений ДУ-58.", false, record: false);

    public static IEnumerable<Restriction> ActiveOn(string obj) => Restrictions.Where(r => r.Active && r.Obj == obj);
    public static IEnumerable<Restriction> ActiveAll => Restrictions.Where(r => r.Active);

    public static string ObjLabel(string obj)
    {
        if (obj.StartsWith("L:")) return il.Lines.FirstOrDefault(l => l.Id == obj[2..])?.Title ?? obj;
        if (obj.StartsWith("P:") && il.Sections.TryGetValue(obj[2..], out var s)) return $"участок {s.Label}";
        return obj;
    }

    /// <summary>Перегон, к которому относится участок (null — участок станции).</summary>
    public static DirectionLine LineOf(TrackSection s) => il.Lines.FirstOrDefault(l => l.Sections.Contains(s));

    /// <summary>Номера приказов ДНЦ, которые нужно найти в ДУ-58.</summary>
    public static IEnumerable<(int no, string what)> DncOrders() =>
        Restrictions.Where(r => r.OrderNo > 0).Select(r => (r.OrderNo, $"о закрытии: {r.ObjLabel}"))
            .Concat(Restrictions.Where(r => r.CancelOrderNo > 0).Select(r => (r.CancelOrderNo, $"об открытии: {r.ObjLabel}")));

    // =================================================================
    //  РАДИОСВЯЗЬ
    // =================================================================

    public static List<string> Talk(int n) => Talks.TryGetValue(n, out var l) ? l : Talks[n] = new();

    static void Line(int n, string who, string text)
    {
        Talk(n).Add($"{Training.Fmt(Du46.Now)[..5]}  {who}: {text}");
        Training.Note($"Радиосвязь, поезд №{n}: {who}: {text}");
        Changed?.Invoke();
    }

    static void Say(int n, string text) => Line(n, "ДСП", text);
    static void Reply(int n, string text) => Line(n, $"Машинист поезда №{n}", text);

    /// <summary>Где поезд: «Ст. АКЖТ, участок 3П» и с новой строки — стоит / следует (строки переводятся по отдельности).</summary>
    public static string Where(VirtualTrain t) =>
        t == null ? "" : $"Ст. {DispatchLink.StationName}, участок {t.Head?.Label}\n" + (t.Waiting.Length > 0 ? t.Waiting : "следует");

    /// <summary>Поезд на макете (номер по датчикам): только запись переговоров, поезд ведут руками.</summary>
    static bool Physical(VirtualTrain t, int n, string cmd)
    {
        if (t != null) return false;
        Say(n, cmd);
        Reply(n, "Понял, выполняю");
        return true;
    }

    public static void Stop(VirtualTrain t, int n)
    {
        if (Physical(t, n, "Остановитесь!")) return;
        Say(n, "Остановитесь!");
        t.StopOrder = true;
        Reply(n, $"Понял, останавливаюсь на {t.Head?.Label}");
    }

    public static void Go(VirtualTrain t, int n)
    {
        if (Physical(t, n, "Продолжайте движение по сигналам")) return;
        Say(n, "Продолжайте движение по сигналам");
        t.StopOrder = false;
        t.Waiting = "";
        Reply(n, "Понял, следую по сигналам");
    }

    /// <summary>«Указание» — регистрируемый приказ на проследование запрещающего сигнала. Записывается в ДУ-58.</summary>
    public static void Order(VirtualTrain t, int n, string signal, int number, string text)
    {
        Journals.AddAuto("ДУ-58", new()
        {
            ["no"] = number.ToString(), ["time"] = Training.Fmt(Du46.Now)[..5], ["from"] = $"ДСП {Du46Student}",
            ["to"] = $"машинисту поезда №{n}", ["text"] = text,
        }, "регистрируемый приказ ДСП по радиосвязи");
        Say(n, $"Приказ №{number}: {text}");
        if (t != null)   // поезд на макете ведут руками — только запись
        {
            t.PermitOnce = text;
            t.StopOrder = false;
        }
        Reply(n, $"Приказ №{number} понял: проследовать запрещающий светофор {signal}");
    }

    static string Du46Student => Training.Student?.Name ?? "";

    public static void BackUp(VirtualTrain t, int n)
    {
        if (Physical(t, n, "Осадите состав на один участок назад")) return;
        Say(n, "Осадите состав на один участок назад");
        var tail = t.Cells[^1];
        var back = sim.Peek(tail, t.Dir == Dir.Right ? Dir.Left : Dir.Right);
        if (back?.Section == null || back.Section.TrainCount > 0 || back.Exit || back.Buffer)
        {
            Reply(n, "Осадить не могу: сзади нет свободного участка");
            return;
        }
        t.StopOrder = false;
        sim.Reverse(t);
        t.BackSteps = 1;
        Reply(n, "Понял, осаживаю");
    }

    public static void Repair(VirtualTrain t, int n)
    {
        if (Physical(t, n, "Разрешаю ремонт локомотива")) return;
        Say(n, "Разрешаю ремонт локомотива");
        if (!t.FaultEngine) { Reply(n, "Локомотив исправен, ремонт не требуется"); return; }
        repairs.Add((Du46.Now + rnd.Next(240, 481), n));
        Reply(n, "Понял, приступаю к ремонту. Доложу по готовности");
    }

    public static void Shorten(VirtualTrain t, int n)
    {
        if (Physical(t, n, "Сократите стоянку, отправляйтесь по сигналу")) return;
        Say(n, "Сократите стоянку, отправляйтесь по открытому сигналу");
        t.ShortStop = true;
        Reply(n, t.Passenger ? "Понял, стоянку сокращаю, к отправлению готов" : "Понял (стоянки по расписанию у поезда нет)");
    }

    /// <summary>Предупреждение на поезд: скорость (0 — снять) и сообщить машинисту действующие предупреждения.</summary>
    public static void WarnTrain(VirtualTrain t, int n, int speed)
    {
        var list = ActiveAll.Where(r => !r.IsWindow).Select(r => r.Text).ToList();
        string text = (speed > 0 ? $"Предупреждение на поезд: скорость не более {speed} км/ч. " : "") +
                      (list.Count > 0 ? "Действуют предупреждения: " + string.Join("; ", list) : "Других предупреждений нет");
        if (Physical(t, n, text)) return;
        Say(n, text);
        t.SpeedLimit = speed;
        t.Warned = true;
        Reply(n, "Предупреждение понял");
    }

    public static void AskWhere(VirtualTrain t, int n)
    {
        Say(n, "Сообщите, где находитесь");
        if (t == null) { Reply(n, "Нахожусь на макете, " + HwWhere(n)); return; }
        Reply(n, Where(t));
    }

    // =================================================================
    //  НОМЕР И ИНДЕКС, ПАСПОРТ ПОЕЗДА (передаётся соседу вместе с поездом)
    // =================================================================

    /// <summary>Задать номер и индекс (окно «Номер и индекс…»). null — получилось, иначе причина отказа.</summary>
    public static string SetInfo(VirtualTrain t, int number, string index, string category, bool lng, bool heavy, bool oversize, int wagons)
    {
        if (number <= 0) return "номер поезда — целое число больше нуля";
        if (number != t.Number && sim.Trains.Any(x => x != t && !x.Leaving && x.Number == number))
            return $"поезд №{number} уже есть на станции";
        if (number != t.Number)
        {
            il.Say($"Поезду №{t.Number} присвоен номер {number}", LogKind.Train);
            t.Number = number;
        }
        t.Index = index.Trim();
        t.Category = category;
        (t.Long, t.Heavy, t.Oversize, t.Wagons) = (lng, heavy, oversize, wagons);
        Shunting.EnsureConsist(t);   // [ДОБАВЛЕНО 23.10.2026, этап 7] стал грузовым — натурный лист
        if (t.Consist.Count > 0) t.Wagons = t.Consist.Count;   // есть натурный лист — вагонов столько, сколько в нём
        Training.Note($"Поезд №{t.Number}: индекс {t.Index}, {t.Category}{Marks(t)}");
        il.NotifyChanged();
        Changed?.Invoke();
        return null;
    }

    /// <summary>Номер не по направлению: чётные — чётному (→), нечётные — нечётному (←).</summary>
    public static bool WrongParity(int number, Dir dir) => (number % 2 == 0) != (dir == Dir.Right);

    public static string Marks(VirtualTrain t) =>
        (t.Long ? ", длинносоставный" : "") + (t.Heavy ? ", тяжеловесный" : "") + (t.Oversize ? ", негабаритный" : "") +
        (t.Wagons > 0 ? $", {t.Wagons} ваг." : "");

    public static object Passport(VirtualTrain t) => new
    {
        index = t.Index, category = t.Category, lng = t.Long, heavy = t.Heavy, oversize = t.Oversize, wagons = t.Wagons,
        speed = t.SpeedLimit, warned = t.Warned,
        consist = Shunting.ConsistWire(t),   // [ДОБАВЛЕНО 23.10.2026, этап 7] натурный лист
    };

    public static void ApplyPassport(VirtualTrain t, JsonElement d)
    {
        if (t == null || d.ValueKind != JsonValueKind.Object) return;
        string S(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        bool B(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
        int I(string n) => d.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : 0;
        t.Index = S("index");
        if (S("category").Length > 0) t.Category = S("category");
        (t.Long, t.Heavy, t.Oversize, t.Wagons, t.SpeedLimit, t.Warned) = (B("lng"), B("heavy"), B("oversize"), I("wagons"), I("speed"), B("warned"));
        if (d.TryGetProperty("consist", out var c)) Shunting.ApplyConsist(t, c);   // [ДОБАВЛЕНО 23.10.2026, этап 7]
    }

    // =================================================================
    //  ПОЕЗД НА МАКЕТЕ: номер по участкам
    // =================================================================

    /// <summary>Присвоить номер настоящему поезду, который занимает участок по датчику (0 — снять).</summary>
    public static void SetHwNumber(TrackSection s, int number)
    {
        foreach (var x in il.Sections.Values.Where(x => x.HwTrainNo == s.HwTrainNo && s.HwTrainNo > 0)) x.HwTrainNo = 0;
        s.HwTrainNo = number;
        il.Say(number > 0 ? $"Поезду на макете (участок {s.Label}) присвоен номер {number}" : $"Номер поезда на макете (участок {s.Label}) снят", LogKind.Train);
        SpreadHwNumbers();
        il.NotifyChanged();
    }

    /// <summary>Номер переходит за поездом: на занятые датчиком соседние участки; освободившийся участок номер теряет.</summary>
    static void SpreadHwNumbers()
    {
        bool changed = false;
        foreach (var s in il.Sections.Values.Where(s => s.HwTrainNo > 0 && !s.HwOcc).ToList())
        {
            s.HwTrainNo = 0;
            changed = true;
        }
        for (bool more = true; more;)
        {
            more = false;
            foreach (var s in il.Sections.Values.Where(s => s.HwTrainNo > 0).ToList())
                foreach (var d in new[] { Dir.Left, Dir.Right })
                    if (sim.Peek(s, d)?.Section is { HwOcc: true, HwTrainNo: 0 } nb)
                    {
                        nb.HwTrainNo = s.HwTrainNo;
                        more = changed = true;
                    }
        }
        if (changed) il.NotifyChanged();
    }

    public static string HwWhere(int n) =>
        string.Join(", ", il.Sections.Values.Where(s => s.HwTrainNo == n).Select(s => s.Label)) is { Length: > 0 } w ? $"участки {w}" : "место неизвестно";

    /// <summary>Номера поездов на макете, которые сейчас на схеме.</summary>
    public static IEnumerable<int> HwNumbers => il.Sections.Values.Where(s => s.HwTrainNo > 0).Select(s => s.HwTrainNo).Distinct();

    // =================================================================
    //  УРОК
    // =================================================================

    public static void ResetLesson()
    {
        Restrictions.Clear();
        Talks.Clear();
        repairs.Clear();
        lastHead.Clear();
        du61Reported.Clear();
        restrictionNo = 0;
        orderNo = 100 + rnd.Next(0, 50) * 10;
        Apply();
        Changed?.Invoke();
    }

    /// <summary>Для отчёта: предупреждения и «окна» за урок.</summary>
    public static IEnumerable<string[]> ReportRows() => Restrictions.Select(r => new[]
    {
        r.No.ToString(), r.IsWindow ? "окно" : "предупреждение", r.ObjLabel, r.Place, r.IsWindow ? r.Service : r.SpeedText,
        Training.Fmt(r.From)[..5], r.Until < 0 ? "до отмены" : Training.Fmt(r.Until)[..5],
        r.Cancelled ? Training.Fmt(r.CancelledAt)[..5] : "действует",
        r.OrderNo > 0 ? $"№{r.OrderNo}" + (r.CancelOrderNo > 0 ? $", №{r.CancelOrderNo}" : "") : "",
    });

    public static void NotifyChanged() => Changed?.Invoke();
}
