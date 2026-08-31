# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Project root: `/Users/sundoll/Library/Mobile Documents/com~apple~CloudDocs/Desktop/SundollUnity/SundollWorld`
- Last analyzed: 2026-08-31
- Git root: `/Users/sundoll/Library/Mobile Documents/com~apple~CloudDocs/Desktop/SundollUnity`; latest audit covers the formal M4-M7 implementation slices, M3 Workbench integration and UX1-UX7 current-device closure
- This is the formal M7 Unity project. `M0-Spike` remains disposable validation material; M1/M2 core, the M3 Workbench, M4 piece library, M5 console slice, M6A/M6B proofs and M7 macOS hardening are implemented with high-resolution, Windows and cross-platform release validation pending.

## Confirmed Environment

- Unity version: `6000.3.22f1` (`1c726e1fb402`)
- Render pipeline: Universal Render Pipeline with the Universal 2D template and 2D Renderer
- Input system: Unity Input System package `1.20.0`; the template also contains the legacy Input Manager asset
- Target platforms: macOS and Windows are the M0/M1/M2/M3/M4 target platforms; current Hub project row is macOS

## Important Packages And Frameworks

| Area | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Rendering | URP `17.3.0` and URP 2D template assets | Confirmed | `Packages/manifest.json`, `Assets/Settings/Renderer2D.asset` |
| Input | Input System `1.20.0` with template action asset | Confirmed | `Packages/manifest.json`, `Assets/Settings/InputSystem_Actions.inputactions` |
| Testing | Unity Test Framework `1.6.0` | Confirmed | `Packages/manifest.json` |
| UI | UI Toolkit module and UGUI module are available; no project UI architecture yet | Confirmed | `Packages/manifest.json` |
| Networking | No networking package or first-party networking code detected | Confirmed | `Packages/manifest.json`, empty first-party Assets beyond template |
| MCP | No Unity MCP package or client config detected | Confirmed | package/config inspection; no `.mcp.json` or MCP package markers |

## Directory Structure

| Path | Purpose | Confidence | Evidence |
| --- | --- | --- | --- |
| `Assets/Scenes/` | Template scene location; contains `SampleScene.unity` | Confirmed | filesystem and `ProjectSettings/EditorBuildSettings.asset` |
| `Assets/Settings/` | URP, renderer, input-action, and scene-template assets | Confirmed | filesystem |
| `Docs/AI/` | Persistent AI/project context documentation | Confirmed | this document |
| `Packages/` | Unity registry package manifest and lock file | Confirmed | filesystem |
| `ProjectSettings/` | Unity project settings and build scenes | Confirmed | filesystem |
| `Library/`, `Temp/`, `Logs/` | Generated Unity state | Confirmed | filesystem; treated as generated |
| `Assets/Sundoll/` | Formal M1+ product code root; currently contains M1 runtime, M2 persistence and M3 map editor code | Confirmed | filesystem |

## Assembly Boundaries

| Assembly | Responsibility | Key references | Notes |
| --- | --- | --- | --- |
| `Sundoll.Core` / `Sundoll.Application` | Pure domain state and command flow | Core has no Unity dependency; Application references Core | Confirmed in asmdef and source |
| `Sundoll.Infrastructure` | M2 Revision, HEAD, Journal, blob and package persistence | References Core | Confirmed in asmdef and source |
| `Sundoll.Presentation` / `Sundoll.Bootstrap` | Runtime diagnostic overlay, M3 grid editor and composition root | References Application/Infrastructure | Confirmed in asmdef and source |
| `Sundoll.Tests.EditMode` | M1/M2/M3 EditMode tests | References product assemblies and Test Framework | Confirmed in asmdef and test files |

## Scenes And Startup Flow

- Build scenes: `Assets/Sundoll/Scenes/M3Workbench.unity` is first and `Assets/Sundoll/Scenes/M1Bootstrap.unity` remains enabled as a diagnostic scene.
- Startup flow: `M3WorkbenchRoot` creates the pure-data command bus, opens the isolated M4 save session under `Application.persistentDataPath/SundollWorld_M4`, binds the Tilemap and M4 placeholder projections plus the UI Toolkit Workbench, then attaches the Input System adapter.
- `M1Bootstrap` remains a diagnostic surface; its automatic runtime creation hook is disabled so it cannot interfere with the formal M3 scene.

