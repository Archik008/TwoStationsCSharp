/**
 * SwitchesTestI2C.ino
 * Изолированный тест стрелок ДЛЯ ПЛАТЫ PCA9685 с плавным ходом.
 * Управление через Монитор порта (Serial Monitor) на скорости 9600
 */

#include <Wire.h>
#include <Adafruit_PWMServoDriver.h>

// Создаем объект для работы с платой расширения
Adafruit_PWMServoDriver pwm = Adafruit_PWMServoDriver();

#define TURNOUTS_NUM 9   // Количество стрелок

// Настройки плавности хода (строго из твоего рабочего кода)
int stepSize = 1;      // Шаг изменения импульса
int delayTime = 25;     // Задержка между шагами (мс)

struct TurnoutPosition { 
    uint16_t plus; 
    uint16_t minus; 
}; 

// ТВОЯ ТАБЛИЦА БЕЗОПАСНЫХ ЗАДАННЫХ ПАРАМЕТРОВ
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

// Храним текущие положения стрелок: 0 — неизвестно, 1 = плюс, 2 = минус
uint8_t currentPos[TURNOUTS_NUM] = {0}; 

void setup() {
  Serial.begin(9600);
  
  pwm.begin();
  pwm.setPWMFreq(60); // Твоя рабочая частота 60 Гц
  delay(10);

  Serial.println("=== СИСТЕМА БЕЗОПАСНОСТИ PCA9685 АКТИВНА ===");
  Serial.println("Формат команд для теста: W [Номер_Стрелки] [1 или 2]");
  Serial.println("Пример: W 2 2  (перевести стрелку 2 в минус)");
  Serial.println("Пример: W 2 1  (перевести стрелку 2 в плюс)");
}

// Твоя фирменная функция плавного движения сервы
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
      if (p < stepSize) break; // Защита от переполнения uint16_t
    }
  }

  delay(500);                
  pwm.setPWM(id, 0, 0);      // Отключение питания сервы после хода (чтобы не гудела!)
}

// Логика перевода стрелки с защитой рельсов
void moveTurnout(uint8_t id, uint8_t targetPos) {
  // Защита №1: проверка границ массива
  if (id >= TURNOUTS_NUM) {
    Serial.println("ОШИБКА: Неверный номер стрелки!");
    return;
  }

  // Защита №2: если стрелка уже там — стоим на месте
  if (currentPos[id] == targetPos) {
    Serial.print("Стрелка "); Serial.print(id); Serial.println(" уже в этом положении!");
    return;
  }

  // Перевод в ПЛЮС (Положение 1)
  if (targetPos == 1) {  
    Serial.print("Перевод стрелки "); Serial.print(id); Serial.println(" в ПЛЮС (+)...");
    moveServoSlow(id, turnout[id].minus, turnout[id].plus);
    currentPos[id] = 1;
    Serial.println("Готово!");
  } 
  // Перевод в МИНУС (Положение 2)
  else if (targetPos == 2) { 
    Serial.print("Перевод стрелки "); Serial.print(id); Serial.println(" в МИНУС (-)...");
    moveServoSlow(id, turnout[id].plus, turnout[id].minus);
    currentPos[id] = 2;
    Serial.println("Готово!");
  }
}

void loop() {
  // Слушаем команды вида: W [ID] [1 или 2]
  if (Serial.available() > 0) {
    char header = Serial.read();

    if (header == 'W') {
      int swId = Serial.parseInt();    // Читаем номер стрелки
      int posMode = Serial.parseInt(); // Читаем положение (1 или 2)

      moveTurnout(swId, posMode);
    }
  }
}