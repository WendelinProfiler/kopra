param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
& $Dotnet publish (Join-Path $root 'Kopra.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $root 'build')
if($LASTEXITCODE){throw 'Build failed'}
$test=Start-Process -FilePath (Join-Path $root 'build/Kopra.exe') -ArgumentList '--self-test' -Wait -PassThru
Get-Content -LiteralPath (Join-Path $root 'build/self-test-result.txt')
if($test.ExitCode){throw 'Regression checks failed'}
