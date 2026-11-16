using StationEditor.Model;

namespace StationEditor.Ui;

/// <summary>[ДОБАВЛЕНО 10.11.2026] Окно «Обозначения…»: как называются светофоры станции.
/// Направление (буквы Н / Ч), чётность маневровых, шаблоны имён по видам, вид нового светофора по типу участка,
/// «Перенумеровать все». Всё хранится в файле станции (even, naming). Правила — Model\Naming.cs.
/// [ИЗМЕНЕНО 11.11.2026] окно своего размера, растягивается; разделы — на вкладках с прокруткой, кнопки всегда внизу,
/// у шаблонов — пример имени.</summary>
public sealed class NamingForm : Form
{
    public bool RightOdd { get; private set; }
    public NamingSettings Result { get; private set; }
    public bool RenumberAll { get; private set; }

    readonly Station st;
    readonly Func<Station, bool> askRenumber;
    readonly ComboBox dirBox;
    readonly Dictionary<string, TextBox> tpl = new();
    readonly Dictionary<string, ComboBox> bySec = new();
    readonly ComboBox shuntBox;    // [11.11.2026] чётность номеров маневровых
    readonly List<(Label L, Control Host)> notes = new();
    static readonly Color Grey = Color.FromArgb(0x4a, 0x5a, 0x56);

    sealed class Item
    {
        public string V, T;
        public override string ToString() => T;
    }

    public NamingForm(Station st, Func<Station, bool> askRenumber)
    {
        this.st = st;
        this.askRenumber = askRenumber;
        Text = "Обозначения светофоров — " + st.Name;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(s(700), wa.Width - s(40)), Math.Min(s(540), wa.Height - s(40)));
        MinimumSize = new Size(Math.Min(s(520), wa.Width - s(40)), Math.Min(s(360), wa.Height - s(40)));

        var tabs = new TabControl { Dock = DockStyle.Fill };

