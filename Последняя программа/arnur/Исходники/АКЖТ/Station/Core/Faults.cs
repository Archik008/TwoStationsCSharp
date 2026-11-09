using System.Text.Json;

namespace StationApp.Core;

// =====================================================================
//  НЕИСПРАВНОСТИ («Экспресс-вводные» преподавателя)  [ДОБАВЛЕНО 20.10.2026, этап 3]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//
//  Как в тренажёре ДСП (инструкция преподавателя, раздел 2):
//   • преподаватель выбирает объект (светофор, стрелку, участок, перегон, поезд), неисправность,
//     условие возникновения и время начала ожидания условия (t_fault_add);
//   • условие: нет / состояние X / переход в состояние X / переход из состояния X,
//     плюс подвижная единица на объекте: любая, по номеру, поездной, маневровый;
//   • неисправность включает флаги в модели (FaultOcc, FaultNoControl, FaultClosed… — Core/Model.cs),
//     ядро ведёт себя как при настоящем отказе, на макет команда неисправной стрелки не уходит;
//   • студент видит обычную картину пульта: надписей «неисправность» нет;
//   • устраняет «виртуальный работник» (ШН, ДНЦ, ПД) после записи в ДУ-46 и сообщения ему,
//     доклад нужно подписать (Core/Du46.cs), или преподаватель снимает неисправность сам (t_fault_remove).
//  Неисправности бывают только на занятии и живут по игровому времени.
// =====================================================================

/// <summary>Вид неисправности из каталога.</summary>
public record FaultKind(string Code, string Name, string Target, string Worker, string Cause, string Report);

public class Fault
{
    public string Id = "";
    public string Kind = "";
    public string Obj = "";
    /// <summary>none | state | to | from</summary>
    public string Cond = "none";
    public string CondState = "";
    /// <summary>none | any | number | train | shunt</summary>
    public string TrainFilter = "none";
    public int TrainNumber;
    /// <summary>Игровое время начала ожидания условия, &lt; 0 — немедленно.</summary>
    public double Start = -1;

    /// <summary>wait — ждёт условия; active — действует; fixing — устраняется; fixed; removed; gone (поезд ушёл).</summary>
    public string State = "wait";
    public bool Armed;
    public string Prev = "";
    public double ActivatedAt = -1, EndedAt = -1;
    /// <summary>Поезд, на который легла неисправность подвижной единицы (0 — настоящий поезд на макете).</summary>
    public int Train;
    public bool Triggered;
    public string Worker = "";
    public double ArriveAt, DoneAt;
    public bool Arrived, Reported, Checked;
    public readonly List<string> Log = new();

    public FaultKind K => Faults.KindOf(Kind);
    public bool Effective => State is "active" or "fixing";
    public bool IsTrainFault => K?.Target == "train";
}

