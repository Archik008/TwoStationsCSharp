using StationApp.Core;
using StationApp.Post;
using StationApp.Ui;

namespace StationApp.Akzht;

// =====================================================================
//  СХЕМА СТАНЦИИ АКЖТ (по чертежу преподавателя)
//
//                                      5 ── Н5 ─────────── Ч5 ─╮17
//   6 ]── М10 ─ 12 ─ 16 ── Н3 ── 3 ─────────────────── Ч3 ── 17 ── 15 ── М7 ─ (9 ─ М9 ─ 7 ── депо: ПРОЕКТ)
//   тоннель ◄ М4 ─ 4 ── 8 ─ М8 ─ 10 ── Н1 ── 1 ──────────────── Ч1 ── 5 ─ 13 ─ 3 ─ (11) ─ М1 ── Н ◄ 1 ◄ 3 (к посту)
//   тоннель ► Ч ─ М2 ─ 2 ─ М6 ─ 6 ─ 14 ── Н2 ── 2 ──────────────── Ч2 ── 19 ─ 7 ─ М5 ─ 1 ─ М3 ── ► 6 ► 4 (к посту)
//                                  4 ── Н4 ── 4 ──────────────── Ч4 ──╯19
//
//  Нечётная (правая) горловина: стрелки 1..19 нечётные, маневровые М1, М3, М5, М7, М9.
//  Чётная (левая) горловина: стрелки 2..16 чётные, маневровые М2, М4, М6, М8, М10.
//  Съезды (спаренные стрелки): 2/4, 6/8, 10/12, 1/3, 5/7, 13/15, 9/11. Одиночные: 14, 16, 17, 19.
//  Слева — тоннель (продолжение макета): путь 1 уходит в тоннель, по пути 2 поезда приходят к входному Ч.
//  Справа — перегон на путевой пост (путь I ← к АКЖТ, путь II → от АКЖТ), он общий с окном поста.
//  Зелёная рамка чертежа (путь 7, стрелки 9/11, М9, депо) — ПРОЕКТ: включается галочкой «Депо».
//
//  Внутренние имена объектов АКЖТ начинаются с "А:", чтобы не путать с постом
//  (там тоже есть Ч, Н, стрелка 1 и т.д.). На экране подписи без приставки.
// =====================================================================

public class AkzhtLayout
{
    public readonly Interlocking IL;
    public readonly bool Depot;
    public readonly StationView View = new();
    SchemeGeometry G => View.Geo;

    public const float Y5 = 150, Y3 = 260, Y1 = 370, Y2 = 480, Y4 = 590;

    readonly HashSet<TrackSection> own = new();

    // ---- участки ----
    public TrackSection UA1, CHU, CH_M2, SP2, SP4, SP6, SP8, SP10, SP12, SP14, SP16, P6T,
                        P1T, P2T, P3T, P4T, P5T,
                        SP5_13, SP3_11, SP19_7, M5P, SP1, SP17, SP15, M7P, H_M1, UII,
                        SP9, P7T, DV, D1P, D2P;
    // ---- стрелки ----
    public RailSwitch SW2_4, SW6_8, SW10_12, SW14, SW16, SW17, SW19, SW5_7, SW13_15, SW1_3, SW9_11, SWD1, SWD2;
    // ---- светофоры ----
    public RailSignal CH, H, H1, H2, H3, H4, H5, CH1, CH2, CH3, CH4, CH5,
                      M1, M2, M3, M4, M5, M6, M7, M8, M9, M10;

    PostLayout post;

    public AkzhtLayout(Interlocking il, bool depot)
    {
        IL = il;
        Depot = depot;
        View.Title = "Станция АКЖТ";
        View.Caption = "Ст. АКЖТ";
        BuildSections();
        BuildSwitches();
        BuildSignals();
    }

    public bool Owns(TrackSection s) => own.Contains(s);

    // =================================================================
    //  УЧАСТКИ
    // =================================================================
    TrackSection S(string label, string title)
    {
        var s = IL.AddSection("А:" + label, title, label);
        own.Add(s);
        View.Sections.Add(s);
        return s;
    }

    void BuildSections()
    {
        // левая (чётная) горловина
        UA1 = S("УАI", "Участок удаления в тоннель по пути 1 (слева от станции)");
        CHU = S("ЧУ", "Участок приближения к входному Ч (из тоннеля по пути 2)");
        CH_M2 = S("Ч-М2", "Участок между входным Ч и М2");
        SP4 = S("4СП", "Стрелочный участок стрелки 4");
        SP2 = S("2СП", "Стрелочный участок стрелки 2");
        SP8 = S("8СП", "Стрелочный участок стрелки 8");
        SP6 = S("6СП", "Стрелочный участок стрелки 6");
        SP10 = S("10СП", "Стрелочный участок стрелки 10");
        SP12 = S("12СП", "Стрелочный участок стрелки 12");
        SP14 = S("14СП", "Стрелочный участок стрелки 14");
        SP16 = S("16СП", "Стрелочный участок стрелки 16");
        P6T = S("6П", "Путь 6 — тупик за М10");

        // станционные пути
        P5T = S("5П", "Приёмо-отправочный путь 5");
        P3T = S("3П", "Приёмо-отправочный путь 3");
        P1T = S("IП", "Главный путь I (путь 1)");
        P2T = S("IIП", "Главный путь II (путь 2)");
        P4T = S("4П", "Приёмо-отправочный путь 4");

        // правая (нечётная) горловина
        SP17 = S("17СП", "Стрелочный участок стрелки 17");
        SP15 = S("15СП", "Стрелочный участок стрелки 15");
        M7P = S("М7П", Depot ? "Участок за М7 (к стрелке 9)" : "Вытяжка за М7 (дальше — проект: путь 7, депо)");
        SP5_13 = S("5-13СП", "Стрелочный участок стрелок 5 и 13");
        SP3_11 = S(Depot ? "3-11СП" : "3СП", Depot ? "Стрелочный участок стрелок 3 и 11" : "Стрелочный участок стрелки 3");
        SP19_7 = S("19-7СП", "Стрелочный участок стрелок 19 и 7");
        M5P = S("М5П", "Участок пути 2 между М5 и стрелкой 1");
        SP1 = S("1СП", "Стрелочный участок стрелки 1");
        H_M1 = S("Н-М1", "Участок между входным Н и М1");
        UII = S("УII", "Участок удаления на перегон по пути II (от М3 до проходного 6)");

        if (Depot)
        {
            SP9 = S("9СП", "Стрелочный участок стрелки 9 (проект)");
            P7T = S("7П", "Путь 7 (проект)");
            DV = S("ДВ", "Вытяжка в депо (проект)");
            D1P = S("Д1П", "Депо, путь 1 (проект)");
            D2P = S("Д2П", "Депо, путь 2 (проект)");
        }
    }

