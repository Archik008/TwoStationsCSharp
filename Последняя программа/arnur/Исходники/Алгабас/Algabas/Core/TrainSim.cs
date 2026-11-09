namespace StationApp.Core;

// =====================================================================
//  ВИРТУАЛЬНЫЕ ПОЕЗДА
//  Поезд "едет" по участкам: занимает их и освобождает. Останавливается
//  перед запрещающим светофором, перед стрелкой без контроля или не по
//  маршруту, перед занятым участком и у упора тупика.
//  Нужен, чтобы показывать всю логику без макета (например, специалистам).
//
//  [ДОБАВЛЕНО 20.10.2026, правки преподавателя] У поезда есть РЕЖИМ и его можно РАЗВЕРНУТЬ:
//   • поездной — едет по поездным показаниям (жёлтый, зелёный, пригласительный); перед лунно-белым стоит;
//   • маневровый — едет только на лунно-белый; на перегон и в тоннель не уходит.
//  Режим и разворот — ПКМ по поезду на схеме (SetShunting, Reverse).
// =====================================================================

/// <summary>Куда поезд попадёт из участка при движении в направлении dir.</summary>
public class TrackStep
{
    public TrackSection Section;       // следующий участок (null — уход со схемы или упор)
    public Dir Dir;                    // направление после шага (в петле разворачивается)
    public RailSignal Signal;          // светофор на границе (если есть)
    public bool Exit;                  // поезд уходит со схемы (на соседнюю станцию)
    public string ExitName = "";
    public bool Buffer;                // упор тупика
}

/// <summary>Топология схемы: следующий шаг или null (дальше хода нет — стрелка не так).</summary>
public delegate TrackStep NextStep(TrackSection from, Dir dir);

public class VirtualTrain
{
    public int Number;
    public Dir Dir;
    /// <summary>[ДОБАВЛЕНО 20.10.2026] Маневровый состав (едет на лунно-белый), иначе поездной.</summary>
    public bool Shunting;
    public string ModeText => Shunting ? "маневровый" : "поездной";
    public readonly List<TrackSection> Cells = new();   // [0] — голова
    public double Timer;
    public bool Leaving;
    public string Waiting = "";                          // почему стоит
    public TrackSection Head => Cells.Count > 0 ? Cells[0] : null;

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправности подвижной единицы (Core/Faults.cs):
    /// двигатель — стоит; тормоза — не останавливается у запрещающего; сход — стоит, участки заняты; негабарит — только сведения.</summary>
    public bool FaultEngine, FaultBrakes, Derailed, OutOfGauge;

    /// <summary>[ДОБАВЛЕНО 21.10.2026, этап 6] У машиниста ключ-жезл этого перегона (Core/Panel.cs).</summary>
    public DirectionLine KeyLine;

    // ---- [ДОБАВЛЕНО 23.10.2026, этап 4] поезд как объект, радиосвязь, бланки (Core/Radio.cs) ----
    /// <summary>Индекс поезда «6762-076-6762», категория (SchedPoints.Categories), особые отметки.</summary>
    public string Index = "";
    public string Category = "грузовой";
    public bool Long, Heavy, Oversize;
    /// <summary>Длина в условных вагонах (0 — не задана).</summary>
    public int Wagons;
    /// <summary>«Остановка» по радиосвязи: стоит, пока ДСП не скажет «Продолжить».</summary>
    public bool StopOrder;
    /// <summary>«Осаживание»: сколько участков ещё проехать назад.</summary>
    public int BackSteps;
    /// <summary>«Сократить стоянку»: стоянка по расписанию отменена.</summary>
    public bool ShortStop;
    /// <summary>Предупреждение на поезд: не быстрее, км/ч (0 — нет).</summary>
    public int SpeedLimit;
    /// <summary>«Указание» (регистрируемый приказ): проследовать один запрещающий светофор — текст приказа.</summary>
    public string PermitOnce = "";
    /// <summary>Бланк разрешения (ДУ-50/52/54/56/64): проследовать запрещающий выходной — вид бланка.</summary>
    public string PermitExit = "";
    /// <summary>По перегону — не по сигналам автоблокировки (ДУ-50, ДУ-56, ДУ-64).</summary>
    public bool IgnoreAutoSignals;
    /// <summary>Машинист предупреждён о действующих предупреждениях (ДУ-61 или по радиосвязи).</summary>
    public bool Warned;
    public bool Passenger => SchedPoints.IsPassenger(Category);