public static class Faults
{
    // ------------------------ каталог ------------------------
    public static readonly FaultKind[] Kinds =
    {
        new("sig_noopen", "Светофор не открывается", "signal", "ШН",
            "Светофор {0} не открывается на разрешающее показание: обрыв в цепи сигнального реле. Приступаю к устранению.",
            "Неисправность светофора {0} устранена: восстановлена цепь сигнального реле. Светофор проверен, работает нормально."),
        new("sig_drop", "Самопроизвольное перекрытие светофора", "signal", "ШН",
            "Светофор {0} перекрылся из-за кратковременного нарушения контакта в цепи сигнального реле. Приступаю к устранению.",
            "Неисправность светофора {0} устранена: заменено сигнальное реле. Светофор проверен, работает нормально."),
        new("sw_noplus", "Невозможность перевода в положение «плюс»", "switch", "ШН",
            "Стрелка {0} не доходит в плюсовое положение: посторонний предмет между остряком и рамным рельсом. Устраняю.",
            "Стрелка {0}: посторонний предмет удалён, стрелка проверена, переводится нормально."),
        new("sw_nominus", "Невозможность перевода в положение «минус»", "switch", "ШН",
            "Стрелка {0} не доходит в минусовое положение: посторонний предмет между остряком и рамным рельсом. Устраняю.",
            "Стрелка {0}: посторонний предмет удалён, стрелка проверена, переводится нормально."),
        new("sw_nocontrol", "Потеря контроля положения", "switch", "ШН",
            "Стрелка {0} не имеет контроля из-за плохого контакта в автопереключателе. Устраняю неисправность.",
            "Неисправность стрелки {0} устранена. Стрелка проверена, работает нормально."),
        new("sw_gap", "Отжим 4 мм", "switch", "ШН",
            "Стрелка {0}: отжим остряка 4 мм, контроля положения нет. Регулирую стрелку.",
            "Стрелка {0}: отжим устранён, стрелка отрегулирована, контроль положения есть. Работает нормально."),
        new("sw_fromplus", "Невозможность перевода из положения «плюс»", "switch", "ШН",
            "Стрелка {0} не переводится из плюсового положения: неисправность электродвигателя привода. Устраняю.",
            "Стрелка {0}: электропривод отремонтирован, стрелка проверена, переводится нормально."),
        new("sw_fromminus", "Невозможность перевода из положения «минус»", "switch", "ШН",
            "Стрелка {0} не переводится из минусового положения: неисправность электродвигателя привода. Устраняю.",
            "Стрелка {0}: электропривод отремонтирован, стрелка проверена, переводится нормально."),
        // [ДОБАВЛЕНО 21.10.2026, этап 6] взрез: задаёт преподаватель или происходит сам (поезд с пошёрстного направления)
        new("sw_cut", "Взрез стрелки", "switch", "ШН",
            "Стрелка {0} взрезана: остряки отжаты, повреждены тяги. Осматриваю, приступаю к восстановлению.",
            "Стрелка {0} после взреза осмотрена и восстановлена, проверена, работает нормально."),
        new("sec_occ", "Ложная занятость", "section", "ШН",
            "Рельсовая цепь {0} показывает занятость без поезда: неисправность изолирующего стыка. Устраняю.",
            "Рельсовая цепь {0}: изолирующий стык заменён, рельсовая цепь проверена, работает нормально."),
        new("sec_free", "Ложная свободность", "section", "ШН",
            "Рельсовая цепь {0} не фиксирует занятость: обрыв перемычки путевого реле. Устраняю.",
            "Рельсовая цепь {0}: перемычка восстановлена, рельсовая цепь проверена, работает нормально."),
        new("line_nodir", "Невозможность смены направления", "line", "ШН",
            "{0}: не переключается реле направления автоблокировки. Устраняю.",
            "{0}: схема смены направления проверена, неисправность устранена, работает нормально."),
        new("line_free", "Ложная свободность перегона", "line", "ШН",
            "{0}: блок-участки не фиксируют занятость (неисправность путевых реле). Устраняю.",
            "{0}: путевые реле заменены, блок-участки проверены, работают нормально."),
        new("tr_engine", "Неисправность двигателя", "train", "ДНЦ",
            "Неисправность тягового двигателя, поезд остановился на {2}. Прошу помощь.",
            "{1}: неисправность локомотива устранена локомотивной бригадой, поезд может следовать дальше."),
        new("tr_brakes", "Неисправность тормозов", "train", "ДНЦ",
            "Отказ автотормозов! Остановить поезд не могу! Нахожусь на {2}.",
            "{1}: тормоза восстановлены, опробование тормозов произведено, поезд может следовать."),
        new("tr_gauge", "Негабаритность", "train", "ДНЦ",
            "В составе поезда вагон с негабаритным грузом, нахожусь на {2}.",
            "Принято. {1} с негабаритным грузом: принимать и пропускать по путям, где обеспечен габарит, движение по соседним путям не допускать."),
        new("tr_derail", "Сход подвижного состава", "train", "ПД",
            "Сход подвижного состава на {2}! Поезд остановлен.",
            "{2}: подвижной состав поставлен на рельсы, путь восстановлен и проверен. Движение открыто."),
    };

    public static FaultKind KindOf(string code) => Kinds.FirstOrDefault(k => k.Code == code);

    /// <summary>Состояния объектов для условий (как в окне «Экспресс-вводная»).</summary>
    public static readonly Dictionary<string, string[]> States = new()
    {
        ["signal"] = new[] { "открыт", "закрыт" },
        ["switch"] = new[] { "+", "−" },
        ["section"] = new[] { "свободен", "занят", "назначен" },
        ["line"] = new[] { "свободен", "занят" },
        ["train"] = new[] { "стоит", "движется" },
    };

    public static readonly List<Fault> List = new();
    static Interlocking il;
    static TrainSim trains;
    static int counter;
    static readonly Random rnd = new();

