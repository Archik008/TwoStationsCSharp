using System.Text.RegularExpressions;

namespace StationApp.Core;

// =====================================================================
//  ЖУРНАЛЫ ДУ-2, ДУ-47, ДУ-58, ДУ-60 И БЛАНКИ ДУ-50…64  [ДОБАВЛЕНО 23.10.2026, этап 4]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Окна — Ui/JournalsUi.cs.
//
//  Как в тренажёре ДСП (часть 1, раздел 1.11; часть 2, разделы 17–24):
//   • студент заполняет журналы сам: «Новая запись» → поля → «Сохранить». Исправлять можно, удалять нельзя —
//     каждое исправление помечается временем;
//   • регистрируемый приказ ДСП по радиосвязи записывается в ДУ-58 сам (Core/Radio.cs);
//   • телефонограмма в ДУ-47 «Передать» → соседняя станция отвечает входящей (согласие, перегон свободен…);
//   • бланки: создать → заполнить → «Вручить машинисту». ДУ-50 / 52 / 54 / 56 / 64 разрешают поезду проследовать
//     запрещающий выходной (ДУ-50, 56, 64 — и по перегону не по сигналам автоблокировки), ДУ-61 — машинист предупреждён;
//   • автопроверка в конце урока (З7: «давай и оставь для препода»): ДУ-2 против факта (номер, путь, время ± 2 мин),
//     приказы ДНЦ в ДУ-58, выданные предупреждения в ДУ-60, ДУ-50 только после согласия соседа в ДУ-47.
//     Расхождения — неправильные действия −2 (не больше −10 по журналу) и таблица для преподавателя: решает он.
// =====================================================================

public class JField
{
    public string Key = "", Label = "";
    /// <summary>text | time | multi | choice | check</summary>
    public string Kind = "text";
    public string[] Choices = Array.Empty<string>();
    /// <summary>Ширина столбца в таблице журнала (0 — только в форме).</summary>
    public int Width = 80;
    /// <summary>Значение для новой записи / бланка.</summary>
    public Func<string> Default;
    public bool ReadOnly;
}

public class JournalDef
{
    public string Code = "", Title = "", Hint = "";
    public JField[] Fields = Array.Empty<JField>();
    /// <summary>Заготовки текста: название → текст (в поле text).</summary>
    public (string name, string text)[] Templates = Array.Empty<(string, string)>();
}

public class JEntry
{
    public int No { get; set; }
    public double Clock { get; set; }
    /// <summary>Пусто — запись ДСП; иначе откуда взялась («входящая», «регистрируемый приказ…»).</summary>
    public string Source { get; set; } = "";
    public Dictionary<string, string> V { get; set; } = new();
    public List<string> Edits { get; set; } = new();
    public string this[string key] => V.TryGetValue(key, out var v) ? v ?? "" : "";
}

public class BlankDef
{
    public string Code = "", Title = "", Hint = "";
    public JField[] Fields = Array.Empty<JField>();
    /// <summary>Неизменяемый текст бланка (печатный).</summary>
    public string Fixed = "";
}

public class Blank
{
    public int No { get; set; }
    public string Kind { get; set; } = "";
    public double Clock { get; set; }
    public Dictionary<string, string> V { get; set; } = new();
    public int HandedTo { get; set; }
    public double HandedAt { get; set; } = -1;
    public string this[string key] => V.TryGetValue(key, out var v) ? v ?? "" : "";
    public BlankDef Def => Journals.BlankDefs.FirstOrDefault(d => d.Code == Kind);
    public string Title => $"{Kind} №{No}";
}

public static class Journals
{
    static Interlocking il;
    static TrainSim sim;
    static readonly Random rnd = new();

    static string Now5 => Training.Fmt(Du46.Now)[..5];
    static string Dsp => Training.Student?.Name ?? "";
    static string Station => DispatchLink.StationName;
    static string[] LineNames => il?.Lines.Select(l => l.Title).ToArray() ?? Array.Empty<string>();
    static string[] PointNames => Schedule.Stations.Select(SchedPoints.Name).DefaultIfEmpty(Station).ToArray();

    /// <summary>Варианты для поля-списка: пункты и перегоны этой программы берутся со схемы.</summary>
    public static string[] ChoicesOf(JField f) => f.Key == "point" ? PointNames : f.Key == "line" ? LineNames : f.Choices;

