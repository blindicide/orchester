# Windows packaging

## Portable folder

```powershell
dotnet publish src/Orchester.App -c Release -r win-x64 --self-contained true -o artifacts/Orchester
dotnet publish src/Orchester.Helper -c Release -r win-x64 --self-contained true -o artifacts/Orchester
Compress-Archive -Path artifacts/Orchester/* -DestinationPath artifacts/Orchester-win-x64.zip
```

Keep application, helper, resources and runtime together. No separate .NET installation is required. The UI starts unelevated; explicit native and diagnostic actions invoke the helper through UAC.

## Installer

Install Inno Setup 6 on Windows, publish the portable folder above, then run:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' packaging/Orchester.iss
```

The English/Russian x64 installer targets Windows 10 22H2 or later, installs in Program Files and offers an optional desktop shortcut. It installs no persistent service. Uninstall refuses active preparation and does not delete installed Linux systems, retained journals or downloaded images.

The manual **Build** workflow exposes `windows_build` and `package_installer`. Select both for an operator-requested Windows setup build. Automatic CI only cross-publishes the portable folder on Linux. Existing Windows validation runs only when explicitly selected.

## Signing

`Sign-Release.ps1` signs/verifies ORCHESTER binaries and setup using an installed certificate thumbprint and explicit HTTPS timestamp service. No signing certificate or private key is supplied. Sign app/helper assemblies and executables before setup compilation, then sign the setup executable. Keep keys outside the repository.

Signed helpers verify matching GUI publisher identity. Production distribution requires a provisioned trusted Authenticode identity; unsigned branch artifacts are development distribution. Authenticated update services also need separately pinned publisher keys and hosted metadata. The verification library does not automatically launch an update; update installation UI is not supplied.
