@echo off
chcp 65001 >nul
cd /d "%~dp0python"
echo Закрой Station.exe! Нужна прошивка AKZHT_Lights_Test.
python akzht_lights_panel.py
pause
