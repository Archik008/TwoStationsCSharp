/**
 * AKZHT_Main.ino — прошивка Arduino Uno станции АКЖТ (+ путевой пост)
 * Версия 4 (06.10.2026). Автор изменений: Claude (по заданию Арнура).
 *
 * ОТКУДА ВЗЯТЫ ПИНЫ И КАЛИБРОВКА
 *   Всё ниже восстановлено из старой рабочей прошивки АКЖТ — из резервной копии
 *   backup_AKZHT_2026-10-02_10-25.hex (её сняли батником 1_backup_firmware.bat ДО заливки
 *   StationMain_AKZHT). С той прошивкой светофоры и стрелки АКЖТ работали.
 *   Я разобрал .hex и нашёл в нём:
 *     - светофоры: регистры 74HC595, DATA = 13, LATCH = 12, CLOCK = 11, 2 байта;
 *     - стрелки: сервошилд PCA9685 (I2C, A4/A5), 16 каналов, частота 60 Гц,
 *       таблица калибровки (плюс / минус / середина) — она ниже без изменений;
 *       шаг 1, пауза 25 мс между шагами, после перевода 400 мс и отключение импульса;
 *     - пин 6 всё время HIGH, пин 7 всё время LOW (так было в старой прошивке —
 *       скорее всего, это питание/реле сервошилда; НЕ МЕНЯТЬ);
 *     - датчики занятости: пины 10, 8, 9 с подтяжкой INPUT_PULLUP, '0' = занято.
 *
 *   Почему сломалось после StationMain_AKZHT: тот скетч был сделан из StationMain другой станции —
 *   он вёл светофоры на пины 2/5/7 (а у АКЖТ они на 13/12/11), дёргал пин 7 как тактовый
 *   (а у АКЖТ он должен всё время быть LOW) и имел чужую калибровку серво.
 *
 * ЧТО НОВОГО ПО СРАВНЕНИЮ СО СТАРОЙ ПРОШИВКОЙ АКЖТ
 *   1. Занятость приходит строкой "Occ: 101" (старая слала просто "101") — программа понимает обе.
 *      Порядок символов тот же: 1-й = пин 10 (участок Н-М1), 2-й = пин 8 (УII), 3-й = пин 9 (резерв).
 *      Добавлена защита от дребезга (5 одинаковых чтений подряд через 10 мс).
 *   2. Светофоры: принимается 'L' + 5 байт (столько шлёт C#-программа). На регистры уходят все 5,
 *      а физически стоят 2 регистра — им достаются байты 0 и 1, ровно как раньше.
 *   3. Стрелки переводятся БЕЗ остановки всей программы: пока серво едет, Arduino продолжает
 *      принимать команды, зажигать светофоры и присылать занятость. Сами импульсы, шаг и паузы
 *      те же, что в старой прошивке, стрелки по-прежнему переводятся по одной, по очереди.
 *
 * ПРОТОКОЛ (как раньше)
 *   ПК → Arduino:  'L' + 5 байт         — лампы (1 бит = 1 лампа, 0 = горит)
 *                  "W <канал> <1|2|3>\n"  — стрелка: 1 = плюс, 2 = минус, 3 = середина
 *                  "C <канал> <плюс> <минус> <середина>\n" — временно поменять калибровку (до перезагрузки)
 *                  "I"                    — проверка всех 16 серво (плюс → минус → плюс), как в старой прошивке
 *   Arduino → ПК:  "SYSTEM: AKZHT v4 ..." — при старте
 *                  "Occ: 101"             — датчики (при изменении и раз в 0,5 с)
 *
 * Плата: Arduino Uno. Библиотека: Adafruit PWM Servo Driver Library (та же, что у Алгабаса).
 */

#include <Wire.h>
#include <Adafruit_PWMServoDriver.h>

// ======================= ПИНЫ (из старой рабочей прошивки) =======================
const uint8_t PIN_KEEP_HIGH = 6;   // всё время HIGH
const uint8_t PIN_KEEP_LOW  = 7;   // всё время LOW

