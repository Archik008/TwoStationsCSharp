using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StationEditor.Model;

// =====================================================================
//  СТАНЦИЯ — то, что сохраняется в файл .station.json (формат тот же, что у веб-макета):
//    tiles    — "x,y" → { t: тип кубика, v: вариант (поворот), sec: участок,
//                         name: номер стрелки, group: съезд (спаренные стрелки), plus: положение,
//                         sigs: [{ dir: 'R' | 'L', kind, name, id, ro }] }
//               [ДОБАВЛЕНО 09.11.2026] id — номер светофора для связи станций (автоинкремент), ro: true — только чтение
//               [ДОБАВЛЕНО 10.11.2026] fixed: true — имя задано вручную (автонумерация его не трогает)
//    even     — [ДОБАВЛЕНО 11.11.2026] куда идут чётные поезда: "R" / "L" — по нему и пути со стрелками, и буквы Н / Ч светофоров
//    rightOdd — (старое, 10.11) true: движение → нечётное; при открытии переводится в even: "L"
//    labels   — [ДОБАВЛЕНО 10.11.2026] свободный текст: [{ x, y (клетки, дробные), text, size, bold }]
//    sections — участок → { type, color }
//    texts    — "x,y" → { text, size }
//    objects  — "x,y" (левый верхний угол) → { type: ammeter | building | clock, name, w, h, seconds }
//               [ДОБАВЛЕНО 05.11.2026] амперметр, вокзал, часовая башня; веб-макет этот раздел не знает и не трогает
// =====================================================================
public sealed class Station
{
    [JsonPropertyName("format")] public string Format { get; set; } = "station-editor";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("name")] public string Name { get; set; } = "Новая станция";
    [JsonPropertyName("tiles")] public Dictionary<string, Tile> Tiles { get; set; } = new();
    [JsonPropertyName("sections")] public Dictionary<string, SectionInfo> Sections { get; set; } = new();
    [JsonPropertyName("texts")] public Dictionary<string, TextItem> Texts { get; set; } = new();
    [JsonPropertyName("objects")] public Dictionary<string, StationObject> Objects { get; set; } = new();

    // ---- [ДОБАВЛЕНО 11.11.2026] нумерация по правилам (Model/Numbering.cs; ВСН 56-78 прил. 2, РУ-30-80 листы 89–90) ----
    // [11.11.2026] при слиянии веток эти три свойства попали в NamingSettings — вернул в Station, где их ищет Numbering.cs
    /// <summary>Куда идут чётные поезда: "R" — вправо (→, как было в редакторе всегда), "L" — влево (←).</summary>
    [JsonPropertyName("even")] public string Even { get; set; } = "R";
    /// <summary>Боковые приёмо-отправочные пути: "parity" — чётные со стороны чётного главного, нечётные — со стороны нечётного;
    /// "row" — подряд от пассажирского здания в сторону поля (малые станции, где пути принимают поезда обоих направлений).</summary>
    [JsonPropertyName("numbering")] public string Numbering { get; set; } = "parity";
    /// <summary>Номера новым путям, стрелкам и светофорам — сами при рисовании.</summary>
    [JsonPropertyName("autoNumbers")] public bool AutoNumbers { get; set; } = true;

    /// <summary>[ДОБАВЛЕНО 11.11.2026] Подписи участков и путей (1П, 3П, 1СП…) — авто-текст под путями.
    /// true — показывать; нет значения — не показывать. Действует и в редакторе, и в «Станции по схеме» (JsonLayout).</summary>
    [JsonPropertyName("secLabels")] public bool? SecLabels { get; set; }
    /// <summary>[ДОБАВЛЕНО 11.11.2026] Название пути — в разрыве линии пути со стрелками «→ 3П ←», как на схемах РУ-30-80
    /// (стр. 89, 90). Нет значения — включено; false — выключено.</summary>
    [JsonPropertyName("trackGaps")] public bool? TrackGaps { get; set; }

    /// <summary>[ДОБАВЛЕНО 10.11.2026, с 11.11 только для чтения старых файлов] true — движение → нечётное.
    /// При открытии файла переводится в Even ("L") и больше не пишется — направление задаёт одна настройка Even.</summary>
    [JsonPropertyName("rightOdd")] public bool? RightOdd { get; set; }
    /// <summary>[ДОБАВЛЕНО 10.11.2026, с 10.11 не используется] Признак «имена считались автоматически» —
    /// остался в файлах, сохранённых в тот день; имя теперь даётся только новому светофору.</summary>
    [JsonPropertyName("autoNames")] public bool? AutoNames { get; set; }
    /// <summary>[ДОБАВЛЕНО 10.11.2026] Настройки обозначений: шаблоны имён по видам, вид светофора по типу участка
    /// (окно «Обозначения…»). null — всё по умолчанию.</summary>
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; }
    /// <summary>[ДОБАВЛЕНО 10.11.2026] Свободный текст — подписи в любом месте схемы, не по клеткам (инструмент «Свободный текст»).</summary>
    [JsonPropertyName("labels")] public List<FreeLabel> Labels { get; set; } = new();
}

