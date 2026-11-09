using StationApp.Core;
using StationApp.Ui;

namespace StationApp.Algabas;

// =====================================================================
//  СХЕМА СТАНЦИИ АЛГАБАС (перенесена из Python-версии MainVersion\V0.9.0:
//  configs.py — пути, стрелки, светофоры, маршруты; ArduinoCode.py — провода)
//
//                                                        8╱── ◄Н4 ── путь 4 ─────────────────┤
//                                     ┤── М6► ─────── 6 ●─────── ◄Н2 ── путь 2 ─────────────────┤
//                                                    ╱
//  пост ◄НА ── НАУ ── 1◄/►2 ── Б12 ── Ч► ── М2► ── 2 ●── 4 ●──── ◄Н1 ── путь 1 ── М8► ── 1 ●── ◄М1 ── вытяжка ┤
//          (однопутный перегон)                    ╲                                      ╱
//                                                   ╲──── ◄Н3 ── путь 3 ──────── М10► ──╱
//
//  Слева — однопутный перегон от путевого поста (тот же, что в окне поста программы АКЖТ).
//  Направление "→" (вправо) = к Алгабасу (чётное, приём на Ч),
//  направление "←" (влево)  = от Алгабаса к посту (нечётное, отправление с Н1–Н4).
//  Справа все пути заканчиваются тупиками (упорами).
//
//  Стрелки (как в Python): 1 (ALB_Turn1), 2 (ALB_Turn2), 4/6 — спаренный съезд (ALB_Turn4-6), 8 (ALB_Turn8).
//  "+" — нормальное положение (в Python "left"), "−" — на отклонение ("right").
// =====================================================================

public class AlgabasLayout
{
    public const float Y4 = 130, Y2 = 250, Y1 = 370, Y3 = 490;
    const float Mid12 = (Y1 + Y2) / 2;

    public readonly Interlocking IL;
    public readonly StationView View = new();
    SchemeGeometry G => View.Geo;

    public readonly DirectionLine Line;

    /// <summary>Виртуальные поезда — выставляет RailModel.</summary>
    public TrainSim Trains;

    // ---- участки ----
    public TrackSection NAU, B12, CH_M2, SP24, P1, SP1, VT, P3, M6T, SP6, SP8, P2, P4;
    // ---- стрелки ----
    public RailSwitch SW1, SW2, SW46, SW8;
    // ---- светофоры ----
    public RailSignal CH, M2, H1, H2, H3, H4, M1, M6, M8, M10, OP1, OP2, HA_POST;

    public AlgabasLayout(Interlocking il)
    {
        IL = il;
        View.Title = "Станция Алгабас";
        View.Caption = "Ст. Алгабас";

        BuildSections();
        BuildSwitches();
        BuildSignals();

        Line = new DirectionLine
        {
            Id = "ОП", Title = "Перегон Путевой пост — Алгабас", Current = Dir.Right,
            RightName = "к Алгабасу (приём)", LeftName = "к посту (отправление)"
        };
        Line.Sections.Add(NAU);
        Line.Sections.Add(B12);
        IL.Lines.Add(Line);
        View.Lines.Add(Line);

        // проходные однопутного перегона горят только при "своём" направлении
        OP1.IsActive = () => Line.Current == Dir.Left;
        OP2.IsActive = () => Line.Current == Dir.Right;

        BuildRoutes();
        // маршруты приёма и отправления зависят от направления перегона:
        // пока хоть один из них задан, направление сменить нельзя
        foreach (var r in View.Routes.Where(r => !r.Shunting)) Line.DependentRoutes.Add(r);
        BuildGeometry();
        foreach (var r in View.Routes) r.Pieces = RoutePainter.Compute(G, r);

        View.InviteSignals.Add(CH);
        View.SpawnButtons.Add(("Поезд от поста", () =>
        {
            if (Line.Current != Dir.Right)
            {
                IL.Say("Поезд от поста не может выйти: направление перегона «←» (к посту). Сначала смените направление.", LogKind.Refuse);
                return;
            }
            Trains?.Spawn(NAU, Dir.Right);
        }));
        // [УБРАНО 28.10.2026] галочка «Пост принимает»: НА поста теперь только по данным АКЖТ (связь диспетчеров)
        View.TrainPlaces.Add(("Путь 1", P1));
        View.TrainPlaces.Add(("Путь 2", P2));
        View.TrainPlaces.Add(("Путь 3", P3));
        View.TrainPlaces.Add(("Путь 4", P4));
        View.TrainPlaces.Add(("Тупик за М6", M6T));
        View.TrainPlaces.Add(("Вытяжка за М1", VT));
    }

