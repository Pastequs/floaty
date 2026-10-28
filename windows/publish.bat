@echo off
cd /d "%~dp0"
taskkill /f /im Navbar.exe >nul 2>&1
dotnet publish src\Navbar.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist --nologo -v q
if errorlevel 1 (
    echo Echec de la publication.
    pause
    exit /b 1
)
echo.
echo Termine : %~dp0dist\Navbar.exe
pause
