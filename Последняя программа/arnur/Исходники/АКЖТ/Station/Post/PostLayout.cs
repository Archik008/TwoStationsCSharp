using StationApp.Akzht;
using StationApp.Core;
using StationApp.Ui;

namespace StationApp.Post;

// =====================================================================
//  СХЕМА ПУТЕВОГО ПОСТА (от Н станции АКЖТ до Ч станции Алгабас)
//
//   ← АКЖТ   путь I (нечётное, ←):  Н(АКЖТ) ◄ 1 ◄ 3 ◄ 5 ◄ переезд ◄|гр.| 1 ── ◄Н ── НП ──[ тоннель
//                                                                  \
//            путь II (чётное, →):   6 ► 4 ► 2 ► переезд ► Ч ────── 2 ── 3 ─── ЧП ──[ тоннель
//                                                                         \
//                                                     тупик ]─────────── 4 ── ◄НА ── 1◄ / ►2 ── Ч(Алгабас) →
//
//  Стрелки 1/2 и 3/4 — спаренные съезды. Справа пути I и II уходят в ТОННЕЛЬ —
//  это будущее продолжение макета (светофоров там пока нет, поэтому Ч на тоннель даёт жёлтый).
//  Слева перегон общий с окном АКЖТ: "Н (АКЖТ)" — это настоящий входной Н станции АКЖТ,
//  участок перед проходным 6 — участок удаления АКЖТ (УII).
// =====================================================================

public class PostLayout
{
    // [ИЗМЕНЕНО 08.10.2026] Пути I и II — на той же высоте, что у АКЖТ (было 210 / 350 / 490):
    // перегон общий, и на двух мониторах пути продолжаются на одном уровне.
    // Нижний путь (на Алгабас) — на уровне пути 4 АКЖТ.
    public const float Y1 = AkzhtLayout.Y1, Y2 = AkzhtLayout.Y2, Y3 = AkzhtLayout.Y4;

    public readonly Interlocking IL;
    public readonly StationView View = new();
    SchemeGeometry G => View.Geo;
    readonly AkzhtLayout ak;
    readonly HashSet<TrackSection> own = new();

    public readonly DirectionLine Line;
    public readonly LevelCrossing Crossing;

    // быстрые ссылки
    public TrackSection S_1BU, S_3BU, S_5BU, S_NU, S_1SP, S_NP, S_TUN1,
                        S_6BU, S_4BU, S_2BU, S_23SP, S_CHP, S_TUN2,
                        S_TUP, S_4SP, S_NAU, S_B12;
    public RailSwitch SW12, SW34;
    public RailSignal P1, P3, P5, P6, P4, P2, CH, H, HA, OP1, OP2, CH_ALG;
    public RailRoute R_CH_TUN, R_CH_ALG, R_HA_AKZHT, R_H_AKZHT;

    /// <summary>Виртуальные поезда (общие для всей модели) — выставляет RailModel.</summary>
    public TrainSim Trains;

    public bool Owns(TrackSection s) => own.Contains(s);

