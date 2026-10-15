# Architecture

**English** · [Русский](ru/ARCHITECTURE.md) · [Home](../README.md)

## Repository layout

| Path | Purpose |
|---|---|
| `src/` | C# application, Win32 integration, rendering, capture, editor, localization |
| `Shaders/` | HLSL passes and shared constant buffers |
| `Assets/` | Application icons |
| `Locales/` | Embedded English translation catalog; Russian strings are fallback keys |
| `Profiles/` | Versioned physical component data, sources, units, and assumptions |
| `presets/` | User-editable JSON equivalents of built-in looks |
| `scripts/` | HLSL compilation, icon generation, release packaging |
| `docs/`, `docs/ru/` | English and Russian reference documentation |
| `licenses/` | Upstream dependency license texts |
| `.github/ISSUE_TEMPLATE/` | Bug-report and feature-request forms |
| Root BAT files | Stable launch, editor, stop, and developer-build entry points |
| `crt.csproj`, `global.json`, `packages.lock.json` | Project, SDK policy, dependency resolution |

`app/`, `bin/`, `obj/`, and `artifacts/` contain build outputs and are excluded from source control. The project explicitly compiles only `src/**/*.cs`, so packaging folders cannot accidentally become compilation inputs.

Key classes: `Program` owns startup, single-instance IPC, the render/message loop, recovery and logs; `OverlayWindow` owns the click-through HWND; `CaptureExclusion` verifies capture exclusion; `Renderer` owns DXGI/D3D11/DirectComposition; `ScreenCapture` owns Desktop Duplication; `CursorOverlay` handles cursor shapes and recovery; `CrtPipeline` owns passes and GPU resources; `CrtSettings` defines CLI/JSON/defaults; `TubeProfile` loads embedded component descriptions; `PresetEditor` owns the STA editor thread. `PipelineSelfTest` provides the optional GPU shader diagnostic.

## Window, presentation, and shortcuts

The Win32 popup uses TOPMOST, TOOLWINDOW, LAYERED, TRANSPARENT, and NOACTIVATE. Initialize layered attributes while hidden, then set and verify `WDA_EXCLUDEFROMCAPTURE` before showing it. Failure stops startup: falling back to a captured black rectangle cannot supply the underlying desktop. The editor is excluded and verified separately.

DirectComposition uses a FlipSequential swap chain with two B8G8R8A8 UNorm buffers, premultiplied alpha, and a maximum frame latency of one. Final shader alpha is 1: outside the rendered tube is black, not transparent. Capture coordinates account for monitor rotation. One output is selected by DXGI index, which is not a stable physical display ID.

Hotkeys register independently. Error 1409 disables only the occupied combination and logs its readable name; other registration errors preserve their system code and stop startup. Tray actions and `Stop.bat` remain available. Only successfully registered hotkeys are unregistered.

## Capture and cursor

`AcquireNextFrame` uses timeout 0. A timeout reuses the last GPU texture, but still runs the effects. A successful frame is copied GPU-to-GPU and released in `finally`. The capture path uses a borrowed COM wrapper through the stable ABI to avoid a managed resource wrapper on each frame. No desktop pixels are read back to the CPU.

Cursor metadata and shape data are the exception: they are prepared on the CPU and uploaded. Color, monochrome, and masked-color shapes are supported, with a Win32 fallback. AND/XOR operates on original encoded bytes; ordinary alpha blending uses linear light. Cursor composition precedes console/CRT effects. The editor temporarily uses the system cursor. A guard process restores cursor visibility after an abnormal main-process exit.

## GPU passes

Intermediate surfaces are FP16 (R16G16B16A16 Float). Fullscreen passes share `Common.hlsl` and a fullscreen triangle.

| Pass | Work |
|---|---|
| Input | Capture rotation, sRGB decode, cursor composition |
| Retro (optional) | Low-resolution framebuffer, console color quantization and dithering |
| Colorize | Tube-content placement, approximate composite, gamma, primary matrix, saturation, P4, noise/flicker |
| Beam | Horizontal filtering, five-row Gaussian beam integration, legacy RGB mask |
| Persistence / RollingDisplay | Instantaneous ping-pong history, optional separate integrated exposure |
| Downsample / Blur | Bright-area threshold and five levels of separable bloom |
| Glow | Multiscale bloom and broad halation |
| Geometry | Physical faceplate projection/stripes or legacy path, artistic curvature, convergence, focus, overscan, corners |
| Final | Vignette/reflection, luminance scale and user brightness, BFI, output encoding/dither |

When Bloom and Halation are both zero, the pyramid and Glow mix are bypassed: Geometry consumes the temporal output directly, removing 16 draws. Temporal passes still run on every rendered frame. The surfaces are retained to allow immediate editor toggling without resource allocation. With either glow effect enabled, the original five levels and mix are unchanged.

Bloom levels are separate textures from 1/2 to 1/32, not mip levels of a single texture. Scatter kernels are approximations. SRVs are unbound between passes to avoid simultaneous input/output binding.

## Physical profiles

`legacy` keeps the original path. `philips-a51eal155x` loads an embedded description lazily once. Applying settings computes its color matrix, output area, source placement, and constant-buffer coefficients. No profile file access, shader compilation, or profile-surface allocation occurs in steady rendering. The six extra Vector4/float4 fields match between C# and HLSL.

