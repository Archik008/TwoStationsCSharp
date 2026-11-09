#ifndef PIN_SENSORS_H
#define PIN_SENSORS_H

#include <Arduino.h>

// Занятость прямо на пинах (у Алгабаса — плата 74HC165 → "Data:").
// "Occ: 101" — порядок = PINS: [0]=пин10 Н-М1, [1]=пин8 УII, [2]=пин9 резерв.
// '0' = занято, '1' = свободно. INPUT_PULLUP: общий детектора → GND.

class PinSensorsManager {
private:
    static const uint8_t COUNT = 3;
    const uint8_t PINS[COUNT] = { 10, 8, 9 };

    static const uint8_t SAMPLE_MS = 10;
    static const uint8_t STABLE_SAMPLES = 5;
    // Реже шлём Occ — меньше шансов пересечься с кадром L на 9600.
    static const uint16_t RESEND_MS = 2000;

    uint8_t stable[COUNT];
    uint8_t counter[COUNT];
    unsigned long lastSample = 0;
    unsigned long lastSend = 0;

    void send() {
        Serial.print("Occ: ");
        for (uint8_t i = 0; i < COUNT; i++) Serial.print(stable[i] ? '1' : '0');
        Serial.println();
        lastSend = millis();
    }

public:
    void begin() {
        for (uint8_t i = 0; i < COUNT; i++) {
            pinMode(PINS[i], INPUT_PULLUP);
            counter[i] = 0;
        }
        delay(5);
        for (uint8_t i = 0; i < COUNT; i++) stable[i] = digitalRead(PINS[i]);
        send();
    }

    void update() {
        unsigned long now = millis();
        if (now - lastSample >= SAMPLE_MS) {
            lastSample = now;
            bool changed = false;
            for (uint8_t i = 0; i < COUNT; i++) {
                uint8_t r = digitalRead(PINS[i]);
                if (r != stable[i]) {
                    if (++counter[i] >= STABLE_SAMPLES) {
                        stable[i] = r;
                        counter[i] = 0;
                        changed = true;
                    }
                } else {
                    counter[i] = 0;
                }
            }
            if (changed) send();
        }
        if (now - lastSend >= RESEND_MS) send();
    }
};

#endif
