#ifndef PIN_SENSORS_H
#define PIN_SENSORS_H

#include <Arduino.h>

// =====================================================================
//  [АКЖТ] ДЕТЕКТОРЫ ЗАНЯТОСТИ, ПОДКЛЮЧЁННЫЕ ПРЯМО К ПИНАМ ARDUINO
//
//  Выход "Занятость" (X4) детектора: поезд на рельсах → провод притянут к земле (LOW, '0'),
//  поезда нет → провод "висит" и подтягивается к +5 В (HIGH, '1').
//  Поэтому пины включены как INPUT_PULLUP (встроенная подтяжка к +5 В внутри Arduino).
//  ВАЖНО: "Общий" (X4) детектора должен быть соединён с GND Arduino.
//
//  Что отправляется в порт: "Occ: 10"
//    символы идут по порядку массива PINS: 1-й — пин 8, 2-й — пин 10;
//    '0' = участок ЗАНЯТ, '1' = свободен (как в строке "Data:").
//  Отправка: сразу при изменении + раз в 0,5 с (чтобы программа видела, что датчики живы).
//
//  Добавить ещё детектор: дописать пин в PINS (например {8, 10, 12}) — и в программе
//  (AkzhtHardware.cs) указать, какой участок соответствует 3-му символу.
// =====================================================================

class PinSensorsManager {
private:
    static const uint8_t COUNT = 2;
    const uint8_t PINS[COUNT] = {8, 10};      // 1-й символ — пин 8 (Н-М1), 2-й — пин 10 (УII)

    static const uint8_t SAMPLE_MS = 10;      // опрос каждые 10 мс
    static const uint8_t STABLE_SAMPLES = 5;  // изменение принимаем, если оно держится 5 опросов (50 мс) — защита от дребезга
    static const uint16_t RESEND_MS = 500;    // повтор состояния раз в 0,5 с

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
