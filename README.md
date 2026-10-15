# CRT Overlay

**English** · [Русский](README.ru.md)

Bring a CRT-inspired atmosphere to your games, films, anime, and retro video. CRT Overlay processes the Windows desktop in real time, adding scanlines, phosphor texture, glow, curved glass, and afterglow over the content you already use.

No game modification is required. Run a game or video player in a window or borderless mode, choose a preset, and tune the look in the live editor. The overlay passes mouse clicks through to the applications underneath.

**Version 1.0.1 · Windows x64 · MIT license**

## What it does

- Scanlines, RGB masks, brightness-dependent beam width, bloom, halation, convergence, focus, noise, and geometry controls.
- Soft, Balanced, Strong, and Rolling looks, plus PSX, Mega Drive, SNES, Master System, and Game Boy styles.
- A separate Philips A51EAL155X picture-tube profile using documented primaries, stripe pitch, and faceplate geometry.
- A live preset editor with immediate selection and slider preview, JSON import/export, and English/Russian UI.
- Tray controls, optional global shortcuts, cursor processing, and real-time persistence with integrated rolling exposure.

The goal is an enjoyable viewing atmosphere. This is an approximate display effect, not a console emulator or a calibrated replacement for a real CRT. Game Boy is an LCD-inspired style. [Read the model limits](docs/CRT-RESEARCH.md).

## Get started