    static JField F(string key, string label, int width = 80, string kind = "text", Func<string> def = null, params string[] choices) =>
        new() { Key = key, Label = label, Width = width, Kind = kind, Default = def, Choices = choices };

    // =================================================================
    //  ЖУРНАЛЫ
    // =================================================================

    public static readonly JournalDef[] Defs =
    {
        new()
        {
            Code = "ДУ-2", Title = "Журнал движения поездов ДУ-2",
            Hint = "Записываются все поезда: прибывшие, отправленные и проследовавшие без остановки. Отметки: «ПС» — по пригласительному сигналу, «ПР» — по письменному разрешению.",
            Fields = new[]
            {
                F("point", "Станция / пост", 90, "choice", () => PointNames.FirstOrDefault() ?? ""),
                F("no", "№ поезда", 70),
                F("index", "Индекс", 110),
                F("depNb", "Отпр. с соседней (ч:мин)", 80, "time"),
                F("arr", "Прибытие (ч:мин)", 70, "time"),
                F("track", "Путь", 50),
                F("dep", "Отправление / проследование (ч:мин)", 90, "time"),
                F("arrNb", "Приб. на соседнюю (ч:мин)", 80, "time"),
                F("wagons", "Вагонов", 55),
                F("weight", "Вес брутто, т", 65),
                F("note", "Примечания (ПС, ПР, записи о поездной работе)", 200, "multi"),
            },
        },
        new()
        {
            Code = "ДУ-47", Title = "Журнал поездных телефонограмм ДУ-47",
            Hint = "Исходящая телефонограмма — «Передать»: соседняя станция ответит входящей. Без согласия соседа (ДУ-47) путевую записку ДУ-50 не вручают.",
            Fields = new[]
            {
                F("outNo", "№ исх.", 50), F("inNo", "№ вх.", 50),
                F("time", "Время", 55, "time", () => Now5),
                F("line", "Перегон", 160, "choice", () => LineNames.FirstOrDefault() ?? ""),
                F("text", "Содержание", 380, "multi"),
                F("sign", "Подпись", 120, "text", () => $"ДСП {Dsp}"),
            },
            Templates = new[]
            {
                ("Свободен ли перегон", "Свободен ли перегон {line}? ДСП ст. {station} {dsp}"),
                ("Согласие на отправление", "Прошу принять поезд №___. ДСП ст. {station} {dsp}"),
                ("Об отправлении поезда", "Поезд №___ отправился в {time}. ДСП ст. {station} {dsp}"),
                ("О прибытии поезда", "Поезд №___ прибыл в полном составе в {time}. ДСП ст. {station} {dsp}"),
                ("Переход на телефонные средства связи", "С {time} в связи с неисправностью автоблокировки движение поездов на перегоне {line} осуществляется по телефонным средствам связи. ДСП ст. {station} {dsp}"),
            },
        },
        new()
        {
            Code = "ДУ-58", Title = "Журнал диспетчерских распоряжений ДУ-58",
            Hint = "Регистрируются все приказы поездного диспетчера и регистрируемые приказы ДСП. Приказ «Указание» по радиосвязи записывается сам.",
            Fields = new[]
            {
                F("no", "№ приказа", 70),
                F("time", "Время", 55, "time", () => Now5),
                F("from", "Кто отдал", 120, "text", () => $"ДНЦ {Du46.WorkerName("ДНЦ")}"),
                F("to", "Кому", 120, "text", () => $"ДСП ст. {Station}"),
                F("text", "Текст приказа", 400, "multi"),
                F("sign", "Принял", 120, "text", () => $"ДСП {Dsp}"),
            },
            Templates = new[]
            {
                ("Регистрируемый приказ ДСП: проследовать запрещающий", "Разрешаю поезду №___ проследовать запрещающий сигнал светофора ___ ст. {station}. ДСП {dsp}"),
                ("Об изменении направления (вспомогательный режим)", "ДСП ст. {station}. Разрешаю изменить направление движения по автоблокировке по перегону {line} с помощью кнопок вспомогательного режима. Перегон от поездов свободен. ДНЦ {dnc}"),
                ("О закрытии перегона («окно»)", "ДСП ст. {station}. Для производства работ закрывается перегон {line} с ___ до ___. Поезда на перегон не отправлять. ДНЦ {dnc}"),
                ("Об открытии перегона", "ДСП ст. {station}. Работы закончены, перегон {line} открывается для движения поездов. ДНЦ {dnc}"),
                ("О переходе на телефонные средства связи", "ДСП ст. {station}. В связи с неисправностью автоблокировки на перегоне {line} движение поездов с {time} осуществлять по телефонным средствам связи. ДНЦ {dnc}"),
                ("Об отправлении хозяйственного поезда", "ДСП ст. {station}. Разрешаю отправить хозяйственный поезд №___ на перегон {line} до ___ км с возвращением обратно. ДНЦ {dnc}"),
            },
        },
        new()
        {
            Code = "ДУ-60", Title = "Журнал предупреждений ДУ-60",
            Hint = "Записываются все заявки и приказы о предупреждениях. Отмену — кнопкой «Отменить выделенное».",
            Fields = new[]
            {
                F("got", "Время получения заявки, № и кем подписана", 200),
                F("start", "Начало действия (ч:мин)", 70, "time", () => Now5),
                F("place", "Километр или место действия", 170),
                F("speed", "Скорость, км/ч", 70),
                F("char", "Характер предупреждения", 200, "multi"),
                F("end", "Окончание (ч:мин)", 70, "time"),
                F("cancel", "Отмена: время, № уведомления", 140),
            },
        },
    };

