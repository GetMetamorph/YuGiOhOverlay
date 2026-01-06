@echo off
setlocal

set PROJECT=.\YuGiOhOverlay.UI\YuGiOhOverlay.UI.csproj
set RID=win-x64

dotnet restore %PROJECT%
dotnet publish %PROJECT% -c Release -r %RID% ^
  -p:SelfContained=true ^
  -p:PublishSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false

echo.
echo Publish done: YuGiOhOverlay.UI\bin\Release\net8.0-windows\%RID%\publish\
endlocal
