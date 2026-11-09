using System.Text.Json;

namespace StationApp.Core;

// =====================================================================
//  СВЯЗЬ ДИСПЕТЧЕРОВ — что отдаём соседу и что берём у него
//  [ДОБАВЛЕНО 12.10.2026] Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//
//  СВЕТОФОРЫ (по ТЗ): НА и 1 — светофоры АКЖТ (пост), Алгабас их только показывает;
//                     Ч и 2 — светофоры Алгабаса, АКЖТ их только показывает.
//    [ИЗМЕНЕНО 28.10.2026] светофоры соседа — ТОЛЬКО ЧТЕНИЕ, имитации галочками больше нет:
//    Нет данных (связь выключена, нет сервера, соседа нет) → светофоры соседа ПОГАШЕНЫ (было: имитация / красный).
//    Сосед на связи       → показание ровно то, что прислал сосед.
//
//  НАПРАВЛЕНИЕ ОДНОПУТНОГО ПЕРЕГОНА — общее для двух станций:
//    • Дежурный нажал «Сменить направление» → наши условия проверены → запрос соседу (dir_request).
//      Сосед проверяет свои условия (нет маршрутов на перегон, перегон свободен), меняет направление
//      у себя и отвечает «да» (dir_reply ok) — тогда меняем и мы. Или отвечает отказом с причиной.
//      Пока ждём ответа (до RequestSeconds) — маршруты на перегон не задаются.
//    • Вспомогательная смена: сосед даёт согласие, выдержку ведёт тот, кто начал; сосед на это время
//      блокирует маршруты на перегон и принимает направление, когда выдержка закончится.
//    • У каждой станции у направления есть версия (растёт при каждой смене). Если направления
//      разошлись (программу перезапустили, меняли без связи) — берётся более новое; при равных
//      версиях — направление станции с меньшим именем ("akzht"). Если у нас задан маршрут на
//      перегон — чужое направление принимается только после его разборки.
//
//  [ДОБАВЛЕНО 12.10.2026, проблемы.txt]
//  ЗАНЯТОСТЬ ОБЩИХ УЧАСТКОВ (НАУ, Б12): каждая станция отправляет, как их видит сама (поезда, датчики,
//    вручную), и показывает занятость соседа (RemoteOcc) — маршрут/поезд соседа на перегоне виден у обеих.
//  МАРШРУТ НА ПЕРЕГОН: поездной маршрут отправления (выход на общий участок) подсвечивает весь перегон
//    до входного светофора соседа — у себя (LineRouteOwn) и у соседа (LineRouteRemote, поле "routes").
//  ПОЕЗДА: поезд, ушедший со схемы на соседнюю станцию с общего участка, появляется у соседа на том же
//    участке с тем же номером и едет дальше по светофорам соседа.
//  СОГЛАСИЕ ДЕЖУРНОГО: запрос смены направления от соседа показывается окном «Дать согласие / Отказать»
//    (AskConsent). Ждём ответа до RequestSeconds; дежурный не ответил — отказ.
//
//  Сама карта (какой светофор / перегон / участок как называется в сети) — в ModelHost каждой программы.
// =====================================================================

public class DispatchMirror
{
    readonly Interlocking il;
    readonly (string wire, RailSignal sig)[] own, mirror;
    readonly List<SyncLine> lines = new();

    /// <summary>Имя соседней станции на сервере ("algabas") и подпись для людей ("Алгабас").</summary>
    public readonly string PeerId, PeerName;

    /// <summary>Сколько ждать ответа соседа на запрос смены направления (там отвечает дежурный — нужно время).</summary>
    public double RequestSeconds = 60;

    /// <summary>[ДОБАВЛЕНО 12.10.2026] Спросить дежурного, согласен ли он на смену направления по запросу соседа
    /// (окно «Дать согласие / Отказать»). Задаёт окно программы. null — согласие даётся автоматически
    /// (если наши условия выполнены).</summary>
    public Action<ConsentRequest> AskConsent;

