using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StationEditor.Model;

// =====================================================================
//  ЭКСПОРТ В C# (ЧЕРНОВИК) — заготовка файла схемы в стиле Algabas/AlgabasLayout.cs:
//  BuildSections, BuildSwitches, BuildSignals, BuildRoutes, BuildGeometry.
//  Топологию Next() и направление перегона (DirectionLine) пока надо дописать руками.
// =====================================================================
public static class CsExport
{
    static readonly Dictionary<char, string> Translit = new()
    {
        ['А'] = "A", ['Б'] = "B", ['В'] = "V", ['Г'] = "G", ['Д'] = "D", ['Е'] = "E", ['Ё'] = "E", ['Ж'] = "ZH", ['З'] = "Z",
        ['И'] = "I", ['Й'] = "J", ['К'] = "K", ['Л'] = "L", ['М'] = "M", ['Н'] = "N", ['О'] = "O", ['П'] = "P", ['Р'] = "R",
        ['С'] = "S", ['Т'] = "T", ['У'] = "U", ['Ф'] = "F", ['Х'] = "H", ['Ц'] = "C", ['Ч'] = "CH", ['Ш'] = "SH", ['Щ'] = "SCH",
        ['Ъ'] = "", ['Ы'] = "Y", ['Ь'] = "", ['Э'] = "E", ['Ю'] = "YU", ['Я'] = "YA", ['Ә'] = "A", ['Ғ'] = "G", ['Қ'] = "Q",
        ['Ң'] = "NG", ['Ө'] = "O", ['Ұ'] = "U", ['Ү'] = "U", ['Һ'] = "H", ['І'] = "I",
    };