/// <summary>[ДОБАВЛЕНО 10.11.2026] Свободный текст: левый верхний угол в клетках (дробные — не по сетке), размер, жирный.</summary>
public sealed class FreeLabel
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("size")] public double Size { get; set; } = 14;
    /// <summary>null — жирный (как надписи в клетках), false — обычный</summary>
    [JsonPropertyName("bold")] public bool? Bold { get; set; }
    [JsonIgnore] public bool IsBold => Bold != false;
}

/// <summary>[ДОБАВЛЕНО 10.11.2026] Обозначения светофоров станции. Пустые значения — по умолчанию (Model\Naming.cs).</summary>
public sealed class NamingSettings
{
    /// <summary>Вид светофора → шаблон имени: {Б} — буква Н/Ч, {П} — номер пути, {N} — порядковый номер, {О} — имя основного.</summary>
    [JsonPropertyName("templates")] public Dictionary<string, string> Templates { get; set; }
    /// <summary>Тип участка (path, plain, line, dead, switch) → вид нового светофора; "auto-line" — входной перед станцией,
    /// иначе проходной; "" — тот, что выбран в панели.</summary>
    [JsonPropertyName("kindBySec")] public Dictionary<string, string> KindBySec { get; set; }
    /// <summary>[ДОБАВЛЕНО 11.11.2026] Чётность номеров маневровых: null — по направлению светофора (Н — М1, М3…; Ч — М2, М4…);
    /// "neck" — по горловине (нечётные у входного Н, урок caredenis.ru les03).</summary>
    [JsonPropertyName("shuntParity")] public string ShuntParity { get; set; }
}

/// <summary>[ДОБАВЛЕНО 05.11.2026] Объект на схеме размером W×H клеток (не путь): амперметр, вокзал, часовая башня.</summary>
public sealed class StationObject
{
    /// <summary>ammeter — амперметр тока перевода стрелок, building — пассажирское здание, clock — часовая башня</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "building";
    /// <summary>Надпись: название вокзала, подпись прибора или башни</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("w")] public int W { get; set; } = 1;
    [JsonPropertyName("h")] public int H { get; set; } = 1;
    /// <summary>Часы: показывать секундную стрелку (нет значения — показывать)</summary>
    [JsonPropertyName("seconds")] public bool? Seconds { get; set; }
}

