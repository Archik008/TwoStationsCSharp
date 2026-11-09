using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  ГЕОМЕТРИЯ СХЕМЫ — "что где нарисовано"
//  Схема (АКЖТ, путевой пост) заполняет эти списки, а общая панель
//  RailSchemePanel рисует их одинаково для всех станций.
//  Координаты — "проектные" (DesignW × DesignH), масштабируются под окно.
// =====================================================================

/// <summary>Кусок линии пути.</summary>
public class TrackPiece
{
    public string Id = "";             // уникален в пределах схемы, на него ссылается RailRoute.Pieces
    public string SectionId = "";      // к какому участку относится (для занятости)
    public PointF[] Pts = Array.Empty<PointF>();
    public string SwitchId;            // если кусок — ветка стрелки
    public bool StraightBranch;        // ветка "плюс" (прямо) или "минус" (на отклонение)
    public bool PointAtStart;          // остряк стрелки у первой точки (иначе у последней)
}

/// <summary>Где рисовать светофор.</summary>
public class SignalGeom
{
    public string SignalId = "";
    public PointF Foot;                // точка изолирующего стыка на пути
    public bool Above;                 // над путём или под путём
    public bool LampsToLeft;           // лампы уходят влево от мачты
    public string Suffix = "";         // приписка к подписи, например " (АКЖТ)"
}

/// <summary>Подпись на схеме.</summary>
public class SchemeText
{
    public string Text = "";
    public PointF Pos;
    public float Size = 9.5f;
    public bool Bold;
    public bool White;                 // белый текст (иначе тёмный)
    public StringAlignment Align = StringAlignment.Near;
}

/// <summary>Портал тоннеля (вход/выход на продолжение макета).</summary>
public class TunnelGeom
{
    public PointF Portal;              // точка на пути, где начинается тоннель
    public bool OpensRight;            // тоннель уходит вправо (иначе влево)
    public float Length = 150;         // сколько тоннеля видно на схеме
    public string Label = "";
}

public class CrossingGeom
{
    public LevelCrossing Crossing;
    public float X;
    public float Y1, Y2;               // от какого до какого пути
}

public class DirectionGeom
{
    public DirectionLine Line;
    public PointF Pos;                 // центр подписи
    public float ArrowX1, ArrowX2, ArrowY;
    public string RightText = "→", LeftText = "←";
}

public class SchemeGeometry
{
    public float DesignW = 2400, DesignH = 640;

    public readonly List<TrackPiece> Pieces = new();
    public readonly List<PointF[]> Decor = new();          // просто линии (без участка)
    // [ДОБАВЛЕНО 13.10.2026] продолжение перегона за светофором соседней станции (до края схемы):
    // белая линия, жёлтая — когда участок section в поездном маршруте на перегон (TrackSection.LineRoute)
    public readonly List<(PointF[] pts, string section)> LineDecor = new();
    public readonly List<PointF[]> ProjectDecor = new();   // пунктир: запроектировано, на макете нет
    public readonly List<PointF> Joints = new();
    public readonly List<PointF> Buffers = new();          // упоры тупиков
    public readonly List<SignalGeom> Signals = new();
    public readonly Dictionary<string, (PointF pos, string switchId)> SwitchLabels = new();
    public readonly Dictionary<string, PointF> SectionLabels = new();   // SectionId → точка
    public readonly List<SchemeText> Texts = new();
    public readonly List<PointF> Boundaries = new();       // таблички "граница станции"
    public readonly List<TunnelGeom> Tunnels = new();
    public readonly List<CrossingGeom> Crossings = new();
    public readonly List<DirectionGeom> Directions = new();

    // ---------- помощники для описания схемы ----------
    public static PointF[] P(params (float x, float y)[] pts) => pts.Select(p => new PointF(p.x, p.y)).ToArray();

    public TrackPiece Piece(string id, string section, params (float x, float y)[] pts)
    {
        var p = new TrackPiece { Id = id, SectionId = section, Pts = P(pts) };
        Pieces.Add(p);
        return p;
    }

    public TrackPiece Branch(string id, string section, string sw, bool straight, bool pointAtStart, params (float x, float y)[] pts)
    {
        var p = new TrackPiece { Id = id, SectionId = section, Pts = P(pts), SwitchId = sw, StraightBranch = straight, PointAtStart = pointAtStart };
        Pieces.Add(p);
        return p;
    }

