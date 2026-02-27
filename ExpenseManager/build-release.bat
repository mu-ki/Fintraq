@echo off
setlocal EnableExtensions

set "OUTPUT_DIR=%~1"
if "%OUTPUT_DIR%"=="" set "OUTPUT_DIR=build"

echo Building ExpenseManager in Release mode...
dotnet publish -c Release -o "%OUTPUT_DIR%"
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo.
echo Release build complete. Output: %OUTPUT_DIR%\
exit /b 0
