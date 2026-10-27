

\# AGENTS.md — Pixel Desktop Pet



\## 1. Project Mission



Build a lightweight Windows desktop virtual pet application in C# using modern .NET and WPF.



The application displays a small, animated pixel-art cat on the Windows desktop. The cat lives independently, moves around, sleeps, eats from a bowl, and plays with a ball. The application runs in the background and is controlled through the Windows system tray.



This is an initial prototype, not a production-ready commercial product. However, its architecture must support future expansion to multiple breeds, coat variants, animations, and gameplay mechanics.



\*\*The visual style is strictly classic 2D pixel art. Do not use 3D models, real-time 3D rendering, vector-style characters, or smooth illustrated sprites.\*\*



Do not stop at writing a plan or presenting code snippets. Implement, build, test, and document the working prototype in the available environment.



If a required capability cannot be completed or verified in the current environment, explain the limitation and provide a clear implementation boundary rather than pretending it works.



\---



\## 2. Technology Stack



Use the following stack unless a concrete compatibility issue requires a documented alternative:



\- Language: C#

\- Framework: the latest stable .NET version available in the environment.

\- UI: WPF.

\- Desktop integration: Win32 API through P/Invoke where necessary.

\- Image format: PNG with genuine alpha transparency.

\- Sprite animation: frame-based animation using sprite sheets.

\- State persistence: System.Text.Json and JSON files.

\- System tray: NotifyIcon, using Windows Forms interop if appropriate.

\- Testing: automated unit tests for domain logic and persistence.



Do not use Unity, Unreal Engine, or a 3D engine.



Do not introduce a database, external backend, cloud service, or AI inference dependency at runtime. The application must operate locally.



Prefer built-in .NET functionality and a small number of justified dependencies.



\---



\## 3. Initial Scope



Implement exactly one pet configuration initially:



\- Species/breed: Cat.

\- One fixed, pre-generated coat variant.

\- A user-defined pet name.

\- One food bowl.

\- One temporary play ball.

\- A predefined set of animations.

\- Hunger and happiness needs.

\- A system tray icon and context menu.

\- A first-run pet creation wizard.

\- A settings window.

\- Local persistence.



The codebase must allow additional breeds and coat variants to be added later through assets and configuration rather than requiring a rewrite of the application.



Do not implement a procedural coat generator in this prototype. The initial coat is a fixed asset generated during development.



Do not implement networking, accounts, multiplayer, achievements, or other unrelated features.



\---



\## 4. Mandatory Asset Generation



A major requirement is a real, usable pixel-art sprite sheet.



Do not substitute Unicode characters, emoji, generated geometric shapes, generic placeholders, or a single static cat image for the actual animation assets.



\### 4.1 Character design



Create one original orange-and-white pixel-art cat.



Visual requirements:



\- Classic pixel-art aesthetics.

\- Crisp, deliberately placed pixels.

\- Consistent anatomy, proportions, face, and coat markings.

\- A readable silhouette at small display sizes.

\- A limited color palette.

\- No anti-aliased outlines.

\- No blur, gradients, or painterly rendering.

\- No shadows baked into transparent pixels unless they are intentional parts of the sprite.

\- Consistent orientation and scale across animation frames.

\- Genuine transparent backgrounds.



The cat should look like the same character in every frame.



Use a nominal frame size of 64x64 pixels as the initial target. If the artwork requires more room, choose another consistent size, document the reason, and keep every frame within the same grid dimensions.



\### 4.2 Generate an actual sprite sheet



Use an available image-generation tool or image-generation workflow to produce the character assets.



The target is a sprite sheet containing separate, consistently aligned frames for these animations:



| Animation | Initial target |

|---|---:|

| Idle | 4 frames |

| Walk | 8 frames |

| Run / playful movement | 8 frames |

| Eat | 6 frames |

| Sleep | 4 frames |

| Play with a ball | 8 frames |

| Tired / hungry appearance | 4 frames |

| Exit screen | 6 frames |



These counts are initial targets. If an animation requires a different number of frames to look correct, document and use the actual count.



Prefer a single consistent grid per sheet, or separate sheets per animation if that makes asset generation and validation more reliable.



The exit animation must move the cat toward the screen edge.



The eating animation must include a recognizable eating posture.



The play animation must be compatible with a separately rendered or positioned ball object.



The sleep animation should use a curled-up resting posture.



The tired/hungry animation should visibly differ from the normal appearance.



\### 4.3 AI image-generation prompt