## Architecture

| Pattern | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Formal M1/M2/M3/M4 architecture | Pure C# state authority, command bus, M2 persistence infrastructure, M3 map editing commands, M4 piece-library commands, Unity bootstrap/presentation boundary | Confirmed | `Assets/Sundoll/` source and asmdef files |
| M1/M2/M3/M4 target | Pure C# domain state as authority, explicit Application command flow, M2 persistence, M3 map editing, M4 piece library/relationships, Unity as presentation/bootstrap | Confirmed requirement | `../../../Unity从零开发工作计划.md` and M1/M2/M3/M4 reports |
| Existing DI/event bus/networking | No DI framework, external event bus, or networking SDK | Confirmed | no such package or source dependency |

## Coding Conventions

- Namespace style: `Sundoll.*` namespaces.
- Runtime domain and persistence DTOs remain free of Scene/GameObject references; Unity APIs are limited to the bootstrap/presentation boundary and JSON serialization integration.
- M2 Revision writes use `M2SaveQueue`: the main thread captures an immutable DTO snapshot and a single background chain performs serialization, atomic write and flush. `M2ProjectStore.Save()` also holds the project-root `.save.lock` with `FileShare.None`, rechecks expected generation under that lock, and times out explicitly. The synchronous `Save()` API remains for compatibility and initialization.
- Persistence invariants and recovery limitations are documented in `Docs/Reports/M2-结果报告.md`.

## Testing And Validation

- EditMode tests: 97 first-party tests; 97 passed, 0 failed, 0 ignored in the latest completed Unity Editor run. The suite contains M1-M4 coverage plus M5 multi-map/console tests, M6A/M6B rule and Loopback tests, M7 migration/frozen-save/pool tests, project workspace tests, starter-content tests and UX6/UX7 state and focus coverage.
- PlayMode validation: 16/16 passed in `M3Workbench.unity`/isolated projection setup, covering startup, five Tilemap projections, edit and dirty-region refresh, hidden/locked layer behavior, M4 placeholder piece projection, M4 64-piece texture sharing baseline, M5 fog/annotation projection, Workbench UI controls, runtime image import/thumbnail generation, M7 texture cache lifecycle, View destruction cleanup, virtualized piece-library grid filtering, bounded thumbnail LRU behavior, 1000-piece projection/allocation baseline, UX6 state presentation and UX7 workspace/focus behavior. A real macOS Player window capture at 2560×1440 measured 1000 visible pieces, uncapped render-capacity p95 `4.5495 ms`, and managed allocation p95 `0 B`; the render-capacity gate passed, while long-run production 60 FPS pacing remains to be verified.
- CI/build validation: no CI configuration detected. M7 macOS universal IL2CPP build result is Success and the latest Player workspace visual check passed at the available 1280×720 desktop size. After removing unused Visual Scripting, a clean temporary build reports `errors=0`, `warnings=1`, `TypeDB: Class` count 0 and no `visualscripting` residue; the remaining warning is Unity Cloud native symbols upload token absence, not script compilation. The current local Library/Bee build still emits legacy TypeDB diagnostics, so release evidence continues to prefer clean imports. Details are in `Docs/Reports/M7-结果报告.md` and `Docs/Evidence/M7-clean-build-no-visualscripting-20260827.md`. Latest batch validation evidence is `Docs/Evidence/TestResults/TestResults_EditMode_20260831_125749.xml` (97/97) and `Docs/Evidence/TestResults/TestResults_PlayMode_20260831_125749.xml` (16/16); both use the version-pinned license channel through `scripts/unity-common.sh`. macOS baselines cover batch edit, Snapshot, Revision save, 10,000 Journal recovery, 64-piece texture sharing, 1000-piece projection/allocation, piece-thumbnail LRU behavior, real-window render capacity and production pacing capture, while the 60-minute operation soak remains unverified, strict production pacing remains over budget, Windows build, independent-process forced-exit recovery and cross-platform save exchange remain unverified.

## Available Unity Tooling

