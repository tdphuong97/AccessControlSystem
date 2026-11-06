# Builds everything: tests, the app, the Probe, and the installer.
#   .\build.ps1                 full build, installer in dist\
#   .\build.ps1 -SkipTests      skip the tests
#   .\build.ps1 -SkipInstaller  stop after dist\app and the Probe
#   .\build.ps1 -Publish        also publish a GitHub release; every copy from 0.9.0 on offers it as an update
param([switch]$SkipTests, [switch]$SkipInstaller, [switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $SkipTests) {
    dotnet test AccessControlSystem.sln -c Release
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    if (Get-Command node -ErrorAction SilentlyContinue) {
        node --test --test-reporter=spec "src/Acs.App/ui/test/*.test.mjs"
        if ($LASTEXITCODE) { throw 'UI tests failed.' }
    }
}

# The app: self-contained single file; ui\** stays next to it as content.
$app = Join-Path $PSScriptRoot 'dist\app'
Remove-Item -Recurse -Force $app -ErrorAction SilentlyContinue
dotnet publish src\Acs.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $app
if ($LASTEXITCODE) { throw 'Publish failed.' }
Get-ChildItem $app -Recurse -Include *.pdb, *.xml | Remove-Item
"App: {0:N0} MB in {1}" -f ((Get-ChildItem $app -Recurse | Measure-Object Length -Sum).Sum / 1MB), $app

# The Probe: a separate single-file exe, a release asset the owner runs on site.
$probe = Join-Path $PSScriptRoot 'dist\probe'
Remove-Item -Recurse -Force $probe -ErrorAction SilentlyContinue
dotnet publish src\Acs.Probe -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $probe
if ($LASTEXITCODE) { throw 'Probe publish failed.' }
Copy-Item (Join-Path $probe 'AccessControlSystem-Probe.exe') (Join-Path $PSScriptRoot 'dist\AccessControlSystem-Probe.exe') -Force
Remove-Item -Recurse -Force $probe

if ($SkipInstaller) { return }
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is missing: winget install JRSoftware.InnoSetup' }
$env:ACS_VERSION = (Select-Xml -Path src\Acs.App\Acs.App.csproj -XPath '//Version').Node.InnerText
& $iscc /Q build\AccessControlSystem.iss
if ($LASTEXITCODE) { throw 'Installer build failed.' }
$version = $env:ACS_VERSION
Get-ChildItem "dist\AccessControlSystem-Setup-$version.exe", 'dist\AccessControlSystem-Probe.exe' |
    Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize

if ($Publish) { & .\build\publish.ps1 }