Use the following prompt as the starting point for image generation:



> Create a production-oriented 2D pixel-art sprite sheet for an original small orange-and-white cat character in a desktop virtual pet game.

>

> The style must be classic, crisp pixel art with deliberate pixel placement, a limited color palette, hard pixel edges, and consistent dark outlines. Do not use 3D rendering, smooth vector art, painterly textures, blur, anti-aliasing, or gradients.

>

> Keep the cat's anatomy, head size, eye placement, ear shape, body proportions, orange markings, white chest, and white paws consistent across every frame.

>

> Use a fixed side-view orientation for locomotion. Keep the character's feet aligned to a consistent ground baseline, except during intentional jumps. Maintain consistent character scale and frame dimensions.

>

> Arrange animation frames in a strict, evenly spaced grid. Each cell must contain exactly one complete frame. Do not place labels, text, decorative borders, panel backgrounds, or UI elements inside the sprite cells.

>

> Required animation sequences: idle, walk, run, eating from a bowl, curled-up sleep, playing with a ball, tired appearance, and exiting toward the screen edge.

>

> The image background must be genuinely transparent, with a valid alpha channel. Do not represent transparency using a checkerboard pattern.

>

> Optimize the artwork for a small desktop character. Ensure the poses remain recognizable at approximately 64x64 pixels per frame.

>

> The result must be suitable for automated frame extraction into individual PNG images and playback in a WPF application.



\### 4.4 Asset validation



Image generation is not complete until the output is inspected and validated.



Verify:



1\. The generated image file exists and can be decoded.

2\. The background is actually transparent, not a checkerboard baked into the image.

3\. Frame boundaries and dimensions are known.

4\. Every extracted frame contains a complete character.

5\. Frames do not unintentionally overlap.

6\. The character's scale and ground baseline remain consistent.

7\. The walk cycle can loop without an obvious discontinuity.

8\. The sleep and eating animations are visually distinguishable.

9\. The ball and bowl do not become accidental parts of unrelated animations.

10\. The extracted frames look correct when displayed at their intended size.



If the generated sheet is malformed, do not silently accept it. Try a different generation strategy, generate individual frames, or generate separate sheets for each animation.



A single large image-generation request is not guaranteed to produce a technically valid sprite sheet.



\### 4.5 Asset processing



Create a repeatable asset-processing workflow.



It should:



\- Split sprite sheets into individual frame images or load them through a validated atlas.

\- Use explicit frame dimensions and animation metadata.

\- Preserve alpha transparency.

\- Avoid resampling or smoothing the pixel art.

\- Use nearest-neighbor scaling when enlargement is required.

\- Produce consistent frame origins and animation timing.

\- Keep source sheets and processing instructions in the repository.



If image processing is automated, provide a script or utility that can be rerun when assets change.



Never invent a file path or claim an asset exists without verifying it.



If image generation is unavailable, explain the limitation and implement the asset-loading pipeline against a documented, replaceable sample asset. Do not claim that the required final artwork has been generated.



\---



\## 5. Desktop Window and Windows Integration



This application is not a conventional desktop window.



The pet must appear on the Windows desktop, remain behind ordinary application windows, and must not behave like a permanently topmost overlay.



\### 5.1 Required behavior



\- No ordinary application window should remain visible in the taskbar.

\- The application is controlled through its notification-area tray icon.

\- The pet is visible when the desktop is exposed.

\- Ordinary application windows should cover the pet.

\- The pet should be able to move near the top of the taskbar and, where technically feasible, appear in front of the taskbar.

\- The transparent parts of the pet window must not intercept normal desktop interactions.

\- Only the pet and bowl should have relevant interaction regions.

\- The bowl must be draggable horizontally.

\- The application must not prevent the user from clicking desktop icons, the taskbar, or ordinary windows.



\### 5.2 Implementation requirements



Use a transparent, borderless WPF window where appropriate.



Investigate and prototype the necessary Win32 desktop-window integration.



Do not assume that `Topmost = false`, `WindowStyle = None`, or `AllowsTransparency = true` automatically places the pet correctly in the desktop layer.



The Windows desktop shell can involve WorkerW, Progman, Explorer-owned windows, and taskbar windows. Their behavior can vary across Windows versions and shell configurations.



Test the actual window hierarchy and z-order on Windows 10/11 where possible.



Avoid globally forcing the pet above all windows.



Use window hit-testing and appropriate Win32 styles or message handling to allow clicks through transparent areas. Validate the behavior with the actual WPF rendering approach.



