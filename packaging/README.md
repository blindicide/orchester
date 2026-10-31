# Windows x64 preview packaging

```powershell
dotnet publish src/Orchester.App/Orchester.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/Orchester
Compress-Archive -Path artifacts/Orchester/* -DestinationPath artifacts/Orchester-win-x64-preview.zip
```

Keep WPF/resource/catalog files together. The self-contained portable folder requires no separate runtime installation. No Windows installer, production publisher certificate or update channel is supplied yet.

For production, sign the application and every elevated executable with an organization-controlled Authenticode certificate using Windows SDK `signtool`, SHA-256 file digest and a trusted RFC3161 timestamp. Verify signatures during packaging and authenticate release metadata with a separately pinned update key. Keep signing keys in protected CI signing infrastructure, never in the repository. Installer and portable production artifacts must pass the VM and physical hardware matrix before publication.
