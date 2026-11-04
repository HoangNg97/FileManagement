$ErrorActionPreference = "Stop"

$output = [IO.Path]::GetFullPath("$PSScriptRoot/../dist/WorkSync-win-x64")

# Deleting files under a running copy would leave it half-removed.
$running = Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($output, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "WorkSync is running from $output. Quit it from the tray icon, then publish again." }

# Start clean so files from older publishes (e.g. a previous single-file build) don't linger.
if (Test-Path $output) { Remove-Item $output -Recurse -Force }

# Self-contained folder publish into app\, runnable without installing .NET.
dotnet publish "$PSScriptRoot/../src/LauncherCatalog/LauncherCatalog.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o "$output\app"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Root WorkSync.exe is a tiny launcher for app\WorkSync.exe, built with the csc.exe that ships with Windows.
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll `
  "/win32icon:$PSScriptRoot\..\src\LauncherCatalog\Assets\catalog_icon.ico" `
  "/out:$output\WorkSync.exe" "$PSScriptRoot\WorkSyncLauncher.cs"
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed with exit code $LASTEXITCODE" }

Write-Host "Publish output (copy the whole folder; run WorkSync.exe):"
Write-Host $output
