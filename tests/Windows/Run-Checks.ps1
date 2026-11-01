$ErrorActionPreference='Stop'
if ($env:GITHUB_ACTIONS -ne 'true') {
 & dotnet run --project tests/Windows/Orchester.UiSmoke.csproj -c Release --no-build
 exit $LASTEXITCODE
}
$fixture=Join-Path $env:RUNNER_TEMP ('orchester-signing-'+[guid]::NewGuid())
New-Item -ItemType Directory -Path $fixture | Out-Null
$cert=$null
try {
 $tool=Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' | Sort-Object FullName -Descending | Select-Object -First 1
 if(!$tool){throw 'Windows SDK signing tool is required for the signature test'}
 $cert=New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=ORCHESTER disposable CI fixture' -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddDays(1)
 $public=Join-Path $fixture 'publisher.cer'
 Export-Certificate -Cert $cert -FilePath $public | Out-Null
 Import-Certificate -FilePath $public -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
 $signed=Join-Path $fixture 'fixture.exe'
 Copy-Item 'src/Orchester.Helper/bin/Release/net10.0-windows/Orchester.Helper.exe' $signed
 & $tool.FullName sign /sha1 $cert.Thumbprint /s My /fd SHA256 /q $signed
 if($LASTEXITCODE -ne 0){throw 'Fixture signing failed'}
 $env:ORCHESTER_TEST_SIGNED_EXE=$signed
 $env:ORCHESTER_TEST_SIGNER_CERT=$public
 & dotnet run --project tests/Windows/Orchester.UiSmoke.csproj -c Release --no-build
 if($LASTEXITCODE -ne 0){throw 'Windows checks failed'}
} finally {
 Remove-Item Env:ORCHESTER_TEST_SIGNED_EXE -ErrorAction SilentlyContinue
 Remove-Item Env:ORCHESTER_TEST_SIGNER_CERT -ErrorAction SilentlyContinue
 if($cert){
  Remove-Item ('Cert:\CurrentUser\Root\'+$cert.Thumbprint) -Force -ErrorAction SilentlyContinue
  Remove-Item ('Cert:\CurrentUser\My\'+$cert.Thumbprint) -DeleteKey -Force -ErrorAction SilentlyContinue
 }
 Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
