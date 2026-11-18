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
    /// <summary>[ДОБАВЛЕНО 12.11.2026] Кнопка палитры, которая ставит другой тип: кубик получает T = Base, V = VBase + вариант
    /// (симметричная стрелка — это "switch" с вариантами 4–7). null — обычный тип.</summary>
    public string Base;
    public int VBase;
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
        // [ИЗМЕНЕНО 12.11.2026] варианты 4–7 — симметричная стрелка (ставится своей кнопкой «Симм. стрелка», см. ниже)
        new() { Id = "switch", Name = "Стрелка", Key = "3", Hint = "остряк, прямое направление (плюс) и отклонение (минус)",
            Variants = new[] { Sw("W", "E", "NE"), Sw("W", "E", "SE"), Sw("E", "W", "NW"), Sw("E", "W", "SW"),
                               Sw("W", "NE", "SE"), Sw("W", "SE", "NE"), Sw("E", "NW", "SW"), Sw("E", "SW", "NW") } },
        // [ДОБАВЛЕНО 12.11.2026] симметричный стрелочный перевод (как на горочных горловинах): оба пути расходятся
        // от остряка под одинаковым углом «<». В файле — обычная стрелка ("switch", варианты 4–7): номер, перевод,
        // маршруты, «Испытание» и «Станция по схеме» — как у стрелки. Варианты: остряк слева / справа, какой путь — плюс.
        new() { Id = "symswitch", Name = "Симм. стрелка", Key = "Y", Base = "switch", VBase = 4,
            Hint = "симметричный стрелочный перевод: оба пути расходятся от остряка под одинаковым углом (горка, пучки)",
            Variants = new[] { Sw("W", "NE", "SE"), Sw("W", "SE", "NE"), Sw("E", "NW", "SW"), Sw("E", "SW", "NW") } },
        new() { Id = "cross", Name = "Пересечение", Key = "4", Hint = "глухое пересечение путей",
            Variants = new[] { Ed(P("W", "E"), P("SW", "NE")), Ed(P("W", "E"), P("NW", "SE")), Ed(P("SW", "NE"), P("NW", "SE")) } },
        new() { Id = "buffer", Name = "Тупик", Key = "5", Hint = "упор в конце тупикового пути",
            Variants = new[] { End("W"), End("E") } },
        new() { Id = "exit", Name = "Выход", Key = "6", Hint = "путь уходит за край схемы: перегон, тоннель, соседняя станция",
            Variants = new[] { End("E"), End("W") } },
        new() { Id = "crossing", Name = "Переезд", Key = "7", Hint = "путь с автомобильным переездом",
            Variants = new[] { Ed(P("W", "E")) } },
        // [ДОБАВЛЕНО 12.11.2026] для будущих сортировочных горок: путь с вагонным замедлителем (только вид и данные)
        new() { Id = "brake", Name = "Тормозная позиция", Key = "K", Hint = "путь с вагонным замедлителем сортировочной горки (ТП1, ТП2, ТП3)",
            Variants = new[] { Ed(P("W", "E")), Ed(P("SW", "NE")), Ed(P("NW", "SE")) } },
    };

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Тормозные позиции горки (рис. 2.1 методички «Проектирование сортировочных горок»).</summary>
    public static readonly (int Pos, string Name)[] BrakePositions =
    {
        (1, "ТП1 — первая (спускная часть)"), (2, "ТП2 — вторая (пучковая)"), (3, "ТП3 — третья (парковая)"),
    };

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Вагонные замедлители (табл. 3.1 той же методички): назначение, погашаемая энерг. высота
    /// (м эн. в.), допустимая скорость входа (м/с), длина изолированного участка (м).</summary>
    public static readonly (string Type, string Use, double H, double V, double L)[] Retarders =
    {
        ("ВЗПГ-3", "горочный", 1.00, 8.0, 11.50), ("ВЗПГ-5", "горочный", 1.30, 8.0, 13.48),
        ("ВЗП-3", "горочный", 0.80, 8.5, 11.50), ("ВЗП-5", "горочный", 1.40, 8.5, 13.48),
        ("КЗ-3", "горочный", 1.00, 8.5, 11.50), ("КЗ-5", "горочный", 1.40, 8.5, 13.48),
        ("НК-114", "горочный", 1.40, 8.0, 11.50), ("ЗВУ-5", "горочный, парковый", 1.40, 8.5, 13.49),
        ("КЗПУ-3", "горочный, парковый", 0.80, 8.5, 11.50), ("КЗПУ-5", "горочный, парковый", 1.30, 8.5, 13.48),
        ("РЗ-5", "парковый", 1.00, 6.5, 13.48), ("РНЗ-2М", "парковый", 0.45, 6.0, 6.25), ("ПНЗ-1", "парковый", 0.26, 6.0, 6.25),
    };

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Клетки, где подписать тормозные позиции: замедлители подряд (соседние по пути)
    /// с одной подписью — одна группа, подпись у средней клетки.</summary>
    public static List<string> BrakeLabelTiles(Station st)
    {
        var res = new List<string>();
        var done = new HashSet<string>();
        foreach (var (k, t) in st.Tiles)
        {
            if (t.T != "brake" || t.Brk == null || !done.Add(k)) continue;
            var group = new List<string> { k };
            var queue = new Queue<string>(group);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var d in EndsOf(st.Tiles[cur]))
                {
                    var nb = NeighborOf(st, cur, d);
                    if (nb == null || nb.Tile.T != "brake" || nb.Tile.Brk?.Label != t.Brk.Label || !done.Add(nb.Key)) continue;
                    group.Add(nb.Key);
                    queue.Enqueue(nb.Key);
                }
            }
            var ordered = group.Select(q => (K: q, P: Parse(q))).OrderBy(q => q.P.X).ThenBy(q => q.P.Y).ToList();
            res.Add(ordered[(ordered.Count - 1) / 2].K);
        }
        return res;
    }

    /// <summary>[ДОБАВЛЕНО 12.11.2026] У кубиков «Тормозная позиция» — данные позиции и id (без номера и повторы — новые,
    /// продолжая наибольший); у остальных кубиков данных позиции нет. fixRepeats = false (при правке) — повторы, введённые
    /// руками, не трогаем (о них предупреждает панель свойств), номер получают только новые.</summary>
    public static void AssignBrakes(Station st, bool fixRepeats = true)
    {
        var all = st.Tiles.OrderBy(p => p.Key, NaturalComparer.Instance).ToList();
        foreach (var (_, t) in all)
        {
            if (t.T != "brake") { t.Brk = null; continue; }
            t.Brk ??= new BrakePos();
            t.Brk.Pos = Math.Clamp(t.Brk.Pos, 1, 3);
            if (string.IsNullOrWhiteSpace(t.Brk.Name)) t.Brk.Name = null;
            if (string.IsNullOrWhiteSpace(t.Brk.Type)) t.Brk.Type = null;
        }
        var seen = new HashSet<int>();
        int next = all.Where(p => p.Value.Brk != null).Select(p => p.Value.Brk.Id).DefaultIfEmpty(0).Max() + 1;
        foreach (var (_, t) in all)
            if (t.Brk != null && (t.Brk.Id <= 0 || !seen.Add(t.Brk.Id) && fixRepeats)) { t.Brk.Id = next++; seen.Add(t.Brk.Id); }
    }

    public static readonly Dictionary<string, TileType> Types = TypeList.ToDictionary(t => t.Id);

    // [ИЗМЕНЕНО 05.11.2026] добавлены маршрутный, предупредительный, повторительный, заградительный, прикрытия
    // (по Инструкции по сигнализации: гл. 2.8, 2.9 и статье «Железнодорожная светофорная сигнализация»)
    public static readonly (string Id, string Name)[] SigKinds =
    {
        ("entry", "Входной"), ("exit", "Выходной"), ("route", "Маршрутный"), ("shunt", "Маневровый"), ("block", "Проходной"),
        ("warning", "Предупредительный"), ("repeater", "Повторительный"), ("barrier", "Заградительный"), ("cover", "Прикрытия"),
        // [ДОБАВЛЕНО 13.11.2026] сортировочная горка (РУ-30-80, листы 45–47, стр. 53–55; caredenis.ru, урок 14)
        ("hump", "Горочный"), ("humprep", "Повторитель горочного"),
        ("other", "Чужой (соседа)"),
    };

    // [ДОБАВЛЕНО 17.11.2026] знаки у пути и марки крестовин (правила РК — ЗАДАЧИ_расхождения_РК.md, раздел 3)
    public static readonly (string Id, string Name, string Short, string Rule)[] SignTypes =
    {
        ("limit", "Предельный столбик", "", "ПТЭ РК п. 114, 116: у стрелок и мест соединения путей, посередине междупутья, где оси путей сходятся на 4100 мм"),
        ("station", "Граница станции", "ГС", "ПТЭ РК п. 133: на электрифицированных участках — перед воздушным промежутком со стороны перегона; справа по ходу (п. 115)"),
        ("block", "Граница блок-участка", "ГБ", "Инструкция по сигнализации РК п. 81, ПТЭ РК п. 135: при АЛС как самостоятельном средстве — на границах блок-участков"),
    };
    public static string SignTypeName(string id) => SignTypes.Where(s => s.Id == id).Select(s => s.Name).FirstOrDefault();
    public static readonly string[] FrogMarks = { "1/9", "1/11", "1/18", "1/22" };

    /// <summary>Светофор открывается поездным маршрутом (входной, выходной, маршрутный).</summary>
    public static bool IsTrainStart(string kind) => kind is "entry" or "exit" or "route";
    /// <summary>Светофор открывается маневровым маршрутом (маневровый; выходной и маршрутный — лунно-белым).</summary>
    public static bool IsShuntStart(string kind) => kind is "shunt" or "exit" or "route" or "hump";   // [13.11.2026] горочный — маневры на пути сортировочного парка

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
        // [ДОБАВЛЕНО 13.11.2026] РУ-30-80 листы 45–47 (стр. 53–55), caredenis.ru урок 14
        "hump" => "Горочный (Г, Г1, Г2… — перед горбом горки на пути надвига): огни лунно-белый, зелёный, красный, жёлтый и указатель «Н». " +
                  "Красный — стоп; роспуск: жёлтый — медленно, жёлтый и зелёный — нормально, зелёный — быстро; красный и «Н» — осаживание назад; " +
                  "лунно-белый — маневры на пути сортировочного парка. Показание задаёт оператор горки (в «Испытании» — щелчок, меню); " +
                  "с маршрутом стрелок горки не связан (урок 14: «отсутствие замыкания стрелок … и увязки их с показаниями горочного светофора»).",
        "humprep" => "Повторитель горочного (ПГ1… — на пути надвига перед Г, РУ-30-80 лист 47, прим. 2): показывает то же, что горочный впереди. " +
                     "На пути парка приёма (участок «путь») — синий вместо красного (лист 46, прим. 2).",
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

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Симметричная стрелка: "switch" с вариантами 4–7.</summary>
    public static bool IsSymSwitch(Tile t) => t != null && t.T == "switch" && t.V >= 4;

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Где номер стрелки — доли клетки (0..1): у обычной — над / под серединой
    /// (со стороны, куда не уходит отклонение), у симметричной — над остряком (пути расходятся вверх и вниз).</summary>
    public static (float X, float Y) SwitchLabelAt(Tile t)
    {
        var v = VariantOf(t);
        if (IsSymSwitch(t)) return (v.Trunk == "W" ? 0.22f : 0.78f, 0.16f);
        return (0.5f, v.Minus.Contains('N') ? 0.9f : 0.1f);
    }

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Кубик, который ставит кнопка палитры type с её вариантом v (симм. стрелка → "switch", 4 + v).</summary>
    public static Tile NewTile(string type, int v)
    {
        var T = Types[type];
        return new Tile { T = T.Base ?? type, V = T.VBase + v };
    }

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Кнопка палитры, которой поставлен кубик, и её вариант (обратное к NewTile).</summary>
    public static (TileType Type, int V) PaletteTypeOf(Tile t)
    {
        foreach (var T in TypeList)
            if (T.Base == t.T && t.V >= T.VBase && t.V < T.VBase + T.Variants.Length) return (T, t.V - T.VBase);
        var own = Types[t.T];
        return (own, Math.Clamp(t.V, 0, PaletteCount(own) - 1));
    }

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Сколько вариантов у кнопки палитры (у обычной стрелки — 4 первых, остальные — симметричные).</summary>
    public static int PaletteCount(TileType T) => T.Id == "switch" ? 4 : T.Variants.Length;

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
    public static Station NewStation() => new() { AutoNames = true };

    /// <summary>[ДОБАВЛЕНО 10.11.2026] Буква направления светофора: Ч (чётное) или Н (нечётное).
    /// [ИЗМЕНЕНО 11.11.2026] по одной настройке станции «Чётные поезда идут → / ←» (Station.Even, Numbering.cs) —
    /// той же, по которой нумеруются пути и стрелки.</summary>
    public static string DirLetter(Station st, string dir) => Numbering.Letter(st, dir);

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
        if (g == "switch") return "plain";    // «…СП» без стрелки — просто участок
        if (g != "plain") return g;
        // [ДОБАВЛЕНО 18.11.2026] тип «авто», а название ничего не говорит («3», «уч1»): узнаём по схеме —
        // выход за край схемы → перегон; на участке выходной светофор → путь приёма-отправления; упор → тупик.
        // Раньше такой участок считался просто «участком», поездной маршрут проходил его насквозь мимо выходных
        // и заканчивался не там (или не находился вовсе).
        bool exitTile = false, exitSig = false, buffer = false;
        foreach (var t in st.Tiles.Values)
        {
            if (t.Sec != name) continue;
            if (t.T == "exit") exitTile = true;
            if (t.T == "buffer") buffer = true;
            if (t.Sigs != null) foreach (var sg in t.Sigs) if (sg.Kind == "exit") exitSig = true;
        }
        if (exitTile) return "line";
        if (exitSig) return "path";
        if (buffer) return "dead";
        return "plain";
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

    // ---- [ДОБАВЛЕНО 12.11.2026] перекрёстный съезд ----
    // Четыре стрелки квадратом 2×2, минусы которых идут друг к другу крест-накрест, — один перекрёстный съезд из двух
    // диагоналей: (x,y)↘(x+1,y+1) и (x+1,y)↙(x,y+1). Каждая диагональ — спаренная пара (съезд «16/20»), а в минусе может
    // быть только одна из двух: диагонали пересекаются в середине (глухое пересечение). В РУ-30-80 (содержание, стр. 2–5)
    // отдельного листа нет — это не сигнализация, а взаимозависимость стрелок.

    /// <summary>Перекрёстные съезды схемы: A и B — клетки двух диагоналей (по две стрелки).</summary>
    public static List<(string[] A, string[] B)> Scissors(Station st)
    {
        var res = new List<(string[], string[])>();
        bool M(string k, string minus) => st.Tiles.TryGetValue(k, out var t) && t.T == "switch" && !IsSymSwitch(t) && VariantOf(t).Minus == minus;
        foreach (var k in st.Tiles.Keys)
        {
            if (!M(k, "SE")) continue;
            var (x, y) = Parse(k);
            string k11 = Key(x + 1, y + 1), k10 = Key(x + 1, y), k01 = Key(x, y + 1);
            if (M(k11, "NW") && M(k10, "SW") && M(k01, "NE")) res.Add((new[] { k, k11 }, new[] { k10, k01 }));
        }
        return res;
    }

    /// <summary>Имя диагонали: номера двух её стрелок через «/» по возрастанию (16/20); нет номера — по клетке.</summary>
    static string ScissorsName(Station st, string[] keys)
    {
        var names = keys.Select(k => st.Tiles[k].Name?.Trim()).ToList();
        if (names.Any(string.IsNullOrEmpty)) return "съезд " + keys[0];
        return string.Join("/", names.OrderBy(n => n, NaturalComparer.Instance));
    }

    /// <summary>Спарить стрелки каждой диагонали перекрёстного съезда (поле «Съезд» = «16/20»). Вызывается при открытии
    /// файла и после каждой правки — переименовали стрелку, имя съезда поменяется само. true — что-то поменялось.</summary>
    public static bool AssignScissors(Station st)
    {
        bool changed = false;
        foreach (var (a, b) in Scissors(st))
            foreach (var keys in new[] { a, b })
            {
                var name = ScissorsName(st, keys);
                foreach (var k in keys)
                    if (st.Tiles[k].Group != name) { st.Tiles[k].Group = name; changed = true; }
            }
        return changed;
    }

    /// <summary>Группа стрелки → группа второй диагонали того же перекрёстного съезда (в обе стороны).</summary>
    public static Dictionary<string, string> ScissorsRivals(Station st)
    {
        var res = new Dictionary<string, string>();
        foreach (var (a, b) in Scissors(st))
        {
            string ga = SwitchGroup(a[0], st.Tiles[a[0]]), gb = SwitchGroup(b[0], st.Tiles[b[0]]);
            if (ga == gb) continue;
            res[ga] = gb;
            res[gb] = ga;
        }
        return res;
    }

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

    // «…СП» — стрелочный участок, не путь. [ИЗМЕНЕНО 10.11.2026] «П» в конце необязательна: участок «3» — путь 3
    static readonly Regex ReTrackNo = new(@"^((?:[IVX]+)|\d+)([А-ЯA-Z-[СП]]?)П?$", RegexOptions.Compiled);

    /// <summary>Номер пути из названия участка: «3П» и «3» → «3», «IIП» и «II» → «II», «2АП» → «2А»; не путь — null.</summary>
    public static string TrackNo(string sec)
    {
        var m = ReTrackNo.Match((sec ?? "").Trim().ToUpperInvariant());
        return m.Success ? m.Groups[1].Value + m.Groups[2].Value : null;
    }

    // [10.11.2026] имя нового светофора — в Model/Naming.cs (Naming.ForNew)

    /// <summary>[ДОБАВЛЕНО 11.11.2026] Где подписать пути разрывом «→ 3П ←» (РУ-30-80, стр. 89–90): для каждого участка типа «путь» —
    /// самый длинный горизонтальный прямой кусок, не занятый огнями светофоров (при равной длине — ближе к середине участка).
    /// Клетки X0..X1 в ряду Y; поместится ли надпись — решает тот, кто рисует. Общее для редактора и «Станции по схеме».
    /// [ИЗМЕНЕНО 11.11.2026] мачта светофора стоит на торце своей клетки, а огни уходят в соседнюю клетку по ходу
    /// («Ч» — влево, «Н» — вправо): занятой считаем соседнюю клетку, а клетку со светофором — свободной.
    /// Так у короткого пути «Ч5 ⊣ 5П ⊢ Н5» (3 клетки) надпись встаёт между мачтами, а не под огнями.</summary>
    public static List<(string Sec, int Y, int X0, int X1)> TrackGapRuns(Station st)
    {
        var res = new List<(string, int, int, int)>();
        if (st.TrackGaps == false) return res;
        var lamps = new HashSet<(int X, int Y)>();
        foreach (var (k, t) in st.Tiles)
        {
            if (!t.HasSigs) continue;
            var (x, y) = Parse(k);
            foreach (var s in t.Sigs) lamps.Add((s.Dir == "R" ? x + 1 : x - 1, y));
        }
        foreach (var (sec, keys) in TilesBySection(st))
        {
            if (SecType(st, sec) != "path") continue;
            var cells = keys.Where(k => st.Tiles[k].T == "straight" && st.Tiles[k].V == 0).Select(Parse)
                .Where(c => !lamps.Contains((c.X, c.Y))).ToList();
            if (cells.Count == 0) continue;
            double mid = keys.Select(k => Parse(k).X).Average();
            int bY = 0, b0 = 0, b1 = -1;
            foreach (var row in cells.GroupBy(c => c.Y))
            {
                var xs = row.Select(c => c.X).OrderBy(v => v).ToList();
                int s0 = xs[0];
                for (int i = 1; i <= xs.Count; i++)
                {
                    if (i < xs.Count && xs[i] == xs[i - 1] + 1) continue;
                    int e0 = xs[i - 1], len = e0 - s0, bLen = b1 - b0;
                    if (len > bLen || len == bLen && Math.Abs((s0 + e0) / 2.0 - mid) < Math.Abs((b0 + b1) / 2.0 - mid))
                        (bY, b0, b1) = (row.Key, s0, e0);
                    if (i < xs.Count) s0 = xs[i];
                }
            }
            if (b1 >= b0) res.Add((sec, bY, b0, b1));
        }
        return res;
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