    public PostLayout(Interlocking il, AkzhtLayout akzht)
    {
        IL = il;
        ak = akzht;
        View.Title = "Путевой пост (от Н ст. АКЖТ до Ч ст. Алгабас)";
        View.Caption = "Путевой пост";

        BuildSections();
        BuildSwitches();
        BuildSignals();

        Line = new DirectionLine { Id = "ОП", Title = "Перегон Путевой пост — Алгабас", Current = Dir.Right,
                                  RightName = "на Алгабас (чётное)", LeftName = "от Алгабаса (нечётное)" };
        Line.Sections.Add(S_NAU);
        Line.Sections.Add(S_B12);
        IL.Lines.Add(Line);
        View.Lines.Add(Line);

        BuildRoutes();
        Line.DependentRoutes.Add(R_CH_ALG);
        Line.DependentRoutes.Add(R_HA_AKZHT);

        Crossing = new LevelCrossing
        {
            Id = "ПЕР",
            Title = "Переезд",
            // [ИЗМЕНЕНО 08.10.2026] переезд теперь правее Ч: на пути I — на 1СП, на пути II — на 2-3СП.
            // [ИЗМЕНЕНО 20.10.2026, правки преподавателя] Закрыт, пока поезд на переезде (занят 1СП или 2-3СП).
            // Открытие Н, НА или Ч переезд больше НЕ закрывает. Временно — ручное закрытие кнопкой
            // (LevelCrossing.ManualClosed, кнопка «Закрыть переезд» у поста).
            // В будущем (переезд на макете пока не работает): закрытие по Н и НА вместе с занятостью НП или НАУ.
            ShouldClose = () => S_1SP.Occupied || S_23SP.Occupied
        };
        IL.Crossings.Add(Crossing);
        View.Crossings.Add(Crossing);   // [ДОБАВЛЕНО 21.10.2026, этап 6] кнопки переезда в колонке поста

        BuildGeometry();
        foreach (var r in View.Routes) r.Pieces = RoutePainter.Compute(G, r);

        View.InviteSignals.Add(CH);
        View.InviteSignals.Add(H);
        View.InviteSignals.Add(HA);
        View.SpawnButtons.Add(("Поезд из тоннеля (путь I)", () => Trains?.Spawn(S_TUN1, Dir.Left)));
        View.SpawnButtons.Add(("Поезд от Алгабаса", () =>
        {
            if (Line.Current != Dir.Left)
            {
                IL.Say("Поезд от Алгабаса не может выйти: направление перегона «→». Сначала смените направление.", LogKind.Refuse);
                return;
            }
            Trains?.Spawn(S_B12, Dir.Left);
        }));
        // [УБРАНО 28.10.2026] галочка «Алгабас принимает»: Ч Алгабаса теперь только по данным Алгабаса (связь диспетчеров)
    }

    // =================================================================
    //  УЧАСТКИ
    // =================================================================
    TrackSection S(string id, string title)
    {
        var s = IL.AddSection(id, title);
        own.Add(s);
        View.Sections.Add(s);
        return s;
    }

    void BuildSections()
    {
        S_1BU = S("1БУ", "Блок-участок пути I между светофорами 1 и Н станции АКЖТ");
        S_3BU = S("3БУ", "Блок-участок пути I между светофорами 3 и 1");
        S_5BU = S("5БУ", "Блок-участок пути I между светофорами 5 и 3");
        S_NU = S("НУ", "Участок удаления поста по пути I (от границы поста до светофора 5)");
        S_1SP = S("1СП", "Стрелочный участок стрелки 1, на нём переезд");
        S_NP = S("НП", "Участок приближения к светофору Н (от тоннеля до Н)");
        S_TUN1 = S("Тоннель I", "Тоннель, путь I (продолжение макета) — отсюда поезда приходят к посту");

        S_6BU = S("6БУ", "Блок-участок пути II между светофорами 6 и 4");
        S_4BU = S("4БУ", "Блок-участок пути II между светофорами 4 и 2");
        S_2BU = S("2БУ", "Участок приближения к Ч (между 2 и Ч)");
        S_23SP = S("2-3СП", "Стрелочный участок стрелок 2 и 3, на нём переезд");
        S_CHP = S("ЧП", "Путь поста от стрелки 3 до тоннеля");
        S_TUN2 = S("Тоннель II", "Тоннель, путь II (продолжение макета) — сюда уходят поезда от поста");

        S_TUP = S("Туп", "Предохранительный тупик");
        S_4SP = S("4СП", "Стрелочный участок стрелки 4");
        S_NAU = S("НАУ", "Участок приближения к НА / удаления на Алгабас (между НА и 1/2)");
        S_B12 = S("Б12", "Блок-участок однопутного перегона между 1/2 и Ч станции Алгабас");

        // 1БУ, 3БУ, 6БУ нарисованы только на схеме АКЖТ — и в списке занятости они там (AkzhtLayout.Link)
        View.Sections.Remove(S_1BU);
        View.Sections.Remove(S_3BU);
        View.Sections.Remove(S_6BU);
    }