const uint8_t LIGHT_DATA  = 13;    // 74HC595 DS
const uint8_t LIGHT_LATCH = 12;    // 74HC595 ST_CP
const uint8_t LIGHT_CLOCK = 11;    // 74HC595 SH_CP
const uint8_t LIGHT_BYTES = 5;     // сколько байт ламп шлёт программа (C#: ArduinoLink.FrameBytes)

// Датчики занятости — порядок = порядок символов в строке "Occ:"
//   [0] пин 10 → Н-М1,  [1] пин 8 → УII,  [2] пин 9 → резерв
const uint8_t SENSOR_PINS[] = { 10, 8, 9 };
const uint8_t SENSOR_COUNT = sizeof(SENSOR_PINS);

// ======================= СТРЕЛКИ (калибровка из старой прошивки) =======================
const uint8_t TURNOUTS_NUM = 16;
struct TurnoutPosition { uint16_t plus; uint16_t minus; uint16_t mid; };

TurnoutPosition turnout[TURNOUTS_NUM] = {
  {245, 215, 230},  // канал 0
  {215, 245, 245},  // канал 1
  {210, 230, 220},  // канал 2
  {210, 232, 210},  // канал 3   (1/3)
  {235, 215, 235},  // канал 4   (5/7)
  {230, 210, 230},  // канал 5   (5/7)
  {220, 245, 245},  // канал 6   (1/3)
  {230, 210, 230},  // канал 7   (19)
  {240, 220, 240},  // канал 8   (13/15)
  {230, 210, 220},  // канал 9
  {230, 210, 220},  // канал 10
  {230, 210, 220},  // канал 11
  {230, 210, 220},  // канал 12
  {230, 210, 220},  // канал 13
  {230, 210, 220},  // канал 14
  {230, 210, 220}   // канал 15  (13/15)
};

const uint16_t STEP_SIZE  = 1;     // шаг импульса
const uint16_t STEP_MS    = 25;    // пауза между шагами
const uint16_t HOLD_MS    = 400;   // подождать после перевода, потом отключить импульс

Adafruit_PWMServoDriver pwm(0x40);
uint8_t currentPos[TURNOUTS_NUM];  // 0 — неизвестно (после включения), 1 — плюс, 2 — минус, 3 — середина

// ---- очередь команд стрелок (переводим по одной, как раньше) ----
struct ServoJob { uint8_t ch; uint8_t pos; };
const uint8_t QUEUE_SIZE = 32;
ServoJob queue[QUEUE_SIZE];
uint8_t qHead = 0, qCount = 0;

// ---- текущий перевод ----
bool     moving = false;      // идёт перевод
bool     holding = false;     // доехали, ждём HOLD_MS
uint8_t  mvCh, mvPos;
uint16_t mvNow, mvTo;
unsigned long mvNext;

// ======================= СВЕТОФОРЫ =======================
byte lights[LIGHT_BYTES];

void writeLights() {
  digitalWrite(LIGHT_LATCH, LOW);
  for (int i = LIGHT_BYTES - 1; i >= 0; i--)          // последним уходит байт 0 → он в первом регистре
    shiftOut(LIGHT_DATA, LIGHT_CLOCK, MSBFIRST, lights[i]);
  digitalWrite(LIGHT_LATCH, HIGH);
}

// ======================= ДАТЧИКИ =======================
bool sensStable[SENSOR_COUNT];     // true = '1' (свободно)
bool sensLast[SENSOR_COUNT];
uint8_t sensSame[SENSOR_COUNT];
unsigned long sensReadAt = 0, sensSentAt = 0;
bool sensChanged = true;

void sendSensors() {
  Serial.print("Occ: ");
  for (uint8_t i = 0; i < SENSOR_COUNT; i++) Serial.print(sensStable[i] ? '1' : '0');
  Serial.println();
  sensSentAt = millis();
  sensChanged = false;
}

