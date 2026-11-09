using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  ПЕРЕВОД ОКОН  [ДОБАВЛЕНО 22.10.2026, этап 8]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса; программа «Преподаватель» подключает его ссылкой.
//  Перевод — Core/Lang.cs (L.T). Здесь — чтобы окна переводились САМИ, без правки кода окон:
//   • каждое окно при показе (перехват WM_SHOWWINDOW) и раз в секунду обходится: подписи (Text) кнопок,
//     надписей, вкладок, групп, заголовков; пункты меню; заголовки столбцов таблиц и списков; подсказки ToolTip;
//   • код, который сам меняет подпись, не мешает: TextChanged — сразу перевод (до отрисовки), исходная
//     русская строка запоминается и при смене языка переводится заново;
//   • строки в ComboBox / ListBox / ячейках DataGridView не трогаются — переводится только показ (Format,
//     CellFormatting), поэтому SelectedItem и значения в коде остаются русскими;
//   • окна MessageBox — перехват WM_INITDIALOG: текст переводится, окно растягивается под текст;
//   • поля ввода (TextBox, NumericUpDown…) и элементы с Tag = "nolang" не переводятся.
//  Своё рисование (DrawString) переводить явно: L.T(текст).
// =====================================================================

public static class LangUi
{
    class Tr
    {
        public string Orig, Shown;
        public bool Hooked;
    }

    static readonly ConditionalWeakTable<object, Tr> state = new();
    static readonly ConditionalWeakTable<Control, object> listHooked = new();
    static readonly Dictionary<Type, FieldInfo[]> tipFields = new();
    static bool busy, installed;
    static HookProc hookProc;
    static IntPtr hook;

