namespace StationApp.Core;

// =====================================================================
//  ЖУРНАЛ ОСМОТРА ДУ-46, КНОПКИ СО СЧЁТЧИКОМ, ДОКЛАДЫ РАБОТНИКОВ  [ДОБАВЛЕНО 20.10.2026, этап 3]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//
//  Как в тренажёре ДСП (часть 2, разделы 1 и 3):
//   • ДСП пишет в журнал запись о неисправности и сообщает причастным (ДНЦ, ШН, ШЧД, ПД, ЭЧЦ, ДС);
//   • работники отвечают сообщениями (только «Закрыть») и докладами («Подписать» / «Закрыть»);
//   • кнопки со счётчиком (пригласительный, ИР, ВК, ВСН): нажатие → счётчик +1 → запись в журнал.
//  Окна — Ui/Du46Ui.cs. Неисправности и работа «виртуальных работников» — Core/Faults.cs.
// =====================================================================

/// <summary>Строка журнала ДУ-46 («Порядок действий»).</summary>
public class Du46Record
{
    public double Clock { get; set; }
    /// <summary>запись — пишет ДСП; сообщено; прибыл; сообщение; доклад; подписан; не подписан; кнопка.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Должность: ДСП, ШН, ДНЦ…</summary>
    public string Who { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Объект (id, как в Faults: S:…, W:…, P:…, L:…, T:…), пусто — без объекта.</summary>
    public string Obj { get; set; } = "";
    public string ObjLabel { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>Сообщение или доклад работника (всплывающее окно у ДСП).</summary>
public class WorkerMessage
{
    public int Id;
    public double Clock;
    public string Who = "", Name = "", Text = "", Obj = "";
    /// <summary>Доклад: «Подписать» / «Закрыть». Иначе сообщение: только «Закрыть».</summary>
    public bool IsReport;
    public bool Done;
    public Action OnSign, OnReject;
    public string From => Name.Length > 0 ? $"{Who} {Name}" : Who;
}

/// <summary>Нажатие кнопки со счётчиком: его нужно записать в ДУ-46.</summary>
public class CounterPress
{
    public string Key = "", Label = "", What = "";
    public int Value;
    public double Clock;
    public bool Recorded, Penalized;
}

public static class Du46
{
    /// <summary>Работники служб: должность, кто это, фамилия.</summary>
    public static readonly (string code, string title, string name)[] Workers =
    {
        ("ДНЦ", "поездной диспетчер", "Сапаров"),
        ("ШН", "электромеханик СЦБ", "Петров"),
        ("ШЧД", "диспетчер дистанции СЦБ", "Ахметов"),
        ("ПД", "дорожный мастер", "Кононов"),
        ("ЭЧЦ", "энергодиспетчер", "Жумабеков"),
        ("ДС", "начальник станции", "Иванова"),
    };

    public static string WorkerName(string code) => Workers.FirstOrDefault(w => w.code == code).name ?? "";

    public static readonly List<Du46Record> Records = new();
    public static readonly List<WorkerMessage> Messages = new();
    public static readonly List<CounterPress> Presses = new();
    static readonly Dictionary<string, int> counters = new();
    static int msgId;

    public static event Action Changed;
    public static event Action<WorkerMessage> MessageArrived;
    public static event Action<CounterPress> CounterPressed;

    /// <summary>Время записей: игровое на занятии, иначе обычные часы.</summary>
    public static double Now => Training.Active ? Training.Clock : DateTime.Now.TimeOfDay.TotalSeconds;
    static string Dsp => Training.Student?.Name ?? "";

    public static IEnumerable<WorkerMessage> Pending => Messages.Where(m => m.IsReport && !m.Done);
    public static IEnumerable<CounterPress> Unrecorded => Presses.Where(p => !p.Recorded);

    static void Add(Du46Record r)
    {
        Records.Add(r);
        Training.Note($"ДУ-46: {r.Who} {r.Name} — {r.Text}{(r.ObjLabel.Length > 0 ? $" [{r.ObjLabel}]" : "")}");
        Changed?.Invoke();
    }

    // ------------------------ ДСП ------------------------

    /// <summary>ДСП заносит запись в журнал.</summary>
    public static void Write(string text, string obj)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        Add(new Du46Record { Clock = Now, Kind = "запись", Who = "ДСП", Name = Dsp, Obj = obj ?? "", ObjLabel = Faults.Label(obj), Text = text });
    }

    /// <summary>ДСП сообщил работнику о неисправности (объект может быть не указан).</summary>
    public static void Notify(string code, string obj)
    {
        Add(new Du46Record { Clock = Now, Kind = "сообщено", Who = code, Name = WorkerName(code), Obj = obj ?? "", ObjLabel = Faults.Label(obj),
                             Text = $"Сообщено {code}" });
        Faults.OnNotified(code, obj ?? "");
    }

    // ------------------------ работники ------------------------

    /// <summary>Работник прибыл, начал работу и т.п. — строка в «Порядок действий».</summary>
    public static void WorkerAction(string code, string obj, string text) =>
        Add(new Du46Record { Clock = Now, Kind = "прибыл", Who = code, Name = WorkerName(code), Obj = obj ?? "", ObjLabel = Faults.Label(obj), Text = text });

    /// <summary>Сообщение (только «Закрыть») или доклад («Подписать» / «Закрыть») — всплывает у ДСП.</summary>
    /// <param name="record">[ДОБАВЛЕНО 23.10.2026, этап 4] false — не в ДУ-46 (приказ ДНЦ — в ДУ-58, телефонограмма — в ДУ-47).</param>
    public static WorkerMessage Message(string who, string name, string obj, string text, bool report, Action sign = null, Action reject = null,
                                        bool record = true)
    {
        var m = new WorkerMessage { Id = ++msgId, Clock = Now, Who = who, Name = name, Obj = obj ?? "", Text = text, IsReport = report,
                                    OnSign = sign, OnReject = reject };
        Messages.Add(m);
        if (record && (report || who != "Машинист"))
            Add(new Du46Record { Clock = m.Clock, Kind = report ? "доклад" : "сообщение", Who = who, Name = name, Obj = m.Obj,
                                 ObjLabel = Faults.Label(obj), Text = text });
        else
            Training.Note(who == "Машинист" ? $"Радиосвязь: {m.From}: {text}" : $"Сообщение {m.From}: {text}");
        MessageArrived?.Invoke(m);
        return m;
    }

    /// <summary>ДСП подписал доклад (true) или закрыл его, не подписав (false).</summary>
    public static void Answer(WorkerMessage m, bool sign)
    {
        if (m.Done) return;
        m.Done = true;
        if (m.IsReport)
            Add(new Du46Record { Clock = Now, Kind = sign ? "подписан" : "не подписан", Who = "ДСП", Name = Dsp, Obj = m.Obj, ObjLabel = Faults.Label(m.Obj),
                                 Text = sign ? $"Доклад {m.From} подписан" : $"Доклад {m.From} не подписан" });
        (sign ? m.OnSign : m.OnReject)?.Invoke();
        Changed?.Invoke();
    }

    // ------------------------ кнопки со счётчиком ------------------------

    static string CountersFile => Path.Combine(Path.GetDirectoryName(DispatchLink.SettingsFile) ?? "", "counters.txt");

    public static int Counter(string key) => counters.TryGetValue(key, out var v) ? v : 0;

    public static void Init(Interlocking il)
    {
        try
        {
            if (File.Exists(CountersFile))
                foreach (var line in File.ReadAllLines(CountersFile))
                {
                    int i = line.LastIndexOf('=');
                    if (i > 0 && int.TryParse(line[(i + 1)..], out var v)) counters[line[..i]] = v;
                }
        }
        catch { }
        il.CountedCommand += Press;
        // [ДОБАВЛЕНО 21.10.2026, этап 6] сообщения и доклады работников о курбеле, замках, выключении стрелки
        il.WorkerInfo += (who, obj, text) => Message(who, WorkerName(who), obj, text, false);
        il.WorkerReport += (who, obj, text, sign, reject) =>
            Message(who, WorkerName(who), obj, $"{text}\n{who} {WorkerName(who)}", true, sign, reject);
    }

    static void SaveCounters()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CountersFile)!);
            File.WriteAllLines(CountersFile, counters.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch { }
    }

