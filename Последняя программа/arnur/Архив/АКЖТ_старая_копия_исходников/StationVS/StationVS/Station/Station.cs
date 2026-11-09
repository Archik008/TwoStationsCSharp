
namespace StationApp;

// =====================================================================
//  МОДЕЛЬ СТАНЦИИ
//  ВАЖНО: буквы Н, Ч, М в названиях светофоров — КИРИЛЛИЧЕСКИЕ.
// =====================================================================

class Signal
{
    public string Name = "";
    public PointF Lamp;      // центр первой лампы
    public PointF Label;     // центр подписи
    public bool Shunting;    // маневровый (одна лампа)
    public bool Open;
}

class Switch
{
    public int Num;
    public PointF Label;
    public bool Plus = true;
    public bool Locked;      // заперта маршрутом
}

class Route
{
    public string Name = "";
    public bool Shunting;
    public string From = "";                                   // начальный светофор
    public string[] Segs = Array.Empty<string>();              // участки пути
    public (int num, bool plus)[] Sw = Array.Empty<(int, bool)>(); // нужные положения стрелок
}

class Station
{
    public const float W = 1640, H = 640;   // размер схемы в "проектных" координатах

    public Dictionary<string, PointF[]> Segs = new();   // участки, по которым строятся маршруты
    public List<PointF[]> Decor = new();                // просто линии пути (без маршрутов)
    public List<Signal> Signals = new();
    public Dictionary<int, Switch> Switches = new();
    public List<Route> Routes = new();
    public List<Route> Active = new();
    public HashSet<string> Occupied = new();
    public bool TestMode;

    public event Action Changed;
    public event Action<string> Log;
    public event Action<int, bool> SwitchMoved;   // номер стрелки, плюс?
    public event Action<int, bool> SignalChanged; // индекс светофора, открыт?