    // ---- [ДОБАВЛЕНО 23.10.2026, этап 7] вагоны и маневровая работа (Core/Shunting.cs) ----
    /// <summary>Натурный лист: вагоны от локомотива по порядку.</summary>
    public readonly List<Wagon> Consist = new();
    /// <summary>Вагоны без локомотива (отцеплены на пути): сами не двигаются, должны быть закреплены башмаками.</summary>
    public bool NoLoco;
    /// <summary>Поезд сформирован на станции и предъявлен к осмотру / осмотрен (ПТО).</summary>
    public bool FormedHere, InspectAsked, Inspected;
}

/// <summary>[ДОБАВЛЕНО 23.10.2026, этап 7] Вагон: номер (первая цифра — род), гружёный / порожний, куда (ПНП станции или транзит).</summary>
public class Wagon
{
    public string Number { get; set; } = "";
    /// <summary>кр — крытый, пл — платформа, пв — полувагон, цс — цистерна, пр — прочий.</summary>
    public string Kind { get; set; } = "пр";
    public bool Loaded { get; set; }
    /// <summary>ПНП назначения на этой станции (местный вагон), пусто — транзитный.</summary>
    public string Dest { get; set; } = "";
    /// <summary>Грузовая операция на ПНП пройдена.</summary>
    public bool Done { get; set; }
    /// <summary>Под вагоном тормозной башмак.</summary>
    public bool Shoe { get; set; }
    public string State => Done ? "после грузовой операции" : Dest.Length > 0 ? $"местный, на ПНП «{Dest}»" : "транзит";
    public override string ToString() => $"{Number} {Kind} {(Loaded ? "гружёный" : "порожний")}";
}

public class TrainSim
{
    readonly Interlocking il;
    readonly NextStep next;

    public readonly List<VirtualTrain> Trains = new();
    /// <summary>Секунд на проход одного участка.</summary>
    public double StepSeconds = 2.0;
    /// <summary>Длина поезда в участках.</summary>
    public int Length = 2;
    int counter;

    /// <summary>[ДОБАВЛЕНО 12.10.2026] Поезд уходит со схемы (на соседнюю станцию): поезд, участок, с которого
    /// уходит, шаг топологии. Связь диспетчеров передаёт такой поезд соседу.</summary>
    public event Action<VirtualTrain, TrackSection, TrackStep> Exited;
    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Поезд проехал светофор с запрещающим показанием (отказ тормозов).</summary>
    public event Action<VirtualTrain, RailSignal> PassedAtDanger;
    /// <summary>[ДОБАВЛЕНО 22.10.2026, этап 5] Почему поезд должен стоять (стоянка по расписанию, Core/Schedule.cs), null — может ехать.</summary>
    public Func<VirtualTrain, string> Hold;

    // ---- [ДОБАВЛЕНО 23.10.2026, этап 4] правила движения из Core/Radio.cs ----
    /// <summary>Во сколько раз медленнее (предупреждения): 1 — как обычно.</summary>
    public Func<VirtualTrain, double> SpeedFactor;
    /// <summary>Можно ли проехать запрещающий светофор (приказ, бланк) — true: можно, разрешение использовано.</summary>
    public Func<VirtualTrain, TrackStep, bool> Permit;
    /// <summary>Почему нельзя вступить на участок («окно»), null — можно.</summary>
    public Func<VirtualTrain, TrackSection, string> Blocked;
    /// <summary>Поезд появился на схеме (номер, категория по умолчанию).</summary>
    public event Action<VirtualTrain> Spawned;
    /// <summary>Поезд закончил осаживание.</summary>
    public event Action<VirtualTrain> BackedUp;
    /// <summary>Куда поезд попадёт из участка (без проверок) — соседний участок для номера поезда на макете.</summary>
    public TrackStep Peek(TrackSection from, Dir dir) => next(from, dir);
    public void OnSpawned(VirtualTrain t) => Spawned?.Invoke(t);

    public TrainSim(Interlocking interlocking, NextStep topology)
    {
        il = interlocking;
        next = topology;
    }

