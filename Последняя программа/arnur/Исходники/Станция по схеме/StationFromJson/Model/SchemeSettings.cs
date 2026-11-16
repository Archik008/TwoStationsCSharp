using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using StationApp.Core;

namespace StationApp.Model;

// =====================================================================
//  НАСТРОЙКА СТАНЦИИ  [ДОБАВЛЕНО 13.11.2026]
//  Файл «<схема>.settings.json» рядом со схемой (сама схема .station.json не меняется). Окно «Настройка…».
//    routes      — свои маршруты: клик по светофору начала n1 и конца n2 → эти участки жёлтым (замыкаются),
//                  эти стрелки в эти положения, эти светофоры — эти показания («auto» / нет — как считает программа).
//                  Свой маршрут n1 → n2 заменяет автомаршруты с тем же началом и концом (или путём назначения).
//    autoRoutes  — false: автомаршрутов (поиск редактора) нет совсем, только свои
//    auto        — автомаршрут (ключ — начало, путь, стрелки) → выключен / своё имя
//    signals     — светофор ("клетка#номер") → своё имя, вид
//    switches    — стрелка (группа редактора: "g:16/20" или "k:x,y") → номер на пульте, начальное положение
//    times       — времена зависимостей (перевод стрелки, установка маршрута, разделка…)
//  Записывается только то, что отличается от схемы; пусто — файла нет.
// =====================================================================

public sealed class SchemeSettings
{
    [JsonPropertyName("format")] public string Format { get; set; } = "station-scheme-settings";
    [JsonPropertyName("autoRoutes")] public bool? AutoRoutes { get; set; }
    [JsonPropertyName("routes")] public List<CustomRoute> Routes { get; set; } = new();
    [JsonPropertyName("auto")] public Dictionary<string, AutoRouteSetting> Auto { get; set; } = new();
    [JsonPropertyName("signals")] public Dictionary<string, SignalSetting> Signals { get; set; } = new();
    [JsonPropertyName("switches")] public Dictionary<string, SwitchSetting> Switches { get; set; } = new();
    [JsonPropertyName("times")] public TimeSettings Times { get; set; } = new();

    [JsonIgnore] public bool AutoOn => AutoRoutes != false;

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>«Станция Алгабас.station.json» → «Станция Алгабас.settings.json» в той же папке.</summary>
    public static string PathFor(string scheme)
    {
        var full = Path.GetFullPath(scheme);
        var n = Path.GetFileName(full);
        foreach (var ext in new[] { ".station.json", ".json" })
            if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { n = n[..^ext.Length]; break; }
        return Path.Combine(Path.GetDirectoryName(full) ?? "", n + ".settings.json");
    }

    /// <summary>Прочитать настройку схемы; нет файла — пустая; файл испорчен — пустая и строка в notes.</summary>
    public static SchemeSettings Load(string scheme, List<string> notes)
    {
        var p = PathFor(scheme);
        if (!File.Exists(p)) return new SchemeSettings();
        try
        {
            var s = JsonSerializer.Deserialize<SchemeSettings>(File.ReadAllText(p, Encoding.UTF8), Opts) ?? new SchemeSettings();
            s.Routes ??= new(); s.Auto ??= new(); s.Signals ??= new(); s.Switches ??= new(); s.Times ??= new();
            foreach (var r in s.Routes) { r.Sections ??= new(); r.Switches ??= new(); r.Aspects ??= new(); }
            notes?.Add($"Настройка станции — из файла {Path.GetFileName(p)}" +
                       (s.Routes.Count > 0 ? $", своих маршрутов: {s.Routes.Count}" : "") + (s.AutoOn ? "" : ", автомаршруты выключены") + ".");
            return s;
        }
        catch (Exception e)
        {
            notes?.Add($"Файл настройки {Path.GetFileName(p)} не прочитан ({e.Message}) — всё по схеме.");
            return new SchemeSettings();
        }
    }

    /// <summary>Записать рядом со схемой (пустая настройка — файл удаляется).</summary>
    public void Save(string scheme)
    {
        Clean();
        var p = PathFor(scheme);
        if (IsEmpty) { if (File.Exists(p)) File.Delete(p); return; }
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts), new UTF8Encoding(false));
        File.Move(tmp, p, true);
    }

    void Clean()
    {
        if (AutoRoutes == true) AutoRoutes = null;
        foreach (var k in Auto.Where(p => p.Value == null || p.Value.IsEmpty).Select(p => p.Key).ToList()) Auto.Remove(k);
        foreach (var k in Signals.Where(p => p.Value == null || p.Value.IsEmpty).Select(p => p.Key).ToList()) Signals.Remove(k);
        foreach (var k in Switches.Where(p => p.Value == null || p.Value.IsEmpty).Select(p => p.Key).ToList()) Switches.Remove(k);
    }

    [JsonIgnore] public bool IsEmpty => AutoRoutes == null && Routes.Count == 0 && Auto.Count == 0 && Signals.Count == 0 && Switches.Count == 0 && Times.IsEmpty;

    // ---- показания: как пишутся в файле и как называются в окне ----
    public static readonly (Aspect A, string Id, string Name)[] Aspects =
    {
        (Aspect.Red, "red", "красный"),
        (Aspect.Yellow, "yellow", "жёлтый"),
        (Aspect.YellowBlink, "yellowBlink", "жёлтый мигающий"),
        (Aspect.Green, "green", "зелёный"),
        (Aspect.TwoYellow, "twoYellow", "два жёлтых"),
        (Aspect.TwoYellowTopBlink, "twoYellowTopBlink", "два жёлтых, верхний мигающий"),
        (Aspect.White, "white", "лунно-белый"),
        (Aspect.Blue, "blue", "синий"),
        (Aspect.Dark, "dark", "погашен"),
    };

    public static Aspect? AspectOf(string id) => Aspects.Where(a => a.Id == id).Select(a => (Aspect?)a.A).FirstOrDefault();
}

