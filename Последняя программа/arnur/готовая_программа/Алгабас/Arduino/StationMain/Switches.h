#ifndef SWITCHES_H
#define SWITCHES_H

#include <Arduino.h>
#include <Wire.h>

class LightsManager;

// PCA9685 без Adafruit — только Wire (адрес 0x40)
class Pca9685 {
    static const uint8_t ADDR = 0x40;
    static const uint8_t MODE1 = 0x00;
    static const uint8_t PRESCALE = 0xFE;
    static const uint8_t LED0 = 0x06;

    uint8_t wr8(uint8_t reg, uint8_t val) {
        Wire.beginTransmission(ADDR);
        Wire.write(reg);
        Wire.write(val);
        return Wire.endTransmission();
    }

    uint8_t rd8(uint8_t reg) {
        Wire.beginTransmission(ADDR);
        Wire.write(reg);
        if (Wire.endTransmission() != 0) return 0;
        Wire.requestFrom(ADDR, (uint8_t)1);
        return Wire.available() ? Wire.read() : 0;
    }

public:
    void begin() {
        Wire.begin();
        wr8(MODE1, 0x00);
        delay(10);
        setPWMFreq(60);
    }

    void setPWMFreq(float hz) {
        if (hz < 1) hz = 1;
        uint8_t prescale = (uint8_t)((25000000.0 / (4096.0 * hz)) + 0.5 - 1.0);
        uint8_t old = rd8(MODE1);
        wr8(MODE1, (old & 0x7F) | 0x10);
        wr8(PRESCALE, prescale);
        wr8(MODE1, old);
        delay(5);
        wr8(MODE1, old | 0xA0);
    }

    void setPWM(uint8_t ch, uint16_t on, uint16_t off) {
        Wire.beginTransmission(ADDR);
        Wire.write(LED0 + 4 * ch);
        Wire.write(on & 0xFF);
        Wire.write(on >> 8);
        Wire.write(off & 0xFF);
        Wire.write(off >> 8);
        Wire.endTransmission();
    }
};

class SwitchesManager {
private:
    Pca9685 pwm;
    bool shieldOk = false;
    static const int TURNOUTS_NUM = 16;

    int stepSize = 1;
    int delayTime = 25;

    struct TurnoutPosition {
        uint16_t plus;
        uint16_t minus;
    };

    // Калибровка по новым разъёмам (с нуля). Было: 1→4, 4/6→5+6, 8→7, 2→8.
    TurnoutPosition turnout[TURNOUTS_NUM] = {
        {238, 210}, // 0  стрелка 1 (первый путь)
        {220, 210}, // 1  стрелка 2
        {210, 230}, // 2  стрелка 4 (съезд 4/6)
        {228, 210}, // 3  стрелка 6 (съезд 4/6)
        {238, 210}, // 4  стрелка 8
        {230, 210}, // 5  свободно
        {230, 210}, // 6  свободно
        {230, 210}, // 7  свободно
        {220, 233}, // 8  свободно (раньше стрелка 8)
        {230, 210},
        {230, 210},
        {230, 210},
        {230, 210},
        {230, 210},
        {230, 210},
        {230, 210}
    };

    uint8_t currentPos[TURNOUTS_NUM];

    void releaseServo(uint8_t id) {
        // 4096 = полное OFF на PCA9685 (0,0 импульс не глушит — серво пищит)
        pwm.setPWM(id, 0, 4096);
    }

    // lights — чтобы во время хода не забивался RX кадрами L от Algabas.exe
    LightsManager *lightsPump = nullptr;

public:
    void attachLights(LightsManager *l) { lightsPump = l; }

private:
    void pumpLights() {
        if (lightsPump) lightsPump->pumpWhileBusy();
    }

    void moveServoSlow(uint8_t id, uint16_t fromPos, uint16_t toPos) {
        // темп хода не трогаем (stepSize / delayTime как были)
        if (fromPos < toPos) {
            for (uint16_t p = fromPos; p <= toPos; p += stepSize) {
                pwm.setPWM(id, 0, p);
                pumpLights();
                delay(delayTime);
            }
        } else {
            for (uint16_t p = fromPos; p >= toPos; p -= stepSize) {
                pwm.setPWM(id, 0, p);
                pumpLights();
                delay(delayTime);
                if (p < stepSize) break;
            }
        }
        delay(120);       // чуть доехать, без долгого упора
        releaseServo(id); // сразу снять силу — Algabas.exe после W не пищат
        pumpLights();
    }

public:
    SwitchesManager() {
        for (int i = 0; i < TURNOUTS_NUM; i++) currentPos[i] = 0;
    }

