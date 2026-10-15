# Parameters

**English** · [Русский](ru/PARAMETERS.md) · [Home](../README.md)

Defaults below are for Balanced / `new CrtSettings()`. Other presets override selected values. Ranges include both endpoints. The source of truth is `src/CrtSettings.cs`.

## Syntax and precedence

Use separate arguments: `--brightness 1.1`, not `--brightness=1.1`. CLI numbers use a decimal point. Vector values such as `--convergence-r "0.4,0.0"` use a comma between X and Y. Editor numeric fields accept a decimal point or comma.

Boolean options accept `on/off`, `true/false`, or `1/0`; omitting a boolean value means `on`. JSON uses native booleans, numbers, and strings. JSON keys are case-insensitive property names from the tables. Missing fields use defaults; unknown fields, null values, non-finite numbers, and invalid ranges are rejected. Comments and trailing commas are not supported.

The last `--preset` supplies the base, then all explicit CLI overrides are applied in order. `--curvature` sets both axes; later axis-specific arguments can override it. Saving writes a complete settings object, including runtime settings, but never the UI language.

Preset `SchemaVersion` is 1 when absent; versions 1 and 2 are accepted. Saving writes version 2. Unsupported versions are rejected before strict field parsing. Old presets retain `TubeProfile=legacy`. Embedded tube descriptions use a separate schema version 1.

## Commands

| Command | Meaning |
|---|---|
| `--help` | Show help and exit |
| `--preset NAME_OR_FILE` | Load a built-in name or JSON file |
| `--save-preset [FILE]` | Save final settings (default `preset.json`), then continue normal startup |
| `--editor` | Open the editor, including in an existing instance |
| `--language ru\|en` | Set and remember UI language, including in an existing instance |
| `--stop` | Exit the running instance; use as a separate command |
| `--curvature N` | Set both curvature axes, 0–0.4; no separate JSON property |
| `--self-test` | Run the GPU shader diagnostic and exit |

`--cursor-guard` is an internal recovery-process argument.

## Runtime and presentation

| CLI | JSON | Default | Allowed | Meaning |
|---|---|---|---|---|
| `--monitor` | `Monitor` | 0 | integer 0–31 | DXGI output index, not necessarily the Windows display number |
| `--present` | `Present` | `vsync` | `vsync`, `immediate`, `tearing` | Presentation mode |
| `--sync-interval` | `SyncInterval` | 1 | integer 0–4 | vsync requires 1–4; other modes require 0 |
| `--fps` | `Fps` | 0 | 0 or integer 30–1000 | 0 uses the DXGI refresh rate; otherwise a render limit |
| `--cursor` | `Cursor` | `capture` | `capture`, `system` | Process cursor through effects or leave it to Windows |
| `--test-seconds` | `TestSeconds` | 0 | integer 0–3600 | 0 runs normally; positive values exit after the interval and log diagnostics |
| `--performance-log` | — (CLI only) | false | boolean | Optional 10-second performance log; see below |
| `--debug-layer` | `DebugLayer` | false | boolean | Requires the D3D11 Debug Layer |

Explicit CLI `--present` without `--sync-interval` chooses a matching interval. JSON does not infer this. A missing monitor is not silently replaced by another. The renderer uses rational refresh rates such as 60000/1001, falling back to 60 Hz if unknown. Present, DWM, and GPU load also constrain output.

Live edits and preset changes preserve `Monitor`, `Present`, `SyncInterval`, `Fps`, `TestSeconds`, `DebugLayer`, and `Cursor`. `PerformanceLog` is also kept at runtime but is never serialized. Set them at launch.

## Performance log

Start with `--performance-log on` to enable optional diagnostics without automatic shutdown. The flag is CLI-only (`JsonIgnore`), defaults to off, survives editor/tray changes, and does not add fields to schema 2 presets. Existing schema 1 and 2 presets remain compatible. The editor does not expose runtime launch options.

Once per 10-second wall interval, `%LOCALAPPDATA%\CrtOverlay\crt.log` receives:

| Field | Meaning |
|---|---|
| `interval-s` | Actual reporting interval; includes any pause between reports |
| `present-fps`, `capture-fps` | Successful Present calls and fresh desktop acquisitions per wall second; not the game's internal frame rate |
| `render-wall-ms`, `render-wall-max-ms` | Mean and maximum wall time in capture, cursor update, CRT submission and Present, including blocking waits; not CPU execution time or input latency |
| `gpu-effects-ms`, `gpu-max-ms`, `gpu-samples` | Mean/max/sample count for GPU CRT passes, excluding capture, cursor preparation and Present. One frame in sixteen is sampled with four query sets; busy sets are skipped |
| `cpu-process` | CPU time consumed by the whole process divided by wall time and logical processor count; 100% means all logical processors busy |

GPU results are polled with `DoNotFlush`, never waited on. Samples with discontinuous GPU timestamps are discarded. With no usable result, the GPU field is `n/a`; a query failure disables GPU timing for that renderer without disabling the effect. Renderer recovery creates new counters. A report can span different settings or pauses, so keep content, preset, display and refresh rate fixed for meaningful comparisons. Logging and sampling add some overhead and are off by default.

## Physical tube profile

Choose **Advanced settings**, then **Physical tube profile** in the editor to find these controls. They do not change the console style. Except for the profile selector itself, they only affect an active physical profile.

| CLI | JSON | Default | Allowed | Meaning |
|---|---|---|---|---|
| `--tube-profile` | `TubeProfile` | `legacy` | `legacy`, `philips-a51eal155x` | Original artistic path or documented tube |
| `--tube-area` | `TubeArea` | `fit` | `fit`, `stretch` | Preserve 411.2:308.9 tube aspect or deliberately stretch |
| `--tube-scale` | `TubeScale` | 1 | 0.25–1 | Scale both dimensions of the selected tube area |
| `--tube-content` | `TubeContent` | `fit` | `fit`, `crop`, `stretch` | Desktop placement within the tube raster |
| `--view-distance-mm` | `ViewDistanceMm` | 1000 | 300–3000 | Axial viewer distance from the central glass apex, mm |
| `--tube-white` | `TubeWhite` | `reference` | `reference`, `temperature` | Datasheet measurement white or user temperature |
| `--tube-luminance` | `TubeLuminance` | `relative` | `relative`, `reference` | Normal brightness or extra factor 79 / display white |
| `--display-white-nits` | `DisplayWhiteNits` | 120 | 40–1000 | User-supplied output display white, cd/m²; reference mode only |

At 1920×1080, `fit` gives a roughly 1438×1080 tube area before perspective. `tube-scale=0.5` halves both dimensions. Content placement uses desktop aspect, not analysis of game borders or the original console's pixel aspect. `tube-area=stretch` deliberately distorts both the tube and its content. Windows click positions are not remapped.

Philips overrides `gamut`, `mask`, and `mask-scale` while retaining their stored values for legacy mode. Use `mask-strength=0` to disable its stripes; P4 also disables them. Mask softness/compensation, saturation, gamma, glow, focus, convergence, and artistic curvature remain adjustments. Use `--curvature 0` for the surface projection without extra artistic warping. Corner shape is approximate.

Reference white is x=0.313, y=0.329, from the luminance measurement conditions. Reference luminance uses 79 cd/m², already measured after the glass; glass transmission 0.41 is separate metadata and is not multiplied again. `brightness` is independent. The app does not measure your monitor. Effects, gamma, changed white, and SDR clipping prevent guaranteed absolute photometric matching. Decay, spot, and scattering remain estimates using the controls below.

## Geometry and focus