    static string Tr(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? "").ToUpperInvariant())
            sb.Append(Translit.TryGetValue(ch, out var r) ? r : ch.ToString());
        return sb.ToString();
    }

    static readonly Regex NotIdent = new("[^A-Z0-9]+", RegexOptions.Compiled);

    static string CsStr(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static string Num(double v)
    {
        var r = Math.Round(v * 10, MidpointRounding.AwayFromZero) / 10;
        if (r == 0) r = 0;   // без «-0»
        return r.ToString("0.#", CultureInfo.InvariantCulture);
    }

    public static string Export(Station st, List<Route> routes)
    {
        const float C = Core.Cell;
        var used = new HashSet<string>();
        string Ident(string prefix, string name)
        {
            string s = prefix + NotIdent.Replace(Tr(name), "_").Trim('_');
            if (s == prefix) s = prefix + "X";
            if (char.IsDigit(s[0])) s = "SIG_" + s;      // проходные «1», «2» — имя C# не может начинаться с цифры
            string r = s;
            int i = 2;
            while (used.Contains(r)) r = s + "_" + i++;
            used.Add(r);
            return r;
        }

        // ---- участки ----
        var secId = new Dictionary<string, string>();
        foreach (var n in Core.SectionNames(st)) secId[n] = Ident("S_", n);
        string SecIdOrNull(string n) => n != null && secId.TryGetValue(n, out var v) ? v : null;

        // ---- стрелки (спаренные — одна RailSwitch) ----
        var swGroups = new Dictionary<string, (string Label, List<string> Keys)>();
        foreach (var (k, t) in st.Tiles)
        {
            if (t.T != "switch") continue;
            var g = Core.SwitchGroup(k, t);
            if (!swGroups.TryGetValue(g, out var x))
                swGroups[g] = x = (!string.IsNullOrEmpty(t.Group) ? t.Group : (!string.IsNullOrEmpty(t.Name) ? t.Name : k), new List<string>());
            x.Keys.Add(k);
        }
        var swId = new Dictionary<string, string>();
        foreach (var (g, x) in swGroups) swId[g] = Ident("SW_", x.Label);

        // ---- светофоры ----
        // [ИЗМЕНЕНО 05.11.2026] предупредительных, повторительных, заградительных и прикрытия в программах станций
        // пока нет (SignalKind: Controlled / Automatic / Neighbor) — в черновик они идут только комментарием
        var allSigs = Core.AllSignals(st);
        var sigs = allSigs.Where(s => s.Sig.Kind is not ("warning" or "repeater" or "barrier" or "cover")).ToList();
        var skipped = allSigs.Except(sigs).ToList();
        var sigId = new Dictionary<string, string>();
        foreach (var s in sigs) sigId[s.Key + "#" + s.Idx] = Ident("", string.IsNullOrEmpty(s.Sig.Name) ? "SIG" : s.Sig.Name);
        string Sid(SignalEntry s) => sigId[s.Key + "#" + s.Idx];

        // смещение: левый верхний кубик — в (1, 1) клетку
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var k in st.Tiles.Keys.Concat(st.Texts.Keys))
        {
            var (x, y) = Core.Parse(k);
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        if (minX == int.MaxValue) { minX = minY = maxX = maxY = 0; }
        double X(int gx) => (gx - minX + 1) * C;
        double Y(int gy) => (gy - minY + 1) * C;
        (double, double) Pt(int x, int y, string d) => (X(x) + Core.Dirs[d].Px * C, Y(y) + Core.Dirs[d].Py * C);
        (double, double) Ctr(int x, int y) => (X(x) + C / 2, Y(y) + C / 2);
        string Tup((double X, double Y) p) => $"({Num(p.X)}, {Num(p.Y)})";

        var L = new List<string>();
        void W(string s) => L.Add(s);
        string date = DateTime.Now.ToString("dd.MM.yyyy, HH:mm:ss", CultureInfo.InvariantCulture);

        W("// =====================================================================");
        W($"//  ЧЕРНОВИК СХЕМЫ «{st.Name}» — сгенерирован редактором станций {date}");
        W("//  Стиль — как Algabas/AlgabasLayout.cs (там же вспомогательные S, Sw, Ctl, Sig, Route, P, M).");
        W("//  Проверьте названия, лампы и маршруты. Next() (топология) и DirectionLine — пока вручную.");
        if (skipped.Count > 0)
            W("//  Не перенесены (в программе станции таких светофоров пока нет): " +
              string.Join(", ", skipped.Select(s => $"{Core.SigKindName(s.Sig.Kind)} {s.Sig.Name}")));
        if (st.Objects.Count > 0)
            W("//  Объекты схемы (амперметр, вокзал, часы) в программу станции не переносятся.");
        W("// =====================================================================");
        W("");
        if (secId.Count > 0) W($"public TrackSection {string.Join(", ", secId.Values)};");
        if (swId.Count > 0) W($"public RailSwitch {string.Join(", ", swId.Values)};");
        if (sigs.Count > 0) W($"public RailSignal {string.Join(", ", sigs.Select(Sid))};");
        W("");

        // ---------------- BuildSections ----------------
        var secTitle = new Dictionary<string, string>
        { ["path"] = "Путь", ["plain"] = "Участок", ["line"] = "Блок-участок перегона", ["dead"] = "Тупик", ["switch"] = "Стрелочный участок" };
        W("void BuildSections()");
        W("{");
        foreach (var (n, idn) in secId)
        {
            var title = (secTitle.TryGetValue(Core.SecType(st, n), out var tt) ? tt : "Участок") + " " + n;
            W($"    {idn} = S({CsStr(n)}, {CsStr(title)});");
        }
        W("}");
        W("");

        // ---------------- BuildSwitches ----------------
        W("void BuildSwitches()");
        W("{");
        foreach (var (g, x) in swGroups)
        {
            var secs = x.Keys.Select(k => st.Tiles[k].Sec).Where(s => !string.IsNullOrEmpty(s)).Distinct().Select(n => secId[n]).ToList();
            var title = x.Keys.Count > 1 ? $"Съезд {x.Label}" : $"Стрелка {x.Label}";
            W($"    {swId[g]} = Sw({CsStr(x.Label)}, {CsStr(title)}{(secs.Count > 0 ? ", " + string.Join(", ", secs) : "")});");
        }
        W("}");
        W("");

        // ---------------- BuildSignals ----------------
        var lamps = new Dictionary<string, string> { ["entry"] = "L_ENTRY", ["exit"] = "L_EXIT", ["shunt"] = "L_SHUNT_BLUE", ["block"] = "L_BLOCK" };
        W("void BuildSignals()");
        W("{");
        foreach (var s in sigs)
        {
            var dir = s.Sig.Dir == "R" ? "Dir.Right" : "Dir.Left";
            var name = string.IsNullOrEmpty(s.Sig.Name) ? "?" : s.Sig.Name;
            var title = $"{Core.SigKindName(s.Sig.Kind)} {name}";
            if (s.Sig.Kind == "block")
                W($"    {Sid(s)} = Sig({CsStr(name)}, {CsStr(name)}, {CsStr(title)}, SignalKind.Automatic, {dir}, L_BLOCK);");
            else if (s.Sig.Kind == "other")
                W($"    {Sid(s)} = Sig({CsStr(name)}, {CsStr(name)}, {CsStr(title)}, SignalKind.Neighbor, {dir}, new[] {{ \"yellow\", \"red\", \"yellow1\", \"white\" }});");
            else
                W($"    {Sid(s)} = Ctl({CsStr(name)}, {CsStr(title)}, {dir}, {(lamps.TryGetValue(s.Sig.Kind, out var lp) ? lp : "L_EXIT")});");
        }
        var appr = sigs.Where(s => !string.IsNullOrEmpty(s.Tile.Sec) && s.Sig.Kind != "other").ToList();
        if (appr.Count > 0)
        {
            W("");
            W("    // участки приближения (на чём стоит поезд перед светофором)");
            foreach (var s in appr) W($"    {Sid(s)}.Approach = {secId[s.Tile.Sec]};");
        }
        // [ДОБАВЛЕНО 09.11.2026] id светофоров для связи станций и светофоры «только чтение»
        W("");
        W("    // id светофоров для связи станций (поле id в схеме): " +
          string.Join(", ", sigs.Select(s => $"{Sid(s)} = {s.Sig.Id}")));
        var ro = allSigs.Where(s => s.Sig.IsReadOnly).ToList();
        if (ro.Count > 0)
        {
            W("    // только чтение — показание присылает другая станция (RailSignal.Remote). В DispatchMirror, список mirror:");
            foreach (var s in ro)
                W(sigId.ContainsKey(s.Key + "#" + s.Idx)
                    ? $"    //   (\"{s.Sig.Id}\", {Sid(s)}),"
                    : $"    //   (\"{s.Sig.Id}\", ?)  — {Core.SigKindName(s.Sig.Kind)} {s.Sig.Name} (в программе станции такого вида нет)");
        }
        W("}");
        W("");

        // ---------------- BuildRoutes ----------------
        W("void BuildRoutes()");
        W("{");
        foreach (var shunt in new[] { false, true })
        {
            var list = routes.Where(r => r.Shunt == shunt).ToList();
            if (list.Count == 0) continue;
            W($"    // ======== {(shunt ? "МАНЕВРОВЫЕ" : "ПОЕЗДНЫЕ")} (найдены автоматически: {list.Count}) ========");
            foreach (var r in list)
            {
                var startSig = sigs.First(s => s.Ref == r.Start);
                var end = RouteFinder.RouteEndSignal(st, r);
                var secs = string.Join(", ", r.Sections.Select(n => secId[n]));
                var sws = string.Join(", ", r.Switches.Select(x => $"{(x.Plus ? "P" : "M")}({swId[x.Group]})"));
                var line = $"    Route({(shunt ? "true" : "false")}, {Sid(startSig)}, {(end != null ? Sid(end) : "null")}, {SecIdOrNull(r.Dest) ?? "null"}, {CsStr(r.Name)}, " +
                           $"new TrackSection[] {{ {secs} }}, new (RailSwitch, bool)[] {{ {sws} }}";
                if (r.Diverging) line += ", div: true";
                if (r.Exit && SecIdOrNull(r.Dest) != null) line += $", exit: {SecIdOrNull(r.Dest)}";
                W(line + ");");
            }
            W("");
        }
        if (routes.Count == 0) W("    // маршрутов не найдено — расставьте светофоры и типы участков");
        W("}");
        W("");

        // ---------------- BuildGeometry ----------------
        W("void BuildGeometry()");
        W("{");
        W("    var g = G;");
        W($"    g.DesignW = {Num(X(maxX) + 2 * C)};");
        W($"    g.DesignH = {Num(Y(maxY) + 2 * C)};");
        W("    string id(TrackSection s) => s.Id;");
        W("");
        int pieceN = 0;
        string Pid() => CsStr("e" + (++pieceN));
        string SecRef(Tile t) => !string.IsNullOrEmpty(t.Sec) ? $"id({secId[t.Sec]})" : "\"\"";

        // горизонтальные пути одного участка подряд — одним куском
        var flat = st.Tiles.Where(p => (p.Value.T == "straight" && p.Value.V == 0) || p.Value.T == "crossing")
            .Select(p => (K: p.Key, T: p.Value, XY: Core.Parse(p.Key)))
            .OrderBy(a => a.XY.Y).ThenBy(a => a.XY.X).ToList();
        var done = new HashSet<string>();
        for (int i = 0; i < flat.Count;)
        {
            int j = i;
            while (j + 1 < flat.Count && flat[j + 1].XY.Y == flat[i].XY.Y && flat[j + 1].XY.X == flat[j].XY.X + 1 &&
                   (flat[j + 1].T.Sec ?? "") == (flat[i].T.Sec ?? "")) j++;
            var a = flat[i];
            var b = flat[j];
            W($"    g.Piece({Pid()}, {SecRef(a.T)}, {Tup(Pt(a.XY.X, a.XY.Y, "W"))}, {Tup(Pt(b.XY.X, b.XY.Y, "E"))});");
            for (int q = i; q <= j; q++)
            {
                done.Add(flat[q].K);
                if (flat[q].T.T == "crossing") W($"    // переезд у {Tup(Ctr(flat[q].XY.X, flat[q].XY.Y))} — LevelCrossing добавить вручную");
            }
            i = j + 1;
        }
        foreach (var (k, t) in st.Tiles)
        {
            if (done.Contains(k)) continue;
            var (x, y) = Core.Parse(k);
            var c = Ctr(x, y);
            var v = Core.VariantOf(t);
            if (t.T == "switch")
            {
                var sw = swId[Core.SwitchGroup(k, t)];
                W($"    g.Piece({Pid()}, {SecRef(t)}, {Tup(Pt(x, y, v.Trunk))}, {Tup(c)});");
                W($"    g.Branch({Pid()}, {SecRef(t)}, {sw}.Id, true, true, {Tup(c)}, {Tup(Pt(x, y, v.Plus))});");
                W($"    g.Branch({Pid()}, {SecRef(t)}, {sw}.Id, false, true, {Tup(c)}, {Tup(Pt(x, y, v.Minus))});");
            }
            else if (t.T == "buffer")
            {
                W($"    g.Piece({Pid()}, {SecRef(t)}, {Tup(Pt(x, y, v.Ends[0]))}, {Tup(c)});");
                W($"    g.Buffers.Add(new PointF({Num(c.Item1)}, {Num(c.Item2)}));");
            }
            else if (t.T == "exit")
            {
                W($"    g.Piece({Pid()}, {SecRef(t)}, {Tup(Pt(x, y, v.Ends[0]))}, {Tup(Pt(x, y, Core.Opp[v.Ends[0]]))});   // выход за край схемы");
            }
            else
            {
                foreach (var e in Core.EdgesOf(t))
                {
                    var pts = Core.Opp[e.A] == e.B
                        ? new[] { Pt(x, y, e.A), Pt(x, y, e.B) }
                        : new[] { Pt(x, y, e.A), c, Pt(x, y, e.B) };
                    W($"    g.Piece({Pid()}, {SecRef(t)}, {string.Join(", ", pts.Select(p => Tup(p)))});");
                }
            }
        }
        W("");
        W("    // изолирующие стыки — там, где соседние кубики относятся к разным участкам");
        foreach (var (k, t) in st.Tiles)
        {
            var (x, y) = Core.Parse(k);
            foreach (var d in Core.EndsOf(t))
            {
                var nb = Core.NeighborOf(st, k, d);
                if (nb != null && Core.IsRight(d) && (t.Sec ?? "") != (nb.Tile.Sec ?? ""))
                {
                    var p = Pt(x, y, d);
                    W($"    g.Joint({Num(p.Item1)}, {Num(p.Item2)});");
                }
            }
        }
        W("");
        W("    // светофоры: «←» над путём, «→» под путём");
        foreach (var s in sigs)
        {
            var (x, y) = Core.Parse(s.Key);
            var want = s.Sig.Dir == "R" ? "E" : "W";
            var ends = Core.EndsOf(s.Tile).Where(d => (s.Sig.Dir == "R") == Core.IsRight(d)).ToList();
            var p = Pt(x, y, ends.Contains(want) ? want : (ends.Count > 0 ? ends[0] : want));
            W($"    g.Sig({Sid(s)}, {Num(p.Item1)}, {Num(p.Item2)}, {(s.Sig.Dir == "L" ? "true" : "false")});");
        }
        W("");
        W("    // номера стрелок");
        foreach (var (k, t) in st.Tiles)
        {
            if (t.T != "switch") continue;
            var (x, y) = Core.Parse(k);
            bool below = Core.VariantOf(t).Minus.Contains('N');
            W($"    g.SwLabel({CsStr(string.IsNullOrEmpty(t.Name) ? "?" : t.Name)}, {swId[Core.SwitchGroup(k, t)]}, {Num(X(x) + C / 2)}, {Num(Y(y) + (below ? C * 0.9 : C * 0.1))});");
        }
        if (st.Texts.Count > 0)
        {
            W("");
            W("    // надписи");
            foreach (var (k, tx) in st.Texts)
            {
                var (x, y) = Core.Parse(k);
                W($"    g.Text({CsStr(tx.Text)}, {Num(X(x) + 3)}, {Num(Y(y) + C / 2 - 8)}, {Num((tx.Size > 0 ? tx.Size : 14) * 0.7)}f, true);");
            }
        }
        W("");
        W("    g.AutoSectionLabels();");
        W("}");
        return string.Join("\r\n", L);
    }
}
