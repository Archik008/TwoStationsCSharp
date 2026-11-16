namespace StationApp.Ai;

// =====================================================================
//  ИИ-ПОМОЩНИК: ЧТО СВОЁ У ПРОГРАММЫ «СОРТИРОВОЧНАЯ»  [ДОБАВЛЕНО 12.11.2026]
//  Общие файлы помощника (окно, поиск по базе знаний, Википедия, нейросеть) — из Алгабаса по ссылкам.
// =====================================================================

public static class AiProfile
{
    public const string Station = "Сортировочная";
    public static string KnowledgeFolder => Path.Combine(AppContext.BaseDirectory, "Knowledge");

    public const string RouteExampleRu = "маршрут отцепу на путь 5";
    public const string RouteExampleKk = "отцепке 5 жолға маршрут";
    public const string HelpExampleRu = "кнопка пути 5 перед отцепом.";
    public const string HelpExampleKk = "отцеп алдында 5-жол батырмасы.";

    public static string StationOf(string id, AiLang l) => Station;

    public const string HelpLines = "";
}
