# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Project root: `/Users/sundoll/Desktop/SundollUnity/SundollWorld`
- Last analyzed: 2026-08-25
- Git root: `/Users/sundoll/Desktop/SundollUnity`; latest audit covers the versioned command operation batch implementation
- This is the formal M3 Unity project. `M0-Spike` remains disposable validation material; M1 is complete and M2 core persistence is implemented with cross-platform validation pending.

## Confirmed Environment

- Unity version: `6000.3.22f1` (`1c726e1fb402`)
- Render pipeline: Universal Render Pipeline with the Universal 2D template and 2D Renderer
- Input system: Unity Input System package `1.20.0`; the template also contains the legacy Input Manager asset
- Target platforms: macOS and Windows are the M0/M1/M2/M3 target platforms; current Hub project row is macOS

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

- Build scenes: `Assets/Sundoll/Scenes/M1Bootstrap.unity` is enabled in Build Settings.
- Startup flow: `M1Bootstrap` creates the demo command bus, opens the M2 save session under `Application.persistentDataPath`, then attaches the M1/M2 diagnostic overlay and M3 grid editor.
- The bootstrap scene remains a diagnostic surface and currently has no Camera; final Workbench UI is deferred to M3/M5.

## Architecture

| Pattern | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Formal M1/M2/M3 architecture | Pure C# state authority, command bus, M2 persistence infrastructure, M3 map editing commands, Unity bootstrap/presentation boundary | Confirmed | `Assets/Sundoll/` source and asmdef files |
| M1/M2/M3 target | Pure C# domain state as authority, explicit Application command flow, M2 persistence, M3 map editing, Unity as presentation/bootstrap | Confirmed requirement | `../../../Unity从零开发工作计划.md` and M1/M2/M3 reports |
| Existing DI/event bus/networking | No DI framework, external event bus, or networking SDK | Confirmed | no such package or source dependency |

## Coding Conventions

- Namespace style: `Sundoll.*` namespaces.
- Runtime domain and persistence DTOs remain free of Scene/GameObject references; Unity APIs are limited to the bootstrap/presentation boundary and JSON serialization integration.
- M2 Revision writes use `M2SaveQueue`: the main thread captures an immutable DTO snapshot and a single background chain performs serialization, atomic write and flush. The synchronous `Save()` API remains for compatibility and initialization.
- Persistence invariants and recovery limitations are documented in `Docs/M2-结果报告.md`.

## Testing And Validation

- EditMode tests: 48 first-party tests; 48 passed, 0 failed, 0 ignored in the latest completed Unity Editor run. The suite contains 4 M1, 22 M2 and 22 M3 tests, including Journal v2 command replay, v1/v2 compatibility, Snapshot-after-unsaved-command recovery, background save snapshot isolation, failure status and session tracking, the 256×256 batch save/reload benchmark, geometry rasterizer tests, multi-layer persistence tests, Dirty Region tracking, incremental content-cache tests, visible-bounds tests, layer visibility/lock state tests, and Workspace State round-trip/corruption fallback tests.
- PlayMode validation: M2 save/session flow and the M3 runtime 8×8 grid panel were exercised in `M1Bootstrap`; the runtime overlay now displays save status and Play Mode enter/exit completed without new Console errors. Hiding Terrain removed its symbols from the grid, and a versioned `workspace-state.json` restored Terrain hidden/locked state after exiting and re-entering Play Mode. A click on the locked Terrain produced no new Journal entry and did not advance the current domain Revision. Manual brush/line/rectangle/fill confirmation passed; wheel/middle-button input remains unverified because the available Computer Use surface cannot target those Unity IMGUI events. The 256×256 visible-bounds path is covered by EditMode rather than a large-map runtime scene.
- CI/build validation: no CI configuration detected. M2 has a successful macOS universal build; the latest EditMode XML is `SundollWorld/TestResults_EditMode_20260825_140236.xml`; Windows build, cross-platform save exchange and performance checks remain unverified.

## Available Unity Tooling

| Capability | Status | Evidence |
| --- | --- | --- |
| `unity.connection.status` | unavailable | no Unity MCP bridge detected |
| `unity.editor.version` | available | Unity Editor/Hub and `ProjectVersion.txt` |
| `unity.console.read` | available | Console can be read through local Computer Use; no MCP bridge |
| `unity.scene.list` | available | `EditorBuildSettings.asset` and local Editor |
| `unity.scene.inspect` | available | local Editor/serialized assets; no MCP bridge |
| `unity.buildsettings.read` | available | `ProjectSettings/EditorBuildSettings.asset` |
| `unity.gameobject.inspect` | available | local Editor/serialized scene; no MCP bridge |
| `unity.asset.search` | available | filesystem and Unity Project window |
| `unity.package.read` | available | `Packages/manifest.json` and lock file |
| `unity.tests.list` | available | Unity Test Framework installed; 48 first-party EditMode tests present |
| `unity.tests.run` | available | via Unity Test Framework/Editor; no MCP bridge |
| `unity.playmode.read` | available | local Editor; no MCP bridge |
| `unity.profiler.read` | unverified | M2 performance budget and long-session profiling remain deferred |

## Important Constraints

- Do not copy historical web-project code, data, maps, images, music, or save files.
- Keep formal product code under `Assets/Sundoll/`; keep M0-only code under `M0-Spike`.
- Do not add packages, networking SDKs, DOTS, or DI frameworks without an explicit requirement.
- Domain state must be reconstructable without Scene, GameObject, Transform, or Unity object references.
- M2/M3 persistence is pre-v1; save format compatibility is not promised beyond the current schema.
- Do not claim Windows IL2CPP or Windows durable-write validation from this macOS environment.

## Unknowns And Confidence

- M1 domain schema and exact command set: implementation decision, documented in code and tests.
- Final boot scene composition and runtime UI layout: not established yet.
- Windows Editor/module availability: not verified in the formal project.
- Unity MCP bridge: not installed or configured; repository-only and local Computer Use workflows are available.

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
- `Assets/Sundoll/Tests/EditMode/M3MapEditorTests.cs`
- `Docs/M2-结果报告.md`
- `Docs/M3-结果报告.md`
- root `Unity从零开发工作计划.md`
- root `README.md`
- `M0-Spike/README.md`

<!-- unity-onboarding:generated:end -->
