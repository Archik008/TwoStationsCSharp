using System.Globalization;
using System.Text.RegularExpressions;

namespace StationEditor.Model;

// =====================================================================
//  КУБИКИ СХЕМЫ И МОДЕЛЬ СТАНЦИИ
//
//  Схема — сетка клеток («кубиков»). Клетка соединяется с соседями через 6 выходов:
//  W, E — середины левой и правой стороны; NW, NE, SW, SE — углы.
//  Так собираются пути и съезды под 45°, как на схемах ДСП.
// =====================================================================

public sealed class DirInfo
{
    public readonly int Dx, Dy;
    /// <summary>Точка выхода в долях клетки</summary>
    public readonly float Px, Py;
    public DirInfo(int dx, int dy, float px, float py) { Dx = dx; Dy = dy; Px = px; Py = py; }
}

/// <summary>Вариант кубика: рёбра (Edges), или стрелка (Trunk/Plus/Minus), или конец (Ends).</summary>
public sealed class Variant
{
    public string[][] Edges;
    public string Trunk, Plus, Minus;
    public string[] Ends;
}

public sealed class TileType
{
    public string Id, Name, Key, Hint;
    public Variant[] Variants;
}

public readonly record struct Edge(string A, string B, string Role);
public readonly record struct Pass(string Out, bool? Need);
public readonly record struct SigRef(string Key, int Idx);

public sealed class Neighbor
{
    public string Key;
    public Tile Tile;
    public string InSide;
}

public sealed class SignalEntry
{
    public string Key;
    public int Idx;
    public Signal Sig;
    public Tile Tile;
    public SigRef Ref => new(Key, Idx);
}

public static class Core
{
    /// <summary>Размер клетки в проектных единицах (как DesignW/DesignH в SchemeGeometry.cs)</summary>
    public const float Cell = 40f;

    public static readonly Dictionary<string, DirInfo> Dirs = new()
    {
        ["E"] = new(1, 0, 1, 0.5f),
        ["NE"] = new(1, -1, 1, 0),
        ["SE"] = new(1, 1, 1, 1),
        ["W"] = new(-1, 0, 0, 0.5f),
        ["NW"] = new(-1, -1, 0, 0),
        ["SW"] = new(-1, 1, 0, 1),
    };

    public static readonly Dictionary<string, string> Opp = new()
    { ["E"] = "W", ["W"] = "E", ["NE"] = "SW", ["SW"] = "NE", ["SE"] = "NW", ["NW"] = "SE" };

    public static readonly Dictionary<string, string> DirArrow = new()
    { ["E"] = "→", ["W"] = "←", ["NE"] = "↗", ["NW"] = "↖", ["SE"] = "↘", ["SW"] = "↙" };

    /// <summary>Выход ведёт вправо (движение «→», чётное)</summary>
    public static bool IsRight(string d) => d == "E" || d == "NE" || d == "SE";

    // ---------------------------------------------------------------------
    //  ТИПЫ КУБИКОВ. Variants — повороты / отражения (клавиша R перебирает их)
    // ---------------------------------------------------------------------
    static string[] P(string a, string b) => new[] { a, b };
    static Variant Ed(params string[][] edges) => new() { Edges = edges };
    static Variant Sw(string trunk, string plus, string minus) => new() { Trunk = trunk, Plus = plus, Minus = minus };
    static Variant End(string d) => new() { Ends = new[] { d } };