    void BuildSwitches()
    {
        SW12 = IL.AddSwitch("1/2", "Съезд 1/2 между путями I и II", S_1SP, S_23SP);
        SW34 = IL.AddSwitch("3/4", "Съезд 3/4 на однопутный перегон к Алгабасу", S_23SP, S_4SP);
        View.Switches.Add(SW12);
        View.Switches.Add(SW34);
    }

    // =================================================================
    //  СВЕТОФОРЫ
    // =================================================================
    static readonly string[] L_ENTRY5 = { "yellow", "green", "red", "yellow1", "white" };
    static readonly string[] L_BLOCK = { "yellow", "green", "red" };

    void BuildSignals()
    {
        CH_ALG = Sig("Ч-АЛГ", "Ч", "Входной Ч станции Алгабас (управляет ДСП Алгабаса — у нас имитация)", SignalKind.Neighbor, Dir.Right, L_ENTRY5);

        // путь I: проходные нечётного направления; 1 стоит перед настоящим входным Н станции АКЖТ
        P1 = Auto("1-I", "1", "Проходной 1 пути I (предвходной к Н АКЖТ)", Dir.Left, S_1BU, ak.H);
        P3 = Auto("3-I", "3", "Проходной 3 пути I", Dir.Left, S_3BU, P1);
        P5 = Auto("5-I", "5", "Проходной 5 пути I", Dir.Left, S_5BU, P3);

        // путь II: проходные чётного направления
        P6 = Auto("6-II", "6", "Проходной 6 пути II (первый за станцией АКЖТ)", Dir.Right, S_6BU, null);
        P4 = Auto("4-II", "4", "Проходной 4 пути II", Dir.Right, S_4BU, null);
        P2 = Auto("2-II", "2", "Проходной 2 пути II (перед входным Ч поста)", Dir.Right, S_2BU, null);

        CH = Sig("Ч", "Ч", "Входной Ч путевого поста (чётное направление, со стороны АКЖТ)", SignalKind.Controlled, Dir.Right, L_ENTRY5);
        CH.Approach = S_2BU;
        H = Sig("Н", "Н", "Светофор Н поста (из тоннеля на путь I к АКЖТ)", SignalKind.Controlled, Dir.Left,
                new[] { "yellow", "green", "red", "white" });
        H.Approach = S_NP;
        H.SimpleSecondLamp = true;    // [ДОБАВЛЕНО 12.10.2026] в упрощённом режиме — вторая лампа (пригласительный)
        HA = Sig("НА", "НА", "Входной НА (со стороны Алгабаса, всегда по отклонению)", SignalKind.Controlled, Dir.Left,
                 new[] { "yellow", "red", "yellow1", "white" });
        HA.Approach = S_NAU;
        HA.SimpleSecondLamp = true;   // [ДОБАВЛЕНО 12.10.2026] вторая лампа: нижний жёлтый и пригласительный

        // однопутный перегон: двусторонняя автоблокировка
        OP1 = Auto("1-ОП", "1", "Проходной 1 однопутного перегона (к посту)", Dir.Left, S_NAU, HA);
        OP2 = Auto("2-ОП", "2", "Проходной 2 однопутного перегона (к Алгабасу)", Dir.Right, S_B12, CH_ALG);
        OP1.IsActive = () => Line.Current == Dir.Left;
        OP2.IsActive = () => Line.Current == Dir.Right;

        P6.Next = P4;
        P4.Next = P2;
        P2.Next = CH;
    }

    RailSignal Sig(string id, string label, string title, SignalKind kind, Dir dir, string[] lamps) =>
        IL.AddSignal(new RailSignal { Id = id, Label = label, Title = title, Kind = kind, Dir = dir, Lamps = lamps });

    RailSignal Auto(string id, string label, string title, Dir dir, TrackSection protects, RailSignal next)
    {
        var s = Sig(id, label, title, SignalKind.Automatic, dir, L_BLOCK);
        s.Protects = protects;
        s.Next = next;
        return s;
    }

    // =================================================================
    //  МАРШРУТЫ (4 поездных; маневровых светофоров на посту нет)
    // =================================================================
    RailRoute Add(RailRoute r)
    {
        IL.AddRoute(r);
        View.Routes.Add(r);
        return r;
    }

