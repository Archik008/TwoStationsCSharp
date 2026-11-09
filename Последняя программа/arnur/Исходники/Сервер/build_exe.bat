@echo off
chcp 65001 >nul
rem Сборка PromServer.exe (нужен Go 1.23 или новее: https://go.dev/dl/)
rem [ИЗМЕНЕНО 20.10.2026] exe кладётся в общую папку arnur\готовая_программа\Сервер
set "OUT=%~dp0..\..\готовая_программа\Сервер"
cd /d "%~dp0"
go test ./... || goto fail
if not exist "%OUT%" mkdir "%OUT%"
go build -trimpath -ldflags "-s -w" -o "%OUT%\PromServer.exe" . || goto fail
echo.
echo Готово: %OUT%\PromServer.exe
if "%~1"=="" pause
exit /b 0
:fail
echo.
echo ОШИБКА сборки
if "%~1"=="" pause
exit /b 1
