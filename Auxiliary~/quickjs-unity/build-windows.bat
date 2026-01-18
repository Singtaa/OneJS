@echo off
setlocal

REM Build quickjs_unity.dll for Windows x64 using CMake
REM Requires: Visual Studio 2019+ with C++ workload, CMake 3.16+

set SCRIPT_DIR=%~dp0
cd /d "%SCRIPT_DIR%"

REM Check for cmake
where cmake >nul 2>&1 || (
    echo Error: cmake not found in PATH
    echo Install CMake from https://cmake.org/download/
    exit /b 1
)

echo === Configuring with CMake ===
if not exist "build" mkdir build
cd build

REM Try VS 2022 first, then 2019
cmake .. -G "Visual Studio 17 2022" -A x64 2>nul
if errorlevel 1 (
    cmake .. -G "Visual Studio 16 2019" -A x64
)
if errorlevel 1 (
    echo Error: CMake configuration failed
    echo Make sure Visual Studio with C++ workload is installed
    exit /b 1
)

echo === Building ===
cmake --build . --config Release
if errorlevel 1 (
    echo Error: Build failed
    exit /b 1
)

cd ..

REM Find and copy the DLL
echo === Installing to Plugins\Windows\x64 ===
set PLUGIN_DIR=..\..\Plugins\Windows\x64
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"

if exist "build\Release\quickjs_unity.dll" (
    copy /y "build\Release\quickjs_unity.dll" "%PLUGIN_DIR%\"
) else if exist "build\quickjs_unity.dll" (
    copy /y "build\quickjs_unity.dll" "%PLUGIN_DIR%\"
) else (
    echo Error: Could not find quickjs_unity.dll
    dir /s build\*.dll
    exit /b 1
)

echo.
echo DONE. Built and installed quickjs_unity.dll to Plugins\Windows\x64\
echo.

endlocal