    readonly (string wire, TrackSection sec)[] sections;
    readonly TrainSim trains;

    enum Mode { Off, NoData, Remote }
    Mode mode = Mode.Off;
    readonly Dictionary<string, string> snapshot = new();
    readonly Dictionary<string, (Dir dir, long ver)> snapshotLines = new();
    readonly Dictionary<string, bool> snapshotSections = new();
    readonly Dictionary<string, string> snapshotRoutes = new();
    int requestCounter;

    /// <summary>Запрос соседа, на который мы сейчас отвечаем (дежурный думает).</summary>
    Incoming incoming;

    class Incoming
    {
        public int Id;
        public SyncLine Line;
        public Dir Target;
        public bool Aux;
        public long Ver;
        public ConsentRequest Request;
    }

    /// <summary>Общий с соседом перегон и его состояние синхронизации.</summary>
    class SyncLine
    {
        public string Wire;
        public DirectionLine Line;
        public Dir LastDir;          // направление, которое мы уже учли (чтобы заметить смену у себя)
        public long Ver;             // версия нашего направления
        public int PendingId;        // > 0 — ждём ответа соседа на наш запрос
        public Dir PendingDir;
        public bool PendingAux;
        public long PendingVer;
        public bool ConflictWarned;
    }

    /// <param name="own">свои светофоры: имя в сети → Id светофора в этой программе</param>
    /// <param name="mirror">светофоры соседа: имя в сети → Id светофора-«копии» в этой программе</param>
    /// <param name="sharedLines">общие с соседом однопутные перегоны: имя в сети → перегон в этой программе</param>
    /// <param name="sharedSections">общие с соседом участки (их занятость дублируется): имя в сети → Id участка</param>
    public DispatchMirror(Interlocking il, TrainSim trains, string peerId, string peerName,
                          (string wire, string local)[] own, (string wire, string local)[] mirror,
                          (string wire, DirectionLine line)[] sharedLines, (string wire, string local)[] sharedSections)
    {
        this.il = il;
        this.trains = trains;
        PeerId = peerId;
        PeerName = peerName;
        this.own = own.Select(x => (x.wire, Find(x.local))).ToArray();
        this.mirror = mirror.Select(x => (x.wire, Find(x.local))).ToArray();
        foreach (var (wire, line) in sharedLines)
            lines.Add(new SyncLine { Wire = wire, Line = line, LastDir = line.Current });
        sections = sharedSections.Select(x => (x.wire, FindSection(x.local))).ToArray();

        il.DirectionGate = Gate;
        DispatchLink.EventReceived += OnEvent;
        trains.Exited += OnTrainExited;
    }

    RailSignal Find(string id) => il.Signals.TryGetValue(id, out var s) ? s
        : throw new ArgumentException($"Связь диспетчеров: светофора «{id}» нет в схеме");

    TrackSection FindSection(string id) => il.Sections.TryGetValue(id, out var s) ? s
        : throw new ArgumentException($"Связь диспетчеров: участка «{id}» нет в схеме");

    /// <summary>Подписи светофоров соседа для журнала: "Ч и 2".</summary>
    string MirrorNames => string.Join(" и ", mirror.Select(m => m.sig.Label));

    static string Arrow(DirectionLine l, Dir d) => d == Dir.Right ? "→ " + l.RightName : "← " + l.LeftName;

    SyncLine LineByWire(string wire) => lines.FirstOrDefault(x => x.Wire == wire);

    (Dir dir, long ver)? PeerLine(SyncLine sl) =>
        DispatchLink.Peer(PeerId) is { } p && p.Lines.TryGetValue(sl.Wire, out var pl) ? pl : null;

    // =================================================================
    //  ТАКТ (10 раз в секунду, из ModelHost)
    // =================================================================

