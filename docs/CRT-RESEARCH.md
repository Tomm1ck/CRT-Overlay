# CRT sources and model limits

**English** · [Русский](ru/CRT-RESEARCH.md) · [Home](../README.md)

The app aims to evoke the atmosphere of CRT viewing. Documented component data and engineering approximations are distinguished below. A tube profile is not a calibration of a complete television or a measured individual unit.

## Philips A51EAL155X Black Line FX

Primary source: [Philips Components product specification, 21 January 2000](https://patric-sokoll.de/Roehrenmuseum/Datenbank/PDF/Datenblatt%20A51EAL155X%20Philips.pdf). This manufacturer document is hosted in a third-party archive. Page numbers are printed numbers; pages 4, 10, and 11 were checked against text and rendered pages on 12 October 2026.

| Data | Value | Page |
|---|---|---|
| Useful screen axes; diagonal | 411.2 × 308.9 mm; 509.8 mm | 4 |
| Construction | Slotted shadow mask, vertical phosphor stripes, black matrix | 2, 4 |
| Identical-color stripe pitch at center | Approximately 0.78 mm | 4 |
| Red CIE xy | 0.630, 0.330 | 4 |
| Green CIE xy | 0.300, 0.610 | 4 |
| Blue CIE xy | 0.155, 0.065 | 4 |
| Center face-glass transmission | 41% | 4 |
| Center luminance under stated conditions | 79 cd/m², not a maximum rating | 4 |
| Persistence | Qualitatively medium short; no numeric RGB tau | 4 |
| Surface | Faceplate formula and sagittal-height table | 10–11 |
| Gun | Cathode-current versus video-voltage graph | 19 |

Luminance conditions include focused raster, white x=0.313/y=0.329, and current density 0.4 µA/cm². This is a picture tube specification; a particular television chassis has not been established.

### Data and implementation

[`Profiles/philips-a51eal155x.json`](../Profiles/philips-a51eal155x.json) stores values, units, origins, conditions, source pages, and unknown-data fallbacks. [`presets/philips-a51eal155x.json`](../presets/philips-a51eal155x.json) contains artistic settings matching the built-in preset; it is not the physical-data source. Tube selection is independent of console style.

The primary matrix uses the Philips xy coordinates, not the unrelated PTL P22 coordinates. Reference white is the datasheet measurement white, not an immutable native white of all displays using this tube. User temperature is an explicit alternative.

Extending the center pitch across the width gives about `411.2 / 0.78 = 527` identical-color periods. This is a calculation, not a resolution specification. A 1920-pixel tube width projects approximately 3.64 pixels per period; a fitted near-4:3 area on 1080p is roughly 1438 pixels wide and projects about 2.73 pixels per period before perspective.

The model extends center pitch uniformly in tube X/Y. Three equal emitting stripes occupy an estimated 75% of each period, leaving 25% black gaps. Stripe widths, RGB order, peripheral pitch variation, and vertical slot geometry are not a measured mask map. The period is filtered using the final projected pixel footprint, including scaling and perspective. Physical stripes are applied after optical filters to avoid fine-mask resampling artifacts; their microstructure does not participate in the modeled scattering/history.

### Surface and observer

Page 10 defines, with X/Y/Z expressed in millimeters:

```text
Z = A1*|X|^B1 + A2*|Y|^B2 + A3*|X|^B3*|Y|^B4
A1 = 2.33161e-4; A2 = 2.50647e-4; A3 = -9.31800e-11
B1 = 2.1; B2 = 2.1; B3 = 1.84082; B4 = 2.65536
```

Coefficient dimensions depend on the exponents. Mirroring across both axes is a modeling assumption. The table on page 11 references the diagonal endpoint, whereas the formula references the center apex: its 25.1 mm center height is not `Z(0,0)=25.1` in the formula.

The viewer is on-axis at a configurable distance. Outer glass approximates the emitting surface; thickness and refraction are omitted. Deflection is assumed ideally corrected and uniform in X/Y. Glass shape alone does not define the television's barrel-distortion map. User curvature, focus, convergence, overscan, and rounded corners remain separate artistic approximations.

### Light, decay, and gun response

Relative mode keeps normal brightness scaling. Reference mode multiplies by `79 / DisplayWhiteNits`, then applies independent user brightness. The 79 cd/m² value already represents light through the glass; multiplying by 0.41 again would double-count attenuation. The app does not measure display white, ambient illumination, or actual emitted luminance. White changes, gamma, glow, mask settings, output gamut, and SDR clipping limit photometric interpretation.

Numeric RGB decay, a calibrated spot map, scattering kernel, and the used gun transfer curve have null values with explicit fallbacks. The page-19 graph is not digitized or applied. Gamma is an artistic control, not a reconstructed video amplifier/gun chain. Existing P22/custom effective decay times and Gaussian/bloom approximations are retained without claiming Philips measurements.

## Other research references

[Sony PVM-20M2MD / PVM-14M2MD official manual](https://pro.sony/s3/cms-static-content/operation-manual/3861644051.pdf), printed page 35, describes professional Trinitron monitors. It lists 600 TVL, RGB bandwidth 10 MHz ±3 dB, aperture correction 0…+6 dB, and selectable D65/D56/D93 white. PVM-20M2MD convergence figures include less than 0.6 mm at center and less than 1.0 mm at the periphery. These are reference specifications, not a measured error map. **No Sony profile is implemented.** TVL is not scanline count; bandwidth needs a signal timebase and chosen transfer function before becoming a filter kernel.

[Phosphor Technology's CRT phosphor catalog](https://www.phosphor-technology.com/crt-phosphors/) supplies the legacy PTL P22 primary coordinates: R=(0.647,0.343), G=(0.310,0.594), B=(0.148,0.062). It is not the source of the app's effective 6/4/2 ms P22 or 24 ms P4 decay settings. A phosphor family name does not imply identical gamut or decay across tubes.

[PSX-SPX GPU Misc](https://psx-spx.consoledev.net/dev/ps1/gpu/misc/) documents the PS1 dither matrix used in `Shaders/Retro.hlsl`. This is community technical documentation, not a manufacturer manual. The post-process applies dithering to a finished desktop; the original GPU applied it according to primitive/command rules. RGB333/RGB555 alone does not establish the analog DAC response or palette of a particular board.

## What is still approximate

A full chain includes source video timing, RGB/composite/RF connection, decoder, amplifier, gun, deflection, phosphors, mask, and glass. The same tube can produce different pictures in different chassis and conditions. Aging and adjustment matter.

Further calibration needs measured decay, spot size/shape versus current and position, scattering, black level, reflections, geometry, convergence, and known viewing conditions. The schema supports measured data, but no measurements of an individual Philips unit are claimed. There is no meaningful overall accuracy percentage without a reference and a measurement method.

Composite remains five-tap YIQ filtering, not a complete NTSC/PAL encoder/decoder or RF model. LCD/OLED pixel structure, gamut, brightness, refresh and temporal response still constrain output. A more informed calculation does not turn a modern panel into a physical CRT.
