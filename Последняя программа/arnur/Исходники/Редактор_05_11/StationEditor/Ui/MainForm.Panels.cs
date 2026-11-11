using StationEditor.Model;

namespace StationEditor.Ui;

// =====================================================================
//  ПАНЕЛИ: слева — кубики и инструменты, справа — вкладки
//  «Свойства», «Участки», «Маршруты», «Проверка».
//  Панели перестраиваются отложенно (BeginInvoke), чтобы не разрушать элемент
//  управления прямо в его же обработчике события.
// =====================================================================
public sealed partial class MainForm
{
    static readonly Color CText = Color.FromArgb(0x23, 0x37, 0x32);
    static readonly Color CHint = Color.FromArgb(0x4a, 0x5a, 0x56);
    static readonly Color CAccent = Color.FromArgb(0x1f, 0x7a, 0x68);
    static readonly Color CDanger = Color.FromArgb(0xb3, 0x13, 0x1a);

    Font fH3, fH4, fSym;
    Font FH3 => fH3 ??= new Font(Font.FontFamily, 11f, FontStyle.Bold);
    Font FH4 => fH4 ??= new Font(Font.FontFamily, 9.5f, FontStyle.Bold);
    Font FSym => fSym ??= new Font("Segoe UI Symbol", 16f);

    sealed class Opt
    {
        public string V, T;
        public override string ToString() => T;
    }

    // =================================================================
    //  ОТЛОЖЕННОЕ ОБНОВЛЕНИЕ
    // =================================================================
    bool panelsQueued, panelsRebuild, palQueued, optsQueued, syncingTab;
    string renderedTab;

    void Later(Action a)
    {
        if (IsHandleCreated) BeginInvoke(a);
        else a();
    }

    void RefreshPanels(bool rebuildTab = true)
    {
        panelsRebuild |= rebuildTab;
        if (panelsQueued || !IsHandleCreated) return;
        panelsQueued = true;
        BeginInvoke((Action)DoRefreshPanels);
    }

    void RefreshPalette()
    {
        if (!IsHandleCreated) { DoRefreshPalette(); return; }
        if (palQueued) return;
        palQueued = true;
        BeginInvoke((Action)(() => { palQueued = false; DoRefreshPalette(); }));
    }

    void RefreshToolOpts()
    {
        if (!IsHandleCreated) { DoRefreshToolOpts(); return; }
        if (optsQueued) return;
        optsQueued = true;
        BeginInvoke((Action)(() => { optsQueued = false; DoRefreshToolOpts(); }));
    }

    void ShowTab(string t)
    {
        ui.Tab = t;
        RefreshPanels();
    }

    TabPage PageOf(string tab) => tab switch { "secs" => tpSecs, "routes" => tpRoutes, "checks" => tpChecks, _ => tpProps };

    void DoRefreshPanels()
    {
        panelsQueued = false;
        bool rb = panelsRebuild;
        panelsRebuild = false;

        int errs = Validator.Validate(st, Routes).Count(p => p.Level == "err");
        var ct = errs > 0 ? $"Проверка ({errs})" : "Проверка";
        if (tpChecks.Text != ct) tpChecks.Text = ct;

        var page = PageOf(ui.Tab);
        if (tabs.SelectedTab != page)
        {
            syncingTab = true;
            tabs.SelectedTab = page;
            syncingTab = false;
        }
        if (rb || ui.Tab != "props" || renderedTab != ui.Tab)
        {
            page.SuspendLayout();
            switch (ui.Tab)
            {
                case "secs": RenderSecs(page); break;
                case "routes": RenderRoutes(page); break;
                case "checks": RenderChecks(page); break;
                default: RenderProps(page); break;
            }
            page.ResumeLayout(true);
            renderedTab = ui.Tab;
        }
        bUndo.Enabled = undoStack.Count > 0 && !ui.IsTest;
        bRedo.Enabled = redoStack.Count > 0 && !ui.IsTest;
    }

    // =================================================================
    //  ПОМОЩНИКИ ДЛЯ ПОСТРОЕНИЯ ПАНЕЛЕЙ
    // =================================================================
    static void ClearControls(Control c)
    {
        var list = c.Controls.Cast<Control>().ToList();
        c.Controls.Clear();
        foreach (var x in list) x.Dispose();
    }

