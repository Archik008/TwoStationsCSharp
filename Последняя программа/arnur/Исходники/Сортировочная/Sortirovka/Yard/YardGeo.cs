namespace StationApp.Yard;

// =====================================================================
//  [ДОБАВЛЕНО 24.11.2026] ГЕОМЕТРИЯ ГОРКИ — ДАННЫМИ, А НЕ КОНСТАНТАМИ
//  Раньше горка «Сортировочной» была зашита в Lay константами (8 путей, стрелки 1–7, 1ТП и 2ТП-А/Б).
//  Теперь всё, что модель (YardSim) и окно (YardView) знают о горке, лежит здесь:
//    BuiltinGeo  — учебная станция «Сортировочной» (как было, цифры те же);
//    горка из файла .station.json редактора станций — строит «Станция по схеме» (Scheme\YardFromJson.cs).
//  Lay (YardLayout.cs) — прежние имена для остального кода, берёт всё из Lay.Cur.
//
//  s — метры вдоль пути от вершины горки (горба): до горба s < 0 (путь надвига, парк приёма), после — спуск,
//  стрелочная зона, сортировочные пути. Точки схемы — условные (окно масштабирует), вдоль каждого пути
//  задана ломаная (s → точка): к сортировочному пути k — Tree[k], от пути k парка приёма — Push[k].
// =====================================================================

/// <summary>Рельсовая цепь в описании горки (S0…S1 для путей Lo…Hi); стрелка в ней — SwNo &gt; 0.</summary>
public sealed class EdgeDef
{
    public string Id = "";
    public double S0, S1;
    public int Lo, Hi;
    public int SwNo;
    public double SwS;
    public int PlusLo, PlusHi, MinusLo, MinusHi;
}

/// <summary>Тормозная позиция в описании горки.</summary>
public sealed class RetDef
{
    public string Name = "";
    public double S0, S1;
    public int Lo, Hi;
}

public class YardGeo
{
    public bool Builtin;
    /// <summary>Название станции (заголовок окна); пусто — «Сортировочная».</summary>
    public string Title = "";

    // ---- главные отметки, м ----
    public double STrack = 186, SEnd = 500, SArrFront = -150, SArrTail = -450, SPushTrack = -100;
    /// <summary>Горочный светофор Г; дальше LocoLimit горочный локомотив не заходит; SUncouple — место расцепки (для вида сбоку).</summary>
    public double SSignal = -60, LocoLimit = -3, SUncouple = -35;
    public int Tracks = 8, ArrTracks = 4, DepTracks = 4;
    public double TrackLength => SEnd - STrack;

    // ---- профиль: уклон действует до S1 (по порядку); дальше последнего — LastGrade ----
    public readonly List<(double S1, double Grade)> Grades = new();
    public double LastGrade = -2.5;
    public readonly List<(double S0, double S1)> SwitchZones = new();

    // ---- рельсовые цепи, стрелки, замедлители ----
    public readonly List<EdgeDef> EdgeDefs = new();
    public readonly List<RetDef> RetDefs = new();

    // ---- назначения путей ----
    public string[] TrackNames = { "" };
    public int[] Norms = { 0 };

    public virtual int Norm(int k) => k >= 1 && k < Norms.Length ? Norms[k] : YardSim.NormMain;
    /// <summary>На какой путь идёт отцеп в новом поезде (по листку).</summary>
    public virtual int RandomDest(Random rnd) => rnd.Next(1, Tracks + 1);
    public virtual WagonKind KindFor(int dest, Random rnd) => Rolling.Normal[rnd.Next(Rolling.Normal.Length)];
    /// <summary>Путь для вагона, запрещённого к роспуску (служебный — на «сборный», если есть).</summary>
    public virtual int ForbiddenDest(WagonKind k, Random rnd) => rnd.Next(1, Tracks + 1);
    public virtual double LoadedShare(int dest) => 0.62;

    // =================================================================
    //  ПРОФИЛЬ
    // =================================================================
    public double Grade(double s)
    {
        foreach (var (s1, g) in Grades) if (s < s1) return g;
        return LastGrade;
    }

    double[] elev;
    double elevFrom;
    const double ElevStep = 1;