Profile schema 1 contains `Id`, `Component`, nullable `Chassis`, `Sources`, `Parameters`, and `Assumptions`. Each parameter stores a numeric array or null `Value`, `Units`, `Origin` (`documented`, `measured`, `estimated`, `unknown`), source ID or null, printed `Pages`, `Conditions`, and an explicit `Fallback` for unknown data. The loader rejects unsupported versions/fields and checks used array sizes, units, finite values, and selected physical constraints. Arbitrary external profile files are not a CLI feature.

The intermediate raster spans normalized tube coordinates. Content fitting happens before beam/history. Primaries are normalized to the reference or user white and converted to linear sRGB. Output gamut and clipping remain limits. Physical projection assumes an axial pinhole observer:

```text
projectedXY = surfaceXY * D / (D + sag(surfaceXY))
```

Geometry inverts this using eight fixed-point iterations. The outer faceplate approximates the emitting surface, with no refraction/thickness model; deflection is ideal and uniformly maps raster to X/Y. Artistic curvature is additional. The outline is an approximate rounded rectangle.

Physical stripes are evaluated after projection, glow, and focus, using physical pitch and final pixel derivatives. Up to six Fourier harmonics approximate rectangular stripes; sinc pixel integration and a fade between 0.35 and 0.5 cycles/pixel suppress unresolved detail. Fill and gaps are estimates; no invented vertical slot map is used. Stripes stay attached to the surface independently of overscan. This order avoids resampling an already-rasterized fine mask, but its microstructure does not participate in scattering or history.

Reference luminance divides documented after-glass luminance by user-supplied display white. Transmission remains metadata to avoid double attenuation. User brightness is independent. A tube profile does not replace decay constants or rolling/BFI timing.

## Time and resource lifetime

`FrameWaiter` uses a relative, auto-reset [Windows waitable timer](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw) for the next frame deadline. It first requests high resolution, then falls back to a standard timer or rounded-up message timeout if unavailable. `MsgWaitForMultipleObjectsEx` wakes for the timer, the stop event, or queued UI messages. No spin-wait tail remains. The existing 1 ms timer-period request remains for fallback waits; actual scheduling still depends on Windows and the driver.

`Stopwatch` supplies real elapsed time and delta time. Ordinary persistence is `max(current, previous * exp(-dt/tau))`. Rolling integrates exponential impulses over the complete frame interval. History holds instantaneous light at the end; exposure holds average visible light. Feeding exposure back into history would change the model and reintroduce phase/brightness errors. Interlace doubles each row's excitation interval. BFI gates output frames independently. Physical panel phase, DWM latency, and VRR timing are not measured.

History resets on settings changes, successful shader replacement, or resume. The rational DXGI refresh rate is chosen when the renderer is created, with a 60 Hz fallback. Display/DPI changes and recoverable capture/device failures hide the overlay, restore the cursor, release graphics resources, and retry. Capture recovery includes ACCESS_LOST, device removal/reset/hang, session disconnect, and access denied during lock/UAC. Disposal unbinds composition/context resources and frees all views, textures, shaders and devices.

Steady rendering reuses surfaces and buffers; settings upload only when changed, time/cursor data each frame. UI changes, cursor-shape changes and recovery can allocate. No blanket zero-allocation or performance measurement is claimed.

## Editor, JSON, and localization

The editor has its own STA thread. It holds a baseline and draft and publishes validated snapshots through a pending slot; the render thread applies them between frames. Choosing a preset (`CBN_SELCHANGE`) immediately replaces the draft and previews it. Trackbar drag notifications use the supplied position; other movements use `TBM_GETPOS`. The active slider is not repositioned inside its own drag notification.

Apply/Save update the baseline; cancel/close restores it. Editor saves use a temporary file and replacement; CLI saves write directly. External tray preset changes synchronize the draft and discard stale pending edits. Runtime settings stay fixed through `KeepRuntime`. The schema rejects unknown fields. Missing preset version means 1, versions 1/2 are accepted, and saves use 2.

Seven editor groups share option ranges with CLI/JSON. New controls need a caption, group, English translation, validation, and GPU binding. Russian strings are translation keys/fallbacks; `Locales/en.json` is embedded without satellite assemblies. `L.Changed` posts a UI-thread notification. Language preferences never enter a graphics preset.

## Build and diagnostics

C# compilation invokes `scripts/CompileShaders.ps1`, using the Windows D3DCompiler DLL without a C++ compiler, D3D device, or application launch. Runtime reload only replaces the old shader set when all variants compile. New passes require synchronized pass IDs, resource lifetimes, bindings, and compile-script variants. GPU structures use Vector4/float4 packing, not C# bool fields.

Release PathMap replaces the source root with `/_/CRT-Overlay`; Debug retains local paths. Startup logs include build MVID, OS/.NET/architecture, DWM state, remote-session indication, and exact capture-exclusion errors. This logging occurs during initialization, not in steady rendering.

`PerformanceLog` is created only with `--performance-log on`. It samples CRT GPU time on one frame in sixteen using four sets of disjoint/start/end queries. `GetData(DoNotFlush)` is polled on later frames; pending slots are skipped rather than reused or waited on, and disjoint samples are discarded. GPU timing failure disables only this optional measurement. CPU/render/rate counters are reported every ten wall seconds. Diagnostics own their queries for the renderer lifetime and are reset on recovery. See [field definitions](PARAMETERS.md#performance-log). The existing timed diagnostic's allocation counters are only read with `TestSeconds > 0`.
