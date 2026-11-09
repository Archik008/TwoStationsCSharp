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
//                         sigs: [{ dir: 'R' | 'L', kind, name }] }
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

    [JsonIgnore] public bool IsPlus => Plus != false;
    [JsonIgnore] public bool HasSigs => Sigs != null && Sigs.Count > 0;
}

public sealed class Signal
{
    /// <summary>R — «→» (чётное), L — «←» (нечётное)</summary>
    [JsonPropertyName("dir")] public string Dir { get; set; } = "R";
    /// <summary>entry, exit, route, shunt, block, warning, repeater, barrier, cover, other (см. Core.SigKinds)</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "entry";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
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
                }
                if (t.Sigs.Count == 0) t.Sigs = null;
            }
        }
        foreach (var k in st.Sections.Keys.ToList())
            if (st.Sections[k] == null) st.Sections[k] = new SectionInfo { Type = "auto" };
        foreach (var k in st.Texts.Keys.ToList())
        {
            var tx = st.Texts[k];
            if (tx == null || string.IsNullOrEmpty(tx.Text) || !Core.TryParse(k, out _, out _)) { st.Texts.Remove(k); continue; }
            if (!(tx.Size > 0)) tx.Size = 14;
        }
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