    /// <summary>Профиль и все отметки заданы — посчитать высоты (вызывает тот, кто построил горку).</summary>
    public void Finish()
    {
        elevFrom = Math.Floor(Math.Min(-1200, SArrTail - 200));
        int n = (int)((SEnd + 50 - elevFrom) / ElevStep) + 1;
        var z = new double[n];
        int i0 = (int)(-elevFrom / ElevStep);
        for (int i = i0 + 1; i < n; i++) z[i] = z[i - 1] - Grade(elevFrom + (i - 1) * ElevStep) * ElevStep / 1000;
        for (int i = i0 - 1; i >= 0; i--) z[i] = z[i + 1] + Grade(elevFrom + i * ElevStep) * ElevStep / 1000;
        elev = z;
        elevView = BuildView();
    }

    /// <summary>Высота пути над горбом (м, ниже — минус).</summary>
    public double Elev(double s)
    {
        double f = (s - elevFrom) / ElevStep;
        int i = Math.Clamp((int)f, 0, elev.Length - 2);
        double t = Math.Clamp(f - i, 0, 1);
        return elev[i] + (elev[i + 1] - elev[i]) * t;
    }

    public bool InSwitchZone(double s)
    {
        foreach (var (a, b) in SwitchZones) if (s >= a && s < b) return true;
        return false;
    }

    // =================================================================
    //  РЕЛЬСОВЫЕ ЦЕПИ, СТРЕЛКИ, ЗАМЕДЛИТЕЛИ — новый набор на каждую смену (у стрелок и замедлителей есть состояние)
    // =================================================================
    public List<Edge> MakeEdges(out Switch[] sw, out Retarder[] rt)
    {
        var e = new List<Edge>();
        int n = EdgeDefs.Count == 0 ? 0 : EdgeDefs.Max(d => d.SwNo);
        sw = new Switch[n + 1];
        foreach (var d in EdgeDefs)
        {
            var x = new Edge { Id = d.Id, S0 = d.S0, S1 = d.S1, Lo = d.Lo, Hi = d.Hi };
            e.Add(x);
            if (d.SwNo <= 0) continue;
            var s = new Switch
            {
                No = d.SwNo, S = d.SwS, Lo = d.Lo, Hi = d.Hi, Section = x,
                PlusLo = d.PlusLo, PlusHi = d.PlusHi, MinusLo = d.MinusLo, MinusHi = d.MinusHi, Mid = d.PlusHi,
            };
            x.Sw = s;
            sw[d.SwNo] = s;
        }
        rt = RetDefs.Select(r => new Retarder { Name = r.Name, S0 = r.S0, S1 = r.S1, Lo = r.Lo, Hi = r.Hi }).ToArray();
        return e;
    }

    // =================================================================
    //  ЭКРАН: схема в условных точках W × Hv (окно масштабирует)
    // =================================================================
    public float W = 1600, H = 630;
    /// <summary>Высота схемы над полосой «вид сбоку».</summary>
    public float SchemeH = 448;
    public RectangleF Profile = new(10, 448, 1580, 176);
    public double ProfileFrom = -150, ProfileTo = 260;
    public float Hv = 630;

    /// <summary>Высота схемы в условных точках: окно выше — полоса «вид сбоку» растёт (схема не мельчит).</summary>
    public void Fit(float aspect)
    {
        Hv = Math.Clamp(W * aspect, H, SchemeH + 6 + 250);
        Profile = new RectangleF(10, SchemeH, W - 20, Hv - SchemeH - 6);
    }

    /// <summary>Ломаные с отметками: к сортировочному пути k (s ≥ 0) и от пути a парка приёма до горба (s ≤ 0).</summary>
    public (double s, PointF p)[][] Tree = { null }, Push = { null };

    static PointF Interp((double s, PointF p)[] pts, double s)
    {
        if (s <= pts[0].s) return pts[0].p;
        for (int i = 1; i < pts.Length; i++)
            if (s <= pts[i].s)
            {
                double ds = pts[i].s - pts[i - 1].s;
                float t = ds <= 0 ? 1 : (float)((s - pts[i - 1].s) / ds);
                return new(pts[i - 1].p.X + (pts[i].p.X - pts[i - 1].p.X) * t, pts[i - 1].p.Y + (pts[i].p.Y - pts[i - 1].p.Y) * t);
            }
        return pts[^1].p;
    }

