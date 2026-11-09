namespace StationEditor;

static class Program
{
    /// <summary>Запуск: StationEditor.exe [файл.station.json]</summary>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new Ui.MainForm(args.Length > 0 ? args[0] : null));
    }
}