    // =================================================================
    //  УЧАСТКИ
    // =================================================================
    TrackSection S(string label, string title)
    {
        var s = IL.AddSection(label, title, label);
        View.Sections.Add(s);
        return s;
    }

    void BuildSections()
    {
        // перегон
        NAU = S("НАУ", "Блок-участок однопутного перегона у путевого поста (между НА поста и проходными 1/2)");
        B12 = S("Б12", "Блок-участок однопутного перегона между проходными 1/2 и входным Ч — участок приближения к Ч");

        // станция
        CH_M2 = S("Ч-М2", "Участок между входным Ч и маневровым М2");
        // [ИЗМЕНЕНО 27.10.2026] 2СП и 4СП на макете — одна рельсовая цепь, без стыка между ними
        SP24 = S("2-4СП", "Стрелочный участок стрелок 2 и 4 (съезд 4/6)");
        // [ИЗМЕНЕНО 28.10.2026] между стрелками 6 и 8 изостык: было одно 6-8СП, стало 6СП и 8СП
        SP6 = S("6СП", "Стрелочный участок стрелки 6 (съезд 4/6)");
        SP8 = S("8СП", "Стрелочный участок стрелки 8");
        M6T = S("М6Т", "Тупик за маневровым М6");
        P1 = S("1П", "Главный путь 1");
        P2 = S("2П", "Путь 2 (тупиковый)");
        P3 = S("3П", "Путь 3");
        P4 = S("4П", "Путь 4 (тупиковый)");
        SP1 = S("1СП", "Стрелочный участок стрелки 1 (соединяет пути 1 и 3 справа)");
        VT = S("ВТ", "Вытяжной тупик за маневровым М1");
    }

    // =================================================================
    //  СТРЕЛКИ
    // =================================================================
    RailSwitch Sw(string label, string title, params TrackSection[] secs)
    {
        var sw = IL.AddSwitchL(label, label, title, secs);
        View.Switches.Add(sw);
        return sw;
    }

    void BuildSwitches()
    {
        SW2 = Sw("2", "Стрелка 2: путь 1 (+) / путь 3 (−)", SP24);
        SW46 = Sw("4/6", "Съезд 4/6 между путём 1 и путями 2/4 (стрелки 4 и 6 переводятся вместе)", SP24, SP6);
        SW8 = Sw("8", "Стрелка 8: путь 2 (+) / путь 4 (−)", SP8);
        SW1 = Sw("1", "Стрелка 1 (справа): путь 1 (+) / путь 3 (−)", SP1);
    }

    // =================================================================
    //  СВЕТОФОРЫ (лампы сверху вниз по мачте, имена ламп — как в Python)
    // =================================================================
    static readonly string[] L_ENTRY = { "yellow", "green", "red", "yellow1", "white" };
    static readonly string[] L_EXIT = { "yellow", "green", "red", "white" };
    static readonly string[] L_SHUNT_BLUE = { "white", "blue" };
    static readonly string[] L_SHUNT_RED = { "white", "red" };
    static readonly string[] L_BLOCK = { "yellow", "green", "red" };

    RailSignal Sig(string id, string label, string title, SignalKind kind, Dir dir, string[] lamps) =>
        IL.AddSignal(new RailSignal { Id = id, Label = label, Title = title, Kind = kind, Dir = dir, Lamps = lamps });

    RailSignal Ctl(string label, string title, Dir dir, string[] lamps) =>
        Sig(label, label, title, SignalKind.Controlled, dir, lamps);

