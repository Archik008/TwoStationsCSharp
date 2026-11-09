namespace StationApp.Core;

// =====================================================================
//  ВОССТАНОВИТЕЛЬНЫЙ ПОЕЗД  [ДОБАВЛЕНО 23.10.2026, этап 7]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Окно вызова — Ui/ShuntUi.cs.
//
//  Как в тренажёре ДСП (часть 2, раздел 11):
//   • сход подвижного состава (неисправность «Сход» от преподавателя) → машинист сообщает по радиосвязи;
//     ПД осматривает и сообщает, что нужен восстановительный поезд;
//   • ПКМ по сошедшему поезду (или радиосвязь) → «Вызвать восстановительный поезд…»: поезд №8002 появляется
//     на подходе к станции через RecoveryArriveMin минут, его подводят маршрутами к месту схода (в голову или хвост);
//   • прибыл — доклад НВП, работы RecoveryWorkMin минут, доклад о ликвидации схода: неисправность устранена,
//     поезду устанавливается скорость не более 40 км/ч — выдать предупреждение на поезд (радиосвязь),
//     иначе при отправлении — неправильное действие.
// =====================================================================

public class RecoveryCall
{
    public int Victim;
    public int Number;
    public double CalledAt, ArriveAt, WorkUntil = -1;
    /// <summary>вызван → в пути (на схеме) → работа → готово</summary>
    public string State = "вызван";
    public string Where = "";
}

public static class Recovery
{
    public const int SpeedAfter = 40;
    public static double ArriveMin = 10, WorkMin = 15;
    public static readonly List<RecoveryCall> Calls = new();
    public static event Action Changed;

    static Interlocking il;
    static TrainSim sim;
    static int nextNo = 8002;
    /// <summary>Поезда после схода: нужно предупреждение «не более 40 км/ч» до отправления.</summary>
    static readonly HashSet<int> mustWarn = new();
    static readonly Dictionary<int, TrackSection> victimHead = new();

    public static void Init(Interlocking interlocking, TrainSim trains)
    {
        il = interlocking;
        sim = trains;
        il.Ticked += Tick;
    }

    public static bool Derailed(VirtualTrain t) => t != null && t.Derailed;

    /// <summary>Вызвать восстановительный поезд к поезду (null — вызван, иначе причина).</summary>
    public static string Call(VirtualTrain victim)
    {
        if (victim == null || !victim.Derailed) return "у поезда нет схода — восстановительный поезд не нужен";
        if (Calls.Any(c => c.Victim == victim.Number && c.State != "готово")) return $"к поезду №{victim.Number} восстановительный поезд уже вызван";
        var c = new RecoveryCall { Victim = victim.Number, Number = nextNo, CalledAt = Du46.Now, ArriveAt = Du46.Now + ArriveMin * 60, Where = victim.Head?.Label ?? "" };
        nextNo += 2;
        Calls.Add(c);
        Training.Note($"Вызван восстановительный поезд №{c.Number} к поезду №{c.Victim} ({c.Where}), подход в {Training.Fmt(c.ArriveAt)[..5]}");
        Du46.Message("ДНЦ", Du46.WorkerName("ДНЦ"), "T:" + c.Victim,
            $"Восстановительный поезд №{c.Number} назначен к поезду №{c.Victim} на ст. {DispatchLink.StationName} ({c.Where}). " +
            $"Подход к станции в {Training.Fmt(c.ArriveAt)[..5]}. Примите и пропустите его к месту схода.", false, record: false);
        Changed?.Invoke();
        return null;
    }

