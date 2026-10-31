# Session 07 — bowl levels and pouring

- Stage document: `docs/petty-v1/sessions/07-bowl.md`
- Result: implemented; owner desktop visual/interaction check pending.
- Behavior: five bowl sprites share one hit mask. Positive food rounds up to the next quarter, and zero alone displays empty. An accepted click immediately adds up to 25 units and saves; a 1.2-second click-through packet/food effect rejects further clicks without blocking drags. Eating updates the displayed level. The effect follows the bowl and is disposed on replacement or shutdown.
- Checks: Release build passed with zero warnings; 150 tests passed outside the sandbox, including level boundaries, repeated clicks, capacity, eating and saving during a pour. Asset verification checks all five masks and effect frames. An isolated application smoke launch and animated exit completed with code 0.
- Owner checks: click through all levels, try rapid clicks and dragging during pouring, observe eating, and check packet position near screen/taskbar edges and across DPI settings. Real desktop clicks, dragging and visual quality were not tested in this session.
- Ready to start next stage? **yes**, subject to the owner checks above.

- Owner follow-up: pouring was confirmed visually. Bowl art now has a taller, progressively rising food mound and a current-frame alpha hit mask; the pouring anchor was adjusted. Updated art and hit testing still need desktop verification.