    /// <summary>Список изменился — отправить преподавателю (Training).</summary>
    public static event Action Changed;

    public static void Init(Interlocking interlocking, TrainSim sim)
    {
        il = interlocking;
        trains = sim;
        trains.PassedAtDanger += (t, s) =>
            Training.Note($"Поезд №{t.Number} с неисправными тормозами проследовал запрещающий светофор {s.Label}");
        il.SwitchCut += OnCut;
    }

    /// <summary>[ДОБАВЛЕНО 21.10.2026, этап 6] Поезд взрезал стрелку — на уроке это неисправность, её устраняет ШН.</summary>
    static void OnCut(RailSwitch sw, VirtualTrain t)
    {
        if (t == null || !Training.Active || List.Any(f => f.Effective && f.Kind == "sw_cut" && f.Obj == "W:" + sw.Id)) return;
        var f = new Fault { Id = $"f{++counter}", Kind = "sw_cut", Obj = "W:" + sw.Id, State = "active", ActivatedAt = Clock, Armed = true };
        List.Add(f);
        Note(f, $"возникла: взрез стрелки {sw.Label} поездом №{t.Number}");
        Changed?.Invoke();
    }

    // =================================================================
    //  ОБЪЕКТЫ
    // =================================================================

    static double Clock => Training.Clock;

    static IEnumerable<RailSignal> Signals => il.Signals.Values.Where(s => s.Kind != SignalKind.Neighbor && s.Remote == null);

    /// <summary>Объекты станции для окна преподавателя: id → подпись. Одинаковые подписи уточняются id.</summary>
    public static List<(string id, string label)> Objects(string kind)
    {
        IEnumerable<(string id, string label)> raw = kind switch
        {
            "signal" => Signals.Select(s => ("S:" + s.Id, s.Label)),
            "switch" => il.Switches.Values.Select(s => ("W:" + s.Id, s.Label)),
            "section" => il.Sections.Values.Select(s => ("P:" + s.Id, s.Label)),
            "line" => il.Lines.Select(l => ("L:" + l.Id, l.Title)),
            "train" => trains.Trains.Where(t => !t.Leaving).Select(t => ("T:" + t.Number, $"№{t.Number} ({t.ModeText}) на {t.Head?.Label}")),
            _ => Enumerable.Empty<(string, string)>()
        };
        var list = raw.ToList();
        var dup = list.GroupBy(x => x.label).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return list.Select(x => (id: x.id, label: dup.Contains(x.label) ? $"{x.label} ({x.id[2..]})" : x.label))
                   .OrderBy(x => x.label, StringComparer.CurrentCulture).ToList();
    }

    static string Id(string obj) => obj.Length > 2 ? obj[2..] : "";

    static RailSignal Sig(string obj) => obj.StartsWith("S:") && il.Signals.TryGetValue(Id(obj), out var s) ? s : null;
    static RailSwitch Sw(string obj) => obj.StartsWith("W:") && il.Switches.TryGetValue(Id(obj), out var s) ? s : null;
    static TrackSection Sec(string obj) => obj.StartsWith("P:") && il.Sections.TryGetValue(Id(obj), out var s) ? s : null;
    static DirectionLine Line(string obj) => obj.StartsWith("L:") ? il.Lines.FirstOrDefault(l => l.Id == Id(obj)) : null;
    static VirtualTrain TrainObj(string obj) => obj.StartsWith("T:") && int.TryParse(Id(obj), out var n) ? trains.Find(n) : null;

    /// <summary>Как назвать объект в журнале и окнах.</summary>
    public static string Label(string obj)
    {
        if (string.IsNullOrEmpty(obj) || il == null) return "";
        if (Sig(obj) is { } s) return $"светофор {s.Label}";
        if (Sw(obj) is { } w) return $"стрелка {w.Label}";
        if (Sec(obj) is { } p) return $"участок {p.Label}";
        if (Line(obj) is { } l) return l.Title;
        if (obj.StartsWith("T:")) return $"поезд №{Id(obj)}";
        return obj;
    }

    static string Target(string obj) => obj.Length > 0 ? obj[0] switch
    {
        'S' => "signal", 'W' => "switch", 'P' => "section", 'L' => "line", 'T' => "train", _ => ""
    } : "";

