using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  КНОПОЧНЫЙ ПУЛЬТ («ЕНИСЕЙ»)  [ДОБАВЛЕНО 28.10.2026]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//  Павлодарский режим интерфейса: под схемой — ряды кнопок, как на пульте-табло тренажёра ДСП:
//   • ПОЕЗДНЫЕ — кнопки светофоров поездных маршрутов, МАНЕВРОВЫЕ — маневровых.
//     Нажать кнопку светофора начала, потом светофора конца (или кнопку пути) — маршрут задан;
//     то же второй раз — маршрут отменён. Это тот же набор, что кликами по схеме.
//   • СТРЕЛКИ — у каждой стрелки «+», номер (горит зелёным в плюсе, жёлтым в минусе), «−».
//     ПКМ по номеру — вспомогательный перевод.
//   • ПУТИ И УЧАСТКИ — лампа занятости (красная — занят, белая — замкнут в маршруте).
//     Во время набора — путь назначения; без набора — искусственная разделка (ИР) этой секции.
// =====================================================================

class ClassicPanel : TableLayoutPanel
{
    static Interlocking IL => ModelHost.Model.IL;
    static readonly Color Off = Color.FromArgb(225, 225, 225), Dark = Color.FromArgb(20, 20, 20);

    readonly RailSchemePanel scheme;
    readonly Action<bool> setShunt;
    readonly List<Action> refresh = new();

