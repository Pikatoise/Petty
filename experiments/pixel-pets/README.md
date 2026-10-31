# Pixel pets — isolated generator study

Run from the repository root with the installed .NET 10 SDK on Windows:

```powershell
dotnet run --project experiments/pixel-pets/PixelPets.csproj -c Release
```

An optional first argument overrides the output directory. Open `output/preview.html`
directly in a local browser; no server, network, packages or app installation is required.
The file embeds all four generated atlases and offers clip selection, pause, frame
stepping, mirroring, 1×/3×/5× scale and background switching. `overview.png` is a static
contact sheet. `manifest.json` describes the experimental grid, not Petty's runtime format.

## Approach

Reuses the existing generator's `PixelCanvas` and palette type by linking their source
files read-only. There is no project reference, solution entry or runtime integration.
All output stays here. Existing production sprites and generator code are untouched.

Original hand-authored pixel clusters describe the torso and head; integer limb poses
and discrete tail paths assemble the animation. A 32×32 logical frame is expanded to
64×64 with exact 2× nearest-neighbour pixels, genuine binary alpha and ground row 53.
One silhouette per species, two coat studies each; the tricolor dog also has tan markings.
Eight atlas rows: idle, walk, run, sit, eat, sleep, play, stretch. Eight columns per row;
eat uses six frames and sleep four, leaving unused cells transparent. 58 frames per coat.

Visual references, viewed as direction only:

- [Pet Cats Pack — LuizMelo](https://luizmelo.itch.io/pet-cat-pack): compact side-view cats,
  economical clusters, readable poses.
- [Pixel Dogs — Benvictus](https://benvictus.itch.io/pixel-dogs): elongated torso, muzzle,
  distinct far limbs and coat blocks.

No third-party sprites, sheets or pixel data are included. This is procedural C# drawing,
not image generation. It is deliberately template-driven: new anatomy still requires
authored pixel clusters, rather than automatically discovering a new breed from a prompt.

## Verification and limits

Release generator ran successfully on 2026-10-31. Every run checks all 232 frames for
nonempty content, binary alpha, safe margins, repeatable rendering, at least two distinct
frames per clip, and a lossless PNG decode round-trip. The static overview was inspected.
Some frames intentionally repeat for timing; uniqueness does not establish animation quality.

The automated browser could not reach the local preview server and disallows local file
URLs, so interactive controls and animation playback remain **unverified in a browser**.
Open the HTML locally to judge timing, loop seams and final-scale readability.

This is not a production content pack: sitting, stretching and play are simplified;
sleep shares a curled-body template across species; no transitions, ball choreography,
petting, tired or exit clips are implemented. New breed identities are not production
choices. The goal is to judge whether pixel-cluster assembly improves the visual direction
before spending time on full animation work or integration.
