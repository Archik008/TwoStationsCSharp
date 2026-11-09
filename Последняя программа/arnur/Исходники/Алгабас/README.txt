ПРОГРАММА СТАНЦИИ АЛГАБАС (C#, .NET 8, WinForms)

Запуск:   открыть Algabas.sln в Visual Studio -> F5.
На флешку: двойной щелчок по build_exe.bat -> папка arnur\готовая_программа -> Algabas.exe.

Порт Arduino выбирается вверху слева. Прошивка - та же StationMain, что сейчас в плате Алгабаса.

Где что лежит:
  Algabas\Algabas\AlgabasLayout.cs    - схема станции: участки, стрелки, светофоры, маршруты, рисунок
  Algabas\Algabas\AlgabasHardware.cs  - провода Arduino: лампы, сервоприводы, датчики (из Python ArduinoCode.py)
  Algabas\Core\                       - ядро зависимостей (копия из программы АКЖТ)
  Algabas\Ui\                         - окно пульта и рисование схемы (копия из программы АКЖТ)
  Algabas\Model\                      - модель и таймер

Подробно, что сделано и почему - CHANGES.md.
