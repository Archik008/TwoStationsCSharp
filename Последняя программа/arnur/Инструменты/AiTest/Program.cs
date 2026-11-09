using StationApp.Ai;
using StationApp.Model;

// =====================================================================
//  [ДОБАВЛЕНО 28.10.2026] Тест ЖИ-помощника (папка Ai программы АКЖТ) без окон.
//  Запуск: dotnet run --project Инструменты\AiTest [all]   (all — напечатать все ответы)
//  Проверяет: вопрос → ответ на языке вопроса, нужная тема (слово в ответе), маршруты по названиям, объекты схемы.
// =====================================================================

int fails = 0;
bool all = args.Contains("all");
Console.OutputEncoding = System.Text.Encoding.UTF8;
ModelHost.Start(false);
AiStation.Install();
var dbg = args.FirstOrDefault(a => a.StartsWith("debug:"));
if (dbg != null)
{
    var kb = new AiKnowledge();
    Console.WriteLine(string.Join(" ", AiText.ConceptsOf(dbg[6..])));
    foreach (var h in kb.Search(dbg[6..], 6)) Console.WriteLine($"{h.Score:0.00} cov {h.Coverage:0.00} [{h.Chunk.Source}] {h.Chunk.Title} :: {string.Join(" ", h.Chunk.TitleConcepts)}");
    return 0;
}
var ai = new AiAssistant();
await ai.StartAsync();
Console.WriteLine($"знаний: {ai.KnowledgeCount}, папка {ai.KnowledgePath}");

async Task Q(string q, params string[] must)
{
    var a = await ai.AskAsync(q);
    var lang = AiText.DetectLang(q);
    bool langOk = AiText.LangOk(a, lang) || a.Length < 60;
    var missing = must.Where(m => a.IndexOf(m, StringComparison.OrdinalIgnoreCase) < 0).ToList();
    bool ok = langOk && missing.Count == 0;
    if (!ok) fails++;
    Console.WriteLine($"{(ok ? "OK    " : "ОШИБКА")} [{lang}] {q}" + (missing.Count > 0 ? "   нет: " + string.Join(", ", missing) : "") + (langOk ? "" : "   НЕ ТОТ ЯЗЫК"));
    if (all || !ok) Console.WriteLine("        → " + a.Replace("\n", "\n          ") + "\n");
}

// маршрут — главная жалоба
await Q("маршрутты қалай жасайды", "светофор", "басыңыз");
await Q("маршрутты қалай беремін?", "басыңыз");
await Q("как задать маршрут", "светофор");
await Q("как сделать маршрут?", "светофор");
await Q("как создать маршрут на станции", "светофор");
await Q("маршрут Н на путь 3", "Н", "3");
await Q("Н-нан 3 жолға маршрутты қалай беремін", "басыңыз");
await Q("маршрут от Ч на путь 1", "Ч");
await Q("маршрутты қалай болдырмаймын", "болдырма");
await Q("как отменить маршрут", "Отменить");
// интерфейс
await Q("стрелканы қалай ауыстырамын", "стрелка");
await Q("как перевести стрелку", "стрелк");
await Q("что делает кнопка Убрать всё", "поезда");
await Q("Параметры деген не", "режим");
await Q("какие режимы интерфейса есть", "Рабочий");
await Q("пригласительный сигнал деген не", "сигнал");
await Q("как сменить направление перегона", "направлен");
await Q("ИР деген не", "ажырату");
await Q("что такое ДУ-46", "журнал");
await Q("как поставить поезд", "поезд");
// объекты схемы
await Q("для чего светофор Н1", "Н1");
await Q("Н1 светофоры не үшін керек", "Н1");
await Q("что за стрелка 5/7", "5/7");
await Q("5/7 стрелкасы қандай", "5/7");
// термины (без интернета — глоссарий)
await Q("автоблокировка деген не", "блок");
await Q("что такое перегон", "станци");
await Q("враждебный маршрут это что", "маршрут");
await Q("стрелка деген не", "жол");
await Q("қызыл от нені білдіреді", "ТОҚТА");

await Q("маршрут деген не", "станция");
await Q("что такое маршрут", "светофор");

// отказ программы → «неге?» по-казахски и по-русски
var il = ModelHost.Model.IL;
var rt = il.Routes.First(r => r.Name == "Н → путь 3");
rt.Sections[1].ManualOcc = true;
il.SetRoute(rt);
await Q("неге берілмейді?", "учаске");
await Q("почему не задаётся?", "занят");
await Q("Н-нан 3 жолға маршрут неге берілмейді", "бос емес");
rt.Sections[1].ManualOcc = false;

// интернет (если есть): термины, которых нет в глоссарии
await Q("тепловоз деген не");
await Q("что такое тепловоз");   // зависит от интернета — без проверки слов

ModelHost.Stop();
ai.Dispose();
Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
return fails;
