using StationApp.Core;

namespace StationApp.Model;

// =====================================================================
//  РАСПИСАНИЕ: пункт программы Алгабас и откуда подавать поезда  [ДОБАВЛЕНО 22.10.2026, этап 5]
//  Поезда от поста передаёт программа АКЖТ, а если её нет на связи — они появляются
//  сами на НАУ (направление перегона ОП — «→»). Свои поезда — формирование и оборот на путях.
// =====================================================================

public static class ScheduleSetup
{
    public static void Register(RailModel m)
    {
        Schedule.AddStation(SchedPoints.Algabas, m.Algabas.View.Routes, m.Algabas.View.TrainPlaces);
        Schedule.AddBoundary(SchedPoints.Post, "akzht", m.Algabas.NAU, Dir.Right, m.Algabas.Line);
        Shunting.AddPnp("ТЭЦ", m.Algabas.M6T);   // [ДОБАВЛЕНО 23.10.2026, этап 7] путь необщего пользования — тупик М6Т; [ИЗМЕНЕНО 28.10.2026] было «Нефтебаза»
    }
}
