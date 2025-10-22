@echo off
echo ========================================
echo   Sudoku Game - Windows EXE Builder
echo ========================================
echo.

REM Check if we're in the right directory
if not exist "Sudoku Game.csproj" (
    echo ERROR: Sudoku Game.csproj not found!
    echo Please run this script from the "Sudoku Game" project folder.
    echo.
    pause
    exit /b 1
)

echo [1/4] Cleaning previous builds...
dotnet clean -c Release >nul 2>&1

echo [2/4] Building project...
dotnet build -c Release
if errorlevel 1 (
    echo.
    echo ERROR: Build failed! Check the errors above.
    pause
    exit /b 1
)

echo [3/4] Publishing self-contained EXE...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true
if errorlevel 1 (
    echo.
    echo ERROR: Publish failed! Check the errors above.
    pause
    exit /b 1
)

echo [4/4] Locating your EXE...
echo.
echo ========================================
echo   BUILD SUCCESSFUL!
echo ========================================

set "EXE_PATH=bin\Release\net8.0-windows\win-x64\publish\Sudoku Game.exe"

if exist "%EXE_PATH%" (
    echo.
    echo Your game is ready at:
    echo %CD%\%EXE_PATH%
    echo.
    echo File size: 
    for %%A in ("%EXE_PATH%") do echo %%~zA bytes
    echo.
    echo Opening folder...
    explorer "bin\Release\net8.0-windows\win-x64\publish"
) else (
    echo.
    echo Warning: Could not find EXE at expected location.
    echo Check: bin\Release\net8.0-windows\win-x64\publish\
)

echo.
echo ========================================
echo Press any key to exit...
pause >nul