    public static readonly TileType[] TypeList =
    {
        new() { Id = "straight", Name = "Путь", Key = "1", Hint = "прямой участок пути: горизонталь или диагональ",
            Variants = new[] { Ed(P("W", "E")), Ed(P("SW", "NE")), Ed(P("NW", "SE")) } },
        new() { Id = "curve", Name = "Изгиб", Key = "2", Hint = "переход с горизонтали на диагональ (съезд, ответвление)",
            Variants = new[] { Ed(P("W", "NE")), Ed(P("W", "SE")), Ed(P("E", "NW")), Ed(P("E", "SW")) } },
        new() { Id = "switch", Name = "Стрелка", Key = "3", Hint = "остряк, прямое направление (плюс) и отклонение (минус)",
            Variants = new[] { Sw("W", "E", "NE"), Sw("W", "E", "SE"), Sw("E", "W", "NW"), Sw("E", "W", "SW") } },
        new() { Id = "cross", Name = "Пересечение", Key = "4", Hint = "глухое пересечение путей",
            Variants = new[] { Ed(P("W", "E"), P("SW", "NE")), Ed(P("W", "E"), P("NW", "SE")), Ed(P("SW", "NE"), P("NW", "SE")) } },
        new() { Id = "buffer", Name = "Тупик", Key = "5", Hint = "упор в конце тупикового пути",
            Variants = new[] { End("W"), End("E") } },
        new() { Id = "exit", Name = "Выход", Key = "6", Hint = "путь уходит за край схемы: перегон, тоннель, соседняя станция",
            Variants = new[] { End("E"), End("W") } },
        new() { Id = "crossing", Name = "Переезд", Key = "7", Hint = "путь с автомобильным переездом",
            Variants = new[] { Ed(P("W", "E")) } },
    };

    public static readonly Dictionary<string, TileType> Types = TypeList.ToDictionary(t => t.Id);

    // [ИЗМЕНЕНО 05.11.2026] добавлены маршрутный, предупредительный, повторительный, заградительный, прикрытия
    // (по Инструкции по сигнализации: гл. 2.8, 2.9 и статье «Железнодорожная светофорная сигнализация»)
    public static readonly (string Id, string Name)[] SigKinds =
    {
        ("entry", "Входной"), ("exit", "Выходной"), ("route", "Маршрутный"), ("shunt", "Маневровый"), ("block", "Проходной"),
        ("warning", "Предупредительный"), ("repeater", "Повторительный"), ("barrier", "Заградительный"), ("cover", "Прикрытия"),
        ("other", "Чужой (соседа)"),
    };

    /// <summary>Светофор открывается поездным маршрутом (входной, выходной, маршрутный).</summary>
    public static bool IsTrainStart(string kind) => kind is "entry" or "exit" or "route";
    /// <summary>Светофор открывается маневровым маршрутом (маневровый; выходной и маршрутный — лунно-белым).</summary>
    public static bool IsShuntStart(string kind) => kind is "shunt" or "exit" or "route";

    /// <summary>Чем светофор занят в «Испытании» (подсказка в свойствах и при щелчке).</summary>
    public static string SigKindHint(string kind) => kind switch
    {
        // [ИЗМЕНЕНО 09.11.2026] по РУ-30-80 (стр. 9, 14, 20)
        "entry" => "Приём с перегона. Красный; жёлтый / зелёный — приём на главный путь; жёлтый мигающий — выходной открыт двумя жёлтыми; " +
                   "два жёлтых (верхний мигает, если выходной открыт) — с отклонением по стрелкам.",
        "exit" => "Отправление на перегон. Красный; жёлтый — свободен один блок-участок, зелёный — два и больше; " +
                  "жёлтый мигающий — следующий показывает два жёлтых; с главного пути (IП, IIП…) с отклонением по стрелкам — " +
                  "два жёлтых (верхний мигает, если следующий открыт); лунно-белый — манёвры.",
        "route" => "Маршрутный (НМ, ЧМ): из одного района станции в другой. Огни — как у выходного (два жёлтых — только с главного пути).",
        "shunt" => "Маневровый: синий — манёвры запрещены, лунно-белый — разрешены.",
        "block" => "Проходной автоблокировки: красный — блок-участок впереди занят, жёлтый — следующий светофор закрыт, " +
                   "жёлтый мигающий — следующий входной открыт на боковой путь, зелёный — свободно.",
        "warning" => "Предупредительный: повторяет, что впереди. Зелёный — основной открыт, жёлтый — закрыт, " +
                     "жёлтый мигающий — входной открыт на боковой путь. Перед заградительным: жёлтый, когда он красный, иначе не горит.",
        "repeater" => "Повторительный: зелёный — выходной (маршрутный) впереди открыт. Обычно не горит, и тогда сигнального значения не имеет.",
        "barrier" => "Заградительный (мачта в чёрно-белые полосы): нормально не горит; красный — стой. " +
                     "В «Испытании» горит красным, когда переезд впереди занят, или включается щелчком.",
        "cover" => "Прикрытия: ограждает пересечение путей, разводной мост. Красный; зелёный — открыт щелчком, если впереди свободно.",
        _ => "Светофор соседней станции: на нашей схеме только показан.",
    } + " Галочка «только чтение» — показание задаёт другая станция по id (подходит для любого вида).";

