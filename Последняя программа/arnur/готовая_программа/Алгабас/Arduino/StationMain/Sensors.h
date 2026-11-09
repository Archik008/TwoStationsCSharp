#ifndef SENSORS_H
#define SENSORS_H

#include <Arduino.h>

// 3 × 74HC165
// Протокол: 0 = занято, 1 = свободно
// Пины: Data=13, Latch/~PL=12, Clock=11 (как в StationMain.ino)
//
// Если в мониторе сплошные нули, а путь пустой — сначала проводка/питание 165.
// INVERT только если датчики дают наоборот (1=занято на железе).

#ifndef OCC_INVERT_BITS
#define OCC_INVERT_BITS 0
#endif

class SensorsManager {
private:
    int dataPin;
    int latchPin;
    int clockPin;
    unsigned long previousMillis;
    unsigned long interval;
    bool warnedAllZero;

    byte reg[3];

    void readRegisters() {
        // ~PL (latch): LOW = защёлкнуть параллельные входы, HIGH = сдвиг
        digitalWrite(clockPin, LOW);
        digitalWrite(latchPin, LOW);
        delayMicroseconds(40);
        digitalWrite(latchPin, HIGH);
        delayMicroseconds(5);

        reg[0] = reg[1] = reg[2] = 0;
        for (uint8_t r = 0; r < 3; r++) {
            // после load первый бит уже на Data — читаем, потом clock
            for (int8_t b = 7; b >= 0; b--) {
                delayMicroseconds(2);
                uint8_t bit = digitalRead(dataPin) ? 1 : 0;
#if OCC_INVERT_BITS
                bit ^= 1;
#endif
                if (bit) reg[r] |= (1 << b);

                digitalWrite(clockPin, HIGH);
                delayMicroseconds(5);
                digitalWrite(clockPin, LOW);
                delayMicroseconds(5);
            }
        }
    }

    void sendBytes() {
        Serial.print("Data: ");
        bool anyOne = false;
        for (uint8_t r = 0; r < 3; r++) {
            for (int8_t b = 7; b >= 0; b--) {
                char c = ((reg[r] >> b) & 1) ? '1' : '0';
                if (c == '1') anyOne = true;
                Serial.print(c);
            }
            if (r < 2) Serial.print(' ');
        }
        Serial.println();

        if (!anyOne && !warnedAllZero) {
            warnedAllZero = true;
            Serial.println("OCC_ALL_ZERO: проверь 165 питание, Data=11 Latch=7 Clock=6 (это не Python)");
        }
        if (anyOne) warnedAllZero = false;
    }

public:
    SensorsManager(int dPin, int lPin, int cPin, unsigned long checkInterval = 150) {
        dataPin = dPin;
        latchPin = lPin;
        clockPin = cPin;
        interval = checkInterval;
        previousMillis = 0;
        warnedAllZero = false;
    }

    void begin() {
        pinMode(dataPin, INPUT_PULLUP);
        pinMode(latchPin, OUTPUT);
        pinMode(clockPin, OUTPUT);
        digitalWrite(latchPin, HIGH);
        digitalWrite(clockPin, LOW);
        readRegisters();
        sendBytes();
    }

    void update() {
        unsigned long now = millis();
        if (now - previousMillis < interval) return;
        previousMillis = now;
        readRegisters();
        sendBytes();
    }
};

#endif
