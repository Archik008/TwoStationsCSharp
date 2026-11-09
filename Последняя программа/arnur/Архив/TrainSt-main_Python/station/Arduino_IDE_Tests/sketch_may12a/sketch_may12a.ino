// Пины для светофоров (74HC595)
#define DATA_PIN  2  // DS
#define LATCH_PIN 5  // ST_CP
#define CLOCK_PIN 7  // SH_CP

byte signalBytes[5] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF}; 

void setup() {
  Serial.begin(9600);
  pinMode(DATA_PIN, OUTPUT);
  pinMode(LATCH_PIN, OUTPUT);
  pinMode(CLOCK_PIN, OUTPUT);
  
  // Гасим всё при старте
  updateHardware();
  Serial.println("SYSTEM: LIGHTS ONLY MODE");
}

void loop() {
  // Ждем пакет: 'L' + 5 байт данных
  if (Serial.available() >= 6) {
    if (Serial.read() == 'L') {
      for (int i = 0; i < 5; i++) {
        signalBytes[i] = Serial.read();
      }
      updateHardware();
    }
  }
}

void updateHardware() {
  digitalWrite(LATCH_PIN, LOW);
  // Шлем 5 байт в цепочку
  for (int i = 4; i >= 0; i--) {
    shiftOut(DATA_PIN, CLOCK_PIN, MSBFIRST, signalBytes[i]);
  }
  digitalWrite(LATCH_PIN, HIGH);
}