    /// <summary>Участки объекта: на них ищется подвижная единица для условия.</summary>
    static List<TrackSection> SectionsOf(string obj)
    {
        if (Sig(obj) is { } s) return new[] { s.Approach ?? s.Protects }.Where(x => x != null).ToList();
        if (Sw(obj) is { } w) return w.Sections.ToList();
        if (Sec(obj) is { } p) return new() { p };
        if (Line(obj) is { } l) return l.Sections.ToList();
        if (TrainObj(obj) is { } t) return t.Cells.ToList();
        return new();
    }

    static string StateOf(string obj)
    {
        if (Sig(obj) is { } s) return AspectInfo.IsPermissive(s.Aspect) ? "открыт" : "закрыт";
        if (Sw(obj) is { } w) return w.Plus ? "+" : "−";
        if (Sec(obj) is { } p) return p.Occupied ? "занят" : p.LockedBy != null ? "назначен" : "свободен";
        if (Line(obj) is { } l) return l.Sections.Any(x => x.Occupied) ? "занят" : "свободен";
        if (TrainObj(obj) is { } t) return t.Waiting.Length > 0 ? "стоит" : "движется";
        return "";
    }

    /// <summary>Подвижные единицы на объекте, подходящие под фильтр. null в списке — настоящий поезд (датчик).</summary>
    static List<VirtualTrain> TrainsOn(Fault f)
    {
        if (TrainObj(f.Obj) is { } own) return new() { own };
        var secs = SectionsOf(f.Obj);
        var list = trains.Trains.Where(t => !t.Leaving && t.Cells.Any(secs.Contains)).ToList();
        list = f.TrainFilter switch
        {
            "number" => list.Where(t => t.Number == f.TrainNumber).ToList(),
            "train" => list.Where(t => !t.Shunting).ToList(),
            "pass" => list.Where(t => !t.Shunting && t.Passenger).ToList(),        // [ДОБАВЛЕНО 23.10.2026, этап 4] категория поезда
            "freight" => list.Where(t => !t.Shunting && t.Category == "грузовой").ToList(),
            "shunt" => list.Where(t => t.Shunting).ToList(),
            _ => list
        };
        bool physical = f.TrainFilter is "none" or "any" && secs.Any(x => x.HwOcc);
        if (list.Count == 0 && physical) list.Add(null);
        return list;
    }

    // =================================================================
    //  ОТ ПРЕПОДАВАТЕЛЯ
    // =================================================================

    public static object ObjectsMessage() => new
    {
        kind = "s_objects",
        signals = Objects("signal").Select(o => new { id = o.id, label = o.label }),
        switches = Objects("switch").Select(o => new { id = o.id, label = o.label }),
        sections = Objects("section").Select(o => new { id = o.id, label = o.label }),
        lines = Objects("line").Select(o => new { id = o.id, label = o.label }),
        trains = Objects("train").Select(o => new { id = o.id, label = o.label }),
        kinds = Kinds.Select(k => new { code = k.Code, name = k.Name, target = k.Target }),
        states = States,
    };

    public static void Add(JsonElement d)
    {
        if (!Training.Active) return;
        var f = new Fault
        {
            Id = $"f{++counter}",
            Kind = S(d, "kind"),
            Obj = S(d, "obj"),
            Cond = S(d, "cond") is { Length: > 0 } c ? c : "none",
            CondState = S(d, "condState"),
            TrainFilter = S(d, "trainFilter") is { Length: > 0 } tf ? tf : "none",
            TrainNumber = d.TryGetProperty("trainNumber", out var n) && n.TryGetInt32(out var nv) ? nv : 0,
            Start = d.TryGetProperty("start", out var st) && st.TryGetDouble(out var sv) ? sv : -1,
        };
        var k = f.K;
        if (k == null || Target(f.Obj) == "" || (k.Target != "train" && k.Target != Target(f.Obj)))
        {
            Training.Note($"Неисправность не задана: неверный объект или вид ({f.Kind}, {f.Obj})");
            return;
        }
        List.Add(f);
        Note(f, $"задана: {k.Name} — {Label(f.Obj)}; {CondText(f)}");
        Changed?.Invoke();
    }

    /// <summary>Преподаватель снял неисправность вручную.</summary>
    public static void Remove(string id)
    {
        var f = List.FirstOrDefault(x => x.Id == id);
        if (f == null || f.State is "fixed" or "removed" or "gone") return;
        End(f, "removed", "снята преподавателем");
    }