    void BuildRoutes()
    {
        R_CH_TUN = Add(new RailRoute
        {
            Id = "Ч-ТУН",
            Name = "Ч → тоннель",
            Start = CH,
            End = null,                  // за тоннелем светофора пока нет → на Ч жёлтый
            Dest = S_TUN2,
            Sections = { S_23SP, S_CHP },
            Switches = { (SW12, true), (SW34, true) },
            ExitSection = S_TUN2,
        });

        R_CH_ALG = Add(new RailRoute
        {
            Id = "Ч-2ОП",
            Name = "Ч → перегон на Алгабас (2)",
            Start = CH,
            End = OP2,
            Dest = S_NAU,
            Sections = { S_23SP, S_4SP },
            Switches = { (SW12, true), (SW34, false) },
            ExitSection = S_NAU,
            Diverging = true,
            Precondition = () => Line.Current == Dir.Right ? null
                : "направление перегона на Алгабас установлено «←» (от Алгабаса) — сначала смените направление",
        });

        R_HA_AKZHT = Add(new RailRoute
        {
            Id = "НА-5",
            Name = "НА → путь I на АКЖТ (5)",
            Start = HA,
            End = P5,
            Dest = S_NU,
            Sections = { S_4SP, S_23SP, S_1SP },
            Switches = { (SW34, false), (SW12, false) },
            ExitSection = S_NU,
            Diverging = true,
            Precondition = () => Line.Current == Dir.Left ? null
                : "направление перегона установлено «→» (на Алгабас) — приём с Алгабаса невозможен, смените направление",
        });

        R_H_AKZHT = Add(new RailRoute
        {
            Id = "Н-5",
            Name = "Н → путь I на АКЖТ (5)",
            Start = H,
            End = P5,
            Dest = S_NU,
            Sections = { S_1SP },
            Switches = { (SW12, true) },
            ExitSection = S_NU,
        });
    }

    // =================================================================
    //  ТОПОЛОГИЯ
    // =================================================================
    public TrackStep Next(TrackSection s, Dir d)
    {
        TrackStep To(TrackSection x, Dir nd, RailSignal sig = null) => new() { Section = x, Dir = nd, Signal = sig };

        if (d == Dir.Left)
        {
            if (s == S_TUN1) return To(S_NP, Dir.Left);
            if (s == S_NP) return SW12.Plus ? To(S_1SP, Dir.Left, H) : null;           // остряки стрелки 1 не по ходу — взрез!
            if (s == S_1SP) return To(S_NU, Dir.Left);
            if (s == S_NU) return To(S_5BU, Dir.Left, P5);
            if (s == S_5BU) return To(S_3BU, Dir.Left, P3);
            if (s == S_3BU) return To(S_1BU, Dir.Left, P1);
            if (s == S_1BU) return To(ak.H_M1, Dir.Left, ak.H);                        // въезд на станцию АКЖТ

            if (s == S_B12) return To(S_NAU, Dir.Left, OP1);
            if (s == S_NAU) return To(S_4SP, Dir.Left, HA);
            if (s == S_4SP) return SW34.Plus ? To(S_TUP, Dir.Left) : To(S_23SP, Dir.Left);
            if (s == S_TUP) return new TrackStep { Buffer = true, Dir = Dir.Left };
            if (s == S_23SP) return SW12.Plus ? null : To(S_1SP, Dir.Left);            // по неправильному пути II не пускаем
            return null;
        }
        else
        {
            if (s == S_6BU) return To(S_4BU, Dir.Right, P4);
            if (s == S_4BU) return To(S_2BU, Dir.Right, P2);
            if (s == S_2BU) return SW12.Plus ? To(S_23SP, Dir.Right, CH) : null;       // стрелка 2 не по ходу — взрез!
            if (s == S_23SP) return SW34.Plus ? To(S_CHP, Dir.Right) : To(S_4SP, Dir.Right);
            if (s == S_CHP) return To(S_TUN2, Dir.Right);
            if (s == S_TUN2) return new TrackStep { Exit = true, ExitName = "тоннель (продолжение макета)", Dir = Dir.Right };
            if (s == S_4SP) return To(S_NAU, Dir.Right);
            if (s == S_NAU) return To(S_B12, Dir.Right, OP2);
            if (s == S_B12) return new TrackStep { Exit = true, ExitName = "ст. Алгабас", Signal = CH_ALG, Dir = Dir.Right };
            if (s == S_TUP) return SW34.Plus ? To(S_4SP, Dir.Right) : null;
            return null;
        }
    }

