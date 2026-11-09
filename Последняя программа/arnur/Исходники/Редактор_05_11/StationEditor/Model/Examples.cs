namespace StationEditor.Model;

// =====================================================================
//  ПРИМЕРЫ СХЕМ. Алгабас — по Algabas/AlgabasLayout.cs (пути 1–4, вытяжка, тупик М6Т,
//  однопутный перегон к путевому посту), собран из кубиков.
// =====================================================================
public static class Examples
{
    public static readonly (string Id, string Name, Func<Station> Build)[] All =
    {
        ("algabas", "Станция Алгабас", Algabas),
        ("razezd", "Разъезд (2 пути)", Razezd),
        ("novyi", "Разъезд с переездом (новые элементы)", Novyi),
    };

    // варианты кубиков (см. Core.TypeList)
    const int H = 0, UP = 1, DOWN = 2;                                   // straight: ─  ╱  ╲
    const int W_NE = 0, W_SE = 1, E_NW = 2, E_SW = 3;                    // curve
    const int SW_UP = 0, SW_DOWN = 1, SW_UP_L = 2, SW_DOWN_L = 3;        // switch: остряк слева / справа, отклонение вверх / вниз

    sealed class B
    {
        public readonly Station St = new();
        public B(string name) { St.Name = name; }

        public B Put(int x, int y, string t, int v, string sec, string name = null, string group = null)
        {
            St.Tiles[Core.Key(x, y)] = new Tile { T = t, V = v, Sec = sec, Name = name, Group = group };
            return this;
        }
        public B Run(int x1, int x2, int y, string sec)
        {
            for (int x = x1; x <= x2; x++) Put(x, y, "straight", H, sec);
            return this;
        }
        public B Sig(int x, int y, string dir, string kind, string name)
        {
            var t = St.Tiles[Core.Key(x, y)];
            (t.Sigs ??= new List<Signal>()).Add(new Signal { Dir = dir, Kind = kind, Name = name, Id = Core.NextSignalId(St) });
            return this;
        }
        public B Text(int x, int y, string text, double size = 14)
        {
            St.Texts[Core.Key(x, y)] = new TextItem { Text = text, Size = size };
            return this;
        }
        public B Type(string sec, string type)
        {
            if (!St.Sections.TryGetValue(sec, out var s)) St.Sections[sec] = s = new SectionInfo();
            s.Type = type;
            return this;
        }
        public B Obj(int x, int y, string type, string name, int w, int h)
        {
            St.Objects[Core.Key(x, y)] = new StationObject { Type = type, Name = name, W = w, H = h };
            return this;
        }
    }

    /// <summary>[ДОБАВЛЕНО 05.11.2026] Разъезд со всеми новыми элементами: предупредительный ПЧ перед входным,
    /// повторительный ПЧ1 перед выходным (имя — «П» + имя основного, как ПЧМ2А в уроке caredenis.ru les03), переезд с заградительными З1 и З2, вокзал, часовая башня, амперметр.</summary>
    public static Station Novyi()
    {
        var b = new B("Разъезд Новый");
        b.Put(0, 6, "exit", 0, "НП").Run(1, 8, 6, "НП").Sig(2, 6, "R", "warning", "ПЧ").Sig(8, 6, "R", "entry", "Ч");
        b.Put(9, 6, "switch", SW_UP, "1СП", name: "1");
        b.Run(10, 21, 6, "IП").Sig(10, 6, "L", "exit", "Н1").Sig(15, 6, "R", "repeater", "ПЧ1").Sig(21, 6, "R", "exit", "Ч1");
        b.Put(10, 5, "straight", UP, "1СП").Put(11, 4, "curve", E_SW, "1СП");
        b.Run(12, 19, 4, "3П").Sig(12, 4, "L", "exit", "Н3").Sig(19, 4, "R", "exit", "Ч3");
        b.Put(20, 4, "curve", W_SE, "2СП").Put(21, 5, "straight", DOWN, "2СП");
        b.Put(22, 6, "switch", SW_UP_L, "2СП", name: "2");
        b.Run(23, 31, 6, "ЧП").Sig(23, 6, "L", "entry", "Н");
        b.Put(27, 6, "crossing", 0, "ЧП");
        b.Sig(25, 6, "R", "barrier", "З2").Sig(29, 6, "L", "barrier", "З1");
        b.Put(32, 6, "exit", 1, "ЧП");
        b.Type("НП", "line").Type("ЧП", "line");
        b.Obj(12, 8, "building", "Разъезд Новый", 7, 2).Obj(21, 8, "clock", "", 2, 4).Obj(2, 9, "ammeter", "Ток перевода стрелок", 3, 2);
        b.Text(0, 8, "← от станции А", 13).Text(26, 8, "к станции Б →", 13).Text(12, 1, "Разъезд Новый", 20).Text(26, 4, "переезд", 12);
        return b.St;
    }

