using StationEditor.Model;

namespace StationEditor.Ui;

/// <summary>[ДОБАВЛЕНО 10.11.2026] Окно «Обозначения…»: как называются светофоры станции.
/// Буква для → (← — противоположная), шаблоны имён по видам, вид нового светофора по типу участка, «Перенумеровать все».
/// Всё хранится в файле станции (rightOdd, naming). Правила — Model\Naming.cs.</summary>
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
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(s(12));

        var tl = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        int row = 0;
        void Head(string text)
        {
            var l = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, row == 0 ? 0 : s(12), 0, s(4)) };
            tl.Controls.Add(l, 0, row);
            tl.SetColumnSpan(l, 2);
            row++;
        }
        void Note(string text)
        {
            var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(s(560), 0), ForeColor = Color.FromArgb(0x4a, 0x5a, 0x56), Margin = new Padding(0, 0, 0, s(4)) };
            tl.Controls.Add(l, 0, row);
            tl.SetColumnSpan(l, 2);
            row++;
        }
        void Line(string label, Control c)
        {
            tl.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, s(4), s(10), s(2)) }, 0, row);
            c.Margin = new Padding(0, s(2), 0, s(2));
            tl.Controls.Add(c, 1, row);
            row++;
        }

        // ---- буква направления ----
        Head("Буква направления");
        var inferred = Naming.InferRightOdd(st);
        bool odd = st.RightOdd ?? inferred ?? false;
        dirBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = s(330) };
        dirBox.Items.Add(new Item { V = "odd", T = "→ нечётное: светофоры Н   (← — Ч)" });
        dirBox.Items.Add(new Item { V = "even", T = "→ чётное: светофоры Ч   (← — Н)" });
        dirBox.SelectedIndex = odd ? 0 : 1;
        Line("Движение →", dirBox);
        Note("Новый светофор берёт букву от ближайшего светофора на схеме (того же направления — ту же, встречного — " +
             "противоположную). Эта настройка — когда светофоров рядом ещё нет и для «Перенумеровать все»." +
             (inferred != null && st.RightOdd == null ? $"\nПо уже названным светофорам схемы: → = {(inferred == true ? "Н" : "Ч")}." : ""));

        // ---- шаблоны ----
        Head("Шаблоны имён");
        Note("{Б} — буква Н / Ч, {П} — номер пути из названия участка (3, 3П → 3; II, IIП → II), {N} — порядковый номер, " +
             "{О} — имя основного светофора впереди. Пусто — по умолчанию.");
        foreach (var (kind, def) in Naming.DefaultTemplates)
        {
            var tb = new TextBox { Width = s(160), PlaceholderText = def };
            if (st.Naming?.Templates != null && st.Naming.Templates.TryGetValue(kind, out var v) && v != def) tb.Text = v;
            tpl[kind] = tb;
            Line(Core.SigKindName(kind), tb);
        }
        Note("По умолчанию цифра — от названия пути, на котором стоит светофор (переименуете путь — имена поменяются сами): " +
             "входной Н / Ч (на пути 1П — Н1; второй — НД), выходной Н3, маршрутный НМ3, маневровый М3 на пути / М1 в горловине " +
             "(нечётные у входного Н, чётные — Ч), проходной 1 / 2 у входного, предупредительный и повторительный ПЧ3, " +
             "заградительный З1, прикрытия НП3. Свою цифру можно вписать у светофора в поле «№».");

        // ---- вид по участку ----
        Head("Вид нового светофора по участку");
        Note("Работает, когда в панели «Светофор» выбран вид «Авто (по участку)».");
        var kinds = new List<Item> { new() { V = "", T = "как выбрано в панели" } };
        kinds.AddRange(Core.SigKinds.Where(k => k.Id != "other").Select(k => new Item { V = k.Id, T = k.Name }));
        foreach (var (sec, name) in Core.SecTypes)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = s(240) };
            var items = new List<Item>(kinds);
            if (sec == "line") items.Insert(1, new Item { V = "auto-line", T = "входной перед станцией, иначе проходной" });
            foreach (var it in items) cb.Items.Add(it);
            var cur = Naming.KindForSec(st, sec);
            cb.SelectedIndex = Math.Max(0, items.FindIndex(i => i.V == cur));
            bySec[sec] = cb;
            Line(name, cb);
        }

        // ---- кнопки ----
        var btns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, s(14), 0, 0) };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "ОК", AutoSize = true };
        var renum = new Button { Text = "Перенумеровать все…", AutoSize = true };
        var reset = new Button { Text = "По умолчанию", AutoSize = true };
        ok.Click += (_, _) => { FixHardLetters(); Collect(); DialogResult = DialogResult.OK; };
        renum.Click += (_, _) =>
        {
            FixHardLetters();
            Collect();
            var draft = StationFile.Clone(st);
            draft.RightOdd = RightOdd ? true : null;
            draft.Naming = Result;
            if (!askRenumber(draft)) return;
            RenumberAll = true;
            DialogResult = DialogResult.OK;
        };
        reset.Click += (_, _) =>
        {
            foreach (var tb in tpl.Values) tb.Text = "";
            foreach (var (sec, cb) in bySec)
            {
                var def = Naming.DefaultKindBySec.FirstOrDefault(p => p.Sec == sec).Kind ?? "";
                for (int i = 0; i < cb.Items.Count; i++) if (((Item)cb.Items[i]).V == def) cb.SelectedIndex = i;
            }
        };
        btns.Controls.AddRange(new Control[] { cancel, ok, renum, reset });
        tl.Controls.Add(btns, 0, row);
        tl.SetColumnSpan(btns, 2);

        Controls.Add(tl);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    int s(int v) => v * DeviceDpi / 96;

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
        Result = res.Templates == null && res.KindBySec == null ? null : res;
    }
}
