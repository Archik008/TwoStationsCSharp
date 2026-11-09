namespace StationApp;

static class Program
{
    /// <summary>Точка входа приложения.</summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
