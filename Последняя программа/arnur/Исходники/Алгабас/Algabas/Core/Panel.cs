namespace StationApp.Core;

// =====================================================================
//  ПУЛЬТ ДСП: ФУНКЦИИ ЭТАПА 6  [ДОБАВЛЕНО 21.10.2026]
//  Вторая часть класса Interlocking (partial). Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//
//  Как в тренажёре ДСП (часть 2, разделы 4–10, 13, 14) и на пульте «Енисей»:
//   Е1  отмена маршрута с выдержкой: «ОС» (приближение свободно, 6 с), «ОП» / «ОМ» (занято: поездной 3 мин, маневровый 1 мин);
//   Е2  искусственная разделка секций кнопкой «ИР» (выдержка, счётчик);
//   Е4  замыкание стрелок кнопками «ЧЗС», «НЗС», «ЗС» (счётчик);
//   Е5  красные колпачки;  Е6  курбельная заслонка, перевод курбелем, закладка с замком / типовая скоба;
//   Е7  выключение стрелки из централизации с сохранением пользования сигналами, макет;
//   Е8  автодействие «АДЧ» / «АДН»;  Е9  ключ-жезл;  Е10  переезд: извещение, отмена, закрытие, лампа ЗП;
//   Е11 взрез стрелки (лампа, звонок, отключение звонка);  Е14 раздельное управление (стрелки маршрутом не переводятся).
//  В Interlocking.cs только короткие вызовы отсюда (помечены «этап 6»).
// =====================================================================

public partial class Interlocking
{
    // ------------------------ настройки ------------------------
    /// <summary>Е1: выдержка отмены маршрута при свободном участке приближения (светофор был открыт).</summary>
    public double CancelFreeSeconds = 6;
    /// <summary>Е2: выдержка искусственной разделки секции кнопкой ИР (в жизни 3–4 мин; на уроке — из настроек урока).</summary>
    public double IrSeconds = 15;
    /// <summary>Е6: сколько работник опускает / поднимает заслонку, переводит курбелем, ставит замок (игровые секунды).</summary>
    public double CrankSeconds = 20;
    /// <summary>Е14: раздельное управление — стрелки маршрутом не переводятся, ДСП ставит их сам.</summary>
    public bool SeparateControl;

    // ------------------------ события ------------------------
    /// <summary>Сообщение работника (только «Закрыть»): должность, объект (W:…), текст.</summary>
    public event Action<string, string, string> WorkerInfo;
    /// <summary>Доклад работника («Подписать» / «Закрыть»): должность, объект, текст, подписал, не подписал.</summary>
    public event Action<string, string, string, Action, Action> WorkerReport;
    /// <summary>Взрез стрелки: стрелка, поезд (null — неисправность от преподавателя).</summary>
    public event Action<RailSwitch, VirtualTrain> SwitchCut;
    /// <summary>[ДОБАВЛЕНО 23.10.2026, этап 4] Такт модели (игровые секунды) — предупреждения, номера поездов на макете (Core/Radio.cs).</summary>
    public event Action<double> Ticked;

    // =================================================================
    //  ЧТО МЕШАЕТ ПЕРЕВЕСТИ СТРЕЛКУ С ПУЛЬТА (Е4–Е7, Е11)
    // =================================================================

    public string SwitchMoveBlock(RailSwitch sw)
    {
        if (sw.Cut) return "взрез, нужен электромеханик";
        if (sw.Excluded) return "выключена из централизации";
        if (sw.FlapDown) return "опущена курбельная заслонка";
        if (sw.Clamp.Length > 0) return sw.Clamp == "скоба" ? "закреплена на типовую скобу" : "заперта на закладку и замок";
        if (sw.RedCap) return "на кнопке стрелки красный колпачок";
        if (GroupLock(sw) is { } by) return $"замкнута кнопкой {by}";
        return null;
    }

    /// <summary>Контроля нет из-за действий на пульте (а не неисправности).</summary>
    static bool PanelNoControl(RailSwitch sw) => sw.FlapDown || sw.Cut || (sw.Excluded && sw.Maket == null);