If reliable desktop integration cannot be verified, isolate it behind a dedicated desktop-window integration service and document the remaining limitations.



Do not compromise normal desktop interaction merely to make the pet visible.



\### 5.3 Lifecycle



\- Start with the pet entering from the left or right screen edge.

\- Keep the application running when the creation wizard or settings window closes.

\- Closing settings must not shut down the application.

\- A normal application exit is initiated from the tray menu.

\- On normal exit, play the exit animation and then terminate.

\- Forced termination by Task Manager does not need to play an exit animation.

\- Prevent accidental shutdown when the last ordinary WPF window closes. Use an appropriate application lifetime configuration, such as explicit shutdown, if needed.



\---



\## 6. System Tray



Create a notification-area icon with a context menu containing exactly these initial commands:



1\. Pet settings

2\. Exit application



The tray icon should remain available while the pet is running.



The settings command opens the settings window.



The exit command initiates the exit animation and then shuts down the application.



Handle disposal of the tray icon and associated resources during shutdown.



Do not expose the application as an ordinary taskbar application window.



\---



\## 7. First-Run Creation Wizard



When no valid pet configuration exists, display a creation wizard.



The wizard must provide:



\- A visual preview of the cat.

\- The selected breed, initially fixed to Cat.

\- A Generate coat button, initially selecting the only available coat.

\- A pet-name input field.

\- A confirmation button.



Because the prototype contains one coat, the Generate coat button may reselect or preview that single asset. Do not imply that multiple coats exist when they do not.



The architecture should allow the button to select from a larger collection in a future version.



On confirmation:



1\. Validate the name.

2\. Create the pet configuration.

3\. Persist the initial state.

4\. Close the wizard.

5\. Display the pet on the desktop.

6\. Continue running in the background.



The application must not show the wizard again on every normal startup once a valid pet exists.



If loading saved state fails, handle the error safely and provide a recovery path.



\---



\## 8. Pet Settings and Recreation



The settings window is accessible through the tray icon.



It must allow the user to:



\- Change the pet's name.

\- Create a new pet.



The selected breed and coat are fixed after creation.



Do not expose breed or coat editing in the settings window for an existing pet.



Creating a new pet resets the current pet's identity and needs. Ask for confirmation before discarding the existing pet.



The prototype may recreate the same cat and coat because only one configuration is initially available.



Keep the creation workflow separate from the runtime pet controller.



\---



\## 9. Pet Needs and Behavior



Implement two independent needs:



\- Hunger / satiety.

\- Happiness / playfulness.



Represent both as normalized values in the range 0-100.



The values should decrease over time according to configurable rates. Do not display numerical values during normal desktop operation.



Keep rates configurable rather than embedding unexplained constants throughout the code.



\### 9.1 Hunger



\- Hunger or satiety changes gradually with elapsed time.

\- The cat should seek food when sufficiently hungry.

\- Below approximately 20% satiety, display the tired/hungry visual state.

\- When food is available, the cat can approach the bowl and eat.

\- Eating increases satiety.

\- If the bowl is empty, the cat cannot eat.



\### 9.2 Happiness



\- Happiness gradually decreases over time.

\- The user can initiate play by interacting with the pet.

\- The pet plays with a ball for a short period.

\- Playing restores happiness.

\- The ball is a temporary game object and must not be confused with the bowl.



\### 9.3 Food bowl



The bowl has its own food inventory, separate from the cat's satiety.



\- Clicking the bowl adds a configured amount of food.

\- Food inventory cannot become negative.

\- The cat consumes food from the bowl while eating.

\- The bowl can be dragged horizontally.

\- Its position is saved and restored.

\- Dragging must not accidentally trigger feeding.

\- The bowl must remain interactive without making its entire transparent window block mouse input.



\### 9.4 Pet interaction



When the user hovers over or interacts with the pet, expose a small Play action.



Selecting Play initiates the play animation and ball interaction.



Do not require the user to keep a settings window open.



\---



\## 10. State Machine and Animation



Use an explicit finite-state machine for pet behavior.



Possible states include:



\- Entering

\- Idle

\- Walking

\- Running

\- SeekingFood

\- Eating

\- Sleeping

\- Playing

\- Tired

\- Exiting



Add, remove, or refine states when justified.



Define clear transitions and priorities. For example:



\- Exiting takes priority over normal behavior.

\- Eating requires available food.

\- Playing temporarily interrupts autonomous behavior.

\- Hunger can trigger seeking food.

