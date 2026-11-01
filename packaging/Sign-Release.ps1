[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$ArtifactDirectory,
 [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
 [Parameter(Mandatory=$true)][ValidatePattern('^https://')][string]$TimestampUrl
)
$ErrorActionPreference='Stop'
$signer=Get-Command signtool.exe -ErrorAction Stop
$files=Get-ChildItem -LiteralPath $ArtifactDirectory -Filter 'Orchester*.exe' -File
if(!$files){throw 'No ORCHESTER executables found'}
foreach($file in $files){
 & $signer.Source sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $file.FullName
 if($LASTEXITCODE -ne 0){throw 'Authenticode signing failed'}
 & $signer.Source verify /pa /all /v $file.FullName
 if($LASTEXITCODE -ne 0){throw 'Authenticode verification failed'}
}
