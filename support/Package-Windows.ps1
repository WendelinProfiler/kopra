$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$build=Join-Path $root 'build'
$artifacts=Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force|Out-Null
$stage=Join-Path $artifacts 'Kopra-Windows'
New-Item -ItemType Directory -Path $stage -Force|Out-Null
Get-ChildItem -LiteralPath $build -File|Where-Object {($_.Extension -in '.exe','.dll' -or $_.Name -in 'Kopra.deps.json','Kopra.runtimeconfig.json','ThirdPartyNotices.txt')}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $stage -Force}
foreach($folder in @('de','en','runtimes','assets')){if(Test-Path (Join-Path $build $folder)){Copy-Item -LiteralPath (Join-Path $build $folder) -Destination $stage -Recurse -Force}}
Copy-Item (Join-Path $root 'LICENSE'),(Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $stage -Force
$zip=Join-Path $artifacts 'Kopra-Windows-x64-preview.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($zip)|Set-Content (Join-Path $artifacts 'windows-SHA256SUMS.txt') -Encoding utf8NoBOM

$compiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
if(Test-Path -LiteralPath $compiler){
 & $compiler (Join-Path $PSScriptRoot 'Kopra.iss')
 if($LASTEXITCODE){throw 'Setup build failed'}
 Get-ChildItem -LiteralPath $artifacts -File|Where-Object Extension -in '.zip','.exe'|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name}|Set-Content (Join-Path $artifacts 'windows-SHA256SUMS.txt') -Encoding utf8NoBOM
}else{Write-Host 'Inno Setup 6 not installed; portable package is available.'}