| CLI | JSON | Default | Range | Meaning |
|---|---|---|---|---|
| `--curvature-x` | `CurvatureX` | 0.08 | 0–0.4 | Artistic X warp |
| `--curvature-y` | `CurvatureY` | 0.08 | 0–0.4 | Artistic Y warp |
| `--corner-radius` | `CornerRadius` | 0.03 | 0–0.25 | Rounded corners in normalized image coordinates |
| `--overscan` | `Overscan` | 0 | 0–0.15 | Shrink the sampled UV span to crop edges |
| `--convergence-r` | `ConvergenceR` | `"0.4,0.0"` | X/Y −10…10 | Red shift relative to green, pixel units, stronger at edges |
| `--convergence-b` | `ConvergenceB` | `"-0.4,0.0"` | X/Y −10…10 | Blue shift relative to green |
| `--focus` | `Focus` | 0.45 | 0–4 | Edge blur sample radius scale |

`overscan=0.1` reduces the sampled span by 10% in total, not 10% on each side. Curvature coefficients are artistic, not millimeters or measured deflection errors. In a physical profile they add to the documented surface projection. Focus/convergence operate in the intermediate raster; physical-area scaling changes their final pixel footprint.

## Beam and mask

| CLI | JSON | Default | Allowed | Meaning |
|---|---|---|---|---|
| `--mask` | `Mask` | `aperture` | `none`, `aperture`, `shadow`, `slot` | Artistic mask type in legacy mode |
| `--mask-strength` | `MaskStrength` | 0.35 | 0–1 | Mask blend amount |
| `--mask-scale` | `MaskScale` | 1 | 0.5–8 | Legacy RGB period is approximately 3 × scale pixels before geometry |
| `--soft-mask` | `SoftMask` | 0.65 | 0–1 | Blend fine mask detail toward its average |
| `--mask-compensation` | `MaskCompensation` | true | boolean | Normalize average mask brightness |
| `--scanline-strength` | `ScanlineStrength` | 0.5 | 0–1 | Blend toward the beam model |
| `--scanlines` | `Scanlines` | 540 | integer 180–2160 | Virtual raster rows, not horizontal TVL |
| `--beam-min` | `BeamMin` | 0.5 | 0.2–2 | Minimum beam-width parameter |
| `--beam-max` | `BeamMax` | 1.4 | 0.2–3 | Maximum beam-width parameter |
| `--horizontal-blur` | `HorizontalBlur` | 0.35 | 0–3 | Horizontal sampling offset in intermediate pixels |

`beam-min <= beam-max` is required. Gaussian sigma is `0.4 × lerp(beam-min, beam-max, sqrt(saturate(luminance)))` in virtual-row spacings, not a measured spot diameter. Legacy masks are applied before geometry. Physical stripes are evaluated in Geometry with the final projected pixel footprint and do not move with overscan. P4 disables color masks.

## Color and glow

| CLI | JSON | Default | Allowed | Meaning |
|---|---|---|---|---|
| `--gamut` | `Gamut` | `SMPTE-C` | `P22`, `SMPTE-C`, `EBU` | Legacy primary coordinates |
| `--temperature` | `Temperature` | 6500 | 4000–12000 | Approximate daylight white temperature, K |
| `--gamma` | `Gamma` | 2.4 | 1–3.5 | Artistic CRT response adjustment |
| `--output-gamma` | `OutputGamma` | 2.2 | 1–3.5 | Output encoding adjustment |
| `--brightness` | `Brightness` | 1 | 0.1–3 | Final linear-light multiplier |
| `--saturation` | `Saturation` | 1 | 0–2 | 0 is luminance only, 1 is unchanged |
| `--bloom` | `Bloom` | 0.2 | 0–2 | Bright-area multiscale glow |
| `--halation` | `Halation` | 0.1 | 0–1 | Broad, slightly warm glow |

Input sRGB uses the piecewise transfer function. `gamma` then applies a `gamma / 2.2` exponent to linear values. Output uses `2.2 / output-gamma` before sRGB encoding; this is an artistic control, not an end-to-end calibrated gun curve. Matrix output and SDR presentation may clip out-of-gamut/bright values.

## Time and persistence

