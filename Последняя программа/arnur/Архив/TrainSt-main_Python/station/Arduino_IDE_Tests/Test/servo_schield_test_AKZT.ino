#include <Wire.h>
#include <Adafruit_PWMServoDriver.h>

// ===================== PCA9685 (серво) =====================
Adafruit_PWMServoDriver pwm = Adafruit_PWMServoDriver();  // адрес по умолчанию 0x40

// Каналы PCA9685 (как в твоём тестовом коде)
const uint8_t CH_M1M10 = 13;  // стрелка 2
const uint8_t CH_M2H3  = 7;   // стрелка 8
const uint8_t CH_H42   = 6;   // стрелка 4
const uint8_t CH_2T1A  = 5;   // стрелка 6, привод A
const uint8_t CH_2T1B  = 4;   // стрелка 6, привод B

// ===================== 74HC165 (кнопки) =====================
const int loadPin    = 10; // SH/LD 74HC165
const int clockInPin = 11; // CLK   74HC165
const int dataInPin  = 8;  // QH    74HC165

// ===================== 74HC595 (индикация кнопок) =====================
const int dataOutPin  = 9;  // DS    74HC595
const int clockOutPin = 11; // SH_CP 74HC595
const int latchPin    = 12; // ST_CP 74HC595

// ===================== 74HC595 для светофора CH =====================
#define CLOCK A1
#define DATA  A3
#define LATCH A2

// 0 в бите = лампа ВКЛ, 1 = ВЫКЛ (как в твоём тесте CH)
byte currentPattern = 0b11011111; // стартовый аспект (красный)

// Состояния кнопок/LED
byte buttonStates = 0; // 1 = кнопка нажата
byte ledStates    = 0; // 1 = LED горит

// ===================== Калибровка импульсов =====================
// Твои значения:
// 2: 210–230
// 4: 230–210
// 6: 230–210
// 8: 240–220

// Две фазы: OPEN (1) / CLOSED (2)

// M1M10 (стрелка 2, канал 13)
const uint16_t M1M10_OPEN   = 210;
const uint16_t M1M10_CLOSED = 230;

// H42 (стрелка 4, канал 6)
const uint16_t H42_OPEN   = 230;
const uint16_t H42_CLOSED = 210;

// 2T1 (стрелка 6, каналы 5 и 4)
const uint16_t T2T1A_OPEN   = 230;
const uint16_t T2T1A_CLOSED = 210;
const uint16_t T2T1B_OPEN   = 230;
const uint16_t T2T1B_CLOSED = 210;

// M2H3 (стрелка 8, канал 7)
const uint16_t M2H3_OPEN   = 240;
const uint16_t M2H3_CLOSED = 220;

// ===================== Плавность движения =====================
int stepSize  = 1;    // шаг по тикам PCA9685 (чем меньше, тем плавнее)
int delayTime = 25;   // задержка между шагами (мс)

// ===================== Вспомогательные функции =====================

// Отправка байта на светофор CH (74HC595 на A1/A2/A3)
void sendPattern(byte pattern) {
  digitalWrite(LATCH, LOW);
  shiftOut(DATA, CLOCK, LSBFIRST, pattern);
  digitalWrite(LATCH, HIGH);
}

// Плавное движение одного канала PCA9685
void moveChannelSlow(uint8_t ch, uint16_t fromVal, uint16_t toVal) {
  if (fromVal < toVal) {
    for (uint16_t v = fromVal; v <= toVal; v += stepSize) {
      pwm.setPWM(ch, 0, v);
      delay(delayTime);
    }
  } else {
    for (uint16_t v = fromVal; v >= toVal; v -= stepSize) {
      pwm.setPWM(ch, 0, v);
      delay(delayTime);
      if (v < stepSize) break;
    }
  }

  delay(200);
  // Отрубаем питание, чтобы серво не пищали
  pwm.setPWM(ch, 0, 0);
}

// ===================== Стрелки (две фазы, без запоминания) =====================
// targetPos: 1 = OPEN, 2 = CLOSED

