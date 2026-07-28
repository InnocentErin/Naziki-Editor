# Naziki Editor Architecture

The source tree is organized by editor capability. Physical paths intentionally
do not force namespace changes; the existing namespaces remain stable for
compatibility.

## Feature modules

- `Features/EditorShell`: main window composition, shell commands, status, recent
  projects, and persisted dock layouts.
- `Features/Project`: project hub, project persistence, workspace conflict
  handling, project state, and project models.
- `Features/Storyboard`: storyboard models, repositories, compilation,
  validation, corrections, templates, and event-list views.
- `Features/Timeline`: main and micro timelines, event blocks, timeline
  projections/editing, view models, and rendering helpers.
- `Features/PropertyEditing`: property panel, full property editor, dynamic
  editor controls, converters, and property metadata services.
- `Features/Assets`: asset scanning, metadata, view models, asset browser, and
  asset drag sources.
- `Features/Chart`: chart models and logic, note list, note selection, and note
  rendering.
- `Features/Audio`: audio engine and its application adapter.
- `Features/Settings`: settings storage, settings UI, and theme management.
- `Features/Preview`: immutable preview snapshots, versioned change feed,
  preview-host contracts, Canvas, and JSON source editing.
- `Features/Editing`: editor mutation transactions shared by feature modules.

## Shared modules

- `Shared/Input`: selection, drag payloads, input sessions, and reusable WPF
  interaction behaviors.
- `Shared/Abstractions`: stable cross-feature interfaces.
- `Shared/Application`: global commands and editor coordination.
- `Shared/Core`: messaging, history, errors, notifications, shortcuts, and
  general helpers.
- `Shared/UI`: common dialogs, notifications, and WPF services.
- `Shared/Rendering`: the shared render tick engine.

## Dependency rules

1. A feature may depend on `Shared` and public contracts from another feature,
   but not on another feature's concrete WPF controls.
2. Business mutations go through `IEditorMutationService`; UI-only preview
   movement must not create history entries until committed.
3. Cross-feature selection uses `ISelectionService`. Cross-feature drag and
   drop uses `IEditorDragPayload` and typed drop handlers.
4. Preview consumers use `IStoryboardPreviewDataSource` and
   `IStoryboardChangeFeed`; they must never retain mutable objects from
   `ProjectDataContext`.
5. Persisted dock layouts use stable `ContentId` values and restore only known
   panes.

## Native Canvas preview

`Features/Preview` owns the editor side of the Unity integration:

- `UnityPreviewHwndHost` supplies the child HWND used by Unity's supported
  `-parentHWND ... delayed` launch mode.
- `UnityPreviewProcessService` owns exactly one Windows x64 child process,
  waits for Unity's `GWLP_USERDATA` graphics-ready marker, reparents rebuilt
  dock HWNDs, and terminates only its own child after graceful shutdown timeout.
- `NamedPipeUnityPreviewTransport` implements current-user-only,
  length-prefixed `naziki.editor-preview.v1` messages with a per-launch nonce.
- `PreviewValidationService` blocks invalid storyboard/chart/assets before any
  VFS version becomes visible to Unity.
- `PreviewVfsMaterializer` writes immutable version directories using atomic
  JSON replacement, hard links where possible, content hashes, protected
  last-known-good versions, and bounded cache pruning.
- `UnityStoryboardPreviewHost` serializes update/ACK flow, preserves the last
  accepted version, coalesces scrub updates, and recovers one time after a
  process failure.

The Unity source is isolated under `External/original_player`; the WPF project
explicitly excludes all Unity scripts and assets from MSBuild item discovery.
Development and release players are built with:

```powershell
.\tools\build-original-player.ps1 -Configuration Development
.\tools\build-original-player.ps1 -Configuration Release
```

Unity `6000.0.75f1` with Windows Build Support is mandatory. The resulting
`Runtime/OriginalPlayer` directory is local build output and is not committed.

## Verification

Run:

```powershell
dotnet build "Naziki Editor.csproj" --no-restore
dotnet test "Tests\Naziki.Editor.Timeline.Tests.csproj" --no-restore
```

The pre-refactor warning baseline is retained; new work must not introduce
build errors or test regressions.
