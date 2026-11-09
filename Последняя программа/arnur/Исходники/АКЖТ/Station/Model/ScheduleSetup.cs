using StationApp.Core;

namespace StationApp.Model;

// =====================================================================
//  РАСПИСАНИЕ: пункты программы АКЖТ и откуда подавать поезда  [ДОБАВЛЕНО 22.10.2026, этап 5]
//  Программа АКЖТ ведёт два пункта: станцию АКЖТ и путевой пост (одна модель).
//  Поезда из тоннелей появляются здесь; поезда от Алгабаса передаёт Алгабас,
//  а если его нет на связи — они появляются сами на Б12 (направление перегона ОП — «←»).
// =====================================================================

public static class ScheduleSetup
{
    public static void Register(RailModel m)
    {
        Schedule.AddStation(SchedPoints.Akzht, m.Akzht.View.Routes, m.Akzht.View.TrainPlaces);
        Schedule.AddStation(SchedPoints.Post, m.Post.View.Routes, m.Post.View.TrainPlaces);
        Schedule.AddEntry(SchedPoints.FromTunnelAkzht, m.Akzht.CHU, Dir.Right);
        Schedule.AddEntry(SchedPoints.FromTunnelPost, m.Post.S_TUN1, Dir.Left);
        Schedule.AddBoundary(SchedPoints.Algabas, "algabas", m.Post.S_B12, Dir.Left, m.Post.Line);
        Shunting.AddPnp("Стройкомплект", m.Akzht.P6T);   // [ДОБАВЛЕНО 23.10.2026, этап 7] путь необщего пользования — тупик 6
    }
}
