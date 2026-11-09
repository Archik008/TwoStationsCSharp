using StationApp.Algabas;
using StationApp.Core;

namespace StationApp.Model;

// =====================================================================
//  ВСЯ МОДЕЛЬ: станция Алгабас + её кусок однопутного перегона до поста.
//  Одно ядро зависимостей (Interlocking — то же, что в программе АКЖТ),
//  виртуальные поезда и карта проводов Arduino Алгабаса.
// =====================================================================

public class RailModel
{
    public readonly Interlocking IL = new();
    public readonly AlgabasLayout Algabas;
    public readonly TrainSim Trains;
    public readonly HardwareMap Hw;

    public RailModel()
    {
        Algabas = new AlgabasLayout(IL);
        Trains = new TrainSim(IL, Algabas.Next);
        Algabas.Trains = Trains;
        Hw = AlgabasHardware.Build();
        IL.ComputeAspects();
    }
}
