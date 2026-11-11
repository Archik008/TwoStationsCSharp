using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using StationEditor.Model;

namespace StationEditor.Ui;

// =====================================================================
//  РЕДАКТОР: окно, история (отмена / возврат), файлы, клавиатура.
//  Мышь и действия над клетками — MainForm.Canvas.cs, испытание — MainForm.Test.cs,
//  панели слева и справа — MainForm.Panels.cs.
//  Схема хранится в поле st (формат — Model/Station.cs), в файл — .station.json (JSON).
// =====================================================================
public sealed partial class MainForm : Form
{
    Station st = Core.NewStation();
    readonly UiState ui = new();

    // ---------------- история ----------------
    readonly List<string> undoStack = new(), redoStack = new();
    string gestureSnap;
    List<Route> routesCache;
    List<Route> Routes => routesCache ??= RouteFinder.FindRoutes(st);

    // ---------------- файл ----------------
    string currentFile;
    bool dirty;
    readonly string startFile;

    static readonly string AutosavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationEditor", "autosave.station.json");

    // ---------------- элементы окна ----------------
    ToolStrip top;
    ToolStripTextBox tbName;
    ToolStripButton bUndo, bRedo, bEdit, bTest, vGrid, vColors, vNames, vGost;
    ToolStripComboBox cbExample;
    Panel left;
    FlowLayoutPanel palettePanel, optsPanel;
    Panel stage;
    Label testBanner;
    SchemeCanvas canvas;
    FlowLayoutPanel zoomBox;
    Label zVal;
    TabControl tabs;
    TabPage tpProps, tpSecs, tpRoutes, tpChecks;
    StatusStrip status;
    ToolStripStatusLabel sCell, sHint, sMsg;
    readonly System.Windows.Forms.Timer msgTimer = new() { Interval = 5000 };
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 300 };
    readonly ToolTip tips = new() { AutoPopDelay = 15000, InitialDelay = 400 };

    int S(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    public MainForm(string fileToOpen)
    {
        startFile = fileToOpen;
        Text = "Редактор станций";
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(S(1440), S(880));
        MinimumSize = new Size(S(960), S(600));
        BuildLayout();

        msgTimer.Tick += (_, _) => { msgTimer.Stop(); sMsg.Text = ""; };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); WriteAutosave(); };
        StartAnim();    // [ДОБАВЛЕНО 05.11.2026] перевод стрелок с амперметром, мигание огней, ход часов
    }

    // =================================================================
    //  РАЗМЕТКА ОКНА
    // =================================================================
    void BuildLayout()
    {
        SuspendLayout();

        // ---- верхняя полоса ----
        top = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(S(6), S(3), S(6), S(3)), RenderMode = ToolStripRenderMode.System };
        top.Items.Add(new ToolStripLabel("Станция:") { ForeColor = Color.FromArgb(0x1f, 0x7a, 0x68), Font = new Font(Font, FontStyle.Bold) });
        tbName = new ToolStripTextBox { AutoSize = false, Width = S(180), ToolTipText = "Название станции" };
        tbName.Leave += (_, _) => CommitName();
        tbName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitName(); canvas.Focus(); } };
        top.Items.Add(tbName);
        top.Items.Add(new ToolStripSeparator());
        Btn("Новая", "Новая пустая схема (Ctrl+N)", NewStationCmd);
        Btn("Открыть…", "Открыть файл .station.json (Ctrl+O)", OpenCmd);
        Btn("Сохранить", "Сохранить в файл .station.json (Ctrl+S)", () => SaveFile(false));
        Btn("Сохранить как…", "Сохранить в другой файл (Ctrl+Shift+S)", () => SaveFile(true));
        cbExample = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = S(150), ToolTipText = "Загрузить готовый пример" };
        cbExample.Items.Add("Пример…");
        foreach (var ex in Examples.All) cbExample.Items.Add(ex.Name);
        cbExample.SelectedIndex = 0;
        cbExample.SelectedIndexChanged += (_, _) =>
        {
            int i = cbExample.SelectedIndex;
            if (i <= 0) return;
            cbExample.SelectedIndex = 0;
            BeginInvoke((Action)(() => { canvas.Focus(); LoadExample(Examples.All[i - 1].Id); }));
        };
        top.Items.Add(cbExample);
        Btn("Экспорт в C#", "Черновик C#-кода схемы для программы станции", ExportCmd);
        Btn("Обозначения…", "Как называются светофоры: буква для →, шаблоны имён по видам, вид по участку; перенумеровать все", NamingCmd);
        Btn("Нумерация", "Перенумеровать пути и стрелки по правилам (ВСН 56-78, РУ-30-80)", RenumberCmd);   // [ДОБАВЛЕНО 11.11.2026]
        top.Items.Add(new ToolStripSeparator());
        bUndo = Btn("↶", "Отменить (Ctrl+Z)", Undo);
        bRedo = Btn("↷", "Вернуть (Ctrl+Y)", Redo);
        bUndo.Font = bRedo.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        top.Items.Add(new ToolStripSeparator());
        bEdit = Btn("Редактор", "Рисовать схему", () => SetMode("edit"));
        bTest = Btn("Испытание", "Проверить схему: маршруты, стрелки, занятость (M)", () => SetMode("test"));
        bEdit.Checked = true;
        top.Items.Add(new ToolStripSeparator());
        vGrid = Toggle("Сетка", "Показать / скрыть сетку", v => ui.ShowGrid = v);
        vColors = Toggle("Цвета", "Показать / скрыть цвета участков", v => ui.ShowSecColors = v);
        vNames = Toggle("Названия", "Показать / скрыть названия участков", v => ui.ShowSecNames = v);
        // [ДОБАВЛЕНО 09.11.2026] огни светофоров: в цвете или графическими знаками ГОСТ 2.749-84 (табл. 6)
        var vGost = new ToolStripButton("Огни по ГОСТ") { CheckOnClick = true, Checked = false, DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Огни светофоров: в цвете / графическими знаками ГОСТ 2.749-84 (G)\n" +
                          "красный — залитый круг, зелёный — пустой, жёлтый — заштрихованный,\n" +
                          "лунно-белый — круг в круге, синий — точка в круге, мигающий — четыре луча" };
        vGost.CheckedChanged += (_, _) => { Renderer.GostLamps = vGost.Checked; Draw(); };
        top.Items.Add(vGost);
        this.vGost = vGost;
        var help = Btn("Справка", "Справка и горячие клавиши (F1)", ShowHelp);
        help.Alignment = ToolStripItemAlignment.Right;

        // ---- строка состояния ----
        status = new StatusStrip { SizingGrip = true };
        sCell = new ToolStripStatusLabel { AutoSize = false, Width = S(250), TextAlign = ContentAlignment.MiddleLeft };
        sMsg = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
        sHint = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(0x4a, 0x5a, 0x56) };
        status.Items.AddRange(new ToolStripItem[] { sCell, sMsg, sHint });

        // ---- слева: кубики и инструменты ----
        left = new Panel { Dock = DockStyle.Left, Width = S(232), AutoScroll = true, BackColor = Color.FromArgb(0xee, 0xf3, 0xf1), Padding = new Padding(S(6)) };
        var leftFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(S(6), S(6)) };
        palettePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        optsPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, S(8), 0, 0) };
        leftFlow.Controls.Add(palettePanel);
        leftFlow.Controls.Add(optsPanel);
        left.Controls.Add(leftFlow);

        // ---- справа: вкладки ----
        var right = new Panel { Dock = DockStyle.Right, Width = S(360), Padding = new Padding(S(4), S(4), S(4), 0) };
        tabs = new TabControl { Dock = DockStyle.Fill };
        tpProps = Page("Свойства", "props");
        tpSecs = Page("Участки", "secs");
        tpRoutes = Page("Маршруты", "routes");
        tpChecks = Page("Проверка", "checks");
        tabs.TabPages.AddRange(new[] { tpProps, tpSecs, tpRoutes, tpChecks });
        tabs.Selected += (_, _) =>
        {
            if (syncingTab) return;
            ui.Tab = (string)tabs.SelectedTab.Tag;
            if (ui.Tab != "routes") ui.Preview = null;
            RefreshPanels();
            Draw();
        };
        tabs.SizeChanged += (_, _) => { if (ui.Tab == "routes" || ui.Tab == "checks" || ui.Tab == "secs") RefreshPanels(); };
        right.Controls.Add(tabs);

        // ---- середина: схема ----
        stage = new Panel { Dock = DockStyle.Fill };
        canvas = new SchemeCanvas { Dock = DockStyle.Fill, GetStation = () => st, Ui = ui };
        testBanner = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = S(30), Visible = false, Padding = new Padding(S(10), 0, S(10), 0),
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(0x23, 0x37, 0x32), ForeColor = Color.White,
            Text = "Испытание.  ЛКМ по светофору, затем по пути — маршрут (ещё раз — отменить).  ЛКМ по стрелке — перевести (смотрите амперметр).  " +
                   "ПКМ по пути — занять / освободить.  Shift+ПКМ по стрелке — препятствие.  ЛКМ по заградительному / прикрытия — включить.",
        };
        stage.Controls.Add(canvas);
        stage.Controls.Add(testBanner);

        zoomBox = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(235, 255, 255, 255), Padding = new Padding(S(3)) };
        Button ZB(string text, string tip, Action a)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(S(30), S(26)),
                TabStop = false, Margin = new Padding(S(2)), UseVisualStyleBackColor = true,
            };
            tips.SetToolTip(b, tip);
            b.Click += (_, _) => { a(); canvas.Focus(); };
            zoomBox.Controls.Add(b);
            return b;
        }
        ZB("−", "Отдалить", () => ZoomAt(canvas.Width / 2f, canvas.Height / 2f, ui.Cam.Z / 1.25f));
        zVal = new Label { Text = "100%", AutoSize = false, Width = S(48), Height = S(26), TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(S(2)) };
        zoomBox.Controls.Add(zVal);
        ZB("+", "Приблизить", () => ZoomAt(canvas.Width / 2f, canvas.Height / 2f, ui.Cam.Z * 1.25f));
        ZB("Вся схема", "Показать всю схему (F)", FitView);
        canvas.Controls.Add(zoomBox);
        canvas.Resize += (_, _) => PlaceZoomBox();
        zoomBox.SizeChanged += (_, _) => PlaceZoomBox();

        WireCanvas();

        // порядок важен: заполняющая панель добавляется первой
        Controls.Add(stage);
        Controls.Add(left);
        Controls.Add(right);
        Controls.Add(top);
        Controls.Add(status);
        ResumeLayout(true);
    }

    void PlaceZoomBox() =>
        zoomBox.Location = new Point(Math.Max(0, canvas.ClientSize.Width - zoomBox.Width - S(12)), Math.Max(0, canvas.ClientSize.Height - zoomBox.Height - S(12)));

    TabPage Page(string text, string tag)
    {
        var p = new TabPage(text) { Tag = tag, AutoScroll = true, BackColor = Color.White, Padding = new Padding(S(4)) };
        return p;
    }

    ToolStripButton Btn(string text, string tip, Action a)
    {
        var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
        b.Click += (_, _) => { CommitFocusedField(); a(); };
        top.Items.Add(b);
        return b;
    }

    ToolStripButton Toggle(string text, string tip, Action<bool> set)
    {
        var b = new ToolStripButton(text) { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tip };
        b.CheckedChanged += (_, _) => { set(b.Checked); Draw(); };
        top.Items.Add(b);
        return b;
    }

    // =================================================================
    //  ЗАПУСК И ЗАКРЫТИЕ
    // =================================================================
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PlaceZoomBox();
        bool opened = false;
        if (!string.IsNullOrEmpty(startFile))
        {
            try
            {
                var sf = StationFile.Load(startFile);
                LoadStation(sf, true);
                currentFile = Path.GetFullPath(startFile);
                opened = true;
                Msg("Открыт файл " + Path.GetFileName(startFile));
            }
            catch (Exception ex) { Msg("Не удалось открыть файл: " + ex.Message, true); }
        }
        if (!opened)
        {
            Station restored = null;
            try { if (File.Exists(AutosavePath)) restored = StationFile.Load(AutosavePath); } catch { restored = null; }
            if (restored != null && restored.Tiles.Count > 0)
            {
                LoadStation(restored, true);
                Msg("Открыта схема с прошлого раза (автосохранение)");
            }
            else
            {
                LoadStation(Examples.Algabas(), true);
                Msg("Для начала открыт пример «Алгабас». Новая схема — кнопка «Новая»");
            }
        }
        dirty = false;
        SetTool("place");
        DoRefreshPanels();
        UpdateTitle();
        canvas.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CommitFocusedField();
        if (ui.IsTest) SetMode("edit");
        if (dirty && st.Tiles.Count > 0 && e.CloseReason == CloseReason.UserClosing)
        {
            var r = MessageBox.Show(this, $"Схема «{st.Name}» изменена. Сохранить в файл?", "Редактор станций",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel || (r == DialogResult.Yes && !SaveFile(false))) { e.Cancel = true; return; }
        }
        saveTimer.Stop();
        anim.Stop();
        WriteAutosave();
        base.OnFormClosing(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (spaceDown) { spaceDown = false; canvas.Cursor = Cursors.Default; }
    }

    // =================================================================
    //  ИСТОРИЯ (отмена / возврат) И АВТОСОХРАНЕНИЕ
    // =================================================================
    void Begin() { gestureSnap ??= StationFile.ToJson(st, false); }

    void End(bool rebuildTab = true)
    {
        if (gestureSnap == null) return;
        var now = StationFile.ToJson(st, false);
        if (now != gestureSnap)
        {
            undoStack.Add(gestureSnap);
            if (undoStack.Count > 200) undoStack.RemoveAt(0);
            redoStack.Clear();
            gestureSnap = null;
            Changed(rebuildTab);
        }
        gestureSnap = null;
    }

    /// <summary>Изменить схему одним шагом отмены. rebuildTab=false — не перестраивать вкладку «Свойства»
    /// (правка пришла из её же поля ввода, значения на ней уже верные).</summary>
    void Edit(Action fn, bool rebuildTab = true)
    {
        Begin();
        try { fn(); }
        finally { End(rebuildTab); }
        Draw();
    }

    void Changed(bool rebuildTab = true)
    {
        // [ДОБАВЛЕНО 10.11.2026] «собранные» имена следят за названием пути, шаблоном и цифрой «№»
        if (Naming.RebuildAll(st) > 0) rebuildTab = true;
        routesCache = null;
        ui.Preview = null;
        dirty = true;
        ScheduleAutosave();
        RefreshPanels(rebuildTab);
        UpdateTitle();
        Draw();
    }

    void ScheduleAutosave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    void WriteAutosave()
    {
        if (ui.IsTest) return;     // положения стрелок в испытании не сохраняем
        try { StationFile.Save(st, AutosavePath); } catch { /* без автосохранения */ }
    }

    void LoadStation(Station n, bool fit)
    {
        if (ui.IsTest) SetMode("edit");
        st = n;
        ui.Sel.Clear();
        ui.SelSig = null;
        ui.SelObj = null;
        ui.SelLabel = null;
        ui.ObjMove = null;
        ui.FlashSec = null;
        ui.Preview = null;
        routesCache = null;
        gestureSnap = null;
        tbName.Text = st.Name;
        RefreshPanels();
        RefreshToolOpts();
        if (fit) FitView(); else Draw();
        ScheduleAutosave();
        UpdateTitle();
    }

    void ReplaceStation(Station n)
    {
        undoStack.Add(StationFile.ToJson(st, false));
        if (undoStack.Count > 200) undoStack.RemoveAt(0);
        redoStack.Clear();
        LoadStation(n, true);
    }

    void Undo()
    {
        if (ui.IsTest || undoStack.Count == 0) return;
        redoStack.Add(StationFile.ToJson(st, false));
        var s = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        LoadStation(StationFile.FromSnapshot(s), false);
        dirty = true;
        UpdateTitle();
        Msg("Отменено");
    }

    void Redo()
    {
        if (ui.IsTest || redoStack.Count == 0) return;
        undoStack.Add(StationFile.ToJson(st, false));
        var s = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        LoadStation(StationFile.FromSnapshot(s), false);
        dirty = true;
        UpdateTitle();
        Msg("Возвращено");
    }

    void UpdateTitle()
    {
        var file = currentFile != null ? " — " + Path.GetFileName(currentFile) : "";
        Text = $"{(dirty ? "• " : "")}{st.Name}{file} — Редактор станций";
    }

    void CommitName()
    {
        var v = tbName.Text.Trim();
        if (v.Length == 0) v = "Без названия";
        if (v != st.Name) Edit(() => st.Name = v);
        if (tbName.Text != st.Name) tbName.Text = st.Name;
    }

    // =================================================================
    //  ФАЙЛЫ
    // =================================================================
    /// <summary>Папка «Станции» рядом с программой — сюда по умолчанию сохраняются схемы.</summary>
    static string StationsDir()
    {
        try
        {
            var d = Path.Combine(AppContext.BaseDirectory, "Станции");
            Directory.CreateDirectory(d);
            return d;
        }
        catch { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
    }

    static string SafeName(string s) =>
        Regex.Replace(string.IsNullOrWhiteSpace(s) ? "станция" : s.Trim(), @"[\\/:*?""<>|]+", "_");

    bool SaveFile(bool saveAs)
    {
        CommitFocusedField();
        if (ui.IsTest) SetMode("edit");     // положения стрелок после испытания в файл не идут
        var path = currentFile;
        if (saveAs || path == null)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Сохранить схему станции", Filter = StationFile.DialogFilter,
                FileName = SafeName(st.Name) + ".station.json",
                InitialDirectory = path != null ? Path.GetDirectoryName(path) : StationsDir(),
                DefaultExt = "json", AddExtension = true, OverwritePrompt = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            path = dlg.FileName;
        }
        try { StationFile.Save(st, path); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить файл:\n" + ex.Message, "Редактор станций", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        currentFile = path;
        dirty = false;
        UpdateTitle();
        Msg("Схема сохранена: " + path);
        return true;
    }

    void OpenCmd()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Открыть схему станции", Filter = StationFile.DialogFilter,
            InitialDirectory = currentFile != null ? Path.GetDirectoryName(currentFile) : StationsDir(),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var n = StationFile.Load(dlg.FileName);
            ReplaceStation(n);
            currentFile = dlg.FileName;
            dirty = false;
            UpdateTitle();
            Msg("Открыт файл " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            Msg("Не удалось открыть файл: " + ex.Message, true);
            MessageBox.Show(this, "Не удалось открыть файл:\n" + ex.Message, "Редактор станций", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void NewStationCmd()
    {
        if (st.Tiles.Count > 0 && !Confirm("Начать новую пустую схему? (Отменить можно кнопкой «↶ Отменить»)")) return;
        ReplaceStation(Core.NewStation());
        currentFile = null;
        dirty = false;
        UpdateTitle();
    }

    void LoadExample(string id)
    {
        var ex = Examples.All.FirstOrDefault(e => e.Id == id);
        if (ex.Build == null) return;
        if (st.Tiles.Count > 0 && !Confirm($"Заменить текущую схему примером «{ex.Name}»? (Отменить можно кнопкой «↶ Отменить»)")) return;
        ReplaceStation(ex.Build());
        currentFile = null;
        dirty = false;
        UpdateTitle();
        Msg($"Загружен пример «{ex.Name}»");
    }

    void ExportCmd()
    {
        var code = CsExport.Export(st, Routes);
        using var f = new ExportForm(code, SafeName(st.Name));
        f.ShowDialog(this);
    }

    void ShowHelp()
    {
        using var f = new HelpForm();
        f.ShowDialog(this);
    }

    /// <summary>[ДОБАВЛЕНО 10.11.2026] Окно «Обозначения…»: буква для →, шаблоны имён, вид по участку, «Перенумеровать все».</summary>
    void NamingCmd()
    {
        CommitFocusedField();
        if (ui.IsTest) SetMode("edit");
        using var f = new NamingForm(st, RenumberPreview);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Edit(() =>
        {
            st.Even = f.RightOdd ? "L" : "R";    // [11.11.2026] одна настройка направления (→ нечётное = чётные идут ←)
            st.Naming = f.Result;
            if (f.RenumberAll) Naming.Apply(st);
        });
        RefreshToolOpts();
        Msg(f.RenumberAll ? "Обозначения сохранены, светофоры перенумерованы" : "Обозначения сохранены — новые светофоры получат имена по ним");
    }

    /// <summary>Спросить «Перенумеровать все?» со списком изменений (по настройкам из окна). true — да.</summary>
    bool RenumberPreview(Station draft)
    {
        var ch = Naming.Preview(draft);
        if (ch.Count == 0)
        {
            MessageBox.Show(this, "Все имена уже такие, как по правилам (или закреплены «имя вручную»).", "Перенумеровать",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        const int max = 25;
        var lines = string.Join("\n", ch.Take(max).Select(c =>
            $"    {(string.IsNullOrEmpty(c.Old) ? "?" : c.Old)}  →  {c.New}   (клетка {c.Key})"));
        if (ch.Count > max) lines += $"\n    …и ещё {ch.Count - max}";
        return MessageBox.Show(this, "Перенумеровать светофоры по правилам? Закреплённые («имя вручную») не меняются.\n\n" + lines +
                                     "\n\nОтменить потом можно кнопкой «↶ Отменить».", "Перенумеровать",
                   MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
    }

    bool Confirm(string text) =>
        MessageBox.Show(this, text, "Редактор станций", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;

    // =================================================================
    //  КЛАВИАТУРА
    // =================================================================
    [DllImport("user32.dll")] static extern IntPtr GetFocus();

    static Control FocusedControl()
    {
        var h = GetFocus();
        return h == IntPtr.Zero ? null : Control.FromChildHandle(h);
    }

    /// <summary>Применить поле ввода, в котором сейчас курсор (как «change» при потере фокуса).</summary>
    void CommitFocusedField()
    {
        var f = FocusedControl();
        if (f is TextBoxBase || f is UpDownBase || f?.Parent is UpDownBase) canvas.Focus();
    }

    bool spaceDown;

    protected override bool ProcessCmdKey(ref Message m, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        bool ctrl = (keyData & Keys.Control) != 0, shift = (keyData & Keys.Shift) != 0, alt = (keyData & Keys.Alt) != 0;
        if (alt) return base.ProcessCmdKey(ref m, keyData);

        var f = FocusedControl();
        bool typing = f is TextBoxBase || f is ComboBox || f is UpDownBase || f?.Parent is UpDownBase;
        bool inList = f is ListView;

        if (ctrl)
        {
            switch (key)
            {
                case Keys.S: SaveFile(shift); return true;
                case Keys.O: CommitFocusedField(); OpenCmd(); return true;
                case Keys.N: CommitFocusedField(); NewStationCmd(); return true;
                case Keys.Z when !typing: if (shift) Redo(); else Undo(); return true;
                case Keys.Y when !typing: Redo(); return true;
                case Keys.A when !typing && !ui.IsTest:
                    ui.Sel = new HashSet<string>(st.Tiles.Keys.Concat(st.Texts.Keys));
                    ui.SelSig = null;
                    SetTool("select");
                    RefreshPanels();
                    return true;
            }
            return base.ProcessCmdKey(ref m, keyData);
        }

        if (typing)
        {
            if (key == Keys.Escape) { canvas.Focus(); return true; }
            return base.ProcessCmdKey(ref m, keyData);
        }

        switch (key)
        {
            case Keys.F1: ShowHelp(); return true;
            case Keys.Space:
                if (!spaceDown) { spaceDown = true; canvas.Cursor = Cursors.SizeAll; }
                return true;
            case Keys.F: FitView(); return true;
            case Keys.M: SetMode(ui.IsTest ? "edit" : "test"); return true;
            case Keys.G: vGost.Checked = !vGost.Checked; return true;    // [ДОБАВЛЕНО 09.11.2026] огни: цвет / ГОСТ
            case Keys.Escape:
                if (ui.IsTest) { ui.Test.Pending = null; Msg("Набор маршрута отменён"); Draw(); return true; }
                ui.Sel.Clear(); ui.SelSig = null; ui.SelObj = null; ui.SelLabel = null; ui.FlashSec = null;
                SetTool("select");
                RefreshPanels();
                return true;
        }
        if (ui.IsTest) return base.ProcessCmdKey(ref m, keyData);

        if ((key == Keys.Delete || key == Keys.Back) && !inList) { DeleteSelection(); return true; }
        if (!inList && ui.SelLabel is int sl && sl < st.Labels.Count)    // [ДОБАВЛЕНО 10.11.2026] свободный текст: чуть-чуть, Shift — на клетку
        {
            double d = shift ? 1 : 0.1;
            switch (key)
            {
                case Keys.Left: MoveLabel(-d, 0); return true;
                case Keys.Right: MoveLabel(d, 0); return true;
                case Keys.Up: MoveLabel(0, -d); return true;
                case Keys.Down: MoveLabel(0, d); return true;
                case Keys.Enter: EditLabel(sl); return true;
            }
        }
        if (!inList && ui.SelObj != null)
        {
            switch (key)
            {
                case Keys.Left: MoveObject(-1, 0); return true;
                case Keys.Right: MoveObject(1, 0); return true;
                case Keys.Up: MoveObject(0, -1); return true;
                case Keys.Down: MoveObject(0, 1); return true;
            }
        }
        if (!inList && ui.Sel.Count > 0)
        {
            switch (key)
            {
                case Keys.Left: MoveSelection(-1, 0); return true;
                case Keys.Right: MoveSelection(1, 0); return true;
                case Keys.Up: MoveSelection(0, -1); return true;
                case Keys.Down: MoveSelection(0, 1); return true;
            }
        }
        switch (key)
        {
            case Keys.V: SetTool("select"); return true;
            case Keys.E: SetTool("erase"); return true;
            case Keys.B: SetTool("brush"); return true;
            case Keys.S: SetTool("signal"); return true;
            case Keys.T: SetTool("text"); return true;
            case Keys.L: SetTool("label"); return true;    // [ДОБАВЛЕНО 10.11.2026] свободный текст
            case Keys.R: RotateSelected(shift ? -1 : 1); return true;
        }
        int digit = key >= Keys.D0 && key <= Keys.D9 ? key - Keys.D0 : key >= Keys.NumPad0 && key <= Keys.NumPad9 ? key - Keys.NumPad0 : -1;
        if (digit >= 0)
        {
            var typ = Core.TypeList.FirstOrDefault(t => t.Key == digit.ToString());
            if (typ != null) { ui.PlaceType = typ.Id; SetTool("place"); return true; }
            // [ДОБАВЛЕНО 05.11.2026] 8 — амперметр, 9 — вокзал, 0 — часы
            var ot = Core.ObjTypes.FirstOrDefault(o => o.Key == digit.ToString());
            if (ot != null) { ui.ObjType = ot.Id; SetTool("object"); return true; }
        }
        return base.ProcessCmdKey(ref m, keyData);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && spaceDown) { spaceDown = false; canvas.Cursor = Cursors.Default; }
        base.OnKeyUp(e);
    }

    // =================================================================
    //  СООБЩЕНИЯ
    // =================================================================
    void Msg(string text, bool bad = false)
    {
        sMsg.Text = text;
        sMsg.ForeColor = bad ? Color.FromArgb(0xc0, 0x10, 0x18) : Color.FromArgb(0x1f, 0x5a, 0x4e);
        msgTimer.Stop();
        msgTimer.Start();
    }

    void Draw()
    {
        canvas?.Invalidate();
        if (zVal != null) zVal.Text = Math.Round(ui.Cam.Z * 100) + "%";
    }
}