    public void Tick()
    {
        foreach (var sl in lines)
        {
            // направление сменилось у нас без согласования (связь выключена, вспомогательная выдержка
            // закончилась и т.п.) — новая версия, сосед её примет
            if (sl.Line.Current != sl.LastDir)
            {
                sl.Ver = Math.Max(sl.Ver, PeerLine(sl)?.ver ?? 0) + 1;
                sl.LastDir = sl.Line.Current;
            }
            // ответ на наш запрос так и не пришёл (сообщение в журнал пишет Interlocking) —
            // сообщаем соседу, чтобы у его дежурного закрылось окно
            if (sl.PendingId != 0 && !sl.Line.Syncing)
            {
                DispatchLink.SendEvent(PeerId, new { kind = "dir_cancel", id = sl.PendingId });
                sl.PendingId = 0;
            }
        }
        // окно у нашего дежурного висит дольше, чем ждёт сосед (не должно быть, но на всякий случай)
        if (incoming != null && !incoming.Line.Line.Syncing)
        {
            var inc = incoming;
            inc.Request?.Cancel();                       // закрыть окно
            Respond(inc, false, "дежурный не ответил");  // и ответить соседу отказом
        }
        UpdateOwnLineRoutes();
        Reconcile();
        Publish();
    }

    /// <summary>[ДОБАВЛЕНО 12.10.2026] Наш поездной маршрут на перегон (отправление: выход на общий участок)
    /// продолжается по всему перегону до входного светофора соседа — подсвечиваем НАУ и Б12 у себя
    /// и отправляем соседу, чтобы у него был виден весь маршрут, а не только занятость.</summary>
    void UpdateOwnLineRoutes()
    {
        bool onLine = il.Routes.Any(r => r.Active && !r.Shunting && r.ExitSection != null &&
                                         sections.Any(x => x.sec == r.ExitSection));
        bool changed = false;
        foreach (var (_, sec) in sections)
            if (sec.LineRouteOwn != onLine)
            {
                sec.LineRouteOwn = onLine;
                changed = true;
            }
        if (changed) il.NotifyChanged();
    }

    /// <summary>Отправить показания своих светофоров и направления (сеть отправит, только если они изменились).</summary>
    void Publish()
    {
        if (!DispatchLink.Enabled) return;
        snapshot.Clear();
        foreach (var (wire, sig) in own) snapshot[wire] = sig.Aspect.ToString();
        snapshotLines.Clear();
        foreach (var sl in lines) snapshotLines[sl.Wire] = (sl.Line.Current, sl.Ver);
        snapshotSections.Clear();
        foreach (var (wire, sec) in sections) snapshotSections[wire] = sec.LocalOccupied;   // без занятости от соседа
        snapshotRoutes.Clear();
        foreach (var (wire, sec) in sections) if (sec.LineRouteOwn) snapshotRoutes[wire] = "train";
        DispatchLink.Publish(snapshot, snapshotLines, snapshotSections, snapshotRoutes);
    }

    // =================================================================
    //  ПОЕЗДА: уход на соседнюю станцию и приём от неё
    // =================================================================

    void OnTrainExited(VirtualTrain t, TrackSection from, TrackStep step)
    {
        var sec = sections.FirstOrDefault(x => x.sec == from);
        if (sec.wire == null || !DispatchLink.Enabled || DispatchLink.Peer(PeerId) == null) return;
        DispatchLink.SendEvent(PeerId, new { kind = "train", section = sec.wire, dir = t.Dir.ToString(), number = t.Number,
                                             passport = Radio.Passport(t) });   // [ДОБАВЛЕНО 23.10.2026, этап 4] категория, индекс, отметки
        il.Say($"Поезд №{t.Number} передан станции {PeerName}", LogKind.Train);
    }

