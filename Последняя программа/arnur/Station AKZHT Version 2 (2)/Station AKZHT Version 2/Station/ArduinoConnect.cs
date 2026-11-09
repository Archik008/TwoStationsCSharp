using System;
using System.IO.Ports;
using System.Threading;

namespace StationApp
{
    /// <summary>
    /// Управление Arduino через USB (COM-порт).
    /// 
    /// Команды:
    /// S1:0 - стрелка 1 в прямое положение
    /// S1:1 - стрелка 1 в отклонённое положение
    /// </summary>
    public class ArduinoConnection : IDisposable
    {
        private SerialPort? _serialPort;

        public bool IsConnected =>
            _serialPort != null && _serialPort.IsOpen;

        /// <summary>
        /// Подключение к Arduino.
        /// Например: "COM3", скорость 9600.
        /// </summary>
        public bool Connect(string portName = "COM7", int baudRate = 9600)
        {
            try
            {
                if (IsConnected)
                    return true;

                _serialPort = new SerialPort(portName, baudRate)
                {
                    NewLine = "\n",
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                _serialPort.Open();

                // Небольшая пауза: Arduino обычно перезапускается
                // при открытии COM-порта.
                Thread.Sleep(1500);

                return true;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Установка состояния стрелки.
        /// state = 0 -> прямой путь
        /// state = 1 -> отклонённый путь
        /// </summary>
        public bool SetSwitch(int switchNumber, int state)
        {
            if (!IsConnected)
                return false;

            if (state != 0 && state != 1)
                return false;

            try
            {
                string command = $"S{switchNumber}:{state}";
                _serialPort!.WriteLine(command);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetSwitchStraight(int switchNumber)
        {
            return SetSwitch(switchNumber, 0);
        }

        public bool SetSwitchDiverging(int switchNumber)
        {
            return SetSwitch(switchNumber, 1);
        }

        public void Disconnect()
        {
            try
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                        _serialPort.Close();

                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch
            {
                // Ничего не делаем при закрытии
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
    }