    // =================================================================
    //  СТРЕЛКИ
    // =================================================================
    RailSwitch Sw(string label, string title, params TrackSection[] secs)
    {
        var sw = IL.AddSwitchL("А:" + label, label, title, secs);
        View.Switches.Add(sw);
        return sw;
    }

    void BuildSwitches()
    {
        SW2_4 = Sw("2/4", "Съезд 2/4 между путями 1 и 2 (левая горловина)", SP2, SP4);
        SW6_8 = Sw("6/8", "Съезд 6/8 между путями 2 и 1", SP6, SP8);
        SW10_12 = Sw("10/12", "Съезд 10/12 между путями 1 и 3", SP10, SP12);
        SW14 = Sw("14", "Стрелка 14 на путь 4", SP14);
        SW16 = Sw("16", "Стрелка 16 на путь 5", SP16);
        SW17 = Sw("17", "Стрелка 17 (путь 5 → путь 3)", SP17);
        SW19 = Sw("19", "Стрелка 19 (путь 4 → путь 2)", SP19_7);
        SW5_7 = Sw("5/7", "Съезд 5/7 между путями 2 и 1 (правая горловина)", SP5_13, SP19_7);
        SW13_15 = Sw("13/15", "Съезд 13/15 между путями 3 и 1", SP5_13, SP15);
        SW1_3 = Sw("1/3", "Съезд 1/3 между путями 1 и 2 (выход на перегон)", SP1, SP3_11);
        if (Depot)
        {
            SW9_11 = Sw("9/11", "Съезд 9/11 между путями 1 и 3/7 (проект)", SP3_11, SP9);
            SWD1 = Sw("Д1", "Стрелка на депо (проект, номер на чертеже не указан)", P7T);
            SWD2 = Sw("Д2", "Стрелка между путями депо (проект)", DV);
        }
    }

    // =================================================================
    //  СВЕТОФОРЫ
    // =================================================================
    // лампы сверху вниз по мачте (как у Алгабаса): входной — Ж З К Ж Б, выходной — Ж З К Б
    static readonly string[] L_ENTRY = { "yellow", "green", "red", "yellow1", "white" };
    static readonly string[] L_EXIT = { "yellow", "green", "red", "white" };
    static readonly string[] L_SHUNT_BLUE = { "white", "blue" };
    static readonly string[] L_SHUNT_RED = { "white", "red" };

    RailSignal Sig(string label, string title, Dir dir, string[] lamps) =>
        IL.AddSignal(new RailSignal { Id = "А:" + label, Label = label, Title = title, Kind = SignalKind.Controlled, Dir = dir, Lamps = lamps });

    void BuildSignals()
    {
        CH = Sig("Ч", "Входной Ч станции АКЖТ (чётное направление, из тоннеля по пути 2)", Dir.Right, L_ENTRY);
        CH.Approach = CHU;
        H = Sig("Н", "Входной Н станции АКЖТ (нечётное направление, с перегона от путевого поста)", Dir.Left, L_ENTRY);

        H1 = Sig("Н1", "Выходной Н1 с пути 1 (в тоннель)", Dir.Left, L_EXIT);
        H2 = Sig("Н2", "Выходной Н2 с пути 2 (в тоннель)", Dir.Left, L_EXIT);
        H3 = Sig("Н3", "Выходной Н3 с пути 3 (в тоннель)", Dir.Left, L_EXIT);
        H4 = Sig("Н4", "Выходной Н4 с пути 4 (в тоннель)", Dir.Left, L_EXIT);
        H5 = Sig("Н5", "Выходной Н5 с пути 5 (в тоннель)", Dir.Left, L_EXIT);
        CH1 = Sig("Ч1", "Выходной Ч1 с пути 1 (на перегон к посту)", Dir.Right, L_EXIT);
        CH2 = Sig("Ч2", "Выходной Ч2 с пути 2 (на перегон к посту)", Dir.Right, L_EXIT);
        CH3 = Sig("Ч3", "Выходной Ч3 с пути 3 (на перегон к посту)", Dir.Right, L_EXIT);
        CH4 = Sig("Ч4", "Выходной Ч4 с пути 4 (на перегон к посту)", Dir.Right, L_EXIT);
        CH5 = Sig("Ч5", "Выходной Ч5 с пути 5 (на перегон к посту)", Dir.Right, L_EXIT);

        // маневровые: чётные — левая горловина (движение →), нечётные — правая (движение ←)
        M2 = Sig("М2", "Маневровый М2", Dir.Right, L_SHUNT_BLUE);
        M4 = Sig("М4", "Маневровый М4 (с пути 1 от тоннеля)", Dir.Right, L_SHUNT_BLUE);
        M6 = Sig("М6", "Маневровый М6", Dir.Right, L_SHUNT_BLUE);
        M8 = Sig("М8", "Маневровый М8", Dir.Right, L_SHUNT_BLUE);
        M10 = Sig("М10", "Маневровый М10 (с тупика 6)", Dir.Right, L_SHUNT_RED);
        M1 = Sig("М1", "Маневровый М1", Dir.Left, L_SHUNT_BLUE);
        M3 = Sig("М3", "Маневровый М3 (с участка удаления пути II)", Dir.Left, L_SHUNT_BLUE);
        M5 = Sig("М5", "Маневровый М5", Dir.Left, L_SHUNT_BLUE);
        M7 = Sig("М7", "Маневровый М7", Dir.Left, L_SHUNT_BLUE);
        if (Depot) M9 = Sig("М9", "Маневровый М9 (с пути 7, проект)", Dir.Left, L_SHUNT_RED);

        // приближение для искусственной разделки маневровых
        M2.Approach = CH_M2; M6.Approach = SP2; M8.Approach = SP8; M4.Approach = UA1; M10.Approach = P6T;
        M1.Approach = H_M1; M3.Approach = UII; M5.Approach = M5P; M7.Approach = M7P;
        H1.Approach = P1T; H2.Approach = P2T; H3.Approach = P3T; H4.Approach = P4T; H5.Approach = P5T;
        CH1.Approach = P1T; CH2.Approach = P2T; CH3.Approach = P3T; CH4.Approach = P4T; CH5.Approach = P5T;
        if (Depot) M9.Approach = P7T;

        View.InviteSignals.Add(CH);
        View.InviteSignals.Add(H);
    }