    void OnTrain(JsonElement d)
    {
        var sec = sections.FirstOrDefault(x => x.wire == Str(d, "section"));
        if (sec.wire == null || !Enum.TryParse<Dir>(Str(d, "dir"), out var dir)) return;
        int number = d.TryGetProperty("number", out var n) && n.TryGetInt32(out var nn) ? nn : 0;
        var t = trains.SpawnFromNeighbor(sec.sec, dir, number, PeerName);
        if (d.TryGetProperty("passport", out var pp)) Radio.ApplyPassport(t, pp);   // [ДОБАВЛЕНО 23.10.2026, этап 4]
    }

    /// <summary>Направления разошлись — принять более новое направление соседа.</summary>
    void Reconcile()
    {
        if (!DispatchLink.Enabled) return;
        foreach (var sl in lines)
        {
            var pl = PeerLine(sl);
            if (pl == null) continue;
            var (pDir, pVer) = pl.Value;
            if (pDir == sl.Line.Current)
            {
                if (pVer > sl.Ver) sl.Ver = pVer;
                sl.ConflictWarned = false;
                continue;
            }
            bool peerNewer = pVer > sl.Ver || (pVer == sl.Ver && string.CompareOrdinal(PeerId, DispatchLink.StationId) < 0);
            if (!peerNewer) continue;                         // у нас новее — сосед примет наше
            if (sl.PendingId != 0 || sl.Line.AuxRunning) continue;   // идёт наша смена — ждём её окончания

            var busy = sl.Line.DependentRoutes.FirstOrDefault(r => r.Active);
            if (busy != null)
            {
                if (!sl.ConflictWarned)
                    il.Say($"{sl.Line.Title}: направление не совпадает со станцией {PeerName} (у неё {Arrow(sl.Line, pDir)}), " +
                           $"но у нас задан маршрут {busy.Name} — направление будет принято после его разборки", LogKind.Refuse);
                sl.ConflictWarned = true;
                continue;
            }
            il.SetDirection(sl.Line, pDir, $"{sl.Line.Title}: направление {Arrow(sl.Line, pDir)} — по данным станции {PeerName}");
            sl.LastDir = pDir;
            sl.Ver = pVer;
            sl.ConflictWarned = false;
        }
    }

    // =================================================================
    //  СМЕНА НАПРАВЛЕНИЯ: запрос соседу и его ответ
    // =================================================================

    /// <summary>Interlocking.DirectionGate: наши условия выполнены — спросить соседа.</summary>
    bool Gate(DirectionLine line, Dir target, bool aux)
    {
        var sl = lines.FirstOrDefault(x => x.Line == line);
        if (sl == null || !DispatchLink.Enabled) return false;       // связь выключена — как раньше
        if (DispatchLink.Peer(PeerId) == null)
        {
            il.Say($"{line.Title}: станции {PeerName} нет на связи — направление меняется только у нас, " +
                   "согласуется при её подключении", LogKind.Aux);
            return false;
        }

        sl.PendingId = ++requestCounter;
        sl.PendingDir = target;
        sl.PendingAux = aux;
        sl.PendingVer = Math.Max(sl.Ver, PeerLine(sl)?.ver ?? 0) + 1;
        DispatchLink.SendEvent(PeerId, new
        {
            kind = "dir_request", line = sl.Wire, dir = target.ToString(), aux, ver = sl.PendingVer, id = sl.PendingId,
            timeout = RequestSeconds
        });
        line.SyncTimer = RequestSeconds;
        line.SyncTimeoutText = $"{line.Title}: дежурный станции {PeerName} не ответил — направление не изменено";
        il.Say($"{line.Title}: {(aux ? "ВСП смена" : "смена")} направления {Arrow(line, target)} — запрос станции {PeerName}, " +
               $"ждём согласия дежурного (до {RequestSeconds:0} с)", aux ? LogKind.Aux : LogKind.Info);
        il.NotifyChanged();
        return true;
    }