public sealed class Tile
{
    /// <summary>Тип кубика: straight, curve, switch, cross, buffer, exit, crossing</summary>
    [JsonPropertyName("t")] public string T { get; set; }
    /// <summary>Вариант (поворот / отражение)</summary>
    [JsonPropertyName("v")] public int V { get; set; }
    /// <summary>Участок ("" — без участка)</summary>
    [JsonPropertyName("sec")] public string Sec { get; set; } = "";
    /// <summary>Номер стрелки (только у стрелки)</summary>
    [JsonPropertyName("name")] public string Name { get; set; }
    /// <summary>Съезд: стрелки с одинаковой группой переводятся вместе</summary>
    [JsonPropertyName("group")] public string Group { get; set; }
    /// <summary>false — стрелка в минусе; нет значения — в плюсе</summary>
    [JsonPropertyName("plus")] public bool? Plus { get; set; }
    [JsonPropertyName("sigs")] public List<Signal> Sigs { get; set; }
    /// <summary>[ДОБАВЛЕНО 12.11.2026] Тормозная позиция (только у кубика "brake"), для будущих сортировочных горок.</summary>
    [JsonPropertyName("brk")] public BrakePos Brk { get; set; }

    [JsonIgnore] public bool IsPlus => Plus != false;
    [JsonIgnore] public bool HasSigs => Sigs != null && Sigs.Count > 0;
}

/// <summary>[ДОБАВЛЕНО 12.11.2026] Тормозная позиция сортировочной горки — вагонный замедлитель на пути
/// («Проектирование сортировочных горок», УрГУПС 2018: рис. 2.1, 4.1 — толстая полоса на пути; табл. 3.1 — типы замедлителей).
/// В редакторе — только вид и данные; торможение будет считать другая программа (по id).</summary>
public sealed class BrakePos
{
    /// <summary>1 — ТП1 (первая, спускная часть), 2 — ТП2 (вторая, пучковая), 3 — ТП3 (третья, парковая)</summary>
    [JsonPropertyName("pos")] public int Pos { get; set; } = 1;
    /// <summary>Подпись на схеме; пусто — «ТП1» / «ТП2» / «ТП3» по позиции</summary>
    [JsonPropertyName("name")] public string Name { get; set; }
    /// <summary>Тип замедлителя (КЗ-3, РНЗ-2М…, табл. 3.1); пусто — не указан</summary>
    [JsonPropertyName("type")] public string Type { get; set; }
    /// <summary>Номер для другой программы (1, 2, 3… — автоинкремент, уникальный на схеме, как id светофора)</summary>
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonIgnore] public string Label => string.IsNullOrWhiteSpace(Name) ? "ТП" + Pos : Name.Trim();
}

public sealed class Signal
{
    /// <summary>R — «→» (чётное), L — «←» (нечётное)</summary>
    [JsonPropertyName("dir")] public string Dir { get; set; } = "R";
    /// <summary>entry, exit, route, shunt, block, warning, repeater, barrier, cover, other (см. Core.SigKinds)</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "entry";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>[ДОБАВЛЕНО 09.11.2026] Номер светофора для связи станций (1, 2, 3… — автоинкремент, уникальный на схеме):
    /// по нему другая программа (станция) находит, какому светофору у себя показать присланное показание.</summary>
    [JsonPropertyName("id")] public int Id { get; set; }
    /// <summary>[ДОБАВЛЕНО 09.11.2026] Только чтение: показание задаёт другая станция по сети (как Remote в программах
    /// станций, DispatchMirror). Маршрутов от него нет; без данных от соседа — погашен. null — обычный светофор.</summary>
    [JsonPropertyName("ro")] public bool? ReadOnly { get; set; }

    [JsonIgnore] public bool IsReadOnly => ReadOnly == true;

    /// <summary>[ДОБАВЛЕНО 10.11.2026] Имя задано вручную и автоматической нумерацией не меняется. null — имя автоматическое.</summary>
    [JsonPropertyName("fixed")] public bool? Fixed { get; set; }
    [JsonIgnore] public bool IsFixed => Fixed == true;

    /// <summary>[ДОБАВЛЕНО 10.11.2026] Буква (Н / Ч), с которой светофор назван при добавлении. Есть — имя собирается по шаблону
    /// и пересобирается само (переименовали путь — поменялась цифра). null — старый светофор, имя не трогается.</summary>
    [JsonPropertyName("ltr")] public string Ltr { get; set; }
    /// <summary>Цифра, заданная вручную (поле «№» в свойствах). null — от названия пути, иначе порядковая (Seq).</summary>
    [JsonPropertyName("num")] public string Num { get; set; }
    /// <summary>Порядковый номер, выданный при добавлении, — когда светофор стоит не на пути (М1 в горловине, проходной 3).</summary>
    [JsonPropertyName("seq")] public int? Seq { get; set; }
}

