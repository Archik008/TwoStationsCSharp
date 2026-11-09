using StationEditor.Model;

namespace StationEditor.Ui;

// =====================================================================
//  РЕЖИМ ИСПЫТАНИЯ: маршруты, стрелки, занятость
// =====================================================================
public sealed partial class MainForm
{
    void SetMode(string m)
    {
        if (m == ui.Mode) return;
        CommitFocusedField();
        var T = ui.Test;
        if (m == "test")
        {
            ui.Sel.Clear(); ui.SelSig = null; ui.Ghost = null; ui.Band = null; ui.FlashSec = null; ui.Preview = null;
            ui.Moving = null; drag = null;
            T.SavedPlus = new Dictionary<string, bool?>();
            foreach (var (k, t) in st.Tiles) if (t.T == "switch") T.SavedPlus[k] = t.Plus;
            T.Occ = new HashSet<string>(); T.Set = new List<Route>(); T.Pending = null;
            ui.Mode = m;
            RecomputeTest();
            if (ui.Tab == "props") ui.Tab = "routes";
            Msg($"Испытание: найдено маршрутов {Routes.Count}");
        }
        else
        {
            // положения стрелок после испытания не сохраняем — возвращаем как было
            foreach (var (k, p) in T.SavedPlus ?? new Dictionary<string, bool?>())
                if (st.Tiles.TryGetValue(k, out var t)) t.Plus = p == false ? false : null;
            T.Set = new List<Route>(); T.Occ = new HashSet<string>(); T.Pending = null;
            ui.Mode = m;
            RecomputeTest();
        }
        bEdit.Checked = m == "edit";
        bTest.Checked = m == "test";
        testBanner.Visible = m == "test";
        RefreshPalette();
        RefreshToolOpts();
        RefreshPanels();
        Draw();
    }

    void RecomputeTest()
    {
        var T = ui.Test;
        T.RouteTiles = new HashSet<string>();
        T.OpenSigs = new HashSet<string>();
        foreach (var r in T.Set)
        {
            foreach (var k in r.Tiles) T.RouteTiles.Add(k);
            bool busy = r.Sections.Any(T.Occ.Contains) || (!r.Exit && T.Occ.Contains(r.Dest));
            if (!busy) T.OpenSigs.Add(r.Start.Key + "#" + r.Start.Idx);
        }
    }

    bool SetRoute(Route r, bool quiet = false)
    {
        var T = ui.Test;
        if (r.Tiles.Any(T.RouteTiles.Contains))
        {
            if (!quiet) Msg($"Маршрут {r.Name} не задан: пересекается с уже заданным маршрутом", true);
            return false;
        }
        var busy = r.Sections.Concat(r.Exit ? new[] { r.Dest } : Array.Empty<string>()).FirstOrDefault(T.Occ.Contains);
        if (busy != null)
        {
            if (!quiet) Msg($"Маршрут {r.Name} не задан: участок {busy} занят", true);
            return false;
        }
        foreach (var x in r.Switches)
            foreach (var k in x.Keys)
                if (st.Tiles.TryGetValue(k, out var t)) t.Plus = x.Plus ? null : false;
        T.Set.Add(r);
        RecomputeTest();
        Msg($"Задан маршрут {(r.Shunt ? "маневровый" : "поездной")}: {r.Name}");
        return true;
    }

    void CancelRoute(Route r)
    {
        ui.Test.Set.Remove(r);
        RecomputeTest();
        Msg($"Маршрут {r.Name} отменён");
    }

    void TestClick(int gx, int gy, SigRef? sg)
    {
        var T = ui.Test;
        var k = Core.Key(gx, gy);
        if (sg is SigRef s)
        {
            var set = T.Set.FirstOrDefault(r => r.Start == s);
            var name = st.Tiles[s.Key].Sigs[s.Idx].Name;
            if (set != null) CancelRoute(set);
            else if (T.Pending == s) { T.Pending = null; Msg("Набор маршрута отменён"); }
            else if (!Routes.Any(r => r.Start == s)) Msg($"От светофора {name} маршрутов нет", true);
            else { T.Pending = s; Msg($"Начало маршрута — {name}. Щёлкните по пути назначения"); }
        }
        else if (T.Pending is SigRef pend)
        {
            st.Tiles.TryGetValue(k, out var t);
            var from = t == null ? new List<Route>() : Routes.Where(r => r.Start == pend).ToList();
            var exact = from.Where(r => t != null && r.Dest == t.Sec).ToList();
            var cand = exact.Count > 0 ? exact : from.Where(r => r.Tiles.Contains(k)).ToList();
            // поездной или маневровый — по фильтру на вкладке «Маршруты» (по умолчанию сначала поездной)
            cand = ui.RouteFilter == "shunt"
                ? cand.OrderByDescending(r => r.Shunt).ToList()
                : cand.OrderBy(r => r.Shunt).ToList();
            var name = st.Tiles[pend.Key].Sigs[pend.Idx].Name;
            T.Pending = null;
            if (cand.Count == 0)
                Msg($"Маршрута от {name} до {(t != null && !string.IsNullOrEmpty(t.Sec) ? t.Sec : "этой клетки")} нет", true);
            else if (!cand.Any(r => SetRoute(r, true))) SetRoute(cand[0]);
        }
        else if (st.Tiles.TryGetValue(k, out var t) && t.T == "switch") ToggleSwitch(k);
        RefreshPanels();
        Draw();
    }

    void ToggleSwitch(string k)
    {
        var T = ui.Test;
        var t = st.Tiles[k];
        var g = Core.SwitchGroup(k, t);
        var keys = st.Tiles.Where(p => p.Value.T == "switch" && Core.SwitchGroup(p.Key, p.Value) == g).Select(p => p.Key).ToList();
        var nm = string.IsNullOrEmpty(t.Name) ? "?" : t.Name;
        if (keys.Any(T.RouteTiles.Contains)) { Msg($"Стрелка {nm} замкнута в маршруте — сначала отмените маршрут", true); return; }
        if (keys.Any(x => !string.IsNullOrEmpty(st.Tiles[x].Sec) && T.Occ.Contains(st.Tiles[x].Sec)))
        { Msg($"Стрелка {nm} занята — под составом не переводится", true); return; }
        bool plus = t.Plus == false;
        foreach (var x in keys) st.Tiles[x].Plus = plus ? null : false;
        Msg($"Стрелка {(!string.IsNullOrEmpty(t.Group) ? t.Group : nm)} → {(plus ? "плюс" : "минус")}");
    }

    void TestOccupy(string k)
    {
        if (!st.Tiles.TryGetValue(k, out var t)) return;
        if (string.IsNullOrEmpty(t.Sec)) { Msg("Клетка не отнесена к участку — занять нечего", true); return; }
        var T = ui.Test;
        if (!T.Occ.Remove(t.Sec)) T.Occ.Add(t.Sec);
        Msg($"Участок {t.Sec} {(T.Occ.Contains(t.Sec) ? "занят" : "свободен")}");
        RecomputeTest();
        RefreshPanels();
        Draw();
    }

    void ResetTest()
    {
        var T = ui.Test;
        T.Set = new List<Route>();
        T.Occ = new HashSet<string>();
        T.Pending = null;
        RecomputeTest();
        RefreshPanels();
        Draw();
        Msg("Всё сброшено");
    }
}