    void OnEvent(string from, JsonElement data)
    {
        if (from != PeerId) return;
        switch (Str(data, "kind"))
        {
            case "dir_request": OnRequest(data); break;
            case "dir_reply": OnReply(data); break;
            case "dir_cancel": OnCancel(data); break;
            case "train": OnTrain(data); break;
        }
    }

    /// <summary>Сосед просит сменить направление: наши условия → окно дежурному → ответ.</summary>
    void OnRequest(JsonElement d)
    {
        var sl = LineByWire(Str(d, "line"));
        if (sl == null || !Enum.TryParse<Dir>(Str(d, "dir"), out var target)) return;
        bool aux = d.TryGetProperty("aux", out var a) && a.ValueKind == JsonValueKind.True;
        long ver = d.TryGetProperty("ver", out var v) && v.TryGetInt64(out var vv) ? vv : 0;
        int id = d.TryGetProperty("id", out var i) && i.TryGetInt32(out var ii) ? ii : 0;
        double timeout = d.TryGetProperty("timeout", out var t) && t.TryGetDouble(out var tt) ? tt : 5;
        var line = sl.Line;

        // у нас уже такое направление — согласны, спрашивать дежурного не о чем
        if (target == line.Current)
        {
            Reply(id, true, "");
            return;
        }
        string blocker = sl.PendingId != 0 ? "у нас самих идёт смена направления"
                       : incoming != null ? "дежурный отвечает на другой запрос"
                       : il.DirectionBlocker(line, aux);
        if (blocker != null)
        {
            Refuse(sl, id, target, blocker);
            return;
        }

        var inc = new Incoming { Id = id, Line = sl, Target = target, Aux = aux, Ver = ver };
        if (AskConsent == null)
        {
            Accept(inc);
            return;
        }

        // ответить надо раньше, чем у соседа кончится ожидание
        double seconds = Math.Max(3, timeout - 3);
        incoming = inc;
        line.SyncTimer = seconds + 2;            // пока дежурный думает — маршруты на перегон не задаются
        line.SyncTimeoutText = "";
        inc.Request = new ConsentRequest
        {
            Title = aux ? "Вспомогательная смена направления" : "Смена направления",
            Text = $"Станция {PeerName} просит {(aux ? "ВСПОМОГАТЕЛЬНУЮ смену" : "сменить")} направление\n" +
                   $"на перегоне «{line.Title}»:\n\n{Arrow(line, line.Current)}   ⇒   {Arrow(line, target)}" +
                   (aux ? "\n\nВспомогательная смена — только при ложной занятости перегона.\nУбедитесь, что перегон свободен от поездов."
                        : "\n\nПока вы решаете, маршруты на перегон не задаются."),
            Seconds = seconds,
            Answer = (ok, reason) => Respond(inc, ok, reason),
        };
        il.Say($"{line.Title}: станция {PeerName} просит сменить направление на {Arrow(line, target)} — ждём решения дежурного", LogKind.Aux);
        il.NotifyChanged();
        AskConsent(inc.Request);
    }

    /// <summary>Дежурный ответил в окне (или окно закрылось по времени).</summary>
    void Respond(Incoming inc, bool ok, string reason)
    {
        if (incoming != inc) return;             // запрос уже отменён
        incoming = null;
        inc.Line.Line.SyncTimer = 0;
        if (!ok)
        {
            Refuse(inc.Line, inc.Id, inc.Target, string.IsNullOrEmpty(reason) ? "дежурный отказал" : reason);
            il.NotifyChanged();
            return;
        }
        // пока дежурный думал, обстановка могла измениться — проверяем ещё раз
        var blocker = inc.Line.PendingId != 0 ? "у нас самих идёт смена направления" : il.DirectionBlocker(inc.Line.Line, inc.Aux);
        if (blocker != null)
        {
            Refuse(inc.Line, inc.Id, inc.Target, blocker);
            il.NotifyChanged();
            return;
        }
        Accept(inc);
    }