    // ---------------------------------------------------------------------
    //  [ДОБАВЛЕНО 05.11.2026] ОБЪЕКТЫ СТАНЦИИ (не пути): занимают прямоугольник W×H клеток
    // ---------------------------------------------------------------------
    public sealed class ObjType
    {
        public string Id, Name, Key, Hint, DefaultName;
        public int W, H;
    }

    public static readonly ObjType[] ObjTypes =
    {
        new() { Id = "ammeter", Name = "Амперметр", Key = "8", W = 3, H = 2, DefaultName = "Ток перевода стрелок",
                Hint = "прибор на табло: ток электродвигателя при переводе стрелки (в «Испытании»)" },
        new() { Id = "building", Name = "Вокзал", Key = "9", W = 6, H = 2, DefaultName = "",
                Hint = "пассажирское здание со своим названием" },
        new() { Id = "clock", Name = "Часы", Key = "0", W = 2, H = 4, DefaultName = "",
                Hint = "часовая башня: показывает время компьютера" },
    };

    public static ObjType ObjTypeOf(string id) => ObjTypes.FirstOrDefault(o => o.Id == id);

    /// <summary>Объект, который накрывает клетку (x, y): ключ его левого верхнего угла или null.</summary>
    public static string ObjectAt(Station st, int x, int y)
    {
        foreach (var (k, o) in st.Objects)
        {
            var (ox, oy) = Parse(k);
            if (x >= ox && x < ox + o.W && y >= oy && y < oy + o.H) return k;
        }
        return null;
    }

    /// <summary>Свободен ли прямоугольник под объект (другие объекты не мешают; except — сам переносимый объект).</summary>
    public static bool ObjectFits(Station st, int x, int y, int w, int h, string except = null)
    {
        foreach (var (k, o) in st.Objects)
        {
            if (k == except) continue;
            var (ox, oy) = Parse(k);
            if (x < ox + o.W && ox < x + w && y < oy + o.H && oy < y + h) return false;
        }
        return true;
    }

    public static readonly (string Id, string Name)[] SecTypes =
    {
        ("path", "Путь (приём / отправление)"), ("plain", "Участок между светофорами"), ("line", "Перегон, блок-участок"),
        ("dead", "Тупик, вытяжка"), ("switch", "Стрелочный (сам — есть стрелка)"),
    };

    public static string SigKindName(string id)
    {
        foreach (var k in SigKinds) if (k.Id == id) return k.Name;
        return null;
    }

    public static string SecTypeName(string id)
    {
        foreach (var k in SecTypes) if (k.Id == id) return k.Name;
        return "";
    }

    public static string ShortType(string t) => t switch
    {
        "path" => "путь", "plain" => "участок", "line" => "перегон", "dead" => "тупик", "switch" => "стрелочный", _ => t
    };