    void BuildSignals()
    {
        // перегон
        HA_POST = Sig("НА-ПОСТ", "НА", "Светофор НА путевого поста (управляет ДСП поста — у нас имитация)", SignalKind.Neighbor, Dir.Left,
                      new[] { "yellow", "red", "yellow1", "white" });
        OP1 = Sig("1-ОП", "1", "Проходной 1 однопутного перегона (к посту)", SignalKind.Automatic, Dir.Left, L_BLOCK);
        OP1.Protects = NAU;
        OP1.Next = HA_POST;

        CH = Ctl("Ч", "Входной Ч станции Алгабас (со стороны путевого поста)", Dir.Right, L_ENTRY);
        CH.Approach = B12;

        OP2 = Sig("2-ОП", "2", "Проходной 2 однопутного перегона (к Алгабасу, предвходной к Ч)", SignalKind.Automatic, Dir.Right, L_BLOCK);
        OP2.Protects = B12;
        OP2.Next = CH;

        // выходные (на перегон к посту) — у них есть белый для манёвров
        H1 = Ctl("Н1", "Выходной Н1 с пути 1 (на перегон к посту)", Dir.Left, L_EXIT);
        H2 = Ctl("Н2", "Выходной Н2 с пути 2 (на перегон к посту)", Dir.Left, L_EXIT);
        H3 = Ctl("Н3", "Выходной Н3 с пути 3 (на перегон к посту)", Dir.Left, L_EXIT);
        H4 = Ctl("Н4", "Выходной Н4 с пути 4 (на перегон к посту)", Dir.Left, L_EXIT);

        // маневровые
        M2 = Ctl("М2", "Маневровый М2 (за входным Ч)", Dir.Right, L_SHUNT_BLUE);
        M6 = Ctl("М6", "Маневровый М6 (из тупика М6Т)", Dir.Right, L_SHUNT_RED);
        M8 = Ctl("М8", "Маневровый М8 (с пути 1 на вытяжку)", Dir.Right, L_SHUNT_RED);
        M10 = Ctl("М10", "Маневровый М10 (с пути 3 на вытяжку)", Dir.Right, L_SHUNT_RED);
        M1 = Ctl("М1", "Маневровый М1 (с вытяжки на пути 1 и 3)", Dir.Left, L_SHUNT_RED);

        // участки приближения (для искусственной разделки)
        M2.Approach = CH_M2; M6.Approach = M6T; M8.Approach = P1; M10.Approach = P3; M1.Approach = VT;
        H1.Approach = P1; H2.Approach = P2; H3.Approach = P3; H4.Approach = P4;
    }

    // =================================================================
    //  МАРШРУТЫ
    // =================================================================
    static (RailSwitch, bool) P(RailSwitch sw) => (sw, true);
    static (RailSwitch, bool) M(RailSwitch sw) => (sw, false);

    RailRoute Route(bool shunt, RailSignal start, RailSignal end, TrackSection dest, string name,
                    TrackSection[] secs, (RailSwitch sw, bool plus)[] sws, bool div = false, TrackSection exit = null,
                    Func<string> pre = null)
    {
        var r = new RailRoute
        {
            Id = start.Label + ">" + name,
            Name = name,
            Shunting = shunt,
            Start = start,
            End = end,
            Dest = dest,
            Diverging = div,
            ExitSection = exit,
            Precondition = pre,
        };
        r.Sections.AddRange(secs);
        r.Switches.AddRange(sws);
        IL.AddRoute(r);
        View.Routes.Add(r);
        return r;
    }

    string NeedRight() => Line.Current == Dir.Right ? null
        : "направление перегона «←» (к посту) — приём с поста невозможен, сначала смените направление";

    string NeedLeft() => Line.Current == Dir.Left ? null
        : "направление перегона «→» (к Алгабасу) — для отправления на пост сначала смените направление";