    /// <summary>Включить перевод окон этой программы (один раз, в Program.Main до Application.Run).</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        hookProc = HookCallback;
        hook = SetWindowsHookEx(WH_CALLWNDPROCRET, hookProc, IntPtr.Zero, GetCurrentThreadId());
        var t = new System.Windows.Forms.Timer { Interval = 1000 };
        t.Tick += (_, _) => ApplyAll(false);
        t.Start();
        L.Changed += () => ApplyAll(true);
    }

    static void ApplyAll(bool force)
    {
        foreach (var f in Application.OpenForms.Cast<Form>().ToList())
        {
            Apply(f, force);
            if (force) f.Invalidate(true);
        }
    }

    /// <summary>Перевести окно / элемент со всем содержимым.</summary>
    public static void Apply(Control root, bool force = false)
    {
        if (root == null || root.IsDisposed) return;
        try { Walk(root, force, root); } catch { /* перевод не должен ронять программу */ }
    }

    /// <summary>Выбор языка (названия языков — на самих языках).</summary>
    public static ComboBox Selector()
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Tag = "nolang", Margin = new Padding(6, 6, 6, 0) };
        cb.Items.AddRange(L.Names);
        cb.SelectedIndex = Array.IndexOf(L.Codes, L.Current);
        cb.SelectedIndexChanged += (_, _) => { if (cb.SelectedIndex >= 0) L.Set(L.Codes[cb.SelectedIndex]); };
        void Sync() { int i = Array.IndexOf(L.Codes, L.Current); if (!cb.IsDisposed && cb.SelectedIndex != i) cb.SelectedIndex = i; }
        L.Changed += Sync;
        cb.Disposed += (_, _) => L.Changed -= Sync;
        return cb;
    }

    /// <summary>[ДОБАВЛЕНО 09.11.2026] Поставить подпись (русская строка-ключ), только если она правда поменялась.
    /// Код, который обновляет подписи по таймеру, раньше сравнивал «if (c.Text != t)» — а на казахском и английском
    /// в c.Text лежит перевод, он всегда «не равен» русскому. Тогда каждые 0,1 с подпись ставилась русской и сразу
    /// переводилась обратно: надпись мигала, кнопки меняли ширину, верхняя полоса перестраивалась — и раскрытый
    /// список языков закрывался, не дав выбрать язык. Здесь сравнение — с исходной русской строкой.</summary>
    public static void SetText(Control c, string text)
    {
        if (c == null || c.IsDisposed) return;
        text ??= "";
        if (state.TryGetValue(c, out var st) && st.Orig == text && st.Shown == c.Text) return;
        if (c.Text != text) c.Text = text;
    }

    // ------------------------------------------------------------------
    //  ОБХОД
    // ------------------------------------------------------------------

    static bool Skip(object o) => o is Control c && Equals(c.Tag, "nolang");

    static void Walk(Control c, bool force, Control form)
    {
        if (Skip(c)) return;
        if (c is not (TextBoxBase or UpDownBase or DateTimePicker or ComboBox or ListControl or WebBrowser))
            TrackText(c, force);

        switch (c)
        {
            case ComboBox cb: HookList(cb, force); break;
            case ListBox lb: if (lb.DrawMode == DrawMode.Normal) HookList(lb, force); else if (force) lb.Invalidate(); break;
            case DataGridView g: HookGrid(g, force); break;
            case ListView lv: WalkListView(lv, force); break;
            case ToolStrip ts: WalkItems(ts.Items, force); break;
            case PropertyGrid pg: if (force) pg.Refresh(); break;
        }
        if (c.ContextMenuStrip != null) WalkItems(c.ContextMenuStrip.Items, force);
        Tooltips(c, form, force);

        var st = state.GetValue(c, _ => new Tr());
        if (!st.Hooked)
        {
            st.Hooked = true;
            c.TextChanged += OnTextChanged;
            c.ControlAdded += (_, e) => { if (!busy) Apply(e.Control); };
        }
        foreach (Control ch in c.Controls) Walk(ch, force, form);
    }

    static void TrackText(Control c, bool force)
    {
        var st = state.GetValue(c, _ => new Tr());
        Update(st, c.Text, force, t => c.Text = t);
    }

    /// <summary>Общее для всех подписей: исходный текст → показанный перевод.</summary>
    static void Update(Tr st, string cur, bool force, Action<string> set)
    {
        cur ??= "";
        if (cur != st.Shown) st.Orig = cur;
        else if (!force) return;
        var tr = L.T(st.Orig ?? "");
        st.Shown = tr;
        if (tr != cur)
        {
            busy = true;
            try { set(tr); } finally { busy = false; }
        }
    }

    static void OnTextChanged(object sender, EventArgs e)
    {
        if (busy || sender is not Control c || Skip(c) || c is TextBoxBase or UpDownBase or DateTimePicker or ComboBox or ListControl) return;
        if (state.TryGetValue(c, out var st)) Update(st, c.Text, false, t => c.Text = t);
    }

    static void WalkItems(ToolStripItemCollection items, bool force)
    {
        foreach (ToolStripItem it in items)
        {
            var st = state.GetValue(it, _ => new Tr());
            if (!st.Hooked)
            {
                st.Hooked = true;
                it.TextChanged += (s, _) => { if (!busy && s is ToolStripItem i && state.TryGetValue(i, out var t)) Update(t, i.Text, false, x => i.Text = x); };
            }
            Update(st, it.Text, force, t => it.Text = t);
            if (!string.IsNullOrEmpty(it.ToolTipText))
            {
                var tt = itemTips.GetValue(it, _ => new Tr());
                Update(tt, it.ToolTipText, force, x => it.ToolTipText = x);
            }
            if (it is ToolStripDropDownItem dd)
            {
                WalkItems(dd.DropDownItems, force);
                dd.DropDownOpening -= OnDropDownOpening;
                dd.DropDownOpening += OnDropDownOpening;
            }
        }
    }

    static readonly ConditionalWeakTable<ToolStripItem, Tr> itemTips = new();

    static void OnDropDownOpening(object sender, EventArgs e)
    {
        if (sender is ToolStripDropDownItem dd) WalkItems(dd.DropDownItems, false);
    }

    // ---- списки: перевод только показа ----
    static void HookList(ListControl lc, bool force)
    {
        if (!listHooked.TryGetValue(lc, out _))
        {
            listHooked.Add(lc, true);
            lc.FormattingEnabled = true;
            lc.Format += (_, e) => { if (e.Value is string s) e.Value = L.T(s); else if (e.Value != null) e.Value = L.T(e.Value.ToString()); };
            force = true;
        }
        if (force && !lc.IsDisposed) Refill(lc);
    }

    /// <summary>Перерисовать строки списка с новым переводом (ListBox / ComboBox держат строки в окне Windows).</summary>
    static void Refill(ListControl lc)
    {
        try
        {
            var m = lc.GetType().GetMethod("RefreshItems", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);
            if (m == null) return;
            if (lc is ComboBox cb)
            {
                int sel = cb.SelectedIndex;
                busy = true;
                m.Invoke(cb, null);
                busy = false;
                if (cb.SelectedIndex != sel && sel < cb.Items.Count) cb.SelectedIndex = sel;
            }
            else m.Invoke(lc, null);
        }
        catch { busy = false; }
    }

    // ---- таблицы ----
    static readonly ConditionalWeakTable<DataGridView, object> gridHooked = new();

    static void HookGrid(DataGridView g, bool force)
    {
        if (!gridHooked.TryGetValue(g, out _))
        {
            gridHooked.Add(g, true);
            g.CellFormatting += (_, e) => { if (e.Value is string s && s.Length > 0) e.Value = L.T(s); };
            g.EditingControlShowing += (_, e) => { if (e.Control is ComboBox cb) HookList(cb, true); };
        }
        foreach (DataGridViewColumn col in g.Columns)
        {
            var st = state.GetValue(col, _ => new Tr());
            Update(st, col.HeaderText, force, t => col.HeaderText = t);
            if (col is DataGridViewComboBoxColumn cc && force) g.InvalidateColumn(cc.Index);
        }
        if (force) g.Invalidate();
    }

    static void WalkListView(ListView lv, bool force)
    {
        foreach (ColumnHeader h in lv.Columns)
        {
            var st = state.GetValue(h, _ => new Tr());
            Update(st, h.Text, force, t => h.Text = t);
        }
        foreach (ListViewItem it in lv.Items)
            foreach (ListViewItem.ListViewSubItem si in it.SubItems)
            {
                var st = state.GetValue(si, _ => new Tr());
                Update(st, si.Text, force, t => si.Text = t);
            }
        foreach (ListViewGroup gr in lv.Groups)
        {
            var st = state.GetValue(gr, _ => new Tr());
            Update(st, gr.Header, force, t => gr.Header = t);
        }
    }

    // ---- подсказки: ToolTip из полей окна / элементов ----
    static void Tooltips(Control c, Control form, bool force)
    {
        foreach (var tip in TipsOf(c))
            TipTree(tip, form, force);
    }

    static IEnumerable<ToolTip> TipsOf(Control c)
    {
        var type = c.GetType();
        if (!tipFields.TryGetValue(type, out var fields))
        {
            var list = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(Control); t = t.BaseType)
                list.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                               .Where(f => f.FieldType == typeof(ToolTip)));
            tipFields[type] = fields = list.ToArray();
        }
        foreach (var f in fields)
            if (f.GetValue(c) is ToolTip tip) yield return tip;
    }

    static readonly ConditionalWeakTable<ToolTip, Dictionary<Control, Tr>> tipState = new();

    static void TipTree(ToolTip tip, Control root, bool force)
    {
        var map = tipState.GetValue(tip, _ => new Dictionary<Control, Tr>());
        void Visit(Control c)
        {
            var text = tip.GetToolTip(c);
            if (!string.IsNullOrEmpty(text))
            {
                if (!map.TryGetValue(c, out var st)) map[c] = st = new Tr();
                Update(st, text, force, t => tip.SetToolTip(c, t));
            }
            foreach (Control ch in c.Controls) Visit(ch);
        }
        Visit(root);
    }

    // ------------------------------------------------------------------
    //  ПЕРЕХВАТ ОКОН: показ окна / меню, MessageBox
    // ------------------------------------------------------------------

    const int WH_CALLWNDPROCRET = 12, WM_SHOWWINDOW = 0x0018, WM_INITDIALOG = 0x0110, WM_GETFONT = 0x0031;
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct CWPRETSTRUCT { public IntPtr lResult, lParam, wParam; public uint message; public IntPtr hwnd; }
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowText(IntPtr hWnd, string s);
    delegate bool EnumProc(IntPtr hWnd, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc fn, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hWnd, ref Point p);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawText(IntPtr dc, string s, int n, ref RECT r, uint fmt);

    static IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !busy)
        {
            try
            {
                var m = Marshal.PtrToStructure<CWPRETSTRUCT>(lParam);
                if (m.message == WM_SHOWWINDOW && m.wParam != IntPtr.Zero)
                {
                    var c = Control.FromHandle(m.hwnd);
                    if (c is ToolStripDropDown dd) WalkItems(dd.Items, false);
                    else if (c is Form f) Apply(f);
                }
                else if (m.message == WM_INITDIALOG && L.Current != "ru" && ClassOf(m.hwnd) == "#32770")
                    FixDialog(m.hwnd);
            }
            catch { }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    static string ClassOf(IntPtr h) { var sb = new StringBuilder(64); GetClassName(h, sb, 64); return sb.ToString(); }
    static string TextOf(IntPtr h) { var sb = new StringBuilder(GetWindowTextLength(h) + 2); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

    /// <summary>MessageBox: перевести заголовок, текст и кнопки, растянуть окно под новый текст.</summary>
    static void FixDialog(IntPtr dlg)
    {
        var title = TextOf(dlg);
        if (title.Length > 0) SetWindowText(dlg, L.T(title));
        var kids = new List<IntPtr>();
        EnumChildWindows(dlg, (h, _) => { kids.Add(h); return true; }, IntPtr.Zero);
        IntPtr text = IntPtr.Zero;
        var buttons = new List<IntPtr>();
        foreach (var h in kids)
        {
            string cls = ClassOf(h), t = TextOf(h);
            if (cls == "Button") { buttons.Add(h); if (t.Length > 0) SetWindowText(h, Btn(t)); }
            else if (cls == "Static" && t.Length > 0 && text == IntPtr.Zero) text = h;
        }
        if (text == IntPtr.Zero) return;
        var old = TextOf(text);
        var tr = L.T(old);
        if (tr == old) return;
        GetWindowRect(text, out var r);
        int w = r.Right - r.Left, h0 = r.Bottom - r.Top;
        var (nw, nh) = Measure(text, tr, w);
        SetWindowText(text, tr);
        int dw = Math.Max(0, nw - w), dh = Math.Max(0, nh - h0);
        if (dw == 0 && dh == 0) return;
        var p = new Point(r.Left, r.Top);
        ScreenToClient(dlg, ref p);
        const uint NoZ = 0x0004, NoSize = 0x0001, NoMove = 0x0002;
        SetWindowPos(text, IntPtr.Zero, p.X, p.Y, w + dw, h0 + dh, NoZ);
        foreach (var b in buttons)
        {
            GetWindowRect(b, out var br);
            var bp = new Point(br.Left, br.Top);
            ScreenToClient(dlg, ref bp);
            SetWindowPos(b, IntPtr.Zero, bp.X + dw, bp.Y + dh, 0, 0, NoZ | NoSize);
        }
        GetWindowRect(dlg, out var dr);
        SetWindowPos(dlg, IntPtr.Zero, dr.Left - dw / 2, dr.Top - dh / 2, dr.Right - dr.Left + dw, dr.Bottom - dr.Top + dh, NoZ);
        _ = NoMove;
    }

    static string Btn(string t)
    {
        string plain = t.Replace("&", "");
        string tr = L.T(plain);
        return tr == plain ? t : "&" + tr;
    }

    static (int w, int h) Measure(IntPtr ctl, string s, int width)
    {
        const uint DT_CALCRECT = 0x400, DT_WORDBREAK = 0x10, DT_NOPREFIX = 0x800, DT_EXPANDTABS = 0x40, DT_EDITCONTROL = 0x2000;
        var dc = GetDC(ctl);
        var oldFont = SelectObject(dc, SendMessage(ctl, WM_GETFONT, IntPtr.Zero, IntPtr.Zero));
        try
        {
            int max = Math.Max(width, Screen.PrimaryScreen?.WorkingArea.Width * 2 / 5 ?? 600);
            var one = new RECT { Right = 10000 };
            DrawText(dc, s, -1, ref one, DT_CALCRECT | DT_NOPREFIX | DT_EXPANDTABS);
            int w = Math.Min(max, Math.Max(width, one.Right));
            var r = new RECT { Right = w };
            DrawText(dc, s, -1, ref r, DT_CALCRECT | DT_WORDBREAK | DT_NOPREFIX | DT_EXPANDTABS | DT_EDITCONTROL);
            return (w, r.Bottom);
        }
        finally
        {
            SelectObject(dc, oldFont);
            ReleaseDC(ctl, dc);
        }
    }

    // ------------------------------------------------------------------
    //  СВОЙСТВА В PropertyGrid: подпись, раздел, описание — по-переводу
    // ------------------------------------------------------------------

    /// <summary>Переводить DisplayName / Category / Description свойств типа в PropertyGrid.</summary>
    public static void TranslateProperties(Type type) =>
        TypeDescriptor.AddProvider(new LangProvider(TypeDescriptor.GetProvider(type)), type);

    class LangProvider : TypeDescriptionProvider
    {
        public LangProvider(TypeDescriptionProvider parent) : base(parent) { }
        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance) =>
            new LangDescriptor(base.GetTypeDescriptor(objectType, instance));
    }

    class LangDescriptor : CustomTypeDescriptor
    {
        public LangDescriptor(ICustomTypeDescriptor parent) : base(parent) { }
        public override PropertyDescriptorCollection GetProperties() => Wrap(base.GetProperties());
        public override PropertyDescriptorCollection GetProperties(Attribute[] a) => Wrap(base.GetProperties(a));
        static PropertyDescriptorCollection Wrap(PropertyDescriptorCollection c) =>
            new(c.Cast<PropertyDescriptor>().Select(p => (PropertyDescriptor)new LangProperty(p)).ToArray());
    }

    class LangProperty : PropertyDescriptor
    {
        readonly PropertyDescriptor p;
        public LangProperty(PropertyDescriptor p) : base(p) { this.p = p; }
        public override string DisplayName => L.T(p.DisplayName);
        public override string Category => L.T(p.Category);
        public override string Description => L.T(p.Description);
        public override Type ComponentType => p.ComponentType;
        public override bool IsReadOnly => p.IsReadOnly;
        public override Type PropertyType => p.PropertyType;
        public override TypeConverter Converter => p.Converter;
        public override bool CanResetValue(object c) => p.CanResetValue(c);
        public override object GetValue(object c) => p.GetValue(c);
        public override void ResetValue(object c) => p.ResetValue(c);
        public override void SetValue(object c, object v) => p.SetValue(c, v);
        public override bool ShouldSerializeValue(object c) => p.ShouldSerializeValue(c);
        public override object GetEditor(Type t) => p.GetEditor(t);
    }
}