    public static string CondText(Fault f)
    {
        string c = f.Cond switch
        {
            "state" => $"при состоянии «{f.CondState}»",
            "to" => $"при переходе в состояние «{f.CondState}»",
            "from" => $"при переходе из состояния «{f.CondState}»",
            _ => "без условия"
        };
        string t = f.TrainFilter switch
        {
            "any" => ", любая подвижная единица",
            "number" => $", поезд №{f.TrainNumber}",
            "train" => ", поездной",
            "shunt" => ", маневровый",
            "pass" => ", пассажирский",
            "freight" => ", грузовой",
            _ => ""
        };
        return c + t + (f.Start >= 0 ? $", ожидание с {Training.Fmt(f.Start)}" : ", немедленно");
    }

    public static string StateText(Fault f) => f.State switch
    {
        "wait" => f.Start >= 0 && Clock < f.Start ? $"ждёт времени {Training.Fmt(f.Start)}" : "ждёт условия",
        "active" => "действует",
        "fixing" => $"устраняет {f.Worker}",
        "fixed" => "устранена",
        "removed" => "снята преподавателем",
        "gone" => "поезд ушёл со станции",
        _ => f.State
    };

    public static object FaultsMessage() => new
    {
        kind = "s_faults",
        list = List.Select(f => new
        {
            id = f.Id, name = f.K?.Name, obj = f.Obj, label = Label(f.Obj), cond = CondText(f),
            state = f.State, stateText = StateText(f), train = f.Train,
            activated = f.ActivatedAt, ended = f.EndedAt,
        })
    };

    /// <summary>Для отчёта: что задавалось и чем кончилось.</summary>
    public static object ReportList() => List.Select(f => new
    {
        name = f.K?.Name, label = Label(f.Obj) + (f.Train > 0 && !f.Obj.StartsWith("T:") ? $" (поезд №{f.Train})" : ""),
        cond = CondText(f), activated = f.ActivatedAt, ended = f.EndedAt, state = StateText(f), log = f.Log
    }).ToList();

    static void Note(Fault f, string text)
    {
        f.Log.Add($"{Training.ClockText}  {text}");
        Training.Note($"[Неисправность {f.Id}] {text}");
    }

    // =================================================================
    //  ТАКТ (только на занятии, когда идёт время)
    // =================================================================

    public static void Tick()
    {
        bool changed = false;
        foreach (var f in List.ToList())
        {
            switch (f.State)
            {
                case "wait": changed |= TryActivate(f); break;
                case "active":
                case "fixing": changed |= Work(f); break;
            }
        }
        TickChecks();
        Apply();
        if (changed) Changed?.Invoke();
    }

    static bool TryActivate(Fault f)
    {
        if (f.Start >= 0 && Clock < f.Start) return false;
        string cur = StateOf(f.Obj);
        if (f.Obj.StartsWith("T:") && TrainObj(f.Obj) == null) { End(f, "gone", "поезда больше нет на станции"); return true; }
        if (!f.Armed) { f.Armed = true; f.Prev = cur; if (f.Cond is "to" or "from") return false; }
        string prev = f.Prev;
        f.Prev = cur;
        bool ok = f.Cond switch
        {
            "state" => cur == f.CondState,
            "to" => cur == f.CondState && prev != f.CondState,
            "from" => prev == f.CondState && cur != f.CondState,
            _ => true
        };
        if (!ok) return false;

        var onObj = f.TrainFilter != "none" || f.IsTrainFault ? TrainsOn(f) : null;
        if (onObj is { Count: 0 }) return false;           // нужной подвижной единицы на объекте нет
        Activate(f, onObj?.FirstOrDefault());
        return true;
    }

    static void Activate(Fault f, VirtualTrain t)
    {
        f.State = "active";
        f.ActivatedAt = Clock;
        if (f.IsTrainFault) f.Train = t?.Number ?? 0;
        Note(f, $"возникла: {f.K.Name} — {Label(f.Obj)}" +
                (!f.IsTrainFault || f.Obj.StartsWith("T:") ? "" : t != null ? $", поезд №{t.Number}" : ", поезд на макете"));

        if (f.Kind == "sw_cut" && Sw(f.Obj) is { Cut: false } cutSw) il.CutSwitch(cutSw, null);
        if (f.IsTrainFault)
        {
            // о неисправности подвижной единицы ДСП узнаёт от машиниста по радиосвязи
            Apply();
            Du46.Message("Машинист", f.Train > 0 ? $"поезда №{f.Train}" : "поезда на макете", TrainKey(f), Fill(f.K.Cause, f), false);
        }
        if (f.Kind == "tr_derail" && t != null) il.Say($"Поезд №{t.Number} остановился на {t.Head?.Label}", LogKind.Train);
    }

