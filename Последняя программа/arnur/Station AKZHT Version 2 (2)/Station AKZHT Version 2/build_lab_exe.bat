@echo off
chcp 65001 >nul
cd /d "%~dp0Station"
echo ===== Сборка StationLab.exe (win-x64, всё внутри, один файл) =====
echo Чищу старые папки bin и obj...
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
echo.
dotnet publish StationLab.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0ПРОГРАММА_LAB"
if errorlevel 1 (
  echo.
  echo ===== ОШИБКА. Скопируйте текст выше и пришлите =====
  pause
  exit /b 1
)
echo.
echo ===== ГОТОВО. Папка ПРОГРАММА_LAB. Запуск: StationLab.exe =====
pause