public sealed class SectionInfo
{
    /// <summary>auto, path, plain, line, dead</summary>
    [JsonPropertyName("type")] public string Type { get; set; }
    /// <summary>Цвет в редакторе, "#rrggbb"</summary>
    [JsonPropertyName("color")] public string Color { get; set; }
}

public sealed class TextItem
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("size")] public double Size { get; set; } = 14;
}

/// <summary>Чтение и запись файла станции (JSON).</summary>
public static class StationFile
{
    public const string DialogFilter = "Схема станции (*.station.json; *.json)|*.station.json;*.json|Все файлы (*.*)|*.*";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,      // кириллица в файле читается как есть
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
    static readonly JsonSerializerOptions Compact = new(Pretty) { WriteIndented = false };

    public static string ToJson(Station st, bool pretty = true) => JsonSerializer.Serialize(st, pretty ? Pretty : Compact);

    /// <summary>Снимок для отмены / возврата (без проверки).</summary>
    public static Station FromSnapshot(string json) => JsonSerializer.Deserialize<Station>(json, Compact);

    public static Station Clone(Station st) => FromSnapshot(ToJson(st, false));

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Часть станции — только выбранные клетки (для «Экспорт JSON → Выделенные клетки»).
    /// Кубики и надписи — только из выбранных; объекты (вокзал, часы…) и свободный текст — если целиком внутри прямоугольника,
    /// охватывающего выбранные клетки; участки — только те, что остались на кубиках. Координаты не сдвигаются
    /// («Станция по схеме» сама подгоняет схему под окно), id светофоров и тормозных позиций — прежние.
    /// Настройки станции (направление, обозначения, подписи) — как у всей станции.</summary>
    public static Station Fragment(Station st, IEnumerable<string> keys, string name = null)
    {
        var set = new HashSet<string>(keys.Where(k => st.Tiles.ContainsKey(k) || st.Texts.ContainsKey(k)));
        var f = Clone(st);
        f.Name = string.IsNullOrWhiteSpace(name) ? st.Name + " (часть)" : name.Trim();
        foreach (var k in f.Tiles.Keys.Where(k => !set.Contains(k)).ToList()) f.Tiles.Remove(k);
        foreach (var k in f.Texts.Keys.Where(k => !set.Contains(k)).ToList()) f.Texts.Remove(k);

        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue;
        foreach (var k in set)
        {
            var (x, y) = Core.Parse(k);
            x1 = Math.Min(x1, x); y1 = Math.Min(y1, y); x2 = Math.Max(x2, x); y2 = Math.Max(y2, y);
        }
        bool Inside(int x, int y) => x >= x1 && x <= x2 && y >= y1 && y <= y2;
        foreach (var k in f.Objects.Keys.ToList())
        {
            var o = f.Objects[k];
            if (!Core.TryParse(k, out int ox, out int oy) || !Inside(ox, oy) || !Inside(ox + Math.Max(1, o.W) - 1, oy + Math.Max(1, o.H) - 1))
                f.Objects.Remove(k);
        }
        // клетка свободного текста — по тому же правилу, что и при стирании (MainForm.Canvas: EraseAt)
        f.Labels?.RemoveAll(l => !Inside((int)Math.Floor(l.X), (int)Math.Floor(l.Y + 0.25)));

        var used = new HashSet<string>(f.Tiles.Values.Select(t => t.Sec ?? ""));
        foreach (var s in f.Sections.Keys.Where(s => !used.Contains(s)).ToList()) f.Sections.Remove(s);
        return f;
    }

    /// <summary>Разобрать файл. Бросает InvalidDataException с понятным текстом.</summary>
    public static Station FromJson(string json)
    {
        JsonNode root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e) { throw new InvalidDataException("файл повреждён или это не JSON (" + e.Message + ")"); }

