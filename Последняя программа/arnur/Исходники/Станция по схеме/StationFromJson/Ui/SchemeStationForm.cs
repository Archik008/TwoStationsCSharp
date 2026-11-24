using System.Diagnostics;
using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  ГЛАВНОЕ ОКНО «СТАНЦИИ ПО СХЕМЕ»  [ДОБАВЛЕНО 10.11.2026]
//  Это общее окно пульта Алгабаса (Ui/StationForm.cs: схема, режимы, пульт ДСП, журналы, ИИ, языки)
//  с видом станции из файла. Своё — только кнопка «Схема…»: открыть другую станцию (.station.json).
// =====================================================================

class SchemeStationForm : StationForm
{
    public SchemeStationForm() : base(ModelHost.Model.Layout.View)
    {
        IsMainWindow = true;
        var lay = ModelHost.Model.Layout;
        var b = AddTopButton("Схема…", OpenOther);
        b.BackColor = Color.FromArgb(60, 110, 160);
        new ToolTip().SetToolTip(b, "Открыть другую станцию: файл .station.json из редактора станций");
        // [ДОБАВЛЕНО 13.11.2026] свои маршруты (n1 → n2), автомаршруты, светофоры, стрелки, времена — файл рядом со схемой
        var bs = AddTopButton("Настройка…", OpenSettings);
        bs.BackColor = Color.FromArgb(60, 110, 160);
        new ToolTip().SetToolTip(bs, "Свои маршруты (клик n1 → n2: какие участки жёлтым, какие стрелки, какие показания), " +
                                     "автомаршруты вкл / выкл, имена и виды светофоров, стрелки, времена");
        // [ДОБАВЛЕНО 23.11.2026] «О программе» и «О разработчиках» — тексты общие с редактором станций (Model\AboutInfo.cs)
        var ba = AddTopButton("О программе", ShowAbout);
        ba.BackColor = Color.FromArgb(60, 110, 160);
        new ToolTip().SetToolTip(ba, "О программе и о разработчиках");

        AddLog(L.F("Станция «{0}» открыта из файла {1}", lay.Name, Path.GetFileName(ModelHost.Model.FilePath)), LogKind.Info);
        AddLog(L.F("Участков: {0}, стрелок: {1}, светофоров: {2}, маршрутов: поездных {3}, маневровых {4}",
            lay.View.Sections.Count, lay.View.Switches.Count, IL.Signals.Count, lay.TrainRoutes, lay.ShuntRoutes), LogKind.Info);
        foreach (var n in lay.Notes) AddLog(n, LogKind.Aux);
        AddLog("Макет Arduino и связь с соседними станциями для схем из файла пока не подключаются — всё работает на экране.", LogKind.Aux);
    }

    SchemeSettingsForm settings;

    /// <summary>Версия «Станции по схеме» — как в журнале изменений CHANGES.md.</summary>
    const string Version = "2.10";

    /// <summary>[ДОБАВЛЕНО 23.11.2026] Окно с вкладками «О программе» и «О разработчиках».</summary>
    void ShowAbout()
    {
        int s(int v) => v * DeviceDpi / 96;
        using var f = new Form
        {
            Text = "О программе — Станция по схеме", Font = new Font("Segoe UI", 10f), StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, Size = new Size(s(640), s(480)), MinimizeBox = false, MaximizeBox = false,
        };
        var bold = new Font(f.Font, FontStyle.Bold);
        RichTextBox Body((string, string)[] rows)
        {
            var rtb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = false };
            foreach (var (h, t) in rows)
            {
                rtb.SelectionFont = bold;
                rtb.SelectionColor = Color.FromArgb(40, 80, 130);
                rtb.AppendText(h + "\n");
                rtb.SelectionFont = f.Font;
                rtb.SelectionColor = Color.FromArgb(30, 40, 50);
                rtb.AppendText(t + "\n\n");
            }
            rtb.Select(0, 0);
            return rtb;
        }
        TabPage Page(string title, (string, string)[] rows)
        {
            var pg = new TabPage(title) { BackColor = Color.White };
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(s(14)), BackColor = Color.White };
            pad.Controls.Add(Body(rows));
            pg.Controls.Add(pad);
            return pg;
        }
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("О программе", StationEditor.Model.AboutInfo.Scheme(Version)));
        tabs.TabPages.Add(Page("О разработчиках", StationEditor.Model.AboutInfo.Developers));
        var close = new Button { Text = "Закрыть", Dock = DockStyle.Bottom, Height = s(34), DialogResult = DialogResult.Cancel };
        f.Controls.Add(tabs);
        f.Controls.Add(close);
        f.CancelButton = close;
        f.ShowDialog(this);
    }

    void OpenSettings()
    {
        if (settings is { IsDisposed: false }) { settings.Activate(); return; }
        settings = new SchemeSettingsForm(this, Restart);
        settings.Show(this);
    }

    void OpenOther()
    {
        var path = Program.AskFile(this);
        if (path == null) return;
        Restart(path);
    }

    /// <summary>Открыть схему заново (другую или эту же после настройки) — новым запуском программы.</summary>
    void Restart(string path)
    {
        Program.SaveLast(path);
        try
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("Не удалось открыть схему: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