    public static JournalDef Def(string code) => Defs.First(d => d.Code == code);

    public static readonly Dictionary<string, List<JEntry>> Entries = Defs.ToDictionary(d => d.Code, _ => new List<JEntry>());
    public static event Action Changed;

    static JEntry NewEntry(string code, Dictionary<string, string> v, string source)
    {
        var list = Entries[code];
        var e = new JEntry { No = list.Count + 1, Clock = Du46.Now, Source = source, V = new(v) };
        list.Add(e);
        return e;
    }

    /// <summary>ДСП сохранил новую запись.</summary>
    public static JEntry Add(string code, Dictionary<string, string> v)
    {
        var e = NewEntry(code, v, "");
        if (code == "ДУ-47" && e["outNo"].Length == 0)
            e.V["outNo"] = (Entries[code].Count(x => x["outNo"].Length > 0) + 1).ToString();
        Training.Note($"{code}: новая запись — {Brief(code, e)}");
        if (code == "ДУ-47") Telephonogram(e);
        Changed?.Invoke();
        return e;
    }

    /// <summary>Запись делает программа (приказ по радиосвязи, входящая телефонограмма).</summary>
    public static JEntry AddAuto(string code, Dictionary<string, string> v, string source)
    {
        var e = NewEntry(code, v, source);
        Training.Note($"{code}: {source} — {Brief(code, e)}");
        Changed?.Invoke();
        return e;
    }

    /// <summary>Исправление записи: что поменялось, помечается временем.</summary>
    public static void Update(string code, JEntry e, Dictionary<string, string> v)
    {
        var diff = v.Where(kv => e[kv.Key] != (kv.Value ?? "")).ToList();
        if (diff.Count == 0) return;
        var def = Def(code);
        foreach (var (k, val) in diff)
        {
            string label = def.Fields.FirstOrDefault(f => f.Key == k)?.Label ?? k;
            e.Edits.Add($"{Now5} исправлено «{label}»: «{e[k]}» → «{val}»");
            e.V[k] = val ?? "";
        }
        Training.Note($"{code}: исправлена запись №{e.No} ({string.Join("; ", e.Edits.TakeLast(diff.Count))})");
        Changed?.Invoke();
    }

    /// <summary>ДУ-60: отменить предупреждение (графа «Отмена»).</summary>
    public static void CancelWarning(JEntry e)
    {
        if (e["cancel"].Length > 0) return;
        Update("ДУ-60", e, new() { ["cancel"] = $"{Now5}, отменено ДСП {Dsp}" });
    }

    static string Brief(string code, JEntry e) => code switch
    {
        "ДУ-2" => $"поезд №{e["no"]}, путь {e["track"]}, прибытие {e["arr"]}, отправление {e["dep"]}",
        "ДУ-60" => $"{e["place"]}, {e["speed"]} км/ч",
        _ => $"№{(e["no"].Length > 0 ? e["no"] : e["outNo"] + e["inNo"])} {Cut(e["text"], 80)}"
    };