void updateSensors() {
  unsigned long now = millis();
  if (now - sensReadAt >= 10) {
    sensReadAt = now;
    for (uint8_t i = 0; i < SENSOR_COUNT; i++) {
      bool v = digitalRead(SENSOR_PINS[i]) == HIGH;
      if (v == sensLast[i]) {
        if (sensSame[i] < 5) sensSame[i]++;
      } else {
        sensLast[i] = v;
        sensSame[i] = 1;
      }
      if (sensSame[i] >= 5 && sensStable[i] != v) {   // 5 одинаковых чтений подряд = 50 мс
        sensStable[i] = v;
        sensChanged = true;
      }
    }
  }
  if (sensChanged || now - sensSentAt >= 500) sendSensors();
}

// ======================= СТРЕЛКИ: очередь и плавный перевод без остановки =======================
uint16_t pulseFor(uint8_t ch, uint8_t pos) {
  if (pos == 1) return turnout[ch].plus;
  if (pos == 2) return turnout[ch].minus;
  return turnout[ch].mid;
}

void enqueueServo(int ch, int pos) {
  if (ch < 0 || ch >= TURNOUTS_NUM || pos < 1 || pos > 3) return;
  if (qCount >= QUEUE_SIZE) return;                      // переполнение — команду пропускаем
  queue[(qHead + qCount) % QUEUE_SIZE] = { (uint8_t)ch, (uint8_t)pos };
  qCount++;
}

void startNextServo() {
  while (qCount > 0) {
    ServoJob j = queue[qHead];
    qHead = (qHead + 1) % QUEUE_SIZE;
    qCount--;

    // откуда едем — как в старой прошивке: из плюса, из минуса или (если неизвестно) из середины
    uint16_t from = (currentPos[j.ch] == 1) ? turnout[j.ch].plus
                  : (currentPos[j.ch] == 2) ? turnout[j.ch].minus
                  : turnout[j.ch].mid;
    uint16_t to = pulseFor(j.ch, j.pos);
    if (from == to && currentPos[j.ch] != 0) {            // уже там — ничего не делаем
      currentPos[j.ch] = j.pos;
      continue;
    }
    mvCh = j.ch; mvPos = j.pos; mvNow = from; mvTo = to;
    moving = true; holding = false;
    pwm.setPWM(mvCh, 0, mvNow);
    mvNext = millis() + STEP_MS;
    return;
  }
}

void updateServo() {
  if (!moving) { startNextServo(); return; }
  unsigned long now = millis();
  if ((long)(now - mvNext) < 0) return;

  if (holding) {                                        // выдержали паузу — отключаем импульс
    pwm.setPWM(mvCh, 0, 0);
    currentPos[mvCh] = mvPos;
    moving = holding = false;
    return;
  }
  if (mvNow == mvTo) {                                  // доехали
    holding = true;
    mvNext = now + HOLD_MS;
    return;
  }
  if (mvNow < mvTo) mvNow = (mvTo - mvNow > STEP_SIZE) ? mvNow + STEP_SIZE : mvTo;
  else              mvNow = (mvNow - mvTo > STEP_SIZE) ? mvNow - STEP_SIZE : mvTo;
  pwm.setPWM(mvCh, 0, mvNow);
  mvNext = now + STEP_MS;
}

// Проверка всех серво по команде 'I' (как в старой прошивке; программа её не шлёт)
void servoSelfTest() {
  for (uint8_t i = 0; i < TURNOUTS_NUM; i++) { pwm.setPWM(i, 0, turnout[i].plus); delay(60); }
  delay(400);
  for (uint8_t i = 0; i < TURNOUTS_NUM; i++) { pwm.setPWM(i, 0, turnout[i].minus); delay(60); }
  delay(400);
  for (uint8_t i = 0; i < TURNOUTS_NUM; i++) { pwm.setPWM(i, 0, turnout[i].plus); currentPos[i] = 1; delay(60); }
  delay(400);
  for (uint8_t i = 0; i < TURNOUTS_NUM; i++) pwm.setPWM(i, 0, 0);
}

// ======================= ПРИЁМ КОМАНД (без зависаний) =======================
enum RxState { RX_IDLE, RX_LIGHTS, RX_TEXT };
RxState rxState = RX_IDLE;
char    rxCmd;                 // 'W' или 'C'
char    rxText[40];
uint8_t rxLen;
byte    rxLights[LIGHT_BYTES];
uint8_t rxLightIdx;
unsigned long rxStarted;

