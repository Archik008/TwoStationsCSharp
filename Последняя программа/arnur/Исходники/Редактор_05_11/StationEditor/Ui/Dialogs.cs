using System.Text;

namespace StationEditor.Ui;

/// <summary>Окно ввода строки (замена prompt() из веб-версии).</summary>
public static class InputBox
{
    /// <returns>Введённый текст или null, если нажали «Отмена».</returns>
    public static string Show(IWin32Window owner, string title, string prompt, string value = "")
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = new Font("Segoe UI", 9.5f), Padding = new Padding(10),
        };
        int s(int v) => v * f.DeviceDpi / 96;
        var tl = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        var lbl = new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(s(380), 0), Margin = new Padding(0, 0, 0, s(6)) };
        var tb = new TextBox { Text = value ?? "", Width = s(380), Margin = new Padding(0, 0, 0, s(10)) };
        var btns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "ОК", DialogResult = DialogResult.OK, AutoSize = true };
        btns.Controls.Add(cancel);
        btns.Controls.Add(ok);
        tl.Controls.Add(lbl);
        tl.Controls.Add(tb);
        tl.Controls.Add(btns);
        f.Controls.Add(tl);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        f.Shown += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
    }
}

/// <summary>Окно «Экспорт в C#».</summary>
public sealed class ExportForm : Form
{
    public ExportForm(string code, string fileBase)
    {
        Text = "Экспорт в C# — черновик схемы";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        int s(int v) => v * DeviceDpi / 96;
        Size = new Size(s(1000), s(700));
        MinimumSize = new Size(s(500), s(300));

        var hint = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = s(48), Padding = new Padding(s(8)),
            Text = "Заготовка в стиле Algabas/AlgabasLayout.cs: участки, стрелки, светофоры, найденные маршруты и геометрия. " +
                   "Топологию Next() и направление перегона пока дописывают вручную.",
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = new Font("Consolas", 9.5f), Text = code, BackColor = Color.White,
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(s(6)) };
        var close = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Сохранить .cs…", AutoSize = true };
        var copy = new Button { Text = "Копировать", AutoSize = true };
        bottom.Controls.AddRange(new Control[] { close, save, copy });
        copy.Click += (_, _) => { Clipboard.SetText(code); copy.Text = "Скопировано ✓"; };
        save.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Сохранить черновик C#", Filter = "Файл C# (*.cs)|*.cs|Все файлы (*.*)|*.*",
                FileName = fileBase + "Layout.cs", DefaultExt = "cs", AddExtension = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dlg.FileName, code, new UTF8Encoding(true)); save.Text = "Сохранено ✓"; }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        Controls.Add(box);
        Controls.Add(hint);
        Controls.Add(bottom);
        CancelButton = close;
    }
}

/// <summary>Окно «Справка». [ИЗМЕНЕНО 17.11.2026] две вкладки: «Инструкция» (по шагам, с предупреждением о маршрутах)
/// и «Справка» (клавиши и подробности, как было).</summary>
public sealed class HelpForm : Form
{
    /// <summary>[ДОБАВЛЕНО 17.11.2026] Главное предупреждение инструкции — вверху, на цветной плашке.</summary>
    public const string RoutesWarning =
        "ВАЖНО: маршруты в редакторе — только для проверки схемы, это не настоящие маршруты станции.\n" +
        "Их находит сам редактор по нарисованным путям, стрелкам, участкам и светофорам, чтобы схему можно было испытать " +
        "(«Испытание»). Таблица маршрутов, зависимости и показания здесь упрощены. Маршрутов сортировочной горки " +
        "(роспуск отцепов, программный и маршрутный режимы ГАЦ, торможение на тормозных позициях) редактор делать не умеет: " +
        "горочный светофор в «Испытании» только переключается вручную. Рабочие маршруты задаются во второй программе — " +
        "«Станция по схеме», кнопка «Настройка…» → «Свои маршруты».";