/// <summary>Свой маршрут: что происходит после клика по n1 и n2.</summary>
public sealed class CustomRoute
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>Маневровый (белый огонь) или поездной.</summary>
    [JsonPropertyName("shunt")] public bool Shunt { get; set; }
    /// <summary>Светофор начала n1 — «клетка#номер» из схемы (например "10,8#0").</summary>
    [JsonPropertyName("start")] public string Start { get; set; }
    /// <summary>Светофор конца n2; null — маршрут набирается кликом по пути назначения (последний участок).</summary>
    [JsonPropertyName("end")] public string End { get; set; }
    /// <summary>Участки по порядку движения (имена из схемы): горят жёлтым и замыкаются. Последний — путь назначения;
    /// если это перегон — маршрут отправления (перегон должен быть свободен, но не замыкается).</summary>
    [JsonPropertyName("sections")] public List<string> Sections { get; set; } = new();
    /// <summary>Стрелка (группа редактора) → true — плюс, false — минус.</summary>
    [JsonPropertyName("switches")] public Dictionary<string, bool> Switches { get; set; } = new();
    /// <summary>Светофор → показание (id из SchemeSettings.Aspects); начального нет — считается как обычно.</summary>
    [JsonPropertyName("aspects")] public Dictionary<string, string> Aspects { get; set; } = new();
    /// <summary>true — маршрут записан, но не работает.</summary>
    [JsonPropertyName("off")] public bool? Off { get; set; }
}

public sealed class AutoRouteSetting
{
    [JsonPropertyName("off")] public bool? Off { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonIgnore] public bool IsEmpty => Off != true && string.IsNullOrWhiteSpace(Name);
}

public sealed class SignalSetting
{
    [JsonPropertyName("name")] public string Name { get; set; }
    /// <summary>entry, exit, route, shunt, block, other — как в редакторе (Core.SigKinds).</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; }
    [JsonIgnore] public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Kind);
}

public sealed class SwitchSetting
{
    /// <summary>Номер на пульте и на схеме (у одиночной стрелки).</summary>
    [JsonPropertyName("label")] public string Label { get; set; }
    /// <summary>Начальное положение: true — минус, false — плюс; null — как в схеме.</summary>
    [JsonPropertyName("minus")] public bool? Minus { get; set; }
    [JsonIgnore] public bool IsEmpty => string.IsNullOrWhiteSpace(Label) && Minus == null;
}

/// <summary>Времена зависимостей, секунды; null — как в программе (Interlocking).</summary>
public sealed class TimeSettings
{
    [JsonPropertyName("switchMove")] public double? SwitchMove { get; set; }
    [JsonPropertyName("routeSetup")] public double? RouteSetup { get; set; }
    [JsonPropertyName("artificialRelease")] public double? ArtificialRelease { get; set; }
    [JsonPropertyName("shuntRelease")] public double? ShuntRelease { get; set; }
    [JsonPropertyName("invite")] public double? Invite { get; set; }
    [JsonPropertyName("auxDirection")] public double? AuxDirection { get; set; }

    [JsonIgnore] public bool IsEmpty => SwitchMove == null && RouteSetup == null && ArtificialRelease == null &&
                                        ShuntRelease == null && Invite == null && AuxDirection == null;

    /// <summary>Поля окна: название, прочитать из настройки, записать, значение в программе по умолчанию.</summary>
    public static readonly (string Title, Func<TimeSettings, double?> Get, Action<TimeSettings, double?> Set, Func<Interlocking, double> Def)[] Fields =
    {
        ("Перевод стрелки (без контроля)", t => t.SwitchMove, (t, v) => t.SwitchMove = v, il => il.SwitchMoveSeconds),
        ("Установка маршрута до открытия светофора", t => t.RouteSetup, (t, v) => t.RouteSetup = v, il => il.RouteSetupSeconds),
        ("Искусственная разделка поездного маршрута", t => t.ArtificialRelease, (t, v) => t.ArtificialRelease = v, il => il.ArtificialReleaseSeconds),
        ("Разделка маневрового маршрута", t => t.ShuntRelease, (t, v) => t.ShuntRelease = v, il => il.ShuntReleaseSeconds),
        ("Пригласительный горит (если поезд не вступил)", t => t.Invite, (t, v) => t.Invite = v, il => il.InviteSeconds),
        ("Вспомогательная смена направления", t => t.AuxDirection, (t, v) => t.AuxDirection = v, il => il.AuxDirectionSeconds),
    };

    public void Apply(Interlocking il)
    {
        if (SwitchMove is double a) il.SwitchMoveSeconds = a;
        if (RouteSetup is double b) il.RouteSetupSeconds = b;
        if (ArtificialRelease is double c) il.ArtificialReleaseSeconds = c;
        if (ShuntRelease is double d) il.ShuntReleaseSeconds = d;
        if (Invite is double e) il.InviteSeconds = e;
        if (AuxDirection is double f) il.AuxDirectionSeconds = f;
    }
}