    void BuildRoutes()
    {
        // ======== ПОЕЗДНЫЕ ========
        // --- приём с перегона на входной Ч ---
        // путь 1 — прямо: один жёлтый (справа тупик, следующего поездного светофора нет)
        Route(false, CH, H1, P1, "Ч → путь 1", new[] { CH_M2, SP24, P1 }, new[] { P(SW2), P(SW46) }, pre: NeedRight);
        Route(false, CH, H2, P2, "Ч → путь 2", new[] { CH_M2, SP24, SP6, SP8, P2 }, new[] { P(SW2), M(SW46), P(SW8) }, div: true, pre: NeedRight);
        Route(false, CH, H3, P3, "Ч → путь 3", new[] { CH_M2, SP24, P3 }, new[] { M(SW2) }, div: true, pre: NeedRight);
        Route(false, CH, H4, P4, "Ч → путь 4", new[] { CH_M2, SP24, SP6, SP8, P4 }, new[] { P(SW2), M(SW46), M(SW8) }, div: true, pre: NeedRight);

        // --- отправление на однопутный перегон к посту (налево) ---
        Route(false, H1, OP1, B12, "Н1 → перегон на пост (1)", new[] { SP24, CH_M2 }, new[] { P(SW46), P(SW2) }, exit: B12, pre: NeedLeft);
        Route(false, H2, OP1, B12, "Н2 → перегон на пост (1)", new[] { SP8, SP6, SP24, CH_M2 }, new[] { P(SW8), M(SW46), P(SW2) }, div: true, exit: B12, pre: NeedLeft);
        Route(false, H3, OP1, B12, "Н3 → перегон на пост (1)", new[] { SP24, CH_M2 }, new[] { M(SW2) }, div: true, exit: B12, pre: NeedLeft);
        Route(false, H4, OP1, B12, "Н4 → перегон на пост (1)", new[] { SP8, SP6, SP24, CH_M2 }, new[] { M(SW8), M(SW46), P(SW2) }, div: true, exit: B12, pre: NeedLeft);

        // ======== МАНЕВРОВЫЕ ========
        // --- движение → ---
        Route(true, M2, H1, P1, "М2 → путь 1", new[] { SP24 }, new[] { P(SW2), P(SW46) });
        Route(true, M2, H2, P2, "М2 → путь 2", new[] { SP24, SP6, SP8 }, new[] { P(SW2), M(SW46), P(SW8) });
        Route(true, M2, H3, P3, "М2 → путь 3", new[] { SP24 }, new[] { M(SW2) });
        Route(true, M2, H4, P4, "М2 → путь 4", new[] { SP24, SP6, SP8 }, new[] { P(SW2), M(SW46), M(SW8) });
        Route(true, M6, H2, P2, "М6 → путь 2", new[] { SP6, SP8 }, new[] { P(SW46), P(SW8) });
        Route(true, M6, H4, P4, "М6 → путь 4", new[] { SP6, SP8 }, new[] { P(SW46), M(SW8) });
        Route(true, M8, M1, VT, "М8 → вытяжка", new[] { SP1 }, new[] { P(SW1) });
        Route(true, M10, M1, VT, "М10 → вытяжка", new[] { SP1 }, new[] { M(SW1) });

        // --- движение ← (белый на выходных Н) ---
        Route(true, M1, M8, P1, "М1 → путь 1", new[] { SP1 }, new[] { P(SW1) });
        Route(true, M1, M10, P3, "М1 → путь 3", new[] { SP1 }, new[] { M(SW1) });
        Route(true, H1, M2, CH_M2, "Н1 → М2", new[] { SP24 }, new[] { P(SW46), P(SW2) });
        Route(true, H2, M2, CH_M2, "Н2 → М2", new[] { SP8, SP6, SP24 }, new[] { P(SW8), M(SW46), P(SW2) });
        Route(true, H3, M2, CH_M2, "Н3 → М2", new[] { SP24 }, new[] { M(SW2) });
        Route(true, H4, M2, CH_M2, "Н4 → М2", new[] { SP8, SP6, SP24 }, new[] { M(SW8), M(SW46), P(SW2) });
        Route(true, H2, M6, M6T, "Н2 → тупик М6", new[] { SP8, SP6 }, new[] { P(SW8), P(SW46) });
        Route(true, H4, M6, M6T, "Н4 → тупик М6", new[] { SP8, SP6 }, new[] { M(SW8), P(SW46) });
    }

