@echo off
chcp 65001 >nul
rem [ИЗМЕНЕНО 20.10.2026] собирает в общую папку arnur\готовая_программа\АКЖТ
rem Запуск с любым параметром (build_exe.bat x) - без паузы в конце (так вызывает собрать_всё.bat)
set "OUT=%~dp0..\..\готовая_программа\АКЖТ"
cd /d "%~dp0Station"
echo ===== Сборка Station.exe (win-x64, всё внутри, один файл) =====
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
dotnet publish Station.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishProfile= -o "%OUT%"
if errorlevel 1 (
  echo.
  echo ===== ОШИБКА. Сфотографируйте/скопируйте текст выше и пришлите Claude =====
  if "%~1"=="" pause
  exit /b 1
)
if exist "%~dp0Arduino" robocopy "%~dp0Arduino" "%OUT%\Arduino" /e /njh /njs /nfl /ndl >nul
del /q "%OUT%\*.pdb" 2>nul
echo ===== ГОТОВО: %OUT%\Station.exe =====
if "%~1"=="" pause
exit /b 0
