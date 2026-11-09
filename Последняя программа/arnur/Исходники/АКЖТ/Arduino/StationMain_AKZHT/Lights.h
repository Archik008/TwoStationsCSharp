#ifndef LIGHTS_H
#define LIGHTS_H

#include <Arduino.h>

// Как у Алгабаса: 'L' + N байт, active-low (0 = горит).
// У Алгабаса N=5, у АКЖТ N=13 (Excel «АКЖТ + промежуточная»).
#ifndef LIGHT_FRAME_BYTES
#define LIGHT_FRAME_BYTES 13
#endif

class LightsManager {
private:
    int dataPin;
    int latchPin;
    int clockPin;
    byte signalBytes[LIGHT_FRAME_BYTES];

    void flushJunk() {
        // Сбросить битый хвост кадра — иначе следующий 'L' смешается с мусором
        // и лампы вспыхивают зелёным/белым «от помех».
        while (Serial.available() > 0) {
            int c = Serial.peek();
            if (c == 'L' || c == 'W' || c == 'C' || c == 'H') break;
            Serial.read();
        }
    }

public:
    LightsManager(int dPin, int lPin, int cPin) {
        dataPin = dPin;
        latchPin = lPin;
        clockPin = cPin;
        for (int i = 0; i < LIGHT_FRAME_BYTES; i++) signalBytes[i] = 0xFF;
    }

    void begin() {
        pinMode(dataPin, OUTPUT);
        pinMode(latchPin, OUTPUT);
        pinMode(clockPin, OUTPUT);
        updateHardware();
    }

    // Маркер 'L' ещё в буфере. Ждём 1+N байт; иначе сброс всего мусора.
    // Таймаут 120 мс: 13 байт @9600 ≈ 14 мс, запас под очередь Windows.
    void checkSerial() {
        flushJunk();
        if (Serial.peek() != 'L') return;

        const int need = LIGHT_FRAME_BYTES + 1;
        unsigned long t0 = millis();
        while (Serial.available() < need) {
            if (millis() - t0 > 120) {
                while (Serial.available() > 0) Serial.read();
                return;
            }
        }

        if (Serial.read() != 'L') {
            flushJunk();
            return;
        }
        for (int i = 0; i < LIGHT_FRAME_BYTES; i++) {
            signalBytes[i] = Serial.read();
        }
        updateHardware();
    }

    void pumpWhileBusy() {
        while (Serial.available() > 0) {
            flushJunk();
            if (Serial.peek() != 'L') break;
            if (Serial.available() < LIGHT_FRAME_BYTES + 1) break;
            checkSerial();
        }
    }

private:
    void updateHardware() {
        digitalWrite(latchPin, LOW);
        for (int i = LIGHT_FRAME_BYTES - 1; i >= 0; i--) {
            shiftOut(dataPin, clockPin, MSBFIRST, signalBytes[i]);
        }
        digitalWrite(latchPin, HIGH);
    }
};

#endif