    // =================================================================
    //  СВЯЗЬ С ПОСТОМ + МАРШРУТЫ + ГЕОМЕТРИЯ (когда пост уже создан)
    // =================================================================
    public void Link(PostLayout p)
    {
        post = p;
        H.Approach = p.S_1BU;
        BuildRoutes();
        BuildGeometry();
        foreach (var r in View.Routes) r.Pieces = RoutePainter.Compute(G, r);

        View.SpawnButtons.Add(("Поезд из тоннеля к Ч", () => Trains?.Spawn(CHU, Dir.Right)));
        // блок-участки перегона, которые нарисованы только на схеме АКЖТ
        View.Sections.Add(p.S_1BU);
        View.Sections.Add(p.S_3BU);
        View.Sections.Add(p.S_6BU);

        View.TrainPlaces.Add(("Путь 1", P1T));
        View.TrainPlaces.Add(("Путь 2", P2T));
        View.TrainPlaces.Add(("Путь 3", P3T));
        View.TrainPlaces.Add(("Путь 4", P4T));
        View.TrainPlaces.Add(("Путь 5", P5T));
        View.TrainPlaces.Add(("Тупик 6", P6T));
        if (Depot) View.TrainPlaces.Add(("Путь 7", P7T));
    }

    /// <summary>Виртуальные поезда (общие для всей модели) — выставляет RailModel.</summary>
    public TrainSim Trains;

    // ---------------- маршруты ----------------
    (RailSwitch, bool) P(RailSwitch sw) => (sw, true);
    (RailSwitch, bool) M(RailSwitch sw) => (sw, false);

    RailRoute Route(bool shunt, RailSignal start, RailSignal end, TrackSection dest, string name,
                    TrackSection[] secs, (RailSwitch sw, bool plus)[] sws, bool div = false, TrackSection exit = null)
    {
        var r = new RailRoute
        {
            Id = "А:" + start.Label + ">" + name,
            Name = name,
            Shunting = shunt,
            Start = start,
            End = end,
            Dest = dest,
            Diverging = div,
            ExitSection = exit,
        };
        r.Sections.AddRange(secs);
        // стрелки 9/11 и депо существуют только в варианте "с депо"
        foreach (var x in sws) if (x.sw != null) r.Switches.Add(x);
        IL.AddRoute(r);
        View.Routes.Add(r);
        return r;
    }

