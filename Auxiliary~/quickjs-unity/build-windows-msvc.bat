@echo off
setlocal enabledelayedexpansion

REM Build quickjs_unity.dll for Windows x64 using MSVC and quickjs-ng
REM Requires: Visual Studio 2019+ with C++ workload, CMake, Git

set SCRIPT_DIR=%~dp0
cd /d "%SCRIPT_DIR%"

REM Check for git
where git >nul 2>&1 || (
    echo Error: git not found in PATH
    exit /b 1
)

REM Check for cmake
where cmake >nul 2>&1 || (
    echo Error: cmake not found in PATH
    exit /b 1
)

REM Clone quickjs-ng if not present
if not exist "quickjs-ng" (
    echo === Cloning quickjs-ng ===
    git clone --depth 1 https://github.com/nicegraf/nicegraf-deps.git -b main tmp-deps 2>nul
    if errorlevel 1 (
        echo Cloning nicegraf-deps failed, trying quickjs-ng directly...
        git clone --depth 1 https://github.com/nickg/quickjs-ng.git quickjs-ng
        if errorlevel 1 (
            git clone --depth 1 https://github.com/nickg/quickjs.git quickjs-ng
        )
    ) else (
        rmdir /s /q tmp-deps
        git clone --depth 1 https://github.com/nickg/quickjs-ng.git quickjs-ng
    )
)

if not exist "quickjs-ng" (
    echo Error: Failed to clone quickjs-ng
    exit /b 1
)

REM Build quickjs-ng as static library
echo === Building quickjs-ng ===
cd quickjs-ng
if not exist "build" mkdir build
cd build

cmake .. -G "Visual Studio 17 2022" -A x64 -DBUILD_SHARED_LIBS=OFF -DCMAKE_BUILD_TYPE=Release
if errorlevel 1 (
    REM Try VS 2019
    cmake .. -G "Visual Studio 16 2019" -A x64 -DBUILD_SHARED_LIBS=OFF -DCMAKE_BUILD_TYPE=Release
)
if errorlevel 1 (
    echo Error: CMake configuration failed
    exit /b 1
)

cmake --build . --config Release
if errorlevel 1 (
    echo Error: Build failed
    exit /b 1
)

cd ..\..

REM Find the static library
set QJS_LIB=
for /r quickjs-ng\build %%f in (quickjs.lib qjs.lib libquickjs.lib) do (
    if exist "%%f" set QJS_LIB=%%f
)

if "!QJS_LIB!"=="" (
    echo Error: Could not find quickjs static library
    dir /s quickjs-ng\build\*.lib
    exit /b 1
)

echo Found QuickJS library: !QJS_LIB!

REM Build quickjs_unity.dll
echo === Building quickjs_unity.dll ===

REM Find cl.exe via vswhere or environment
where cl >nul 2>&1
if errorlevel 1 (
    REM Try to set up VS environment
    if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" (
        call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
    ) else if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat" (
        call "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat"
    ) else if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat" (
        call "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat"
    ) else (
        echo Error: Could not find Visual Studio. Please run from Developer Command Prompt.
        exit /b 1
    )
)

cl /LD /O2 /DNDEBUG ^
    /I"quickjs-ng" ^
    src\quickjs_unity.c ^
    "!QJS_LIB!" ^
    /Fe:quickjs_unity.dll

if errorlevel 1 (
    echo Error: Compilation failed
    exit /b 1
)

REM Install
echo === Installing to Plugins\Windows\x64 ===
set PLUGIN_DIR=..\..\Plugins\Windows\x64
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"
copy /y quickjs_unity.dll "%PLUGIN_DIR%\"

echo.
echo DONE. Generated quickjs_unity.dll at Plugins\Windows\x64\
echo.

endlocal
