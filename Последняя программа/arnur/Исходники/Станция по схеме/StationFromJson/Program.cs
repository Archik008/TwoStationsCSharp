using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

namespace StationApp;

// =====================================================================
//  «СТАНЦИЯ ПО СХЕМЕ» — пульт ДСП любой станции, нарисованной в редакторе станций  [ДОБАВЛЕНО 10.11.2026]
//  Запуск:  StationFromJson.exe "схема.station.json"   (или перетащить файл на exe)
//           без файла — последняя открытая схема, а если её нет — окно выбора (папка «Станции» рядом с программой).
// =====================================================================

static class Program
{
    static string LastFile => Path.Combine(ModelHost.SettingsDir, "последняя_схема.txt");
    public static string StationsDir => Path.Combine(AppContext.BaseDirectory, "Станции");

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try { Directory.CreateDirectory(ModelHost.SettingsDir); } catch { }
        L.Init(Path.Combine(ModelHost.SettingsDir, "lang.txt"));
        LangUi.Install();
        SchemeStyle.Load();

        string path = args.FirstOrDefault(File.Exists) ?? LoadLast() ?? AskFile(null);
        while (true)
        {
            if (path == null) return;                                // выбор схемы отменён
            try
            {
                ModelHost.Start(path);
                break;
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Не удалось построить станцию по схеме") + $"\n{path}\n\n{ex.Message}",
                    L.T("Станция по схеме"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                path = AskFile(null);
            }
        }
        SaveLast(path);
        Ai.AiStation.Install();          // ИИ-помощник читает журнал программы и «показ мышью»
        UiSettings.AiAvailable = true;
        Application.Run(new SchemeStationForm());
        ModelHost.Stop();
    }

    static string LoadLast()
    {
        try
        {
            if (!File.Exists(LastFile)) return null;
            var p = File.ReadAllText(LastFile).Trim();
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    public static void SaveLast(string path)
    {
        try { File.WriteAllText(LastFile, path); } catch { }
    }

    /// <summary>Окно выбора файла схемы. null — отменили.</summary>
    public static string AskFile(IWin32Window owner)
    {
        using var d = new OpenFileDialog
        {
            Title = L.T("Схема станции (файл редактора станций)"),
            Filter = L.T("Схема станции") + " (*.station.json)|*.station.json|JSON (*.json)|*.json|" + L.T("Все файлы") + " (*.*)|*.*",
            InitialDirectory = Directory.Exists(StationsDir) ? StationsDir : AppContext.BaseDirectory,
        };
        return d.ShowDialog(owner) == DialogResult.OK ? d.FileName : null;
    }
}
