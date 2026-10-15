# Building and packaging

**English** · [Русский](ru/BUILDING.md) · [README](../README.md)

## Prerequisites

Use Windows x64 with .NET SDK 9.0.306 or a later patch in the 9.0.3xx feature band (`global.json`). Restore needs access to NuGet. The build invokes Windows PowerShell and the operating system's `d3dcompiler_47.dll`; no C++ compiler, WPF, WinForms, Windows SDK shader compiler, or external `fxc` is required.

The project stays on `net9.0`, `WinExe`, and `win-x64`. Vortice packages are pinned to 3.8.3; Vortice.Mathematics uses its own version series (2.1.0). Keep `packages.lock.json` with dependency changes.

## Compile

From the repository root:

```powershell
dotnet build
```

This compiles C# and all configured HLSL variants. HLSL files are embedded and also compiled at runtime when the rendering pipeline is created.

## Publish locally

```powershell
dotnet publish crt.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o app
```

This produces `app/crt.exe` and requires .NET Runtime 9 x64 on the destination PC. If the EXE is in use, choose a separate output folder.

`Build.bat` deliberately stops the running overlay before publishing to `app`. Use the command above for publication without a stop request. Release stack traces map source paths to `/_/CRT-Overlay`, so they do not expose the build machine's user directory.

Single-file publication enables the SDK's ILLink tooling package even though trimming is not enabled. A normal build and a single-file publish can update that tooling entry in the lock file; the final release lock file is captured after publication.

## Create a release ZIP

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Package-Release.ps1
```

The script reads the version from `crt.csproj`, publishes to a fresh staging folder, and copies an explicit list of launchers, presets, profile data, documentation, and licenses. It does not alter the installed `app` folder. Output:

- `artifacts/releases/CRT-Overlay-v1.0.0-win-x64.zip`
- `artifacts/releases/CRT-Overlay-v1.0.0-win-x64.zip.sha256`

Existing output files are not overwritten. To build another copy, supply `-OutputDirectory artifacts/releases-review`. Staging is kept under `artifacts/release-staging` for inspection. Build products and old archives are ignored by Git.

The archive is runtime-dependent and unsigned. It includes `BUILD-INFO.json` with the application version, SDK, configuration, runtime requirements, and UTC build timestamp. It does not include a .NET installer, source checkout, personal paths, runtime logs, or user preferences. Preserve the release's folder layout when extracting it.

Upload the ZIP and SHA-256 file to a GitHub Release matching the version tag. Do not upload `app`, `bin`, `obj`, `.local`, or the entire `artifacts` tree to source control. Set the version in `crt.csproj` and update both READMEs and changelogs when preparing a later release.
