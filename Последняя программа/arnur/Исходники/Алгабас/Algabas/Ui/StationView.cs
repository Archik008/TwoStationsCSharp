using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  "ВИД СТАНЦИИ" — всё, что окну нужно знать о своей схеме:
//  что рисовать, какие маршруты/стрелки/участки показывать в панелях,
//  какие кнопки имитации добавить. Окно StationForm одно на все станции.
// =====================================================================

public class StationView
{
    public string Title = "";              // заголовок окна
    public string Caption = "";            // большая надпись на схеме
    public SchemeGeometry Geo = new();

    public readonly List<RailRoute> Routes = new();
    public readonly List<RailSwitch> Switches = new();
    public readonly List<TrackSection> Sections = new();
    public readonly List<RailSignal> InviteSignals = new();
    public readonly List<DirectionLine> Lines = new();
    /// <summary>[ДОБАВЛЕНО 21.10.2026, этап 6] Переезды схемы (кнопки «Извещение», «Закрытие», лампа ЗП).</summary>
    public readonly List<LevelCrossing> Crossings = new();

    /// <summary>Кнопки имитации: текст → действие.</summary>
    public readonly List<(string text, Action action)> SpawnButtons = new();

    /// <summary>Галочки имитации: текст, прочитать, записать.</summary>
    public readonly List<(string text, Func<bool> get, Action<bool> set)> Toggles = new();

    /// <summary>Постановка поезда на станционный путь (для АКЖТ): название пути → участок.</summary>
    public readonly List<(string name, TrackSection section)> TrainPlaces = new();
}