    public static Station Algabas()
    {
        var b = new B("Станция Алгабас");

        // ---- перегон к путевому посту (путь 1, ряд 8) ----
        b.Put(0, 8, "exit", 0, "НАУ").Run(1, 5, 8, "НАУ").Run(6, 10, 8, "Б12");
        b.Sig(1, 8, "L", "other", "НА").Sig(5, 8, "R", "block", "2").Sig(6, 8, "L", "block", "1").Sig(10, 8, "R", "entry", "Ч");

        // ---- горловина: Ч-М2, стрелки 2 и 4 ----
        b.Run(11, 12, 8, "Ч-М2").Sig(12, 8, "R", "shunt", "М2");
        b.Put(13, 8, "switch", SW_DOWN, "2-4СП", name: "2");
        b.Run(14, 14, 8, "2-4СП");
        b.Put(15, 8, "switch", SW_UP, "2-4СП", name: "4", group: "4/6");

        // ---- путь 1, стрелка 1, вытяжка ----
        b.Run(16, 27, 8, "1П").Sig(16, 8, "L", "exit", "Н1").Sig(27, 8, "R", "shunt", "М8");
        b.Put(28, 8, "switch", SW_DOWN_L, "1СП", name: "1").Run(29, 29, 8, "1СП");
        b.Run(30, 32, 8, "ВТ").Sig(30, 8, "L", "shunt", "М1").Put(33, 8, "buffer", 0, "ВТ");

        // ---- путь 3 (ряд 11) ----
        b.Put(14, 9, "straight", DOWN, "2-4СП").Put(15, 10, "straight", DOWN, "2-4СП").Put(16, 11, "curve", E_NW, "2-4СП");
        b.Run(17, 24, 11, "3П").Sig(17, 11, "L", "exit", "Н3").Sig(24, 11, "R", "shunt", "М10");
        b.Put(25, 11, "curve", W_NE, "1СП").Put(26, 10, "straight", UP, "1СП").Put(27, 9, "straight", UP, "1СП");

        // ---- съезд 4/6, тупик М6Т, путь 2 (ряд 5) ----
        b.Put(16, 7, "straight", UP, "2-4СП").Put(17, 6, "straight", UP, "6СП");
        b.Put(18, 5, "switch", SW_DOWN_L, "6СП", name: "6", group: "4/6");
        b.Put(13, 5, "buffer", 1, "М6Т").Run(14, 17, 5, "М6Т").Sig(17, 5, "R", "shunt", "М6");
        b.Put(19, 5, "switch", SW_UP, "8СП", name: "8").Run(20, 20, 5, "8СП");
        b.Run(21, 31, 5, "2П").Sig(21, 5, "L", "exit", "Н2").Put(32, 5, "buffer", 0, "2П");

        // ---- путь 4 (ряд 2) ----
        b.Put(20, 4, "straight", UP, "8СП").Put(21, 3, "straight", UP, "8СП").Put(22, 2, "curve", E_SW, "8СП");
        b.Run(23, 31, 2, "4П").Sig(23, 2, "L", "exit", "Н4").Put(32, 2, "buffer", 0, "4П");

        // ---- типы участков и надписи ----
        b.Type("НАУ", "line").Type("Б12", "line").Type("Ч-М2", "plain").Type("ВТ", "dead").Type("М6Т", "dead");
        b.Text(22, 7, "1", 18).Text(26, 4, "2", 18).Text(21, 10, "3", 18).Text(26, 1, "4", 18);
        b.Text(0, 10, "← к путевому посту", 13).Text(1, 6, "Однопутный перегон", 13).Text(13, 0, "Станция Алгабас", 20);
        return b.St;
    }

    public static Station Razezd()
    {
        var b = new B("Разъезд");
        b.Put(0, 6, "exit", 0, "НП").Run(1, 4, 6, "НП").Sig(4, 6, "R", "entry", "Ч");
        b.Put(5, 6, "switch", SW_UP, "1СП", name: "1");
        b.Run(6, 17, 6, "IП").Sig(6, 6, "L", "exit", "Н1").Sig(17, 6, "R", "exit", "Ч1");
        b.Put(6, 5, "straight", UP, "1СП").Put(7, 4, "curve", E_SW, "1СП");
        b.Run(8, 15, 4, "3П").Sig(8, 4, "L", "exit", "Н3").Sig(15, 4, "R", "exit", "Ч3");
        b.Put(16, 4, "curve", W_SE, "2СП").Put(17, 5, "straight", DOWN, "2СП");
        b.Put(18, 6, "switch", SW_UP_L, "2СП", name: "2");
        b.Run(19, 22, 6, "ЧП").Sig(19, 6, "L", "entry", "Н").Put(23, 6, "exit", 1, "ЧП");
        b.Type("НП", "line").Type("ЧП", "line");
        b.Text(10, 7, "← от станции А", 13).Text(17, 8, "к станции Б →", 13).Text(10, 2, "Разъезд", 20);
        return b.St;
    }
}
