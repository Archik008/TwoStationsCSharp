#include <Servo.h>

// ===== ПИНЫ ДЛЯ РЕГИСТРА ВВОДА (74HC165) =====
const int loadPin    = 10; // SH/LD 74HC165
const int clockInPin = 11; // CLK   74HC165
const int dataInPin  = 8;  // QH    74HC165

// ===== ПИНЫ ДЛЯ РЕГИСТРА ВЫВОДА (74HC595, ИНДИКАЦИЯ КНОПОК) =====
const int dataOutPin  = 9;  // DS    74HC595
const int clockOutPin = 11; // SH_CP 74HC595
const int latchPin    = 12; // ST_CP 74HC595

// ===== ПИНЫ ДЛЯ РЕГИСТРА СИГНАЛОВ (ОДИН 74HC595 ДЛЯ СВЕТОФОРА CH) =====
#define CLOCK A1
#define DATA  A3
#define LATCH A2

// текущий паттерн для регистра (0/1 – как разведено на плате)
// 0 в бите = лампа ВКЛ, 1 = ВЫКЛ (у тебя 0b11011111 = один красный)
byte currentPattern = 0b11011111;

// отправка байта в 74HC595 (CH)
void sendPattern(byte pattern) {
  digitalWrite(LATCH, LOW);
  shiftOut(DATA, CLOCK, LSBFIRST, pattern);
  digitalWrite(LATCH, HIGH);
}

// ===== СЕРВОПРИВОДЫ =====
// M1M10 перенесена на A2, чтобы D4 освободить под зелёный свет светофора
const int SERVO_M1M10_PIN  = A2; // стрелка M1M10
const int SERVO_H42_PIN    = 5;  // стрелка H42
const int SERVO_2T1_PIN    = 6;  // стрелка 2T1
const int SERVO_M2H3_PIN   = 7;  // стрелка M2H3
const int SERVO_MASTER_PIN = 13; // доп. серво (мастер)

// ===== СВЕТОФОР (кнопка + 3 светодиода) =====
const int Button_clook    = A0; // кнопка
const int LED_GREEN_PIN   = 4;  // зелёный
const int LED_YELLOW_PIN  = 3;  // жёлтый
const int LED_RED_PIN     = 2;  // красный

// Объекты Servo
Servo servoM1M10;
Servo servoH42;
Servo servo2T1;
Servo servoM2H3;
Servo servoMaster;

// «стоп»-импульс для уменьшения писка (серво стоит, но под током)
const int SERVO_STOP_US = 1500;

// ===== КАЛИБРОВКИ PWM (из 2/4/6/8) =====
// 2: 210–230, 4: 230–210, 6: 230–210, 8: 240–220

// M1M10 (стрелка 2)
const int M1M10_PLUS_US   = 2100; // "+"
const int M1M10_MINUS_US  = 2300; // "-"

// H42 (стрелка 4)
const int H42_PLUS_US     = 2300;
const int H42_MINUS_US    = 2100;

// 2T1 (стрелка 6)
const int T2T1_PLUS_US    = 2300;
const int T2T1_MINUS_US   = 2100;

// M2H3 (стрелка 8)
const int M2H3_PLUS_US    = 2400;
const int M2H3_MINUS_US   = 2200;

// MASTER (13-й пин) – временно такие же, потом подправишь
const int MASTER_PLUS_US  = 2400;
const int MASTER_MINUS_US = 2200;

// ===== ПАМЯТЬ О ПОЛОЖЕНИЯХ СТРЕЛОК =====
// 0 — неизвестно, 1 — плюс, 2 — минус
uint8_t pos_M1M10  = 0;
uint8_t pos_H42    = 0;
uint8_t pos_2T1    = 0;
uint8_t pos_M2H3   = 0;
uint8_t pos_MASTER = 0;