| CLI | JSON | Default | Allowed | Meaning |
|---|---|---|---|---|
| `--persistence` | `Persistence` | 0.35 | 0–1 | Multiplier on base decay times |
| `--phosphor` | `Phosphor` | `P22` | `P22`, `P4`, `custom` | Effective decay preset; P4 is monochrome |
| `--decay-r` | `DecayR` | 6 | 0.001–200 | Custom red base tau, ms |
| `--decay-g` | `DecayG` | 4 | 0.001–200 | Custom green base tau, ms |
| `--decay-b` | `DecayB` | 2 | 0.001–200 | Custom blue base tau, ms |
| `--rolling-scan` | `RollingScan` | false | boolean | Impulse excitation with integrated exposure |
| `--rolling-compensation` | `RollingCompensation` | true | boolean | Preserve average rolling brightness |
| `--scan-hz` | `ScanHz` | 60 | 24–240 | Simulated scan/field rate |
| `--bfi` | `Bfi` | 0 | 0–0.9 | Fraction of output frames darkened |
| `--interlace` | `Interlace` | false | boolean | Alternate virtual row parity |

P22 uses 6/4/2 ms and P4 uses 24 ms for every channel. These are effective artistic settings, not measurements of Philips or all phosphors with those names. `tau_effective = tau_base × persistence`; use `custom` and persistence 1 to specify tau directly.

Ordinary persistence uses `max(current, previous × exp(-dt/tau))`; zero disables its tail. Rolling keeps instantaneous end-of-frame history separate from average exposure, integrates real elapsed time, and applies a 0.01 ms numerical tau floor. Compensation normalizes impulse energy. Interlace excites a row every other field. BFI works at output-frame level; neither feature measures the panel's physical scan phase or pulse response.

## Additional effects

| CLI | JSON | Default | Range | Meaning |
|---|---|---|---|---|
| `--noise` | `Noise` | 0.003 | 0–0.2 | Temporal noise amplitude |
| `--flicker` | `Flicker` | 0.005 | 0–0.2 | Sinusoidal brightness modulation |
| `--flicker-hz` | `FlickerHz` | 60 | 40–120 | Modulation rate, Hz |
| `--line-interference` | `LineInterference` | 0 | 0–0.2 | Procedural line interference |
| `--vignette` | `Vignette` | 0.2 | 0–1 | Edge darkening |
| `--glass-reflection` | `GlassReflection` | 0.025 | 0–0.3 | Static procedural reflection |

Noise/flicker/interference precede beam and persistence. Flicker close to output frequency may look static or beat; it does not model the panel refresh mechanism.

## Console post-processing

| CLI | JSON | Default | Allowed | Meaning |
|---|---|---|---|---|
| `--console` | `ConsoleStyle` | `none` | `none`, `psx`, `megadrive`, `snes`, `mastersystem`, `gameboy` | Low-resolution color-processing pass |
| `--retro-width` | `RetroWidth` | 0 | 0 or integer 64–2048 | 0 chooses the console preset width |
| `--retro-height` | `RetroHeight` | 0 | 0 or integer 64–2048 | 0 chooses the console preset height |
| `--retro-dither` | `RetroDither` | 1 | 0–1 | Dithering before quantization |
| `--retro-filter` | `RetroFilter` | 0.25 | 0–1 | Input downsampling blend |
| `--composite` | `Composite` | 0 | 0–1 | Approximate YIQ filtering when a console pass is active |

`--console psx` selects only that pass. `--preset PSX` also sets artistic CRT controls. Changing retro height does not automatically change scanlines. Retro/composite controls do nothing with `console=none`.

## JSON example

```json
{
  "SchemaVersion": 2,
  "TubeProfile": "philips-a51eal155x",
  "TubeArea": "fit",
  "TubeContent": "fit",
  "CurvatureX": 0,
  "CurvatureY": 0,
  "MaskStrength": 0.3,
  "Phosphor": "custom",
  "DecayR": 6,
  "DecayG": 4,
  "DecayB": 2,
  "Persistence": 0.35,
  "RollingScan": false
}
```

Decay values in this example are estimates. Use the editor or `--save-preset` for a complete settings file. Saving from CLI continues into normal startup; it is not an export-only exit command.
