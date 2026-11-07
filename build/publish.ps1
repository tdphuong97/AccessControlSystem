# Publishes the version built in dist\ as a GitHub release; every copy from 0.9.0 on offers it as an update.
#   .\build.ps1 -Publish      build, then publish
#   .\build\publish.ps1       publish what is already built (for example after a failed upload)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$version = (Select-Xml -Path src\Acs.App\Acs.App.csproj -XPath '//Version').Node.InnerText
$setup = "dist\AccessControlSystem-Setup-$version.exe"
$probe = 'dist\AccessControlSystem-Probe.exe'
foreach ($f in $setup, $probe) { if (-not (Test-Path $f)) { throw "$f is missing: run .\build.ps1 first." } }
$repo = [regex]::Match((Get-Content src\Acs.Core\Update\Updater.cs -Raw), 'const string Repo = "([^"]+)"').Groups[1].Value
if (-not $repo -or $repo -like 'OWNER/*') { throw 'Set Updater.Repo in src\Acs.Core\Update\Updater.cs to the GitHub repository first.' }
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }
if (-not (Test-Path $gh)) { throw 'GitHub CLI is missing: winget install GitHub.cli, then gh auth login' }

# The notes are this version's section of CHANGELOG.md, one change per line.
$section = [regex]::Match((Get-Content CHANGELOG.md -Raw -Encoding utf8), '(?ms)^## ' + [regex]::Escape($version) + '\s*$(.*?)(?=^## |\z)').Groups[1].Value
$notes = (($section -split "`r?`n") | Where-Object { $_.Trim() }) -join "`n"
if (-not $notes) { throw "CHANGELOG.md has no section for $version." }

function Get-AssetInfo($file) {
    [ordered]@{
        url    = "https://github.com/$repo/releases/download/v$version/$(Split-Path $file -Leaf)"
        size   = (Get-Item $file).Length
        sha256 = (Get-FileHash $file -Algorithm SHA256).Hash
    }
}
$latest = [ordered]@{
    version = $version
    date    = (Get-Date -Format 'yyyy-MM-dd')
    notes   = $notes
    page    = "https://github.com/$repo/releases/tag/v$version"
    windows = Get-AssetInfo $setup
}
[IO.File]::WriteAllText((Join-Path (Get-Location) 'dist\latest.json'), ($latest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))

$body = @"
## Có gì mới
$notes

## Cài đặt
Lần đầu: tải **AccessControlSystem-Setup-$version.exe** ở mục Assets bên dưới, chạy rồi làm theo hướng dẫn trong README.
Đã cài bản 0.9.0 trở lên: không cần tải, phần mềm tự báo bản mới, bấm **Cập nhật ngay** là xong.

## Công cụ kiểm tra tại chỗ
**AccessControlSystem-Probe.exe** chạy một lần trước máy chấm công thật rồi gửi lại file báo cáo. Xem mục Probe trong README.

## Kiểm tra file
Bản chính thức chỉ có ở trang này. SHA-256 của AccessControlSystem-Setup-$version.exe:
``$($latest.windows.sha256)``
"@
# The notes go through a file: Windows PowerShell 5.1 splits an argument at embedded double quotes, so notes quoting
# a message ("Đã kết nối") reached gh as stray file names.
$notesFile = Join-Path ([IO.Path]::GetTempPath()) "acs-release-notes-$version.md"
[IO.File]::WriteAllText($notesFile, $body, [Text.UTF8Encoding]::new($false))
try {
    & $gh release create "v$version" $setup $probe 'dist\latest.json' --repo $repo --title "Access Control System $version" --notes-file $notesFile
    if ($LASTEXITCODE) { throw 'Publishing the release failed.' }
}
finally { Remove-Item $notesFile -ErrorAction SilentlyContinue }
"Published https://github.com/$repo/releases/tag/v$version"