    /// <summary>[ДОБАВЛЕНО 17.11.2026] Инструкция для пользователя — по шагам.</summary>
    static readonly (string, string)[] Steps =
    {
        ("1. Начало", "«Новая» — пустая схема, «Открыть…» — файл .station.json, «Пример…» — готовая станция. " +
                      "Название станции — поле слева вверху."),
        ("2. Пути", "Выберите кубик в палитре (1 путь, 2 изгиб, 3 стрелка, Y симметричная стрелка, 4 пересечение, 5 тупик, " +
                    "6 выход за край, 7 переезд, K тормозная позиция) и щёлкайте или тяните по сетке. R — повернуть, ПКМ — стереть. " +
                    "Красная точка — конец пути, который никуда не ведёт."),
        ("3. Участки", "Инструмент B (кисть) — раскрасить кубики по участкам. Тип подбирается по названию: 1П — путь, 2-4СП — стрелочный, " +
                       "Б12 / НАУ — перегон, ВТ — тупик; поменять — вкладка «Участки». Без участков нет занятости и маршрутов."),
        ("4. Светофоры", "Инструмент S, вид — в панели слева (входной, выходной, маневровый, проходной, горочный…). " +
                         "Щелчок у правого края клетки — «→», у левого — «←». Имена даются сами по правилам РУ-30-80; " +
                         "«Обозначения…» и «Нумерация» — настроить и перенумеровать."),
        ("5. Проверка", "Вкладка «Проверка»: разрывы путей, светофоры без маршрутов, повторители без основного и т. п. " +
                        "Исправьте замечания до испытания."),
        ("6. Испытание (M)", "Щелчок по светофору начала, затем по светофору конца или по пути назначения — маршрут задан, стрелки " +
                             "переводятся, светофор открывается. Щелчок по стрелке — перевести, ПКМ по пути — занять / освободить. " +
                             "Горочный светофор — щелчок открывает меню оператора горки (стоп, роспуск, осаживание, маневры). " +
                             "Помните: это проверка схемы, а не настоящие маршруты (см. вверху)."),
        ("7. Сохранение", "Ctrl+S — сохранить в файл .station.json (папка «Станции» рядом с программой). " +
                          "Схема ещё и сохраняется сама (автосохранение) и откроется после перезапуска."),
        ("8. Вторая программа", "«Экспорт JSON» — вся станция или только выделенные клетки. Файл открывается в «Станции по схеме» " +
                                "кнопкой «Схема…». Там — пульт ДСП, поезда и окно «Настройка…»: свои маршруты (клик n1 → n2, " +
                                "какие участки жёлтым, какие стрелки, какие показания), имена светофоров, времена."),
        ("9. Сортировочная горка", "Можно нарисовать: тормозные позиции ТП1–ТП3 (K), симметричные стрелки (Y), перекрёстный съезд, " +
                                   "горочный светофор Г и его повторитель ПГ (указатель «Н»). Нельзя: маршруты роспуска и логику горки — " +
                                   "редактор показывает только вид; маршруты на горку и к Г задавайте сами в «Станции по схеме»."),
    };

