# Windows x64 preview packaging

```powershell
dotnet publish src/Orchester.App/Orchester.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/Orchester
dotnet publish src/Orchester.Helper/Orchester.Helper.csproj -c Release -r win-x64 --self-contained true -o artifacts/Orchester
Compress-Archive -Path artifacts/Orchester/* -DestinationPath artifacts/Orchester-win-x64-preview.zip
```

Keep WPF/resource/catalog files together. The self-contained portable folder requires no separate runtime installation. No Windows installer, production publisher certificate or update channel is supplied yet.

For production, sign the application and every elevated executable with an organization-controlled Authenticode certificate using Windows SDK `signtool`, SHA-256 file digest and a trusted RFC3161 timestamp. Verify signatures during packaging and authenticate release metadata with a separately pinned update key. Keep signing keys in protected CI signing infrastructure, never in the repository. Installer and portable production artifacts must pass the VM and physical hardware matrix before publication.


The app and `Orchester.Helper.exe` must stay in the same directory. The UI requests UAC only when the user chooses elevated diagnostics. The helper itself exposes read-only commands in this preview.

`Sign-Release.ps1` signs and verifies ORCHESTER executables using an installed certificate selected by thumbprint and an explicit HTTPS timestamp service. It never accepts or stores a certificate password/private key. Supply production keys through protected signing infrastructure.

`SignedRelease.Verify` accepts exact RSA-PSS/SHA-256 signed release metadata with schema/version/architecture/HTTPS URL/file SHA-256 and pinned Authenticode public-key SHA-256. It rejects downgrade metadata. `AuthenticatedUpdateDownload` stages the download and validates both its hash and Windows signature/publisher; it never launches the file. Provision the release-verification key independently; do not download the trust key with the update. A remote update service and update installation UI remain pending.

Windows CI can temporarily create a self-signed code-signing fixture in the disposable runner's CurrentUser stores to test positive signature verification, wrong-publisher rejection and file tampering. The wrapper removes the test certificate, key and fixture in `finally`; the test signer is never bundled into the product or trusted by product code.