    FlowLayoutPanel NewFlow(ScrollableControl host, out int w)
    {
        ClearControls(host);
        host.AutoScrollPosition = Point.Empty;
        w = Math.Max(S(200), host.ClientSize.Width - S(28));
        var f = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(S(6), S(6)), Margin = Padding.Empty,
        };
        host.Controls.Add(f);
        return f;
    }

    Label H3(Control f, int w, string text) => AddTo(f, new Label
    { Text = text, AutoSize = true, MaximumSize = new Size(w, 0), Font = FH3, ForeColor = CText, Margin = new Padding(0, S(2), 0, S(6)) });

    Label H4(Control f, int w, string text) => AddTo(f, new Label
    { Text = text, AutoSize = true, MaximumSize = new Size(w, 0), Font = FH4, ForeColor = CText, Margin = new Padding(0, S(10), 0, S(4)) });

    Label Hint(Control f, int w, string text, Color? color = null) => AddTo(f, new Label
    { Text = text, AutoSize = true, MaximumSize = new Size(w, 0), ForeColor = color ?? CHint, Margin = new Padding(0, 0, 0, S(6)) });

    static T AddTo<T>(Control f, T c) where T : Control
    {
        f.Controls.Add(c);
        return c;
    }

    Button AddButton(Control f, string text, Action a, bool danger = false)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, S(4), S(4), S(4)), UseVisualStyleBackColor = true };
        if (danger) b.ForeColor = CDanger;
        b.Click += (_, _) => a();
        f.Controls.Add(b);
        return b;
    }

    /// <summary>Строка «подпись — поле».</summary>
    T Row<T>(Control f, int w, string label, T c) where T : Control
    {
        int lw = S(104);
        int h = Math.Max(c.Height, S(24));
        var p = new Panel { Width = w, Height = h + S(4), Margin = new Padding(0, 0, 0, S(2)) };
        var l = new Label { Text = label, AutoSize = false, Width = lw - S(4), Height = h, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CText, Location = new Point(0, S(2)) };
        c.Location = new Point(lw, S(2) + (h - c.Height) / 2);
        if (c is not Button) c.Width = w - lw;
        p.Controls.Add(l);
        p.Controls.Add(c);
        f.Controls.Add(p);
        return c;
    }

    /// <summary>Поле ввода: применяется по Enter или при уходе из поля (как «change» в браузере).</summary>
    TextBox CommitBox(string value, string placeholder, Action<string> commit)
    {
        var tb = new TextBox { Text = value ?? "", PlaceholderText = placeholder ?? "" };
        string last = tb.Text;
        void Do()
        {
            if (tb.IsDisposed || tb.Text == last) return;
            last = tb.Text;
            commit(tb.Text);
        }
        tb.Leave += (_, _) => Do();
        tb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Do(); } };
        return tb;
    }

    ComboBox Combo(IEnumerable<Opt> items, string selected)
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 20, IntegralHeight = false };
        foreach (var o in items) cb.Items.Add(o);
        for (int i = 0; i < cb.Items.Count; i++)
            if (((Opt)cb.Items[i]).V == selected) { cb.SelectedIndex = i; break; }
        if (cb.SelectedIndex < 0 && cb.Items.Count > 0) cb.SelectedIndex = 0;
        return cb;
    }

    IEnumerable<Opt> SecOptions(bool withNew, bool keep = false, bool autoPath = false)
    {
        if (keep) yield return new Opt { V = "__keep", T = "— не менять —" };
        yield return new Opt { V = "", T = "— без участка —" };
        foreach (var n in Core.SectionNames(st)) yield return new Opt { V = n, T = n };
        if (withNew) yield return new Opt { V = "__new", T = "+ новый участок…" };
        if (autoPath) yield return new Opt { V = AutoPath, T = "+ путь (номер по правилам)" };   // [ДОБАВЛЕНО 11.11.2026] только у кисти
    }

    static string OptV(ComboBox cb) => (cb.SelectedItem as Opt)?.V;

    /// <summary>Выбор участка в списке (с пунктом «+ новый участок…»): apply получает имя участка.</summary>
    void OnSecChosen(ComboBox cb, Action<string> apply)
    {
        cb.SelectedIndexChanged += (_, _) =>
        {
            var v = OptV(cb);
            if (v == "__keep") return;
            Later(() =>
            {
                if (v == "__new")
                {
                    v = NewSection(false);
                    if (v.Length == 0) { RefreshPanels(); RefreshToolOpts(); return; }
                }
                apply(v);
            });
        };
    }

    Tile TileAt(string k) => st.Tiles.TryGetValue(k, out var t) ? t : null;

    Signal SigAt(string k, int i) =>
        st.Tiles.TryGetValue(k, out var t) && t.Sigs != null && i < t.Sigs.Count ? t.Sigs[i] : null;

    // =================================================================
    //  ЛЕВАЯ ПАНЕЛЬ: КУБИКИ И ИНСТРУМЕНТЫ
    // =================================================================
    static readonly (string Id, string Name, string Key)[] Tools =
    {
        ("select", "Выбор", "V"), ("erase", "Ластик", "E"), ("brush", "Участок", "B"), ("signal", "Светофор", "S"), ("text", "Надпись", "T"),
        ("label", "Свободный текст", "L"),    // [ДОБАВЛЕНО 10.11.2026] подпись в любом месте, тянется мышью не по клеткам
    };

    int LeftW => S(214);

    PaletteItem PalItem(string caption, string key, bool on, Action<Graphics, RectangleF> icon, string tip)
    {
        var it = new PaletteItem
        {
            Caption = caption, KeyHint = key, On = on, DrawIcon = icon, Size = new Size(S(66), S(66)),
            Margin = new Padding(S(2)), Font = Font,
        };
        tips.SetToolTip(it, tip);
        return it;
    }

    FlowLayoutPanel PalGrid() => new()
    {
        FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MaximumSize = new Size(LeftW + S(4), 0), Margin = new Padding(0, 0, 0, S(6)),
    };

    void DrawToolIcon(Graphics g, RectangleF r, string id)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        switch (id)
        {
            case "signal":
                Renderer.DrawSignalIcon(g, r);
                return;
            case "select":
            {
                using var p = new Pen(CText, 2) { DashPattern = new[] { 2f, 1.5f } };
                g.DrawRectangle(p, r.X + r.Width * 0.18f, r.Y + r.Height * 0.18f, r.Width * 0.64f, r.Height * 0.64f);
                return;
            }
            case "brush":
            {
                float c = r.Width * 0.3f, x0 = r.X + r.Width * 0.18f, y0 = r.Y + r.Height * 0.18f;
                var cols = new[] { Core.Hsl(10, 0.75, 0.52), Core.Hsl(140, 0.75, 0.45), Core.Hsl(210, 0.75, 0.52), Core.Hsl(50, 0.85, 0.5) };
                for (int i = 0; i < 4; i++)
                {
                    using var b = new SolidBrush(cols[i]);
                    g.FillRectangle(b, x0 + (i % 2) * (c + 2), y0 + (i / 2) * (c + 2), c, c);
                }
                return;
            }
            case "erase":
                TextRenderer.DrawText(g, "⌫", FSym, Rectangle.Round(r), Pal.Bad, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            case "text":
                using (var ft = new Font(Font.FontFamily, 15f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Aa", ft, Rectangle.Round(r), CText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            case "label":    // [ДОБАВЛЕНО 10.11.2026] свободный текст: «Aa» в пунктирной рамке со стрелками — двигается куда угодно
            {
                using (var ft = new Font(Font.FontFamily, 12f, FontStyle.Italic | FontStyle.Bold))
                    TextRenderer.DrawText(g, "Aa", ft, Rectangle.Round(r), CText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using var p = new Pen(CText, 1.2f) { DashPattern = new[] { 2f, 2f } };
                g.DrawRectangle(p, r.X + r.Width * 0.12f, r.Y + r.Height * 0.22f, r.Width * 0.76f, r.Height * 0.56f);
                using var a = new Pen(CText, 1.4f) { EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor };
                g.DrawLine(a, r.X + r.Width * 0.62f, r.Y + r.Height * 0.86f, r.X + r.Width * 0.92f, r.Y + r.Height * 0.98f);
                return;
            }
        }
    }

    void DoRefreshPalette()
    {
        palettePanel.SuspendLayout();
        ClearControls(palettePanel);
        int w = LeftW;
        if (ui.IsTest)
        {
            Hint(palettePanel, w, "В режиме испытания схему не меняют. Вернитесь в «Редактор», чтобы рисовать.");
            palettePanel.ResumeLayout(true);
            return;
        }
        H4(palettePanel, w, "Кубики пути").Margin = new Padding(0, 0, 0, S(4));
        var grid = PalGrid();
        foreach (var T in Core.TypeList)
        {
            var id = T.Id;
            var tile = new Tile { T = id, V = id == ui.PlaceType ? CurVariant : 0 };
            var it = PalItem(T.Name, T.Key, ui.Tool == "place" && ui.PlaceType == id, (g, r) => Renderer.DrawTileIcon(g, tile, r),
                $"{T.Name} — {T.Hint} (клавиша {T.Key})");
            it.Click += (_, _) => { ui.PlaceType = id; SetTool("place"); };
            grid.Controls.Add(it);
        }
        palettePanel.Controls.Add(grid);

        // [ДОБАВЛЕНО 05.11.2026] объекты станции: амперметр, вокзал, часовая башня
        H4(palettePanel, w, "Объекты станции").Margin = new Padding(0, S(4), 0, S(4));
        var gridO = PalGrid();
        foreach (var O in Core.ObjTypes)
        {
            var id = O.Id;
            var it = PalItem(O.Name, O.Key, ui.Tool == "object" && ui.ObjType == id, (g, r) => Renderer.DrawObjectIcon(g, id, r),
                $"{O.Name} — {O.Hint} (клавиша {O.Key})");
            it.Click += (_, _) => { ui.ObjType = id; SetTool("object"); };
            gridO.Controls.Add(it);
        }
        palettePanel.Controls.Add(gridO);

        H4(palettePanel, w, "Разметка и правка").Margin = new Padding(0, S(4), 0, S(4));
        var grid2 = PalGrid();
        foreach (var T in Tools)
        {
            var id = T.Id;
            var it = PalItem(id == "label" ? "Своб. текст" : T.Name, T.Key, ui.Tool == id, (g, r) => DrawToolIcon(g, r, id),
                $"{T.Name} (клавиша {T.Key})" + (id == "label" ? " — подпись в любом месте схемы, тянется мышью не по клеткам" : ""));
            it.Click += (_, _) => SetTool(id);
            grid2.Controls.Add(it);
        }
        palettePanel.Controls.Add(grid2);
        palettePanel.ResumeLayout(true);
    }

    void DoRefreshToolOpts()
    {
        optsPanel.SuspendLayout();
        ClearControls(optsPanel);
        int w = LeftW;
        if (ui.IsTest) { optsPanel.ResumeLayout(true); return; }

        var hints = new Dictionary<string, string>
        {
            ["select"] = "Щелчок — выбрать клетку или светофор. Тяните по пустому месту — выделить рамкой, тяните выделенное — перенести. " +
                         "Delete — удалить, стрелки клавиатуры — сдвинуть, R — повернуть.",
            ["erase"] = "Щелчок или протягивание — стереть кубики и надписи. Правая кнопка мыши стирает в любом инструменте.",
            ["brush"] = "Протяните по кубикам — они войдут в участок. Ctrl+щелчок — взять участок с клетки. " +
                        "Стыки рисуются сами там, где участок меняется.",
            ["signal"] = "Щелчок по кубику пути — поставить светофор. «→» встаёт под путём на правом краю клетки, «←» — над путём на левом.",
            ["text"] = "Щелчок по клетке — написать или поправить надпись (номер пути, «к станции …»).",
            ["label"] = "Щелчок по свободному месту — новый текст ровно там, где щёлкнули (не по клеткам). Тяните текст мышью — " +
                        "двигается плавно, куда угодно; двойной щелчок — поправить. Размер, жирность — в «Свойствах»; " +
                        "стрелки клавиатуры — подвинуть чуть-чуть (Shift — на клетку), Delete — удалить.",
            ["object"] = "Щелчок по сетке — поставить объект (левый верхний угол). Выбрать, перетащить и поменять название или размер — " +
                         "инструментом «Выбор» (V). Амперметр показывает ток перевода стрелок в «Испытании», часы — время компьютера.",
        };

        if (ui.Tool == "place")
        {
            var T = Core.Types[ui.PlaceType];
            H4(optsPanel, w, $"{T.Name}: вариант (R)").Margin = new Padding(0, 0, 0, S(4));
            var vf = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MaximumSize = new Size(w, 0), Margin = new Padding(0, 0, 0, S(6)) };
            for (int i = 0; i < T.Variants.Length; i++)
            {
                int ii = i;
                var vi = new VariantIcon { Tile = new Tile { T = ui.PlaceType, V = i }, On = i == CurVariant, Size = new Size(S(40), S(40)), Margin = new Padding(0, 0, S(4), S(4)) };
                tips.SetToolTip(vi, "Вариант " + (i + 1));
                vi.Click += (_, _) => { ui.Variant[ui.PlaceType] = ii; RefreshToolOpts(); RefreshPalette(); UpdateGhost(); Draw(); };
                vf.Controls.Add(vi);
            }
            optsPanel.Controls.Add(vf);
            Hint(optsPanel, w, "Участок новых кубиков:").Margin = new Padding(0, 0, 0, S(2));
            var cb = AddTo(optsPanel, Combo(SecOptions(true), ui.BrushSec));
            cb.Width = w;
            OnSecChosen(cb, v => { ui.BrushSec = v; UpdateGhost(); RefreshToolOpts(); });
            Hint(optsPanel, w, $"{T.Hint}. Щелчок или протягивание — поставить, R / Shift+R — повернуть." +
                (ui.PlaceType == "straight" ? " Путь сам ложится по направлению протягивания: ─ ╱ ╲." : "")).Margin = new Padding(0, S(6), 0, 0);
        }
        else if (ui.Tool == "brush")
        {
            Hint(optsPanel, w, "Участок:").Margin = new Padding(0, 0, 0, S(2));
            var cb = AddTo(optsPanel, Combo(SecOptions(true, autoPath: true), ui.BrushSec));
            cb.Width = w;
            OnSecChosen(cb, v => { ui.BrushSec = v; UpdateGhost(); RefreshToolOpts(); });
            Hint(optsPanel, w, hints["brush"]).Margin = new Padding(0, S(6), 0, 0);
        }
        else if (ui.Tool == "signal")
        {
            Hint(optsPanel, w, "Тип светофора:").Margin = new Padding(0, 0, 0, S(2));
            // [ИЗМЕНЕНО 10.11.2026] «Авто» — вид по участку (окно «Обозначения…»)
            var kindOpts = new List<Opt> { new() { V = "auto", T = "Авто (по участку)" } };
            kindOpts.AddRange(Core.SigKinds.Select(k => new Opt { V = k.Id, T = k.Name }));
            var ck = AddTo(optsPanel, Combo(kindOpts, ui.SigKind));
            ck.Width = w;
            ck.SelectedIndexChanged += (_, _) => { ui.SigKind = OptV(ck); RefreshToolOpts(); };
            Hint(optsPanel, w, ui.SigKind == "auto"
                ? "Вид — по участку: путь → выходной; перегон → входной перед станцией, иначе проходной; тупик, стрелочный → маневровый. " +
                  "Поменять — «Обозначения…»."
                : Core.SigKindHint(ui.SigKind)).Margin = new Padding(0, S(4), 0, 0);
            Hint(optsPanel, w, "Направление:").Margin = new Padding(0, S(6), 0, S(2));
            var dirs = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
            string lr = Core.DirLetter(st, "R"), ll = Core.DirLetter(st, "L");
            foreach (var (d, text, tip) in new[]
                     { ("R", "→ " + lr, (lr == "Ч" ? "чётное" : "нечётное") + ", вправо"), ("L", "← " + ll, (ll == "Ч" ? "чётное" : "нечётное") + ", влево") })
            {
                var b = new Button { Text = text, Width = (w - S(8)) / 2, Height = S(30), Margin = new Padding(0, 0, S(4), 0), UseVisualStyleBackColor = ui.SigDir != d };
                if (ui.SigDir == d) { b.BackColor = Color.FromArgb(0xd8, 0xef, 0xe9); b.Font = FH4; }
                tips.SetToolTip(b, tip);
                b.Click += (_, _) => { ui.SigDir = d; RefreshToolOpts(); };
                dirs.Controls.Add(b);
            }
            optsPanel.Controls.Add(dirs);
            Hint(optsPanel, w, hints["signal"] + " Имя подставится само по «Обозначениям…»: буква — от ближайшего светофора " +
                               "(рядом с Ч→ новый ← будет Н), выходной — по номеру пути (Н3). Поменять можно в «Свойствах».").Margin = new Padding(0, S(6), 0, 0);
        }
        else if (ui.Tool == "object")
        {
            var O = Core.ObjTypeOf(ui.ObjType);
            H4(optsPanel, w, $"{O.Name} ({O.W}×{O.H} клеток)").Margin = new Padding(0, 0, 0, S(4));
            Hint(optsPanel, w, O.Hint + ".");
            Hint(optsPanel, w, hints["object"]);
        }
        else if (hints.TryGetValue(ui.Tool, out var h)) Hint(optsPanel, w, h);
        optsPanel.ResumeLayout(true);
    }

    void SetTool(string t)
    {
        if (ui.IsTest) return;
        ui.Tool = t;
        if (t != "select") { ui.Moving = null; ui.Band = null; }
        UpdateGhost();
        RefreshPalette();
        RefreshToolOpts();
        var hint = t switch
        {
            "select" => "Выбор: щелчок — выбрать, рамка — выделить несколько",
            "place" => $"Ставим «{Core.Types[ui.PlaceType].Name}»: щелчок / протягивание, R — повернуть, ПКМ — стереть",
            "erase" => "Ластик: щелчок / протягивание",
            "brush" => "Участок: протяните по кубикам",
            "signal" => "Светофор: щелчок по пути",
            "text" => "Надпись: щелчок по клетке",
            "label" => "Свободный текст: щелчок — новый, тяните — подвинуть, двойной щелчок — поправить",
            "object" => $"Объект «{Core.ObjTypeOf(ui.ObjType).Name}»: щелчок по сетке",
            _ => "",
        };
        sHint.Text = hint + " · колесо — масштаб, средняя кнопка или пробел+мышь — сдвинуть схему";
        Draw();
    }

    // =================================================================
    //  ВКЛАДКА «СВОЙСТВА»
    // =================================================================
    void RenderProps(TabPage host)
    {
        var f = NewFlow(host, out int w);
        if (ui.IsTest)
        {
            Hint(f, w, "В режиме испытания свойства не правятся. Маршруты — на вкладке «Маршруты».");
            return;
        }
        if (ui.SelObj != null && st.Objects.TryGetValue(ui.SelObj, out var ob))
        {
            RenderObjectProps(f, w, ui.SelObj, ob);
            return;
        }
        if (ui.SelLabel is int sli && sli < st.Labels.Count)    // [ДОБАВЛЕНО 10.11.2026]
        {
            RenderLabelProps(f, w, sli);
            return;
        }
        if (ui.Sel.Count == 0)
        {
            H3(f, w, st.Name);
            Hint(f, w, "Ничего не выбрано. Инструментом «Выбор» (V) щёлкните по клетке или светофору.");
            // [ИЗМЕНЕНО 11.11.2026] направление (буквы Н / Ч) — одна настройка «Чётные поезда» ниже, в «Нумерации»
            Hint(f, w, "Новый светофор получает имя сам: буква — от входного своего направления, а если входных нет — от ближайшего " +
                       "светофора или по настройке «Чётные поезда» (ниже, в «Нумерации»), " +
                       "выходной — по номеру пути (Н3, ЧII), маневровый — М по горловине. Шаблоны и «Перенумеровать все» — " +
                       "кнопка «Обозначения…». Имя, введённое вручную, закрепляется (галочка «имя вручную»).");
            Hint(f, w, $"Кубиков: {st.Tiles.Count}, участков: {Core.SectionNames(st).Count}, светофоров: {Core.AllSignals(st).Count}, " +
                       $"маршрутов найдено: {Routes.Count}.");
            H4(f, w, "С чего начать");
            Hint(f, w, "1. Кубиками (клавиши 1–7) нарисуйте пути, стрелки, тупики, выходы на перегон.\n" +
                       "2. Кистью «Участок» (B) разбейте пути на участки — 1П, 2-4СП, Ч-М2…\n" +
                       "3. Поставьте светофоры (S) и пронумеруйте стрелки.\n" +
                       "4. «Испытание» — проверьте маршруты, стрелки и занятость.\n" +
                       "5. «Сохранить» (Ctrl+S) — схема записывается в файл .station.json.\n" +
                       "6. «Экспорт в C#» — черновик кода схемы для программы станции.");
            AddButton(f, "Открыть пример «Алгабас»", () => LoadExample("algabas"));
            RenderNumberingProps(f, w);   // [ДОБАВЛЕНО 11.11.2026]
            H4(f, w, "Файл");
            Hint(f, w, currentFile ?? "ещё не сохранён — нажмите «Сохранить» (по умолчанию — папка «Станции» рядом с программой)");
            return;
        }
        if (ui.Sel.Count > 1)
        {
            var keys = ui.Sel.ToList();
            int n = keys.Count(st.Tiles.ContainsKey);
            H3(f, w, $"Выделено клеток: {ui.Sel.Count}");
            var cbm = Row(f, w, "Участок", Combo(SecOptions(true, keep: true), "__keep"));
            OnSecChosen(cbm, v => Edit(() => { foreach (var k2 in keys) if (st.Tiles.TryGetValue(k2, out var t2)) t2.Sec = v; }));
            Hint(f, w, $"Кубиков пути: {n}. Тяните выделение мышью или стрелками клавиатуры, R — повернуть каждый.");
            AddButton(f, "Удалить выделенное", DeleteSelection, danger: true);
            return;
        }

        var k = ui.Sel.First();
        var t = TileAt(k);
        st.Texts.TryGetValue(k, out var tx);
        if (t != null)
        {
            var T = Core.Types[t.T];
            H3(f, w, $"{T.Name}  ·  клетка {k}");
            Hint(f, w, "Вариант:").Margin = new Padding(0, 0, 0, S(2));
            var vf = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MaximumSize = new Size(w, 0), Margin = new Padding(0, 0, 0, S(6)) };
            for (int i = 0; i < T.Variants.Length; i++)
            {
                int ii = i;
                var vi = new VariantIcon { Tile = new Tile { T = t.T, V = i, Plus = t.Plus }, On = i == t.V, Size = new Size(S(38), S(38)), Margin = new Padding(0, 0, S(4), S(4)) };
                vi.Click += (_, _) => Edit(() => { if (TileAt(k) is Tile c) c.V = ii; });
                vf.Controls.Add(vi);
            }
            f.Controls.Add(vf);

            var cbs = Row(f, w, "Участок", Combo(SecOptions(true), t.Sec ?? ""));
            OnSecChosen(cbs, v => Edit(() => { if (TileAt(k) is Tile c) c.Sec = v; }));
            if (!string.IsNullOrEmpty(t.Sec))
                Row(f, w, "Тип участка", new Label { Text = Core.SecTypeName(Core.SecType(st, t.Sec)), AutoSize = false, Height = S(22), TextAlign = ContentAlignment.MiddleLeft, ForeColor = CHint });

            if (t.T == "switch")
            {
                H4(f, w, "Стрелка");
                Row(f, w, "Номер", CommitBox(t.Name, "например 2", v => Edit(() => { if (TileAt(k) is Tile c) c.Name = v.Trim(); }, false)));
                Row(f, w, "Съезд", CommitBox(t.Group, "пусто — одиночная; 4/6 — спаренная",
                    v => Edit(() => { if (TileAt(k) is Tile c) c.Group = string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }, false)));
                var pos = Row(f, w, "Положение", Combo(new[] { new Opt { V = "plus", T = "плюс (прямо)" }, new Opt { V = "minus", T = "минус (на отклонение)" } },
                    t.Plus == false ? "minus" : "plus"));
                pos.SelectedIndexChanged += (_, _) =>
                {
                    bool minus = OptV(pos) == "minus";
                    Later(() => Edit(() => { if (TileAt(k) is Tile c) c.Plus = minus ? false : null; }));
                };
                Hint(f, w, "Стрелки с одинаковым «съездом» переводятся вместе (спаренные).");
            }

            H4(f, w, "Светофоры на клетке");
            if (!t.HasSigs) Hint(f, w, "нет");
            for (int i = 0; i < (t.Sigs?.Count ?? 0); i++)
            {
                int ii = i;
                var s = t.Sigs[i];
                bool on = ui.SelSig == new SigRef(k, i);
                var row = new Panel { Width = w, Height = S(30), Margin = new Padding(0, 0, 0, S(3)), BackColor = on ? Color.FromArgb(0xff, 0xf6, 0xb0) : Color.Transparent };
                int x = S(2), y = S(3);
                // [ИЗМЕНЕНО 10.11.2026] имя, введённое руками, закрепляется; пустое — снова автоматическое
                var nm = CommitBox(s.Name, "имя", v => Edit(() =>
                {
                    if (SigAt(k, ii) is not Signal c) return;
                    var n = v.Trim();
                    if (n == c.Name) return;
                    c.Name = n;
                    c.Fixed = n.Length > 0 ? true : null;
                }, false));
                nm.SetBounds(x, y, (int)(w * 0.26), nm.Height);
                x += nm.Width + S(3);
                var kind = Combo(Core.SigKinds.Select(q => new Opt { V = q.Id, T = q.Name }), s.Kind);
                kind.SetBounds(x, y, (int)(w * 0.42), kind.Height);
                x += kind.Width + S(3);
                var dir = Combo(new[] { new Opt { V = "R", T = "→" }, new Opt { V = "L", T = "←" } }, s.Dir);
                dir.SetBounds(x, y, S(48), dir.Height);
                x += dir.Width + S(3);
                var del = new Button { Text = "✕", ForeColor = CDanger, UseVisualStyleBackColor = true };
                del.SetBounds(x, y - 1, Math.Max(S(26), w - x - S(2)), nm.Height + 2);
                tips.SetToolTip(del, "Удалить светофор");
                kind.SelectedIndexChanged += (_, _) => { var v = OptV(kind); Later(() => Edit(() => { if (SigAt(k, ii) is Signal c) c.Kind = v; })); };
                dir.SelectedIndexChanged += (_, _) => { var v = OptV(dir); Later(() => Edit(() => { if (SigAt(k, ii) is Signal c) c.Dir = v; })); };
                del.Click += (_, _) => Later(() => Edit(() =>
                {
                    if (TileAt(k) is Tile c && c.Sigs != null && ii < c.Sigs.Count)
                    {
                        c.Sigs.RemoveAt(ii);
                        if (c.Sigs.Count == 0) c.Sigs = null;
                    }
                    ui.SelSig = null;
                }));
                foreach (Control fc in new Control[] { nm, kind, dir })
                    fc.Enter += (_, _) => { ui.SelSig = new SigRef(k, ii); Draw(); };
                row.Controls.AddRange(new Control[] { nm, kind, dir, del });
                f.Controls.Add(row);

                // [ДОБАВЛЕНО 10.11.2026] цифра в имени: своя (поле «№») или от названия пути / порядковая
                var se = new SignalEntry { Key = k, Idx = ii, Sig = s, Tile = t };
                string autoNum = Naming.AutoNumber(st, se, out var autoFrom);
                var row2 = new Panel { Width = w, Height = S(28), Margin = new Padding(0, 0, 0, 0), BackColor = row.BackColor };
                var nl = new Label { Text = "№", AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = CHint };
                nl.SetBounds(S(2), S(3), S(22), S(22));
                var numBox = CommitBox(s.Num, autoNum.Length > 0 ? $"{autoNum} ({autoFrom})" : "нет", v => Edit(() =>
                {
                    if (SigAt(k, ii) is not Signal c) return;
                    var n = v.Trim();
                    if ((c.Num ?? "") == n && c.Ltr != null && !c.IsFixed) return;
                    if (c.Ltr == null || c.IsFixed)    // старое или закреплённое имя — теперь собирается по шаблону с этой цифрой
                    {
                        c.Ltr = Naming.LetterOf(c) ?? Naming.LetterFor(st, k, c.Dir, new SignalEntry { Key = k, Idx = ii, Sig = c, Tile = t });
                        c.Fixed = null;
                    }
                    c.Num = n.Length > 0 ? n : null;
                }, false));
                numBox.SetBounds(S(26), S(2), (int)(w * 0.30), numBox.Height);
                tips.SetToolTip(numBox, "Цифра в имени светофора. Пусто — от названия пути (участок 3П → 3; переименуете путь — " +
                                        "имя поменяется само), а не на пути — порядковая.\nВписали свою — имя собирается по шаблону " +
                                        "из «Обозначений…» (Ч + 5 → Ч5), буква остаётся.");
                var idl = new Label { Text = "id", AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = CHint };
                idl.SetBounds(numBox.Right + S(8), S(3), S(22), S(22));
                var idn = new NumericUpDown { Minimum = 1, Maximum = 999999, Value = Math.Clamp(s.Id, 1, 999999) };
                idn.SetBounds(idl.Right + S(2), S(2), S(70), S(24));
                tips.SetToolTip(idn, "Номер светофора для связи станций: по нему другая программа находит,\n" +
                                     "какому светофору показать присланное показание. Новые светофоры получают следующий номер сами.");
                var row3 = new Panel { Width = w, Height = S(26), Margin = new Padding(0, 0, 0, S(6)), BackColor = row.BackColor };
                var fx = new CheckBox { Text = "имя вручную", Checked = s.IsFixed, AutoSize = true };
                fx.Location = new Point(S(26), S(2));
                tips.SetToolTip(fx, "Имя задано вручную — само не меняется (ни от пути, ни от «Перенумеровать все»).\n" +
                                    "Снимите галочку — имя снова посчитается само (Н3, Ч4, М1…) и будет следить за названием пути.");
                fx.CheckedChanged += (_, _) =>
                {
                    bool v = fx.Checked;
                    Later(() => Edit(() =>
                    {
                        if (SigAt(k, ii) is not Signal c) return;
                        if (v) c.Fixed = true;
                        else Naming.NameNew(st, new SignalEntry { Key = k, Idx = ii, Sig = c, Tile = t });    // [10.11.2026] заново по правилам
                    }));
                };
                var ro = new CheckBox { Text = "только чтение", Checked = s.IsReadOnly, AutoSize = true };
                ro.Location = new Point(S(140), S(2));
                tips.SetToolTip(ro, "Показание этого светофора задаёт другая станция по сети: здесь он только показывает.\n" +
                                    "Маршрутов от него нет; в «Испытании» (без связи) он погашен.");
                idn.ValueChanged += (_, _) =>
                {
                    int v = (int)idn.Value;
                    var other = Core.AllSignals(st).FirstOrDefault(q => q.Sig.Id == v && !(q.Key == k && q.Idx == ii));
                    Edit(() => { if (SigAt(k, ii) is Signal c) c.Id = v; }, false);
                    if (other != null) Msg($"id {v} уже у светофора {other.Sig.Name} — другая станция их не различит", true);
                };
                ro.CheckedChanged += (_, _) =>
                {
                    bool v = ro.Checked;
                    Edit(() => { if (SigAt(k, ii) is Signal c) c.ReadOnly = v ? true : null; }, false);
                };
                foreach (Control fc in new Control[] { numBox, idn, fx, ro })
                    fc.Enter += (_, _) => { ui.SelSig = new SigRef(k, ii); Draw(); };
                row2.Controls.AddRange(new Control[] { nl, numBox, idl, idn });
                row3.Controls.AddRange(new Control[] { fx, ro });
                f.Controls.Add(row2);
                f.Controls.Add(row3);
            }
            if (ui.SelSig is SigRef ss && ss.Key == k && SigAt(k, ss.Idx) is Signal sel)
                Hint(f, w, $"{Core.SigKindName(sel.Kind)}: {Core.SigKindHint(sel.Kind)}");
            var dirsUsed = (t.Sigs ?? new List<Signal>()).Select(q => q.Dir).ToList();
            var addRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
            if (!dirsUsed.Contains("R")) AddButton(addRow, "+ светофор →", () => AddSignal(k, "R", ui.SigKind));
            if (!dirsUsed.Contains("L")) AddButton(addRow, "+ светофор ←", () => AddSignal(k, "L", ui.SigKind));
            if (addRow.Controls.Count > 0) f.Controls.Add(addRow); else addRow.Dispose();
        }
        else H3(f, w, $"Надпись  ·  клетка {k}");

        H4(f, w, "Надпись в клетке");
        var size = new NumericUpDown { Minimum = 8, Maximum = 48, Width = S(70), Value = (decimal)Math.Clamp(tx?.Size ?? 14, 8, 48) };
        Row(f, w, "Текст", CommitBox(tx?.Text, "нет", v =>
        {
            var s = v.Trim();
            Edit(() =>
            {
                if (s.Length > 0) st.Texts[k] = new TextItem { Text = s, Size = (double)size.Value };
                else st.Texts.Remove(k);
            }, s.Length == 0);
        }));
        Row(f, w, "Размер", size).Width = S(80);
        size.ValueChanged += (_, _) => Edit(() => { if (st.Texts.TryGetValue(k, out var x)) x.Size = (double)size.Value; }, false);
        AddButton(f, "Удалить клетку", () => Edit(() => { var (x, y) = Core.Parse(k); EraseAt(x, y); }), danger: true).Margin = new Padding(0, S(12), 0, 0);
    }

    /// <summary>[ДОБАВЛЕНО 10.11.2026] Свойства свободного текста: текст, размер, жирный, положение.</summary>
    void RenderLabelProps(Control f, int w, int i)
    {
        var l = st.Labels[i];
        H3(f, w, "Свободный текст");
        Hint(f, w, "Подпись в любом месте схемы, не по клеткам. Тяните мышью — двигается плавно; стрелки клавиатуры — " +
                   "чуть-чуть (Shift — на клетку); двойной щелчок или Enter — поправить; Delete — удалить.");
        FreeLabel Cur() => ui.SelLabel is int j && j < st.Labels.Count ? st.Labels[j] : null;
        Row(f, w, "Текст", CommitBox(l.Text, "текст", v =>
        {
            var s = v.Trim();
            if (s.Length == 0) { Edit(() => { if (Cur() != null) st.Labels.RemoveAt(i); ui.SelLabel = null; }); return; }
            Edit(() => { if (Cur() is FreeLabel c) c.Text = s; }, false);
        }));
        var size = new NumericUpDown { Minimum = 6, Maximum = 96, Width = S(70), Value = (decimal)Math.Clamp(l.Size, 6, 96) };
        Row(f, w, "Размер", size).Width = S(80);
        size.ValueChanged += (_, _) => Edit(() => { if (Cur() is FreeLabel c) c.Size = (double)size.Value; }, false);
        var bold = new CheckBox { Text = "Жирный", Checked = l.IsBold, AutoSize = true, Margin = new Padding(0, S(4), 0, S(4)) };
        bold.CheckedChanged += (_, _) => Edit(() => { if (Cur() is FreeLabel c) c.Bold = bold.Checked ? null : false; }, false);
        f.Controls.Add(bold);
        Hint(f, w, $"Положение: x = {l.X:0.##}, y = {l.Y:0.##} (в клетках, левый верхний угол текста).");
        AddButton(f, "Удалить текст", () => Edit(() => { if (Cur() != null) st.Labels.RemoveAt(i); ui.SelLabel = null; }), danger: true)
            .Margin = new Padding(0, S(12), 0, 0);
    }

    /// <summary>[ДОБАВЛЕНО 05.11.2026] Свойства объекта: название, размер, у часов — секундная стрелка.</summary>
    void RenderObjectProps(Control f, int w, string k, StationObject o)
    {
        var ot = Core.ObjTypeOf(o.Type);
        H3(f, w, $"{ot.Name}  ·  клетка {k}");
        Hint(f, w, ot.Hint + ". Тяните мышью или стрелками клавиатуры, Delete — удалить.");
        Row(f, w, o.Type == "building" ? "Название" : "Подпись",
            CommitBox(o.Name, o.Type == "building" ? "например Алгабас" : "необязательно",
                v => Edit(() => { if (st.Objects.TryGetValue(k, out var c)) c.Name = v.Trim(); }, false)));
        var nw = new NumericUpDown { Minimum = 1, Maximum = 30, Value = Math.Clamp(o.W, 1, 30), Width = S(70) };
        var nh = new NumericUpDown { Minimum = 1, Maximum = 30, Value = Math.Clamp(o.H, 1, 30), Width = S(70) };
        Row(f, w, "Ширина, клеток", nw).Width = S(80);
        Row(f, w, "Высота, клеток", nh).Width = S(80);
        void Resize()
        {
            int nwv = (int)nw.Value, nhv = (int)nh.Value;
            var (x, y) = Core.Parse(k);
            if (!Core.ObjectFits(st, x, y, nwv, nhv, k)) { Msg("Такой размер налезает на другой объект", true); RefreshPanels(); return; }
            Edit(() => { if (st.Objects.TryGetValue(k, out var c)) { c.W = nwv; c.H = nhv; } }, false);
        }
        nw.ValueChanged += (_, _) => Resize();
        nh.ValueChanged += (_, _) => Resize();
        if (o.Type == "clock")
        {
            var cb = new CheckBox { Text = "Секундная стрелка", Checked = o.Seconds != false, AutoSize = true, Margin = new Padding(0, S(4), 0, S(4)) };
            cb.CheckedChanged += (_, _) => Edit(() => { if (st.Objects.TryGetValue(k, out var c)) c.Seconds = cb.Checked ? null : false; }, false);
            f.Controls.Add(cb);
            Hint(f, w, "Показывает время этого компьютера, стрелки идут сами.");
        }
        if (o.Type == "ammeter")
            Hint(f, w, $"В «Испытании» при переводе стрелки: пусковой бросок до {SwitchDrive.StartAmp:0.0} А, рабочий ток около " +
                       $"{SwitchDrive.WorkAmp:0.0} А в течение ~{SwitchDrive.ThrowTime:0.0} с. Если стрелке мешает препятствие (Shift+ПКМ по стрелке) — " +
                       $"ток фрикции около {SwitchDrive.FrictionAmp:0.0} А, через {SwitchDrive.FrictionCutoff:0} с привод отключается, стрелка без контроля. " +
                       "Значения типовые для привода с двигателем МСП.");
        AddButton(f, "Удалить объект", DeleteSelection, danger: true).Margin = new Padding(0, S(12), 0, 0);
    }

    // =================================================================
    //  ВКЛАДКА «УЧАСТКИ»
    // =================================================================
    void RenderSecs(TabPage host)
    {
        var f = NewFlow(host, out int w);
        var by = Core.TilesBySection(st);
        var names = Core.SectionNames(st);
        int free = st.Tiles.Values.Count(t => string.IsNullOrEmpty(t.Sec));
        H3(f, w, $"Участки ({names.Count})");
        Hint(f, w, "Тип участка нужен для автопоиска маршрутов: поездной маршрут кончается на «пути», " +
                   "отправление — на «перегоне», маневровый — на первом нестрелочном участке. Щелчок по участку — показать на схеме и взять для кисти.");

        var il = new ImageList { ImageSize = new Size(S(14), S(14)), ColorDepth = ColorDepth.Depth32Bit };
        var lv = new ListView
        {
            View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Width = w,
            HeaderStyle = ColumnHeaderStyle.Nonclickable, SmallImageList = il, Margin = new Padding(0, 0, 0, S(6)),
        };
        lv.Disposed += (_, _) => il.Dispose();
        lv.Columns.Add("Участок", (int)(w * 0.38));
        lv.Columns.Add("Тип", (int)(w * 0.36));
        lv.Columns.Add("Кубиков", w - (int)(w * 0.38) - (int)(w * 0.36) - S(22), HorizontalAlignment.Right);
        foreach (var n in names)
        {
            var bmp = new Bitmap(S(14), S(14));
            using (var g = Graphics.FromImage(bmp))
            {
                using var b = new SolidBrush(Core.SecColor(st, n));
                g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
                g.DrawRectangle(Pens.Gray, 0, 0, bmp.Width - 1, bmp.Height - 1);
            }
            il.Images.Add(bmp);
            bool isSw = Core.SecType(st, n) == "switch";
            var cur = st.Sections.TryGetValue(n, out var si) && !string.IsNullOrEmpty(si.Type) ? si.Type : "auto";
            var typeText = isSw ? "стрелочный" : cur == "auto" ? "авто: " + Core.ShortType(Core.SecType(st, n)) : Core.ShortType(cur);
            var it = new ListViewItem(n, il.Images.Count - 1) { Tag = n };
            it.SubItems.Add(typeText);
            it.SubItems.Add((by.TryGetValue(n, out var l) ? l.Count : 0).ToString());
            lv.Items.Add(it);
        }
        int rowH = lv.Items.Count > 0 ? lv.GetItemRect(0).Height : S(18);
        lv.Height = Math.Clamp(rowH * (lv.Items.Count + 1) + S(10), S(80), Math.Max(S(160), host.ClientSize.Height - S(330)));
        f.Controls.Add(lv);

        var editor = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        f.Controls.Add(editor);

        void BuildEditor()
        {
            editor.SuspendLayout();
            ClearControls(editor);
            var n = ui.FlashSec;
            if (n == null || !names.Contains(n))
            {
                Hint(editor, w, "Выберите участок в списке, чтобы поменять его тип, цвет или название.");
            }
            else
            {
                H4(editor, w, "Участок «" + n + "»").Margin = new Padding(0, S(4), 0, S(4));
                if (Core.SecType(st, n) == "switch")
                    Row(editor, w, "Тип", new Label { Text = "стрелочный (на участке есть стрелка)", AutoSize = false, Height = S(22), TextAlign = ContentAlignment.MiddleLeft, ForeColor = CHint });
                else
                {
                    var cur = st.Sections.TryGetValue(n, out var si) && !string.IsNullOrEmpty(si.Type) ? si.Type : "auto";
                    var auto = Core.SecType(st, n);
                    var opts = new[] { "auto", "path", "plain", "line", "dead" }
                        .Select(v => new Opt { V = v, T = v == "auto" ? "авто: " + Core.ShortType(auto) : Core.ShortType(v) });
                    var ct = Row(editor, w, "Тип", Combo(opts, cur));
                    ct.SelectedIndexChanged += (_, _) =>
                    {
                        var v = OptV(ct);
                        Later(() => Edit(() =>
                        {
                            if (!st.Sections.TryGetValue(n, out var s2)) st.Sections[n] = s2 = new SectionInfo();
                            s2.Type = v;
                        }));
                    };
                }
                var btns = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
                AddButton(btns, "Цвет…", () =>
                {
                    using var cd = new ColorDialog { Color = Core.SecColor(st, n), FullOpen = true };
                    if (cd.ShowDialog(this) != DialogResult.OK) return;
                    var hex = Core.ToHex(cd.Color);
                    Edit(() =>
                    {
                        if (!st.Sections.TryGetValue(n, out var s2)) st.Sections[n] = s2 = new SectionInfo { Type = "auto" };
                        s2.Color = hex;
                    });
                });
                AddButton(btns, "Переименовать…", () => RenameSection(n));
                AddButton(btns, "Удалить", () => DeleteSection(n), danger: true);
                editor.Controls.Add(btns);
            }
            var nb = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, S(8), 0, 0) };
            AddButton(nb, "+ Новый участок", () => { if (NewSection().Length > 0) SetTool("brush"); });
            editor.Controls.Add(nb);
            if (free > 0) Hint(editor, w, $"без участка: {free} кубиков");
            editor.ResumeLayout(true);
        }

        bool building = true;
        foreach (ListViewItem it in lv.Items)
            if ((string)it.Tag == ui.FlashSec) { it.Selected = true; it.Focused = true; it.EnsureVisible(); }
        building = false;
        lv.SelectedIndexChanged += (_, _) =>
        {
            if (building) return;
            var n = lv.SelectedItems.Count > 0 ? (string)lv.SelectedItems[0].Tag : null;
            if (n == ui.FlashSec) return;
            ui.FlashSec = n;
            if (n != null) ui.BrushSec = n;
            RefreshToolOpts();
            BuildEditor();
            Draw();
        };
        BuildEditor();
    }

    void RenameSection(string n)
    {
        var nn = (InputBox.Show(this, "Переименовать участок", $"Новое название участка «{n}»:", n) ?? "").Trim();
        if (nn.Length == 0 || nn == n) return;
        Edit(() =>
        {
            foreach (var t in st.Tiles.Values) if (t.Sec == n) t.Sec = nn;
            if (st.Sections.TryGetValue(n, out var old))
            {
                if (st.Sections.TryGetValue(nn, out var ex))
                {
                    // как Object.assign(old, ex): значения уже существующего участка важнее
                    old.Type = ex.Type ?? old.Type;
                    old.Color = ex.Color ?? old.Color;
                }
                st.Sections.Remove(n);
                st.Sections[nn] = old;
            }
            if (ui.BrushSec == n) ui.BrushSec = nn;
            if (ui.FlashSec == n) ui.FlashSec = nn;
        });
        RefreshToolOpts();
    }

    void DeleteSection(string n)
    {
        if (!Confirm($"Удалить участок «{n}»? Его кубики останутся, но без участка.")) return;
        Edit(() =>
        {
            foreach (var t in st.Tiles.Values) if (t.Sec == n) t.Sec = "";
            st.Sections.Remove(n);
            if (ui.BrushSec == n) ui.BrushSec = "";
            if (ui.FlashSec == n) ui.FlashSec = null;
        });
        RefreshToolOpts();
    }

    // =================================================================
    //  ВКЛАДКА «МАРШРУТЫ»
    // =================================================================
    void RenderRoutes(TabPage host)
    {
        var f = NewFlow(host, out int w);
        var all = Routes;
        var T = ui.Test;
        bool test = ui.IsTest;
        var flt = ui.RouteFilter;
        var list = all.Where(r => flt == "all" || (flt == "shunt") == r.Shunt).ToList();
        H3(f, w, $"Маршруты ({all.Count})");

        var seg = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, S(4)) };
        foreach (var (id, text) in new[] { ("all", "Все"), ("train", "Поездные"), ("shunt", "Маневровые") })
        {
            var rb = new RadioButton { Text = text, Appearance = Appearance.Button, AutoSize = true, Checked = flt == id, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, S(2), 0), MinimumSize = new Size(S(70), S(26)) };
            if (flt == id) rb.Font = FH4;
            rb.CheckedChanged += (_, _) => { if (rb.Checked) { ui.RouteFilter = id; RefreshPanels(); } };
            seg.Controls.Add(rb);
        }
        f.Controls.Add(seg);

        if (test)
        {
            Hint(f, w, "Щелчок — задать / отменить маршрут. Заданные выделены жёлтым. Задать можно и на схеме: светофор, затем путь.");
            AddButton(f, "Отменить все маршруты и освободить участки", ResetTest);

            // [ДОБАВЛЕНО 05.11.2026] неисправность стрелок для амперметра
            var groups = st.Tiles.Where(p => p.Value.T == "switch")
                .GroupBy(p => Core.SwitchGroup(p.Key, p.Value))
                .Select(gr => (G: gr.Key, Key: gr.First().Key, Name: SwName(gr.First().Value)))
                .OrderBy(x => x.Name, NaturalComparer.Instance).ToList();
            if (groups.Count > 0)
            {
                H4(f, w, "Препятствие у стрелки (амперметр)");
                Hint(f, w, "Отмеченная стрелка не дойдёт до другого положения: амперметр покажет ток фрикции, стрелка останется без контроля. " +
                           "То же — Shift+ПКМ по стрелке на схеме.");
                var jf = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(w, 0), WrapContents = true, Margin = new Padding(0, 0, 0, S(6)) };
                foreach (var (g, key, name) in groups)
                {
                    var cb = new CheckBox { Text = "стр. " + name, Checked = T.Drive.Jammed.Contains(g), AutoSize = true, Margin = new Padding(0, 0, S(10), S(2)) };
                    cb.CheckedChanged += (_, _) => { if (cb.Checked != T.Drive.Jammed.Contains(g)) Later(() => ToggleJam(key)); };
                    jf.Controls.Add(cb);
                }
                f.Controls.Add(jf);
            }
        }
        else Hint(f, w, "Найдены автоматически по кубикам. Щелчок — показать маршрут на схеме. Задать и проверить — в режиме «Испытание».");

        if (list.Count == 0)
        {
            Hint(f, w, "Маршрутов нет. Нужны светофоры и участки с типом «путь» / «перегон».");
            return;
        }

        var lv = new ListView
        {
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = true, Width = w, ShowGroups = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true,
        };
        lv.Columns.Add("", S(24));
        lv.Columns.Add("Маршрут", (int)(w * 0.62));
        lv.Columns.Add("Стрелки", w - S(24) - (int)(w * 0.62) - S(22));
        ListViewGroup grp = null;
        string lastStart = null;
        foreach (var r in list)
        {
            if (r.StartName != lastStart)
            {
                grp = new ListViewGroup("от " + r.StartName);
                lv.Groups.Add(grp);
                lastStart = r.StartName;
            }
            bool isSet = test && T.Set.Contains(r);
            bool on = !test && ui.Preview?.Id == r.Id;
            var it = new ListViewItem(r.Shunt ? "◇" : "◆", grp) { Tag = r, ToolTipText = string.Join(" → ", r.Sections) };
            it.SubItems.Add(r.Name);
            it.SubItems.Add(r.SwitchesText());
            if (isSet || on) { it.BackColor = Color.FromArgb(0xff, 0xf0, 0x80); it.Font = FH4; }
            lv.Items.Add(it);
        }
        f.Controls.Add(lv);
        f.PerformLayout();
        lv.Height = Math.Max(S(180), host.ClientSize.Height - f.Top - lv.Top - S(10));
        lv.MouseClick += (_, e) =>
        {
            var it = lv.GetItemAt(e.X, e.Y);
            if (it?.Tag is not Route r) return;
            if (ui.IsTest) { if (ui.Test.Set.Contains(r)) CancelRoute(r); else SetRoute(r); }
            else
            {
                ui.Preview = ui.Preview?.Id == r.Id ? null : new RoutePreview { Id = r.Id, Tiles = new HashSet<string>(r.Tiles) };
                if (ui.Preview != null)
                    Msg($"{r.Name}: {string.Join(" → ", r.Sections)}{(r.Switches.Count > 0 ? " · стрелки " + r.SwitchesText(", ") : "")}");
            }
            RefreshPanels();
            Draw();
        };
    }

    // =================================================================
    //  ВКЛАДКА «ПРОВЕРКА»
    // =================================================================
    void RenderChecks(TabPage host)
    {
        var f = NewFlow(host, out int w);
        var probs = Validator.Validate(st, Routes);
        int N(string l) => probs.Count(p => p.Level == l);
        H3(f, w, "Проверка схемы");
        Hint(f, w, $"Ошибок: {N("err")}, предупреждений: {N("warn")}. Щелчок по строке — показать место на схеме.");
        foreach (var p in probs)
        {
            var (ico, fg, bg) = p.Level switch
            {
                "err" => ("✕", CDanger, Color.FromArgb(0xfd, 0xe8, 0xe8)),
                "warn" => ("!", Color.FromArgb(0x8a, 0x56, 0x00), Color.FromArgb(0xff, 0xf4, 0xdc)),
                _ => ("i", Color.FromArgb(0x3c, 0x55, 0x50), Color.FromArgb(0xee, 0xf3, 0xf1)),
            };
            var l = new Label
            {
                Text = ico + "   " + p.Text, AutoSize = true, MaximumSize = new Size(w, 0), MinimumSize = new Size(w, 0),
                ForeColor = fg, BackColor = bg, Padding = new Padding(S(6), S(4), S(6), S(4)), Margin = new Padding(0, 0, 0, S(3)),
            };
            if (p.Key != null)
            {
                var key = p.Key;
                l.Cursor = Cursors.Hand;
                l.Click += (_, _) =>
                {
                    if (!ui.IsTest) { ui.Sel = new HashSet<string> { key }; ui.SelSig = null; }
                    CenterOn(key);
                };
            }
            f.Controls.Add(l);
        }
        if (N("err") == 0 && N("warn") == 0 && probs.Count > 0) Hint(f, w, "Замечаний нет.", CAccent);
    }

    // =================================================================
    //  [ДОБАВЛЕНО 11.11.2026] НУМЕРАЦИЯ ПО ПРАВИЛАМ (Model/Numbering.cs: ВСН 56-78 прил. 2, РУ-30-80 листы 89–90)
    // =================================================================
    void RenderNumberingProps(Control f, int w)
    {
        H4(f, w, "Нумерация (ВСН 56-78)");
        var cbEven = Row(f, w, "Чётные поезда", Combo(new[]
        {
            // [11.11.2026] та же настройка задаёт буквы светофоров (одна на станцию)
            new Opt { V = "R", T = "идут вправо → (светофоры → Ч, ← Н)" }, new Opt { V = "L", T = "идут влево ← (светофоры → Н, ← Ч)" },
        }, st.Even == "L" ? "L" : "R"));
        cbEven.SelectedIndexChanged += (_, _) => { var v = OptV(cbEven); if (v != null && v != st.Even) Edit(() => st.Even = v); };
        var cbMode = Row(f, w, "Боковые пути", Combo(new[]
        {
            new Opt { V = "parity", T = "по чётности (чётные — со стороны чётного главного)" },
            new Opt { V = "row", T = "подряд от вокзала (малая станция)" },
        }, st.Numbering == "row" ? "row" : "parity"));
        cbMode.SelectedIndexChanged += (_, _) => { var v = OptV(cbMode); if (v != null && v != st.Numbering) Edit(() => st.Numbering = v); };
        var cbAuto = new CheckBox { Text = "Номера при рисовании", Checked = st.AutoNumbers, AutoSize = true, MaximumSize = new Size(w, 0), Margin = new Padding(0, S(4), 0, S(4)) };
        cbAuto.CheckedChanged += (_, _) => Edit(() => st.AutoNumbers = cbAuto.Checked, false);
        f.Controls.Add(cbAuto);
        bool evenLeft = Numbering.EvenRight(st);
        Hint(f, w, $"Ось станции: {Numbering.AxisSource(st)}. Стрелки {(evenLeft ? "слева" : "справа")} от оси — чётные (2, 4…), " +
                   $"{(evenLeft ? "справа" : "слева")} — нечётные (1, 3…), от входа к оси. Главные пути: I — нечётный, II — чётный. " +
                   "Ось задаёт объект «Вокзал» (клавиша 9).");
        AddButton(f, "Перенумеровать всё по правилам", RenumberCmd);
    }

    void RenumberCmd()
    {
        if (ui.IsTest) { Msg("Нумерация меняется в режиме «Редактор»", true); return; }
        Numbering.Report rep = null;
        Edit(() => rep = Numbering.RenumberAll(st));
        Msg("Перенумеровано по правилам — " + rep + ". Отменить — Ctrl+Z");
    }
}
