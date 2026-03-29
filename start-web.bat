@echo off
title C# RPG Web Server
color 0B

echo.
echo  ========================================
echo    C# RPG Web Server + Cloudflare Tunnel
echo  ========================================
echo.

:: Check for dotnet
where dotnet >nul 2>&1
if errorlevel 1 (
    echo  ERROR: dotnet not found. Install .NET 8 SDK.
    pause
    exit /b 1
)

:: Kill any previously running RPGWeb instance so the DLL isn't locked during build
echo  Stopping any previous RPGWeb instance...
taskkill /FI "WINDOWTITLE eq RPG Web Server" /F >nul 2>&1
timeout /t 1 /nobreak >nul

:: Build first to catch errors before starting
echo  Building RPGWeb project...
dotnet build RPGWeb\RPGWeb.csproj --nologo -q
if errorlevel 1 (
    echo  Build failed - trying clean build...
    dotnet clean RPGWeb\RPGWeb.csproj --nologo -q >nul 2>&1
    dotnet build RPGWeb\RPGWeb.csproj --nologo -q
    if errorlevel 1 (
        echo.
        echo  BUILD FAILED. Fix errors above and try again.
        pause
        exit /b 1
    )
)
echo  Build OK.
echo.

:: Ask mode
echo  How do you want to run?
echo.
echo    1. Public  - LAN + Internet (Cloudflare tunnel)
echo    2. LAN     - Local network only
echo.
set /p MODE="  Choice [1/2]: "

if "%MODE%"=="2" goto :lan_only

:: ---- Public mode: start server then tunnel ----
echo.
echo  Starting web server on port 5100...
start "RPG Web Server" /min cmd /c "dotnet run --project RPGWeb --no-build 2>&1"

:: Wait for server to be ready
echo  Waiting for server...
timeout /t 4 /nobreak >nul

:: Check server is up
curl -s --max-time 3 http://localhost:5100/ >nul 2>&1
if errorlevel 1 (
    echo  WARNING: Server may not be ready yet. Continuing anyway...
) else (
    echo  Server is running.
)

echo.
echo  Starting Cloudflare tunnel...
echo  (The public URL will appear below - share it with players!)
echo.
echo  ------------------------------------------------
"%ProgramFiles(x86)%\cloudflared\cloudflared.exe" tunnel --url http://localhost:5100
echo  ------------------------------------------------
echo.
echo  Tunnel closed. Stopping server...
taskkill /FI "WINDOWTITLE eq RPG Web Server" >nul 2>&1
goto :done

:: ---- LAN mode ----
:lan_only
echo.
echo  Starting web server (LAN only)...
echo.

:: Show LAN IP
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4" ^| findstr /v "172."') do (
    set LANIP=%%a
)
if defined LANIP (
    echo  LAN access: http://%LANIP: =%:5100
)
echo  Local access: http://localhost:5100
echo.
echo  Press Ctrl+C to stop.
echo.
dotnet run --project RPGWeb --no-build

:done
echo.
echo  Server stopped.
pause
