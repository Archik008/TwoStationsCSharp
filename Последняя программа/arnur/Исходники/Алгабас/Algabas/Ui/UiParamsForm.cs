namespace StationApp.Ui;

// =====================================================================
//  ОКНО «ПАРАМЕТРЫ»: РЕЖИМ ИНТЕРФЕЙСА И ПАНЕЛИ  [ДОБАВЛЕНО 28.10.2026]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//  Слева — четыре режима (Ui/UiSettings.cs), справа — галочки панелей выбранного режима.
//  «Применить» — окно станции перестраивается сразу; «По умолчанию» — галочки как задумано для режима.
// =====================================================================

class UiParamsForm : Form
{
    readonly Dictionary<UiMode, RadioButton> modes = new();
    readonly Dictionary<string, CheckBox> boxes = new();
    readonly Label hint;
    UiMode shown;

    public UiParamsForm()
    {
        Text = "Параметры интерфейса";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(860, 624);

        var tips = new ToolTip();
        Controls.Add(new Label { Text = "Режим интерфейса:", AutoSize = true, Location = new Point(14, 12), Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
        int y = 40;
        foreach (var m in Enum.GetValues<UiMode>())
        {
            var mm = m;
            var rb = new RadioButton { Text = UiSettings.ModeName(m), AutoSize = true, Location = new Point(20, y), Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
            rb.CheckedChanged += (_, _) => { if (rb.Checked) ShowMode(mm); };
            tips.SetToolTip(rb, UiSettings.ModeHint(m));
            modes[m] = rb;
            Controls.Add(rb);
            y += 34;
        }
        hint = new Label { Location = new Point(14, y + 8), Size = new Size(300, 230), ForeColor = Color.FromArgb(60, 60, 60) };
        Controls.Add(hint);

        Controls.Add(new Label { Text = "Что показывать в этом режиме:", AutoSize = true, Location = new Point(330, 12), Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
        y = 40;
        foreach (var (id, title, h) in UiSettings.Panels)
        {
            if (id == UiSettings.Ai && !UiSettings.AiAvailable) continue;   // [ДОБАВЛЕНО 28.10.2026] ИИ-помощник есть не во всех программах
            var cb = new CheckBox { Text = title, AutoSize = true, Location = new Point(336, y) };
            tips.SetToolTip(cb, h);
            Controls.Add(cb);
            Controls.Add(new Label { Text = h, AutoSize = false, Location = new Point(356, y + 20), Size = new Size(490, 16), ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f) });
            boxes[id] = cb;
            y += 37;
        }

        var bApply = new Button { Text = "Применить", Size = new Size(120, 34), Location = new Point(476, 576), DialogResult = DialogResult.OK };
        var bReset = new Button { Text = "По умолчанию", Size = new Size(130, 34), Location = new Point(602, 576) };
        var bClose = new Button { Text = "Закрыть", Size = new Size(100, 34), Location = new Point(738, 576), DialogResult = DialogResult.Cancel };
        bReset.Click += (_, _) => { foreach (var (id, cb) in boxes) cb.Checked = UiSettings.DefaultShow(shown, id); };
        tips.SetToolTip(bReset, "Галочки — как задумано для выбранного режима");
        bApply.Click += (_, _) => UiSettings.Apply(shown, boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Checked));
        Controls.AddRange(new Control[] { bApply, bReset, bClose });
        AcceptButton = bApply;
        CancelButton = bClose;

        modes[UiSettings.Mode].Checked = true;
        ShowMode(UiSettings.Mode);
    }

    void ShowMode(UiMode m)
    {
        shown = m;
        hint.Text = UiSettings.ModeHint(m);
        foreach (var (id, cb) in boxes) cb.Checked = UiSettings.Show(m, id);
    }
}
