using System.Diagnostics;
using StationEditor.Model;

namespace StationEditor.Ui;

// =====================================================================
//  РЕЖИМ ИСПЫТАНИЯ: маршруты, стрелки, занятость
//  [ИЗМЕНЕНО 05.11.2026] показания светофоров (Model/Aspects.cs), стрелки переводятся приводом по очереди
//  с амперметром (Model/SwitchDrive.cs), заградительный и прикрытия включаются щелчком.
// =====================================================================
public sealed partial class MainForm
{
    // таймер «Испытания» и часов: перевод стрелок, мигание огней, ход часов
    readonly System.Windows.Forms.Timer anim = new() { Interval = 50 };
    readonly Stopwatch animClock = Stopwatch.StartNew();
    double animLast;
    long lastBlink = -1, lastSecond = -1;

    void StartAnim()
    {
        anim.Tick += (_, _) => AnimTick();
        anim.Start();
    }

    void AnimTick()
    {
        double now = animClock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.25, now - animLast);
        animLast = now;
        bool redraw = false;

        if (ui.IsTest)
        {
            var T = ui.Test;
            bool wasActive = T.Drive.Active;
            var ev = T.Drive.Tick(st, dt);
            if (ev is DriveEvent e)
            {
                Msg(e.Text, e.Bad);
                RecomputeTest();
                RefreshPanels();
            }
            else if (T.Drive.Current != null && !T.NoControl.SetEquals(T.Drive.NoControl()))
                RecomputeTest();    // началась очередная стрелка — она без контроля
            redraw |= wasActive || T.Drive.Active;
            long blink = Environment.TickCount64 / 500;
            if (blink != lastBlink)
            {
                lastBlink = blink;
                redraw |= T.Aspects.Values.Any(a => a.Lamps.Any(l => l.Blink)) || T.Drive.Lost.Count > 0;
            }
        }
        long sec = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
        if (sec != lastSecond)
        {
            lastSecond = sec;
            redraw |= st.Objects.Values.Any(o => o.Type == "clock");
        }
        if (redraw) canvas.Invalidate();
    }

    void SetMode(string m)
    {
        if (m == ui.Mode) return;
        CommitFocusedField();
        var T = ui.Test;
        if (m == "test")
        {
            ui.Sel.Clear(); ui.SelSig = null; ui.Ghost = null; ui.Band = null; ui.FlashSec = null; ui.Preview = null;
            ui.SelObj = null; ui.ObjMove = null;
            ui.Moving = null; drag = null;
            T.SavedPlus = new Dictionary<string, bool?>();
            foreach (var (k, t) in st.Tiles) if (t.T == "switch") T.SavedPlus[k] = t.Plus;
            T.Occ = new HashSet<string>(); T.Set = new List<Route>(); T.Pending = null;
            T.BarrierOn = new HashSet<string>(); T.CoverOpen = new HashSet<string>();
            T.Drive.Reset();
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
            T.Drive.Reset();
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

    /// <summary>Стоят ли все стрелки маршрута как надо и с контролем.</summary>
    bool SwitchesReady(Route r) =>
        r.Switches.All(x => x.Keys.All(k => st.Tiles.TryGetValue(k, out var t) && t.IsPlus == x.Plus && !ui.Test.NoControl.Contains(k)));

    void RecomputeTest()
    {
        var T = ui.Test;
        T.NoControl = T.Drive.NoControl();
        T.RouteTiles = new HashSet<string>();
        T.OpenSigs = new HashSet<string>();
        T.OpenRoutes = new Dictionary<string, Route>();
        foreach (var r in T.Set)
        {
            foreach (var k in r.Tiles) T.RouteTiles.Add(k);
            bool busy = r.Sections.Any(T.Occ.Contains) || (!r.Exit && T.Occ.Contains(r.Dest));
            // светофор откроется, когда стрелки маршрута дойдут и будут под контролем
            if (!busy && SwitchesReady(r))
            {
                var id = r.Start.Key + "#" + r.Start.Idx;
                T.OpenSigs.Add(id);
                T.OpenRoutes[id] = r;
            }
        }
        T.Aspects = Aspects.Compute(st, new TestInputs
        {
            Occ = T.Occ, OpenRoutes = T.OpenRoutes, NoControl = T.NoControl, BarrierOn = T.BarrierOn, CoverOpen = T.CoverOpen,
        });
    }

    /// <summary>Клетки стрелок одной группы (спаренные — вместе).</summary>
    List<string> GroupKeys(string group) =>
        st.Tiles.Where(p => p.Value.T == "switch" && Core.SwitchGroup(p.Key, p.Value) == group).Select(p => p.Key).ToList();

    static string SwName(Tile t) => !string.IsNullOrEmpty(t.Group) ? t.Group : !string.IsNullOrEmpty(t.Name) ? t.Name : "?";

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
        var moving = r.Switches.FirstOrDefault(x => T.Drive.Moving(x.Group));
        if (moving != null)
        {
            if (!quiet) Msg($"Маршрут {r.Name} не задан: стрелка {moving.Name} ещё переводится", true);
            return false;
        }
        // стрелки, которые стоят не так или без контроля, переводятся приводом по очереди
        int n = 0;
        foreach (var x in r.Switches)
        {
            var t0 = st.Tiles[x.Keys[0]];
            bool lost = x.Keys.Any(T.Drive.Lost.Contains);
            if (t0.IsPlus == x.Plus && !lost) continue;
            T.Drive.Enqueue(x.Group, x.Keys, x.Name, x.Plus, t0.IsPlus);
            n++;
        }
        T.Set.Add(r);
        RecomputeTest();
        Msg($"Задан маршрут {(r.Shunt ? "маневровый" : "поездной")}: {r.Name}" +
            (n > 0 ? $" — переводятся стрелки ({n}), светофор откроется, когда они дойдут" : ""));
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
            var sig = st.Tiles[s.Key].Sigs[s.Idx];
            var id = s.Key + "#" + s.Idx;
            var name = sig.Name;
            var set = T.Set.FirstOrDefault(r => r.Start == s);
            if (sig.IsReadOnly)    // [ДОБАВЛЕНО 09.11.2026]
                Msg($"{Core.SigKindName(sig.Kind)} {name} — только чтение: его открывает другая станция (id {sig.Id}). " +
                    "Без связи с ней он погашен.", true);
            else if (sig.Kind == "barrier")
            {
                // заградительный включает и выключает дежурный (или его включает занятый переезд впереди)
                if (!T.BarrierOn.Remove(id)) T.BarrierOn.Add(id);
                RecomputeTest();
                Msg($"Заградительный {name}: {(T.BarrierOn.Contains(id) ? "включён — красный" : "выключен")} ({T.Aspects[id].Text})");
            }
            else if (sig.Kind == "cover")
            {
                if (!T.CoverOpen.Remove(id)) T.CoverOpen.Add(id);
                RecomputeTest();
                Msg($"Светофор прикрытия {name}: {T.Aspects[id].Text}", T.CoverOpen.Contains(id) && !T.Aspects[id].Permissive);
            }
            else if (set != null) CancelRoute(set);
            else if (T.Pending == s) { T.Pending = null; Msg("Набор маршрута отменён"); }
            else if (!Routes.Any(r => r.Start == s))
                Msg($"{Core.SigKindName(sig.Kind)} {name}: {(T.Aspects.TryGetValue(id, out var a) ? a.Text : "")} — " +
                    (sig.Kind is "block" or "warning" or "repeater" or "other" ? "показание меняется само" : "маршрутов от него нет"),
                    !(sig.Kind is "block" or "warning" or "repeater" or "other"));
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
        var keys = GroupKeys(g);
        var nm = SwName(t);
        if (keys.Any(T.RouteTiles.Contains)) { Msg($"Стрелка {nm} замкнута в маршруте — сначала отмените маршрут", true); return; }
        if (keys.Any(x => !string.IsNullOrEmpty(st.Tiles[x].Sec) && T.Occ.Contains(st.Tiles[x].Sec)))
        { Msg($"Стрелка {nm} занята — под составом не переводится", true); return; }
        if (T.Drive.Moving(g)) { Msg($"Стрелка {nm} уже переводится — смотрите амперметр", true); return; }
        // без контроля — возвращаем в прежнее положение (туда препятствие не мешает), иначе — в другое
        bool lost = keys.Any(T.Drive.Lost.Contains);
        bool toPlus = lost ? t.IsPlus : !t.IsPlus;
        T.Drive.Enqueue(g, keys, nm, toPlus, t.IsPlus);
        RecomputeTest();
        Msg($"Стрелка {nm} переводится в {(toPlus ? "плюс" : "минус")}" +
            (T.Drive.Jammed.Contains(g) && toPlus != t.IsPlus ? " — на пути остряка препятствие!" : ""));
    }

    /// <summary>[ДОБАВЛЕНО 05.11.2026] Неисправность «стрелка не доходит» (препятствие между остряком и рамным рельсом).</summary>
    void ToggleJam(string k)
    {
        if (!st.Tiles.TryGetValue(k, out var t) || t.T != "switch") return;
        var g = Core.SwitchGroup(k, t);
        var J = ui.Test.Drive.Jammed;
        if (!J.Remove(g)) J.Add(g);
        Msg($"Стрелка {SwName(t)}: {(J.Contains(g) ? "препятствие — при переводе в другое положение не дойдёт" : "препятствие убрано")}", J.Contains(g));
        RefreshPanels();
        Draw();
    }

    void TestOccupy(string k)
    {
        if (!st.Tiles.TryGetValue(k, out var t)) return;
        if (t.T == "switch" && (ModifierKeys & Keys.Shift) != 0) { ToggleJam(k); return; }
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
        T.BarrierOn = new HashSet<string>();
        T.CoverOpen = new HashSet<string>();
        T.Drive.Queue.Clear();
        RecomputeTest();
        RefreshPanels();
        Draw();
        Msg("Всё сброшено");
    }
}
