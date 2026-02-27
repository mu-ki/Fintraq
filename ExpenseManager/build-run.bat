@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "HTTP_PORT=%~1"
if "%HTTP_PORT%"=="" set "HTTP_PORT=5201"

set "HTTPS_PORT=%~2"
if "%HTTPS_PORT%"=="" set "HTTPS_PORT=7153"

echo [1/3] Checking for processes using ports %HTTP_PORT% and %HTTPS_PORT%...
call :kill_port %HTTP_PORT%
if errorlevel 1 exit /b 1

if not "%HTTPS_PORT%"=="%HTTP_PORT%" (
    call :kill_port %HTTPS_PORT%
    if errorlevel 1 exit /b 1
)

echo [2/3] Building project...
dotnet build
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo [3/3] Starting app...
echo URLs: https://localhost:%HTTPS_PORT%;http://localhost:%HTTP_PORT%
dotnet run --no-build --urls "https://localhost:%HTTPS_PORT%;http://localhost:%HTTP_PORT%"
exit /b %errorlevel%

:kill_port
set "PORT=%~1"
set "FOUND=0"

for /f "tokens=5" %%P in ('netstat -ano ^| findstr /R /C:":%PORT% .*LISTENING"') do (
    set "FOUND=1"
    echo Killing PID %%P on port %PORT%...
    taskkill /PID %%P /F >nul 2>&1
    if errorlevel 1 (
        echo Failed to kill PID %%P on port %PORT%.
        exit /b 1
    )
)

if "%FOUND%"=="0" (
    echo Port %PORT% is free.
)

goto :eof
