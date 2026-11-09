@echo off
chcp 65001 >nul
rem [ДОБАВЛЕНО 05.11.2026] Редактор станций (C#) - собирает в общую папку arnur\готовая_программа\Редактор станций
rem Запуск с любым параметром (build_exe.bat x) - без паузы в конце (так вызывает собрать_всё.bat)
set "OUT=%~dp0..\..\готовая_программа\Редактор станций"
cd /d "%~dp0StationEditor"
echo ===== Сборка StationEditor.exe (win-x64, всё внутри, один файл) =====
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
dotnet publish StationEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishProfile= -o "%OUT%"
if errorlevel 1 (
  echo.
  echo ===== ОШИБКА. Сфотографируйте/скопируйте текст выше и пришлите Claude =====
  if "%~1"=="" pause
  exit /b 1
)
del /q "%OUT%\*.pdb" 2>nul
if not exist "%OUT%\Станции" mkdir "%OUT%\Станции"
echo ===== ГОТОВО: %OUT%\StationEditor.exe =====
if "%~1"=="" pause
exit /b 0