    /// <summary>[ДОБАВЛЕНО 12.10.2026] Принять поезд от соседней станции: он появляется на общем участке
    /// перегона с тем же номером. Занятость этого участка соседом (RemoteOcc) не мешает — это он и есть.</summary>
    public VirtualTrain SpawnFromNeighbor(TrackSection at, Dir dir, int number, string from)
    {
        if (at.TrainCount > 0)
        {
            il.Say($"Поезд №{number} от станции {from} не принят: на участке {at.Label} уже стоит поезд", LogKind.Refuse);
            return null;
        }
        var t = new VirtualTrain { Number = number, Dir = dir };
        t.Cells.Add(at);
        at.TrainCount++;
        Trains.Add(t);
        Spawned?.Invoke(t);   // [ДОБАВЛЕНО 23.10.2026, этап 4]
        il.Say($"Поезд №{t.Number} прибыл от станции {from} на участок {at.Label} ({(dir == Dir.Right ? "→" : "←")})", LogKind.Train);
        il.ComputeAspects();
        il.NotifyChanged();
        return t;
    }

    public VirtualTrain Spawn(TrackSection at, Dir dir, bool shunting = false)
    {
        if (at.Occupied)
        {
            il.Say($"Нельзя поставить поезд: участок {at.Label} занят", LogKind.Refuse);
            return null;
        }
        var t = new VirtualTrain { Number = ++counter * 2 - (dir == Dir.Right ? 0 : 1), Dir = dir, Shunting = shunting };
        // чётные номера — чётное направление (→), нечётные — нечётное (←), как на ЖД
        t.Cells.Add(at);
        at.TrainCount++;
        Trains.Add(t);
        Spawned?.Invoke(t);   // [ДОБАВЛЕНО 23.10.2026, этап 4]
        il.Say($"Поезд №{t.Number} ({t.ModeText}) появился на участке {at.Label} ({(dir == Dir.Right ? "→" : "←")})", LogKind.Train);
        il.NotifyChanged();
        return t;
    }

    /// <summary>Поезд, который стоит (или частично стоит) на участке, — для ПКМ по схеме.</summary>
    public VirtualTrain At(TrackSection s) => Trains.FirstOrDefault(t => !t.Leaving && t.Cells.Contains(s));

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Поезд по номеру (null — нет на схеме или уходит).</summary>
    public VirtualTrain Find(int number) => Trains.FirstOrDefault(t => !t.Leaving && t.Number == number);

    /// <summary>[ДОБАВЛЕНО 20.10.2026] Развернуть поезд: голова и хвост меняются местами, направление — на обратное.</summary>
    public bool Reverse(VirtualTrain t)
    {
        if (t.Leaving)
        {
            il.Say($"Поезд №{t.Number} уже уходит со схемы — развернуть нельзя", LogKind.Refuse);
            return false;
        }
        t.Cells.Reverse();
        t.Dir = t.Dir == Dir.Right ? Dir.Left : Dir.Right;
        t.Timer = 0;
        t.Waiting = "";
        il.Say($"Поезд №{t.Number} развёрнут: теперь едет {(t.Dir == Dir.Right ? "→" : "←")}", LogKind.Train);
        il.NotifyChanged();
        return true;
    }

    /// <summary>[ДОБАВЛЕНО 20.10.2026] Сменить режим поезда: поездной / маневровый.</summary>
    public void SetShunting(VirtualTrain t, bool shunting)
    {
        if (t.Shunting == shunting) return;
        t.Shunting = shunting;
        t.Waiting = "";
        il.Say($"Поезд №{t.Number}: режим — {t.ModeText}", LogKind.Train);
        il.NotifyChanged();
    }

    public void RemoveAll()
    {
        foreach (var t in Trains)
            foreach (var c in t.Cells) c.TrainCount = Math.Max(0, c.TrainCount - 1);
        if (Trains.Count > 0) il.Say("Все виртуальные поезда убраны", LogKind.Train);
        Trains.Clear();
        il.NotifyChanged();
    }

    public void Tick(double dt)
    {
        foreach (var t in Trains.ToList())
        {
            if ((t.FaultEngine || t.Derailed) && !t.Leaving)
            {
                t.Timer = 0;
                t.Waiting = $"стоит на {t.Head?.Label}";   // дежурный узнаёт причину от машиниста
                continue;
            }
            if (t.NoLoco)   // [ДОБАВЛЕНО 23.10.2026, этап 7] отцепленные вагоны сами не едут
            {
                t.Timer = 0;
                t.Waiting = $"вагоны без локомотива на {t.Head?.Label}";
                continue;
            }
            if (t.StopOrder && !t.Leaving)   // [ДОБАВЛЕНО 23.10.2026, этап 4] «Остановка» по радиосвязи
            {
                t.Timer = 0;
                Wait(t, $"стоит на {t.Head?.Label}: остановлен дежурным по радиосвязи");
                continue;
            }
            if (!t.Leaving && Hold?.Invoke(t) is { } hold)   // [ДОБАВЛЕНО 22.10.2026, этап 5] стоянка по расписанию
            {
                t.Timer = 0;
                Wait(t, hold);
                continue;
            }
            t.Timer += dt;
            // 1e-6 — от накопления погрешности 0.1+0.1+…; [ИЗМЕНЕНО 23.10.2026, этап 4] предупреждения — медленнее
            if (t.Timer + 1e-6 < StepSeconds * (SpeedFactor?.Invoke(t) ?? 1)) continue;
            t.Timer = 0;
            Advance(t);
        }
    }

