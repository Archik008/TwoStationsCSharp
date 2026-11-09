#ifndef SENSORS_H
#define SENSORS_H

#include <Arduino.h>

class SensorsManager {
private:
    int dataPin;
    int latchPin;
    int clockPin;
    unsigned long previousMillis;
    unsigned long interval;

public:
    // Конструктор: запоминаем пины и интервал опроса путей
    SensorsManager(int dPin, int lPin, int cPin, unsigned long checkInterval = 150) {
        dataPin = dPin;
        latchPin = lPin;
        clockPin = cPin;
        interval = checkInterval;
        previousMillis = 0;
    }

    // Инициализация пинов (вызывается из главного setup)
    void begin() {
        pinMode(dataPin, INPUT);
        pinMode(latchPin, OUTPUT);
        pinMode(clockPin, OUTPUT);
    }

    // Асинхронное обновление по таймеру (вызывается из главного loop)
    void update() {
        unsigned long currentMillis = millis();
        if (currentMillis - previousMillis >= interval) {
            previousMillis = currentMillis;
            readAndSend();
        }
    }

private:
    void readAndSend() {
        // Загружаем данные из входов в регистр 165
        digitalWrite(latchPin, LOW);
        delayMicroseconds(5);
        digitalWrite(latchPin, HIGH);

        uint32_t data = 0;

        // Побитово читаем 3 регистра (24 бита)
        for (int i = 0; i < 24; i++) {
            data <<= 1;

            if (digitalRead(dataPin)) {
                data |= 1;
            }

            // Тактовый импульс
            digitalWrite(clockPin, HIGH);
            delayMicroseconds(5);
            digitalWrite(clockPin, LOW);
        }

        // Вывод пакета в Serial порт для Python
        Serial.print("Data: ");
        Serial.println(data, BIN);
    }
};

#endif