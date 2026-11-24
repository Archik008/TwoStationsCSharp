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
                // [ДОБАВЛЕНО 24.11.2026] схема сортировочной горки ("kind": "yard") — окно «Сортировочной» (пульт ДСПГ)
                if (Scheme.YardFromJson.IsYard(path)) { RunYard(path, args); return; }
                ModelHost.Start(path);
                break;
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Не удалось построить станцию по схеме") + $"\n{path}\n\n{L.T(ex.Message)}",
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

    /// <summary>Сортировочная горка из файла: та же модель и то же окно, что в «Сортировочной».</summary>
    static void RunYard(string path, string[] args)
    {
        var geo = Scheme.YardFromJson.Build(path, out var notes);
        Yard.Lay.Use(geo);
        SaveLast(path);
        YardForm.AiEnabled = false;      // ИИ-помощник горки — в самой «Сортировочной»
        var sortDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Сортировочная"));
        if (File.Exists(Path.Combine(sortDir, "ИНСТРУКЦИЯ.txt"))) YardForm.GuideDir = sortDir;
        YardForm.StartNotes.AddRange(notes.Where(n => !n.StartsWith("горка из файла")));
        YardForm.TopButtons.Add(("Схема…", f => { var p = AskFile(f); if (p != null) Reopen(f, p); }));
        // служебное: StationFromJson.exe горка.station.json --shot снимок.png секунд [lang] [--manual]
        int i = Array.IndexOf(args, "--shot");
        if (i >= 0 && args.Length > i + 2)
        {
            if (args.Length > i + 3 && !args[i + 3].StartsWith("--")) L.Set(args[i + 3]);
            using var f = new YardForm();
            f.Shot(args[i + 1], double.Parse(args[i + 2], System.Globalization.CultureInfo.InvariantCulture), args.Contains("--manual"));
            File.WriteAllLines(Path.ChangeExtension(args[i + 1], ".txt"), notes);
            return;
        }
        Application.Run(new YardForm());
    }

    /// <summary>Открыть другую схему новым запуском программы.</summary>
    public static void Reopen(Form owner, string path)
    {
        SaveLast(path);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false };
            psi.ArgumentList.Add(path);
            System.Diagnostics.Process.Start(psi);
            owner.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, L.T("Не удалось открыть схему: ") + ex.Message, owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
