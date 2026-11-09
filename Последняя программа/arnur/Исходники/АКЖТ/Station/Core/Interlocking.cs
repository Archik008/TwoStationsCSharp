namespace StationApp.Core;

// =====================================================================
//  ОБЩЕЕ ЯДРО ЖД-ЗАВИСИМОСТЕЙ — ЛОГИКА (электрическая централизация + АБ)
//
//  Что умеет (как на реальной ЖД, упрощённо для макета):
//   • задание маршрута: проверка враждебности, свободности, положения стрелок;
//   • замыкание участков и стрелок маршрута;
//   • перевод стрелок с потерей контроля на время перевода;
//   • открытие светофора только при всех условиях; показание по ПТЭ;
//   • автоматическое перекрытие светофора при вступлении поезда;
//   • посекционное размыкание маршрута за поездом;
//   • искусственная разделка с выдержкой времени;
//   • трёхзначная автоблокировка на перегонах;
//   • смена направления на однопутном перегоне (обычная и вспомогательная);
//   • пригласительный сигнал с автоотключением;
//   • вспомогательный перевод стрелки при ложной занятости;
//   • автоматический переезд.
// =====================================================================

public partial class Interlocking   // [ИЗМЕНЕНО 21.10.2026] вторая часть — Core/Panel.cs (пульт ДСП, этап 6)
{
    // ------------------------ настройки ------------------------
    /// <summary>Сколько секунд стрелка переводится (без контроля).</summary>
    public double SwitchMoveSeconds = 1.5;
    /// <summary>Выдержка при искусственной разделке. В жизни ~3 мин (180 с) для поездного маршрута,
    /// на макете укорочено, чтобы на занятии не ждать.</summary>
    public double ArtificialReleaseSeconds = 15;
    /// <summary>Выдержка разделки маневрового маршрута (в жизни ~1 мин).</summary>
    public double ShuntReleaseSeconds = 5;
    /// <summary>Сколько горит пригласительный, если поезд так и не вступил.</summary>
    public double InviteSeconds = 60;
    /// <summary>Выдержка вспомогательной смены направления.</summary>
    public double AuxDirectionSeconds = 10;
    /// <summary>[ДОБАВЛЕНО 20.10.2026] Сколько маршрут устанавливается (мигает) до открытия светофора — как в Python TrainSt.</summary>
    public double RouteSetupSeconds = 2;

    // ------------------------ объекты ------------------------
    public readonly Dictionary<string, TrackSection> Sections = new();
    public readonly Dictionary<string, RailSwitch> Switches = new();
    public readonly Dictionary<string, RailSignal> Signals = new();
    public readonly List<RailRoute> Routes = new();
    public readonly List<DirectionLine> Lines = new();
    public readonly List<LevelCrossing> Crossings = new();

    public IEnumerable<RailRoute> ActiveRoutes => Routes.Where(r => r.Active);

    /// <summary>Проверка ламп: горят все лампы всех светофоров.</summary>
    public bool TestMode;

    // ------------------------ события ------------------------
    public event Action<string, LogKind> Log;
    /// <summary>Стрелку надо физически перевести (для Arduino).</summary>
    public event Action<RailSwitch> SwitchCommand;
    public event Action Changed;
    // [ДОБАВЛЕНО 20.10.2026, этап 2 — занятие] события для подсчёта результатов студента (Core/Training.cs)
    /// <summary>Маршрут не задан (враждебность, занятость…): маршрут, причина.</summary>
    public event Action<RailRoute, string> RouteRefused;
    /// <summary>Поезд проследовал поездной маршрут (маршрут разомкнулся за поездом или поезд принят на путь).</summary>
    public event Action<RailRoute> TrainPassed;
    /// <summary>Маршрут отменён дежурным при поезде на участке приближения (пошла искусственная разделка).</summary>
    public event Action<RailRoute> CancelledBeforeTrain;
    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Нажата кнопка со счётчиком (пригласительный, ИР, ВК, ВСН):
    /// ключ счётчика, название кнопки, что сделано. Счётчики и запись в ДУ-46 — Core/Du46.cs.</summary>
    public event Action<string, string, string> CountedCommand;

    public void Say(string msg, LogKind kind = LogKind.Info) => Log?.Invoke(msg, kind);
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>После любой команды дежурного показания пересчитываются сразу, не дожидаясь такта.</summary>
    void Commit()
    {
        ComputeAspects();
        Changed?.Invoke();
    }