    void begin() {
        pwm.begin();
        pinMode(SDA, INPUT_PULLUP);
        pinMode(SCL, INPUT_PULLUP);
        delay(10);
        Wire.beginTransmission(0x40);
        shieldOk = (Wire.endTransmission() == 0);
        if (shieldOk) {
            for (uint8_t ch = 0; ch < 16; ch++) releaseServo(ch);
            Serial.println("SERVO_SHIELD_OK");
        } else {
            Serial.println("SERVO_SHIELD_FAIL SDA=A4 SCL=A5 addr=0x40");
        }
    }

    // Только текущая строка. parseInt перескакивает перевод строки и съедает следующую команду.
    int readRest(char *buf, int cap) {
        int n = 0;
        unsigned long t0 = millis();
        while (millis() - t0 < 100) {
            while (Serial.available() > 0) {
                char c = (char)Serial.read();
                if (c == '\n' || c == '\r') {
                    buf[n] = 0;
                    return n;
                }
                if (n + 1 < cap) buf[n++] = c;
                t0 = millis();
            }
        }
        buf[n] = 0;
        return n;
    }

    bool readNum(const char *&p, int &out) {
        while (*p == ' ' || *p == '\t') p++;
        if (*p < '0' || *p > '9') return false;
        int v = 0;
        while (*p >= '0' && *p <= '9') {
            v = v * 10 + (*p - '0');
            p++;
        }
        out = v;
        return true;
    }

    void checkSerial() {
        char buf[32];
        readRest(buf, sizeof buf);
        const char *p = buf;
        int swId = 0;
        int posMode = 0;
        if (!readNum(p, swId) || !readNum(p, posMode)) {
            Serial.print("ERR_W ");
            Serial.println(buf);
            return;
        }
        Serial.print("GOT W ");
        Serial.print(swId);
        Serial.print(" ");
        Serial.println(posMode);
        moveTurnout((uint8_t)swId, (uint8_t)posMode);
    }

    // C id plus minus — калибровка в ОЗУ, без новой прошивки
    void applyCalib() {
        char buf[48];
        readRest(buf, sizeof buf);
        int id = 0;
        int plusVal = 0;
        int minusVal = 0;
        if (sscanf(buf, "%d %d %d", &id, &plusVal, &minusVal) != 3) {
            Serial.print("ERR_C ");
            Serial.println(buf);
            return;
        }
        if (id < 0 || id >= TURNOUTS_NUM) {
            Serial.print("ERR_C ");
            Serial.println(id);
            return;
        }
        turnout[id].plus = (uint16_t)plusVal;
        turnout[id].minus = (uint16_t)minusVal;
        currentPos[id] = 0;
        Serial.print("ACK_C ");
        Serial.print(id);
        Serial.print(" plus=");
        Serial.print(plusVal);
        Serial.print(" minus=");
        Serial.println(minusVal);
    }


    // H id pwm — кратко выставить угол и сразу снять силу (не жечь серво)
    void holdPwm() {
        char buf[32];
        readRest(buf, sizeof buf);
        int id = 0;
        int val = 0;
        if (sscanf(buf, "%d %d", &id, &val) != 2) {
            Serial.print("ERR_H ");
            Serial.println(buf);
            return;
        }
        if (id < 0 || id >= TURNOUTS_NUM) {
            Serial.print("ERR_H ");
            Serial.println(id);
            return;
        }
        if (val < 0) val = 0;
        if (val > 4095) val = 4095;
        pwm.setPWM((uint8_t)id, 0, (uint16_t)val);
        delay(120);
        releaseServo((uint8_t)id);
        Serial.print("ACK_H ");
        Serial.print(id);
        Serial.print(" ");
        Serial.println(val);
    }

    void moveTurnout(uint8_t id, uint8_t targetPos) {
        if (id >= TURNOUTS_NUM) {
            Serial.print("ERR_CH ");
            Serial.println(id);
            return;
        }
        if (!shieldOk) {
            Serial.println("ERR_NO_SHIELD");
            return;
        }
        if (turnout[id].plus == turnout[id].minus) {
            Serial.print("ERR_SAME ");
            Serial.println(id);
            return;
        }

        if (targetPos == 1) {
            moveServoSlow(id, turnout[id].minus, turnout[id].plus);
            currentPos[id] = 1;
            Serial.print("ACK_W ");
            Serial.print(id);
            Serial.println(" 1");
        } else if (targetPos == 2) {
            moveServoSlow(id, turnout[id].plus, turnout[id].minus);
            currentPos[id] = 2;
            Serial.print("ACK_W ");
            Serial.print(id);
            Serial.println(" 2");
        }
    }
};

#endif
