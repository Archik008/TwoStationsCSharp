#define dataPin  9   // QH (выход данных)
#define latchPin 5    // SH/LD (загрузка параллельных данных)
#define clockPin 7    // CLK

void setup() {
  Serial.begin(9600);

  pinMode(dataPin, INPUT);
  pinMode(latchPin, OUTPUT);
  pinMode(clockPin, OUTPUT);
}

void loop() {
  // Загружаем данные из входов в регистр
  digitalWrite(latchPin, LOW);
  delayMicroseconds(5);
  digitalWrite(latchPin, HIGH);

  // Читаем 3 регистра (3 * 8 = 24 бита)
  uint32_t data = 0;

  for (int i = 0; i < 24; i++) {
    data <<= 1;

    // Чтение бита
    if (digitalRead(dataPin)) {
      data |= 1;
    }

    // Тактовый импульс
    digitalWrite(clockPin, HIGH);
    delayMicroseconds(5);
    digitalWrite(clockPin, LOW);
  }

  // Вывод в сериал монитор
  Serial.print("Data: ");
  Serial.println(data, BIN);

  delay(2000);
}