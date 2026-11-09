/**
 * StationMain_AKZHT — тот же каркас, что StationMain Алгабаса.
 * Отличия только в пинах / количестве:
 *   Свет:     74HC595  Data=13, Latch=12, Clock=11, 13 байт (Excel АКЖТ)
 *   Занятость: пины 10, 8, 9  → строка "Occ: …"  (у Алгабаса — 74HC165 → "Data:")
 *   Стрелки:  W id pos → PCA9685 (без библиотеки Adafruit, как Алгабас)
 *   пин 6 = HIGH, пин 7 = LOW (так в рабочей прошивке АКЖТ)
 */

#include "Lights.h"
#include "Switches.h"
#include "PinSensors.h"

LightsManager     lights(13, 12, 11);   // 595
SwitchesManager   switches;            // PCA9685 0x40
PinSensorsManager pinSensors;          // Occ: пины 10, 8, 9

void setup() {
  pinMode(6, OUTPUT); digitalWrite(6, HIGH);
  pinMode(7, OUTPUT); digitalWrite(7, LOW);

  Serial.begin(9600);
  Serial.setTimeout(20);

  lights.begin();
  switches.begin();
  switches.attachLights(&lights);
  pinSensors.begin();

  Serial.println("SYSTEM: AKZHT KERNEL (как Алгабас, 13 байт ламп)");
}

void loop() {
  if (Serial.available() > 0) {
    char marker = Serial.peek();

    if (marker == 'L') {
      lights.checkSerial();
    }
    else if (marker == 'W') {
      Serial.read();
      switches.checkSerial();
    }
    else if (marker == 'C') {
      Serial.read();
      switches.applyCalib();
    }
    else if (marker == 'H') {
      Serial.read();
      switches.holdPwm();
    }
    else {
      Serial.read();
    }
  }

  pinSensors.update();
}
