/**
 * StationMain.ino
 */

#include "Sensors.h"
#include "Lights.h"
#include "Switches.h"

// Жесткая рассадка пинов строго под твой рабочий конфиг
SensorsManager sensors(9, 5, 7, 150); // Датчики 165: Data=11, Latch=7, Clock=6
LightsManager  lights(2, 5, 7);       // Светофоры 595: Data=8, Latch=9, Clock=10
SwitchesManager switches;              // Стрелки PCA9685: Шина I2C (A4/A5)

void setup() {
  Serial.begin(9600);

  sensors.begin();
  lights.begin();
  switches.begin();

  Serial.println("SYSTEM: GENERAL KERNEL ACTIVE");
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
}