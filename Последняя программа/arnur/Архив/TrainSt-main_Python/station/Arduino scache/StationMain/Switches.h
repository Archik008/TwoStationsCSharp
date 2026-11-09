#ifndef SWITCHES_H
#define SWITCHES_H

#include <Arduino.h>
#include <Wire.h>
#include <Adafruit_PWMServoDriver.h>

class SwitchesManager {
private:
    Adafruit_PWMServoDriver pwm;
    static const int TURNOUTS_NUM = 9;

    int stepSize = 1;
    int delayTime = 25;

    struct TurnoutPosition { 
        uint16_t plus; 
        uint16_t minus; 
    }; 

    const TurnoutPosition turnout[TURNOUTS_NUM] = {
        {245, 215}, // стрелка 0 (условная, не участвует в работе)
        {245, 215}, // стрелка 1  
        {210, 230}, // стрелка 2  
        {220, 210}, // стрелка 3  
        {230, 210}, // стрелка 4  
        {220, 210}, // стрелка 5  
        {230, 210}, // стрелка 6  
        {220, 210}, // стрелка 7  
        {240, 220}  // стрелка 8
    };

    uint8_t currentPos[TURNOUTS_NUM];

public:
    SwitchesManager() : pwm(0x40) {
        for (int i = 0; i < TURNOUTS_NUM; i++) currentPos[i] = 0;
    }

    void begin() {
        pwm.begin();
        pwm.setPWMFreq(60);
        delay(10);
    }

    void checkSerial() {
        int swId = Serial.parseInt();    
        int posMode = Serial.parseInt(); 
        moveTurnout(swId, posMode);
    }

private:
    void moveServoSlow(uint8_t id, uint16_t fromPos, uint16_t toPos) {
        if (fromPos < toPos) {
            for (uint16_t p = fromPos; p <= toPos; p += stepSize) {
                pwm.setPWM(id, 0, p);
                delay(delayTime);
            }
        } else {
            for (uint16_t p = fromPos; p >= toPos; p -= stepSize) {
                pwm.setPWM(id, 0, p);
                delay(delayTime);
                if (p < stepSize) break; 
            }
        }
        delay(500);                
        pwm.setPWM(id, 0, 0);      
    }

    void moveTurnout(uint8_t id, uint8_t targetPos) {
        if (id >= TURNOUTS_NUM) return;
        if (currentPos[id] == targetPos) return;

        if (targetPos == 1) {  
            moveServoSlow(id, turnout[id].minus, turnout[id].plus);
            currentPos[id] = 1;
        } 
        else if (targetPos == 2) { 
            moveServoSlow(id, turnout[id].plus, turnout[id].minus);
            currentPos[id] = 2;
        }
    }
};

#endif