// ===== НАСТРОЙКА ПЛАВНОГО ДВИЖЕНИЯ =====
int stepSize  = 5;   // шаг по микросекундам
int delayTime = 25;  // задержка между шагами (мс)

// ===== КНОПКИ/СВЕТОДИОДЫ РЕГИСТРА =====
byte buttonStates = 0; // кнопки (1 = нажата)
byte ledStates    = 0; // светодиоды (1 = горит)

// ===== СОСТОЯНИЕ СВЕТОФОРА НА D2-D4 =====
// 0 – всё погашено (старт), 1 – зелёный, 2 – жёлтый, 3 – красный
int  trafficLightState  = 0;
bool lastButtonPressed  = false;

// ======================================================================
//           ФУНКЦИИ УПРАВЛЕНИЯ "ЛОКАЛЬНЫМ" СВЕТОФОРОМ (D2-D4)
// ======================================================================
void updateTrafficLight() {
  digitalWrite(LED_GREEN_PIN,  (trafficLightState == 1) ? HIGH : LOW);
  digitalWrite(LED_YELLOW_PIN, (trafficLightState == 2) ? HIGH : LOW);
  digitalWrite(LED_RED_PIN,    (trafficLightState == 3) ? HIGH : LOW);
}

// ======================================================================
//           ПЛАВНОЕ ДВИЖЕНИЕ СЕРВО
// ======================================================================
void moveServoSlow(Servo &s, uint16_t fromUs, uint16_t toUs) {
  if (fromUs < toUs) {
    for (uint16_t us = fromUs; us <= toUs; us += stepSize) {
      s.writeMicroseconds(us);
      delay(delayTime);
    }
  } else {
    for (int us = fromUs; us >= (int)toUs; us -= stepSize) {
      s.writeMicroseconds(us);
      delay(delayTime);
      if (us < stepSize) break;
    }
  }
  delay(300);
  s.writeMicroseconds(SERVO_STOP_US);
}

void moveMasterSlow(uint16_t fromUs, uint16_t toUs) {
  servoMaster.attach(SERVO_MASTER_PIN);

  if (fromUs < toUs) {
    for (uint16_t us = fromUs; us <= toUs; us += stepSize) {
      servoMaster.writeMicroseconds(us);
      delay(delayTime);
    }
  } else {
    for (int us = fromUs; us >= (int)toUs; us -= stepSize) {
      servoMaster.writeMicroseconds(us);
      delay(delayTime);
      if (us < stepSize) break;
    }
  }

  delay(300);
  servoMaster.writeMicroseconds(SERVO_STOP_US);
  delay(50);
  servoMaster.detach();
}

// ======================================================================
//           УПРАВЛЕНИЕ 4 СТРЕЛКАМИ
// ======================================================================
// targetPos: 1 = плюс, 2 = минус
void setTurnout(Servo &s, uint8_t &curPos,
                uint16_t plusUs, uint16_t minusUs,
                uint8_t targetPos)
{
  if (targetPos != 1 && targetPos != 2) return;
  if (curPos == targetPos) return;

  uint16_t fromUs;
  uint16_t toUs;

  if (curPos == 0) {
    fromUs = (targetPos == 1) ? minusUs : plusUs;
  } else if (curPos == 1) {
    fromUs = plusUs;
  } else {
    fromUs = minusUs;
  }

  toUs = (targetPos == 1) ? plusUs : minusUs;

  moveServoSlow(s, fromUs, toUs);
  curPos = targetPos;
}

void setMaster(uint8_t targetPos) {
  if (targetPos != 1 && targetPos != 2) return;
  if (pos_MASTER == targetPos) return;

  uint16_t fromUs;
  uint16_t toUs;

  if (pos_MASTER == 0) {
    fromUs = (targetPos == 1) ? MASTER_MINUS_US : MASTER_PLUS_US;
  } else if (pos_MASTER == 1) {
    fromUs = MASTER_PLUS_US;
  } else {
    fromUs = MASTER_MINUS_US;
  }

  toUs = (targetPos == 1) ? MASTER_PLUS_US : MASTER_MINUS_US;

  moveMasterSlow(fromUs, toUs);
  pos_MASTER = targetPos;
}

