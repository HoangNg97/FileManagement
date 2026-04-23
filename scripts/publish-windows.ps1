$ErrorActionPreference = "Stop"

dotnet publish "$PSScriptRoot/../src/LauncherCatalog/LauncherCatalog.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o "$PSScriptRoot/../dist/LauncherCatalog-win-x64" `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true

Write-Host "Publish output:"
Write-Host "$PSScriptRoot/../dist/LauncherCatalog-win-x64"
