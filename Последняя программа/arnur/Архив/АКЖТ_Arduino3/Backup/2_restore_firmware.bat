@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
echo ============================================================
echo  ВОЗВРАТ ПРОШИВКИ ARDUINO UNO ИЗ РЕЗЕРВНОЙ КОПИИ (.hex)
echo  Плата станет ровно такой, какой была при создании копии.
echo ============================================================
echo  Закройте программу станции и Arduino IDE, плата по USB.
echo.
call :find_avrdude
if not defined AVRDUDE goto :no_avrdude
set "FILE="
for /f "delims=" %%i in ('dir /b /o:d backup_AKZHT_*.hex 2^>nul') do set "FILE=%%i"
if not defined FILE (
    echo ОШИБКА: в этой папке нет файлов backup_AKZHT_*.hex
    pause
    goto :eof
)
echo Резервные копии в папке:
dir /b /o:d backup_AKZHT_*.hex
echo.
echo Самая новая: %FILE%
set /p CHOICE=Нажмите Enter, чтобы взять её, или впишите имя другого файла: 
if not "%CHOICE%"=="" set "FILE=%CHOICE%"
echo.
echo Доступные COM-порты:
powershell -NoProfile -Command "[System.IO.Ports.SerialPort]::GetPortNames()"
set /p PORT=Введите порт Arduino АКЖТ (например COM5): 
if "%PORT%"=="" goto :eof
echo.
echo Записываю %FILE% в плату...
"%AVRDUDE%" -C "%CONF%" -p atmega328p -c arduino -P %PORT% -b 115200 -D -U flash:w:"%FILE%":i
if errorlevel 1 (
    echo Со скоростью 115200 не получилось, пробую 57600...
    "%AVRDUDE%" -C "%CONF%" -p atmega328p -c arduino -P %PORT% -b 57600 -D -U flash:w:"%FILE%":i
)
if errorlevel 1 (
    echo.
    echo ОШИБКА записи. Проверьте порт и что программа/IDE закрыты.
    pause
    goto :eof
)
echo.
echo ГОТОВО: плата возвращена к резервной копии %FILE%.
pause
goto :eof

:no_avrdude
echo ОШИБКА: не найден avrdude (ставится вместе с Arduino IDE).
pause
goto :eof

:find_avrdude
set "AVRDUDE="
set "CONF="
if exist "%LOCALAPPDATA%\Arduino15\packages\arduino\tools\avrdude" (
    for /r "%LOCALAPPDATA%\Arduino15\packages\arduino\tools\avrdude" %%f in (avrdude.exe) do if exist "%%f" (
        set "AVRDUDE=%%f"
        if exist "%%~dpf..\etc\avrdude.conf" set "CONF=%%~dpf..\etc\avrdude.conf"
        if exist "%%~dpfavrdude.conf" set "CONF=%%~dpfavrdude.conf"
    )
)
if not defined AVRDUDE if exist "%ProgramFiles(x86)%\Arduino\hardware\tools\avr\bin\avrdude.exe" (
    set "AVRDUDE=%ProgramFiles(x86)%\Arduino\hardware\tools\avr\bin\avrdude.exe"
    set "CONF=%ProgramFiles(x86)%\Arduino\hardware\tools\avr\etc\avrdude.conf"
)
if not defined AVRDUDE if exist "%ProgramFiles%\Arduino\hardware\tools\avr\bin\avrdude.exe" (
    set "AVRDUDE=%ProgramFiles%\Arduino\hardware\tools\avr\bin\avrdude.exe"
    set "CONF=%ProgramFiles%\Arduino\hardware\tools\avr\etc\avrdude.conf"
)
exit /b 0
