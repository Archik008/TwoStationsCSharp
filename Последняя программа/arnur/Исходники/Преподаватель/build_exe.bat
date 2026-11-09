@echo off
chcp 65001 >nul
rem Сборка программы «Преподаватель» в общую папку arnur\готовая_программа\Преподаватель
rem Запуск с любым параметром (build_exe.bat x) - без паузы в конце (так вызывает собрать_всё.bat)
set "OUT=%~dp0..\..\готовая_программа\Преподаватель"
cd /d "%~dp0"
echo ===== Сборка Teacher.exe (win-x64, всё внутри, один файл) =====
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
dotnet publish Teacher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUT%"
if errorlevel 1 (
  echo.
  echo ===== ОШИБКА. Сфотографируйте/скопируйте текст выше и пришлите Claude =====
  if "%~1"=="" pause
  exit /b 1
)
del /q "%OUT%\*.pdb" 2>nul
echo ===== ГОТОВО: %OUT%\Teacher.exe =====
if "%~1"=="" pause
exit /b 0
