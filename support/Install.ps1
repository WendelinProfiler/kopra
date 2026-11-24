param([string]$BuildDirectory=(Join-Path $PSScriptRoot '../build'))
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath($BuildDirectory)
if(!(Test-Path -LiteralPath (Join-Path $source 'Kopra.exe'))){throw 'Build the application first.'}
$target=Join-Path $env:LOCALAPPDATA 'Programs/Kopra'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$running=Get-CimInstance Win32_Process -Filter "Name='Kopra.exe'" | Where-Object ExecutablePath -eq (Join-Path $target 'Kopra.exe')
if($running){throw 'Exit Kopra from its tray menu before updating; clipboard copies may still be in progress.'}
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in '.exe','.dll' -or $_.Name -in 'Kopra.deps.json','Kopra.runtimeconfig.json' } | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $target -Force}
foreach($folder in @('de','en','runtimes','assets')){if(Test-Path -LiteralPath (Join-Path $source $folder)){Copy-Item -LiteralPath (Join-Path $source $folder) -Destination $target -Recurse -Force}}
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Kopra.lnk'))
$shortcut.TargetPath=Join-Path $target 'Kopra.exe'
$shortcut.WorkingDirectory=$target
$shortcut.IconLocation=(Join-Path $target 'Kopra.exe')+',0'
$shortcut.Description='Lokale Suche und Zwischenablageverlauf'
$shortcut.Save()
Start-Process -FilePath (Join-Path $target 'Kopra.exe') -ArgumentList '--background'
@{Installed=$true;Path=$target;RestartedWindows=$false}|ConvertTo-Json