    /// <summary>Нет контроля: выключенную на макете стрелку контролирует макет, остальное — неисправность и пульт.</summary>
    static bool LosesControl(RailSwitch sw) => sw.Excluded ? sw.Maket == null : sw.FaultNoControl || PanelNoControl(sw);
    static bool ControlBlocked(RailSwitch sw) => sw.Excluded ? sw.Maket == null : sw.FaultBlocked || PanelNoControl(sw);

    // =================================================================
    //  ДОП. УСЛОВИЯ МАРШРУТА
    // =================================================================

    /// <summary>Предусловие маршрута (направление перегона). Ключ-жезл изъят — перегон в распоряжении ДСП, направление не проверяем.</summary>
    string PreconditionOf(RailRoute r) =>
        Lines.Any(l => l.KeyOut && l.DependentRoutes.Contains(r)) ? null : r.Precondition?.Invoke();

    string PanelCheckRoute(RailRoute r)
    {
        // Переезд: маршрут через него задаётся, но светофор откроется после закрытия переезда (CrossingReady):
        // заданный маршрут сам даёт извещение, лампа ЗП мигает ClosingSeconds. «Извещение на переезд» закрывает его заранее.
        foreach (var l in Lines.Where(l => l.KeyOut && r.ExitSection != null && l.Sections.Contains(r.ExitSection)))
            if (l.KeyTrips >= 1)
                return $"ключ-жезл перегона изъят: по сигналу на перегон уже отправлена одна подвижная единица";

        // [ДОБАВЛЕНО 23.10.2026, этап 4] «окно»: путь закрыт для работ
        if (r.Sections.Append(r.ExitSection).FirstOrDefault(s => s?.Window == true) is { } win)
            return $"на участке {win.Label} «окно» (путь закрыт для работ)";

        if (SeparateControl)
            foreach (var (sw, plus) in r.Switches)
                if (sw.ShownPlus != plus || !sw.Controlled)
                    return $"раздельное управление: стрелка {sw.Label} не стоит в положении {(plus ? "+" : "−")} с контролем — переведите стрелки, потом задайте маршрут";
        return null;
    }

    /// <summary>Е10: маршрут через переезд открывает светофор только при закрытом переезде.</summary>
    bool CrossingReady(RailRoute r) => Crossings.All(c => !c.Routes.Contains(r) || c.Closed);

    // =================================================================
    //  Е1 ОТМЕНА МАРШРУТА С ВЫДЕРЖКОЙ
    // =================================================================

    /// <summary>Выдержка отмены: (секунды, вид «ОС» / «ОП» / «ОМ»), null — отменяется сразу (светофор не открывался).</summary>
    (double, string)? CancelDelay(RailRoute r)
    {
        if (!r.SignalWasOpen) return null;
        bool approachBusy = r.Start.Approach != null && r.Start.Approach.Occupied;
        if (approachBusy) return r.Shunting ? (ShuntReleaseSeconds, "ОМ") : (ArtificialReleaseSeconds, "ОП");
        return CancelFreeSeconds > 0 ? (CancelFreeSeconds, "ОС") : null;
    }

    public static string CancelKindText(string kind) => kind switch
    {
        "ОС" => "отмена маршрута (приближение свободно)",
        "ОП" => "отмена поездного маршрута при занятом приближении",
        "ОМ" => "отмена маневрового маршрута при занятом приближении",
        _ => "отмена маршрута"
    };

    // =================================================================
    //  Е2 ИСКУССТВЕННАЯ РАЗДЕЛКА СЕКЦИЙ (кнопка ИР)
    // =================================================================

    /// <summary>Секции, которые остались замкнутыми после проследования поезда и свободны — их можно разделать.</summary>
    public List<TrackSection> IrCandidates(IEnumerable<TrackSection> among) =>
        among.Where(s => s.LockedBy is { TrainEntered: true } && !s.Occupied && s.IrTimer <= 0).ToList();

    public bool StartIr(IList<TrackSection> secs)
    {
        if (secs.Count == 0) { Say("ИР: не выбрано ни одной секции", LogKind.Refuse); return false; }
        foreach (var s in secs) s.IrTimer = IrSeconds;
        CountedCommand?.Invoke("ИР", "ИР", $"искусственная разделка секций {string.Join(", ", secs.Select(s => s.Label))}");
        Say($"ИР: искусственная разделка секций {string.Join(", ", secs.Select(s => s.Label))}, выдержка {IrSeconds:0} с", LogKind.Aux);
        Commit();
        return true;
    }