| Capability | Status | Evidence |
| --- | --- | --- |
| `unity.connection.status` | unavailable | no Unity MCP bridge/relay detected; use `scripts/unity-doctor.sh` for local diagnostics |
| `unity.editor.version` | available | Unity Editor/Hub and `ProjectVersion.txt` |
| `unity.console.read` | available | Console can be read through local Computer Use; no MCP bridge |
| `unity.scene.list` | available | `EditorBuildSettings.asset` and local Editor |
| `unity.scene.inspect` | available | local Editor/serialized assets; no MCP bridge |
| `unity.buildsettings.read` | available | `ProjectSettings/EditorBuildSettings.asset` |
| `unity.gameobject.inspect` | available | local Editor/serialized scene; no MCP bridge |
| `unity.asset.search` | available | filesystem and Unity Project window |
| `unity.package.read` | available | `Packages/manifest.json` and lock file |
| `unity.tests.list` | available | Unity Test Framework installed; latest first-party run contains 97 EditMode and 16 PlayMode tests |
| `unity.tests.run` | available | via `scripts/unity-run-tests.sh`; no MCP bridge |
| `unity.playmode.read` | available | local Editor; no MCP bridge |
| `unity.profiler.read` | unverified | M2 performance budget and long-session profiling remain deferred |

## Important Constraints

- Do not copy historical web-project code, data, maps, images, music, or save files.
- Keep formal product code under `Assets/Sundoll/`; keep M0-only code under `M0-Spike`.
- Do not add packages, networking SDKs, DOTS, or DI frameworks without an explicit requirement.
- Domain state must be reconstructable without Scene, GameObject, Transform, or Unity object references.
- M2/M3 persistence is pre-v1; save format compatibility is not promised beyond the current schema.
- Do not claim Windows IL2CPP, Windows durable-write, cross-platform save exchange, or real-network validation from this macOS environment.

## Unknowns And Confidence

- M1 domain schema and exact command set: implementation decision, documented in code and tests.
- Final boot scene composition and runtime UI layout: established in `Assets/Sundoll/Scenes/M3Workbench.unity`; UX1-UX7 workspace, boundary, inspector, focus and current-device visual checks are documented, with high-resolution scaling still pending.
- Windows Editor/module availability: not verified in the formal project.
- Unity MCP bridge: not installed or configured; repository-only, fixed Unity batchmode scripts and local Computer Use workflows are available.

## Source Files Inspected

- `ProjectSettings/ProjectVersion.txt`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/EditorBuildSettings.asset`
- `Assets/Scenes/SampleScene.unity`
- `Assets/Settings/Renderer2D.asset`
- `Assets/Settings/InputSystem_Actions.inputactions`
- `Assets/Sundoll/Infrastructure/M2ProjectStore.cs`
- `Assets/Sundoll/Infrastructure/M2JournalStore.cs`
- `Assets/Sundoll/Infrastructure/M2SaveSession.cs`
- `Assets/Sundoll/Infrastructure/M2PackageArchive.cs`
- `Assets/Sundoll/Infrastructure/M3WorkspaceStateStore.cs`
- `Assets/Sundoll/Application/M3ContentLookupCache.cs`
- `Assets/Sundoll/Tests/EditMode/M2PersistenceTests.cs`
- `Assets/Sundoll/Core/M3MapEditorCommands.cs`
- `Assets/Sundoll/Application/M3MapEditorFacade.cs`
- `Assets/Sundoll/Application/M3GridViewport.cs`
- `Assets/Sundoll/Application/M3LayerEditState.cs`
- `Assets/Sundoll/Presentation/M3RuntimeMapEditor.cs`
- `Assets/Sundoll/Presentation/M3WorkbenchRoot.cs`
- `Assets/Sundoll/Presentation/M3WorkbenchInput.cs`
- `Assets/Sundoll/Presentation/M3WorkbenchMapProjection.cs`
- `Assets/Sundoll/Core/M3MapObjectCommands.cs`
- `Assets/Sundoll/Application/M3MapClipboard.cs`
- `Assets/Sundoll/Tests/EditMode/M3MapEditorTests.cs`
- `Docs/Reports/M2-结果报告.md`
- `Docs/Reports/M3-结果报告.md`
- root `Unity从零开发工作计划.md`
- root `README.md`
- `M0-Spike/README.md`

<!-- unity-onboarding:generated:end -->
