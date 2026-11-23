param([string]$FixtureRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (!$FixtureRoot) { $FixtureRoot = Join-Path $root 'saves' }
$FixtureRoot = (Resolve-Path -LiteralPath $FixtureRoot).Path
. (Join-Path $PSScriptRoot 'archive.ps1')
$version = '0.1.0-playtest.2'
$name = 'Meitou-Husbandry-' + $version
$source = Join-Path $root ('dist/' + $name + '-source.zip')
git -C $root archive --format=zip --prefix=MeitouHusbandry/ "--output=$source" HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
$files = @{}
foreach ($mod in @('Meitou Husbandry.zip', 'Stoat Hitching Post.zip')) {
    $files[$name + '/mods/' + $mod] = Join-Path $root ('dist/' + $mod)
}
$files[$name + '/source/' + [IO.Path]::GetFileName($source)] = $source
foreach ($guide in @('README.md', 'AUTHORING.md', 'CONTRIBUTING.md', 'PLAYTEST.md', 'RESULTS.md', 'LICENSING.md', 'LICENSE', 'qa/README.md')) {
    $files[$name + '/' + $guide] = Join-Path $root $guide
}
$files[$name + '/START-HERE.md'] = Join-Path $root 'PLAYTEST.md'
foreach ($folder in @('projects', 'evidence', 'LICENSES')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File -Recurse) {
        $rel = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        $files[$name + '/' + $rel] = $file.FullName
    }
}
foreach ($save in @('Two Garru outside Stoat', 'First Garru assigned')) {
    $folder = Join-Path $FixtureRoot $save
    if (!(Test-Path -LiteralPath (Join-Path $folder 'quick.save'))) { throw "Missing fixture save: $save" }
    foreach ($file in Get-ChildItem -LiteralPath $folder -File -Recurse) {
        $rel = [IO.Path]::GetRelativePath($FixtureRoot, $file.FullName).Replace('\', '/')
        $files[$name + '/saves/' + $rel] = $file.FullName
    }
}
$kit = Join-Path $root ('dist/' + $name + '-tester-kit.zip')
Write-ModZip $kit $files
$artifacts = @('Meitou Husbandry.zip', 'Stoat Hitching Post.zip', [IO.Path]::GetFileName($source), [IO.Path]::GetFileName($kit))
$lines = foreach ($artifact in $artifacts) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $root ('dist/' + $artifact)) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $artifact"
}
[IO.File]::WriteAllLines((Join-Path $root 'dist/SHA256SUMS.txt'), [string[]]$lines)
$lines
