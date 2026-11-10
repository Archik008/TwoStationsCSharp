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

/// <summary>Окно «Справка».</summary>
public sealed class HelpForm : Form
{
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

    public HelpForm()
    {
        Text = "Справка — Редактор станций";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        int s(int v) => v * DeviceDpi / 96;
        Size = new Size(s(760), s(620));
        var rtb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = false };
        var bold = new Font(Font, FontStyle.Bold);
        foreach (var (h, t) in Rows)
        {
            rtb.SelectionFont = bold;
            rtb.SelectionColor = Color.FromArgb(0x1f, 0x7a, 0x68);
            rtb.AppendText(h + "\n");
            rtb.SelectionFont = Font;
            rtb.SelectionColor = Color.FromArgb(0x23, 0x37, 0x32);
            rtb.AppendText(t + "\n\n");
        }
        rtb.Select(0, 0);
        var close = new Button { Text = "Закрыть", Dock = DockStyle.Bottom, Height = s(34), DialogResult = DialogResult.Cancel };
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(s(14)), BackColor = Color.White };
        pad.Controls.Add(rtb);
        Controls.Add(pad);
        Controls.Add(close);
        CancelButton = close;
    }
}