    void BuildRoutes()
    {
        var S911 = SW9_11;   // null без депо — такие позиции просто пропускаются

        // ======== ПОЕЗДНЫЕ ========
        // --- приём с тоннеля на входной Ч ---
        Route(false, CH, CH1, P1T, "Ч → путь 1", new[] { CH_M2, SP2, SP6, SP8, SP10, P1T }, new[] { P(SW2_4), M(SW6_8), P(SW10_12) }, div: true);
        Route(false, CH, CH2, P2T, "Ч → путь 2", new[] { CH_M2, SP2, SP6, SP14, P2T }, new[] { P(SW2_4), P(SW6_8), P(SW14) });
        Route(false, CH, CH3, P3T, "Ч → путь 3", new[] { CH_M2, SP2, SP6, SP8, SP10, SP12, SP16, P3T }, new[] { P(SW2_4), M(SW6_8), M(SW10_12), P(SW16) }, div: true);
        Route(false, CH, CH4, P4T, "Ч → путь 4", new[] { CH_M2, SP2, SP6, SP14, P4T }, new[] { P(SW2_4), P(SW6_8), M(SW14) }, div: true);
        Route(false, CH, CH5, P5T, "Ч → путь 5", new[] { CH_M2, SP2, SP6, SP8, SP10, SP12, SP16, P5T }, new[] { P(SW2_4), M(SW6_8), M(SW10_12), M(SW16) }, div: true);

        // --- приём с перегона от поста на входной Н ---
        Route(false, H, H1, P1T, "Н → путь 1", new[] { H_M1, SP3_11, SP5_13, P1T }, new[] { P(S911), P(SW1_3), P(SW13_15), P(SW5_7) });
        Route(false, H, H2, P2T, "Н → путь 2", new[] { H_M1, SP3_11, SP5_13, SP19_7, P2T }, new[] { P(S911), P(SW1_3), P(SW13_15), M(SW5_7), P(SW19) }, div: true);
        Route(false, H, H3, P3T, "Н → путь 3", new[] { H_M1, SP3_11, SP5_13, SP15, SP17, P3T }, new[] { P(S911), P(SW1_3), M(SW13_15), P(SW17) }, div: true);
        Route(false, H, H4, P4T, "Н → путь 4", new[] { H_M1, SP3_11, SP5_13, SP19_7, P4T }, new[] { P(S911), P(SW1_3), P(SW13_15), M(SW5_7), M(SW19) }, div: true);
        Route(false, H, H5, P5T, "Н → путь 5", new[] { H_M1, SP3_11, SP5_13, SP15, SP17, P5T }, new[] { P(S911), P(SW1_3), M(SW13_15), M(SW17) }, div: true);

        // --- отправление в тоннель (налево) ---
        Route(false, H1, null, UA1, "Н1 → тоннель", new[] { SP10, SP8, SP4 }, new[] { P(SW10_12), P(SW6_8), P(SW2_4) }, exit: UA1);
        Route(false, H2, null, UA1, "Н2 → тоннель", new[] { SP14, SP6, SP2, SP4 }, new[] { P(SW14), P(SW6_8), M(SW2_4) }, div: true, exit: UA1);
        Route(false, H4, null, UA1, "Н4 → тоннель", new[] { SP14, SP6, SP2, SP4 }, new[] { M(SW14), P(SW6_8), M(SW2_4) }, div: true, exit: UA1);
        Route(false, H3, null, UA1, "Н3 → тоннель", new[] { SP16, SP12, SP10, SP8, SP4 }, new[] { P(SW16), M(SW10_12), P(SW6_8), P(SW2_4) }, div: true, exit: UA1);
        Route(false, H5, null, UA1, "Н5 → тоннель", new[] { SP16, SP12, SP10, SP8, SP4 }, new[] { M(SW16), M(SW10_12), P(SW6_8), P(SW2_4) }, div: true, exit: UA1);

        // --- отправление на перегон к посту (направо, путь II) ---
        Route(false, CH1, post.P6, UII, "Ч1 → перегон (6)", new[] { SP5_13, SP3_11, SP1 }, new[] { P(SW5_7), P(SW13_15), M(SW1_3) }, div: true, exit: UII);
        Route(false, CH2, post.P6, UII, "Ч2 → перегон (6)", new[] { SP19_7, M5P, SP1 }, new[] { P(SW19), P(SW5_7), P(SW1_3) }, exit: UII);
        Route(false, CH3, post.P6, UII, "Ч3 → перегон (6)", new[] { SP17, SP15, SP5_13, SP3_11, SP1 }, new[] { P(SW17), M(SW13_15), M(SW1_3) }, div: true, exit: UII);
        Route(false, CH4, post.P6, UII, "Ч4 → перегон (6)", new[] { SP19_7, M5P, SP1 }, new[] { M(SW19), P(SW5_7), P(SW1_3) }, div: true, exit: UII);
        Route(false, CH5, post.P6, UII, "Ч5 → перегон (6)", new[] { SP17, SP15, SP5_13, SP3_11, SP1 }, new[] { M(SW17), M(SW13_15), M(SW1_3) }, div: true, exit: UII);

        // ======== МАНЕВРОВЫЕ ========
        // --- левая горловина, движение → ---
        Route(true, M2, M6, SP6, "М2 → М6", new[] { SP2 }, new[] { P(SW2_4) });
        Route(true, M4, M8, SP10, "М4 → М8", new[] { SP4, SP8 }, new[] { P(SW2_4), P(SW6_8) });
        Route(true, M4, M6, SP6, "М4 → М6", new[] { SP4, SP2 }, new[] { M(SW2_4) });
        Route(true, M6, H2, P2T, "М6 → путь 2", new[] { SP6, SP14 }, new[] { P(SW6_8), P(SW14) });
        Route(true, M6, H4, P4T, "М6 → путь 4", new[] { SP6, SP14 }, new[] { P(SW6_8), M(SW14) });
        Route(true, M6, M8, SP10, "М6 → М8", new[] { SP6, SP8 }, new[] { M(SW6_8) });
        Route(true, M8, H1, P1T, "М8 → путь 1", new[] { SP10 }, new[] { P(SW10_12) });
        Route(true, M8, H3, P3T, "М8 → путь 3", new[] { SP10, SP12, SP16 }, new[] { M(SW10_12), P(SW16) });
        Route(true, M8, H5, P5T, "М8 → путь 5", new[] { SP10, SP12, SP16 }, new[] { M(SW10_12), M(SW16) });
        Route(true, M10, H3, P3T, "М10 → путь 3", new[] { SP12, SP16 }, new[] { P(SW10_12), P(SW16) });
        Route(true, M10, H5, P5T, "М10 → путь 5", new[] { SP12, SP16 }, new[] { P(SW10_12), M(SW16) });
        // --- левая горловина, движение ← (белый на выходных Н) ---
        Route(true, H1, M8, SP8, "Н1 → М8", new[] { SP10 }, new[] { P(SW10_12) });
        Route(true, H1, M4, UA1, "Н1 → за М4", new[] { SP10, SP8, SP4 }, new[] { P(SW10_12), P(SW6_8), P(SW2_4) });
        Route(true, H1, M6, SP2, "Н1 → М6", new[] { SP10, SP8, SP6 }, new[] { P(SW10_12), M(SW6_8) });
        Route(true, H2, M6, SP2, "Н2 → М6", new[] { SP14, SP6 }, new[] { P(SW14), P(SW6_8) });
        Route(true, H4, M6, SP2, "Н4 → М6", new[] { SP14, SP6 }, new[] { M(SW14), P(SW6_8) });
        Route(true, H2, M2, CH_M2, "Н2 → М2", new[] { SP14, SP6, SP2 }, new[] { P(SW14), P(SW6_8), P(SW2_4) });
        Route(true, H4, M2, CH_M2, "Н4 → М2", new[] { SP14, SP6, SP2 }, new[] { M(SW14), P(SW6_8), P(SW2_4) });
        Route(true, H3, M10, P6T, "Н3 → тупик 6", new[] { SP16, SP12 }, new[] { P(SW16), P(SW10_12) });
        Route(true, H5, M10, P6T, "Н5 → тупик 6", new[] { SP16, SP12 }, new[] { M(SW16), P(SW10_12) });
        Route(true, H3, M8, SP8, "Н3 → М8", new[] { SP16, SP12, SP10 }, new[] { P(SW16), M(SW10_12) });
        Route(true, H5, M8, SP8, "Н5 → М8", new[] { SP16, SP12, SP10 }, new[] { M(SW16), M(SW10_12) });
        // --- правая горловина, движение → (белый на выходных Ч) ---
        Route(true, CH1, M1, H_M1, "Ч1 → М1", new[] { SP5_13, SP3_11 }, new[] { P(SW5_7), P(SW13_15), P(SW1_3), P(S911) });
        Route(true, CH1, M3, UII, "Ч1 → М3", new[] { SP5_13, SP3_11, SP1 }, new[] { P(SW5_7), P(SW13_15), M(SW1_3) });
        Route(true, CH2, M5, M5P, "Ч2 → М5", new[] { SP19_7 }, new[] { P(SW19), P(SW5_7) });
        Route(true, CH4, M5, M5P, "Ч4 → М5", new[] { SP19_7 }, new[] { M(SW19), P(SW5_7) });
        Route(true, CH2, M1, H_M1, "Ч2 → М1", new[] { SP19_7, SP5_13, SP3_11 }, new[] { P(SW19), M(SW5_7), P(SW13_15), P(SW1_3), P(S911) });
        Route(true, CH4, M1, H_M1, "Ч4 → М1", new[] { SP19_7, SP5_13, SP3_11 }, new[] { M(SW19), M(SW5_7), P(SW13_15), P(SW1_3), P(S911) });
        Route(true, CH3, M7, M7P, "Ч3 → М7", new[] { SP17, SP15 }, new[] { P(SW17), P(SW13_15) });
        Route(true, CH5, M7, M7P, "Ч5 → М7", new[] { SP17, SP15 }, new[] { M(SW17), P(SW13_15) });
        Route(true, CH3, M1, H_M1, "Ч3 → М1", new[] { SP17, SP15, SP5_13, SP3_11 }, new[] { P(SW17), M(SW13_15), P(SW1_3), P(S911) });
        Route(true, CH5, M1, H_M1, "Ч5 → М1", new[] { SP17, SP15, SP5_13, SP3_11 }, new[] { M(SW17), M(SW13_15), P(SW1_3), P(S911) });
        // --- правая горловина, движение ← ---
        Route(true, M1, CH1, P1T, "М1 → путь 1", new[] { SP3_11, SP5_13 }, new[] { P(S911), P(SW1_3), P(SW13_15), P(SW5_7) });
        Route(true, M1, CH2, P2T, "М1 → путь 2", new[] { SP3_11, SP5_13, SP19_7 }, new[] { P(S911), P(SW1_3), P(SW13_15), M(SW5_7), P(SW19) });
        Route(true, M1, CH4, P4T, "М1 → путь 4", new[] { SP3_11, SP5_13, SP19_7 }, new[] { P(S911), P(SW1_3), P(SW13_15), M(SW5_7), M(SW19) });
        Route(true, M1, CH3, P3T, "М1 → путь 3", new[] { SP3_11, SP5_13, SP15, SP17 }, new[] { P(S911), P(SW1_3), M(SW13_15), P(SW17) });
        Route(true, M1, CH5, P5T, "М1 → путь 5", new[] { SP3_11, SP5_13, SP15, SP17 }, new[] { P(S911), P(SW1_3), M(SW13_15), M(SW17) });
        Route(true, M3, M5, SP19_7, "М3 → М5", new[] { SP1, M5P }, new[] { P(SW1_3) });
        Route(true, M3, CH1, P1T, "М3 → путь 1", new[] { SP1, SP3_11, SP5_13 }, new[] { M(SW1_3), P(SW13_15), P(SW5_7) });
        Route(true, M3, CH2, P2T, "М3 → путь 2", new[] { SP1, SP3_11, SP5_13, SP19_7 }, new[] { M(SW1_3), P(SW13_15), M(SW5_7), P(SW19) });
        Route(true, M3, CH4, P4T, "М3 → путь 4", new[] { SP1, SP3_11, SP5_13, SP19_7 }, new[] { M(SW1_3), P(SW13_15), M(SW5_7), M(SW19) });
        Route(true, M3, CH3, P3T, "М3 → путь 3", new[] { SP1, SP3_11, SP5_13, SP15, SP17 }, new[] { M(SW1_3), M(SW13_15), P(SW17) });
        Route(true, M3, CH5, P5T, "М3 → путь 5", new[] { SP1, SP3_11, SP5_13, SP15, SP17 }, new[] { M(SW1_3), M(SW13_15), M(SW17) });
        Route(true, M5, CH2, P2T, "М5 → путь 2", new[] { SP19_7 }, new[] { P(SW5_7), P(SW19) });
        Route(true, M5, CH4, P4T, "М5 → путь 4", new[] { SP19_7 }, new[] { P(SW5_7), M(SW19) });
        Route(true, M7, CH3, P3T, "М7 → путь 3", new[] { SP15, SP17 }, new[] { P(SW13_15), P(SW17) });
        Route(true, M7, CH5, P5T, "М7 → путь 5", new[] { SP15, SP17 }, new[] { P(SW13_15), M(SW17) });

        if (Depot)
        {
            // --- проектная часть: путь 7 ---
            Route(true, M9, M7, SP15, "М9 → М7", new[] { SP9, M7P }, new[] { P(SW9_11) });
            Route(true, M9, CH1, P1T, "М9 → путь 1", new[] { SP9, SP3_11, SP5_13 }, new[] { M(SW9_11), P(SW1_3), P(SW13_15), P(SW5_7) });
            Route(true, CH3, M9, P7T, "Ч3 → путь 7", new[] { SP17, SP15, M7P, SP9 }, new[] { P(SW17), P(SW13_15), P(SW9_11) });
            Route(true, CH5, M9, P7T, "Ч5 → путь 7", new[] { SP17, SP15, M7P, SP9 }, new[] { M(SW17), P(SW13_15), P(SW9_11) });
            Route(true, CH1, M9, P7T, "Ч1 → путь 7", new[] { SP5_13, SP3_11, SP9 }, new[] { P(SW5_7), P(SW13_15), P(SW1_3), M(SW9_11) });
        }
    }

