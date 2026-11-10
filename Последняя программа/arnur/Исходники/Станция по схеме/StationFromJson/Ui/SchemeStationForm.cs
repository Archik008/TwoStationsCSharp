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

        AddLog(L.F("Станция «{0}» открыта из файла {1}", lay.Name, Path.GetFileName(ModelHost.Model.FilePath)), LogKind.Info);
        AddLog(L.F("Участков: {0}, стрелок: {1}, светофоров: {2}, маршрутов: поездных {3}, маневровых {4}",
            lay.View.Sections.Count, lay.View.Switches.Count, IL.Signals.Count, lay.TrainRoutes, lay.ShuntRoutes), LogKind.Info);
        foreach (var n in lay.Notes) AddLog(n, LogKind.Aux);
        AddLog("Макет Arduino и связь с соседними станциями для схем из файла пока не подключаются — всё работает на экране.", LogKind.Aux);
    }

    void OpenOther()
    {
        var path = Program.AskFile(this);
        if (path == null) return;
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
