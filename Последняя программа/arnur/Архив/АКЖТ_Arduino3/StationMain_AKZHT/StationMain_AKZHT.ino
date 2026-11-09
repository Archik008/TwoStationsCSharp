// =====================================================================
//  [УСТАРЕЛО 06.10.2026] НЕ ЗАЛИВАТЬ В АКЖТ!
//  Этот скетч был сделан из StationMain другой станции: светофоры на пинах 2/5/7 и чужая
//  калибровка серво — из-за него на АКЖТ перестали работать светофоры и стрелки.
//  Правильная прошивка АКЖТ — папка ..\AKZHT_Main (см. ..\ИНСТРУКЦИЯ.txt).
// =====================================================================
#error "Устаревший скетч. Используйте Arduino\\AKZHT_Main\\AKZHT_Main.ino"

/**
 * StationMain_AKZHT.ino
 *
 * Это КОПИЯ StationMain.ino (TrainSt-main\station\Arduino scache\StationMain), в которой
 * светофоры (Lights.h), стрелки/серво (Switches.h) и 165-я плата (Sensors.h) НЕ ИЗМЕНЕНЫ —
 * файлы скопированы один в один, пины те же.
 *
 * ДОБАВЛЕНО только чтение детекторов занятости, подключённых ПРЯМО к пинам 8 и 10
 * (файл PinSensors.h). Отличия от оригинала помечены словом [АКЖТ].
 * В порт дополнительно идёт строка "Occ: 10" (1-й символ — пин 8, 2-й — пин 10, '0' = занято).
 */

#include "Sensors.h"
#include "Lights.h"
#include "Switches.h"
#include "PinSensors.h"   // [АКЖТ] детекторы занятости прямо на пинах 8 и 10

// Жесткая рассадка пинов строго под твой рабочий конфиг
SensorsManager sensors(9, 5, 7, 150); // Датчики 165: Data=11, Latch=7, Clock=6
LightsManager  lights(2, 5, 7);       // Светофоры 595: Data=8, Latch=9, Clock=10
SwitchesManager switches;              // Стрелки PCA9685: Шина I2C (A4/A5)
PinSensorsManager pinSensors;          // [АКЖТ] Детекторы: пин 8 = Н-М1, пин 10 = УII

void setup() {
  Serial.begin(9600);

  sensors.begin();
  lights.begin();
  switches.begin();
  pinSensors.begin();   // [АКЖТ]

  Serial.println("SYSTEM: GENERAL KERNEL ACTIVE");
  Serial.println("SYSTEM: AKZHT PIN SENSORS 8,10");   // [АКЖТ] по этой строке видно, что залита новая версия
}

void loop() {
  if (Serial.available() > 0) {
    char marker = Serial.peek(); // Просто смотрим на маркер, не удаляя его!

    if (marker == 'L') {
      // Маркер 'L' остается в буфере! Передаем управление в Lights,
      // где выполнится твоя родная проверка (Serial.available() >= 6)
      lights.checkSerial(); 
    } 
    else if (marker == 'W') {
      Serial.read();         // А вот маркер стрелок 'W' стираем, им занимается parseInt
      switches.checkSerial(); 
    } 
    else {
      // Очистка от одиночных мусорных байт (\n, \r)
      if (marker != 'L' && marker != 'W') {
        Serial.read();
      }
    }
  }

  // Асинхронный опрос датчиков рельсовых цепей
  sensors.update();

  // [АКЖТ] Опрос детекторов на пинах 8 и 10
  pinSensors.update();
}