// ======================================================================
//            ОБРАБОТКА КОМАНД С ПК ПО SERIAL
// ======================================================================
//
// M1M10: 'A' (плюс), 'a' (минус)
// H42  : 'B'/'b'
// 2T1  : 'C'/'c'
// M2H3 : 'D'/'d'
// MASTER: 'E'/'e'
//
// СИГНАЛ СВЕТОФОРА CH: 'L' + 1 или 2 байта
//   первый байт – паттерн для 74HC595 (CH),
//   второй байт, если пришёл, просто читаем и игнорируем
//   (чтобы не ломать старый протокол, где ты шлёшь два байта).
//
void handleCommand(byte cmd) {
  switch (cmd) {
    // стрелки
    case 'A':
      setTurnout(servoM1M10, pos_M1M10, M1M10_PLUS_US, M1M10_MINUS_US, 1);
      break;
    case 'a':
      setTurnout(servoM1M10, pos_M1M10, M1M10_PLUS_US, M1M10_MINUS_US, 2);
      break;

    case 'B':
      setTurnout(servoH42, pos_H42, H42_PLUS_US, H42_MINUS_US, 1);
      break;
    case 'b':
      setTurnout(servoH42, pos_H42, H42_PLUS_US, H42_MINUS_US, 2);
      break;

    case 'C':
      setTurnout(servo2T1, pos_2T1, T2T1_PLUS_US, T2T1_MINUS_US, 1);
      break;
    case 'c':
      setTurnout(servo2T1, pos_2T1, T2T1_PLUS_US, T2T1_MINUS_US, 2);
      break;

    case 'D':
      setTurnout(servoM2H3, pos_M2H3, M2H3_PLUS_US, M2H3_MINUS_US, 1);
      break;
    case 'd':
      setTurnout(servoM2H3, pos_M2H3, M2H3_PLUS_US, M2H3_MINUS_US, 2);
      break;

    case 'E':
      setMaster(1);
      break;
    case 'e':
      setMaster(2);
      break;

    default:
      // 'L' и прочее обрабатываем в loop()
      break;
  }
}

// ======================================================================
//            ЧТЕНИЕ КНОПОК С 74HC165
// ======================================================================
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

// ======================================================================
//            ВЫВОД НА 74HC595 (ИНДИКАЦИЯ КНОПОК)
// ======================================================================
void setLEDs(byte value) {
  digitalWrite(latchPin, LOW);
  shiftOut(dataOutPin, clockOutPin, LSBFIRST, value);
  digitalWrite(latchPin, HIGH);
}