    (double s, PointF p)[] TreeOf(int k) => Tree[Math.Clamp(k, 1, Tracks)];
    (double s, PointF p)[] PushOf(int a) => Push[Math.Clamp(a, 1, ArrTracks)];

    /// <summary>Точка схемы: s — отметка, track — путь сортировочного парка (для s ≥ 0), arr — путь парка приёма (s &lt; 0).</summary>
    public PointF At(double s, int track, int arr) => s >= 0 ? Interp(TreeOf(track), s) : Interp(PushOf(arr), s);

    /// <summary>Ломаная участка s0…s1 (для рисования рельсовой цепи).</summary>
    public PointF[] Line(double s0, double s1, int track, int arr = 2)
    {
        var src = s0 >= 0 ? TreeOf(track) : PushOf(arr);
        var list = new List<PointF> { At(s0, track, arr) };
        foreach (var (s, p) in src) if (s > s0 && s < s1) list.Add(p);
        list.Add(At(s1, track, arr));
        return list.ToArray();
    }

    /// <summary>Ломаная по рельсам от s0 до s1 (через горб — путь парка приёма arr и сортировочный путь track).</summary>
    public PointF[] Path(double s0, double s1, int track, int arr)
    {
        var list = new List<PointF> { At(s0, track, arr) };
        foreach (var (s, _) in PushOf(arr)) if (s > s0 && s < s1 && s < 0) list.Add(At(s, track, arr));
        foreach (var (s, _) in TreeOf(track)) if (s > s0 && s < s1 && s >= 0) list.Add(At(s, track, arr));
        list.Add(At(s1, track, arr));
        return list.ToArray();
    }

    public PointF SwitchPoint(Switch sw) => At(sw.S, sw.Lo, 1);
    public PointF SignalPoint => At(SSignal, 1, 1);

    // ---- где писать подписи путей (и где их ловит мышь) ----
    public virtual float TrackY(int k) => At(SEnd, k, 1).Y;
    public virtual float ArrY(int a) => At(SArrTail, 1, a).Y;
    public virtual float DepY(int d) => DepLine(d)[0].Y;

    // ---- парк отправления, депо, стоянка горочного локомотива, обходной путь — рисуются, движение по ним «сценой» ----
    public PointF DepotGate = new(470, 362), HumpLocoPark = new(650, 236);
    /// <summary>Обходной путь (ниже парка приёма): по нему локомотивы идут в депо и в хвост составов.</summary>
    public float BypassY = 296;
    /// <summary>Парк отправления: путь d — от X0 до X1 на высоте DepYs[d].</summary>
    public float DepX0 = 1430, DepX1 = 1588;
    public float[] DepYs = { 0 };
    public PointF[] DepLine(int d) => new PointF[] { new(DepX0, DepYs[Math.Clamp(d, 1, DepTracks)]), new(DepX1, DepYs[Math.Clamp(d, 1, DepTracks)]) };

    /// <summary>Метров на точку схемы в парке отправления и на «сценах».</summary>
    public float SceneScale = 0.9f;
    /// <summary>Метров на точку схемы в парке приёма (для «сцены» прибытия — чтобы поезд не менял длину, встав на путь).</summary>
    public float ArrScale = (float)(370 / 300.0);

    PointF ArrTail(int a) => At(SArrTail, 1, a);
    PointF ArrFront(int a) => At(SArrFront, 1, a);
    PointF TrackEnd(int k) => At(SEnd, k, 1);