    static void Tick(double dt)
    {
        double now = Du46.Now;
        foreach (var c in Calls.ToList())
        {
            switch (c.State)
            {
                case "вызван" when now >= c.ArriveAt:
                    Appear(c);
                    break;
                case "в пути":
                {
                    var rt = sim.Find(c.Number);
                    var v = sim.Find(c.Victim);
                    if (rt == null || v == null) { if (v == null) c.State = "готово"; break; }
                    var ahead = rt.Head == null ? null : sim.Peek(rt.Head, rt.Dir)?.Section;
                    if (v.Cells.Contains(ahead) || v.Cells.Intersect(rt.Cells).Any())
                    {
                        c.State = "работа";
                        c.WorkUntil = now + WorkMin * 60;
                        rt.StopOrder = true;
                        Du46.Message("НВП", "Бекетов", "T:" + c.Victim, $"Прибыл на место схода поезда №{c.Victim}. Приступаю к работам.\nНВП Бекетов", false, record: false);
                        Training.Note($"Восстановительный поезд №{c.Number} прибыл к поезду №{c.Victim}, работы до {Training.Fmt(c.WorkUntil)[..5]}");
                        Changed?.Invoke();
                    }
                    break;
                }
                case "работа" when now >= c.WorkUntil:
                {
                    c.State = "готово";
                    Faults.FixedBy(c.Victim, "tr_derail", $"сход ликвидирован восстановительным поездом №{c.Number}");
                    mustWarn.Add(c.Victim);
                    if (sim.Find(c.Victim) is { } v) victimHead[c.Victim] = v.Head;
                    if (sim.Find(c.Number) is { } rt) rt.StopOrder = false;
                    Du46.Message("НВП", "Бекетов", "T:" + c.Victim,
                        $"На ст. {DispatchLink.StationName} сход поезда №{c.Victim} ликвидирован. Габарит в норме, разрешается движение поездов " +
                        $"с установленной скоростью. Поезду №{c.Victim} устанавливается скорость не более {SpeedAfter} км/ч до ближайшего ПТО.\nНВП Бекетов",
                        true, record: false);
                    Changed?.Invoke();
                    break;
                }
            }
        }
        // поезд после схода тронулся без предупреждения «не более 40 км/ч»
        foreach (var n in mustWarn.ToList())
        {
            var v = sim.Find(n);
            if (v == null) { mustWarn.Remove(n); continue; }
            if (victimHead.TryGetValue(n, out var h) && h == v.Head) continue;
            mustWarn.Remove(n);
            if (v.SpeedLimit is <= 0 or > SpeedAfter)
                Training.AddEvent(false, "recovery", $"Поезд №{n} после ликвидации схода отправлен без предупреждения (скорость не более {SpeedAfter} км/ч)", 5);
        }
    }

    static void Appear(RecoveryCall c)
    {
        foreach (var (at, dir) in Schedule.EntryPoints)
        {
            if (at.Occupied) continue;
            var t = new VirtualTrain { Number = c.Number, Dir = dir, Category = "восстановительный" };
            t.Cells.Add(at);
            at.TrainCount++;
            sim.Trains.Add(t);
            c.State = "в пути";
            il.Say($"Восстановительный поезд №{c.Number} появился на участке {at.Label}", LogKind.Train);
            Du46.Message("Машинист", $"восстановительного поезда №{c.Number}", "T:" + c.Number,
                $"Восстановительный поезд №{c.Number} появился на участке {at.Label}. Вызван к поезду №{c.Victim} ({c.Where}).", false);
            il.ComputeAspects();
            il.NotifyChanged();
            Changed?.Invoke();
            return;
        }
    }

    public static string StateText(RecoveryCall c) => c.State switch
    {
        "вызван" => $"вызван, подход в {Training.Fmt(c.ArriveAt)[..5]}",
        "в пути" => $"на станции: {sim.Find(c.Number)?.Head?.Label ?? "—"}",
        "работа" => $"работы до {Training.Fmt(c.WorkUntil)[..5]}",
        _ => "работы закончены"
    };

    public static void Begin(LessonTask l)
    {
        ArriveMin = l != null && l.RecoveryArriveMin > 0 ? l.RecoveryArriveMin : 10;
        WorkMin = l != null && l.RecoveryWorkMin > 0 ? l.RecoveryWorkMin : 15;
        Calls.Clear();
        mustWarn.Clear();
        victimHead.Clear();
        nextNo = 8002;
        Changed?.Invoke();
    }

    public static IEnumerable<string[]> ReportRows() => Calls.Select(c => new[]
    {
        $"№{c.Number}", $"№{c.Victim}", c.Where, Training.Fmt(c.CalledAt)[..5], Training.Fmt(c.ArriveAt)[..5], StateText(c),
    });
}