    bool TickIr(double dt)
    {
        bool changed = false;
        foreach (var s in Sections.Values.Where(s => s.IrTimer > 0))
        {
            s.IrTimer -= dt;
            if (s.IrTimer > 0) continue;
            s.IrTimer = 0;
            var r = s.LockedBy;
            changed = true;
            if (r == null) continue;
            if (s.Occupied) { Say($"ИР секции {s.Label} не выполнена: секция занята", LogKind.Refuse); continue; }
            r.Released.Add(s);
            r.WasOccupied.Add(s);
            s.LockedBy = null;
            foreach (var (sw, _) in r.Switches)
                if (sw.Sections.Where(r.Sections.Contains).All(r.Released.Contains)) sw.LockedBy.Remove(r);
            Say($"ИР: секция {s.Label} разделана ({r.Name})", LogKind.Aux);
            if (r.Sections.All(r.Released.Contains)) ReleaseRoute(r, $"Маршрут {r.Name} разомкнут искусственной разделкой");
        }
        return changed;
    }

    // =================================================================
    //  Е4 ЗАМЫКАНИЕ СТРЕЛОК («ЧЗС», «НЗС», «ЗС»)
    // =================================================================

    readonly Dictionary<string, (string label, List<RailSwitch> sws)> lockGroups = new();
    /// <summary>Нажатые (зафиксированные) кнопки замыкания стрелок.</summary>
    public readonly HashSet<string> ActiveLocks = new();