    // ------------------------ создание объектов ------------------------
    public TrackSection AddSection(string id, string title, string label = null)
    {
        var s = new TrackSection { Id = id, Title = title, Label = label ?? id };
        Sections[id] = s;
        return s;
    }

    public RailSwitch AddSwitch(string id, string title, params TrackSection[] sections) =>
        AddSwitchL(id, id, title, sections);

    public RailSwitch AddSwitchL(string id, string label, string title, params TrackSection[] sections)
    {
        var sw = new RailSwitch { Id = id, Label = label, Title = title };
        sw.Sections.AddRange(sections);
        Switches[id] = sw;
        return sw;
    }

    public RailSignal AddSignal(RailSignal s)
    {
        Signals[s.Id] = s;
        return s;
    }

    public RailRoute AddRoute(RailRoute r)
    {
        Routes.Add(r);
        return r;
    }

    // =================================================================
    //  МАРШРУТЫ
    // =================================================================

    /// <summary>Можно ли задать маршрут. null — можно, иначе причина отказа.</summary>
    public string CheckRoute(RailRoute r)
    {
        if (r.Active) return $"маршрут {r.Name} уже задан";

        foreach (var a in ActiveRoutes)
            if (a.Start == r.Start)
                return $"от светофора {r.Start.Label} уже задан маршрут {a.Name}";

        if (r.Start.InviteOn) return $"на светофоре {r.Start.Label} горит пригласительный";

        var pre = PreconditionOf(r);                       // [ИЗМЕНЕНО 21.10.2026, этап 6] ключ-жезл
        if (pre != null) return pre;
        var panel = PanelCheckRoute(r);                    // [ДОБАВЛЕНО 21.10.2026, этап 6] переезд, ключ-жезл, раздельное управление
        if (panel != null) return panel;

        if (Lines.Any(l => l.Syncing && l.DependentRoutes.Contains(r)))
            return "идёт согласование смены направления перегона с соседней станцией";

        foreach (var s in r.Sections)
        {
            if (s.LockedBy != null && s.LockedBy != r)
                return $"враждебный маршрут: участок {s.Label} замкнут в маршруте {s.LockedBy.Name}";
            if (s.Occupied)
                return $"участок {s.Label} занят";
        }

        foreach (var (sw, plus) in r.Switches)
        {
            if (sw.ShownPlus != plus)
            {
                if (sw.Locked)
                    return $"стрелка {sw.Label} заперта в положении {sw.PosText} (враждебный маршрут или пригласительный)";
                if (SwitchMoveBlock(sw) is { } block)          // [ДОБАВЛЕНО 21.10.2026, этап 6]
                    return $"стрелку {sw.Label} нельзя перевести: {block}";
                if (sw.Sections.Any(x => x.Occupied))
                    return $"стрелку {sw.Label} нельзя перевести: занят стрелочный участок";
            }
        }
        return null;
    }

    public bool SetRoute(RailRoute r)
    {
        var why = CheckRoute(r);
        if (why != null)
        {
            Say($"ОТКАЗ {r.Name}: {why}", LogKind.Refuse);
            RouteRefused?.Invoke(r, why);
            return false;
        }

        r.ResetState();
        r.Active = true;
        r.SetOrder = ++setCounter;
        r.SetupTimer = RouteSetupSeconds;

        foreach (var s in r.Sections) s.LockedBy = r;
        foreach (var (sw, plus) in r.Switches)
        {
            sw.LockedBy.Add(r);
            // [ИЗМЕНЕНО 20.10.2026, этап 3] стрелку без контроля (не дошла из-за неисправности) — перевести ещё раз
            if ((sw.ShownPlus != plus || (!sw.Controlled && sw.MoveTimer <= 0)) && SwitchMoveBlock(sw) == null) StartMove(sw, plus);
        }
        MarkAuto(r);                                       // [ДОБАВЛЕНО 21.10.2026, этап 6] автодействие

        Say($"Задан маршрут {r.Name}");
        Commit();
        return true;
    }

