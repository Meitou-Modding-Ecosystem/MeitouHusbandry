param([string]$MeitouDir = $env:MEITOU_DIR)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'archive.ps1')
if (!$MeitouDir) { throw 'Set MEITOU_DIR to the extracted v0.22.1 Windows game folder.' }
$game = Get-Item (Join-Path $MeitouDir 'MeitouClient.exe')
if ($game.VersionInfo.ProductVersion -notmatch '^0\.22\.1(\+|$)') { throw "Expected Meitou v0.22.1, found $($game.VersionInfo.ProductVersion)" }
dotnet build (Join-Path $root 'framework/Meitou.Husbandry.csproj') -c Release --nologo "-p:MeitouDir=$($game.DirectoryName)"
if ($LASTEXITCODE -ne 0) { throw 'Framework build failed.' }
$files = @{}
foreach ($name in @('Meitou Husbandry.mod', 'Meitou.Husbandry.dll', 'maestro.json', 'meitou.json')) {
    $files['Meitou Husbandry/' + $name] = Join-Path $root ('mods/Meitou Husbandry/' + $name)
}
$files['Meitou Husbandry/LICENSE.txt'] = Join-Path $root 'LICENSE'
$files['Meitou Husbandry/LICENSING.md'] = Join-Path $root 'LICENSING.md'
Write-ModZip (Join-Path $root 'dist/Meitou Husbandry.zip') $files
$files = @{}
foreach ($name in @('Stoat Hitching Post.mod', 'husbandry.json', 'leveldata/zone.39.16.zone', 'LICENSE.txt', 'NOTICE.txt')) {
    $files['Stoat Hitching Post/' + $name] = Join-Path $root ('projects/Stoat Hitching Post/' + $name)
}
Write-ModZip (Join-Path $root 'dist/Stoat Hitching Post.zip') $files
Get-FileHash -LiteralPath (Join-Path $root 'dist/Meitou Husbandry.zip'), (Join-Path $root 'dist/Stoat Hitching Post.zip') -Algorithm SHA256 | Select-Object Hash,Path