        // ---- вкладка 1: буквы и номера ----
        var p1 = Page(tabs, "Буквы и номера");
        Head(p1, "Направление");
        var inferred = Naming.InferRightOdd(st);
        // [ИЗМЕНЕНО 11.11.2026] одна настройка станции — «Чётные поезда идут → / ←» (Station.Even), та же, что в «Нумерации»
        bool odd = st.Even == "L";
        dirBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = s(400) };
        dirBox.Items.Add(new Item { V = "odd", T = "чётные идут ←: светофоры → Н, ← Ч" });
        dirBox.Items.Add(new Item { V = "even", T = "чётные идут →: светофоры → Ч, ← Н" });
        dirBox.SelectedIndex = odd ? 0 : 1;
        Line(p1, "Чётные поезда", dirBox);
        Note(p1, "Та же настройка, что «Чётные поезда» в «Нумерации» — по ней и пути со стрелками. Новый светофор берёт букву " +
                 "от входного своего направления; входных нет — от ближайшего светофора; светофоров нет — отсюда." +
                 (inferred != null && inferred != odd ? $"\nВнимание: по уже названным светофорам → = {(inferred == true ? "Н" : "Ч")} — не совпадает." : ""));
        Head(p1, "Маневровые");
        shuntBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = s(400) };
        shuntBox.Items.Add(new Item { V = "", T = "по направлению светофора (Н — М1, М3…; Ч — М2, М4…)" });
        shuntBox.Items.Add(new Item { V = "neck", T = "по горловине (нечётные у входного Н, чётные — у Ч)" });
        shuntBox.SelectedIndex = st.Naming?.ShuntParity == "neck" ? 1 : 0;
        Line(p1, "Номера", shuntBox);
        Note(p1, "Номер маневрового — порядковый, нечётный или чётный. «По горловине» — как в уроке caredenis.ru.");

        // ---- вкладка 2: шаблоны имён ----
        var p2 = Page(tabs, "Шаблоны имён");
        Note(p2, "{Б} — буква Н / Ч, {П} — номер пути (3П → 3, IIП → II), {N} — порядковый номер, {О} — имя основного впереди. " +
                 "Пусто — по умолчанию (серым). Свою цифру у отдельного светофора — поле «№» в его свойствах.");
        foreach (var (kind, def) in Naming.DefaultTemplates)
        {
            var tb = new TextBox { Width = s(150), PlaceholderText = def };
            if (st.Naming?.Templates != null && st.Naming.Templates.TryGetValue(kind, out var v) && v != def) tb.Text = v;
            tpl[kind] = tb;
            var ex = new Label { AutoSize = true, ForeColor = Grey };
            bool seq = kind is "block" or "barrier" or "shunt";
            void Upd()
            {
                var t = string.IsNullOrWhiteSpace(tb.Text) ? def : tb.Text.Trim();
                ex.Text = $"пример: → {Naming.Render(t, "Н", "3", seq ? "1" : "3", "Ч3")}    ← {Naming.Render(t, "Ч", "3", seq ? "2" : "3", "Н3")}";
            }
            tb.TextChanged += (_, _) => Upd();
            Upd();
            Line(p2, Core.SigKindName(kind), tb, ex);
        }

        // ---- вкладка 3: вид по участку ----
        var p3 = Page(tabs, "Вид по участку");
        Note(p3, "Какой вид получит новый светофор, если в панели «Светофор» выбран вид «Авто (по участку)».");
        var kinds = new List<Item> { new() { V = "", T = "как выбрано в панели" } };
        kinds.AddRange(Core.SigKinds.Where(k => k.Id != "other").Select(k => new Item { V = k.Id, T = k.Name }));
        foreach (var (sec, name) in Core.SecTypes)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = s(320) };
            var items = new List<Item>(kinds);
            if (sec == "line") items.Insert(1, new Item { V = "auto-line", T = "входной перед станцией, иначе проходной" });
            foreach (var it in items) cb.Items.Add(it);
            var cur = Naming.KindForSec(st, sec);
            cb.SelectedIndex = Math.Max(0, items.FindIndex(i => i.V == cur));
            bySec[sec] = cb;
            Line(p3, name, cb);
        }

        // ---- кнопки: всегда внизу ----
        var bar = new Panel { Dock = DockStyle.Bottom, Height = s(46), Padding = new Padding(s(8)) };
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(s(86), 0) };
        var ok = new Button { Text = "ОК", AutoSize = true, MinimumSize = new Size(s(86), 0) };
        var renum = new Button { Text = "Перенумеровать все…", AutoSize = true };
        var reset = new Button { Text = "По умолчанию", AutoSize = true };
        ok.Click += (_, _) => { FixHardLetters(); Collect(); DialogResult = DialogResult.OK; };
        renum.Click += (_, _) =>
        {
            FixHardLetters();
            Collect();
            var draft = StationFile.Clone(st);
            draft.Even = RightOdd ? "L" : "R";    // [11.11.2026] одна настройка направления
            draft.Naming = Result;
            if (!askRenumber(draft)) return;
            RenumberAll = true;
            DialogResult = DialogResult.OK;
        };
        reset.Click += (_, _) =>
        {
            shuntBox.SelectedIndex = 0;
            foreach (var tb in tpl.Values) tb.Text = "";
            foreach (var (sec, cb) in bySec)
            {
                var def = Naming.DefaultKindBySec.FirstOrDefault(p => p.Sec == sec).Kind ?? "";
                for (int i = 0; i < cb.Items.Count; i++) if (((Item)cb.Items[i]).V == def) cb.SelectedIndex = i;
            }
        };
        right.Controls.AddRange(new Control[] { cancel, ok });
        left.Controls.AddRange(new Control[] { renum, reset });
        bar.Controls.Add(right);
        bar.Controls.Add(left);

        Controls.Add(tabs);
        Controls.Add(bar);
        tabs.BringToFront();    // вкладки занимают место над панелью кнопок, а не под ней
        AcceptButton = ok;
        CancelButton = cancel;

        // пояснения переносятся по ширине вкладки (окно можно растягивать)
        Shown += (_, _) => Wrap();
        Resize += (_, _) => Wrap();
    }

    int s(int v) => v * DeviceDpi / 96;

    // ---------------- раскладка вкладок ----------------
    /// <summary>Вкладка: прокручиваемая панель, в ней таблица «подпись | поле | пример».</summary>
    TableLayoutPanel Page(TabControl tabs, string title)
    {
        var tp = new TabPage(title) { Padding = new Padding(s(10)), UseVisualStyleBackColor = true };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Dock = DockStyle.Top };
        for (int i = 0; i < 3; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scroll.Controls.Add(t);
        tp.Controls.Add(scroll);
        tabs.TabPages.Add(tp);
        return t;
    }

    void Head(TableLayoutPanel t, string text)
    {
        var l = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, t.RowCount == 0 ? 0 : s(12), 0, s(4)) };
        t.Controls.Add(l, 0, t.RowCount);
        t.SetColumnSpan(l, 3);
        t.RowCount++;
    }

    void Note(TableLayoutPanel t, string text)
    {
        var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(s(600), 0), ForeColor = Grey, Margin = new Padding(0, 0, 0, s(6)) };
        t.Controls.Add(l, 0, t.RowCount);
        t.SetColumnSpan(l, 3);
        t.RowCount++;
        notes.Add((l, t.Parent));
    }

    void Line(TableLayoutPanel t, string label, Control c, Control extra = null)
    {
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, s(5), s(10), s(3)) }, 0, t.RowCount);
        c.Margin = new Padding(0, s(2), s(10), s(2));
        t.Controls.Add(c, 1, t.RowCount);
        if (extra != null) { extra.Margin = new Padding(0, s(5), 0, s(3)); extra.Anchor = AnchorStyles.Left; t.Controls.Add(extra, 2, t.RowCount); }
        t.RowCount++;
    }

    void Wrap()
    {
        foreach (var (l, host) in notes)
            if (host != null) l.MaximumSize = new Size(Math.Max(s(200), host.ClientSize.Width - s(24)), 0);
    }

    /// <summary>[ДОБАВЛЕНО 10.11.2026] Буква Н / Ч вписана в шаблон жёстко («Ч{П}», «Н») — тогда светофоры этого вида получают
    /// её в обе стороны (выходной → у входного Н станет Ч). Предложить заменить на {Б} — буква от входного / направления.</summary>
    void FixHardLetters()
    {
        var bad = tpl.Where(p => p.Key is "entry" or "exit" or "route" or "cover")
                     .Where(p => p.Value.Text.Trim() is { Length: > 0 } t && (t[0] == 'Н' || t[0] == 'Ч'))
                     .ToList();
        if (bad.Count == 0) return;
        var list = string.Join("\n", bad.Select(p => $"    {Core.SigKindName(p.Key)}: «{p.Value.Text.Trim()}» → «{{Б}}{p.Value.Text.Trim().Substring(1)}»"));
        var r = MessageBox.Show(this,
            "В шаблоне буква Н / Ч вписана жёстко — тогда такие светофоры получат её в обе стороны " +
            "(например, выходной → у входного Н станет Ч).\n\nЗаменить на {Б} — буква будет браться от входного своего направления?\n\n" + list,
            "Обозначения", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r != DialogResult.Yes) return;
        foreach (var (_, tb) in bad) tb.Text = "{Б}" + tb.Text.Trim().Substring(1);
    }

    void Collect()
    {
        RightOdd = ((Item)dirBox.SelectedItem).V == "odd";
        var res = new NamingSettings();
        foreach (var (kind, tb) in tpl)
        {
            var v = tb.Text.Trim();
            if (v.Length == 0 || v == Naming.DefaultTemplate(kind)) continue;
            (res.Templates ??= new())[kind] = v;
        }
        foreach (var (sec, cb) in bySec)
        {
            var v = ((Item)cb.SelectedItem)?.V ?? "";
            var def = Naming.DefaultKindBySec.FirstOrDefault(p => p.Sec == sec).Kind ?? "";
            if (v != def) (res.KindBySec ??= new())[sec] = v;
        }
        var sp = ((Item)shuntBox.SelectedItem)?.V;
        if (!string.IsNullOrEmpty(sp)) res.ShuntParity = sp;
        Result = res.Templates == null && res.KindBySec == null && res.ShuntParity == null ? null : res;
    }
}