    static void Press(string key, string label, string what)
    {
        counters[key] = Counter(key) + 1;
        SaveCounters();
        var p = new CounterPress { Key = key, Label = label, What = what, Value = counters[key], Clock = Now };
        Presses.Add(p);
        Training.Note($"Нажата кнопка со счётчиком {label} ({what}), показание {p.Value}");
        Changed?.Invoke();
        CounterPressed?.Invoke(p);
    }

    /// <summary>Запись о нажатии кнопки со счётчиком (окно «Оформление записи»).</summary>
    public static void RecordPress(CounterPress p, string text, string faultObj)
    {
        if (p.Recorded) return;
        p.Recorded = true;
        Add(new Du46Record { Clock = Now, Kind = "кнопка", Who = "ДСП", Name = Dsp, Obj = faultObj ?? "", ObjLabel = Faults.Label(faultObj),
                             Text = $"{text.Trim()} Кнопка {p.Label}, показание счётчика {p.Value}." +
                                    (string.IsNullOrEmpty(faultObj) ? "" : $" По причине неисправности на объекте {Faults.Label(faultObj)}.") });
    }

    /// <summary>Нажатие не записано за 10 игровых минут (или к концу урока) — неправильное действие.</summary>
    public static void CheckPresses(bool final)
    {
        foreach (var p in Presses)
        {
            if (p.Recorded || p.Penalized || (!final && Now - p.Clock < 600)) continue;
            p.Penalized = true;
            Training.AddEvent(false, "du46_btn", $"Нажатие кнопки со счётчиком {p.Label} (показание {p.Value}) не записано в журнал осмотра ДУ-46", 5);
        }
    }

    /// <summary>Новое занятие: журнал пустой, счётчики с нуля.</summary>
    public static void ResetLesson()
    {
        Records.Clear();
        foreach (var m in Messages) m.Done = true;
        Messages.Clear();
        Presses.Clear();
        counters.Clear();
        SaveCounters();
        Changed?.Invoke();
    }

    public static void NotifyChanged() => Changed?.Invoke();
}
