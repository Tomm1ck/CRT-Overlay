# Changelog

**English** · [Русский](docs/ru/CHANGELOG.md)

## 1.0.1 — 2026-10-15

- Preserve persistence and rolling history when adjusting bloom, halation, brightness, output gamma, geometry, convergence, focus, vignette, glass reflections, or BFI. Physical stripe-mask adjustments also preserve history; changes to beam inputs or the temporal model still reset it.
- Read only the changed editor row while the draft is valid. Repeated control notifications with the same value no longer revalidate or publish a duplicate preview; recovery from invalid input validates the full group.
- Reuse the color-conversion matrix when gamut, white temperature, tube profile, and white-point mode are unchanged.

## 1.0.0 — 2026-10-15

First public release.

- Desktop CRT effects for windowed/borderless games and video through Direct3D 11 and DirectComposition.
- Scanlines, RGB masks, beam, bloom, halation, geometry, convergence, persistence, optional rolling exposure and BFI.
- Soft, Balanced, Strong, Rolling, PSX, Mega Drive, SNES, Master System and Game Boy presets.
- Philips A51EAL155X picture-tube profile with documented source data and explicit model assumptions.
- Live preset editor, JSON import/export, Apply/Cancel and English/Russian UI.
- Tray controls, BAT launchers and independent hotkey registration that tolerates occupied combinations.
- Capture-exclusion diagnostics and portable source paths in release stack traces.
- Skip all 16 glow-related GPU draws when Bloom and Halation are both zero, including the Game Boy preset.
- Message-aware waitable-timer frame pacing and allocation counters limited to the explicit timed diagnostic mode.
- Optional `--performance-log on` with presentation/capture rates, render wall time, process CPU usage and asynchronous sampled GPU effects time.
- English/Russian documentation, MIT license and third-party notices.

Requires Windows x64, SDR and .NET Runtime 9 x64. Preset schemas 1 and 2 are supported.