    static string TrainKey(Fault f) => f.Train > 0 ? "T:" + f.Train : f.Obj;

    /// <summary>{0} — объект, {1} — «Поезд №…», {2} — где стоит поезд.</summary>
    static string Fill(string text, Fault f)
    {
        var t = f.Train > 0 ? trains.Find(f.Train) : null;
        string where = t?.Head?.Label is { } h ? $"участке {h}" : SectionsOf(f.Obj).FirstOrDefault(x => x.HwOcc)?.Label is { } hw ? $"участке {hw}" : Label(f.Obj);
        string who = f.Train > 0 ? $"Поезд №{f.Train}" : "Поезд на макете";
        return string.Format(text, Name(f.Obj), who, where);
    }

    /// <summary>Короткое имя объекта для текстов «Стрелка {0}»: «5/7», «Ч», «1П», название перегона.</summary>
    static string Name(string obj) =>
        Sig(obj)?.Label ?? Sw(obj)?.Label ?? Sec(obj)?.Label ?? Line(obj)?.Title ?? Label(obj);

    /// <summary>Неисправность действует: перекрытие светофора, уход поезда, работа «виртуального работника».</summary>
    static bool Work(Fault f)
    {
        if (f.Kind == "sig_drop" && !f.Triggered && Sig(f.Obj) is { } s && AspectInfo.IsPermissive(s.Aspect) && !s.InviteOn)
        {
            f.Triggered = true;
            Note(f, $"светофор {s.Label} перекрылся");
        }
        if (f.IsTrainFault && f.Train > 0 && trains.Find(f.Train) == null)
        {
            End(f, "gone", $"поезд №{f.Train} ушёл со станции");
            return true;
        }
        if (f.State != "fixing") return false;

        var k = f.K;
        if (!f.Arrived && Clock >= f.ArriveAt)
        {
            f.Arrived = true;
            if (k.Worker == "ДНЦ") return true;            // диспетчер сам не приезжает
            Du46.WorkerAction(k.Worker, f.Obj, "Прибыл для устранения");
            Du46.Message(k.Worker, Du46.WorkerName(k.Worker), TrainKey(f),
                f.Kind == "tr_derail"   // [ИЗМЕНЕНО 23.10.2026, этап 7] подвижной состав поднимает восстановительный поезд
                    ? Fill("Прибыл на {2}. Подвижной состав сошёл с рельсов, для подъёма нужен восстановительный поезд — " +
                           "вызовите его (ПКМ по поезду → «Вызвать восстановительный поезд…»).", f)
                    : f.IsTrainFault ? Fill("Прибыл на {2}, приступаю к восстановлению пути.", f) : Fill(k.Cause, f), false);
            Note(f, $"{k.Worker} прибыл, приступил к работе");
            return true;
        }
        if (f.Arrived && !f.Reported && Clock >= f.DoneAt)
        {
            f.Reported = true;
            Note(f, $"{k.Worker} доложил об устранении — ждёт подписи ДСП");
            Du46.Message(k.Worker, Du46.WorkerName(k.Worker), TrainKey(f), Fill(k.Report, f) + $" {k.Worker} {Du46.WorkerName(k.Worker)}", true,
                sign: () => { if (f.Effective) End(f, "fixed", $"устранена: ДСП подписал доклад {k.Worker}"); },
                reject: () =>
                {
                    if (!f.Effective) return;
                    f.State = "active";
                    f.Worker = "";
                    f.Arrived = f.Reported = false;
                    Note(f, $"ДСП не подписал доклад {k.Worker} — работы прекращены, нужно снова сообщить");
                    Changed?.Invoke();
                });
            return true;
        }
        return false;
    }

