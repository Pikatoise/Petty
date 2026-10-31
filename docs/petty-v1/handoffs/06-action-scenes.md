# Session 06 — action scenes

- Stage document: `docs/petty-v1/sessions/06-action-scenes.md`
- Result: **accepted with a known visual limitation**.
- Behavior: play and pet commands reject overlap and eating; petting has a hand prop and cat clip, play lasts six seconds. Both actions update needs over their duration. Exit, replacement/disposal and display changes hide scene props. Old atlases without `pet` fall back to idle; asset-tool validation requires `pet` for new packs.
- Checks: Release build passed with zero warnings; 139 tests passed outside the sandbox; asset verification passed. The owner confirmed petting works and accepted the current ball scene temporarily.
- Known issues: the ball does not visibly rotate, and the play clip and pet movement are not synchronized to individual strikes; the owner describes the scene as visually rough and accepts it for now. Scene cleanup on display changes and forced shutdown still needs a live desktop check. Walking/sleep interruption and old-atlas fallback need additional focused integration coverage.
- Ready to start next stage? **yes**, with the ball-scene limitation explicitly accepted by the owner.

Owner follow-up (2026-11-01): the earlier unsynchronized/non-rotating ball limitation
was addressed. Play frames and ball contacts now use a single absolute scene clock;
optional playContactX/Y metadata aligns each breed's paw. Gentle rolling/hops replace
the old velocity jumps, rotation is a cached image transform, and final travel uses
the real display edge. Regression cases went red on the old trajectory, then passed.
Debug/Release builds, 179 Debug / 171 Release tests and the Windows frame/asset harness
passed. Contact sheets were inspected; live desktop visual acceptance remains open.
See artifacts/session10/play/preview.html for a preview sampled from the runtime code.