    /// <summary>Отмена маршрута дежурным. С учётом участка приближения — возможна выдержка времени.</summary>
    public void CancelRoute(RailRoute r)
    {
        if (!r.Active) return;

        if (r.TrainEntered)
        {
            Say($"Маршрут {r.Name}: поезд уже в маршруте — маршрут разомкнётся за поездом автоматически", LogKind.Refuse);
            return;
        }
        if (r.Releasing)
        {
            Say($"Маршрут {r.Name}: искусственная разделка уже идёт, осталось {Math.Ceiling(r.ArtificialTimer)} с", LogKind.Refuse);
            return;
        }

        // [ИЗМЕНЕНО 21.10.2026, этап 6] отмена с выдержкой: «ОС» 6 с (приближение свободно), «ОП» 3 мин / «ОМ» 1 мин (занято)
        r.Auto = false;
        if (CancelDelay(r) is var (delay, kind))
        {
            r.ArtificialTimer = delay;
            r.CancelKind = kind;
            if (kind != "ОС")
            {
                CancelledBeforeTrain?.Invoke(r);
                Say($"Отмена маршрута {r.Name} с выдержкой ({kind}): на участке приближения {r.Start.Approach.Label} поезд. " +
                    $"Светофор {r.Start.Label} закрыт, маршрут разомкнётся через {delay:0} с", LogKind.Aux);
            }
            else Say($"Отмена маршрута {r.Name} (ОС): светофор {r.Start.Label} закрыт, маршрут разомкнётся через {delay:0} с", LogKind.Aux);
        }
        else
        {
            ReleaseRoute(r, $"Отменён маршрут {r.Name}");
        }
        Commit();
    }

    /// <summary>Полное размыкание маршрута (все участки и стрелки).</summary>
    public void ReleaseRoute(RailRoute r, string message)
    {
        foreach (var s in r.Sections)
            if (s.LockedBy == r) s.LockedBy = null;
        foreach (var (sw, _) in r.Switches) sw.LockedBy.Remove(r);
        r.ResetState();
        if (message != null) Say(message);
        Commit();
    }

    long setCounter;

    /// <summary>Отменить последний заданный маршрут (можно ограничить маршрутами одного окна).</summary>
    public void CancelLast(IEnumerable<RailRoute> among = null)
    {
        var last = (among ?? Routes).Where(r => r.Active && !r.TrainEntered && !r.Releasing)
                                    .OrderBy(r => r.SetOrder).LastOrDefault();
        if (last == null) { Say("Нет маршрутов, которые можно отменить", LogKind.Refuse); return; }
        CancelRoute(last);
    }

    /// <summary>Полный сброс для учебных целей ("Убрать всё").</summary>
    public void ResetAll()
    {
        foreach (var r in Routes.Where(x => x.Active).ToList()) ReleaseRoute(r, null);
        foreach (var s in Sections.Values) { s.ManualOcc = false; s.LockedBy = null; }
        foreach (var sig in Signals.Values) { sig.InviteTimer = 0; sig.InviteRoute = null; }
        foreach (var sw in Switches.Values) { sw.InviteLocks = 0; sw.LockedBy.Clear(); }
        foreach (var l in Lines) { l.AuxTimer = 0; l.SyncTimer = 0; }
        foreach (var c in Crossings) c.ManualClosed = false;
        PanelReset();                                      // [ДОБАВЛЕНО 21.10.2026, этап 6]
        UpdateCrossings();
        Say("Всё убрано: маршруты разомкнуты, ручная занятость снята");
        Commit();
    }

    // =================================================================
    //  ПЕРЕЕЗДЫ  [ИЗМЕНЕНО 20.10.2026: ручное управление кнопкой]
    // =================================================================

    /// <summary>Закрыть / открыть переезд кнопкой дежурного. Открыть нельзя, пока его держит автоматика
    /// (поезд на переезде) — тогда он откроется сам после проследования.</summary>
    public void SetCrossingManual(LevelCrossing c, bool closed)
    {
        if (c.ManualClosed == closed) return;
        c.ManualClosed = closed;
        Say(closed ? $"{c.Title}: закрыт дежурным (кнопка)" : $"{c.Title}: дежурный снял ручное закрытие", LogKind.Aux);
        if (!closed && (c.ShouldClose?.Invoke() ?? false))
            Say($"{c.Title}: открыть нельзя — поезд на переезде. Откроется сам после проследования", LogKind.Refuse);
        UpdateCrossings();
        Commit();
    }