    /// <summary>Маршрут по началу и "цели" (светофор конца или путь назначения).
    /// Если таких два (поездной и маневровый) — берём нужной категории.</summary>
    public static RailRoute Find(IEnumerable<RailRoute> routes, RailSignal start, object target, bool shunting)
    {
        bool Match(RailRoute r) => r.Start == start && (target is RailSignal sg ? r.End == sg : target is TrackSection ts && r.Dest == ts);
        return routes.FirstOrDefault(r => Match(r) && r.Shunting == shunting) ?? routes.FirstOrDefault(Match);
    }

    // =================================================================
    //  ТОПОЛОГИЯ: куда поезд поедет из участка
    // =================================================================
    public TrackStep Next(TrackSection s, Dir d)
    {
        TrackStep To(TrackSection x, Dir nd, RailSignal sig = null) => new() { Section = x, Dir = nd, Signal = sig };
        bool p24 = SW2_4.Plus, p68 = SW6_8.Plus, p1012 = SW10_12.Plus, p14 = SW14.Plus, p16 = SW16.Plus,
             p17 = SW17.Plus, p19 = SW19.Plus, p57 = SW5_7.Plus, p1315 = SW13_15.Plus, p13 = SW1_3.Plus,
             p911 = SW9_11?.Plus ?? true;

        if (d == Dir.Right)
        {
            if (s == CHU) return To(CH_M2, d, CH);
            if (s == CH_M2) return p24 ? To(SP2, d, M2) : null;
            if (s == UA1) return To(SP4, d, M4);
            if (s == SP4) return p24 ? (p68 ? To(SP8, d) : null) : To(SP2, d);
            if (s == SP2) return To(SP6, d, M6);
            if (s == SP6) return p68 ? To(SP14, d) : To(SP8, d);
            if (s == SP14) return p14 ? To(P2T, d) : To(P4T, d);
            if (s == SP8) return To(SP10, d, M8);
            if (s == SP10) return p1012 ? To(P1T, d) : To(SP12, d);
            if (s == P6T) return p1012 ? To(SP12, d, M10) : null;
            if (s == SP12) return To(SP16, d);
            if (s == SP16) return p16 ? To(P3T, d) : To(P5T, d);

            // со станционного пути — всегда через выходной светофор: он откроется только по маршруту,
            // а маршрут сам поставит стрелки как надо
            if (s == P1T) return To(SP5_13, d, CH1);
            if (s == P2T) return To(SP19_7, d, CH2);
            if (s == P4T) return To(SP19_7, d, CH4);
            if (s == P3T) return To(SP17, d, CH3);
            if (s == P5T) return To(SP17, d, CH5);
            if (s == SP17) return To(SP15, d);
            if (s == SP15) return p1315 ? To(M7P, d) : To(SP5_13, d);
            if (s == M7P) return Depot ? (p911 ? To(SP9, d) : null) : new TrackStep { Buffer = true, Dir = d };
            if (s == SP19_7) return p57 ? To(M5P, d) : (p1315 ? To(SP5_13, d) : null);
            if (s == M5P) return p13 ? To(SP1, d) : null;
            if (s == SP5_13) return To(SP3_11, d);
            if (s == SP3_11) return !p13 ? To(SP1, d) : (p911 ? To(H_M1, d) : To(SP9, d));
            if (s == H_M1) return null;                  // дальше — путь I перегона навстречу поездам: не едем
            if (s == SP1) return To(UII, d);
            if (s == UII) return To(post.S_6BU, d, post.P6);
            if (Depot)
            {
                if (s == SP9) return To(P7T, d);
                if (s == P7T) return new TrackStep { Buffer = true, Dir = d };
            }
            return null;
        }
        else
        {
            if (s == H_M1) return p911 && p13 ? To(SP3_11, d, M1) : null;
            if (s == SP3_11) return To(SP5_13, d);
            if (s == SP5_13) return !p1315 ? To(SP15, d) : (p57 ? To(P1T, d) : To(SP19_7, d));
            if (s == SP15) return To(SP17, d);
            if (s == SP17) return p17 ? To(P3T, d) : To(P5T, d);
            if (s == M7P) return p1315 ? To(SP15, d, M7) : null;
            if (s == SP19_7) return p19 ? To(P2T, d) : To(P4T, d);
            if (s == M5P) return p57 ? To(SP19_7, d, M5) : null;
            if (s == SP1) return p13 ? To(M5P, d) : To(SP3_11, d);
            if (s == UII) return To(SP1, d, M3);

            if (s == P1T) return To(SP10, d, H1);
            if (s == P2T) return To(SP14, d, H2);
            if (s == P4T) return To(SP14, d, H4);
            if (s == P3T) return To(SP16, d, H3);
            if (s == P5T) return To(SP16, d, H5);
            if (s == SP16) return To(SP12, d);
            if (s == SP12) return p1012 ? To(P6T, d) : To(SP10, d);
            if (s == P6T) return new TrackStep { Buffer = true, Dir = d };
            if (s == SP10) return To(SP8, d);
            if (s == SP8) return p68 ? (p24 ? To(SP4, d) : null) : To(SP6, d);
            if (s == SP14) return p68 ? To(SP6, d) : null;
            if (s == SP6) return To(SP2, d);
            if (s == SP2) return p24 ? To(CH_M2, d) : To(SP4, d);
            if (s == CH_M2) return null;                 // к тоннелю по пути 2 навстречу не едем
            if (s == SP4) return To(UA1, d);
            if (s == UA1) return new TrackStep { Exit = true, ExitName = "тоннель (слева от АКЖТ)", Dir = d };
            if (s == CHU) return null;
            if (Depot)
            {
                if (s == SP9) return p911 ? To(M7P, d) : (p13 ? To(SP3_11, d) : null);
                if (s == P7T) return To(SP9, d, M9);
            }
            return null;
        }
    }