    public ClassicPanel(StationView view, RailSchemePanel scheme, Action<bool> setShunt, ToolTip tips)
    {
        this.scheme = scheme;
        this.setShunt = setShunt;
        Dock = DockStyle.Fill;
        ColumnCount = 2;
        BackColor = SchemePalette.Current.Bg;
        Padding = new Padding(6, 2, 6, 2);
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // светофоры: начало или конец маршрутов своей категории
        foreach (var (shunt, title) in new[] { (false, "ПОЕЗДНЫЕ"), (true, "МАНЕВРОВЫЕ") })
        {
            var sigs = view.Routes.Where(r => r.Shunting == shunt)
                                  .SelectMany(r => new[] { r.Start, r.End }).Where(s => s != null).Distinct()
                                  .OrderBy(s => LetterPart(s.Label)).ThenBy(s => NumberPart(s.Label)).ToList();
            if (sigs.Count == 0) continue;
            var row = Row(title);
            foreach (var sg in sigs)
            {
                var s = sg;
                var b = Btn(s.Label, 56);
                b.Click += (_, _) =>
                {
                    if (scheme.Selected == null) setShunt(shunt);   // категорию задаёт кнопка начала
                    scheme.SelectSignal(s);
                };
                tips.SetToolTip(b, $"{s.Title}\nКнопка {(shunt ? "маневрового" : "поездного")} маршрута: сначала светофор начала, потом конца (или путь)");
                row.Controls.Add(b);
                refresh.Add(() => PaintSignal(b, s));
            }
        }

        // стрелки «+  №  −»
        if (view.Switches.Count > 0)
        {
            var row = Row("СТРЕЛКИ");
            foreach (var sw0 in view.Switches)
            {
                var sw = sw0;
                var grp = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(2, 2, 10, 2), BackColor = BackColor };
                var bPlus = Btn("+", 26);
                var lbl = Btn(sw.Label, 44);
                var bMinus = Btn("−", 26);
                bPlus.Click += (_, _) => Put(sw, true);
                bMinus.Click += (_, _) => Put(sw, false);
                lbl.Click += (_, _) => IL.ToggleSwitch(sw);
                lbl.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) RailSchemePanel.AskAuxSwitch(this, sw); };
                tips.SetToolTip(lbl, $"{sw.Title}\nЛКМ — перевести, ПКМ — вспомогательный перевод");
                grp.Controls.AddRange(new Control[] { bPlus, lbl, bMinus });
                row.Controls.Add(grp);
                refresh.Add(() =>
                {
                    var c = !sw.Controlled ? (ModelHost.BlinkPhase ? Color.FromArgb(240, 70, 70) : Off)
                          : sw.ShownPlus ? Color.FromArgb(90, 210, 110) : Color.FromArgb(255, 220, 60);
                    if (lbl.BackColor != c) lbl.BackColor = c;
                });
            }
        }

        // пути и участки
        var rowS = Row("ПУТИ, УЧАСТКИ");
        foreach (var sec0 in view.Sections)
        {
            var sec = sec0;
            var b = Btn(sec.Label, Math.Max(48, TextRenderer.MeasureText(sec.Label, Font).Width + 14));
            b.Click += (_, _) => SectionClick(sec);
            tips.SetToolTip(b, $"{sec.Title}\nВо время набора маршрута — путь назначения; без набора — искусственная разделка (ИР)");
            rowS.Controls.Add(b);
            refresh.Add(() =>
            {
                var c = sec.Occupied ? Color.FromArgb(230, 50, 50) : sec.LockedBy != null || sec.LineRoute ? Color.White : Off;
                if (b.BackColor != c) b.BackColor = c;
                var fc = sec.Occupied ? Color.White : Dark;
                if (b.ForeColor != fc) b.ForeColor = fc;
            });
        }
    }

    public void RefreshUi() { foreach (var a in refresh) a(); }

    // порядок кнопок: Ч, Ч1, Ч2 … Н, Н1 … М1, М2 … М10 (буквы, потом номер по величине)
    static string LetterPart(string label) => new(label.TakeWhile(c => !char.IsDigit(c)).ToArray());
    static int NumberPart(string label) => int.TryParse(new string(label.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray()), out var n) ? n : -1;

    /// <summary>Высота пульта при ширине width: ряды кнопок переносятся по ширине.</summary>
    public int NeededHeight(int width)
    {
        int w = Math.Max(100, width - Padding.Horizontal - 130);
        int h = Padding.Vertical;
        foreach (Control c in Controls)
            if (c is FlowLayoutPanel f) h += Math.Max(30, f.GetPreferredSize(new Size(w, 0)).Height) + 2;
        return h;
    }

    FlowLayoutPanel Row(string title)
    {
        int r = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(new Label
        {
            Text = title, AutoSize = true, ForeColor = SchemePalette.Current.Title, Font = new Font("Bahnschrift", 9.5f, FontStyle.Bold),
            Margin = new Padding(0, 9, 4, 0)
        }, 0, r);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0), BackColor = BackColor };
        Controls.Add(flow, 1, r);
        return flow;
    }

    static Button Btn(string text, int w)
    {
        var b = new Button
        {
            Text = text, Size = new Size(w, 28), Margin = new Padding(1, 2, 1, 2), FlatStyle = FlatStyle.Flat, BackColor = Off, ForeColor = Dark,
            Font = new Font("Bahnschrift", 9.5f, FontStyle.Bold), UseVisualStyleBackColor = false, Padding = new Padding(0)
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(110, 110, 110);
        return b;
    }

    void PaintSignal(Button b, RailSignal s)
    {
        bool sel = scheme.Selected == s;
        var c = sel ? (ModelHost.BlinkPhase ? Color.FromArgb(255, 170, 60) : Off)
              : s.Aspect switch
              {
                  Aspect.Green => Color.FromArgb(90, 210, 110),
                  Aspect.Yellow or Aspect.YellowBlink or Aspect.TwoYellow or Aspect.TwoYellowTopBlink => Color.FromArgb(255, 220, 60),
                  Aspect.White => Color.White,
                  Aspect.Invite => ModelHost.BlinkPhase ? Color.White : Off,
                  _ => Off
              };
        if (b.BackColor != c) b.BackColor = c;
    }

    static void Put(RailSwitch sw, bool plus)
    {
        if (sw.Plus == plus) { IL.Say($"Стрелка {sw.Label} уже в {(plus ? "плюсе" : "минусе")}"); return; }
        IL.ToggleSwitch(sw);
    }

    void SectionClick(TrackSection sec)
    {
        if (scheme.Selected != null) { scheme.SelectTarget(sec); return; }
        if (!IL.IrCandidates(new[] { sec }).Contains(sec))
        {
            IL.Say($"{sec.Label}: {(sec.Occupied ? "занят" : sec.LockedBy != null ? "замкнут в маршруте " + sec.LockedBy.Name : "свободен")}. " +
                   "Чтобы задать маршрут, сначала нажмите кнопку светофора начала.");
            return;
        }
        if (MessageBox.Show(FindForm(), $"ИСКУССТВЕННАЯ РАЗДЕЛКА секции {sec.Label}?\n\n" +
                                        "Дежурный обязан лично убедиться, что секция свободна от подвижного состава.\n" +
                                        $"Выдержка {IL.IrSeconds:0} с. Нажатие кнопки ИР записать в ДУ-46.",
                "Ответственная команда", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
            IL.StartIr(new List<TrackSection> { sec });
    }
}
