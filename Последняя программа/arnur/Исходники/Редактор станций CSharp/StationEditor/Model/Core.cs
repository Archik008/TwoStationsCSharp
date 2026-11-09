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

    public static readonly (string Id, string Name)[] SigKinds =
    {
        ("entry", "Входной"), ("exit", "Выходной"), ("shunt", "Маневровый"), ("block", "Проходной"), ("other", "Чужой (соседа)"),
    };

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

    /// <summary>Имя для нового светофора: Ч / Н, Ч1… / Н1…, маневровые М2, М4… (→) и М1, М3… (←), проходные 1, 2…</summary>
    public static string SuggestSignalName(Station st, string kind, string dir)
    {
        var used = new HashSet<string>(AllSignals(st).Select(s => s.Sig.Name));
        string letter = dir == "R" ? "Ч" : "Н";
        if (kind == "entry" && !used.Contains(letter)) return letter;
        string prefix = "";
        int n = 1, step = 1;
        if (kind == "entry" || kind == "exit") prefix = letter;
        else if (kind == "shunt") { prefix = "М"; n = dir == "R" ? 2 : 1; step = 2; }
        else if (kind == "block") { n = dir == "R" ? 2 : 1; step = 2; }
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
