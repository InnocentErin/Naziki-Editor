# Naziki Editor Preview Protocol v1

This protocol is independent from `cytoid.game-core.v2`. It is available only in
Windows builds compiled with `CYTOID_EDITOR_HOST`.

## Transport

- One Windows named-pipe connection per Unity process.
- Pipe name, editor session ID, and a random authentication nonce are passed on the
  command line.
- Pipe ACL is restricted by the WPF server to the current Windows user.
- Each frame is a 4-byte little-endian payload length followed by UTF-8 JSON.
- Maximum frame size is 64 MiB.

Every envelope contains:

```json
{
  "protocol": "naziki.editor-preview.v1",
  "Type": "preview.play",
  "SessionId": "project-session",
  "RequestId": "unique-request",
  "EditorVersion": 8,
  "BasePreviewVersion": 7,
  "TargetPreviewVersion": 8,
  "Payload": {}
}
```

Unity ignores other protocols and stale sessions. The editor ignores late ACKs and
does not promote a snapshot to last-known-good until `preview.ack`.

## Lifecycle and data

- `host.ready`, `host.ping`, `host.pong`, `host.shutdown`
- `preview.open`, `preview.replaceSnapshot`, `preview.applyChanges`
- `preview.ack`, `preview.rejected`, `preview.validationFailed`, `preview.error`

Data commands point to an immutable VFS version directory containing valid
`level.json`, `chart.json`, `storyboard.json`, music, background, and storyboard
assets. Unity validates and preloads before switching. An entity update that cannot
be safely applied uses an in-scene atomic storyboard replacement.

## Playback

- `preview.play`, `preview.pause`, `preview.stop`, `preview.seek`
- `preview.scrub.begin`, `preview.scrub.update`, `preview.scrub.commit`
- `preview.clock.set`, `preview.clock.tick`
- `preview.viewport.apply`
- `preview.state`, `preview.time`

Scrub updates are latest-only. The commit is never dropped and includes the state
to restore. During random-access evaluation Unity pauses music, SFX and gameplay
side effects, directly evaluates the storyboard at the requested time, rebuilds the
visible note window, and recomputes deterministic Autoplay score/combo state.

`preview.clock.set` selects `internal` (Unity owns music and time) or `external`
(the editor owns audio and time). External mode pauses Unity audio and accepts
latest-only `preview.clock.tick` messages for side-effect-free evaluation.

`preview.viewport.apply` is an editor-only fast viewport refresh. It pauses the
current preview, applies the new physical window size, waits for stable render
frames, reevaluates the current time, and acknowledges without rebuilding the VFS
or reloading the level.

Reaching the preview duration never enters the production completion flow. Unity
keeps the level loaded and emits `preview.state` with `state: "Paused"`,
`reason: "endOfLevel"`, and the final `time` and `duration`. The editor keeps the
timeline seekable; a play request issued at the end first seeks both clocks to zero.

## Settings and diagnostics

- `preview.settings.apply`
- `preview.performance`

Settings include quality, render scale, target frame rate, inactive frame rate, and
adaptive quality thresholds. Hardware acceleration and Job Worker count are launch
settings and require a process restart.
