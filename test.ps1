$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if(!(Test-Path "$root\dist\DropShift.exe")){throw 'Run build.ps1 first'}
$output=Join-Path $root ('test-output\'+[Guid]::NewGuid().ToString('N'))
$process=Start-Process -FilePath "$root\dist\DropShift.exe" -ArgumentList @('--self-test',('"'+$output+'"')) -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath "$output\test-results.txt"
if($process.ExitCode -ne 0){throw 'Image safety self-test failed'}