// ======================================================================
//                               SETUP
// ======================================================================
void setup() {
  Serial.begin(9600);

  // Входной регистр (кнопки)
  pinMode(dataInPin, INPUT);
  pinMode(clockInPin, OUTPUT);
  pinMode(loadPin, OUTPUT);

  // Выходной регистр (индикация кнопок)
  pinMode(dataOutPin, OUTPUT);
  pinMode(clockOutPin, OUTPUT);
  pinMode(latchPin, OUTPUT);

  digitalWrite(clockInPin, LOW);
  digitalWrite(clockOutPin, LOW);
  digitalWrite(loadPin, HIGH);
  digitalWrite(latchPin, LOW);

  setLEDs(0);

  // Регистр светофора CH
  pinMode(CLOCK, OUTPUT);
  pinMode(DATA,  OUTPUT);
  pinMode(LATCH, OUTPUT);
  digitalWrite(LATCH, HIGH);
  // Стартовый аспект – красный (как в твоём тесте)
  sendPattern(currentPattern);

  // Локальный светофор на D2–D4
  pinMode(Button_clook,   INPUT_PULLUP); // кнопка к GND
  pinMode(LED_GREEN_PIN,  OUTPUT);
  pinMode(LED_YELLOW_PIN, OUTPUT);
  pinMode(LED_RED_PIN,    OUTPUT);
  updateTrafficLight(); // всё погашено (state=0)

  // 4 основные стрелки
  servoM1M10.attach(SERVO_M1M10_PIN);
  servoH42.attach(SERVO_H42_PIN);
  servo2T1.attach(SERVO_2T1_PIN);
  servoM2H3.attach(SERVO_M2H3_PIN);

  // Тест стрелок
  setTurnout(servoM1M10, pos_M1M10, M1M10_PLUS_US, M1M10_MINUS_US, 1);
  setTurnout(servoM1M10, pos_M1M10, M1M10_PLUS_US, M1M10_MINUS_US, 2);

  setTurnout(servoH42, pos_H42, H42_PLUS_US, H42_MINUS_US, 1);
  setTurnout(servoH42, pos_H42, H42_PLUS_US, H42_MINUS_US, 2);

  setTurnout(servo2T1, pos_2T1, T2T1_PLUS_US, T2T1_MINUS_US, 1);
  setTurnout(servo2T1, pos_2T1, T2T1_PLUS_US, T2T1_MINUS_US, 2);

  setTurnout(servoM2H3, pos_M2H3, M2H3_PLUS_US, M2H3_MINUS_US, 1);
  setTurnout(servoM2H3, pos_M2H3, M2H3_PLUS_US, M2H3_MINUS_US, 2);

  // Тест MASTER
  setMaster(1);
  setMaster(2);

  // Стоп для основных серв
  servoM1M10.writeMicroseconds(SERVO_STOP_US);
  servoH42.writeMicroseconds(SERVO_STOP_US);
  servo2T1.writeMicroseconds(SERVO_STOP_US);
  servoM2H3.writeMicroseconds(SERVO_STOP_US);
}

// ======================================================================
//                               LOOP
// ======================================================================
void loop() {
  // 1. Чтение команд от ПК (сервы + светофор CH)
  while (Serial.available() > 0) {
    byte cmd = Serial.read();

    if (cmd == 'L') {
      // команда обновления светофора CH:
      // ждём минимум 1 байт (паттерн), максимум 2 (2-й игнорируем)
      unsigned long start = millis();
      while (Serial.available() == 0 && (millis() - start) < 10) {
        // небольшое ожидание
      }

      if (Serial.available() >= 1) {
        byte pattern = Serial.read();
        currentPattern = pattern;
        sendPattern(currentPattern);
      }

      // если Python прислал второй байт (старый протокол) – просто съедаем
      if (Serial.available() > 0) {
        (void)Serial.read();
      }

    } else {
      // остальные команды – стрелки/MASTER
      handleCommand(cmd);
    }
  }

  // 2. Читаем кнопки через 165
  readButtons();

  // 3. Отображаем на светодиоды через 595 (индикация кнопок)
  ledStates = 0;
  for (int i = 0; i < 8; i++) {
    if (buttonStates & (1 << (7 - i))) {
      ledStates |= (1 << (7 - i));
    }
  }
  setLEDs(ledStates);

  // 4. Отправляем байт в Python (для занятости)
  Serial.write(buttonStates);

  // 5. Обработка локального светофора по кнопке A0
  bool nowPressed = (digitalRead(Button_clook) == LOW); // кнопка к GND

  if (nowPressed && !lastButtonPressed) {
    if (trafficLightState == 0) {
      trafficLightState = 1;   // зелёный
    } else {
      trafficLightState++;
      if (trafficLightState > 3) {
        trafficLightState = 1; // по кругу: З→Ж→К→З
      }
    }
    updateTrafficLight();
  }
  lastButtonPressed = nowPressed;

  delay(50);
}