    static readonly (string, string)[] Rows =
    {
        ("Кубики", "1 путь, 2 изгиб, 3 стрелка, 4 пересечение, 5 тупик, 6 выход за край схемы, 7 переезд. R / Shift+R — повернуть. " +
                   "Объекты: 8 амперметр, 9 вокзал, 0 часовая башня."),
        ("Инструменты", "V выбор, E ластик, B участок (кисть), S светофор, T надпись в клетке, " +
                        "L свободный текст — в любом месте, тянется мышью не по клеткам (двойной щелчок — поправить, стрелки — подвинуть)."),
        ("Мышь", "ЛКМ — поставить / выбрать, протягивание — рисовать линией. ПКМ — стереть. Колесо — масштаб, средняя кнопка или Пробел+мышь — сдвинуть схему."),
        ("Выделение", "Рамкой инструментом «Выбор», Shift — добавить. Тянуть мышью или стрелками — перенести. Delete — удалить, Ctrl+A — всё."),
        ("Файл", "Ctrl+S — сохранить в файл .station.json, Ctrl+Shift+S — сохранить как, Ctrl+O — открыть, Ctrl+N — новая схема. " +
                 "По умолчанию файлы кладутся в папку «Станции» рядом с программой. Схема ещё и сама сохраняется (автосохранение) — после перезапуска откроется снова."),
        ("Отмена", "Ctrl+Z — отменить, Ctrl+Y — вернуть."),
        ("Вид", "F — вся схема, M — редактор / испытание, G — огни в цвете / знаками ГОСТ 2.749-84 " +
                "(красный — залитый круг, зелёный — пустой, жёлтый — заштрихованный, лунно-белый — круг в круге, " +
                "синий — точка в круге, мигающий — четыре луча; негорящие лампы — бледные), F1 — эта справка."),
        ("Как стыкуются", "У клетки 6 выходов: середины левой и правой стороны и 4 угла. Соседние кубики стыкуются, если их выходы смотрят друг на друга. " +
                          "Красная точка — выход, который никуда не ведёт."),
        ("Участки", "Стык рисуется сам между кубиками разных участков. Тип участка (путь, перегон, тупик…) подбирается по названию: " +
                    "1П — путь, 2-4СП — стрелочный, НАУ / Б12 — перегон, ВТ — тупик. Поменять — вкладка «Участки»."),
        ("Светофоры", "«→» (Ч, чётные) — под путём на правом краю клетки, «←» (Н, нечётные) — над путём на левом. " +
                      "Входные и выходные дают поездные маршруты, маневровые и выходные — маневровые."),
        ("Где рисуются светофоры (23.11)", "Ряд огней у всех светофоров — на одном расстоянии от пути. Если огни легли бы на " +
                      "другой путь, стрелку, изгиб, соседний светофор или надпись, редактор отодвигает светофор чуть дальше от пути " +
                      "(сторону и направление не меняет). Места нет и там — светофор рисуется полупрозрачным (путь под ним видно), " +
                      "в оранжевой рамке с «!»; в «Свойствах» и на вкладке «Проверка» написано, что он закрывает и что сделать: " +
                      "переставить на клетку дальше от стрелки или раздвинуть пути. Подписи светофоров, номера стрелок и названия участков тоже ищут свободное место. " +
                      "Кнопка «Группировка всех огней» — всем светофорам головки по Инструкции РК (отменить — Ctrl+Z)."),
        ("Испытание", "ЛКМ по светофору, потом по пути — задать маршрут (стрелки переводятся приводом по очереди, светофор откроется, " +
                      "когда они дойдут). ЛКМ по стрелке — перевести. ПКМ по пути — занять / освободить. " +
                      "Пересекающиеся маршруты и занятые участки не дают задать маршрут."),
        ("Новые светофоры (05.11)", "Маршрутный (НМ, ЧМ) — как выходной, внутри станции. Предупредительный (две черты на мачте) — " +
                      "зелёный / жёлтый / жёлтый мигающий по основному светофору впереди; перед заградительным — жёлтый, когда тот красный. " +
                      "Повторительный (одна черта) — зелёный, когда выходной впереди открыт, иначе не горит. " +
                      "Заградительный (мачта в чёрно-белые полосы) — нормально не горит, красный при занятом переезде впереди или по щелчку. " +
                      "Прикрытия — красный, зелёный по щелчку, если впереди свободно. Проходной — красный / жёлтый / жёлтый мигающий / зелёный сам."),
        ("Амперметр (8)", "Прибор табло: ток перевода стрелок. Пуск — бросок тока, затем рабочий ток ~1,8 А около 3 с. " +
                      "Shift+ПКМ по стрелке (или галочка на вкладке «Маршруты») — препятствие: ток фрикции ~2,4 А, через 7 с привод " +
                      "отключается, стрелка без контроля (мигает красная метка). Щелчок по ней — вернуть в прежнее положение."),
        ("Вокзал (9), часы (0)", "Вокзал — пассажирское здание со своим названием. Часовая башня показывает время компьютера. " +
                      "Объекты выбираются и перетаскиваются инструментом «Выбор», название и размер — во «Свойствах»."),
        ("Формат файла", "JSON, тот же, что у веб-макета (index.html): файлы открываются в обеих версиях редактора."),
    };

