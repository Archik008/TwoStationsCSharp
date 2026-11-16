@echo off
chcp 65001 >nul
rem [ДОБАВЛЕНО 12.11.2026] «Сортировочная»: собирает в arnur\готовая_программа\Сортировочная
rem Запуск с любым параметром (build_exe.bat x) - без паузы в конце (так вызывает собрать_всё.bat)
set "OUT=%~dp0..\..\готовая_программа\Сортировочная"
cd /d "%~dp0Sortirovka"
echo ===== Сборка Sortirovka.exe (win-x64, всё внутри, один файл) =====
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
dotnet publish Sortirovka.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishProfile= -o "%OUT%"
if errorlevel 1 (
  echo.
  echo ===== ОШИБКА. Сфотографируйте/скопируйте текст выше и пришлите Claude =====
  if "%~1"=="" pause
  exit /b 1
)
del /q "%OUT%\*.pdb" 2>nul
copy /y "%~dp0ИНСТРУКЦИЯ.txt" "%OUT%\ИНСТРУКЦИЯ.txt" >nul
copy /y "%~dp0НҰСҚАУЛЫҚ.txt" "%OUT%\НҰСҚАУЛЫҚ.txt" >nul
echo ===== ГОТОВО: %OUT%\Sortirovka.exe =====
if "%~1"=="" pause
exit /b 0
