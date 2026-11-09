@echo off
chcp 65001 >nul
cd /d "%~dp0"

set CLI=%LOCALAPPDATA%\Programs\Arduino IDE\resources\app\lib\backend\resources\arduino-cli.exe
set AVRDUDE=%LOCALAPPDATA%\Arduino15\packages\arduino\tools\avrdude\8.0.0-arduino1\bin\avrdude.exe
set CONF=%LOCALAPPDATA%\Arduino15\packages\arduino\tools\avrdude\8.0.0-arduino1\etc\avrdude.conf
set SKETCH=%~dp0arduino\AKZHT_Lights_Test
set BUILD=%~dp0arduino\_build_lights_test
set PORT=COM5

echo Закрой Station.exe и Arduino IDE.
echo Прошивка ТЕСТ светофоров на %PORT% ...
pause

"%CLI%" compile --fqbn arduino:avr:uno --build-path "%BUILD%" "%SKETCH%"
if errorlevel 1 goto fail

"%AVRDUDE%" -C "%CONF%" -patmega328p -carduino -P%PORT% -b115200 -D "-Uflash:w:%BUILD%\AKZHT_Lights_Test.ino.hex:i"
if errorlevel 1 goto fail

echo ГОТОВО. Дальше: тест_светофоров_АКЖТ.bat
pause
exit /b 0
:fail
echo ОШИБКА
pause
exit /b 1