    /// <param name="instructions">true — открыть на вкладке «Инструкция», false — на «Справке».</param>
    public HelpForm(bool instructions = false) : this(instructions ? 0 : 1) { }

    /// <param name="page">[ДОБАВЛЕНО 23.11.2026] вкладка: 0 — «Инструкция», 1 — «Справка», 2 — «О программе», 3 — «О разработчиках».</param>
    public HelpForm(int page)
    {
        Text = "Инструкция, справка, о программе — Редактор станций";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        int s(int v) => v * DeviceDpi / 96;
        Size = new Size(s(780), s(660));
        var bold = new Font(Font, FontStyle.Bold);

        RichTextBox Body((string, string)[] rows)
        {
            var rtb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = false };
            foreach (var (h, t) in rows)
            {
                rtb.SelectionFont = bold;
                rtb.SelectionColor = Color.FromArgb(0x1f, 0x7a, 0x68);
                rtb.AppendText(StationApp.Core.L.T(h) + "\n");   // [ИЗМЕНЕНО 23.11.2026] перевод: RichTextBox сам не переводится
                rtb.SelectionFont = Font;
                rtb.SelectionColor = Color.FromArgb(0x23, 0x37, 0x32);
                rtb.AppendText(StationApp.Core.L.T(t) + "\n\n");
            }
            rtb.Select(0, 0);
            return rtb;
        }
        Panel Pad(TabPage pg)
        {
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(s(14)), BackColor = Color.White };
            pg.Controls.Add(pad);
            return pad;
        }

        var tabs = new TabControl { Dock = DockStyle.Fill };
        // [ДОБАВЛЕНО 17.11.2026] «Инструкция»: предупреждение о маршрутах — оранжевая плашка, ниже шаги
        var pGuide = new TabPage("Инструкция") { BackColor = Color.White };
        var padG = Pad(pGuide);
        padG.Controls.Add(Body(Steps));
        padG.Controls.Add(new Panel { Dock = DockStyle.Top, Height = s(10), BackColor = Color.White });
        padG.Controls.Add(new Label
        {
            Text = RoutesWarning, Dock = DockStyle.Top, AutoSize = false, Height = s(150), Padding = new Padding(s(10)),
            BackColor = Color.FromArgb(0xff, 0xf1, 0xd6), ForeColor = Color.FromArgb(0x7a, 0x3e, 0x00),
            Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), BorderStyle = BorderStyle.FixedSingle,
        });
        tabs.TabPages.Add(pGuide);
        var pRef = new TabPage("Справка: клавиши и подробности") { BackColor = Color.White };
        Pad(pRef).Controls.Add(Body(Rows));
        tabs.TabPages.Add(pRef);
        // [ДОБАВЛЕНО 23.11.2026] «О программе» и «О разработчиках» (тексты — Model\AboutInfo.cs, общие со «Станцией по схеме»)
        var ver = typeof(HelpForm).Assembly.GetName().Version;
        var pAbout = new TabPage("О программе") { BackColor = Color.White };
        Pad(pAbout).Controls.Add(Body(StationEditor.Model.AboutInfo.Editor(ver == null ? "?" : $"{ver.Major}.{ver.Minor}.{ver.Build}")));
        tabs.TabPages.Add(pAbout);
        var pDev = new TabPage("О разработчиках") { BackColor = Color.White };
        Pad(pDev).Controls.Add(Body(StationEditor.Model.AboutInfo.Developers));
        tabs.TabPages.Add(pDev);
        tabs.SelectedIndex = Math.Clamp(page, 0, tabs.TabPages.Count - 1);

        var close = new Button { Text = "Закрыть", Dock = DockStyle.Bottom, Height = s(34), DialogResult = DialogResult.Cancel };
        Controls.Add(tabs);
        Controls.Add(close);
        CancelButton = close;
    }
}