    public void Sig(RailSignal s, float x, float y, bool above, string suffix = "") =>
        Signals.Add(new SignalGeom { SignalId = s.Id, Foot = new PointF(x, y), Above = above, LampsToLeft = above, Suffix = suffix });

    public void SwLabel(string label, RailSwitch sw, float x, float y) =>
        SwitchLabels[label] = (new PointF(x, y), sw.Id);

    public void Text(string text, float x, float y, float size = 9.5f, bool bold = false, bool white = false,
                     StringAlignment align = StringAlignment.Near) =>
        Texts.Add(new SchemeText { Text = text, Pos = new PointF(x, y), Size = size, Bold = bold, White = white, Align = align });

    public void Joint(float x, float y) => Joints.Add(new PointF(x, y));

    /// <summary>Подписи участков по умолчанию — под серединой первого прямого куска
    /// (кроме тех, что уже заданы вручную).</summary>
    public void AutoSectionLabels(float dy = 16)
    {
        foreach (var p in Pieces)
        {
            if (SectionLabels.ContainsKey(p.SectionId)) continue;
            if (p.SwitchId != null && !p.StraightBranch) continue;
            var a = p.Pts[0];
            var b = p.Pts[^1];
            SectionLabels[p.SectionId] = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2 + dy);
        }
    }
}

public static class RoutePainter
{
    /// <summary>
    /// Находит куски схемы, по которым реально идёт маршрут: идём от мачты светофора начала
    /// по кускам участков маршрута в сторону движения, на стрелках выбираем ветку по нужному
    /// положению стрелки. Так не нужно вручную перечислять куски для каждого из ~80 маршрутов.
    /// </summary>
    public static List<string> Compute(SchemeGeometry geo, RailRoute r)
    {
        var result = new List<string>();
        var sg = geo.Signals.FirstOrDefault(g => g.SignalId == r.Start.Id);
        if (sg == null) return result;

        var secIds = r.Sections.Select(s => s.Id).ToHashSet();
        bool right = r.Start.Dir == Dir.Right;
        var need = r.Switches.ToDictionary(x => x.sw.Id, x => x.plus);

        static bool Near(PointF a, PointF b) => Math.Abs(a.X - b.X) < 0.6f && Math.Abs(a.Y - b.Y) < 0.6f;

        PointF cur = sg.Foot;
        var used = new HashSet<TrackPiece>();
        for (int guard = 0; guard < 200; guard++)
        {
            var cands = new List<(TrackPiece p, PointF far)>();
            foreach (var p in geo.Pieces)
            {
                if (used.Contains(p) || !secIds.Contains(p.SectionId)) continue;
                var a = p.Pts[0];
                var b = p.Pts[^1];
                if (Near(a, cur) && (right ? b.X > cur.X : b.X < cur.X)) cands.Add((p, b));
                else if (Near(b, cur) && (right ? a.X > cur.X : a.X < cur.X)) cands.Add((p, a));
            }
            if (cands.Count == 0) break;

            (TrackPiece p, PointF far) pick = cands[0];
            if (cands.Count > 1)
            {
                // развилка: выбираем ветку по положению стрелки в маршруте (если стрелки нет в маршруте — прямо)
                var match = cands.FirstOrDefault(c =>
                {
                    if (c.p.SwitchId == null) return false;
                    bool plus = need.TryGetValue(c.p.SwitchId, out var v) ? v : true;
                    return c.p.StraightBranch == plus;
                });
                if (match.p != null) pick = match;
            }
            used.Add(pick.p);
            result.Add(pick.p.Id);
            cur = pick.far;
        }
        return result;
    }
}

public static class RouteFinder
{
    /// <summary>Маршрут по началу и "цели" (светофор конца или путь назначения).
    /// Если таких два (поездной и маневровый) — берём нужной категории.
    /// (В программе АКЖТ этот метод лежит в AkzhtLayout.Find.)</summary>
    public static RailRoute Find(IEnumerable<RailRoute> routes, RailSignal start, object target, bool shunting)
    {
        bool Match(RailRoute r) => r.Start == start && (target is RailSignal sg ? r.End == sg : target is TrackSection ts && r.Dest == ts);
        return routes.FirstOrDefault(r => Match(r) && r.Shunting == shunting) ?? routes.FirstOrDefault(Match);
    }
}
