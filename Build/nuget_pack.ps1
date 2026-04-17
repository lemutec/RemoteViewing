# Change to the script directory
Set-Location -Path $PSScriptRoot

# RemoteViewing
Set-Location -Path (Join-Path $PSScriptRoot '..\RemoteViewing')
dotnet restore
dotnet build -c Release
dotnet pack -c Release -o ../Build/

# RemoteViewing.Windows.Forms
Set-Location -Path (Join-Path $PSScriptRoot '..\RemoteViewing.Windows.Forms')
dotnet restore
dotnet build -c Release
dotnet pack -c Release -o ../Build/

# RemoteViewing.WPF
Set-Location -Path (Join-Path $PSScriptRoot '..\RemoteViewing.WPF')
dotnet restore
dotnet build -c Release
dotnet pack -c Release -o ../Build/

# RemoteViewing.Avalonia
Set-Location -Path (Join-Path $PSScriptRoot '..\RemoteViewing.Avalonia')
dotnet restore
dotnet build -c Release
dotnet pack -c Release -o ../Build/

# Return to the script directory
Set-Location -Path $PSScriptRoot

Pause # Keep the window open
