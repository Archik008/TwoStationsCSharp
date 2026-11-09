using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

namespace StationApp.Algabas;

// =====================================================================
//  ГЛАВНОЕ ОКНО — СТАНЦИЯ АЛГАБАС
//  Это общее окно пульта (Ui/StationForm.cs, то же, что у АКЖТ и поста)
//  с видом станции Алгабас. Закрытие окна завершает программу.
// =====================================================================

class AlgabasForm : StationForm
{
    public AlgabasForm() : base(ModelHost.Model.Algabas.View)
    {
        IsMainWindow = true;
        AddLog("Пульт станции Алгабас запущен. Перегон к путевому посту — однопутный, направление «→» (к Алгабасу).", LogKind.Info);
        // [ИЗМЕНЕНО 12.10.2026] связь с программой АКЖТ — через сервер PromServer (кнопка «Связь…»)
        AddLog("Связь с АКЖТ — кнопка «Связь…» вверху: светофоры НА и 1 поста приходят от АКЖТ, Ч и 2 уходят к АКЖТ. " +
               "Без связи НА и 1 погашены; поезд от поста — кнопкой «Поезд от поста».", LogKind.Aux);   // [ИЗМЕНЕНО 28.10.2026]
        // [ДОБАВЛЕНО 12.10.2026] запрос АКЖТ на смену направления — окном «Дать согласие / Отказать»
        ModelHost.Dispatch.AskConsent = r => DirectionConsentDialog.Ask(this, r);
        DispatchLink.StartFromSettings();
    }
}