    /// <summary>[ДОБАВЛЕНО 23.10.2026, этап 4] Неисправность поезда устранена его бригадой (ремонт по радиосвязи).</summary>
    public static bool FixedByCrew(int train, string kind) =>
        FixedBy(train, kind, "устранена локомотивной бригадой (ДСП разрешил ремонт по радиосвязи)");

    /// <summary>[ДОБАВЛЕНО 23.10.2026, этап 7] Неисправность поезда устранена (бригадой, восстановительным поездом).</summary>
    public static bool FixedBy(int train, string kind, string why)
    {
        var f = List.FirstOrDefault(x => x.Effective && x.Kind == kind && x.Train == train);
        if (f == null) return false;
        End(f, "fixed", why);
        return true;
    }

    static void End(Fault f, string state, string why)
    {
        bool wasEffective = f.Effective;
        f.State = state;
        f.EndedAt = Clock;
        if (f.Kind == "sw_cut" && Sw(f.Obj) is { } cutSw) il.RestoreCut(cutSw);
        Note(f, why);
        if (wasEffective) Evaluate(f);
        Apply();
        Changed?.Invoke();
    }

    // =================================================================
    //  ДСП СООБЩИЛ РАБОТНИКУ (из окна ДУ-46)
    // =================================================================

    static bool Matches(Fault f, string obj) => obj.Length == 0 || obj == f.Obj || (f.Train > 0 && obj == "T:" + f.Train);

    public static void OnNotified(string worker, string obj)
    {
        if (!Training.Active) return;
        var mine = List.Where(f => f.State == "active" && f.K.Worker == worker && Matches(f, obj)).ToList();
        foreach (var f in mine)
        {
            if (f.Kind == "tr_gauge")
            {
                // негабарит не «устраняется»: диспетчер даёт указание
                if (f.Worker.Length > 0) continue;
                f.Worker = worker;
                Du46.Message(worker, Du46.WorkerName(worker), TrainKey(f), Fill(f.K.Report, f), false);
                Note(f, "ДНЦ дал указание о пропуске негабаритного поезда");
                continue;
            }
            f.State = "fixing";
            f.Worker = worker;
            f.Arrived = f.Reported = false;
            double arrive = worker == "ПД" ? rnd.Next(240, 421) : rnd.Next(120, 301);   // 2–5 мин (ПД — 4–7)
            double work = worker == "ПД" ? rnd.Next(300, 481) : rnd.Next(120, 241);
            f.ArriveAt = Clock + arrive;
            f.DoneAt = f.Kind == "tr_derail" ? double.MaxValue : f.ArriveAt + work;   // [этап 7] сход ликвидирует восстановительный поезд
            Note(f, $"сообщено {worker} — приступит через {arrive / 60:0} мин");
        }
        if (mine.Count == 0 && worker is "ШН" or "ПД" && obj.Length > 0 && !List.Any(f => f.Effective && f.K.Worker == worker && Matches(f, obj)))
            checks.Add((Clock + rnd.Next(120, 241), worker, obj));
        Changed?.Invoke();
    }

    /// <summary>Работник проверил объект, а неисправности там нет: когда, кто, объект.</summary>
    static readonly List<(double at, string worker, string obj)> checks = new();

    static void TickChecks()
    {
        foreach (var c in checks.Where(c => Clock >= c.at).ToList())
        {
            checks.Remove(c);
            Du46.WorkerAction(c.worker, c.obj, "Прибыл, произвёл проверку");
            Du46.Message(c.worker, Du46.WorkerName(c.worker), c.obj,
                $"{char.ToUpper(Label(c.obj)[0])}{Label(c.obj)[1..]} проверен(а): неисправность не обнаружена, устройства работают нормально. {c.worker} {Du46.WorkerName(c.worker)}", false);
        }
    }

    // =================================================================
    //  ОЦЕНКА: записал ли ДСП в ДУ-46 и сообщил ли причастным
    // =================================================================

    const double MinActive = 180;   // неисправность, снятая быстрее 3 игровых минут, не оценивается

