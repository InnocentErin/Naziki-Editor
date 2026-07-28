# Naziki Integration Boundary

This directory is the vendored Unity project used by the Naziki Editor preview.

- Upstream: <https://github.com/Cytoid/cytoid-core-unity.git>
- License: GPL-3.0 (`LICENSE`)
- Required Unity version: `6000.0.75f1`
- Imported from the editor repository's pre-existing `.original_player` source
  snapshot on 2026-07-28.

The supplied `.original_player` snapshot did not contain its own `.git` metadata,
so this working-tree migration preserves it as a self-contained vendor boundary
but cannot manufacture an upstream subtree merge commit. Future synchronized
imports should use `git subtree` from the upstream repository into
`External/original_player`; keep Naziki-specific changes focused in the files
documented by `AGENTS.md`.

Build from the Naziki repository root:

```powershell
.\tools\build-original-player.ps1 -Configuration Development
.\tools\build-original-player.ps1 -Configuration Release
```

Build output is intentionally ignored at `Runtime/OriginalPlayer`.
