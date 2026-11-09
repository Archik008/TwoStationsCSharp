using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  ПУЛЬТ ДСП: КНОПКИ ЭТАПА 6  [ДОБАВЛЕНО 21.10.2026]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса. Логика — Core/Panel.cs.
//   • Build — группа кнопок в колонку «Стрелки…» панели станции: ЧЗС / НЗС / ЗС, АДЧ / АДН, ИР…, лампы ОС / ОП / ОМ,
//     «Взрез» и «Отключение звонка», «Контроль стрелок», ключ-жезл, переезд, «Раздельное управление»;
//   • AddSwitchItems — пункты меню стрелки на схеме: колпачок, курбель, замок / скоба, выключение, макет, информация;
//   • звонок взреза (сгенерированный WAV).
//  Кнопки с фиксацией (ЧЗС, НЗС, ЗС, АДЧ, АДН): ЛКМ — нажать, ПКМ — отжать, как в тренажёре ДСП.
// =====================================================================

public static class PanelUi
{
    static Interlocking IL => ModelHost.Model.IL;
    static TrainSim Trains => ModelHost.Model.Trains;

    /// <summary>Е13: подсветка положения всех стрелок (+ зелёным, − жёлтым), пока зажата кнопка или обе кнопки мыши на схеме.</summary>
    public static bool ShowSwitchPositions;

    static readonly Color Off = Color.FromArgb(238, 238, 238), RedOn = Color.FromArgb(225, 60, 60), Dark = Color.FromArgb(30, 45, 45);