    /// <summary>Номер стрелки «5/7» → 5. Чётные — в горловине чётного замыкания, нечётные — нечётного.</summary>
    public static int SwitchNumber(RailSwitch sw)
    {
        var digits = new string(sw.Label.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }

    public void RegisterLockGroup(string key, string label, IEnumerable<RailSwitch> sws) => lockGroups[key] = (label, sws.ToList());

    string GroupLock(RailSwitch sw) =>
        ActiveLocks.Where(k => lockGroups.TryGetValue(k, out var g) && g.sws.Contains(sw)).Select(k => lockGroups[k].label).FirstOrDefault();

    /// <summary>ЛКМ — нажать (замкнуть), ПКМ — отжать. Каждое нажатие — счётчик и запись в ДУ-46.</summary>
    public void SetGroupLock(string key, bool on)
    {
        if (!lockGroups.TryGetValue(key, out var g) || ActiveLocks.Contains(key) == on) return;
        if (on) ActiveLocks.Add(key); else ActiveLocks.Remove(key);
        CountedCommand?.Invoke(key, g.label, on ? $"замыкание стрелок {string.Join(", ", g.sws.Select(s => s.Label))}" : "размыкание стрелок (кнопка отжата)");
        Say($"Кнопка {g.label} {(on ? "нажата: стрелки замкнуты" : "отжата: стрелки разомкнуты")} ({string.Join(", ", g.sws.Select(s => s.Label))})", LogKind.Aux);
        Commit();
    }

    // =================================================================
    //  Е5 КРАСНЫЕ КОЛПАЧКИ
    // =================================================================

    public void SetRedCap(RailSwitch sw, bool on)
    {
        if (sw.RedCap == on) return;
        sw.RedCap = on;
        Say($"Стрелка {sw.Label}: красный колпачок {(on ? "установлен" : "снят")}", LogKind.Aux);
        Commit();
    }

    // =================================================================
    //  Е6 КУРБЕЛЬ, ЗАКЛАДКА И ЗАМОК, ТИПОВАЯ СКОБА (выполняет работник через CrankSeconds)
    // =================================================================

    readonly List<(double left, string what, Action act)> ops = new();
    public bool Busy(RailSwitch sw) => ops.Any(o => o.what == sw.Id);

    void Later(RailSwitch sw, double sec, Action act) => ops.Add((sec, sw.Id, act));

    string Info(string who, RailSwitch sw, string text)
    {
        WorkerInfo?.Invoke(who, "W:" + sw.Id, text);
        Say(text, LogKind.Aux);
        return text;
    }

    public bool SetFlap(RailSwitch sw, bool down)
    {
        if (sw.FlapDown == down || Busy(sw)) return false;
        if (!down && sw.Clamp.Length > 0) { Say($"Стрелка {sw.Label}: сначала снимите {(sw.Clamp == "скоба" ? "типовую скобу" : "закладку и замок")}", LogKind.Refuse); return false; }
        if (down && sw.MoveTimer > 0) { Say($"Стрелка {sw.Label} переводится — заслонку опустить нельзя", LogKind.Refuse); return false; }
        Say($"Стрелка {sw.Label}: дано указание {(down ? "опустить" : "поднять")} курбельную заслонку", LogKind.Aux);
        Later(sw, CrankSeconds / 2, () =>
        {
            sw.FlapDown = down;
            Info("Монтёр пути", sw, $"Курбельная заслонка {(down ? "опущена" : "поднята")} на стрелке {sw.Label}");
        });
        return true;
    }

    public bool CrankTo(RailSwitch sw, bool plus)
    {
        if (Busy(sw)) return false;
        if (!sw.FlapDown) { Say($"Стрелка {sw.Label}: перевод курбелем — только при опущенной курбельной заслонке", LogKind.Refuse); return false; }
        if (sw.Clamp.Length > 0) { Say($"Стрелка {sw.Label} заперта — перевод курбелем невозможен", LogKind.Refuse); return false; }
        if (sw.Sections.Any(s => s.TrainCount > 0)) { Say($"ОТКАЗ: на стрелке {sw.Label} подвижной состав", LogKind.Refuse); return false; }
        if (sw.Plus == plus) { Say($"Стрелка {sw.Label} и так стоит в {(plus ? "плюсе" : "минусе")}", LogKind.Refuse); return false; }
        Say($"Стрелка {sw.Label}: дано указание перевести курбелем в {(plus ? "плюс" : "минус")}", LogKind.Aux);
        Later(sw, CrankSeconds, () =>
        {
            sw.Plus = plus;
            sw.PendingCommand = false;
            SwitchCommand?.Invoke(sw);           // на макете остряки переводятся руками — сервопривод повторяет
            Info("Монтёр пути", sw, $"Перевод курбелем стрелки {sw.Label} в {(plus ? "плюс" : "минус")} завершён");
        });
        return true;
    }

    /// <summary>kind: «замок» — закладка и навесной замок, «скоба» — типовая скоба, «» — снять.</summary>
    public bool SetClamp(RailSwitch sw, string kind)
    {
        if (Busy(sw) || sw.Clamp == kind) return false;
        if (kind.Length > 0 && sw.Clamp.Length > 0) { Say($"Стрелка {sw.Label} уже заперта", LogKind.Refuse); return false; }
        if (kind.Length > 0 && sw.MoveTimer > 0) { Say($"Стрелка {sw.Label} переводится", LogKind.Refuse); return false; }
        string was = sw.Clamp;
        Say($"Стрелка {sw.Label}: дано указание {(kind.Length > 0 ? (kind == "скоба" ? "закрепить на типовую скобу" : "запереть на закладку и замок") : "снять " + (was == "скоба" ? "типовую скобу" : "замок"))}", LogKind.Aux);
        Later(sw, CrankSeconds, () =>
        {
            sw.Clamp = kind;
            Info("Монтёр пути", sw, kind == "замок" ? $"Замок установлен на стрелку {sw.Label} ({(sw.Plus ? "плюс" : "минус")})"
                : kind == "скоба" ? $"Стрелка {sw.Label} закреплена на типовую скобу ({(sw.Plus ? "плюс" : "минус")})"
                : $"Стрелка {sw.Label}: {(was == "скоба" ? "типовая скоба снята" : "замок снят")}");
        });
        return true;
    }

    // =================================================================
    //  Е7 ВЫКЛЮЧЕНИЕ ИЗ ЦЕНТРАЛИЗАЦИИ С СОХРАНЕНИЕМ ПОЛЬЗОВАНИЯ СИГНАЛАМИ, МАКЕТ
    // =================================================================

    /// <summary>Доклад ШН «Подписать» → стрелка выключается. «Закрыть» — не выключается.</summary>
    public void RequestExclude(RailSwitch sw)
    {
        if (sw.Excluded) return;
        if (sw.Locked) { Say($"Стрелка {sw.Label} замкнута в маршруте — выключить из централизации можно в свободное от движения время", LogKind.Refuse); return; }
        Say($"Стрелка {sw.Label}: запрошено выключение из централизации с сохранением пользования сигналами", LogKind.Aux);
        WorkerReport?.Invoke("ШН", "W:" + sw.Id,
            $"В свободное от движения поездов время стрелка №{sw.Label} будет выключена из централизации с сохранением пользования сигналами.",
            () =>
            {
                if (sw.Locked) { Say($"Стрелка {sw.Label} уже в маршруте — выключение отменено", LogKind.Refuse); return; }
                sw.Excluded = true;
                sw.Maket = null;
                Say($"Стрелка {sw.Label} выключена из централизации (контроля нет). Запереть её и установить на макет", LogKind.Aux);
                Commit();
            },
            () => Say($"Стрелка {sw.Label}: доклад ШН не подписан — стрелка не выключена", LogKind.Refuse));
    }

    public bool SetMaket(RailSwitch sw, bool? pos)
    {
        if (!sw.Excluded) { Say($"Стрелка {sw.Label} не выключена из централизации — макет не ставится", LogKind.Refuse); return false; }
        if (pos != null && sw.Clamp.Length == 0) { Say($"Стрелка {sw.Label}: сначала запереть на закладку и замок или типовую скобу", LogKind.Refuse); return false; }
        if (pos == null && sw.Locked) { Say($"Стрелка {sw.Label} в маршруте — снять макет нельзя", LogKind.Refuse); return false; }
        sw.Maket = pos;
        Say(pos == null ? $"Стрелка {sw.Label}: макет снят" : $"Стрелка {sw.Label} установлена на макет в положение {(pos.Value ? "плюс" : "минус")}", LogKind.Aux);
        Commit();
        return true;
    }

    public bool Include(RailSwitch sw)
    {
        if (!sw.Excluded) return false;
        if (sw.Maket != null || sw.Clamp.Length > 0) { Say($"Стрелка {sw.Label}: сначала снимите макет и замок", LogKind.Refuse); return false; }
        sw.Excluded = false;
        Say($"Стрелка {sw.Label} включена в централизацию", LogKind.Aux);
        Commit();
        return true;
    }

    /// <summary>«Информация»: что показывает пульт и как стрелка стоит на самом деле.</summary>
    public string SwitchInfo(RailSwitch sw)
    {
        var lines = new List<string>
        {
            $"Стрелка {sw.Label} ({sw.Title})",
            $"На пульте: {(sw.Controlled ? (sw.ShownPlus ? "+" : "−") : "нет контроля")}",
            $"Реально: {(sw.Plus ? "+ (плюс)" : "− (минус)")}",
        };
        if (sw.Excluded) lines.Add(sw.Maket is bool m ? $"Выключена из централизации, поставлена на макет: {(m ? "+" : "−")}" : "Выключена из централизации, макета нет");
        if (sw.FlapDown) lines.Add("Курбельная заслонка опущена");
        if (sw.Clamp.Length > 0) lines.Add(sw.Clamp == "скоба" ? "Закреплена на типовую скобу" : "Заперта на закладку и навесной замок");
        if (sw.RedCap) lines.Add("На кнопке красный колпачок");
        if (GroupLock(sw) is { } g) lines.Add($"Замкнута кнопкой {g}");
        if (sw.Cut) lines.Add("ВЗРЕЗ");
        if (Busy(sw)) lines.Add("Работник выполняет указание…");
        return string.Join("\n", lines);
    }

    // =================================================================
    //  Е8 АВТОДЕЙСТВИЕ («АДЧ» — чётное, «АДН» — нечётное)
    // =================================================================

    readonly Dictionary<string, (string label, List<RailRoute> routes, Dir dir)> autoGroups = new();
    public readonly HashSet<string> AutoOn = new();

    public void RegisterAutoGroup(string key, string label, IEnumerable<RailRoute> routes, Dir dir) =>
        autoGroups[key] = (label, routes.Where(r => !r.Shunting && !r.Diverging && r.Start.Dir == dir).ToList(), dir);

    public void SetAutoAction(string key, bool on)
    {
        if (!autoGroups.TryGetValue(key, out var g) || AutoOn.Contains(key) == on) return;
        if (on)
        {
            AutoOn.Add(key);
            // кнопка пломбируемая: срыв пломбы записывают в ДУ-46
            CountedCommand?.Invoke(key, g.label, "включено автодействие светофоров (сорвана пломба)");
            int n = 0;
            foreach (var r in g.routes.Where(r => r.Active)) { r.Auto = true; n++; }
            Say($"{g.label}: автодействие включено" + (n > 0 ? $" ({n} маршр.)" : " — задайте сквозные маршруты по главному пути, они будут задаваться сами"), LogKind.Aux);
        }
        else
        {
            AutoOn.Remove(key);
            foreach (var r in g.routes) r.Auto = false;
            Say($"{g.label}: автодействие выключено", LogKind.Aux);
        }
        Commit();
    }

    /// <summary>Маршрут задан: при включённом автодействии своего направления — на автодействие.</summary>
    void MarkAuto(RailRoute r)
    {
        foreach (var (key, g) in autoGroups)
            if (AutoOn.Contains(key) && g.routes.Contains(r)) r.Auto = true;
    }

    /// <summary>Маршрут разомкнулся за поездом: автодействие задаёт его снова; ключ-жезл считает отправленные единицы.</summary>
    void AfterTrainRelease(RailRoute r)
    {
        foreach (var l in Lines.Where(l => l.KeyOut && r.ExitSection != null && l.Sections.Contains(r.ExitSection))) l.KeyTrips++;
        if (!r.Auto) return;
        var why = CheckRoute(r);
        if (why != null) { r.Auto = false; Say($"Автодействие: маршрут {r.Name} не задан — {why}", LogKind.Refuse); return; }
        SetRoute(r);
    }

    // =================================================================
    //  Е9 КЛЮЧ-ЖЕЗЛ
    // =================================================================

    public bool KeyTake(DirectionLine l)
    {
        if (l.KeyOut) return false;
        if (l.AuxRunning || l.Syncing) { Say($"{l.Title}: идёт смена направления — ключ-жезл не изымается", LogKind.Refuse); return false; }
        l.KeyOut = true;
        l.KeyTrain = 0;
        l.KeyTrips = 0;
        Say($"{l.Title}: ключ-жезл изъят (смена направления заблокирована, на перегон по сигналу — одна единица)", LogKind.Aux);
        Commit();
        return true;
    }

    public bool KeyGive(DirectionLine l, VirtualTrain t)
    {
        if (!l.KeyOut || l.KeyTrain != 0) { Say("Ключ-жезл не у дежурного", LogKind.Refuse); return false; }
        if (t.Cells.Any(l.Sections.Contains)) { Say($"Поезд №{t.Number} на перегоне — ключ-жезл выдаётся на станции", LogKind.Refuse); return false; }
        t.KeyLine = l;
        l.KeyTrain = t.Number;
        Say($"{l.Title}: ключ-жезл выдан машинисту №{t.Number}", LogKind.Aux);
        Commit();
        return true;
    }

    public bool KeyBack(DirectionLine l, TrainSim trains)
    {
        var t = trains.Trains.FirstOrDefault(x => x.KeyLine == l);
        if (t != null && t.Cells.Any(l.Sections.Contains)) { Say($"Локомотив №{t.Number} ещё на перегоне — забрать ключ-жезл нельзя", LogKind.Refuse); return false; }
        if (t != null) t.KeyLine = null;
        int n = l.KeyTrain;
        l.KeyTrain = 0;
        Say($"{l.Title}: ключ-жезл забран у машиниста №{n}", LogKind.Aux);
        Commit();
        return true;
    }

    public bool KeyPut(DirectionLine l)
    {
        if (!l.KeyOut) return false;
        if (l.KeyTrain != 0) { Say($"Ключ-жезл у машиниста №{l.KeyTrain} — сначала заберите", LogKind.Refuse); return false; }
        l.KeyOut = false;
        l.KeyTrips = 0;
        Say($"{l.Title}: ключ-жезл вложен в пульт", LogKind.Aux);
        Commit();
        return true;
    }

    // =================================================================
    //  Е10 ПЕРЕЕЗД
    // =================================================================

    public void SetCrossingNotice(LevelCrossing c, bool on)
    {
        if (c.Notice == on) return;
        c.Notice = on;
        Say(on ? $"{c.Title}: извещение на переезд" : $"{c.Title}: извещение отменено", LogKind.Aux);
        UpdateCrossings();
        Commit();
    }

    /// <summary>Переезд по извещению закрывается не сразу (лампа ЗП мигает). true — надо закрыть, состояние посчитано.</summary>
    bool CrossingWanted(LevelCrossing c, bool auto)
    {
        bool notice = c.Notice || c.Routes.Any(r => r.Active && !r.Releasing);
        if (auto || c.ManualClosed) { c.ClosingTimer = 0; c.NoticeReady = false; return true; }
        if (!notice) { c.ClosingTimer = 0; c.NoticeReady = false; return false; }
        if (!c.NoticeReady && !c.Closing) c.ClosingTimer = c.ClosingSeconds;
        return c.NoticeReady;
    }

    bool TickCrossings(double dt)
    {
        bool changed = false;
        foreach (var c in Crossings.Where(c => c.Closing))
        {
            c.ClosingTimer -= dt;
            if (c.ClosingTimer > 0) continue;
            c.ClosingTimer = 0;
            c.NoticeReady = true;
            changed = true;
        }
        return changed;
    }

    // =================================================================
    //  Е11 ВЗРЕЗ
    // =================================================================

    /// <summary>Звонок взреза звенит, пока его не отключат кнопкой.</summary>
    public bool Bell;
    public bool CutLamp => Switches.Values.Any(s => s.Cut);

    /// <summary>Поезд вступил на стрелку с пошёрстного направления, стоящую не по ходу: остряки отжаты.</summary>
    public void CutSwitch(RailSwitch sw, VirtualTrain t)
    {
        if (sw.Cut) return;
        if (t != null) sw.Plus = !sw.Plus;     // поезд отжал остряки в своё положение
        sw.Cut = true;
        sw.Controlled = false;
        sw.MoveTimer = 0;
        Bell = true;
        Say($"ВЗРЕЗ СТРЕЛКИ {sw.Label}" + (t != null ? $" поездом №{t.Number}" : "") + "! Лампа «Взрез», звонок. Движение по стрелке закрыть, вызвать ШН", LogKind.Refuse);
        SwitchCut?.Invoke(sw, t);
        Commit();
    }

    public void BellOff()
    {
        if (!Bell) return;
        Bell = false;
        Say("Звонок взреза отключён", LogKind.Aux);
        Commit();
    }

    /// <summary>Стрелку после взреза осмотрел и восстановил ШН (или имитация без урока).</summary>
    public void RestoreCut(RailSwitch sw)
    {
        if (!sw.Cut) return;
        sw.Cut = false;
        SwitchCommand?.Invoke(sw);              // привод — в положение, в котором стрелка осталась
        if (!CutLamp) Bell = false;
        Say($"Стрелка {sw.Label} восстановлена после взреза", LogKind.Aux);
        Commit();
    }

    // =================================================================
    //  ТАКТ И СБРОС
    // =================================================================

    bool PanelTick(double dt)
    {
        bool changed = TickIr(dt) | TickCrossings(dt);
        Ticked?.Invoke(dt);   // [ДОБАВЛЕНО 23.10.2026, этап 4]
        for (int i = ops.Count - 1; i >= 0; i--)
        {
            var (left, what, act) = ops[i];
            left -= dt;
            if (left > 0) { ops[i] = (left, what, act); continue; }
            ops.RemoveAt(i);
            act();
            changed = true;
        }
        return changed;
    }

    void PanelReset()
    {
        ops.Clear();
        ActiveLocks.Clear();
        AutoOn.Clear();
        Bell = false;
        foreach (var r in Routes) { r.Auto = false; r.CancelKind = ""; }
        foreach (var s in Sections.Values) s.IrTimer = 0;
        foreach (var sw in Switches.Values)
        {
            sw.RedCap = sw.FlapDown = sw.Excluded = sw.Cut = false;
            sw.Clamp = "";
            sw.Maket = null;
        }
        foreach (var l in Lines) { l.KeyOut = false; l.KeyTrain = 0; l.KeyTrips = 0; }
        foreach (var c in Crossings) { c.Notice = false; c.ClosingTimer = 0; c.NoticeReady = false; }
    }
}
