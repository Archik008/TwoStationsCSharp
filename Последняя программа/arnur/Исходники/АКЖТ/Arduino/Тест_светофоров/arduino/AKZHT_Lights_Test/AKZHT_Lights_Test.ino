/**
 * AKZHT_Lights_Test — ТОЛЬКО светофоры (тест карты битов).
 * Не путать со StationMain_AKZHT. Станцию exe ЗАКРЫТЬ.
 *
 * Пины 74HC595: Data=13, Latch=12, Clock=11. Кадр 13 байт, active-low (0=горит).
 * пин 6=HIGH, 7=LOW (как на АКЖТ).
 *
 * ПК → плата:
 *   'L' + 13 байт     — выставить кадр
 *   'A'               — всё погасить (0xFF)
 *   'Z'               — всё зажечь (0x00) — проверка «есть ли хоть что-то»
 *   'B' byte bit      — одна лампа (остальное гасим), byte 0..12, bit 0..7, строка '\n'
 *
 * Плата → ПК: SYSTEM: AKZHT LIGHTS TEST
 */

const uint8_t DATA = 13, LATCH = 12, CLOCK = 11;
const uint8_t N = 13;
byte frame[N];

void writeFrame() {
  digitalWrite(LATCH, LOW);
  for (int i = N - 1; i >= 0; i--)
    shiftOut(DATA, CLOCK, MSBFIRST, frame[i]);
  digitalWrite(LATCH, HIGH);
}

void allOff() {
  for (uint8_t i = 0; i < N; i++) frame[i] = 0xFF;
  writeFrame();
}

void allOn() {
  for (uint8_t i = 0; i < N; i++) frame[i] = 0x00;
  writeFrame();
}

void oneBit(uint8_t b, uint8_t bit) {
  allOff();
  if (b < N && bit < 8) {
    frame[b] &= (uint8_t)~(1 << bit);
    writeFrame();
  }
}

void setup() {
  pinMode(6, OUTPUT); digitalWrite(6, HIGH);
  pinMode(7, OUTPUT); digitalWrite(7, LOW);
  pinMode(DATA, OUTPUT);
  pinMode(LATCH, OUTPUT);
  pinMode(CLOCK, OUTPUT);
  Serial.begin(9600);
  Serial.setTimeout(30);
  allOff();
  Serial.println("SYSTEM: AKZHT LIGHTS TEST n=13");
}

void loop() {
  if (Serial.available() <= 0) return;
  char m = Serial.peek();

  if (m == 'L') {
    unsigned long t0 = millis();
    while (Serial.available() < N + 1) {
      if (millis() - t0 > 150) { while (Serial.available()) Serial.read(); return; }
    }
    Serial.read();
    for (uint8_t i = 0; i < N; i++) frame[i] = Serial.read();
    writeFrame();
    return;
  }

  if (m == 'A') { Serial.read(); allOff(); Serial.println("OK ALL_OFF"); return; }
  if (m == 'Z') { Serial.read(); allOn();  Serial.println("OK ALL_ON");  return; }

  if (m == 'B') {
    Serial.read();
    int b = Serial.parseInt();
    int bit = Serial.parseInt();
    while (Serial.available() && (Serial.peek() == '\n' || Serial.peek() == '\r')) Serial.read();
    oneBit((uint8_t)b, (uint8_t)bit);
    Serial.print("OK BIT ");
    Serial.print(b);
    Serial.print('.');
    Serial.println(bit);
    return;
  }

  Serial.read(); // мусор
}