    public Station()
    {
        // ---------- Участки пути (id, x1,y1, x2,y2, ...) ----------
        Seg("1a", 485, 367, 1010, 367);
        Seg("1b", 1015, 367, 1290, 367);
        Seg("2a", 430, 477, 822, 477);
        Seg("2b", 830, 477, 1160, 477);
        Seg("2c", 1172, 477, 1270, 477);
        Seg("3a", 436, 256, 822, 256);
        Seg("3b1", 830, 256, 1065, 256);
        Seg("3b2", 1065, 256, 1270, 256);
        Seg("3c", 1278, 256, 1478, 256);
        Seg("4a", 468, 587, 830, 587);
        Seg("4b", 835, 587, 965, 587);
        Seg("5a", 560, 146, 830, 146);
        Seg("5b", 835, 146, 900, 146);
        Seg("d5", 900, 146, 1065, 256);
        Seg("d5L", 445, 256, 515, 146, 560, 146);
        Seg("dL3", 340, 367, 418, 256);
        Seg("dM", 225, 477, 305, 367);
        Seg("dB", 335, 477, 415, 587, 468, 587);
        Seg("dBR", 965, 587, 1150, 477, 1160, 477);
        Seg("d15", 1200, 256, 1310, 367);
        Seg("d7", 1200, 477, 1265, 367);
        Seg("d3", 1360, 367, 1470, 477);
        Seg("d11", 1430, 367, 1465, 256);
        Seg("m10", 312, 256, 436, 256);
        Seg("m8", 330, 367, 485, 367);
        Seg("m6x", 165, 477, 225, 477);
        Seg("m6y", 225, 477, 305, 477);
        Seg("m6z1", 312, 477, 335, 477);
        Seg("m6z2", 335, 477, 430, 477);

        // ---------- Декоративные участки ----------
        Dec(229, 256, 305, 256);
        Dec(186, 367, 320, 367);
        Dec(100, 477, 158, 477);
        Dec(1296, 367, 1530, 367);
        Dec(1278, 477, 1410, 477);
        Dec(1416, 477, 1600, 477);

        // ---------- Светофоры (имя, лампа x,y, подпись x,y, маневровый?) ----------
        Sig("Н5", 520, 132, 561, 122, false);
        Sig("Ч5", 856, 160, 836, 122, false);
        Sig("Н3", 483, 243, 524, 233, false);
        Sig("Ч3", 856, 270, 836, 233, false);
        Sig("М10", 332, 270, 313, 233, true);
        Sig("М7", 1259, 243, 1280, 233, true);
        Sig("Н1", 447, 353, 487, 343, false);
        Sig("Ч1", 1040, 381, 1020, 343, false);
        Sig("М8", 340, 381, 321, 343, true);
        Sig("Н2", 392, 464, 432, 453, false);
        Sig("Ч2", 856, 491, 836, 453, false);
        Sig("М6", 185, 491, 164, 453, true);
        Sig("М5", 1258, 463, 1280, 453, true);
        Sig("Н4", 429, 574, 468, 564, false);
        Sig("Ч4", 856, 601, 836, 564, false);

        // ---------- Стрелки (номер, положение подписи) ----------
        Sw(1, 1452, 492);
        Sw(2, 352, 497);
        Sw(3, 1352, 352);
        Sw(4, 245, 497);
        Sw(5, 1262, 352);
        Sw(6, 285, 388);
        Sw(7, 1187, 495);
        Sw(8, 362, 350);
        Sw(9, 1452, 243);
        Sw(10, 432, 275);
        Sw(11, 1412, 348);
        Sw(12, 465, 272);
        Sw(13, 1310, 345);
        Sw(15, 1185, 243);
        Sw(17, 1090, 272);
        Sw(19, 1148, 458);

        // ---------- Маршруты ----------
        // R(имя, маневровый?, светофор, "участки через запятую", (стрелка, плюс?)...)

        // Поездные
        R("Н1_до_Ч1", false, "Н1", "1a");
        R("Н2_до_Ч2", false, "Н2", "2a");
        R("Н3_до_Ч3", false, "Н3", "3a");
        R("Н4_до_Ч4", false, "Н4", "dB,4a", (2, false));
        R("Н5_до_Ч5", false, "Н5", "d5L,5a", (12, false));
        R("Н1_до_М8", false, "Н1", "m8", (8, true));
        R("Н2_до_М6", false, "Н2", "m6z2,m6z1,m6y,m6x", (2, true), (4, true));
        R("Н4_до_М6", false, "Н4", "dB,m6z1,m6y,m6x", (2, false), (4, true));
        R("Н3_до_М10", false, "Н3", "m10", (10, true));
        R("Ч1_до_Сер_1_Путь", false, "Ч1", "1b", (5, true));
        R("Ч3_до_М7", false, "Ч3", "3b1,3b2", (17, true), (15, true));
        R("Ч3_до_М9", false, "Ч3", "3b1,3b2,3c", (17, true), (15, true), (9, true));
        R("Ч5_до_М7", false, "Ч5", "5b,d5,3b2", (17, false), (15, true));
        R("Ч2_до_М5", false, "Ч2", "2b,2c", (19, true), (7, true));
        R("Ч4_до_М5", false, "Ч4", "4b,dBR,2c", (19, false), (7, true));

        // Маневровые
        R("М6_до_Н2", true, "М6", "m6x,m6y,m6z1,m6z2", (4, true), (2, true));
        R("М6_до_Н4", true, "М6", "m6x,m6y,m6z1,dB", (4, true), (2, false));
        R("М6_до_М8", true, "М6", "m6x,dM", (4, false), (6, false));
        R("М8_до_М6", true, "М8", "dM,m6x", (6, false), (4, false));
        R("М8_до_Н1", true, "М8", "m8", (8, true));
        R("М8_до_М10", true, "М8", "dL3,m10", (8, false), (10, false));
        R("М10_до_Н3", true, "М10", "m10", (10, true));
        R("М10_до_М8", true, "М10", "m10,dL3", (10, false), (8, false));
        R("М10_до_Н5", true, "М10", "m10,d5L", (10, true), (12, false));
        R("М7_до_Ч3", true, "М7", "3b2,3b1", (17, true), (15, true));
        R("М7_до_Ч5", true, "М7", "3b2,d5,5b", (17, false), (15, true));
        R("М5_до_Ч2", true, "М5", "2c,2b", (7, true), (19, true));
        R("М5_до_Ч4", true, "М5", "2c,dBR,4b", (7, true), (19, false));
        R("М5_до_Ч1", true, "М5", "2c,d7", (7, false), (5, false));
    }

