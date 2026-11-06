# Production sprite packs

Eight owner-approved breeds, five coats each: 40 original procedural pixel-art atlases.
See `../breed-designs.json` for anatomy, palettes, markings and ordering. The first coat
of each breed is its creator default; the wizard offers species and breed, with left/right
arrows below the preview to cycle coats. Runtime restores the selected stable asset ID.

## Files and format

`packs/<breed-id>/<breed-id>-<coat-id>.png` and its matching JSON form one variant.
All sprite pixels are original C# artwork from Petty.Assets; no third-party pixels
or AI-generated art are bundled. Breed/coat descriptions live in breed-designs.json.
All atlases are 512×512: eight columns and eight rows of 64×64 cells. Frames are
row-major and clips occupy contiguous ranges; they need not start on a row boundary.
Artwork uses a 32×32 logical grid expanded exactly 2×, binary alpha and ground row 53.
The app caches decoded frames and displays them with nearest-neighbour scaling.

| Clip | Frames | FPS | Loop |
|---|---:|---:|---|
| idle | 8 | 6 | yes |
| walk | 8 | 10 | yes |
| run | 8 | 14 | yes |
| eat | 6 | 7 | yes |
| sleep | 4 | 3 | yes |
| play | 12 | 10 | yes |
| pet | 6 | 5 | yes |
| tired | 4 | 4 | yes |
| exit | 8 | 10 | no |

The head, ears, torso proportions and tail vary by breed. Coat markings are applied in
local body-part coordinates, preserving identity across poses. Naturally solid/pointed
coats follow the approved designs. Gait structure is shared; these are stylized procedural
sprites requiring visual review, rather than artist-finished anatomical studies.

Props and icons remain separate root PNGs: five bowl levels (24×24), six pouring frames
(24×32), ball and hand (16×16), tray icon (32×32), plus `petty.ico`.
No interaction prop is baked into a regular pet frame. Shared prop pixels and interaction behavior are unchanged.

## Reproduce and verify

Run from the repository root on Windows with .NET 10:

```powershell
dotnet run --project src/Petty.Assets -c Release -- --regenerate
dotnet run --project src/Petty.Assets -c Release -- --verify
dotnet run --project src/Petty.Assets -c Release -- --verify --pack assets/sprites/packs/siamese
dotnet run --project src/Petty.Assets -c Release -- --preview artifacts/verification/preview
dotnet run --project scripts/ContentVerification -c Release
```

Normal generation creates missing atlas pairs. Use `--regenerate` after editing design
configurations or renderer code. Valid handcrafted packs can be supplied unchanged in the
same metadata format; runtime never runs the generator. Pack validation accepts the old
three-coat pack format as well as the current five-coat format. New repository packs are
covered by tests enforcing exactly eight breeds and forty variants.

`--preview` writes a coat overview and standalone animated HTML with embedded images.
Open `artifacts/verification/preview/preview.html` in a local browser to inspect clips and frames.
PNG DPI metadata is fixed at 96 for repeatability across console/WPF rendering contexts.

## Runtime and compatibility

Optional metadata fields include speciesId, breedId, coatId, displayName and isDefault.
`playContactX`/`playContactY` identify the extended paw in sprite pixels. The runtime
uses this point to align the ball for each breed; older/custom sheets use a fallback.
Play frames and ball contacts share an absolute scene clock (frames 3/9 in the installed
12-frame clips); the final strike rolls the ball past the primary display edge.
User sprite roots at `<data-dir>/sprites/`, including nested packs, take precedence over
bundled IDs. Duplicate IDs within a root, invalid metadata and malformed sheets are rejected.
Existing recursive build/publish rules include every pack PNG/JSON.

The original `cat-orange-white` art and its generator were retired at the owner's request.
Old saved appearances explicitly migrate to `british-shorthair-blue` after that atlas
successfully loads; name, needs, bowl and identity timestamps are retained. Normal atomic
saving keeps the previous JSON backup. Other missing appearances retain the recovery flow.

## Mail sprite packs

`mail-packs/<breed-id>/<asset-id>-mail.png` and matching JSON contain the 40
prebuilt visitor appearances. These compact 512?320 atlases use the same 64?64
cells, 2? logical pixel grid, binary alpha and ground row 53 as regular pets.
They contain 36 frames; the four unused cells remain transparent.

| Clip | Frames | FPS | Loop |
|---|---:|---:|---|
| mail-walk | 8 | 10 | yes |
| mail-sit | 8 | 10 | no |
| mail-idle | 8 | 6 | yes |
| mail-exit | 12 | 10 | no |

The leather satchel and envelope emblem are baked into each breed's body pose
at asset generation time. Sitting folds the rear legs; exit first rises from the
seated pose, then walks. No runtime composition or image generation is needed.
The normal generation/verification commands cover both regular and mail packs.
`--preview` also writes `mail-overview.png` and `mail-preview.html`.

Mail packs are embedded in Desktop by the existing extraction scheme, but kept
outside `packs/` so they never appear in the creator or replace a local pet.
Load an explicit mail atlas through `SpriteSheetLoader.Load(directory, assetId +
"-mail", mail: true)`; this validates the four mail clips and retains the existing
frozen-frame and alpha-mask behavior. The next visitor stage owns lookup/fallback
for unknown sender appearances, guest windows and visit scheduling.
