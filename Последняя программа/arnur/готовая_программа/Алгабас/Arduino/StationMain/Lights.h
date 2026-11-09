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
    LightsManager(int dPin, int lPin, int cPin) {
        dataPin = dPin;
        latchPin = lPin;
        clockPin = cPin;
        for (int i = 0; i < 5; i++) signalBytes[i] = 0xFF; // active-low: FF = всё погашено
    }

    void begin() {
        pinMode(dataPin, OUTPUT);
        pinMode(latchPin, OUTPUT);
        pinMode(clockPin, OUTPUT);
        updateHardware();
    }

    // Маркер 'L' ещё в буфере. Ждём 6 байт: L + 5 данных.
    // Если за 30 мс пакет не собрался — выкидываем мусор, иначе зависаем на peek('L')
    // и следующие W/L съедаются как «лампы».
    void checkSerial() {
        if (Serial.peek() != 'L') return;

        unsigned long t0 = millis();
        while (Serial.available() < 6) {
            if (millis() - t0 > 30) {
                Serial.read(); // сбросить битый 'L'
                return;
            }
        }

        if (Serial.read() != 'L') return;
        for (int i = 0; i < 5; i++) {
            signalBytes[i] = Serial.read();
        }
        updateHardware();
    }

    // Вызывать во время долгого хода серво, иначе RX переполняется и всё плывёт
    void pumpWhileBusy() {
        while (Serial.available() > 0 && Serial.peek() == 'L') {
            if (Serial.available() < 6) break;
            checkSerial();
        }
    }

private:
    void updateHardware() {
        digitalWrite(latchPin, LOW);
        for (int i = 4; i >= 0; i--) {
            shiftOut(dataPin, clockPin, MSBFIRST, signalBytes[i]);
        }
        digitalWrite(latchPin, HIGH);
    }
};

#endif