    static void Evaluate(Fault f)
    {
        if (f.Checked || f.ActivatedAt < 0) return;
        double end = f.EndedAt >= 0 ? f.EndedAt : Clock;
        if (end - f.ActivatedAt < MinActive) return;
        f.Checked = true;
        string what = $"«{f.K.Name}» ({Label(f.Obj)}{(f.Train > 0 && !f.Obj.StartsWith("T:") ? $", поезд №{f.Train}" : "")})";
        bool Rec(string kind, string who) => Du46.Records.Any(r => r.Kind == kind && (who == null || r.Who == who) &&
                                                                  r.Clock >= f.ActivatedAt - 1 && Matches(f, r.Obj));
        if (!f.IsTrainFault)
        {
            if (!Rec("запись", null))
                Training.AddEvent(false, "du46", $"Неисправность {what} не записана в журнал осмотра ДУ-46", 5);
            if (!Rec("сообщено", "ШН"))
                Training.AddEvent(false, "notify", $"О неисправности {what} не сообщено электромеханику (ШН)", 5);
        }
        if (!Rec("сообщено", "ДНЦ"))
            Training.AddEvent(false, "notify", $"О неисправности {what} не сообщено поездному диспетчеру (ДНЦ)", 5);
        if (f.Kind == "tr_derail" && !Rec("сообщено", "ПД"))
            Training.AddEvent(false, "notify", $"О сходе {what} не сообщено дорожному мастеру (ПД)", 5);
    }

    /// <summary>Конец урока: оценить неисправности, которые ещё действуют, и снять их с модели.</summary>
    public static void Finish()
    {
        foreach (var f in List.Where(f => f.Effective)) Evaluate(f);
        foreach (var f in List.Where(f => f.State == "wait")) Note(f, "так и не возникла");
        Clear();
    }

    /// <summary>Новое занятие: неисправностей нет.</summary>
    public static void Reset()
    {
        List.Clear();
        checks.Clear();
        counter = 0;
        Clear();
    }

    static void Clear()
    {
        foreach (var f in List.Where(f => f.Effective))
        {
            f.State = "removed";
            f.EndedAt = Clock;
            if (f.Kind == "sw_cut" && Sw(f.Obj) is { } cutSw) il.RestoreCut(cutSw);
            Note(f, "урок закончен — неисправность снята");
        }
        Apply();
        Changed?.Invoke();
    }

    // =================================================================
    //  ФЛАГИ В МОДЕЛИ
    // =================================================================

    /// <summary>Выставить флаги неисправностей по действующим неисправностям (каждый такт).</summary>
    static void Apply()
    {
        if (il == null) return;
        foreach (var s in il.Sections.Values) s.FaultOcc = s.FaultFree = false;
        foreach (var w in il.Switches.Values) w.FaultNoControl = w.FaultNoPlus = w.FaultNoMinus = false;
        foreach (var s in il.Signals.Values) s.FaultClosed = false;
        foreach (var l in il.Lines) l.FaultNoChange = false;
        foreach (var t in trains.Trains) t.FaultEngine = t.FaultBrakes = t.Derailed = t.OutOfGauge = false;

        foreach (var f in List.Where(f => f.Effective))
        {
            switch (f.Kind)
            {
                case "sig_noopen": if (Sig(f.Obj) is { } s1) s1.FaultClosed = true; break;
                case "sig_drop": if (f.Triggered && Sig(f.Obj) is { } s2) s2.FaultClosed = true; break;
                case "sw_noplus": case "sw_fromminus": if (Sw(f.Obj) is { } w1) w1.FaultNoPlus = true; break;
                case "sw_nominus": case "sw_fromplus": if (Sw(f.Obj) is { } w2) w2.FaultNoMinus = true; break;
                case "sw_nocontrol": case "sw_gap": if (Sw(f.Obj) is { } w3) w3.FaultNoControl = true; break;
                case "sec_occ": if (Sec(f.Obj) is { } p1) p1.FaultOcc = true; break;
                case "sec_free": if (Sec(f.Obj) is { } p2) p2.FaultFree = true; break;
                case "line_nodir": if (Line(f.Obj) is { } l1) l1.FaultNoChange = true; break;
                case "line_free": if (Line(f.Obj) is { } l2) foreach (var x in l2.Sections) x.FaultFree = true; break;
                default:
                    if (f.IsTrainFault && f.Train > 0 && trains.Find(f.Train) is { } t)
                    {
                        if (f.Kind == "tr_engine") t.FaultEngine = true;
                        if (f.Kind == "tr_brakes") t.FaultBrakes = true;
                        if (f.Kind == "tr_derail") t.Derailed = true;
                        if (f.Kind == "tr_gauge") t.OutOfGauge = true;
                    }
                    break;
            }
        }
    }

    static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