\- Sleep and idle states must not permanently prevent the pet from responding to the user.



Separate behavioral decisions from animation playback.



Animation should not determine game logic merely because a frame has been displayed.



Use time-based animation progression rather than tying behavior to render FPS.



Avoid busy loops and unnecessary polling.



Use timers or a controlled update loop with an appropriate interval. Pause or reduce unnecessary work when animation is not needed.



Keep the number of active WPF objects small. Prefer reusable image resources and efficient sprite-frame updates.



\---



\## 11. Persistence



Use JSON and System.Text.Json.



A representative state model could contain:



\- Schema version.

\- Pet name.

\- Breed identifier.

\- Coat identifier.

\- Selected asset identifier.

\- Hunger/satiety.

\- Happiness.

\- Last-updated UTC timestamp.

\- Bowl food inventory.

\- Bowl position.

\- Monitor identity or position metadata when appropriate.



Use a deterministic asset identifier for the fixed coat. A random seed is not necessary for the initial fixed-asset prototype.



Persist state when:



\- The pet is created.

\- The pet is renamed.

\- The user feeds the bowl.

\- The cat consumes food.

\- The user plays with the cat.

\- The bowl is moved.

\- The application is shutting down normally.



Also save periodically to protect against unexpected termination.



Use atomic file replacement or another robust save strategy where practical. Avoid leaving a partially written state file after a crash.



Handle missing, malformed, incompatible, or outdated JSON safely.



Use a version field so the schema can evolve.



\### 11.1 Offline time



Store `LastUpdatedUtc`.



On startup, calculate the elapsed time since the previous update and apply the configured need-decay rules.



Clamp elapsed time and resulting values to sensible bounds. Handle negative elapsed time and invalid timestamps safely.



Do not run a background process while Windows is asleep or the application is closed.



The application should update needs from elapsed time, not assume it ran continuously.



\### 11.2 Display configuration



Persist the bowl position in a way that can recover when monitor resolution, display arrangement, or DPI changes.



Avoid assuming that one absolute screen coordinate will remain valid forever.



Restore the bowl to a valid visible location if the saved monitor no longer exists.



\---



\## 12. Architecture



Use clear separation of responsibilities. Avoid building the entire application in a single window or monolithic class.



Suggested responsibilities:



\- Application lifecycle and composition.

\- Pet domain model and needs.

\- Pet behavior state machine.

\- Animation controller and sprite-sheet loader.

\- Desktop-window integration service.

\- Pet interaction and hit-testing.

\- Bowl controller and position persistence.

\- Tray icon service.

\- Creation wizard and settings UI.

\- JSON state repository.

\- Time and offline-progression service.



Use dependency injection if it improves clarity without unnecessary complexity.



Prefer testable domain logic independent of WPF and Win32.



Keep platform-specific code isolated from the pet's behavior.



Use asynchronous operations only where they are useful. Do not introduce concurrency merely for architectural appearance.



Document important design decisions and the limitations of desktop-shell integration.



\---



\## 13. Performance and Resource Management



The application should remain lightweight during long periods of operation.



Requirements:



\- No continuous busy-wait loops.

\- No unnecessary per-frame allocation of image objects.

\- Reuse decoded sprite frames.

\- Avoid repeatedly decoding the same PNG files.

\- Load only the currently required pet assets.

\- Keep background timers at sensible intervals.

\- Stop timers and release resources during shutdown.

\- Avoid excessive disk writes.

\- Do not introduce a database or network dependency.

\- Ensure transparent regions do not intercept normal desktop input.



Measure or estimate CPU and memory usage where practical. Do not claim a specific performance target has been met without testing.



\---



\## 14. Error Handling and Reliability



Handle errors from:



\- Missing sprite assets.

\- Invalid sprite-sheet dimensions.

\- Invalid animation metadata.

\- JSON loading and saving.

\- Unavailable display devices.

\- Window integration failures.

\- Tray icon initialization.

\- Unexpected user input.

\- Application shutdown.



Show actionable error messages for user-facing failures.



Log technical details to a local log file or another lightweight diagnostic mechanism.



Do not silently swallow exceptions.



Do not allow a recoverable asset or persistence failure to corrupt the saved pet state.



\---



\## 15. Testing



Write automated tests for the parts that do not require an interactive Windows desktop.



At minimum, test:



\- Hunger and happiness bounds.

\- Need decay over elapsed time.

\- Offline progression.

\- Feeding and bowl inventory.