    void Advance(VirtualTrain t)
    {
        if (t.Leaving)
        {
            var tail = t.Cells[^1];
            t.Cells.RemoveAt(t.Cells.Count - 1);
            tail.TrainCount = Math.Max(0, tail.TrainCount - 1);
            if (t.Cells.Count == 0)
            {
                Trains.Remove(t);
                il.Say($"Поезд №{t.Number} ушёл со схемы", LogKind.Train);
            }
            il.NotifyChanged();
            return;
        }

        var head = t.Head;
        var step = next(head, t.Dir);
        if (step == null && TryCut(t, head) is { } cut) step = cut;   // [ДОБАВЛЕНО 21.10.2026, этап 6] взрез
        if (step == null)
        {
            Wait(t, $"стоит на {head.Label}: дальше хода нет (стрелка не по маршруту)");
            return;
        }

        if (step.Signal != null && step.Signal.Dir == t.Dir)
        {
            var why = SignalStop(t, step);
            if (why != null && step.Signal.Kind != SignalKind.Neighbor && Permit?.Invoke(t, step) == true) why = null;   // [ДОБАВЛЕНО 23.10.2026, этап 4] приказ, бланк
            if (why != null && t.FaultBrakes && !t.Shunting && step.Signal.Kind != SignalKind.Neighbor)
            {
                il.Say($"Поезд №{t.Number} проследовал светофор {step.Signal.Label} с запрещающим показанием", LogKind.Refuse);
                PassedAtDanger?.Invoke(t, step.Signal);
                why = null;
            }
            if (why != null)
            {
                Wait(t, why);
                return;
            }
        }

        if (step.Buffer)
        {
            Wait(t, $"стоит у упора тупика ({head.Label})");
            return;
        }

        if (step.Exit && t.KeyLine != null)
        {
            // [ДОБАВЛЕНО 21.10.2026, этап 6] локомотив с ключ-жезлом соседняя станция не принимает — он возвращается
            Wait(t, $"стоит на {head.Label}: с ключ-жезлом на соседнюю станцию не принимается — вернуть на станцию");
            return;
        }

        if (step.Exit && t.Shunting)
        {
            Wait(t, $"стоит на {head.Label}: маневровый состав не уходит со станции ({step.ExitName})");
            return;
        }

        if (step.Exit)
        {
            t.Leaving = true;
            t.Waiting = "";
            il.Say($"Поезд №{t.Number} проследовал {step.Signal?.Label ?? ""} и уходит на {step.ExitName}", LogKind.Train);
            Exited?.Invoke(t, head, step);
            if (step.Signal != null && step.Signal.Kind == SignalKind.Neighbor && step.Signal.NeighborOpen)
            {
                step.Signal.NeighborOpen = false;   // сосед закрыл входной за поездом
                il.Say($"{step.ExitName}: входной {step.Signal.Label} закрыт после проследования поезда", LogKind.Train);
            }
            Advance(t);
            return;
        }

        var nextSec = step.Section;
        if (nextSec.TrainCount > 0)
        {
            // [ДОБАВЛЕНО 23.10.2026, этап 7] маневровый подъезжает к отцепленным вагонам — дальше сцепка (Core/Shunting.cs)
            bool wagons = t.Shunting && Trains.Where(x => x.Cells.Contains(nextSec)).All(x => x.NoLoco);
            Wait(t, wagons ? $"стоит на {head.Label}: подъехал к вагонам на {nextSec.Label} — можно сцепить"
                           : $"стоит: впереди на {nextSec.Label} другой поезд");
            return;
        }
        if (Blocked?.Invoke(t, nextSec) is { } blocked)   // [ДОБАВЛЕНО 23.10.2026, этап 4] «окно»
        {
            Wait(t, blocked);
            return;
        }
        // [ИЗМЕНЕНО 21.10.2026, этап 6] стрелку без контроля, запертую на закладку / скобу, проезжают (курбель, выключение)
        var noControl = il.Switches.Values.FirstOrDefault(sw => sw.Sections.Contains(nextSec) && !sw.Controlled && sw.Clamp.Length == 0 &&
                                                               !(sw.Cut && cutBy.TryGetValue(t, out var c) && c == sw));   // свою взрезанную проходит
        if (noControl != null)
        {
            Wait(t, $"стоит: стрелка {noControl.Label} без контроля");
            return;
        }

        t.Waiting = "";
        t.Dir = step.Dir;
        t.Cells.Insert(0, nextSec);
        nextSec.TrainCount++;
        if (t.Cells.Count > Length)
        {
            var tail = t.Cells[^1];
            t.Cells.RemoveAt(t.Cells.Count - 1);
            tail.TrainCount = Math.Max(0, tail.TrainCount - 1);
        }
        il.ComputeAspects();   // автоблокировка реагирует сразу, как рельсовая цепь
        il.NotifyChanged();
        if (t.BackSteps > 0 && --t.BackSteps == 0) BackedUp?.Invoke(t);   // [ДОБАВЛЕНО 23.10.2026, этап 4] осаживание закончено
    }

