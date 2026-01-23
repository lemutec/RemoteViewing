cd /d %~dp0
cd /d ..\RemoteViewing
dotnet restore
dotnet build -c Release
dotnet pack -c Release -o ../Build/
cd /d %~dp0
cd /d ..\RemoteViewing.Windows.Forms
dotnet restore
dotnet build -c Release
dotnet pack -c Release -o ../Build/
cd /d %~dp0
cd /d ..\RemoteViewing.WPF
dotnet restore
dotnet build -c Release
dotnet pack -c Release -o ../Build/
@pause
