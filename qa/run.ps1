param([Parameter(Mandatory)][ValidateSet('core','seed','prepare','ride','two')][string]$Mode,
    [Parameter(Mandatory)][string]$MeitouDir, [Parameter(Mandatory)][string]$KenshiPath,
    [string]$CacheDirectory, [string]$SeedSave)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$game=(Get-Item (Join-Path $MeitouDir 'MeitouClient.exe'))
if ($game.VersionInfo.ProductVersion -notmatch '^0\.22\.1(\+|$)') {throw "Expected packaged v0.22.1, got $($game.VersionInfo.ProductVersion)"}
$trial=Join-Path $root ('trials/'+$Mode+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$null=New-Item -ItemType Directory -Path $trial
foreach($sub in @('install/mods','temp','control')) { $null=New-Item -ItemType Directory -Path (Join-Path $trial $sub) }
$install=Join-Path $trial 'install'
$null=New-Item -ItemType Junction -Path (Join-Path $install 'data') -Target (Join-Path $KenshiPath 'data')
foreach($name in @('resources.cfg','fcs.def','kenshi.cfg','settings.cfg','currentVersion.txt','controls.cfg','screens.cfg','__tutorials.data')){
    $p=Join-Path $KenshiPath $name
    if(Test-Path -LiteralPath $p){Copy-Item -LiteralPath $p -Destination $install}
}
$mods=@()
if($Mode -eq 'seed') {
    $mods+= 'Husbandry Field Visit'
    Copy-Item -LiteralPath (Join-Path $root 'qa/starter/Husbandry Field Visit') -Destination (Join-Path $install 'mods') -Recurse
} else {
    $mods+='Meitou Husbandry'
    if ($Mode -in 'prepare','ride','two') {$mods+='Stoat Hitching Post'}
    foreach($name in $mods) {Expand-Archive -LiteralPath (Join-Path $root ('dist/'+$name+'.zip')) -DestinationPath (Join-Path $install 'mods')}
}
if($Mode -eq 'two') {
    $mods+='Husbandry Second Post QA'
    Copy-Item -LiteralPath (Join-Path $root 'qa/fixtures/Husbandry Second Post QA') -Destination (Join-Path $install 'mods') -Recurse
}
$cfg=Join-Path $trial 'mods.cfg'
[IO.File]::WriteAllLines($cfg,[string[]]@($mods | ForEach-Object {$_+'.mod'}))
$childEnv=@{
    KENSHI_PATH=$install
    MEITOU_MODS_CFG=$cfg
    HITCH_TRIAL_MODE=$Mode
    HITCH_SAVE=(Join-Path $trial 'Two Garru at Stoat')
    DOTNET_STARTUP_HOOKS=(Resolve-Path (Join-Path $PSScriptRoot 'Live/bin/Release/net11.0/Hitch.Live.dll')).Path
    DOTNET_PROCESSOR_COUNT='4'
    TEMP=(Join-Path $trial 'temp')
    TMP=(Join-Path $trial 'temp')
}
if ($CacheDirectory) {
    $childEnv.MEITOU_IMPOSTOR_CACHE=Join-Path $CacheDirectory 'impostors'
    $childEnv.MEITOU_OBJECT_LOD_CACHE=Join-Path $CacheDirectory 'objlods'
    $childEnv.MEITOU_CHARACTER_LOD_CACHE=Join-Path $CacheDirectory 'charlods'
}
$start=switch($Mode) {
    'core' {'--new-game Wanderer'}
    'seed' {'--new-game "Husbandry Field Visit"'}
    {$_ -in 'prepare','ride','two'} {
        $slot=(Resolve-Path -LiteralPath $SeedSave).Path
        if (!(Test-Path -LiteralPath (Join-Path $slot 'quick.save'))) {throw 'Missing the portable field-visit save.'}
        '--load "'+$slot+'"'
    }
}
$control=Join-Path $trial 'control'
$opts='--headless --no-load-cache --no-precompile --no-foliage --no-gi --no-audio --water-reflection 0 --shadow-quality 0 --texture-quality 2 --fps-limit 24 --size 1104x720'
[IO.File]::WriteAllText((Join-Path $trial 'meitou.user.json'),'{"version":2,"fullScreen":false,"fpsLimit":24,"vSync":false,"splashScreens":false,"simThreads":4,"cameraLeash":false}')
$argsText=$start+' '+$opts+' --control-dir "'+$control+'" --mods-cfg "'+$cfg+'"'
$log=Join-Path $trial 'client.log'
Write-Output "Starting $Mode in $trial ($($game.VersionInfo.ProductVersion))"
$previous=@{}
try {
    foreach ($key in $childEnv.Keys) {
        $previous[$key]=[Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key,$childEnv[$key])
    }
    $p=Start-Process -FilePath $game.FullName -ArgumentList $argsText -WorkingDirectory $trial -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError (Join-Path $trial 'errors.log')
} finally {
    foreach ($key in $previous.Keys) {
        if ($null -eq $previous[$key]) {
            Remove-Item -LiteralPath ('Env:'+$key) -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable($key,$previous[$key])
        }
    }
}
try {
    $deadline=[datetime]::UtcNow.AddMinutes(10)
    while(!$p.HasExited -and [datetime]::UtcNow -lt $deadline) {
        if (Select-String -LiteralPath $log -Pattern '^HITCH QA (PASS|FAIL)' -Quiet) {break}
        Start-Sleep -Milliseconds 900
    }
    Get-Content -LiteralPath $log -Tail 8
    if (!(Select-String -LiteralPath $log -Pattern '^HITCH QA PASS' -Quiet)) {throw "Live check did not pass. See $trial"}
} finally {
    if(!$p.HasExited){
        [IO.File]::WriteAllText((Join-Path $control 'cmd'),"qa-end quit`n")
        if(!$p.WaitForExit(20000)) {$p.Kill(); $p.WaitForExit()}
    }
}