    /// <summary>Почему поезд не может проехать светофор попутного направления (null — может).
    /// [ИЗМЕНЕНО 20.10.2026] Поездной едет по поездным показаниям, маневровый — только на лунно-белый.</summary>
    static string SignalStop(VirtualTrain t, TrackStep step)
    {
        var s = step.Signal;
        var a = s.Aspect;
        // [ДОБАВЛЕНО 21.10.2026, этап 6] с ключ-жезлом по перегону едут и против установленного направления (проходные погашены)
        if (t.KeyLine != null && a == Aspect.Dark) return null;
        // Маневровый светофор внутри заданного маршрута состав не касается (он остаётся синим):
        // поездной проезжает его в поездном маршруте, маневровый — в маневровом.
        bool shuntInRoute = s.IsShuntOnly && step.Section != null && step.Section.LockedBy != null &&
                            step.Section.LockedBy.Shunting == t.Shunting;
        if (shuntInRoute) return null;

        if (t.Shunting)
            return a == Aspect.White ? null
                : $"стоит у светофора {s.Label} ({AspectInfo.Short(a)}): маневровому составу нужен лунно-белый";

        if (a == Aspect.White)
            return $"стоит у светофора {s.Label} (лунно-белый): это разрешение на манёвры, поездному нужен поездной сигнал";
        return AspectInfo.IsPermissive(a) ? null : $"стоит у светофора {s.Label} ({AspectInfo.Short(a)})";
    }

    /// <summary>[ДОБАВЛЕНО 21.10.2026, этап 6] Взрез: дальше хода нет, потому что стрелка стоит не по ходу поезда,
    /// идущего с пошёрстного направления. Ищем стрелку, переведя которую путь появится. Взрез — только у поезда,
    /// который едет по маршруту (участок замкнут) или не может остановиться (отказ тормозов); иначе поезд просто стоит.</summary>
    TrackStep TryCut(VirtualTrain t, TrackSection head)
    {
        if (head.LockedBy == null && !t.FaultBrakes) return null;
        foreach (var sw in il.Switches.Values)
        {
            if (sw.MoveTimer > 0 || sw.Clamp.Length > 0 || sw.Cut) continue;
            sw.Plus = !sw.Plus;
            var s2 = next(head, t.Dir);
            sw.Plus = !sw.Plus;
            if (s2?.Section == null || !sw.Sections.Contains(s2.Section)) continue;
            il.CutSwitch(sw, t);
            cutBy[t] = sw;
            return next(head, t.Dir);
        }
        return null;
    }

    /// <summary>Какую стрелку взрезал поезд — её он проходит, остальные поезда перед ней стоят.</summary>
    readonly Dictionary<VirtualTrain, RailSwitch> cutBy = new();

    void Wait(VirtualTrain t, string why)
    {
        // Остановился — "подтягивается" к светофору: поезд короче участка и целиком
        // помещается на нём, хвост освобождает участок сзади (он может разомкнуться).
        if (t.Cells.Count > 1)
        {
            for (int i = t.Cells.Count - 1; i >= 1; i--)
            {
                var c = t.Cells[i];
                c.TrainCount = Math.Max(0, c.TrainCount - 1);
                t.Cells.RemoveAt(i);
            }
            il.ComputeAspects();
            il.NotifyChanged();
        }
        if (t.Waiting == why) return;
        t.Waiting = why;
        il.Say($"Поезд №{t.Number} {why}", LogKind.Train);
    }
}