Download the latest Windows x64 release ZIP from [GitHub Releases](https://github.com/Tomm1ck/CRT-Overlay/releases/latest). GitHub's **Code → Download ZIP** contains source code, not a ready-to-run application.

1. Install **.NET Runtime 9 x64** if it is not already installed. The SDK is not needed for the release ZIP; Runtime 10 alone does not replace Runtime 9.
2. Extract the entire ZIP into a writable folder. Keep `app`, `presets`, and the BAT files together.
3. Run `Start.bat` for Balanced, or `Editor.bat` to open the editor.
4. Choose a preset from the editor's list. With **Live preview** enabled, both the settings and the desktop update immediately. Adjust the sliders to taste.
5. Use **Apply** to keep the current settings for this session, or **Save as…** to save a JSON preset.
6. Exit with `Stop.bat`, the tray menu, or **Ctrl+Alt+Shift+Q** if that shortcut is available.

**Requirements:** Windows 10 version 2004/build 19041 or newer, or Windows 11; x64; a Direct3D 11 GPU with feature level 11.0, a working Desktop Duplication driver, and SDR output. One selected monitor is processed. Designed to handle 60, 59.94, and 144 Hz; actual presentation depends on Windows, the driver, and GPU load.

## Choose the mood

| Preset | Look |
|---|---|
| `Soft` | Subtle scanlines, mask, curvature, and glow |
| `Balanced` | Default starting point |
| `Strong` | More visible tube texture and geometry |
| `Rolling` | 60 Hz simulated scanning with average-brightness compensation; BFI off |
| `PSX` / `PSX-Composite` | 320×240, RGB555, PS1-style dithering; optional approximate composite filtering |
| `MegaDrive` / `MegaDrive-Composite` | 320×224, RGB333; optional approximate composite filtering |
| `SNES` | 256×224, RGB555 |
| `MasterSystem` | 256×192, RGB222 |
| `GameBoy` | 160×144, four green shades, LCD-inspired persistence |
| `Philips-A51EAL155X` | Documented tube profile with a fitted near-4:3 area |

`Startup` in the tray restores the settings from the current launch. It is not a CLI preset name.

The ready-made Philips preset is available in both the tray and the editor. To combine the tube with another style, first choose the style, then select **Physical tube profile → Picture tube → Philips A51EAL155X**. The component is a picture tube; no particular TV chassis is claimed.

Geometry and area scaling change the displayed picture but **do not remap Windows mouse coordinates**. Strong curvature or a smaller tube area can make the visible cursor/targets differ from actual click positions. These modes are best suited to viewing or keyboard/controller use.

## Controls and language

Double-click the tray icon to open the editor. All shortcuts use **Ctrl+Alt+Shift**:

| Key | Action |
|---|---|
| `Q` | Exit |
| `F11` | Toggle effect |
| `F10` | Next preset |
| `F5` | Reload shaders |
| `E` | Open editor |

If another app, such as G-Helper, owns a shortcut, only that shortcut is disabled for the session. CRT Overlay continues running; the action remains in the tray. Free the shortcut and restart to register it again.

The editor previews changes without saving them. **Apply** and **Save as…** update the baseline; **Cancel and close**, Escape, and the close button revert edits made since that baseline. Turning preview off shows the last applied settings. Loading a complete preset replaces image settings; choosing a tube alone keeps the other controls.

Switch English/Russian in the editor or tray. The preference lives in `%LOCALAPPDATA%\CrtOverlay\ui.json`, separate from image presets. CLI names and JSON keys stay the same in both languages.

## Launch examples

From the extracted folder or repository root:

```powershell
.\Start.bat --preset Soft
.\Start.bat --preset Philips-A51EAL155X
.\Start.bat --preset PSX --tube-profile philips-a51eal155x --tube-content stretch --curvature 0
.\Editor.bat
.\Stop.bat
```

Only one instance runs per user session. An ordinary second launch does not replace its settings: use the editor/tray, or stop it first. `--editor`, `--language`, and `--stop` can communicate with the running instance. `Start-PSX.bat`, `Start-Sega.bat`, `Start-SNES.bat`, `Start-MasterSystem.bat`, `Start-GameBoy.bat`, and `Start-Rolling.bat` stop and restart it with the named style.

## Limits to expect

- Use windowed/borderless content. Exclusive fullscreen and HDR output are not supported. Protected video may appear black.
- The GPU processes an already-rendered desktop. Console presets do not reproduce game logic, original GPU geometry, game palettes, or PS1 vertex wobble.
- Composite is approximate YIQ filtering, not a complete NTSC/PAL encoder/decoder.
- Phosphor decay, beam shape, focus, and light scattering are effective estimates. The Philips profile contains documented data and explicit assumptions, not a measured whole-TV calibration.
- On a 60 Hz LCD, an entire simulated 60 Hz scan can fall within one displayed frame. Integrated rolling exposure may have no visible moving band. It does not recreate a CRT's physical light pulse. BFI darkens selected output frames and reduces average brightness.
- Latency and frame rate depend on the system. G-Sync/VRR behavior is driver/compositor-dependent.
- Effects intentionally exclude their own windows from capture. If Windows cannot confirm this, startup stops to avoid capturing the overlay repeatedly.

## Troubleshooting

| Problem | What to do |
|---|---|
| App does not start | Check .NET Runtime 9 x64 and the log below |
| Shortcut does nothing | Check the tray; the log names conflicting combinations |
| Changes are not visible | Enable Live preview; use the latest EXE and restart after updating |
| Capture exclusion fails | Send the full log, Windows build, GPU/driver version, and whether the session is local or remote |
| Colors look wrong | Disable HDR in Windows |
| Protected video is black | This capture path does not bypass DRM |
| Need an ordinary cursor | Restart with `--cursor system` |
| No rolling band at 60 Hz | This can be normal for integrated full-frame exposure |

Log: `%LOCALAPPDATA%\CrtOverlay\crt.log`. It is replaced at each main-instance launch. Source paths such as `/_/CRT-Overlay/src/...` in a stack trace identify build-time source locations; they are not paths the application tries to open on your PC.

## Performance diagnostics

The renderer skips 16 glow-related GPU passes when **Bloom and Halation are both zero** (including the Game Boy preset). With either effect enabled, the existing five-level glow path is used. Frame deadlines use a Windows waitable timer instead of a busy-spin loop. Actual gains depend on your settings and hardware.

For an optional diagnostic log, start a new instance with:

```powershell
.\Start.bat --performance-log on
```

If the overlay is already running, exit it first using the tray or `Stop.bat`. The flag takes effect at startup and is not saved in presets. Every 10 seconds, `crt.log` records presentation/capture rates, average/maximum render wall time (including Present waits), normalized process CPU usage, and sampled GPU time for the CRT passes. GPU queries never wait for completion; unsupported or unavailable samples show `n/a`. These are diagnostic timings, not measurements of total GPU utilization or input latency. [Field definitions](docs/PARAMETERS.md#performance-log).

## Documentation and development

- [Parameters: CLI and JSON](docs/PARAMETERS.md)
- [Architecture and repository layout](docs/ARCHITECTURE.md)
- [Sources and physical-model limits](docs/CRT-RESEARCH.md)
- [Building and packaging](docs/BUILDING.md)
- [Contributing](CONTRIBUTING.md)
- [Release notes](CHANGELOG.md)

Build with .NET SDK **9.0.306** or a compatible 9.0.3xx patch:

```powershell
dotnet build
```

The build compiles C# and HLSL without launching the app. `Build.bat` stops an existing instance and publishes a runtime-dependent EXE to `app`; use it deliberately. The release packaging script never stops or runs the app.

## License and acknowledgements

[MIT](LICENSE). Dependency copyrights and license texts are preserved in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and `licenses/`. Manufacturer documents are linked as research sources, not redistributed. Product and console names identify visual styles or source hardware; this project is not affiliated with their manufacturers.