    bool UpdateCrossings()
    {
        bool changed = false;
        foreach (var c in Crossings)
        {
            bool auto = c.ShouldClose?.Invoke() ?? false;
            bool close = CrossingWanted(c, auto);          // [ИЗМЕНЕНО 21.10.2026, этап 6] извещение — с выдержкой
            if (close != c.Closed)
            {
                c.Closed = close;
                Say(!close ? $"{c.Title}: открыт"
                    : auto ? $"{c.Title}: ЗАКРЫТ (поезд на переезде)"
                    : $"{c.Title}: ЗАКРЫТ");
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Может ли маршрут открыть светофор прямо сейчас.</summary>
    public bool CanOpen(RailRoute r)
    {
        if (!r.Active || r.TrainEntered || r.Releasing || r.SetupTimer > 0) return false;
        if (PreconditionOf(r) != null || !CrossingReady(r)) return false;   // [ИЗМЕНЕНО 21.10.2026, этап 6]
        foreach (var s in r.Sections)
            if (s.Occupied || s.LockedBy != r) return false;
        foreach (var (sw, plus) in r.Switches)
            if (!sw.Controlled || sw.ShownPlus != plus) return false;
        if (r.ExitSection != null && r.ExitSection.Occupied) return false;
        return true;
    }

    // =================================================================
    //  СТРЕЛКИ
    // =================================================================

    void StartMove(RailSwitch sw, bool plus)
    {
        sw.Plus = plus;
        sw.Controlled = false;
        sw.MoveTimer = SwitchMoveSeconds;
        // [ИЗМЕНЕНО 20.10.2026, этап 3] неисправность «не переводится в …»: привод на макете не трогаем,
        // команда уйдёт, когда неисправность устранят (см. Tick)
        sw.PendingCommand = !sw.FaultNoControl && sw.FaultBlocked;
        if (!sw.PendingCommand) SwitchCommand?.Invoke(sw);
    }

    /// <summary>Перевод стрелки вручную. aux = вспомогательный перевод (при ложной занятости).</summary>
    public bool ToggleSwitch(RailSwitch sw, bool aux = false)
    {
        string pref = aux ? "ВСП. перевод" : "Перевод";
        if (sw.Locked)
        {
            Say($"ОТКАЗ: стрелка {sw.Label} заперта (маршрут или пригласительный)", LogKind.Refuse);
            return false;
        }
        if (SwitchMoveBlock(sw) is { } block)              // [ДОБАВЛЕНО 21.10.2026, этап 6]
        {
            Say($"ОТКАЗ: стрелка {sw.Label} — {block}", LogKind.Refuse);
            return false;
        }
        // [ИЗМЕНЕНО 20.10.2026, этап 3] стрелку без контроля (не дошла) можно переводить, нельзя — пока идёт перевод
        if (sw.MoveTimer > 0)
        {
            Say($"ОТКАЗ: стрелка {sw.Label} ещё переводится", LogKind.Refuse);
            return false;
        }
        bool busy = sw.Sections.Any(s => s.Occupied);
        if (busy && !aux)
        {
            Say($"ОТКАЗ: стрелка {sw.Label} — стрелочный участок занят, перевод запрещён. " +
                "Если занятость ложная — вспомогательный перевод (ПКМ по стрелке)", LogKind.Refuse);
            return false;
        }
        if (aux)
        {
            if (!busy)
            {
                Say($"Стрелка {sw.Label}: участок свободен — вспомогательный перевод не нужен, переводите обычным порядком", LogKind.Refuse);
                return false;
            }
            if (sw.Sections.Any(s => s.TrainCount > 0))
            {
                Say($"ОТКАЗ ВСП: на стрелке {sw.Label} стоит поезд — переводить под составом запрещено!", LogKind.Refuse);
                return false;
            }
        }
        StartMove(sw, !sw.Plus);
        Say($"{pref} стрелки {sw.Label} → {(sw.Plus ? "плюс" : "минус")}", aux ? LogKind.Aux : LogKind.Info);
        if (aux) CountedCommand?.Invoke("ВК", "ВК", $"вспомогательный перевод стрелки {sw.Label} в {(sw.Plus ? "плюс" : "минус")}");
        Commit();
        return true;
    }

    // =================================================================
    //  ПРИГЛАСИТЕЛЬНЫЙ
    // =================================================================

    public bool SetInvite(RailSignal sig)
    {
        if (sig.Kind != SignalKind.Controlled || !sig.Has("white"))
        {
            Say($"У светофора {sig.Label} нет пригласительного огня", LogKind.Refuse);
            return false;
        }
        if (sig.InviteOn) { Say($"Пригласительный на {sig.Label} уже горит"); return false; }

        var own = ActiveRoutes.FirstOrDefault(r => r.Start == sig);
        if (own != null && own.SetupTimer > 0)
        {
            Say($"ОТКАЗ: маршрут {own.Name} ещё устанавливается", LogKind.Refuse);
            return false;
        }
        if (own != null && CanOpen(own) && !sig.FaultClosed)
        {
            Say($"ОТКАЗ: маршрут {own.Name} открывается обычным порядком — пригласительный не нужен", LogKind.Refuse);
            return false;
        }

        // пригласительный открывают, когда стрелки уже стоят по маршруту и есть контроль
        // [ИЗМЕНЕНО 21.10.2026, этап 6] стрелка без контроля, но запертая на закладку / скобу, годится (курбель, выключение)
        RailRoute path = Routes.FirstOrDefault(r => r.Start == sig &&
            r.Switches.All(x => x.sw.ShownPlus == x.plus && (x.sw.Controlled || x.sw.Clamp.Length > 0)) &&
            r.Sections.All(s => s.LockedBy == null || s.LockedBy == r));
        if (path == null)
        {
            Say($"ОТКАЗ: для пригласительного на {sig.Label} стрелки должны стоять по одному из маршрутов и иметь контроль", LogKind.Refuse);
            return false;
        }
        if (PreconditionOf(path) is string pre)
        {
            Say($"ОТКАЗ пригласительного {sig.Label}: {pre}", LogKind.Refuse);
            return false;
        }

        foreach (var (sw, _) in path.Switches) sw.InviteLocks++;
        sig.InviteRoute = path;
        sig.InviteTimer = InviteSeconds;
        Say($"ПРИГЛАСИТЕЛЬНЫЙ открыт на {sig.Label} по направлению «{path.Name}» (стрелки заперты, {InviteSeconds:0} с)", LogKind.Aux);
        CountedCommand?.Invoke("ПС:" + sig.Id, "ПС " + sig.Label, $"пригласительный сигнал на светофоре {sig.Label} ({path.Name})");
        Commit();
        return true;
    }

    public void CancelInvite(RailSignal sig, string reason)
    {
        if (!sig.InviteOn && sig.InviteRoute == null) return;
        if (sig.InviteRoute != null)
            foreach (var (sw, _) in sig.InviteRoute.Switches)
                sw.InviteLocks = Math.Max(0, sw.InviteLocks - 1);
        sig.InviteTimer = 0;
        sig.InviteRoute = null;
        Say($"Пригласительный на {sig.Label} погашен ({reason})");
        Commit();
    }

    // =================================================================
    //  НАПРАВЛЕНИЕ НА ОДНОПУТНОМ ПЕРЕГОНЕ
    // =================================================================

    /// <summary>[ДОБАВЛЕНО 12.10.2026] Связь диспетчеров: перегон общий с соседней станцией, сменить
    /// направление можно только с её согласия. Вызывается, когда НАШИ условия смены выполнены
    /// (line, новое направление, вспомогательная ли). true — запрос ушёл соседу, менять сейчас не надо:
    /// по его ответу будет вызван SetDirection или StartAuxDirection. false — меняем сами, как раньше.</summary>
    public Func<DirectionLine, Dir, bool, bool> DirectionGate;

    /// <summary>Почему сейчас нельзя менять направление у нас (null — можно). Для ответа на запрос соседа.</summary>
    public string DirectionBlocker(DirectionLine line, bool aux)
    {
        if (line.AuxRunning) return "идёт вспомогательная смена направления";
        if (line.Syncing) return "идёт другое согласование смены направления";
        if (line.FaultNoChange) return "направление не переключается";
        if (line.KeyOut) return "изъят ключ-жезл перегона";     // [ДОБАВЛЕНО 21.10.2026, этап 6]
        var busyRoute = line.DependentRoutes.FirstOrDefault(r => r.Active);
        if (busyRoute != null) return $"задан маршрут {busyRoute.Name}";
        if (aux)
        {
            if (line.Sections.Any(s => s.TrainCount > 0)) return "на перегоне поезд";
        }
        else if (line.Sections.Any(s => s.Occupied))
            return $"перегон занят ({string.Join(", ", line.Sections.Where(s => s.Occupied).Select(s => s.Label))})";
        return null;
    }

    /// <summary>Установить направление (по согласию или по данным соседней станции).</summary>
    public void SetDirection(DirectionLine line, Dir dir, string message, LogKind kind = LogKind.Info)
    {
        line.Current = dir;
        line.AuxTimer = 0;
        line.SyncTimer = 0;
        if (message != null) Say(message, kind);
        Commit();
    }

    /// <summary>Запустить выдержку вспомогательной смены (после согласия соседней станции).</summary>
    public void StartAuxDirection(DirectionLine line)
    {
        line.SyncTimer = 0;
        line.AuxTimer = AuxDirectionSeconds;
        Say($"ВСП смена направления {line.Title}: запущена выдержка {AuxDirectionSeconds:0} с", LogKind.Aux);
        Commit();
    }

    public bool ChangeDirection(DirectionLine line, bool aux = false)
    {
        var target = line.Current == Dir.Right ? Dir.Left : Dir.Right;

        if (line.AuxRunning) { Say($"{line.Title}: вспомогательная смена уже идёт", LogKind.Refuse); return false; }
        if (line.Syncing) { Say($"{line.Title}: идёт согласование смены направления с соседней станцией", LogKind.Refuse); return false; }
        if (line.KeyOut) { Say($"ОТКАЗ смены направления {line.Title}: изъят ключ-жезл", LogKind.Refuse); return false; }   // [этап 6]

        var busyRoute = line.DependentRoutes.FirstOrDefault(r => r.Active);
        if (busyRoute != null)
        {
            Say($"ОТКАЗ смены направления: задан маршрут {busyRoute.Name}", LogKind.Refuse);
            return false;
        }

        bool occupied = line.Sections.Any(s => s.Occupied);
        if (!aux)
        {
            if (occupied)
            {
                Say($"ОТКАЗ смены направления: перегон занят ({string.Join(", ", line.Sections.Where(s => s.Occupied).Select(s => s.Label))}). " +
                    "При ложной занятости — вспомогательная смена", LogKind.Refuse);
                return false;
            }
            if (line.FaultNoChange) { NoDirectionChange(line); return false; }
            if (DirectionGate?.Invoke(line, target, false) == true) return true;   // ждём согласия соседа
            line.Current = target;
            Say($"{line.Title}: направление изменено {line.DirText(target)}");
            Commit();
            return true;
        }

        if (!occupied)
        {
            Say($"{line.Title}: перегон свободен — меняйте направление обычным порядком", LogKind.Refuse);
            return false;
        }
        if (line.Sections.Any(s => s.TrainCount > 0))
        {
            Say("ОТКАЗ ВСП смены направления: на перегоне поезд!", LogKind.Refuse);
            return false;
        }
        CountedCommand?.Invoke("ВСН:" + line.Id, "ВСН", $"вспомогательная смена направления: {line.Title}");
        if (line.FaultNoChange) { NoDirectionChange(line); return false; }
        if (DirectionGate?.Invoke(line, target, true) == true) return true;    // ждём согласия соседа
        StartAuxDirection(line);
        return true;
    }

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправность: реле направления не переключилось.
    /// Дежурный видит только, что направление осталось прежним.</summary>
    void NoDirectionChange(DirectionLine line) =>
        Say($"{line.Title}: команда смены направления подана, направление не изменилось ({line.DirText(line.Current)})", LogKind.Refuse);

    // =================================================================
    //  ТАКТ (вызывается ~10 раз в секунду)
    // =================================================================

    public void Tick(double dt)
    {
        bool changed = false;

        // --- стрелки получают контроль ---
        // [ИЗМЕНЕНО 20.10.2026, этап 3] неисправность (Core/Faults.cs) не даёт контроля; после её устранения
        // несработавшая команда уходит на привод, и стрелка доводится обычным порядком
        foreach (var sw in Switches.Values)
        {
            if (sw.Controlled)
            {
                if (!LosesControl(sw)) continue;   // [ИЗМЕНЕНО 21.10.2026, этап 6] + заслонка, взрез, выключение
                sw.Controlled = false;          // потеря контроля без перевода
                changed = true;
                continue;
            }
            if (sw.MoveTimer > 0)
            {
                sw.MoveTimer -= dt;
                if (sw.MoveTimer > 0) continue;
                sw.MoveTimer = 0;
                changed = true;
            }
            if (ControlBlocked(sw)) continue;   // стрелка не дошла / нет контроля — ждёт устранения (или пульт: заслонка, взрез…)
            if (sw.PendingCommand)
            {
                sw.PendingCommand = false;
                sw.MoveTimer = SwitchMoveSeconds;
                SwitchCommand?.Invoke(sw);
                continue;
            }
            sw.Controlled = true;
            Say($"Стрелка {sw.Label} получила контроль в {(sw.Plus ? "плюсе" : "минусе")}");
            changed = true;
        }

        // --- [ДОБАВЛЕНО 21.10.2026, этап 6] ИР, переезд, указания работникам ---
        if (PanelTick(dt)) changed = true;

        // --- [ДОБАВЛЕНО 20.10.2026] установка маршрута: выдержка до открытия светофора ---
        foreach (var r in Routes)
        {
            if (r.SetupTimer <= 0) continue;
            r.SetupTimer -= dt;
            if (r.SetupTimer <= 0) { r.SetupTimer = 0; changed = true; }
        }

        // --- продвижение поезда по маршрутам: перекрытие и посекционное размыкание ---
        foreach (var r in Routes.Where(x => x.Active).ToList())
        {
            // искусственная разделка
            if (r.Releasing)
            {
                r.ArtificialTimer -= dt;
                if (r.ArtificialTimer <= 0)
                {
                    ReleaseRoute(r, $"Выдержка {r.CancelKind} ({CancelKindText(r.CancelKind)}) {r.Name} завершена — маршрут разомкнут");
                    r.CancelKind = "";
                    changed = true;
                }
                continue;
            }

            // Пока светофор ни разу не открывался, занятость участка — это не "поезд в маршруте",
            // а просто занятость: маршрут можно спокойно отменить.
            if (!r.SignalWasOpen) continue;

            for (int i = 0; i < r.Sections.Count; i++)
            {
                var s = r.Sections[i];
                // занятость засчитываем только по порядку: 1-й участок, потом 2-й и т.д.
                if (s.Occupied && !r.WasOccupied.Contains(s) && (i == 0 || r.WasOccupied.Contains(r.Sections[i - 1])))
                {
                    r.WasOccupied.Add(s);
                    if (!r.TrainEntered)
                    {
                        r.TrainEntered = true;
                        Say($"Поезд вступил в маршрут {r.Name} — светофор {r.Start.Label} перекрыт на красный");
                    }
                    changed = true;
                }
            }

            // По блок-схеме (лист 2): светофор был открыт, поезд ещё не вступил, а занялся НЕ первый
            // участок маршрута (например, ложная занятость) → светофор красный, маршрут разбирается.
            if (!r.TrainEntered)
            {
                var busy = r.Sections.Skip(1).FirstOrDefault(x => x.Occupied);
                if (busy != null)
                {
                    ReleaseRoute(r, null);
                    Say($"Маршрут {r.Name}: занят участок {busy.Label} при открытом светофоре — светофор закрыт, " +
                        "маршрут разобран. Устраните занятость и задайте маршрут заново", LogKind.Refuse);
                    changed = true;
                    continue;
                }
            }

            for (int i = 0; i < r.Sections.Count; i++)
            {
                var s = r.Sections[i];
                if (r.Released.Contains(s)) continue;
                bool prevReleased = i == 0 || r.Released.Contains(r.Sections[i - 1]);
                if (r.WasOccupied.Contains(s) && !s.Occupied && prevReleased)
                {
                    r.Released.Add(s);
                    if (s.LockedBy == r) s.LockedBy = null;
                    Say($"Посекционное размыкание: участок {s.Label} ({r.Name})");
                    foreach (var (sw, _) in r.Switches)
                    {
                        var own = sw.Sections.Where(x => r.Sections.Contains(x)).ToList();
                        if (own.All(x => r.Released.Contains(x))) sw.LockedBy.Remove(r);
                    }
                    changed = true;
                }
                else break;   // размыкание строго по порядку
            }

            if (r.Released.Count == r.Sections.Count)
            {
                if (!r.Shunting) TrainPassed?.Invoke(r);
                ReleaseRoute(r, $"Маршрут {r.Name} разомкнулся за поездом");
                AfterTrainRelease(r);                      // [ДОБАВЛЕНО 21.10.2026, этап 6] автодействие, ключ-жезл
                changed = true;
            }
            else if (!r.Shunting && r.Dest != null && r.Sections.Count > 0 && r.Sections[^1] == r.Dest &&
                     r.Released.Count == r.Sections.Count - 1 && r.WasOccupied.Contains(r.Dest))
            {
                // поезд целиком прибыл на путь назначения и стоит на нём — маршрут приёма выполнен
                TrainPassed?.Invoke(r);
                ReleaseRoute(r, $"Поезд принят на {r.Dest.Label} — маршрут {r.Name} разомкнут");
                AfterTrainRelease(r);                      // [ДОБАВЛЕНО 21.10.2026, этап 6]
                changed = true;
            }
        }

        // --- пригласительные ---
        foreach (var sig in Signals.Values)
        {
            if (!sig.InviteOn) continue;
            sig.InviteTimer -= dt;
            var first = sig.InviteRoute?.Sections.FirstOrDefault();
            if (first != null && first.TrainCount > 0)
            {
                CancelInvite(sig, "поезд проследовал светофор");
                changed = true;
            }
            else if (sig.InviteTimer <= 0)
            {
                CancelInvite(sig, "истекло время");
                changed = true;
            }
        }

        // --- вспомогательная смена направления ---
        foreach (var line in Lines)
        {
            // согласование с соседней станцией не закончилось вовремя
            if (line.Syncing)
            {
                line.SyncTimer -= dt;
                if (line.SyncTimer <= 0)
                {
                    line.SyncTimer = 0;
                    if (line.SyncTimeoutText.Length > 0) Say(line.SyncTimeoutText, LogKind.Refuse);
                    changed = true;
                }
            }

            if (!line.AuxRunning) continue;
            line.AuxTimer -= dt;
            if (line.AuxTimer <= 0)
            {
                line.AuxTimer = 0;
                line.Current = line.Current == Dir.Right ? Dir.Left : Dir.Right;
                Say($"ВСП смена направления {line.Title} выполнена: {line.DirText(line.Current)}", LogKind.Aux);
                changed = true;
            }
        }

        // --- переезды ---
        if (UpdateCrossings()) changed = true;

        // --- показания светофоров ---
        if (ComputeAspects()) changed = true;

        if (changed) Changed?.Invoke();
    }

    /// <summary>Пересчитать показания всех светофоров. true — что-то поменялось.</summary>
    public bool ComputeAspects()
    {
        var before = Signals.Values.ToDictionary(s => s, s => s.Aspect);

        // несколько проходов: показание зависит от следующего светофора
        for (int pass = 0; pass < 6; pass++)
        {
            foreach (var s in Signals.Values)
                s.Aspect = Compute(s);
        }

        bool changed = false;
        foreach (var s in Signals.Values)
            if (before[s] != s.Aspect) changed = true;
        return changed;
    }

    /// <summary>Запрещающее показание: у маневровых с синей лампой — синий, у остальных — красный.</summary>
    static Aspect ClosedAspect(RailSignal s) => s.Has("red") ? Aspect.Red : s.Has("blue") ? Aspect.Blue : Aspect.Red;

    Aspect Compute(RailSignal s)
    {
        // светофор, которым управляет другая станция (связь диспетчеров), — только показываем
        if (s.Remote is Aspect remote) return remote;

        switch (s.Kind)
        {
            case SignalKind.Neighbor:
                return s.NeighborOpen ? Aspect.Yellow : Aspect.Red;

            case SignalKind.Automatic:
            {
                if (s.IsActive != null && !s.IsActive()) return Aspect.Dark;
                if (s.FaultClosed) return Aspect.Red;          // [ДОБАВЛЕНО 20.10.2026, этап 3] неисправность
                if (s.Protects != null && s.Protects.Occupied) return Aspect.Red;
                if (s.Next == null) return Aspect.Yellow;
                var n = s.Next.Aspect;
                if (AspectInfo.IsRestrictive(n)) return Aspect.Yellow;
                if (n is Aspect.TwoYellow or Aspect.TwoYellowTopBlink) return Aspect.YellowBlink;
                return Aspect.Green;
            }

            case SignalKind.Controlled:
            {
                if (s.InviteOn) return Aspect.Invite;
                var r = ActiveRoutes.FirstOrDefault(x => x.Start == s);
                // [ИЗМЕНЕНО 20.10.2026, этап 3] неисправный светофор не открывается (пригласительный — можно)
                if (r == null || !CanOpen(r) || s.FaultClosed) return ClosedAspect(s);

                r.SignalWasOpen = true;
                if (r.Shunting) return s.Has("white") ? Aspect.White : ClosedAspect(s);
                bool nextClosed = r.End == null || AspectInfo.IsRestrictive(r.End.Aspect);
                Aspect a;
                if (r.Diverging) a = nextClosed ? Aspect.TwoYellow : Aspect.TwoYellowTopBlink;
                else a = nextClosed ? Aspect.Yellow : Aspect.Green;

                // если какой-то лампы физически нет — показываем ближайшее более строгое
                if (a == Aspect.Green && !s.Has("green")) a = Aspect.Yellow;
                if ((a == Aspect.TwoYellow || a == Aspect.TwoYellowTopBlink) && !s.Has("yellow1")) a = Aspect.Yellow;
                return a;
            }
        }
        return Aspect.Red;
    }
}