    // =================================================================
    //  ГЕОМЕТРИЯ (проектные координаты, масштабируются под окно)
    // =================================================================
    void BuildGeometry()
    {
        var g = G;
        g.DesignW = 2400;
        g.DesignH = 720;     // под подписью направления перегона (Y3 + 112) должно оставаться место
        float mid12 = (Y1 + Y2) / 2, mid23 = (Y2 + Y3) / 2;

        // [ИЗМЕНЕНО 08.10.2026] Левый край схемы (x = 0) стыкуется с правым краем схемы АКЖТ
        // на соседнем мониторе. Н (АКЖТ), проходные 1, 3 (путь I) и 6, 4 (путь II) и участки
        // 1БУ, 3БУ, 6БУ показаны только на схеме АКЖТ — здесь их нет, чтобы не было дублирования.
        // 5БУ и 4БУ начинаются на схеме АКЖТ и продолжаются здесь.

        // ---- путь I (верхний) ----
        g.Piece("5БУ", S_5BU.Id, (0, Y1), (400, Y1));
        g.Piece("НУ", S_NU.Id, (400, Y1), (1100, Y1));
        g.Piece("1C", S_1SP.Id, (1100, Y1), (1250, Y1));
        g.Branch("1S", S_1SP.Id, SW12.Id, true, true, (1250, Y1), (1450, Y1));
        g.Branch("1D", S_1SP.Id, SW12.Id, false, true, (1250, Y1), (1325, mid12));
        g.Piece("НП", S_NP.Id, (1450, Y1), (1700, Y1));
        g.Piece("ТУН1", S_TUN1.Id, (1700, Y1), (1860, Y1));
        g.Tunnels.Add(new TunnelGeom { Portal = new PointF(1700, Y1), OpensRight = true, Length = 160 });

        // ---- путь II (средний) ----
        // 2БУ растянут влево (было 900–1180): Ч стоит на месте бывшего переезда, переезд — правее Ч
        g.Piece("4БУ", S_4BU.Id, (0, Y2), (400, Y2));
        g.Piece("2БУ", S_2BU.Id, (400, Y2), (1040, Y2));
        g.Branch("23A", S_23SP.Id, SW12.Id, true, false, (1040, Y2), (1400, Y2));
        g.Branch("23B", S_23SP.Id, SW12.Id, false, false, (1325, mid12), (1400, Y2));
        g.Piece("23C", S_23SP.Id, (1400, Y2), (1470, Y2));
        g.Branch("23D", S_23SP.Id, SW34.Id, true, true, (1470, Y2), (1560, Y2));
        g.Branch("23E", S_23SP.Id, SW34.Id, false, true, (1470, Y2), (1545, mid23));
        g.Piece("ЧП", S_CHP.Id, (1560, Y2), (1700, Y2));
        g.Piece("ТУН2", S_TUN2.Id, (1700, Y2), (1860, Y2));
        g.Tunnels.Add(new TunnelGeom { Portal = new PointF(1700, Y2), OpensRight = true, Length = 160 });
        g.Text("Тоннель", 1780, mid12 - 10, 11f, true, align: StringAlignment.Center);
        g.Text("(продолжение макета)", 1780, mid12 + 8, 8.5f, align: StringAlignment.Center);

        // ---- нижний путь (однопутный перегон на Алгабас) ----
        g.Piece("Туп", S_TUP.Id, (1330, Y3), (1520, Y3));
        g.Buffers.Add(new PointF(1330, Y3));
        g.Branch("4F", S_4SP.Id, SW34.Id, true, false, (1520, Y3), (1620, Y3));
        g.Branch("4G", S_4SP.Id, SW34.Id, false, false, (1545, mid23), (1620, Y3));
        g.Piece("4H", S_4SP.Id, (1620, Y3), (1760, Y3));
        g.Piece("НАУ", S_NAU.Id, (1760, Y3), (2040, Y3));
        g.Piece("Б12", S_B12.Id, (2040, Y3), (2280, Y3));
        // [ИЗМЕНЕНО 13.10.2026] хвост за Ч Алгабаса — продолжение перегона: жёлтый вместе с маршрутом на перегон
        g.LineDecor.Add((SchemeGeometry.P((2280, Y3), (2385, Y3)), S_B12.Id));

        // ---- изолирующие стыки ----
        foreach (var x in new float[] { 400, 1100, 1450, 1700 }) g.Joint(x, Y1);
        foreach (var x in new float[] { 400, 1040, 1560, 1700 }) g.Joint(x, Y2);
        foreach (var x in new float[] { 1520, 1760, 2040, 2280 }) g.Joint(x, Y3);
        g.Joint(1325, mid12);
        g.Joint(1545, mid23);

        // ---- светофоры ----
        g.Sig(P5, 400, Y1, true);
        g.Sig(H, 1450, Y1, true);
        g.Sig(P2, 400, Y2, false);
        g.Sig(CH, 1040, Y2, false);
        g.Sig(HA, 1760, Y3, true);
        g.Sig(OP1, 2040, Y3, true);
        g.Sig(OP2, 2040, Y3, false);
        g.Sig(CH_ALG, 2280, Y3, false, " (Алгабас)");

        // ---- номера стрелок ----
        g.SwLabel("1", SW12, 1236, Y1 - 20);
        g.SwLabel("2", SW12, 1414, Y2 + 20);
        g.SwLabel("3", SW34, 1458, Y2 - 20);
        g.SwLabel("4", SW34, 1632, Y3 + 20);

        // ---- надписи ----
        g.Text("← на ст. АКЖТ (путь I)", 20, Y1 - 78, 9.5f, true);
        g.Text("от ст. АКЖТ (путь II) →", 20, Y2 + 48, 9.5f, true);
        g.Text("на ст. Алгабас →", 2385, Y3 + 72, 9.5f, true, align: StringAlignment.Far);
        g.Text("Перегон АКЖТ — Путевой пост (двухпутный, автоблокировка)", 300, Y2 + 84, 9.5f, align: StringAlignment.Center);
        g.Boundaries.Add(new PointF(1100, Y1 - 62));
        g.Boundaries.Add(new PointF(1790, Y3 + 30));
        g.Boundaries.Add(new PointF(1700, Y1 - 62));
        g.Crossings.Add(new CrossingGeom { Crossing = Crossing, X = 1180, Y1 = Y1, Y2 = Y2 });   // правее Ч (было 1040)
        g.Directions.Add(new DirectionGeom
        {
            Line = Line, Pos = new PointF(2150, Y3 + 112), ArrowX1 = 2060, ArrowX2 = 2240, ArrowY = Y3 + 88,
            RightText = "направление: → на Алгабас", LeftText = "направление: ← от Алгабаса"
        });

        // ---- подписи участков ----
        g.SectionLabels[S_23SP.Id] = new PointF(1290, Y2 - 16);
        g.SectionLabels[S_1SP.Id] = new PointF(1130, Y1 + 16);    // левее переезда
        g.SectionLabels[S_4SP.Id] = new PointF(1700, Y3 + 16);
        g.SectionLabels[S_TUN1.Id] = new PointF(1800, Y1 - 34);   // правее таблички «граница станции»
        g.SectionLabels[S_TUN2.Id] = new PointF(1780, Y2 + 34);
        g.SectionLabels[S_CHP.Id] = new PointF(1630, Y2 - 16);
        g.SectionLabels[S_NP.Id] = new PointF(1575, Y1 + 16);
        g.AutoSectionLabels();
    }
}