    // =================================================================
    //  ТОПОЛОГИЯ: куда поезд поедет из участка
    // =================================================================
    public TrackStep Next(TrackSection s, Dir d)
    {
        TrackStep To(TrackSection x, RailSignal sig = null) => new() { Section = x, Dir = d, Signal = sig };
        TrackStep Buffer() => new() { Buffer = true, Dir = d };
        bool p1 = SW1.Plus, p2 = SW2.Plus, p46 = SW46.Plus, p8 = SW8.Plus;

        if (d == Dir.Right)
        {
            if (s == NAU) return To(B12, OP2);
            if (s == B12) return To(CH_M2, CH);
            if (s == CH_M2) return To(SP24, M2);
            if (s == SP24) return !p2 ? To(P3) : p46 ? To(P1) : To(SP6);
            if (s == M6T) return To(SP6, M6);
            if (s == SP6) return To(SP8);
            if (s == SP8) return p8 ? To(P2) : To(P4);
            if (s == P2 || s == P4) return Buffer();
            // со станционного пути — всегда через маневровый светофор: он откроется только по маршруту,
            // а маршрут сам поставит стрелку 1 как надо
            if (s == P1) return To(SP1, M8);
            if (s == P3) return To(SP1, M10);
            if (s == SP1) return To(VT);
            if (s == VT) return Buffer();
            return null;
        }
        else
        {
            if (s == VT) return To(SP1, M1);
            if (s == SP1) return p1 ? To(P1) : To(P3);
            // стрелка 2 стоит на путь 3 — с пути 1 и со съезда 4/6 к Ч не проехать (взрез), не едем
            if (s == P1) return p2 ? To(SP24, H1) : null;
            if (s == P2) return To(SP8, H2);
            if (s == P4) return To(SP8, H4);
            if (s == P3) return To(SP24, H3);
            if (s == SP8) return To(SP6);
            if (s == SP6) return p46 ? To(M6T) : p2 ? To(SP24) : null;
            if (s == M6T) return Buffer();
            if (s == SP24) return To(CH_M2);
            // за Ч — перегон: туда только поездом по маршруту отправления (манёвры за границу станции не выходят)
            if (s == CH_M2) return CH_M2.LockedBy is { Shunting: false } ? To(B12) : null;
            if (s == B12) return To(NAU, OP1);
            if (s == NAU) return new TrackStep { Exit = true, ExitName = "путевой пост", Signal = HA_POST, Dir = d };
            return null;
        }
    }