    /// <summary>Колонка «Пульт ДСП» в нижней части окна станции.</summary>
    public static FlowLayoutPanel Column(Color back) => new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
        AutoScroll = true, BackColor = back, Padding = new Padding(6, 4, 0, 0)
    };

    /// <summary>Собрать кнопки этапа 6 для схемы view. Возвращает обновление (звать из RefreshUi панели).</summary>
    public static Action Build(FlowLayoutPanel col, StationView view, Control owner, ToolTip tips,
                               Func<string, int, int, Button> flat, Func<Control, Control> breaker, Func<string, Label> title)
    {
        var refresh = new List<Action>();
        col.Controls.Add(breaker(title("Пульт ДСП:")));
        // каждый ряд кнопок — своя строка колонки
        void Row(IEnumerable<Control> cs)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = col.BackColor };
            row.Controls.AddRange(cs.ToArray());
            if (row.Controls.Count > 0) col.Controls.Add(breaker(row));
        }
        var cur = new List<Control>();

        // --- Е4 замыкание стрелок ---
        var groups = new List<(string key, string label, string tip, List<RailSwitch> sws)>
        {
            ($"{view.Caption}:ЧЗС", "ЧЗС", "Чётное замыкание стрелок (чётные номера)", view.Switches.Where(s => Interlocking.SwitchNumber(s) % 2 == 0).ToList()),
            ($"{view.Caption}:НЗС", "НЗС", "Нечётное замыкание стрелок (нечётные номера)", view.Switches.Where(s => Interlocking.SwitchNumber(s) % 2 == 1).ToList()),
            ($"{view.Caption}:ЗС", "ЗС", "Замыкание всех стрелок станции", view.Switches.ToList()),
        };
        foreach (var (key, label, tip, sws) in groups.Where(g => g.sws.Count > 0))
        {
            IL.RegisterLockGroup(key, label, sws);
            var b = Fixed(flat, key, label, on => IL.SetGroupLock(key, on));
            tips.SetToolTip(b, $"{tip}: {string.Join(", ", sws.Select(s => s.Label))}.\nЛКМ — нажать (стрелки с пульта не переводятся), ПКМ — отжать.\nКнопка со счётчиком: каждое нажатие записать в ДУ-46.");
            cur.Add(b);
            refresh.Add(() => Paint(b, $"{label} {Du46.Counter(key):0000}", IL.ActiveLocks.Contains(key)));
        }
        Row(cur);
        cur = new();

        // --- Е8 автодействие ---
        foreach (var (dir, label) in new[] { (Dir.Right, "АДЧ"), (Dir.Left, "АДН") })
        {
            string key = $"{view.Caption}:{label}";
            IL.RegisterAutoGroup(key, label, view.Routes, dir);
            if (!view.Routes.Any(r => !r.Shunting && !r.Diverging && r.Start.Dir == dir)) continue;
            var b = Fixed(flat, key, label, on => IL.SetAutoAction(key, on));
            tips.SetToolTip(b, $"Автодействие светофоров {(dir == Dir.Right ? "чётного" : "нечётного")} направления по главному пути:\n" +
                               "заданные сквозные маршруты после проследования поезда задаются сами.\nЛКМ — включить (срыв пломбы → запись в ДУ-46), ПКМ — выключить.");
            cur.Add(b);
            refresh.Add(() => Paint(b, $"{label} {Du46.Counter(key):0000}", IL.AutoOn.Contains(key)));
        }
        Row(cur);
        cur = new();

        // --- Е2 ИР и Е1 лампы выдержки отмены ---
        var bIr = flat("ИР…", 70, 30);
        bIr.Click += (_, _) => AskIr(owner, view);
        tips.SetToolTip(bIr, "Искусственная разделка секций, оставшихся замкнутыми после проследования поезда (выдержка, счётчик ИР).");
        cur.Add(bIr);
        foreach (var kind in new[] { "ОС", "ОП", "ОМ" })
        {
            var lamp = Lamp(kind);
            tips.SetToolTip(lamp, Interlocking.CancelKindText(kind) + " — горит красным, пока идёт выдержка");
            cur.Add(lamp);
            refresh.Add(() =>
            {
                var r = view.Routes.Where(x => x.Active && x.Releasing && x.CancelKind == kind).OrderBy(x => x.ArtificialTimer).FirstOrDefault();
                Paint(lamp, r == null ? kind : $"{kind} {Math.Ceiling(r.ArtificialTimer)}", r != null);
            });
        }

        Row(cur);

        // --- Е11 взрез, Е13 контроль стрелок ---
        var cut = Lamp("Взрез");
        tips.SetToolTip(cut, "Лампа «Взрез»: горит красным, если стрелка взрезана");
        var bBell = flat("Откл. звонка", 110, 30);
        bBell.Click += (_, _) => IL.BellOff();
        tips.SetToolTip(bBell, "Отключение звонка взреза");
        var bPos = flat("Контроль стрелок", 140, 30);
        bPos.MouseDown += (_, _) => { ShowSwitchPositions = true; ModelHost.MarkDirty(); };
        bPos.MouseUp += (_, _) => { ShowSwitchPositions = false; ModelHost.MarkDirty(); };
        tips.SetToolTip(bPos, "Держите нажатой: на схеме подсвечиваются положения всех стрелок (+ зелёным, − жёлтым).\nТо же — нажать обе кнопки мыши на схеме.");
        Row(new Control[] { cut, bBell, bPos });
        refresh.Add(() => Paint(cut, "Взрез", IL.CutLamp && (IL.Bell ? ModelHost.BlinkPhase : true)));

        // --- Е9 ключ-жезл ---
        foreach (var ln in view.Lines)
        {
            var line = ln;
            var b = flat("Ключ-жезл", 120, 30);
            b.Click += (_, _) => KeyMenu(line).Show(b, new Point(0, b.Height));
            tips.SetToolTip(b, $"Ключ-жезл: {line.Title}. Клик — изъять, выдать машинисту, забрать, вложить.\n" +
                               "Горит — в пульте, мигает — изъят (смена направления запрещена, на перегон по сигналу — одна единица).");
            Row(new Control[] { b });
            refresh.Add(() =>
            {
                string t = !line.KeyOut ? "Ключ-жезл" : line.KeyTrain > 0 ? $"КЖ у №{line.KeyTrain}" : "КЖ изъят";
                Paint(b, t, !line.KeyOut || ModelHost.BlinkPhase);
            });
        }

        // --- Е10 переезд ---
        foreach (var cr in view.Crossings)
        {
            var c = cr;
            var zp = Lamp("ЗП");
            tips.SetToolTip(zp, $"{c.Title}: белая — свободен (открыт), мигает — закрывается, красная — закрыт");
            var bN = flat("Извещение", 96, 30);
            var bC = flat("Отмена изв.", 96, 30);
            var bZ = flat("Закрытие", 96, 30);
            bN.Click += (_, _) => IL.SetCrossingNotice(c, true);
            bC.Click += (_, _) => IL.SetCrossingNotice(c, false);
            bZ.Click += (_, _) => IL.SetCrossingManual(c, !c.ManualClosed);
            tips.SetToolTip(bN, "Извещение на переезд: переезд закрывается (лампа ЗП мигает), потом закрыт");
            tips.SetToolTip(bZ, "Закрытие переезда сразу (повторно — снять закрытие)");
            zp.Text = "ЗП";
            Row(new Control[] { zp, bN, bC, bZ });
            refresh.Add(() =>
            {
                zp.BackColor = c.Closed ? RedOn : c.Closing ? (ModelHost.BlinkPhase ? Color.White : Off) : Color.White;
                zp.ForeColor = c.Closed ? Color.White : Dark;
                Paint(bN, "Извещение", c.Notice);
                Paint(bZ, c.ManualClosed ? "Открыть" : "Закрытие", c.ManualClosed);
            });
        }

        // --- [ДОБАВЛЕНО 23.10.2026, этап 7] ПНП: лампы «локомотив на ПНП», «вагоны к уборке», функциональная кнопка ---
        foreach (var pn in Shunting.Pnps.Where(p => view.Sections.Contains(p.Section)))
        {
            var p = pn;
            var lLoco = Lamp("Лок.");
            var lReady = Lamp("Уборка");
            var b = flat($"ПНП «{p.Name}» ▾", 190, 30);
            b.MouseUp += (_, _) =>
            {
                var m = new ContextMenuStrip();
                ShuntUi.AddPnpItems(m.Items, p, owner);
                m.Show(b, new Point(0, b.Height));
            };
            tips.SetToolTip(lLoco, "Локомотив на ПНП: не горит — нет, белая — стоит, синяя — идёт маневровая работа");
            tips.SetToolTip(lReady, "Жёлтая — на ПНП есть вагоны, готовые к уборке");
            tips.SetToolTip(b, $"Путь необщего пользования «{p.Name}» ({p.Section.Label}): уведомление, положение, маневровая работа, журнал");
            Row(new Control[] { lLoco, lReady, b });
            refresh.Add(() =>
            {
                lLoco.BackColor = p.Loco == null ? Off : p.Working ? Color.FromArgb(60, 110, 255) : Color.White;
                lLoco.ForeColor = p.Working ? Color.White : Dark;
                lReady.BackColor = p.HasReady ? Color.FromArgb(255, 210, 0) : Off;
            });
        }

        // --- Е14 раздельное управление ---
        var chk = new CheckBox { Text = "Раздельное управление (как «Енисей»)", AutoSize = true, ForeColor = Dark };
        chk.CheckedChanged += (_, _) =>
        {
            if (chk.Checked == IL.SeparateControl) return;
            IL.SeparateControl = chk.Checked;
            IL.Say(chk.Checked ? "Раздельное управление: стрелки маршрутом не переводятся — ставьте их сами, потом задавайте маршрут"
                               : "Раздельное управление выключено: стрелки переводятся маршрутом", LogKind.Aux);
        };
        tips.SetToolTip(chk, "Как на пульте «Енисей»: ДСП сам переводит стрелки, потом нажимает светофор начала.\nНа уроке задаётся преподавателем.");
        col.Controls.Add(breaker(chk));
        refresh.Add(() => { if (chk.Checked != IL.SeparateControl) chk.Checked = IL.SeparateControl; });

        refresh.Add(Bell);
        return () => { foreach (var a in refresh) a(); };
    }

    /// <summary>Кнопка с фиксацией: ЛКМ — нажать, ПКМ — отжать.</summary>
    static Button Fixed(Func<string, int, int, Button> flat, string key, string label, Action<bool> set)
    {
        var b = flat(label, 96, 30);
        b.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) set(true);
            else if (e.Button == MouseButtons.Right) set(false);
        };
        return b;
    }

    static Label Lamp(string text) => new()
    {
        Text = text, AutoSize = false, Size = new Size(64, 26), TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(3, 4, 3, 3),
        BackColor = Off, ForeColor = Dark, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Bahnschrift", 9f, FontStyle.Bold)
    };

    static void Paint(Control c, string text, bool on)
    {
        if (c.Text != text) c.Text = text;
        var bc = on ? RedOn : Off;
        if (c.BackColor != bc) c.BackColor = bc;
        var fc = on ? Color.White : Dark;
        if (c.ForeColor != fc) c.ForeColor = fc;
    }

    // =================================================================
    //  ИР
    // =================================================================
    static void AskIr(Control owner, StationView view)
    {
        var cand = IL.IrCandidates(view.Sections);
        if (cand.Count == 0)
        {
            IL.Say("ИР: нет секций для разделки (замкнутых после проследования поезда и свободных)", LogKind.Refuse);
            return;
        }
        using var f = new Form
        {
            Text = "Искусственная разделка (ИР)", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 300), Font = new Font("Segoe UI", 10f), ShowInTaskbar = false
        };
        var list = new CheckedListBox { Location = new Point(12, 36), Size = new Size(356, 200), CheckOnClick = true };
        foreach (var s in cand) list.Items.Add(new SecItem(s), true);
        f.Controls.Add(new Label { Text = "Секции, не разомкнувшиеся за поездом:", AutoSize = true, Location = new Point(10, 10) });
        f.Controls.Add(list);
        var ok = new Button { Text = "Разделать", DialogResult = DialogResult.OK, Location = new Point(168, 250), Size = new Size(100, 32) };
        var no = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(274, 250), Size = new Size(94, 32) };
        f.Controls.Add(ok);
        f.Controls.Add(no);
        f.AcceptButton = ok;
        f.CancelButton = no;
        if (f.ShowDialog(owner) != DialogResult.OK) return;
        var secs = list.CheckedItems.Cast<SecItem>().Select(x => x.S).ToList();
        if (secs.Count == 0) return;
        if (MessageBox.Show(owner, $"ИСКУССТВЕННАЯ РАЗДЕЛКА секций {string.Join(", ", secs.Select(s => s.Label))}?\n\n" +
                                   "Дежурный обязан лично убедиться, что секции свободны от подвижного состава.\n" +
                                   $"Выдержка {IL.IrSeconds:0} с. Нажатие кнопки ИР записать в ДУ-46.",
                "Ответственная команда", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
            IL.StartIr(secs);
    }

    record SecItem(TrackSection S) { public override string ToString() => $"{S.Label} — {S.LockedBy?.Name}"; }

    // =================================================================
    //  КЛЮЧ-ЖЕЗЛ
    // =================================================================
    static ContextMenuStrip KeyMenu(DirectionLine l)
    {
        var m = new ContextMenuStrip();
        m.Items.Add(new ToolStripMenuItem($"Ключ-жезл: {l.Title}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        if (!l.KeyOut) m.Items.Add("Изъять ключ-жезл", null, (_, _) => IL.KeyTake(l));
        else if (l.KeyTrain == 0)
        {
            var give = new ToolStripMenuItem("Выдать ключ-жезл машинисту");
            foreach (var t in Trains.Trains.Where(t => !t.Leaving && !t.Cells.Any(l.Sections.Contains)))
            {
                var tr = t;
                give.DropDownItems.Add($"№{t.Number} ({t.ModeText}) на {t.Head?.Label}", null, (_, _) => IL.KeyGive(l, tr));
            }
            if (give.DropDownItems.Count == 0) give.Enabled = false;
            m.Items.Add(give);
            m.Items.Add("Вложить ключ-жезл", null, (_, _) => IL.KeyPut(l));
        }
        else m.Items.Add($"Забрать ключ-жезл (лок. №{l.KeyTrain})", null, (_, _) => IL.KeyBack(l, Trains));
        return m;
    }

    // =================================================================
    //  МЕНЮ СТРЕЛКИ НА СХЕМЕ
    // =================================================================
    public static void AddSwitchItems(ContextMenuStrip m, RailSwitch sw, IWin32Window owner)
    {
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Информация…", null, (_, _) => MessageBox.Show(owner, IL.SwitchInfo(sw), "Объекты ЭЦ", MessageBoxButtons.OK, MessageBoxIcon.Information));
        m.Items.Add(sw.RedCap ? "Удалить красный колпачок" : "Установить красный колпачок", null, (_, _) => IL.SetRedCap(sw, !sw.RedCap));

        var ctl = new ToolStripMenuItem("Управление стрелкой");
        Item(ctl, sw.FlapDown ? "Поднять курбельную заслонку" : "Опустить курбельную заслонку", true, () => IL.SetFlap(sw, !sw.FlapDown));
        Item(ctl, "Перевести курбелем в плюс", sw.FlapDown && !sw.Plus && sw.Clamp.Length == 0, () => IL.CrankTo(sw, true));
        Item(ctl, "Перевести курбелем в минус", sw.FlapDown && sw.Plus && sw.Clamp.Length == 0, () => IL.CrankTo(sw, false));
        if (sw.Clamp.Length == 0)
        {
            Item(ctl, "Запереть на закладку и замок", true, () => IL.SetClamp(sw, "замок"));
            Item(ctl, "Закрепить на типовую скобу", true, () => IL.SetClamp(sw, "скоба"));
        }
        else Item(ctl, sw.Clamp == "скоба" ? "Снять типовую скобу" : "Снять навесной замок", sw.Maket == null, () => IL.SetClamp(sw, ""));
        if (sw.Excluded)
        {
            if (sw.Maket == null)
            {
                Item(ctl, "Установить на макет: плюс", sw.Clamp.Length > 0, () => IL.SetMaket(sw, true));
                Item(ctl, "Установить на макет: минус", sw.Clamp.Length > 0, () => IL.SetMaket(sw, false));
            }
            else
            {
                Item(ctl, $"Макет: перевести в {(sw.Maket.Value ? "минус" : "плюс")}", !sw.Locked, () => IL.SetMaket(sw, !sw.Maket.Value));
                Item(ctl, "Снять с макета", !sw.Locked, () => IL.SetMaket(sw, null));
            }
        }
        m.Items.Add(ctl);

        if (!sw.Excluded) m.Items.Add("Выключение из централизации…", null, (_, _) => IL.RequestExclude(sw));
        else m.Items.Add(new ToolStripMenuItem("Включение в централизацию", null, (_, _) => IL.Include(sw)) { Enabled = sw.Maket == null && sw.Clamp.Length == 0 });

        // вне урока ШН нет — восстановление после взреза имитацией
        if (sw.Cut && !Training.Active) m.Items.Add("Восстановить после взреза (имитация)", null, (_, _) => IL.RestoreCut(sw));
    }

    static void Item(ToolStripMenuItem parent, string text, bool enabled, Action a) =>
        parent.DropDownItems.Add(new ToolStripMenuItem(text, null, (_, _) => a()) { Enabled = enabled });

    /// <summary>Пометка стрелки для подписи на схеме и кнопок: К — колпачок, З — заслонка, Л — замок/скоба, В — выключена, М — макет.</summary>
    public static string Marks(RailSwitch sw) =>
        (sw.RedCap ? "К" : "") + (sw.FlapDown ? "З" : "") + (sw.Clamp.Length > 0 ? "Л" : "") + (sw.Excluded ? (sw.Maket != null ? "М" : "В") : "");

    // =================================================================
    //  ЗВОНОК ВЗРЕЗА
    // =================================================================
    static System.Media.SoundPlayer bell;
    static bool ringing;

    static void Bell()
    {
        bool want = IL.Bell;
        if (want == ringing) return;
        ringing = want;
        try
        {
            if (want) { bell ??= new System.Media.SoundPlayer(BellWav()); bell.PlayLooping(); }
            else bell?.Stop();
        }
        catch { /* нет звука — работаем без него */ }
    }

    /// <summary>Звонок: 0,6 с дребезжащий тон 880 Гц с модуляцией, 0,4 с тишина.</summary>
    static MemoryStream BellWav()
    {
        const int rate = 22050;
        int n = rate;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(n * 2);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)rate;
            double v = t < 0.6 ? Math.Sin(2 * Math.PI * 880 * t) * (0.6 + 0.4 * Math.Sign(Math.Sin(2 * Math.PI * 25 * t))) * 0.35 : 0;
            w.Write((short)(v * short.MaxValue));
        }
        ms.Position = 0;
        return ms;
    }
}