    // ----- помощники для описания станции -----
    void Seg(string id, params float[] xy) => Segs[id] = ToPts(xy);
    void Dec(params float[] xy) => Decor.Add(ToPts(xy));
    static PointF[] ToPts(float[] xy)
    {
        var pts = new PointF[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
        return pts;
    }
    void Sig(string name, float lx, float ly, float tx, float ty, bool shunt) =>
        Signals.Add(new Signal { Name = name, Lamp = new PointF(lx, ly), Label = new PointF(tx, ty), Shunting = shunt });
    void Sw(int num, float x, float y) =>
        Switches[num] = new Switch { Num = num, Label = new PointF(x, y) };
    void R(string name, bool shunt, string from, string segs, params (int, bool)[] sw) =>
        Routes.Add(new Route { Name = name, Shunting = shunt, From = from, Segs = segs.Split(','), Sw = sw });

    // ----- логика -----
    public void NotifyChanged() => Changed?.Invoke();
    void Say(string msg) => Log?.Invoke(msg);

    void SetSignal(string name, bool open)
    {
        int idx = Signals.FindIndex(s => s.Name == name);
        if (idx < 0 || Signals[idx].Open == open) return;
        Signals[idx].Open = open;
        SignalChanged?.Invoke(idx, open);
    }

    void MoveSwitch(int n, bool plus)
    {
        var sw = Switches[n];
        if (sw.Plus == plus) return;
        sw.Plus = plus;
        SwitchMoved?.Invoke(n, plus);
    }

    void RecalcLocks()
    {
        foreach (var sw in Switches.Values) sw.Locked = false;
        foreach (var a in Active)
            foreach (var (n, _) in a.Sw) Switches[n].Locked = true;
    }

    public bool TrySetRoute(Route r)
    {
        if (Active.Contains(r)) { Say($"Маршрут {r.Name} уже установлен"); return false; }

        foreach (var s in r.Segs)
            if (Occupied.Contains(s)) { Say($"ОТКАЗ {r.Name}: участок {s} занят"); return false; }

        foreach (var a in Active)
        {
            if (a.Segs.Intersect(r.Segs).Any()) { Say($"ОТКАЗ {r.Name}: конфликт с {a.Name}"); return false; }
            if (a.From == r.From) { Say($"ОТКАЗ {r.Name}: светофор {r.From} уже занят маршрутом {a.Name}"); return false; }
        }

        foreach (var (n, plus) in r.Sw)
            if (Switches[n].Locked && Switches[n].Plus != plus)
            { Say($"ОТКАЗ {r.Name}: стрелка {n} заперта другим маршрутом"); return false; }

        foreach (var (n, plus) in r.Sw) MoveSwitch(n, plus);
        Active.Add(r);
        RecalcLocks();
        SetSignal(r.From, true);
        Say($"Установлен маршрут {r.Name}");
        Changed?.Invoke();
        return true;
    }

    public void Cancel(Route r, bool silent = false)
    {
        if (!Active.Remove(r)) return;
        SetSignal(r.From, false);
        RecalcLocks();
        if (!silent) { Say($"Отменён маршрут {r.Name}"); Changed?.Invoke(); }
    }

    public void CancelLast()
    {
        if (Active.Count == 0) { Say("Нет установленных маршрутов"); return; }
        Cancel(Active[^1]);
    }

    public void ClearAll()
    {
        foreach (var r in Active.ToList()) Cancel(r, true);
        Occupied.Clear();
        Say("Всё убрано");
        Changed?.Invoke();
    }

    public void ToggleSwitch(int n)
    {
        var sw = Switches[n];
        if (sw.Locked) { Say($"Стрелка {n} заперта маршрутом"); return; }
        MoveSwitch(n, !sw.Plus);
        Say($"Стрелка {n} → {(sw.Plus ? "плюс" : "минус")}");
        Changed?.Invoke();
    }

    public void ToggleOccupied(string seg)
    {
        if (!Occupied.Remove(seg))
        {
            Occupied.Add(seg);
            Say($"Участок {seg} занят");
            foreach (var r in Active.Where(a => a.Segs.Contains(seg)))
            {
                SetSignal(r.From, false);
                Say($"Светофор {r.From} закрыт (занят участок {seg})");
            }
        }
        else Say($"Участок {seg} свободен");
        Changed?.Invoke();
    }

    public string HitSeg(PointF p)
    {
        string best = null;
        float bestD = 9f;
        foreach (var kv in Segs)
        {
            var pts = kv.Value;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float d = DistToSeg(p, pts[i], pts[i + 1]);
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
        }
        return best;
    }

    static float DistToSeg(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        float t = len2 == 0 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Clamp(t, 0f, 1f);
        float cx = a.X + t * dx, cy = a.Y + t * dy;
        return MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }
}
