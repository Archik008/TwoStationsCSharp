/**
 * StationMain — Алғабас
 * Свет:     74HC595  Data=10, Latch=9, Clock=8
 * Занятость: 3×74HC165 Data=13, Latch=12, Clock=11
 * Стрелки: W id pos → PCA9685
 */

#include "Sensors.h"
#include "Lights.h"
#include "Switches.h"

SensorsManager sensors(13, 12, 11, 150);
LightsManager  lights(10, 9, 8);
SwitchesManager switches;

void setup() {
  Serial.begin(9600);
  Serial.setTimeout(20);

  sensors.begin();
  lights.begin();
  switches.begin();
  switches.attachLights(&lights);

  Serial.println("SYSTEM: ALGABAS 165 KERNEL");
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

  sensors.update();
}
