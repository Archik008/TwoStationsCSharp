@echo off
chcp 65001 >nul
rem Собирает все программы в папку готовая_программа (АКЖТ, Алгабас, Сервер, Преподаватель, Редактор станций, Станция по схеме)
call "%~dp0Исходники\АКЖТ\build_exe.bat" x || goto fail
call "%~dp0Исходники\Алгабас\build_exe.bat" x || goto fail
call "%~dp0Исходники\Сервер\build_exe.bat" x || goto fail
call "%~dp0Исходники\Преподаватель\build_exe.bat" x || goto fail
call "%~dp0Исходники\Редактор станций CSharp\build_exe.bat" x || goto fail
call "%~dp0Исходники\Станция по схеме\build_exe.bat" x || goto fail
echo.
echo ===== ВСЁ СОБРАНО: папка готовая_программа =====
pause
exit /b 0
:fail
echo ===== ОШИБКА сборки, см. текст выше =====
pause
exit /b 1