        if (root is not JsonObject o || o["tiles"] is not JsonObject)
            throw new InvalidDataException("это не файл редактора станций (нет раздела tiles)");

        Station st;
        try { st = root.Deserialize<Station>(Pretty); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("неверные данные в файле: " + e.Message); }
        return Normalize(st);
    }

    public static Station Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

    public static void Save(Station st, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // сначала во временный файл — чтобы при сбое не испортить старый
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson(st), new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }

    /// <summary>Проверка и дочистка загруженной станции (как SE.normalize в веб-макете).</summary>
    public static Station Normalize(Station st)
    {
        st ??= new Station();
        st.Format ??= "station-editor";
        st.Name = string.IsNullOrWhiteSpace(st.Name) ? "Без названия" : st.Name;
        st.Tiles ??= new();
        st.Sections ??= new();
        st.Texts ??= new();

        foreach (var k in st.Tiles.Keys.ToList())
        {
            var t = st.Tiles[k];
            if (t == null || t.T == null || !Core.Types.ContainsKey(t.T) || !Core.TryParse(k, out _, out _))
            { st.Tiles.Remove(k); continue; }
            if (t.V < 0 || t.V >= Core.Types[t.T].Variants.Length) t.V = 0;
            t.Sec ??= "";
            if (t.Plus == true) t.Plus = null;
            if (t.T != "switch") { t.Plus = null; }
            if (t.Sigs != null)
            {
                t.Sigs.RemoveAll(s => s == null);
                foreach (var s in t.Sigs)
                {
                    s.Dir = s.Dir == "L" ? "L" : "R";
                    if (s.Kind == null || Core.SigKindName(s.Kind) == null) s.Kind = "other";
                    s.Name ??= "";
                    if (s.ReadOnly == false) s.ReadOnly = null;
                    if (s.Fixed == false) s.Fixed = null;
                }
                if (t.Sigs.Count == 0) t.Sigs = null;
            }
        }
        Core.AssignSignalIds(st);    // [ДОБАВЛЕНО 09.11.2026] старые файлы без id и повторы — получают новые номера
        Core.AssignBrakes(st);       // [ДОБАВЛЕНО 12.11.2026] тормозные позиции: данные и id
        // [ДОБАВЛЕНО 11.11.2026] одна настройка направления: старое «rightOdd: true» (→ нечётное) = «чётные идут ←» (even: "L")
        if (st.RightOdd == true) st.Even = "L";
        st.RightOdd = null;
        if (st.Even != "L") st.Even = "R";
        foreach (var k in st.Sections.Keys.ToList())
            if (st.Sections[k] == null) st.Sections[k] = new SectionInfo { Type = "auto" };
        foreach (var k in st.Texts.Keys.ToList())
        {
            var tx = st.Texts[k];
            if (tx == null || string.IsNullOrEmpty(tx.Text) || !Core.TryParse(k, out _, out _)) { st.Texts.Remove(k); continue; }
            if (!(tx.Size > 0)) tx.Size = 14;
        }
        st.Labels ??= new();    // [ДОБАВЛЕНО 10.11.2026] свободный текст
        st.Labels.RemoveAll(l => l == null || string.IsNullOrWhiteSpace(l.Text) || double.IsNaN(l.X) || double.IsNaN(l.Y));
        foreach (var l in st.Labels) if (!(l.Size > 0)) l.Size = 14;
        st.Objects ??= new();
        foreach (var k in st.Objects.Keys.ToList())
        {
            var o = st.Objects[k];
            var ot = o == null ? null : Core.ObjTypeOf(o.Type);
            if (ot == null || !Core.TryParse(k, out _, out _)) { st.Objects.Remove(k); continue; }
            o.Name ??= "";
            o.W = o.W > 0 ? Math.Min(o.W, 30) : ot.W;
            o.H = o.H > 0 ? Math.Min(o.H, 30) : ot.H;
        }
        return st;
    }

    public static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);
}
