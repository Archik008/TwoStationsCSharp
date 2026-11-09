#ifndef LIGHTS_H
#define LIGHTS_H

#include <Arduino.h>

class LightsManager {
private:
    int dataPin;
    int latchPin;
    int clockPin;
    byte signalBytes[5]; 

public:
    // Конструктор получает твои пины (8, 9, 10)
    LightsManager(int dPin, int lPin, int cPin) {
        dataPin = dPin;
        latchPin = lPin;
        clockPin = cPin;
        for (int i = 0; i < 5; i++) {
            signalBytes[i] = 0xFF;
        }
    }

    // Твой setup
    void begin() {
        pinMode(dataPin, OUTPUT);
        pinMode(latchPin, OUTPUT);
        pinMode(clockPin, OUTPUT);
        updateHardware();
    }

    // Твой loop (вызывается в главном loop)
    void checkSerial() {
        // Код перенесен в точности, как в твоем идеальном скетче
        if (Serial.available() >= 6) {
            if (Serial.read() == 'L') {
                for (int i = 0; i < 5; i++) {
                    signalBytes[i] = Serial.read();
                }
                updateHardware();
            }
        }
    }

private:
    // Твой updateHardware
    void updateHardware() {
        digitalWrite(latchPin, LOW);
        for (int i = 4; i >= 0; i--) {
            shiftOut(dataPin, clockPin, MSBFIRST, signalBytes[i]);
        }
        digitalWrite(latchPin, HIGH);
    }
};

#endif