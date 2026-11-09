@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
echo ============================================================
echo  РЕЗЕРВНАЯ КОПИЯ ПРОШИВКИ ARDUINO UNO (станция АКЖТ)
echo  Скрипт ЧИТАЕТ то, что сейчас залито в плату, и сохраняет
echo  в файл .hex в этой папке. В плату он НИЧЕГО не записывает.
echo ============================================================
echo  Перед запуском: закройте программу станции и Arduino IDE
echo  (они держат COM-порт), плата подключена по USB.
echo.
call :find_avrdude
if not defined AVRDUDE goto :no_avrdude
echo Найден avrdude: "%AVRDUDE%"
echo.
echo Доступные COM-порты:
powershell -NoProfile -Command "[System.IO.Ports.SerialPort]::GetPortNames()"
echo.
set /p PORT=Введите порт Arduino АКЖТ (например COM5) и нажмите Enter: 
if "%PORT%"=="" goto :eof
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm"') do set STAMP=%%i
set "FILE=backup_AKZHT_%STAMP%.hex"
echo.
echo Читаю прошивку в файл %FILE% ...
"%AVRDUDE%" -C "%CONF%" -p atmega328p -c arduino -P %PORT% -b 115200 -U flash:r:"%FILE%":i
if errorlevel 1 (
    echo.
    echo Со скоростью 115200 не получилось, пробую 57600 ^(старый загрузчик^)...
    "%AVRDUDE%" -C "%CONF%" -p atmega328p -c arduino -P %PORT% -b 57600 -U flash:r:"%FILE%":i
)
if errorlevel 1 goto :failed
echo.
echo ============================================================
echo  ГОТОВО. Резервная копия: %~dp0%FILE%
echo  Этот файл возвращает плату ровно в нынешнее состояние
echo  (светофоры, серво с их настройками — всё как сейчас).
echo  НЕ удаляйте его. Вернуть: 2_restore_firmware.bat
echo ============================================================
pause
goto :eof

:failed
echo.
echo ОШИБКА: не удалось прочитать плату. Проверьте номер порта, что программа
echo станции и Arduino IDE закрыты, и что выбрана именно плата АКЖТ.
pause
goto :eof

:no_avrdude
echo ОШИБКА: не найден avrdude. Он ставится вместе с Arduino IDE
echo (после того как в IDE хоть раз загружали скетч в Arduino Uno).
pause
goto :eof

:find_avrdude
set "AVRDUDE="
set "CONF="
rem --- Arduino IDE 2.x (и 1.8 с менеджером плат) ---
if exist "%LOCALAPPDATA%\Arduino15\packages\arduino\tools\avrdude" (
    for /r "%LOCALAPPDATA%\Arduino15\packages\arduino\tools\avrdude" %%f in (avrdude.exe) do if exist "%%f" (
        set "AVRDUDE=%%f"
        if exist "%%~dpf..\etc\avrdude.conf" set "CONF=%%~dpf..\etc\avrdude.conf"
        if exist "%%~dpfavrdude.conf" set "CONF=%%~dpfavrdude.conf"
    )
)
rem --- Arduino IDE 1.8 (стандартная установка) ---
if not defined AVRDUDE if exist "%ProgramFiles(x86)%\Arduino\hardware\tools\avr\bin\avrdude.exe" (
    set "AVRDUDE=%ProgramFiles(x86)%\Arduino\hardware\tools\avr\bin\avrdude.exe"
    set "CONF=%ProgramFiles(x86)%\Arduino\hardware\tools\avr\etc\avrdude.conf"
)
if not defined AVRDUDE if exist "%ProgramFiles%\Arduino\hardware\tools\avr\bin\avrdude.exe" (
    set "AVRDUDE=%ProgramFiles%\Arduino\hardware\tools\avr\bin\avrdude.exe"
    set "CONF=%ProgramFiles%\Arduino\hardware\tools\avr\etc\avrdude.conf"
)
exit /b 0