void runTextCommand() {
  rxText[rxLen] = 0;
  long v[4] = { -1, -1, -1, -1 };
  uint8_t n = 0;
  char *p = rxText;
  while (*p && n < 4) {
    while (*p && (*p < '0' || *p > '9')) p++;            // пропускаем пробелы и мусор
    if (!*p) break;
    v[n++] = strtol(p, &p, 10);
  }
  if (rxCmd == 'W' && n >= 2) {
    enqueueServo((int)v[0], (int)v[1]);
  } else if (rxCmd == 'C' && n >= 4 && v[0] >= 0 && v[0] < TURNOUTS_NUM) {
    turnout[v[0]] = { (uint16_t)v[1], (uint16_t)v[2], (uint16_t)v[3] };
  }
}

void readSerial() {
  // если команда оборвалась на середине — через 200 мс забываем её
  if (rxState != RX_IDLE && millis() - rxStarted > 200) {
    if (rxState == RX_TEXT) runTextCommand();
    rxState = RX_IDLE;
  }

  while (Serial.available() > 0) {
    int c = Serial.read();
    switch (rxState) {
      case RX_IDLE:
        if (c == 'L') { rxState = RX_LIGHTS; rxLightIdx = 0; rxStarted = millis(); }
        else if (c == 'W' || c == 'C') { rxState = RX_TEXT; rxCmd = (char)c; rxLen = 0; rxStarted = millis(); }
        else if (c == 'I') servoSelfTest();
        // остальное (\r, \n, мусор) пропускаем
        break;

      case RX_LIGHTS:
        rxLights[rxLightIdx++] = (byte)c;
        if (rxLightIdx >= LIGHT_BYTES) {
          memcpy(lights, rxLights, LIGHT_BYTES);
          writeLights();
          rxState = RX_IDLE;
        }
        break;

      case RX_TEXT:
        if (c == '\n' || c == '\r' || c == 'L' || c == 'W' || c == 'C' || rxLen >= sizeof(rxText) - 1) {
          runTextCommand();
          rxState = RX_IDLE;
          if (c == 'L') { rxState = RX_LIGHTS; rxLightIdx = 0; rxStarted = millis(); }
          else if (c == 'W' || c == 'C') { rxState = RX_TEXT; rxCmd = (char)c; rxLen = 0; rxStarted = millis(); }
        } else {
          rxText[rxLen++] = (char)c;
        }
        break;
    }
  }
}

// ======================= SETUP / LOOP =======================
void setup() {
  pinMode(PIN_KEEP_HIGH, OUTPUT);
  pinMode(PIN_KEEP_LOW, OUTPUT);
  digitalWrite(PIN_KEEP_HIGH, HIGH);
  digitalWrite(PIN_KEEP_LOW, LOW);

  Serial.begin(9600);

  pwm.begin();
  pwm.setPWMFreq(60);
  delay(10);
  for (uint8_t i = 0; i < TURNOUTS_NUM; i++) {           // все серво расслаблены, положение неизвестно
    currentPos[i] = 0;
    pwm.setPWM(i, 0, 0);
  }

  pinMode(LIGHT_DATA, OUTPUT);
  pinMode(LIGHT_LATCH, OUTPUT);
  pinMode(LIGHT_CLOCK, OUTPUT);
  for (uint8_t i = 0; i < LIGHT_BYTES; i++) lights[i] = 0xFF;   // всё погашено
  writeLights();

  for (uint8_t i = 0; i < SENSOR_COUNT; i++) {
    pinMode(SENSOR_PINS[i], INPUT_PULLUP);
    sensStable[i] = sensLast[i] = digitalRead(SENSOR_PINS[i]) == HIGH;
    sensSame[i] = 5;
  }

  Serial.println("SYSTEM: AKZHT v4 (lights 13/12/11, servo PCA9685, sensors 10,8,9)");
  sendSensors();
}

void loop() {
  digitalWrite(PIN_KEEP_HIGH, HIGH);   // как в старой прошивке: держим уровни в каждом цикле
  digitalWrite(PIN_KEEP_LOW, LOW);

  readSerial();
  updateServo();
  updateSensors();
}