\- Hunger-triggered food seeking.

\- Happiness restoration through play.

\- State-machine transition rules.

\- JSON serialization and deserialization.

\- Recovery from invalid or missing saved state.

\- Bowl-position restoration and clamping.



Validate sprite metadata and frame extraction with automated checks where possible.



On Windows, manually verify:



\- Tray menu behavior.

\- Creation wizard.

\- Settings and renaming.

\- Pet animation playback.

\- Bowl dragging and feeding.

\- Click-through behavior.

\- Z-order relative to ordinary windows and the taskbar.

\- Multi-monitor and display-change behavior.

\- Normal animated exit.



If a test requires an actual Windows desktop and the environment cannot provide one, clearly mark it as unverified instead of claiming it passed.



\---



\## 16. Implementation Milestones



Implement incrementally.



\### Milestone 1 — Project foundation



\- Inspect the environment and available .NET SDKs.

\- Create the WPF application structure.

\- Establish the basic domain models and configuration.

\- Build the project.

\- Document how to run it.



\### Milestone 2 — Sprite assets and animation



\- Generate or obtain the actual cat sprite sheet.

\- Validate transparency and frame layout.

\- Implement sprite-sheet loading and frame extraction.

\- Implement idle, walk, eat, sleep, play, tired, and exit animations.

\- Verify frame alignment and looping.



Do not proceed by substituting a static image for the animation system.



\### Milestone 3 — Pet window and desktop integration



\- Create the transparent pet window.

\- Implement interaction regions.

\- Prototype desktop-layer positioning and z-order.

\- Verify click-through behavior.

\- Keep the integration layer isolated and testable.



\### Milestone 4 — Tray and lifecycle



\- Implement the tray icon and its two menu commands.

\- Ensure closing ordinary windows does not terminate the application.

\- Implement the animated exit sequence.



\### Milestone 5 — Creation and settings



\- Implement the first-run wizard.

\- Implement pet naming.

\- Implement the settings window and rename flow.

\- Implement confirmed pet recreation.



\### Milestone 6 — Behavior and needs



\- Implement the state machine.

\- Implement hunger, happiness, food inventory, eating, and play.

\- Implement the draggable bowl and ball interaction.



\### Milestone 7 — Persistence and recovery



\- Implement versioned JSON persistence.

\- Implement periodic and event-driven saves.

\- Implement atomic saving and offline progression.

\- Handle display configuration changes.



\### Milestone 8 — Verification and cleanup



\- Run automated tests.

\- Build the application in Release configuration.

\- Resolve warnings and errors where practical.

\- Test the actual Windows desktop behavior.

\- Document any unresolved limitations.



Do not mark a milestone complete until its required work has actually been performed or its limitations are explicitly recorded.



\---



\## 17. Acceptance Criteria



The prototype is considered functionally complete when:



1\. The application builds successfully.

2\. A real pixel-art cat sprite sheet exists in the repository.

3\. The sheet has been validated and can be split into individual frames.

4\. The pet displays working frame-based animations.

5\. The first-run wizard creates a named cat.

6\. The pet appears on the desktop without a conventional taskbar application window.

7\. The tray icon exposes the required settings and exit commands.

8\. The user can rename the cat and recreate it with confirmation.

9\. Hunger and happiness change over time.

10\. The bowl has an independent food inventory.

11\. The user can refill and drag the bowl.

12\. The cat can seek food and eat.

13\. The user can initiate play with a ball.

14\. The tired/hungry appearance is displayed under the defined conditions.

15\. State survives application restarts.

16\. Offline time is accounted for on startup.

17\. The application handles missing or invalid state files safely.

18\. The application does not continuously consume resources through a busy loop.

19\. Normal exit plays the exit animation before termination.

20\. Desktop click-through and z-order behavior have been tested on Windows or explicitly documented as unverified.



A visually convincing mockup alone does not satisfy these criteria.



\---



\## 18. Final Deliverables



Provide:



\- The complete source code.

\- The actual generated sprite-sheet image assets.

\- Extracted frames or a repeatable extraction pipeline.

\- Animation metadata.

\- A working WPF application.

\- Automated tests.

\- A README with setup and launch instructions.

\- A short architecture overview.

\- A record of build/test results.

\- A list of any unverified Windows desktop integration behavior.



Keep the initial implementation focused. Do not add features beyond the defined scope without a clear reason.



Prioritize correctness, maintainability, consistent pixel-art animation, low resource consumption, and reliable Windows desktop interaction.