    // =================================================================
    //  ГЕОМЕТРИЯ (проектные координаты, масштабируются под окно).
    //  Расположение — как в Python (configs.py positions), растянуто по ширине,
    //  слева дорисован перегон.
    // =================================================================
    void BuildGeometry()
    {
        var g = G;
        g.DesignW = 2480;
        g.DesignH = 580;
        string id(TrackSection s) => s.Id;

        // ---------- перегон ----------
        // [ИЗМЕНЕНО 13.10.2026] хвост левее НА поста — продолжение перегона: жёлтый вместе с маршрутом на перегон
        g.LineDecor.Add((SchemeGeometry.P((10, Y1), (130, Y1)), id(NAU)));
        g.Piece("NAU", id(NAU), (130, Y1), (390, Y1));
        g.Piece("B12", id(B12), (390, Y1), (680, Y1));

        // ---------- путь 1 (главный) ----------
        g.Piece("CHM2", id(CH_M2), (680, Y1), (815, Y1));
        g.Piece("2c", id(SP24), (815, Y1), (1002, Y1));
        g.Branch("2s", id(SP24), SW2.Id, true, true, (1002, Y1), (1070, Y1));
        g.Branch("2d", id(SP24), SW2.Id, false, true, (1002, Y1), (1120, Y3), (1260, Y3));
        g.Piece("4c", id(SP24), (1070, Y1), (1138, Y1));
        g.Branch("4s", id(SP24), SW46.Id, true, true, (1138, Y1), (1410, Y1));
        g.Branch("4d", id(SP24), SW46.Id, false, true, (1138, Y1), (1227, Mid12));
        g.Piece("P1", id(P1), (1410, Y1), (2005, Y1));
        g.Branch("1s", id(SP1), SW1.Id, true, false, (2005, Y1), (2200, Y1));
        g.Piece("1y", id(SP1), (2005, Y3), (2073, Y3));
        g.Branch("1d", id(SP1), SW1.Id, false, false, (2073, Y3), (2200, Y1));
        g.Piece("1c", id(SP1), (2200, Y1), (2260, Y1));
        g.Piece("VT", id(VT), (2260, Y1), (2413, Y1));
        g.Buffers.Add(new PointF(2413, Y1));

        // ---------- путь 3 ----------
        g.Piece("P3", id(P3), (1260, Y3), (2005, Y3));

        // ---------- линия пути 2: тупик М6, стрелки 6 и 8 ----------
        g.Piece("M6T", id(M6T), (951, Y2), (1070, Y2));
        g.Buffers.Add(new PointF(951, Y2));
        g.Branch("6s", id(SP6), SW46.Id, true, false, (1070, Y2), (1316, Y2));
        g.Branch("6d", id(SP6), SW46.Id, false, false, (1227, Mid12), (1316, Y2));
        g.Piece("6c", id(SP6), (1316, Y2), (1337, Y2));
        g.Piece("8c", id(SP8), (1337, Y2), (1359, Y2));
        g.Branch("8s", id(SP8), SW8.Id, true, true, (1359, Y2), (1614, Y2));
        g.Branch("8d", id(SP8), SW8.Id, false, true, (1359, Y2), (1512, Y4), (1614, Y4));
        g.Piece("P2", id(P2), (1614, Y2), (2209, Y2));
        g.Buffers.Add(new PointF(2209, Y2));

        // ---------- путь 4 ----------
        g.Piece("P4", id(P4), (1614, Y4), (2209, Y4));
        g.Buffers.Add(new PointF(2209, Y4));

        // ---------- изолирующие стыки ----------
        foreach (var x in new float[] { 130, 390, 680, 815, 1410, 2005, 2260 }) g.Joint(x, Y1);   // 1070 убран: 2-4СП без стыка
        foreach (var x in new float[] { 1070, 1337, 1614 }) g.Joint(x, Y2);   // 1337 — изостык 6СП / 8СП
        foreach (var x in new float[] { 1260, 2005 }) g.Joint(x, Y3);
        g.Joint(1614, Y4);
        g.Joint(1227, Mid12);

        // ---------- светофоры (над путём — нечётные ←, под путём — чётные →) ----------
        g.Sig(HA_POST, 130, Y1, true, " (пост)");
        g.Sig(OP1, 390, Y1, true);
        g.Sig(OP2, 390, Y1, false);
        g.Sig(CH, 680, Y1, false);
        g.Sig(M2, 815, Y1, false);
        g.Sig(M6, 1070, Y2, false);
        g.Sig(H1, 1410, Y1, true);
        g.Sig(H2, 1614, Y2, true);
        g.Sig(H4, 1614, Y4, true);
        g.Sig(H3, 1260, Y3, true);
        g.Sig(M8, 2005, Y1, false);
        g.Sig(M10, 2005, Y3, false);
        g.Sig(M1, 2260, Y1, true);

        // ---------- номера стрелок ----------
        g.SwLabel("2", SW2, 995, Y1 - 20);
        g.SwLabel("4", SW46, 1130, Y1 + 20);
        g.SwLabel("6", SW46, 1316, Y2 - 20);
        g.SwLabel("8", SW8, 1368, Y2 + 20);
        g.SwLabel("1", SW1, 2222, Y1 + 22);

        // ---------- номера путей и надписи ----------
        g.Text("1", 1700, Y1 - 34, 16f, true, true);
        g.Text("2", 1900, Y2 - 34, 16f, true, true);
        g.Text("3", 1630, Y3 - 34, 16f, true, true);
        g.Text("4", 1900, Y4 - 34, 16f, true, true);
        g.Text("← к путевому посту", 12, Y1 + 62, 9.5f, true);
        g.Text("Однопутный перегон Путевой пост — Алгабас", 400, Y1 - 92, 9.5f, align: StringAlignment.Center);
        g.Boundaries.Add(new PointF(640, Y1 + 40));
        g.Directions.Add(new DirectionGeom
        {
            Line = Line, Pos = new PointF(400, Y1 + 132), ArrowX1 = 230, ArrowX2 = 570, ArrowY = Y1 + 108,
            RightText = "направление: → к Алгабасу", LeftText = "направление: ← к посту"
        });

        // ---------- подписи участков ----------
        g.SectionLabels[id(CH_M2)] = new PointF(748, Y1 - 16);
        g.SectionLabels[id(SP24)] = new PointF(930, Y1 - 16);
        g.SectionLabels[id(SP6)] = new PointF(1190, Y2 - 16);
        g.SectionLabels[id(SP8)] = new PointF(1480, Y2 - 16);
        g.SectionLabels[id(SP1)] = new PointF(2110, Y1 - 16);
        g.SectionLabels[id(NAU)] = new PointF(250, Y1 + 16);
        g.SectionLabels[id(B12)] = new PointF(560, Y1 + 16);
        g.AutoSectionLabels();
        g.SectionLabels.Remove(id(M6T));   // [ДОБАВЛЕНО 28.10.2026] тупика М6Т на макете пока нет — название не показываем
    }
}