    void Refuse(SyncLine sl, int id, Dir target, string reason)
    {
        Reply(id, false, reason);
        il.Say($"{sl.Line.Title}: станция {PeerName} просит сменить направление на {Arrow(sl.Line, target)} — ОТКАЗ: {reason}", LogKind.Refuse);
    }

    void Reply(int id, bool ok, string reason) =>
        DispatchLink.SendEvent(PeerId, new { kind = "dir_reply", id, ok, reason });

    /// <summary>Согласие: меняем направление у себя (для ВСП — ждём окончания выдержки у соседа) и отвечаем.</summary>
    void Accept(Incoming inc)
    {
        var sl = inc.Line;
        var line = sl.Line;
        if (!inc.Aux)
        {
            il.SetDirection(line, inc.Target, $"{line.Title}: направление изменено {Arrow(line, inc.Target)} по запросу станции {PeerName}");
            sl.LastDir = inc.Target;
            sl.Ver = Math.Max(sl.Ver, inc.Ver);
        }
        else
        {
            // вспомогательная: выдержку ведёт сосед, мы на это время не задаём маршруты на перегон
            line.SyncTimer = il.AuxDirectionSeconds + 10;
            line.SyncTimeoutText = $"{line.Title}: вспомогательная смена направления у станции {PeerName} не завершилась";
            il.Say($"{line.Title}: станция {PeerName} начинает ВСП смену направления на {Arrow(line, inc.Target)} — " +
                   "маршруты на перегон заблокированы до её окончания", LogKind.Aux);
            il.NotifyChanged();
        }
        Reply(inc.Id, true, "");
    }

    /// <summary>Сосед больше не ждёт ответа — закрыть окно дежурного.</summary>
    void OnCancel(JsonElement d)
    {
        int id = d.TryGetProperty("id", out var i) && i.TryGetInt32(out var ii) ? ii : 0;
        if (incoming == null || incoming.Id != id) return;
        DropIncoming($"{incoming.Line.Line.Title}: станция {PeerName} отменила запрос смены направления");
    }

    void DropIncoming(string message)
    {
        var inc = incoming;
        if (inc == null) return;
        incoming = null;
        inc.Line.Line.SyncTimer = 0;
        inc.Request?.Cancel();
        il.Say(message, LogKind.Refuse);
        il.NotifyChanged();
    }

