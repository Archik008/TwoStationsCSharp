using StationApp.Ai;
using StationApp.Model;

// =====================================================================
//  [ДОБАВЛЕНО 06.11.2026] Тест ИИ-помощника программы Алгабас без окон (как Инструменты\AiTest у АКЖТ).
//  Запуск: dotnet run --project Инструменты\AiTestAlgabas [all]   (all — напечатать все ответы)
//  Проверяет: ответ на языке вопроса, нужная тема, маршруты по названиям Алгабаса, объекты схемы, отказ.
// =====================================================================

int fails = 0;
bool all = args.Contains("all");
Console.OutputEncoding = System.Text.Encoding.UTF8;
ModelHost.Start();
AiStation.Install();
var ai = new AiAssistant();
await ai.StartAsync();
Console.WriteLine($"знаний: {ai.KnowledgeCount}, папка {ai.KnowledgePath}");
Console.WriteLine("маршруты: " + string.Join("; ", ModelHost.Model.IL.Routes.Take(8).Select(r => r.Name)));

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

await Q("как задать маршрут", "светофор Ч");
await Q("маршрутты қалай беремін?", "басыңыз");
await Q("маршрут Ч на путь 3", "Ч", "3");
await Q("Ч-ден 3 жолға маршрутты қалай беремін", "басыңыз");
await Q("как отменить маршрут", "Отменить");
await Q("как перевести стрелку", "стрелк");
await Q("какие режимы интерфейса есть", "Рабочий");
await Q("как сменить направление перегона", "направлен");
await Q("что такое ДУ-46", "журнал");
await Q("как подключить arduino", "датчики.txt");
await Q("как связаться с соседней станцией", "АКЖТ");
await Q("что ты умеешь", "Алгабас");
await Q("для чего светофор Н1", "Н1", "Алгабас");
await Q("Н1 светофоры не үшін керек", "Н1");
await Q("что за стрелка 4/6", "4/6");
await Q("4/6 стрелкасы қандай", "4/6");
await Q("для чего светофор НА", "путевой пост");
await Q("автоблокировка деген не", "блок");
await Q("что такое перегон", "станци");

var il = ModelHost.Model.IL;
var rt = il.Routes.First(r => r.Name == "Ч → путь 3");
rt.Sections[1].ManualOcc = true;
il.SetRoute(rt);
await Q("неге берілмейді?", "учаске");
await Q("почему не задаётся?", "занят");
await Q("Ч-ден 3 жолға маршрут неге берілмейді", "бос емес");
rt.Sections[1].ManualOcc = false;

Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
ModelHost.Stop();
return fails == 0 ? 0 : 1;
