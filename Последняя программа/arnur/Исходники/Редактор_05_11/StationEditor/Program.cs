namespace StationEditor;

static class Program
{
    /// <summary>Запуск: StationEditor.exe [файл.station.json]</summary>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // [ДОБАВЛЕНО 23.11.2026] язык интерфейса (ru / kk / en) — выбор в правом углу верхней полосы, запоминается
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationEditor");
        StationApp.Core.L.Init(Path.Combine(dir, "lang.txt"));
        int li = Array.IndexOf(args, "--lang");
        if (li >= 0 && args.Length > li + 1) { StationApp.Core.L.Set(args[li + 1]); args = args.Where((_, i) => i != li && i != li + 1).ToArray(); }
        StationApp.Ui.LangUi.Install();
        Application.Run(new Ui.MainForm(args.Length > 0 ? args[0] : null));
    }
}
