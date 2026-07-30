# POM-Max Development Rules

## Build

- Run `powershell -ExecutionPolicy Bypass -File .\Build.ps1`.
- Game references are read from the local ADOFAI installation configured by `Build.ps1`.

## Validation

- Validate `Info.json` as JSON.
- Inspect the produced `POMMax.dll` with ILSpy when available.
- Run `git diff --check` and review `git status --short`.
- Runtime validation must use UMM logs and a real custom level; compilation alone is not runtime proof.

## Git

- Do not commit build outputs, caches, logs, game files, or `.codegraph/`.
- Do not overwrite unrelated local changes.
- Do not push unless the user explicitly requests it.

## Safety

- Do not modify base game assemblies or assets.
- Back up existing Mod files and UMM state before installation.
- Do not modify Together binaries or encrypted caches.