    // ---------------------------------------------------------------------
    //  КЛЕТКИ
    // ---------------------------------------------------------------------
    public static string Key(int x, int y) =>
        x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);

    public static (int X, int Y) Parse(string k)
    {
        TryParse(k, out int x, out int y);
        return (x, y);
    }

    public static bool TryParse(string k, out int x, out int y)
    {
        x = y = 0;
        if (k == null) return false;
        int i = k.IndexOf(',');
        return i > 0
            && int.TryParse(k.AsSpan(0, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(k.AsSpan(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    public static Variant VariantOf(Tile t)
    {
        var vs = Types[t.T].Variants;
        return t.V >= 0 && t.V < vs.Length ? vs[t.V] : vs[0];
    }

    /// <summary>Рёбра кубика: пары выходов, по которым едет поезд. У тупика и выхода второй конец — null (центр клетки).</summary>
    public static List<Edge> EdgesOf(Tile t)
    {
        var v = VariantOf(t);
        if (t.T == "switch") return new() { new(v.Trunk, v.Plus, "plus"), new(v.Trunk, v.Minus, "minus") };
        if (v.Ends != null) return new() { new(v.Ends[0], null, null) };
        var res = new List<Edge>(v.Edges.Length);
        foreach (var e in v.Edges) res.Add(new(e[0], e[1], null));
        return res;
    }

    public static List<string> EndsOf(Tile t)
    {
        var res = new List<string>(4);
        foreach (var e in EdgesOf(t))
        {
            if (!res.Contains(e.A)) res.Add(e.A);
            if (e.B != null && !res.Contains(e.B)) res.Add(e.B);
        }
        return res;
    }

    /// <summary>Куда можно выехать из кубика, въехав через сторону inSide.
    /// Need: null — всегда; true / false — только при стрелке в плюсе / минусе.</summary>
    public static List<Pass> Through(Tile t, string inSide)
    {
        var res = new List<Pass>(2);
        if (t.T == "switch")
        {
            var v = VariantOf(t);
            if (inSide == v.Trunk) { res.Add(new(v.Plus, true)); res.Add(new(v.Minus, false)); }
            else if (inSide == v.Plus) res.Add(new(v.Trunk, true));
            else if (inSide == v.Minus) res.Add(new(v.Trunk, false));
            return res;
        }
        foreach (var e in EdgesOf(t))
        {
            if (e.B == null) continue;
            if (e.A == inSide) res.Add(new(e.B, null));
            else if (e.B == inSide) res.Add(new(e.A, null));
        }
        return res;
    }

    /// <summary>Сосед через выход dir, если он стыкуется с этим выходом (иначе null).</summary>
    public static Neighbor NeighborOf(Station st, string key, string dir)
    {
        var (x, y) = Parse(key);
        var D = Dirs[dir];
        var nk = Key(x + D.Dx, y + D.Dy);
        if (!st.Tiles.TryGetValue(nk, out var nt)) return null;
        var opp = Opp[dir];
        if (!EndsOf(nt).Contains(opp)) return null;
        return new Neighbor { Key = nk, Tile = nt, InSide = opp };
    }

    // ---------------------------------------------------------------------
    //  СТАНЦИЯ
    // ---------------------------------------------------------------------
    public static Station NewStation() => new();

    public static Dictionary<string, List<string>> TilesBySection(Station st)
    {
        var m = new Dictionary<string, List<string>>();
        foreach (var (k, t) in st.Tiles)
        {
            if (string.IsNullOrEmpty(t.Sec)) continue;
            if (!m.TryGetValue(t.Sec, out var list)) m[t.Sec] = list = new List<string>();
            list.Add(k);
        }
        return m;
    }

    /// <summary>Все участки: и описанные в sections, и встреченные на клетках.</summary>
    public static List<string> SectionNames(Station st)
    {
        var s = new HashSet<string>(st.Sections.Keys);
        foreach (var t in st.Tiles.Values) if (!string.IsNullOrEmpty(t.Sec)) s.Add(t.Sec);
        var list = s.ToList();
        list.Sort(NaturalComparer.Instance);
        return list;
    }

    static readonly Regex ReSwitch = new("СП$", RegexOptions.Compiled);
    static readonly Regex RePath = new(@"^(I|II|III|IV|V|\d+)П$", RegexOptions.Compiled);
    static readonly Regex ReLine = new(@"БУ$|^Б\d|^НАУ$|^ЧУ$|^НУ$|^У|ПЕРЕГОН", RegexOptions.Compiled);
    static readonly Regex ReDead = new(@"Т$|ТУП|^ВТ", RegexOptions.Compiled);

    /// <summary>Тип участка по названию — как принято на схемах (1П, 2-4СП, НАУ, Б12, ВТ…). Можно поменять вручную.</summary>
    public static string GuessSecType(string name)
    {
        var n = (name ?? "").ToUpperInvariant();
        if (ReSwitch.IsMatch(n)) return "switch";
        if (RePath.IsMatch(n)) return "path";
        if (ReLine.IsMatch(n)) return "line";
        if (ReDead.IsMatch(n)) return "dead";
        return "plain";
    }

    public static string SecType(Station st, string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        foreach (var t in st.Tiles.Values) if (t.Sec == name && t.T == "switch") return "switch";
        if (st.Sections.TryGetValue(name, out var s) && !string.IsNullOrEmpty(s.Type) && s.Type != "auto") return s.Type;
        var g = GuessSecType(name);
        return g == "switch" ? "plain" : g;    // «…СП» без стрелки — просто участок
    }

    public static Color SecColor(Station st, string name)
    {
        if (st.Sections.TryGetValue(name, out var s) && TryParseHex(s.Color, out var c)) return c;
        int h = 0;
        foreach (char ch in name) h = (h * 31 + ch) % 360;
        return Hsl((h * 137) % 360, 0.75, 0.52);
    }

    public static Color Hsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double hp = h / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        double r = 0, g = 0, b = 0;
        if (hp < 1) { r = c; g = x; }
        else if (hp < 2) { r = x; g = c; }
        else if (hp < 3) { g = c; b = x; }
        else if (hp < 4) { g = x; b = c; }
        else if (hp < 5) { r = x; b = c; }
        else { r = c; b = x; }
        double m = l - c / 2;
        int B(double v) => Math.Clamp((int)Math.Round((v + m) * 255), 0, 255);
        return Color.FromArgb(B(r), B(g), B(b));
    }

    public static bool TryParseHex(string s, out Color c)
    {
        c = Color.Empty;
        if (string.IsNullOrEmpty(s) || s[0] != '#') return false;
        var h = s.Substring(1);
        if (h.Length == 3) h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        c = Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    /// <summary>Группа стрелки: спаренные (одинаковый «съезд») переводятся вместе.</summary>
    public static string SwitchGroup(string key, Tile t) => !string.IsNullOrEmpty(t.Group) ? "g:" + t.Group : "k:" + key;

    public static List<SignalEntry> AllSignals(Station st)
    {
        var res = new List<SignalEntry>();
        foreach (var (k, t) in st.Tiles)
        {
            if (t.Sigs == null) continue;
            for (int i = 0; i < t.Sigs.Count; i++) res.Add(new SignalEntry { Key = k, Idx = i, Sig = t.Sigs[i], Tile = t });
        }
        return res;
    }

    // «…СП» — стрелочный участок, не путь
    /// <summary>[ДОБАВЛЕНО 09.11.2026] Следующий свободный id светофора (автоинкремент: наибольший на схеме + 1).</summary>
    public static int NextSignalId(Station st)
    {
        int max = 0;
        foreach (var s in AllSignals(st)) max = Math.Max(max, s.Sig.Id);
        return max + 1;
    }

    /// <summary>Выдать id светофорам без номера (0) и повторам: по порядку клеток, номера продолжают наибольший.</summary>
    public static void AssignSignalIds(Station st)
    {
        var all = AllSignals(st).OrderBy(s => s.Key, NaturalComparer.Instance).ThenBy(s => s.Idx).ToList();
        var seen = new HashSet<int>();
        int next = all.Select(s => s.Sig.Id).DefaultIfEmpty(0).Max() + 1;
        foreach (var s in all)
            if (s.Sig.Id <= 0 || !seen.Add(s.Sig.Id)) { s.Sig.Id = next++; seen.Add(s.Sig.Id); }
    }

    static readonly Regex ReTrackNo = new(@"^((?:[IVX]+)|\d+)([А-ЯA-Z-[С]]?)П$", RegexOptions.Compiled);

    /// <summary>Номер пути из названия участка: «3П» → «3», «IIП» → «II», «2АП» → «2А»; не путь — null.</summary>
    public static string TrackNo(string sec)
    {
        var m = ReTrackNo.Match((sec ?? "").Trim().ToUpperInvariant());
        return m.Success ? m.Groups[1].Value + m.Groups[2].Value : null;
    }

    /// <summary>Чётность горловины, где стоит клетка: по ближайшему входному (Н… — нечётная, Ч… — чётная); null — входных нет.</summary>
    static bool? NeckIsOdd(Station st, string key)
    {
        if (key == null) return null;
        var (x, _) = Parse(key);
        var e = AllSignals(st).Where(s => s.Sig.Kind == "entry" && !string.IsNullOrEmpty(s.Sig.Name))
            .OrderBy(s => Math.Abs(Parse(s.Key).X - x)).FirstOrDefault();
        if (e == null) return null;
        return e.Sig.Name[0] == 'Н' ? true : e.Sig.Name[0] == 'Ч' ? false : null;
    }

    /// <summary>Имя для нового светофора. [ИЗМЕНЕНО 09.11.2026] по уроку «Светофорная сигнализация» (caredenis.ru, les03)
    /// и РУ-30-80 стр. 89–90:
    ///   входной Н / Ч; выходной — буква направления + номер пути (Н3, ЧII, Н2А); маршрутный — + «М» (ЧМII, НМ5А);
    ///   маневровый — М + нечётный номер в нечётной горловине (где входной Н…), чётный — в чётной;
    ///   проходные — нечётные / чётные по направлению; заградительные З1…; прикрытия — буква направления + «П» + номер пути (НП1);
    ///   предупредительный и повторительный — «П» + имя светофора впереди (ПЧ, ПН5, ПЧМ2А).
    /// key — клетка светофора (номер пути берётся из её участка), mainAhead — светофор впереди.</summary>
    public static string SuggestSignalName(Station st, string kind, string dir, string mainAhead = null, string key = null)
    {
        var used = new HashSet<string>(AllSignals(st).Select(s => s.Sig.Name));
        string letter = dir == "R" ? "Ч" : "Н";
        string track = key != null && st.Tiles.TryGetValue(key, out var tile) ? TrackNo(tile.Sec) : null;
        string Try(string name) => !string.IsNullOrEmpty(name) && !used.Contains(name) ? name : null;

        if (kind == "entry" && Try(letter) is string en) return en;
        if (kind is "warning" or "repeater" && !string.IsNullOrEmpty(mainAhead) && Try("П" + mainAhead) is string pm) return pm;
        if (track != null)
        {
            if (kind == "exit" && Try(letter + track) is string ex) return ex;
            if (kind == "route" && Try(letter + "М" + track) is string rt) return rt;
            if (kind == "cover" && Try(letter + "П" + track) is string cv) return cv;
        }
        string prefix = "";
        int n = 1, step = 1;
        if (kind == "entry" || kind == "exit") prefix = letter;
        else if (kind == "route") prefix = letter + "М";
        else if (kind == "shunt")
        {
            prefix = "М"; step = 2;
            bool odd = NeckIsOdd(st, key) ?? dir != "R";    // нет входных — по направлению, как раньше
            n = odd ? 1 : 2;
        }
        else if (kind == "block") { n = dir == "R" ? 2 : 1; step = 2; }
        else if (kind is "warning" or "repeater") prefix = "П";
        else if (kind == "barrier") prefix = "З";
        else if (kind == "cover") prefix = letter + "П";
        else prefix = "С";
        while (used.Contains(prefix + n)) n += step;
        return prefix + n;
    }
}

/// <summary>Сравнение «как в русском алфавите, числа — по значению» (1П, 2П, 10П).</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();
    static readonly CompareInfo Ci = CultureInfo.GetCultureInfo("ru-RU").CompareInfo;

    public int Compare(string a, string b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            bool da = char.IsDigit(a[i]), db = char.IsDigit(b[j]);
            if (da != db)
            {
                int c0 = Ci.Compare(a, i, b, j, CompareOptions.IgnoreCase);
                return c0 != 0 ? c0 : (da ? -1 : 1);
            }
            int si = i, sj = j;
            if (da)
            {
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a.AsSpan(si, i - si).TrimStart('0');
                var nb = b.AsSpan(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                int c1 = na.SequenceCompareTo(nb);
                if (c1 != 0) return Math.Sign(c1);
            }
            else
            {
                while (i < a.Length && !char.IsDigit(a[i])) i++;
                while (j < b.Length && !char.IsDigit(b[j])) j++;
                int c2 = Ci.Compare(a, si, i - si, b, sj, j - sj, CompareOptions.IgnoreCase);
                if (c2 != 0) return c2;
            }
        }
        if (i < a.Length) return 1;
        if (j < b.Length) return -1;
        int c3 = Ci.Compare(a, b);
        return c3 != 0 ? c3 : string.CompareOrdinal(a, b);
    }
}