    static string Cut(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    /// <summary>Подставить в заготовку станцию, ДСП, ДНЦ, время, перегон.</summary>
    public static string Fill(string template, string line) => template
        .Replace("{station}", Station).Replace("{dsp}", Dsp).Replace("{dnc}", Du46.WorkerName("ДНЦ"))
        .Replace("{time}", Now5).Replace("{line}", line.Length > 0 ? line : "___");

    // =================================================================
    //  ДУ-47: ответ соседней станции
    // =================================================================

    static readonly List<(double at, JEntry e)> replies = new();
    static int inNo;

    static void Telephonogram(JEntry e) => replies.Add((Du46.Now + rnd.Next(20, 61), e));

    static void Tick(double dt)
    {
        foreach (var r in replies.Where(r => Du46.Now >= r.at).ToList())
        {
            replies.Remove(r);
            Answer(r.e);
        }
    }

    static void Answer(JEntry e)
    {
        string text = e["text"].ToLowerInvariant();
        var line = il.Lines.FirstOrDefault(l => l.Title == e["line"]);
        bool busy = line?.Sections.Any(s => s.Occupied) ?? false;
        string lineName = line?.Title ?? "перегон";
        var m = Regex.Match(e["text"], @"№\s*(\d+)");
        string train = m.Success ? m.Groups[1].Value : "";
        string reply;
        if (text.Contains("свобод"))
            reply = busy ? $"Перегон {lineName} занят." : $"Перегон {lineName} свободен.";
        else if (train.Length > 0 && (text.Contains("приня") || text.Contains("разреш") || text.Contains("соглас")))
            reply = busy ? $"Поезд №{train} принять не могу: перегон {lineName} занят." : $"Поезд №{train} принимаю.";
        else if (train.Length > 0 && text.Contains("отправ"))
            reply = $"Об отправлении поезда №{train} понял.";
        else
            reply = $"Телефонограмму №{e["outNo"]} принял.";
        reply += " ДСП соседней станции";
        AddAuto("ДУ-47", new()
        {
            ["inNo"] = (++inNo).ToString(), ["time"] = Now5, ["line"] = e["line"], ["text"] = reply, ["sign"] = "ДСП соседней станции",
        }, "входящая");
        Du46.Message("ДСП соседней станции", "", "", $"Телефонограмма №{inNo}: {reply}\n\nЗаписана в журнал ДУ-47.", false, record: false);
    }

    /// <summary>Сосед дал согласие на приём поезда (входящая телефонограмма «Поезд №… принимаю»).</summary>
    public static bool Consent(int train) =>
        Entries["ДУ-47"].Any(e => e.Source == "входящая" && e["text"].Contains($"№{train} принимаю"));

    // =================================================================
    //  БЛАНКИ
    // =================================================================

    public static readonly BlankDef[] BlankDefs =
    {
        new()
        {
            Code = "ДУ-50", Title = "ДУ-50 (путевая записка)",
            Hint = "Разрешение на занятие перегона при телефонных средствах связи. Сначала — согласие соседней станции по ДУ-47.",
            Fields = new[]
            {
                F("train", "Разрешаю поезду №", 0), F("from", "отправиться с ___ пути", 0), F("by", "по ___ пути перегона", 0, "text", () => "главному"),
                F("to", "и следовать", 0, "choice", null, "до входного сигнала станции", "до километра"),
                F("toValue", "станция / километр", 0), F("back", "с возвращением обратно", 0, "check"), F("extra", "Доп. отметка", 0),
            },
            Fixed = "Блокировка не действует.",
        },
        new()
        {
            Code = "ДУ-52", Title = "ДУ-52 (полуавтоблокировка)",
            Hint = "Разрешение на отправление при полуавтоматической блокировке, когда выходной светофор не открывается.",
            Fields = new[]
            {
                F("train", "Разрешаю поезду №", 0), F("from", "отправиться с ___ пути", 0),
                F("signal", "при закрытом ___ сигнале", 0, "choice", null, "выходном", "маршрутном"),
                F("toValue", "и следовать до входного сигнала станции", 0),
            },
        },
        new()
        {
            Code = "ДУ-54", Title = "ДУ-54 (автоблокировка)",
            Hint = "Разрешение проследовать запрещающее показание выходного светофора при автоблокировке.",
            Fields = new[]
            {
                F("train", "Разрешаю поезду №", 0), F("from", "отправиться с ___ пути", 0),
                F("signal", "при запрещающем показании ___ светофора", 0, "choice", null, "выходного", "маршрутного"),
                F("first", "до первого проходного светофора", 0),
            },
            Fixed = "Со скоростью не свыше 20 км в час, с особой бдительностью и готовностью немедленно остановиться, если встретится препятствие для " +
                    "дальнейшего движения, следовать до первого проходного светофора и далее по сигналам автоблокировки. Настоящее разрешение даёт право " +
                    "проследовать только запрещающее показание выходного светофора станции.",
        },
        new()
        {
            Code = "ДУ-55", Title = "ДУ-55 (извещение)",
            Hint = "Письменное извещение дежурному соседней станции о движении поезда при перерыве всех средств сигнализации и связи.",
            Fields = new[]
            {
                F("about", "О движении поезда на однопутных перегонах при перерыве всех средств сигнализации и связи", 0),
                F("toStation", "Дежурному по станции", 0), F("text", "Текст", 0, "multi"),
            },
        },
        new()
        {
            Code = "ДУ-56", Title = "ДУ-56 (отсутствие сигнализации и связи)",
            Hint = "Разрешение на занятие перегона при перерыве действия всех средств сигнализации и связи.",
            Fields = new[]
            {
                F("train", "Разрешаю поезду №", 0), F("fromStation", "отправиться со станции", 0, "text", () => Station),
                F("toStation", "и следовать до входного сигнала станции", 0),
                F("signal", "при закрытом ___ сигнале", 0, "choice", null, "выходном", "маршрутном"),
                F("info", "О прибытии на соседнюю станцию ранее отправленного поезда сведения", 0, "choice", null, "имеются", "не имеются"),
            },
            Fixed = "Все виды средств сигнализации и связи прерваны.",
        },
        new()
        {
            Code = "ДУ-61", Title = "ДУ-61 (предупреждение)",
            Hint = "Письменное предупреждение машинисту о действующих ограничениях скорости и особых условиях.",
            Fields = new[]
            {
                F("train", "Предупреждение на поезд №", 0),
                F("rows", "Место действия (км, пикет, станция); время действия; скорость не более, км/ч; особые условия — по строке на предупреждение", 0, "multi"),
            },
        },
        new()
        {
            Code = "ДУ-64", Title = "ДУ-64 (особые условия)",
            Hint = "Разрешение для путевых работ, восстановительных, пожарных, хозяйственных поездов, помощи поезду.",
            Fields = new[]
            {
                F("train", "Разрешаю поезду №", 0), F("loco", "с локомотивом №", 0),
                F("line", "отправиться на перегон", 0, "choice", () => LineNames.FirstOrDefault() ?? ""),
                F("track", "по ___ пути", 0), F("km", "до ___ км", 0), F("purpose", "для", 0, "multi"),
            },
            Fixed = "Настоящее разрешение даёт право проезда выходного сигнала станции с запрещающим показанием и следования по перегону " +
                    "вне зависимости от показаний проходных светофоров автоблокировки.",
        },
    };

    public static readonly List<Blank> Blanks = new();

    /// <summary>Бланки, разрешающие проследовать запрещающий выходной.</summary>
    public static bool IsExitPermit(string kind) => kind is "ДУ-50" or "ДУ-52" or "ДУ-54" or "ДУ-56" or "ДУ-64";

    public static Blank SaveBlank(Blank b, Dictionary<string, string> v)
    {
        if (b == null)
        {
            b = new Blank { Kind = v["kind"], Clock = Du46.Now };
            b.No = Blanks.Count(x => x.Kind == b.Kind) + 1;
            Blanks.Add(b);
        }
        else if (b.HandedTo > 0) return b;   // вручённый бланк не меняется
        b.V = new(v);
        Training.Note($"Бланк {b.Title} заполнен: {BlankText(b)}");
        Changed?.Invoke();
        return b;
    }

    /// <summary>Вручить бланк машинисту поезда (t null — поезд на макете, только запись).</summary>
    public static string Hand(Blank b, VirtualTrain t, int number)
    {
        if (b.HandedTo > 0) return $"бланк уже вручён поезду №{b.HandedTo}";
        b.HandedTo = number;
        b.HandedAt = Du46.Now;
        il.Say($"Бланк {b.Title} вручён машинисту поезда №{number}", LogKind.Train);
        Training.Note($"Бланк {b.Title} вручён машинисту поезда №{number}");
        if (int.TryParse(b["train"], out var inBlank) && inBlank != number)
            Training.AddEvent(false, "blank", $"Бланк {b.Title} выписан на поезд №{inBlank}, а вручён поезду №{number}", 2);
        if (b.Kind == "ДУ-50" && !Consent(number))
            Training.AddEvent(false, "du47", $"Путевая записка {b.Title} вручена поезду №{number} без согласия соседней станции (входящая телефонограмма в ДУ-47)", 5);
        if (t != null)
        {
            if (IsExitPermit(b.Kind)) { t.PermitExit = b.Kind; t.StopOrder = false; }
            if (b.Kind == "ДУ-61") t.Warned = true;
        }
        Changed?.Invoke();
        return null;
    }

    public static string BlankText(Blank b)
    {
        var d = b.Def;
        if (d == null) return "";
        var parts = d.Fields.Where(f => b[f.Key].Length > 0)
                            .Select(f => f.Kind == "check" ? (b[f.Key] == "1" ? f.Label : "") : $"{f.Label}: {b[f.Key]}")
                            .Where(s => s.Length > 0).ToList();
        if (d.Fixed.Length > 0) parts.Add(d.Fixed);
        return string.Join("; ", parts);
    }

    // =================================================================
    //  АВТОПРОВЕРКА (конец урока)
    // =================================================================

    /// <summary>Строки для преподавателя: что проверено, что нашлось.</summary>
    public static readonly List<string[]> Checks = new();

    static bool Near(string hhmm, double clock)
    {
        if (!TimeSpan.TryParse(hhmm.Trim().Replace('.', ':').Replace('-', ':'), out var t)) return false;
        double d = Math.Abs(t.TotalSeconds - clock % 86400);
        return Math.Min(d, 86400 - d) <= 150;   // ± 2 мин (и 30 с на округление)
    }

    static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    public static void Finish()
    {
        Checks.Clear();
        CheckDu2();
        foreach (var (no, what) in Radio.DncOrders())
        {
            bool ok = Entries["ДУ-58"].Any(e => Digits(e["no"]) == no.ToString() || e["text"].Contains($"№{no}"));
            Checks.Add(new[] { "ДУ-58", $"приказ ДНЦ №{no} ({what})", ok ? "записан" : "НЕ ЗАПИСАН" });
            if (!ok) Training.AddEvent(false, "du58", $"Приказ ДНЦ №{no} ({what}) не записан в журнал диспетчерских распоряжений ДУ-58", 2);
        }
        foreach (var r in Radio.Restrictions.Where(r => !r.IsWindow))
        {
            string key = r.Obj.StartsWith("P:") ? r.ObjLabel.Replace("участок ", "") : r.ObjLabel;
            bool ok = Entries["ДУ-60"].Any(e => (e["place"] + " " + e["char"]).Contains(key, StringComparison.OrdinalIgnoreCase) &&
                                                 Digits(e["speed"]).Contains(Math.Min(r.SpeedPass, r.SpeedFreight).ToString()));
            Checks.Add(new[] { "ДУ-60", r.Text, ok ? "записано" : "НЕ ЗАПИСАНО (или место / скорость не совпадают)" });
            if (!ok) Training.AddEvent(false, "du60", $"{r.Text} не записано в журнал предупреждений ДУ-60", 2);
        }
    }

    /// <summary>ДУ-2 против факта: каждый поезд, прибывший / отправленный / проследовавший в пунктах этой программы.</summary>
    static void CheckDu2()
    {
        var facts = Schedule.Events.GroupBy(e => (e.Number, e.Station));
        foreach (var g in facts)
        {
            var (num, st) = g.Key;
            var arr = g.FirstOrDefault(e => e.Kind is "arr" or "pass");
            var dep = g.LastOrDefault(e => e.Kind is "dep" or "pass");
            string track = g.Select(e => e.Track).LastOrDefault(t => t.Length > 0) ?? "";
            string fact = $"{(arr != null ? "приб. " + Training.Fmt(arr.Clock)[..5] : "")}{(dep != null ? ", отпр. " + Training.Fmt(dep.Clock)[..5] : "")}" +
                          (track.Length > 0 ? $", путь {track}" : "");
            bool post = st == SchedPoints.Post;   // путевой пост: только отметка для преподавателя
            string point = SchedPoints.Name(st);
            var e = Entries["ДУ-2"].LastOrDefault(x => Digits(x["no"]) == num.ToString() && (x["point"].Length == 0 || x["point"] == point));
            var bad = new List<string>();
            if (e == null) bad.Add("нет записи");
            else
            {
                if (arr != null && g.Any(x => x.Kind == "arr") && !Near(e["arr"], arr.Clock)) bad.Add($"прибытие {Or(e["arr"])}");
                if (dep != null && !Near(e["dep"], dep.Clock) && !(dep.Kind == "pass" && Near(e["arr"], dep.Clock))) bad.Add($"отправление {Or(e["dep"])}");
                if (track.Length > 0 && Digits(track).Length > 0 && Digits(e["track"]) != Digits(track)) bad.Add($"путь {Or(e["track"])}");
            }
            Checks.Add(new[] { "ДУ-2", $"{point}: поезд №{num} ({fact})", bad.Count == 0 ? "верно" : string.Join(", ", bad) + (post ? " (пост — на усмотрение преподавателя)" : "") });
            if (bad.Count > 0 && !post)
                Training.AddEvent(false, "du2", $"Журнал ДУ-2, поезд №{num} ({point}): {string.Join(", ", bad)}. Факт: {fact}", 2);
        }
        foreach (var e in Entries["ДУ-2"].Where(x => !facts.Any(g => Digits(x["no"]) == g.Key.Number.ToString())))
            Checks.Add(new[] { "ДУ-2", $"запись №{e.No}: поезд №{e["no"]}", "такого поезда по факту не было" });
    }

    static string Or(string s) => s.Trim().Length > 0 ? $"«{s.Trim()}»" : "не указано";

    // =================================================================
    //  ОТЧЁТ (s_result.docs): таблицы {title, columns, rows}
    // =================================================================

    public static List<object> ReportDocs()
    {
        var docs = new List<object>();
        foreach (var d in Defs)
        {
            var cols = new List<string> { "№" };
            cols.AddRange(d.Fields.Select(f => f.Label));
            cols.Add("Исправления / откуда");
            var rows = Entries[d.Code].Select(e =>
            {
                var row = new List<string> { e.No.ToString() };
                row.AddRange(d.Fields.Select(f => e[f.Key]));
                row.Add(string.Join("; ", e.Edits.Prepend(e.Source).Where(s => s.Length > 0)));
                return row;
            }).ToList();
            docs.Add(new { title = d.Title, columns = cols, rows });
        }
        docs.Add(new
        {
            title = "Бланки, вручённые машинистам",
            columns = new[] { "Бланк", "Заполнен", "Вручён поезду", "Когда", "Содержание" },
            rows = Blanks.Select(b => new[] { b.Title, Training.Fmt(b.Clock)[..5], b.HandedTo > 0 ? $"№{b.HandedTo}" : "не вручён",
                                              b.HandedAt >= 0 ? Training.Fmt(b.HandedAt)[..5] : "", BlankText(b) }).ToList(),
        });
        docs.Add(new
        {
            title = "Предупреждения и «окна»",
            columns = new[] { "№", "Вид", "Объект", "Место", "Скорость / служба", "С", "До", "Отменено", "Приказы ДНЦ" },
            rows = Radio.ReportRows().ToList(),
        });
        docs.Add(new
        {
            title = "Поездная радиосвязь",
            columns = new[] { "Поезд", "Переговоры" },
            rows = Radio.Talks.Select(kv => new[] { $"№{kv.Key}", string.Join("\n", kv.Value) }).ToList(),
        });
        docs.Add(new
        {
            title = "Автопроверка журналов (окончательно решает преподаватель)",
            columns = new[] { "Журнал", "Что проверено", "Результат" },
            rows = Checks.ToList(),
        });
        return docs;
    }

    // =================================================================
    //  УРОК
    // =================================================================

    public static void Init(Interlocking interlocking, TrainSim trains)
    {
        il = interlocking;
        sim = trains;
        il.Ticked += Tick;
    }

    public static void ResetLesson()
    {
        foreach (var l in Entries.Values) l.Clear();
        Blanks.Clear();
        Checks.Clear();
        replies.Clear();
        inNo = 0;
        Changed?.Invoke();
    }

    public static void NotifyChanged() => Changed?.Invoke();
}