// ОДНО серво (M1M10, H42, M2H3)
void setTurnoutSingle(uint8_t ch,
                      uint16_t openVal,
                      uint16_t closedVal,
                      uint8_t targetPos)
{
  if (targetPos == 1) {
    // в сторону OPEN
    moveChannelSlow(ch, closedVal, openVal);
  } else if (targetPos == 2) {
    // в сторону CLOSED
    moveChannelSlow(ch, openVal, closedVal);
  }
}

// 2T1 – ДВА серво вместе (каналы CH_2T1A и CH_2T1B)
void setTurnout2T1(uint8_t targetPos) {
  Serial.println("2T1 BOTH MOVE");
  if (targetPos == 1) {
    // ОТКРЫТЬ: оба из CLOSED к OPEN
    moveChannelSlow(CH_2T1A, T2T1A_CLOSED, T2T1A_OPEN);
    moveChannelSlow(CH_2T1B, T2T1B_CLOSED, T2T1B_OPEN);
  } else if (targetPos == 2) {
    // ЗАКРЫТЬ
    moveChannelSlow(CH_2T1A, T2T1A_OPEN, T2T1A_CLOSED);
    moveChannelSlow(CH_2T1B, T2T1B_OPEN, T2T1B_CLOSED);
  }
}

// ===================== Чтение кнопок с 74HC165 =====================
void readButtons() {
  digitalWrite(loadPin, LOW);
  delayMicroseconds(5);
  digitalWrite(loadPin, HIGH);
  delayMicroseconds(5);

  buttonStates = 0;

  for (int i = 0; i < 8; i++) {
    bool bitState = digitalRead(dataInPin);   // 0 = нажата
    buttonStates |= (!bitState << (7 - i));   // 1 = нажата

    digitalWrite(clockInPin, HIGH);
    delayMicroseconds(1);
    digitalWrite(clockInPin, LOW);
    delayMicroseconds(1);
  }
}

// ===================== Вывод на 74HC595 (индикация кнопок) =====================
void setLEDs(byte value) {
  digitalWrite(latchPin, LOW);
  shiftOut(dataOutPin, clockOutPin, LSBFIRST, value);
  digitalWrite(latchPin, HIGH);
}

// ===================== Обработка команд от ПК =====================
//
// M1M10: 'A' (open/left),  'a' (closed/right)
// H42  : 'B' / 'b'
// 2T1  : 'C' / 'c'  (оба серво сразу)
// 2T1A : 'E' / 'e'  (только канал 5, для теста)
// 2T1B : 'F' / 'f'  (только канал 4, для теста)
// M2H3 : 'D' / 'd'
// Светофор CH: 'L' + 1 байт паттерна
//
void handleCommand(byte cmd) {
  switch (cmd) {
    // M1M10 (стрелка 2, канал 13)
    case 'A':
      Serial.println("M1M10 OPEN");
      setTurnoutSingle(CH_M1M10, M1M10_OPEN, M1M10_CLOSED, 1);
      break;
    case 'a':
      Serial.println("M1M10 CLOSED");
      setTurnoutSingle(CH_M1M10, M1M10_OPEN, M1M10_CLOSED, 2);
      break;

    // H42 (стрелка 4, канал 6)
    case 'B':
      Serial.println("H42 OPEN");
      setTurnoutSingle(CH_H42, H42_OPEN, H42_CLOSED, 1);
      break;
    case 'b':
      Serial.println("H42 CLOSED");
      setTurnoutSingle(CH_H42, H42_OPEN, H42_CLOSED, 2);
      break;

    // 2T1 – ОБА серво
    case 'C':
      Serial.println("2T1 OPEN (A+B)");
      setTurnout2T1(1);
      break;
    case 'c':
      Serial.println("2T1 CLOSED (A+B)");
      setTurnout2T1(2);
      break;

    // 2T1A – отдельный привод (канал 5)
    case 'E':
      Serial.println("2T1A OPEN");
      setTurnoutSingle(CH_2T1A, T2T1A_OPEN, T2T1A_CLOSED, 1);
      break;
    case 'e':
      Serial.println("2T1A CLOSED");
      setTurnoutSingle(CH_2T1A, T2T1A_OPEN, T2T1A_CLOSED, 2);
      break;

    // 2T1B – отдельный привод (канал 4)
    case 'F':
      Serial.println("2T1B OPEN");
      setTurnoutSingle(CH_2T1B, T2T1B_OPEN, T2T1B_CLOSED, 1);
      break;
    case 'f':
      Serial.println("2T1B CLOSED");
      setTurnoutSingle(CH_2T1B, T2T1B_OPEN, T2T1B_CLOSED, 2);
      break;

    // M2H3 (стрелка 8, канал 7)
    case 'D':
      Serial.println("M2H3 OPEN");
      setTurnoutSingle(CH_M2H3, M2H3_OPEN, M2H3_CLOSED, 1);
      break;
    case 'd':
      Serial.println("M2H3 CLOSED");
      setTurnoutSingle(CH_M2H3, M2H3_OPEN, M2H3_CLOSED, 2);
      break;

    default:
      // 'L' и прочее разбираем в loop()
      break;
  }
}

