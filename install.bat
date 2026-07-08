@echo off
setlocal EnableDelayedExpansion

:: ============================================================
::  KSA AI Advisor — установка
::  Запускай из папки куда ты распаковал архив
:: ============================================================

set MOD_DIR=%USERPROFILE%\Documents\My Games\Kitten Space Agency\mods\KSAAdvisor
set MANIFEST=%USERPROFILE%\Documents\My Games\Kitten Space Agency\manifest.toml

echo.
echo =============================================
echo   KSA AI Advisor -- Установка
echo =============================================
echo.

:: Проверяем что DLL рядом с батником
if not exist "%~dp0KSAAdvisor.dll" (
    echo [!] KSAAdvisor.dll не найден рядом с install.bat
    echo     Убедись что ты распаковал весь архив целиком.
    pause
    exit /b 1
)

:: Создаём папку мода
if not exist "%MOD_DIR%" (
    mkdir "%MOD_DIR%"
    echo [OK] Создана папка мода
)

:: Копируем файлы
copy /Y "%~dp0KSAAdvisor.dll" "%MOD_DIR%\" >nul
echo [OK] KSAAdvisor.dll установлен

if exist "%~dp0StarMap.API.dll" (
    copy /Y "%~dp0StarMap.API.dll" "%MOD_DIR%\" >nul
    echo [OK] StarMap.API.dll установлен
)

:: Проверяем manifest.toml
if not exist "%MANIFEST%" (
    echo.
    echo [!] manifest.toml не найден: %MANIFEST%
    goto :show_manifest
)

findstr /C:"KSAAdvisor" "%MANIFEST%" >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    goto :show_manifest
) else (
    echo [OK] Мод уже есть в manifest.toml
    goto :done
)

:show_manifest
echo.
echo [!] Добавь в %MANIFEST%:
echo.
echo     [[mods]]
echo     id = "KSAAdvisor"
echo     enabled = true
echo.

:done
echo.
echo =============================================
echo   Готово!
echo   Запусти игру через StarMap.Loader.exe
echo   В игре нажми F2 чтобы открыть советника
echo =============================================
echo.
pause