    /// <summary>Сосед ответил на наш запрос.</summary>
    void OnReply(JsonElement d)
    {
        int id = d.TryGetProperty("id", out var i) && i.TryGetInt32(out var ii) ? ii : 0;
        var sl = lines.FirstOrDefault(x => x.PendingId != 0 && x.PendingId == id);
        if (sl == null) return;                      // опоздавший ответ — уже не ждём
        sl.PendingId = 0;
        var line = sl.Line;
        line.SyncTimer = 0;

        bool ok = d.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
        if (!ok)
        {
            il.Say($"ОТКАЗ смены направления {line.Title}: станция {PeerName} — {Str(d, "reason")}", LogKind.Refuse);
            il.NotifyChanged();
            return;
        }
        if (sl.PendingAux)
        {
            il.StartAuxDirection(line);              // по окончании выдержки Tick поднимет версию, сосед примет
            return;
        }
        il.SetDirection(line, sl.PendingDir, $"{line.Title}: направление изменено {Arrow(line, sl.PendingDir)} (станция {PeerName} согласна)");
        sl.LastDir = sl.PendingDir;
        sl.Ver = Math.Max(sl.Ver, sl.PendingVer);
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

    // =================================================================
    //  СВЕТОФОРЫ СОСЕДА
    // =================================================================

    /// <summary>Перенести показания соседа на светофоры-копии. Вызывать при любом изменении связи.</summary>
    public void Apply()
    {
        var peer = DispatchLink.Enabled ? DispatchLink.Peer(PeerId) : null;
        bool changed = false;
        bool allKnown = true;

        foreach (var (wire, sig) in mirror)
        {
            // [ИЗМЕНЕНО 28.10.2026] нет данных от соседа — светофор погашен (раньше: красный, а без связи — имитация)
            Aspect v = Aspect.Dark;
            if (peer != null && peer.Signals.TryGetValue(wire, out var text) &&
                Enum.TryParse<Aspect>(text, out var a) && Enum.IsDefined(a))
                v = a;
            else
                allKnown = false;
            if (sig.Remote != v)
            {
                sig.Remote = v;
                changed = true;
            }
        }

        // занятость общих участков по данным соседа (нет соседа — его занятости не показываем)
        foreach (var (wire, sec) in sections)
        {
            bool occ = peer != null && peer.Sections.TryGetValue(wire, out var o) && o;
            if (sec.RemoteOcc != occ)
            {
                sec.RemoteOcc = occ;
                changed = true;
            }
            // маршрут соседа на перегон — подсвечиваем у себя
            bool route = peer != null && peer.Routes.ContainsKey(wire);
            if (sec.LineRouteRemote != route)
            {
                sec.LineRouteRemote = route;
                changed = true;
            }
        }

        // сосед пропал, пока наш дежурный решал, — окно больше не нужно
        if (peer == null && incoming != null)
            DropIncoming($"{incoming.Line.Line.Title}: станция {PeerName} отключилась — запрос смены направления отменён");

        var newMode = !DispatchLink.Enabled ? Mode.Off : allKnown ? Mode.Remote : Mode.NoData;
        if (newMode != mode)
        {
            il.Say(newMode switch
            {
                Mode.Remote => $"Связь: светофоры {MirrorNames} показываются по данным станции {PeerName}",
                Mode.NoData => $"Связь: нет данных от станции {PeerName} — светофоры {MirrorNames} погашены",
                _ => $"Связь выключена: светофоры {MirrorNames} погашены (их показание задаёт станция {PeerName})"
            }, newMode == Mode.NoData ? LogKind.Refuse : LogKind.Info);
            mode = newMode;
        }

        Reconcile();
        if (changed)
        {
            il.ComputeAspects();
            il.NotifyChanged();
        }
    }

    /// <summary>Направление какого-нибудь общего перегона сейчас не совпадает с соседом.</summary>
    bool DirectionMismatch => lines.Any(sl => PeerLine(sl) is { } pl && pl.dir != sl.Line.Current);

    /// <summary>Строка для верхней панели окна.</summary>
    public string StatusText =>
        !DispatchLink.Enabled ? "Связь: выключена"
        : !DispatchLink.IsConnected ? "Связь: нет сервера"
        : mode != Mode.Remote ? $"Связь: сервер есть, {PeerName} нет"
        : DirectionMismatch ? $"Связь: {PeerName} на связи, направление НЕ совпадает"
        : $"Связь: {PeerName} на связи";

    /// <summary>0 — выключена (серый), 1 — проблема (красный), 2 — всё хорошо (зелёный).</summary>
    public int StatusLevel => !DispatchLink.Enabled ? 0 : mode == Mode.Remote && !DirectionMismatch ? 2 : 1;
}

/// <summary>[ДОБАВЛЕНО 12.10.2026] Вопрос дежурному: дать согласие соседней станции на смену направления?
/// Окно программы показывает Text, ждёт не дольше Seconds и вызывает Answer ровно один раз.</summary>
public class ConsentRequest
{
    public string Title = "";
    public string Text = "";
    public double Seconds;
    /// <summary>Ответ дежурного: согласен ли, и причина отказа (пусто — «дежурный отказал»).</summary>
    public Action<bool, string> Answer;
    /// <summary>Запрос больше не действителен (сосед отменил, отключился) — окно надо закрыть без ответа.</summary>
    public event Action Cancelled;
    public bool IsCancelled { get; private set; }

    internal void Cancel()
    {
        IsCancelled = true;
        Cancelled?.Invoke();
    }
}