    // =================================================================
    //  ГЕОМЕТРИЯ
    // =================================================================
    void BuildGeometry()
    {
        var g = G;
        g.DesignW = 2600;
        g.DesignH = 700;
        string id(TrackSection s) => s.Id;

        // ---------- тоннель слева ----------
        g.Tunnels.Add(new TunnelGeom { Portal = new PointF(60, Y1), OpensRight = false, Length = 45, Label = "" });
        g.Tunnels.Add(new TunnelGeom { Portal = new PointF(60, Y2), OpensRight = false, Length = 45, Label = "" });
        g.Text("Тоннель", 18, (Y1 + Y2) / 2 - 8, 9.5f, true);
        g.Text("(продолжение)", 8, (Y1 + Y2) / 2 + 8, 8f);

        // ---------- путь 1 ----------
        g.Piece("A_UA1", id(UA1), (60, Y1), (254, Y1));
        g.Piece("A_4c", id(SP4), (254, Y1), (310, Y1));                                     // до остряка стрелки 4
        g.Branch("A_4s", id(SP4), SW2_4.Id, true, true, (310, Y1), (510, Y1));
        g.Branch("A_4d", id(SP4), SW2_4.Id, false, true, (310, Y1), (360, (Y1 + Y2) / 2));
        g.Branch("A_8s", id(SP8), SW6_8.Id, true, false, (510, Y1), (616, Y1));
        g.Branch("A_8d", id(SP8), SW6_8.Id, false, false, (571, (Y1 + Y2) / 2), (616, Y1));
        g.Piece("A_8c", id(SP8), (616, Y1), (654, Y1));
        g.Piece("A_10c", id(SP10), (654, Y1), (680, Y1));
        g.Branch("A_10s", id(SP10), SW10_12.Id, true, true, (680, Y1), (850, Y1));
        g.Branch("A_10d", id(SP10), SW10_12.Id, false, true, (680, Y1), (728, (Y1 + Y3) / 2));
        g.Piece("A_P1", id(P1T), (850, Y1), (1428, Y1));
        g.Branch("A_5s", id(SP5_13), SW5_7.Id, true, false, (1428, Y1), (1614, Y1));
        g.Branch("A_5d", id(SP5_13), SW5_7.Id, false, false, (1564, (Y1 + Y2) / 2), (1614, Y1));
        g.Branch("A_13s", id(SP5_13), SW13_15.Id, true, false, (1614, Y1), (1642, Y1));
        g.Branch("A_13d", id(SP5_13), SW13_15.Id, false, false, (1592, (Y1 + Y3) / 2), (1642, Y1));
        g.Piece("A_13c", id(SP5_13), (1642, Y1), (1676, Y1));
        g.Piece("A_3c", id(SP3_11), (1676, Y1), (1694, Y1));
        g.Branch("A_3d", id(SP3_11), SW1_3.Id, false, true, (1694, Y1), (1747, (Y1 + Y2) / 2));
        if (Depot)
        {
            g.Branch("A_3s", id(SP3_11), SW1_3.Id, true, true, (1694, Y1), (1740, Y1));
            g.Branch("A_11s", id(SP3_11), SW9_11.Id, true, true, (1740, Y1), (1826, Y1));
            g.Branch("A_11d", id(SP3_11), SW9_11.Id, false, true, (1740, Y1), (1794, (Y1 + Y3) / 2));
        }
        else
        {
            g.Branch("A_3s", id(SP3_11), SW1_3.Id, true, true, (1694, Y1), (1826, Y1));
        }
        g.Piece("A_HM1", id(H_M1), (1826, Y1), (2052, Y1));
        g.Piece("A_1BU", post.S_1BU.Id, (2052, Y1), (2268, Y1));
        g.Piece("A_3BU", post.S_3BU.Id, (2268, Y1), (2520, Y1));
        // [ИЗМЕНЕНО 08.10.2026] 5БУ и 4БУ доходят до правого края (x = DesignW): там на соседнем
        // мониторе начинается схема поста, и пути продолжаются без разрыва
        g.Piece("A_5BU", post.S_5BU.Id, (2520, Y1), (2600, Y1));

        // ---------- путь 2 ----------
        g.Piece("A_CHU", id(CHU), (60, Y2), (210, Y2));
        g.Piece("A_CHM2", id(CH_M2), (210, Y2), (340, Y2));
        g.Branch("A_2s", id(SP2), SW2_4.Id, true, false, (340, Y2), (410, Y2));
        g.Branch("A_2d", id(SP2), SW2_4.Id, false, false, (360, (Y1 + Y2) / 2), (410, Y2));
        g.Piece("A_2c", id(SP2), (410, Y2), (444, Y2));
        g.Piece("A_6c", id(SP6), (444, Y2), (526, Y2));
        g.Branch("A_6s", id(SP6), SW6_8.Id, true, true, (526, Y2), (590, Y2));
        g.Branch("A_6d", id(SP6), SW6_8.Id, false, true, (526, Y2), (571, (Y1 + Y2) / 2));
        g.Piece("A_14c", id(SP14), (590, Y2), (620, Y2));
        g.Branch("A_14s", id(SP14), SW14.Id, true, true, (620, Y2), (754, Y2));
        g.Branch("A_14d", id(SP14), SW14.Id, false, true, (620, Y2), (706, Y4), (790, Y4));
        g.Piece("A_P2", id(P2T), (754, Y2), (1328, Y2));
        g.Branch("A_19s", id(SP19_7), SW19.Id, true, false, (1328, Y2), (1492, Y2));
        g.Branch("A_19d", id(SP19_7), SW19.Id, false, false, (1328, Y4), (1414, Y4), (1492, Y2));
        g.Piece("A_19c", id(SP19_7), (1492, Y2), (1514, Y2));
        g.Branch("A_7s", id(SP19_7), SW5_7.Id, true, true, (1514, Y2), (1604, Y2));
        g.Branch("A_7d", id(SP19_7), SW5_7.Id, false, true, (1514, Y2), (1564, (Y1 + Y2) / 2));
        g.Piece("A_M5P", id(M5P), (1604, Y2), (1734, Y2));
        g.Branch("A_1s", id(SP1), SW1_3.Id, true, false, (1734, Y2), (1800, Y2));
        g.Branch("A_1d", id(SP1), SW1_3.Id, false, false, (1747, (Y1 + Y2) / 2), (1800, Y2));
        g.Piece("A_1c", id(SP1), (1800, Y2), (1860, Y2));
        g.Piece("A_UII", id(UII), (1860, Y2), (2058, Y2));
        g.Piece("A_6BU", post.S_6BU.Id, (2058, Y2), (2358, Y2));
        g.Piece("A_4BU", post.S_4BU.Id, (2358, Y2), (2600, Y2));

        // ---------- путь 4 ----------
        g.Piece("A_P4", id(P4T), (790, Y4), (1328, Y4));

        // ---------- путь 3 и тупик 6 ----------
        g.Piece("A_P6", id(P6T), (570, Y3), (684, Y3));
        g.Buffers.Add(new PointF(570, Y3));
        g.Branch("A_12s", id(SP12), SW10_12.Id, true, false, (684, Y3), (776, Y3));
        g.Branch("A_12d", id(SP12), SW10_12.Id, false, false, (728, (Y1 + Y3) / 2), (776, Y3));
        g.Piece("A_12c", id(SP12), (776, Y3), (810, Y3));
        g.Piece("A_16c", id(SP16), (810, Y3), (830, Y3));
        g.Branch("A_16s", id(SP16), SW16.Id, true, true, (830, Y3), (980, Y3));
        g.Branch("A_16d", id(SP16), SW16.Id, false, true, (830, Y3), (910, Y5), (980, Y5));
        g.Piece("A_P3", id(P3T), (980, Y3), (1308, Y3));
        g.Branch("A_17s", id(SP17), SW17.Id, true, false, (1308, Y3), (1432, Y3));
        g.Branch("A_17d", id(SP17), SW17.Id, false, false, (1270, Y5), (1354, Y5), (1432, Y3));
        // [ИЗМЕНЕНО 20.10.2026, правки преподавателя] изостык 17СП / 15СП перенесён к стрелке 17 (x = 1432,
        // где сходятся пути 3 и 5): отрезок до стрелки 15 теперь относится к 15СП
        g.Piece("A_17c", id(SP15), (1432, Y3), (1492, Y3));
        g.Piece("A_15c", id(SP15), (1492, Y3), (1542, Y3));
        g.Branch("A_15s", id(SP15), SW13_15.Id, true, true, (1542, Y3), (1604, Y3));
        g.Branch("A_15d", id(SP15), SW13_15.Id, false, true, (1542, Y3), (1592, (Y1 + Y3) / 2));
        if (Depot)
        {
            g.Piece("A_M7P", id(M7P), (1604, Y3), (1784, Y3));
            g.Piece("A_9c", id(SP9), (1784, Y3), (1848, Y3)); // до остряка? — нет: у 9 две ветки слева
        }
        else
        {
            g.Piece("A_M7P", id(M7P), (1604, Y3), (1700, Y3));
            g.Buffers.Add(new PointF(1700, Y3));
        }

        // ---------- путь 5 ----------
        g.Piece("A_P5", id(P5T), (980, Y5), (1270, Y5));

        // ---------- проект: путь 7 и депо ----------
        if (Depot)
        {
            // у стрелки 9 две ветки слева: путь 3 (прямо) и съезд от 11 (отклонение)
            g.Pieces.RemoveAll(x => x.Id == "A_9c");
            g.Branch("A_9s", id(SP9), SW9_11.Id, true, false, (1784, Y3), (1848, Y3));
            g.Branch("A_9d", id(SP9), SW9_11.Id, false, false, (1794, (Y1 + Y3) / 2), (1848, Y3));
            g.Piece("A_9c", id(SP9), (1848, Y3), (1934, Y3));
            g.Branch("A_D1s", id(P7T), SWD1.Id, true, false, (1934, Y3), (2036, Y3));
            g.Branch("A_D1d", id(P7T), SWD1.Id, false, false, (1956, Y5), (2036, Y3));
            g.Piece("A_P7", id(P7T), (2036, Y3), (2242, Y3));
            g.Buffers.Add(new PointF(2242, Y3));
            g.Branch("A_D2s", id(DV), SWD2.Id, true, true, (1956, Y5), (1880, Y5));
            g.Branch("A_D2d", id(DV), SWD2.Id, false, true, (1956, Y5), (1916, 100));
            g.Piece("A_D1P", id(D1P), (1880, Y5), (1680, Y5));
            g.Piece("A_D2P", id(D2P), (1916, 100), (1680, 100));
            g.Buffers.Add(new PointF(1680, Y5));
            g.Buffers.Add(new PointF(1680, 100));
            g.Text("Депо (проект)", 1690, 70, 10f, true);
            g.Text("7", 2090, Y3 - 34, 16f, true, true);
        }

        // ---------- стыки ----------
        foreach (var x in new float[] { 254, 510, 654, 850, 1428, 1676, 1826, 2052, 2268, 2520 }) g.Joint(x, Y1);
        foreach (var x in new float[] { 210, 340, 444, 590, 754, 1328, 1604, 1734, 1860, 2058, 2358 }) g.Joint(x, Y2);
        foreach (var x in new float[] { 684, 810, 980, 1308, 1432, 1604 }) g.Joint(x, Y3);   // [ИЗМЕНЕНО 20.10.2026] 1492 → 1432
        foreach (var x in new float[] { 980, 1270 }) g.Joint(x, Y5);
        foreach (var x in new float[] { 790, 1328 }) g.Joint(x, Y4);
        g.Joint(360, (Y1 + Y2) / 2);
        g.Joint(571, (Y1 + Y2) / 2);
        g.Joint(728, (Y1 + Y3) / 2);
        g.Joint(1564, (Y1 + Y2) / 2);
        g.Joint(1592, (Y1 + Y3) / 2);
        g.Joint(1747, (Y1 + Y2) / 2);
        if (Depot)
        {
            g.Joint(1784, Y3);
            g.Joint(1934, Y3);
            g.Joint(1794, (Y1 + Y3) / 2);
            g.Joint(1956, Y5);
        }

        // ---------- светофоры (над путём — нечётные ←, под путём — чётные →) ----------
        g.Sig(CH, 210, Y2, false);
        g.Sig(M2, 340, Y2, false);
        g.Sig(M6, 444, Y2, false);
        g.Sig(M4, 254, Y1, false);
        g.Sig(M8, 654, Y1, false);
        g.Sig(M10, 684, Y3, false);
        g.Sig(H1, 850, Y1, true);
        g.Sig(H2, 754, Y2, true);
        g.Sig(H4, 790, Y4, true);
        g.Sig(H3, 980, Y3, true);
        g.Sig(H5, 980, Y5, true);
        g.Sig(CH1, 1428, Y1, false);
        g.Sig(CH2, 1328, Y2, false);
        g.Sig(CH4, 1328, Y4, false);
        g.Sig(CH3, 1308, Y3, false);
        g.Sig(CH5, 1270, Y5, false);
        g.Sig(M5, 1604, Y2, true);
        g.Sig(M7, 1604, Y3, true);
        g.Sig(M1, 1826, Y1, true);
        g.Sig(M3, 1860, Y2, true);
        if (Depot) g.Sig(M9, 1934, Y3, true);
        g.Sig(H, 2052, Y1, true);
        g.Sig(post.P1, 2268, Y1, true);
        g.Sig(post.P3, 2520, Y1, true);
        g.Sig(post.P6, 2058, Y2, false);
        g.Sig(post.P4, 2358, Y2, false);

        // ---------- номера стрелок ----------
        g.SwLabel("4", SW2_4, 300, Y1 - 20);
        g.SwLabel("2", SW2_4, 400, Y2 + 20);
        g.SwLabel("6", SW6_8, 530, Y2 + 20);
        g.SwLabel("8", SW6_8, 612, Y1 - 20);
        g.SwLabel("10", SW10_12, 666, Y1 - 20);
        g.SwLabel("12", SW10_12, 780, Y3 - 20);
        g.SwLabel("14", SW14, 610, Y2 - 20);
        g.SwLabel("16", SW16, 836, Y3 + 20);
        g.SwLabel("17", SW17, 1440, Y3 + 20);
        g.SwLabel("19", SW19, 1482, Y2 - 20);
        g.SwLabel("7", SW5_7, 1520, Y2 + 20);
        g.SwLabel("5", SW5_7, 1606, Y1 - 20);
        g.SwLabel("15", SW13_15, 1538, Y3 - 20);
        g.SwLabel("13", SW13_15, 1648, Y1 + 20);
        g.SwLabel("3", SW1_3, 1690, Y1 - 20);
        g.SwLabel("1", SW1_3, 1806, Y2 + 20);
        if (Depot)
        {
            g.SwLabel("11", SW9_11, 1746, Y1 + 20);
            g.SwLabel("9", SW9_11, 1852, Y3 - 20);
            g.SwLabel("Д1", SWD1, 2046, Y3 + 20);
            g.SwLabel("Д2", SWD2, 1966, Y5 - 20);
        }

        // ---------- номера путей и надписи ----------
        g.Text("5", 1120, Y5 - 34, 16f, true, true);
        g.Text("3", 1150, Y3 - 34, 16f, true, true);
        g.Text("1", 1150, Y1 - 34, 16f, true, true);
        g.Text("2", 1050, Y2 - 34, 16f, true, true);
        g.Text("4", 1050, Y4 - 34, 16f, true, true);
        g.Text("6", 548, Y3 - 12, 14f, true, true);   // [ИЗМЕНЕНО 09.11.2026] было (590, Y3-34) — закрывала поднятая табличка ПНП «Стройкомплект»; теперь слева от упора
        g.Text("к путевому посту →", 2590, Y2 + 70, 9.5f, true, align: StringAlignment.Far);
        g.Text("← от путевого поста", 2590, Y1 - 78, 9.5f, true, align: StringAlignment.Far);
        g.Boundaries.Add(new PointF(1990, Y2 + 36));

        // подписи участков: по умолчанию под путём, для горловин — вручную, чтобы не налезали на лампы
        g.SectionLabels[id(SP5_13)] = new PointF(1560, Y1 + 16);
        g.SectionLabels[id(SP3_11)] = new PointF(1720, Y1 + 30);
        g.SectionLabels[id(SP19_7)] = new PointF(1440, Y2 + 16);   // правее ламп Ч2
        g.SectionLabels[id(SP17)] = new PointF(1390, Y3 - 14);
        g.SectionLabels[id(SP15)] = new PointF(1520, Y3 + 16);
        g.SectionLabels[id(SP1)] = new PointF(1840, Y2 + 16);     // правее номера стрелки 1
        g.SectionLabels[id(SP4)] = new PointF(430, Y1 - 14);
        g.SectionLabels[id(SP2)] = new PointF(420, Y2 + 44);      // левее подписи М6
        g.SectionLabels[id(SP6)] = new PointF(485, Y2 - 14);
        g.SectionLabels[id(SP8)] = new PointF(560, Y1 - 14);
        g.SectionLabels[id(SP10)] = new PointF(760, Y1 + 16);
        g.SectionLabels[id(SP12)] = new PointF(740, Y3 - 14);
        g.SectionLabels[id(SP14)] = new PointF(690, Y2 + 16);
        g.SectionLabels[id(SP16)] = new PointF(900, Y3 + 16);
        g.SectionLabels[id(CH_M2)] = new PointF(275, Y2 - 14);
        g.AutoSectionLabels();
    }
}