// ===================== SETUP =====================
void setup() {
  Serial.begin(9600);

  // --- 74HC165 (кнопки) ---
  pinMode(dataInPin, INPUT);
  pinMode(clockInPin, OUTPUT);
  pinMode(loadPin, OUTPUT);

  // --- 74HC595 (индикация кнопок) ---
  pinMode(dataOutPin, OUTPUT);
  pinMode(clockOutPin, OUTPUT);
  pinMode(latchPin, OUTPUT);

  digitalWrite(clockInPin, LOW);
  digitalWrite(clockOutPin, LOW);
  digitalWrite(loadPin, HIGH);
  digitalWrite(latchPin, LOW);

  setLEDs(0);  // все LED выключены

  // --- 74HC595 светофора CH ---
  pinMode(CLOCK, OUTPUT);
  pinMode(DATA,  OUTPUT);
  pinMode(LATCH, OUTPUT);
  digitalWrite(LATCH, HIGH);
  sendPattern(currentPattern);  // стартовый аспект CH

  // --- PCA9685 (серво) ---
  pwm.begin();
  pwm.setPWMFreq(60);  // 50–60 Гц для серв
  delay(10);

  Serial.println("Station + PCA9685 setup done");

  // КРАТКИЙ САМОТЕСТ: все стрелки туда-обратно

  // M1M10
  setTurnoutSingle(CH_M1M10, M1M10_OPEN, M1M10_CLOSED, 1);
  setTurnoutSingle(CH_M1M10, M1M10_OPEN, M1M10_CLOSED, 2);

  // H42
  setTurnoutSingle(CH_H42, H42_OPEN, H42_CLOSED, 1);
  setTurnoutSingle(CH_H42, H42_OPEN, H42_CLOSED, 2);

  // 2T1 (оба серво)
  setTurnout2T1(1);
  setTurnout2T1(2);

  // M2H3
  setTurnoutSingle(CH_M2H3, M2H3_OPEN, M2H3_CLOSED, 1);
  setTurnoutSingle(CH_M2H3, M2H3_OPEN, M2H3_CLOSED, 2);
}

// ===================== LOOP =====================
void loop() {
  // 1. Приём команд от ПК (Python)
  while (Serial.available() > 0) {
    byte cmd = Serial.read();

    if (cmd == 'L') {
      // Обновление светофора CH: ждём 1 байт паттерна
      unsigned long start = millis();
      while (Serial.available() == 0 && (millis() - start) < 20) {
        // ждём до 20 мс
      }

      if (Serial.available() >= 1) {
        byte pattern = Serial.read();
        currentPattern = pattern;
        sendPattern(currentPattern);
      }

      // Если вдруг прилетит ещё байт по старому протоколу — съедаем
      if (Serial.available() > 0) {
        (void)Serial.read();
      }
    } else {
      // Все стрелочные команды
      handleCommand(cmd);
    }
  }

  // 2. Читаем кнопки (74HC165)
  readButtons();

  // 3. Дублируем состояние кнопок на 74HC595 (индикация)
  ledStates = 0;
  for (int i = 0; i < 8; i++) {
    if (buttonStates & (1 << (7 - i))) {   // 1 = нажата
      ledStates |= (1 << (7 - i));         // соответствующий LED зажигаем
    }
  }
  setLEDs(ledStates);

  // 4. Отправляем байт кнопок в Python (для занятости сегментов)
  Serial.write(buttonStates);

  delay(50);  // ~20 Гц опрос
}
