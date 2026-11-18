namespace DncApp;

// =====================================================================
//  СКВОЗНАЯ ЛЕНТА УЧАСТКА  [ДОБАВЛЕНО 18.11.2026]
//  Как поставить схемы станций в линию, чтобы пути соседних станций стыковались:
//   • единый масштаб: у всех схем одинаково пикселей на единицу чертежа (пути, светофоры, надписи одного размера);
//   • стык по общему участку: станции сообщают общие с соседом участки (НАУ, Б12 — перегон пост — Алгабас,
//     имена как в связи диспетчеров) и их концы на чертеже. Перегон нарисован у обеих станций — показываем его
//     один раз: левую схему режем на четверти первого общего участка, правую — там же на её чертеже,
//     и ставим встык; по высоте сдвигаем так, чтобы путь перегона лёг в одну линию;
//   • стык «край в край»: схемы одной программы в общей системе координат (АКЖТ и путевой пост — как на
//     двух мониторах) — встык, на одной высоте чертежа;
//   • соседи без общего пути — с зазором, по высоте — по середине.
//  Всё считается в единицах чертежа (масштаб 1), окно умножает на свой масштаб k.
// =====================================================================

public enum JoinKind { None, Level, Shared }

/// <summary>Стык левой схемы A с правой B: где резать (X на чертеже A и B) и насколько B ниже A (единицы чертежа).</summary>
public record Join(JoinKind Kind, float SeamA, float SeamB, float Dy, string Name)
{
    public static readonly Join None = new(JoinKind.None, 0, 0, 0, "");
}

/// <summary>Место схемы в ленте: какой кусок чертежа (X0..X1) и на какой высоте его верх (единицы чертежа).</summary>
public class Placement
{
    public SchemeTile Tile;
    public SchemeInfo Info;
    public bool Known => Info is { Dw: > 0, Dh: > 0 };
    public float X0, X1, Top;
    /// <summary>Стыкуется с соседом слева (без зазора).</summary>
    public bool JoinedLeft;
    public JoinKind JoinLeftKind;
}

public static class Ribbon
{
    /// <summary>Где резать общий участок: доля его длины от левого конца (середину занимает подпись участка).</summary>
    public const float SeamAt = 0.25f;
    /// <summary>Зазор между несостыкованными схемами, пикселей.</summary>
    public const int Gap = 24;
    /// <summary>Ширина плитки станции, о которой ещё ничего не известно (нет связи / нет ответа), пикселей.</summary>
    public const int UnknownW = 360;

    public static Join Find(string stA, SchemeInfo a, string stB, SchemeInfo b)
    {
        if (a is not { Dw: > 0 } || b is not { Dw: > 0 }) return Join.None;
        // одна программа, общая система координат: A левее B
        if (stA == stB && a.Lvl.Length > 0 && a.Lvl == b.Lvl && a.LvlPos < b.LvlPos)
            return new Join(JoinKind.Level, a.Dw, 0, 0, "");
        // общий участок: у A он у правого края, у B — у левого; берём самый левый на чертеже A
        Join best = null;
        float bestX = float.MaxValue;
        foreach (var pa in a.Shared)
        {
            if ((pa.X1 + pa.X2) / 2 < a.Dw / 2) continue;
            foreach (var pb in b.Shared)
            {
                if (pb.N != pa.N || (pb.X1 + pb.X2) / 2 > b.Dw / 2) continue;
                float aMin = Math.Min(pa.X1, pa.X2), aLen = Math.Abs(pa.X2 - pa.X1);
                float bMin = Math.Min(pb.X1, pb.X2), bLen = Math.Abs(pb.X2 - pb.X1);
                if (aLen < 1 || bLen < 1 || aMin >= bestX) continue;
                bestX = aMin;
                best = new Join(JoinKind.Shared, aMin + SeamAt * aLen, bMin + SeamAt * bLen,
                                (pa.Y1 + pa.Y2) / 2 - (pb.Y1 + pb.Y2) / 2, pa.N);
            }
        }
        return best ?? Join.None;
    }

    public static Join Find(SchemeTile a, SchemeTile b) => Find(a.StationId, a.Info, b.StationId, b.Info);

    /// <summary>Разложить плитки по порядку. W1 — сумма ширин известных схем (единицы чертежа), H1 — высота ленты
    /// (единицы чертежа), gaps — сколько зазоров, unknown — сколько плиток без данных.</summary>
    public static (List<Placement> list, float W1, float H1, int gaps, int unknown) Compute(IList<SchemeTile> tiles)
    {
        var list = tiles.Select(t => new Placement { Tile = t, Info = t.Info }).ToList();
        foreach (var p in list.Where(p => p.Known)) { p.X0 = 0; p.X1 = p.Info.Dw; }
        Placement prev = null;
        foreach (var p in list)
        {
            if (!p.Known) { continue; }
            if (prev == null) { p.Top = 0; prev = p; continue; }
            var j = list.IndexOf(prev) == list.IndexOf(p) - 1
                ? Find(prev.Tile.StationId, prev.Info, p.Tile.StationId, p.Info) : Join.None;
            // резать можно, только если от схем что-то остаётся
            if (j.Kind != JoinKind.None && j.SeamA > prev.X0 + 20 && j.SeamB < p.Info.Dw - 20)
            {
                prev.X1 = j.SeamA;
                p.X0 = j.SeamB;
                p.Top = prev.Top + j.Dy;
                p.JoinedLeft = true;
                p.JoinLeftKind = j.Kind;
            }
            else p.Top = prev.Top + prev.Info.Dh / 2 - p.Info.Dh / 2;   // без стыка — по середине
            prev = p;
        }
        var known = list.Where(p => p.Known).ToList();
        float min = known.Count > 0 ? known.Min(p => p.Top) : 0;
        foreach (var p in known) p.Top -= min;
        float h1 = known.Count > 0 ? known.Max(p => p.Top + p.Info.Dh) : 0;
        float w1 = known.Sum(p => p.X1 - p.X0);
        int gaps = list.Skip(1).Count(p => !p.JoinedLeft);
        return (list, w1, h1, gaps, list.Count - known.Count);
    }

    /// <summary>Порядок по стыкам: цепочки «левая → правая» (АКЖТ → пост → Алгабас), несвязанные — после.</summary>
    public static List<SchemeTile> AutoOrder(IList<SchemeTile> tiles)
    {
        bool Edge(SchemeTile a, SchemeTile b) => a != b && Find(a, b).Kind != JoinKind.None;
        var order = new List<SchemeTile>();
        var used = new HashSet<SchemeTile>();
        var starts = tiles.Where(t => !tiles.Any(o => Edge(o, t))).ToList();
        foreach (var s in starts.Concat(tiles))
        {
            var cur = s;
            while (cur != null && used.Add(cur))
            {
                order.Add(cur);
                var c = cur;
                cur = tiles.FirstOrDefault(n => !used.Contains(n) && Edge(c, n));
            }
        }
        return order;
    }
}
