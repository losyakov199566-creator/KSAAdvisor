@echo off
setlocal EnableDelayedExpansion

:: ============================================================
::  KSA AI Advisor — сборка и установка
::  Запускай из папки D:\ksamodding\KSAAdvisor\
:: ============================================================

set GAME_DIR=D:\Kitten Space Agency
set KSA_DLLS=%GAME_DIR%
set MOD_DIR=%USERPROFILE%\Documents\My Games\Kitten Space Agency\mods\KSAAdvisor
set MANIFEST=%USERPROFILE%\Documents\My Games\Kitten Space Agency\manifest.toml
set OUT_DIR=bin\Release\net10.0

echo.
echo =============================================
echo   KSA AI Advisor ^| Сборка
echo =============================================
echo.

:: Проверяем что DLL-ки игры на месте
if not exist "%KSA_DLLS%\KSA.dll" (
    echo [!] KSA.dll не найден в %KSA_DLLS%
    echo     Убедись что путь GAME_DIR в этом батнике правильный.
    pause
    exit /b 1
)

:: Собираем
dotnet build KSAAdvisor.csproj -c Release -p:KSA_DLLS_PATH="%KSA_DLLS%"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ОШИБКА] Сборка не удалась. Проверь ошибки выше.
    pause
    exit /b 1
)

echo.
echo =============================================
echo   KSA AI Advisor ^| Установка
echo =============================================
echo.

:: Создаём папку мода если нет
if not exist "%MOD_DIR%" (
    mkdir "%MOD_DIR%"
    echo [OK] Создана папка %MOD_DIR%
)

:: Копируем основную DLL
copy /Y "%OUT_DIR%\KSAAdvisor.dll" "%MOD_DIR%\" >nul
echo [OK] KSAAdvisor.dll скопирован


:: Копируем StarMap.API если он там оказался
if exist "%OUT_DIR%\StarMap.API.dll" (
    copy /Y "%OUT_DIR%\StarMap.API.dll" "%MOD_DIR%\" >nul
    echo [OK] StarMap.API.dll скопирован
)

:: Проверяем manifest.toml
if not exist "%MANIFEST%" (
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
echo   Запускай: D:\ksamodding\StarMap\StarMap.Loader.exe
echo   В игре нажми F2
echo =============================================
echo.
pause