    public virtual PointF[] ArrivalPath(int a)
    {
        var t = ArrTail(a);
        return new PointF[] { new(-40, t.Y), new(t.X - 30, t.Y), t, ArrFront(a) };
    }
    public virtual PointF[] LocoToDepot(int a)
    {
        var f = ArrFront(a);
        return new PointF[] { f, new(f.X + 20, f.Y), new(f.X + 40, BypassY), new(DepotGate.X + 20, BypassY), DepotGate };
    }
    public virtual PointF[] HumpLocoToTail(int a, float tailX)
    {
        var t = ArrTail(a);
        return new PointF[] { HumpLocoPark, new(HumpLocoPark.X - 20, BypassY), new(t.X - 30, BypassY), new(t.X - 14, t.Y), new(t.X, t.Y), new(tailX, t.Y) };
    }
    public virtual PointF[] HumpLocoHome(PointF from) => new PointF[] { from, new(from.X - 30, from.Y), new(HumpLocoPark.X + 20, BypassY), HumpLocoPark };
    public virtual PointF[] ToDeparture(int k, int d)
    {
        var a = At(STrack, k, 1);
        var e = TrackEnd(k);
        var dl = DepLine(d);
        return new PointF[] { a, e, new(e.X + 12, e.Y), dl[0], dl[1] };
    }
    public virtual PointF[] DepotToDeparture(int d)
    {
        var dl = DepLine(d);
        float bottom = SchemeH - 40;
        return new PointF[] { DepotGate, new(DepotGate.X + 20, bottom), new(W - 4, bottom), new(W - 4, dl[1].Y + 12), dl[1] };
    }
    public virtual PointF[] Departure(int d)
    {
        var dl = DepLine(d);
        return new PointF[] { dl[0], dl[1], new(W + 40, dl[1].Y) };
    }

    // =================================================================
    //  ВИД СБОКУ
    // =================================================================
    public float ProfX(double s) => Profile.Left + 10 + (float)((s - ProfileFrom) / (ProfileTo - ProfileFrom)) * (Profile.Width - 20);
    public float ProfPxPerM => (Profile.Width - 20) / (float)(ProfileTo - ProfileFrom);
    double ZMin => ElevView(ProfileTo);
    double ZMax => ElevView(0);
    float ProfVScale => Math.Min(Profile.Height - 70, 150);
    /// <summary>Высоты на виде сбоку — с преувеличением; переломы профиля сглажены вертикальными кривыми.</summary>
    public float ProfY(double s) => Profile.Bottom - 22 - (float)((ElevView(s) - ZMin) / (ZMax - ZMin)) * ProfVScale;

    const double Smooth = 12;
    double viewFrom;
    double[] elevView;
    double[] BuildView()
    {
        viewFrom = ProfileFrom - 60;
        int n = (int)(ProfileTo + 60 - viewFrom) + 1;
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = viewFrom + i, sum = 0; int m = 0;
            for (double d = -Smooth; d <= Smooth; d += 1) { sum += Elev(s + d); m++; }
            z[i] = sum / m;
        }
        return z;
    }
    double ElevView(double s)
    {
        double f = s - viewFrom;
        int i = Math.Clamp((int)f, 0, elevView.Length - 2);
        double t = Math.Clamp(f - i, 0, 1);
        return elevView[i] + (elevView[i + 1] - elevView[i]) * t;
    }

    /// <summary>Отметки на виде сбоку (кроме светофора Г, горба, расцепки и замедлителей — их рисует окно само).</summary>
    public readonly List<(double S, string Text, float Dy)> ProfileMarks = new();

    // =================================================================
    //  СХЕМА ИЗ ФАЙЛА: что рисовать вокруг горки (у встроенной — своё рисование, эти списки пустые)
    // =================================================================
    /// <summary>Пути, которые только рисуются (вход в парк приёма, выходы из парка, обходной…).</summary>
    public readonly List<PointF[]> Decor = new();
    /// <summary>Тело пути a парка приёма (красное, когда на нём состав) и его горловина до пути надвига (белая при надвиге).</summary>
    public PointF[][] ArrBody = { null }, ArrLead = { null };
    /// <summary>Путь надвига — от горловины парка приёма до горба.</summary>
    public PointF[] PushTrack = Array.Empty<PointF>();
    /// <summary>Подписи парков и прочие надписи схемы.</summary>
    public readonly List<(string Text, PointF Pos, bool Big)> Labels = new();
    /// <summary>Где писать номер и состояние: сортировочного пути k, пути a приёма, пути d отправления (левый край подписи).</summary>
    public PointF[] TrackLabel = { default }, ArrLabel = { default }, DepLabel = { default };
    /// <summary>Маневровые светофоры М1… на выходе из парка приёма.</summary>
    public PointF[] ShuntSig = { default };
    /// <summary>Повторитель горочного светофора над парком приёма (null — нет).</summary>
    public PointF? Repeater;
    /// <summary>Прямоугольник депо.</summary>
    public RectangleF Depot = new(290, 344, 180, 40);
}
