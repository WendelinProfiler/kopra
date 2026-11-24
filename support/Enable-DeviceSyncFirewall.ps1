param([Parameter(Mandatory)][string]$InstalledExe,[Parameter(Mandatory)][string]$Result)
$ErrorActionPreference='Stop'
try{
 $path=[IO.Path]::GetFullPath($InstalledExe)
 if([IO.Path]::GetFileName($path) -ne 'Kopra.exe' -or !(Test-Path -LiteralPath $path)){throw 'Unexpected application path'}
 foreach($protocol in @('TCP','UDP')){
  $name='Kopra-Devices-'+$protocol
  $port=if($protocol -eq 'TCP'){53328}else{53329}
  if(Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue){Remove-NetFirewallRule -Name $name}
  New-NetFirewallRule -Name $name -DisplayName ('Kopra devices '+$protocol) -Direction Inbound -Action Allow -Profile Any -Protocol $protocol -LocalPort $port -Program $path -RemoteAddress LocalSubnet|Out-Null
 }
 @{Ok=$true;Scope='Kopra only, local subnet, TCP 53328 and UDP 53329';WindowsRestart=$false}|ConvertTo-Json|Set-Content -LiteralPath $Result
}catch{@{Ok=$false;Error=$_.Exception.Message}|ConvertTo-Json|Set-Content -LiteralPath $Result;throw}
