# Agent Guide

## Repo Direction

This repo is being reworked into a Unity-first Luau engine. Treat `src/Luau.Unity/Assets/Luau.Unity` as the product surface. The .NET projects remain as a fast build/test harness and as sources for generated/copied Unity artifacts.

The native Luau VM stays. "Drop .NET native stuff" means removing the generic NuGet/.NET product path, not replacing the Luau C++ VM.

## Major Areas

- `luau/`: Luau C++ submodule. It may be uninitialized in fresh checkouts.
- `native/luau-ffi/`: Rust/CMake bridge that builds Luau VM/compiler/require and exports the `ffi_*` ABI consumed by C#.
- `src/Luau.Native/`: low-level generated/native interop used by the .NET harness.
- `src/Luau/`: high-level managed runtime wrapper used by tests and copied into Unity as `Luau.dll`.
- `src/Luau.SourceGenerator/`: Roslyn generators for `CreateFunction` and `[LuauLibrary]`.
- `src/Luau.Unity/Assets/Luau.Unity/`: Unity package runtime/editor/native plugin surface.
- `tests/Luau.Tests/`: primary fast validation suite outside Unity.
- `tools/legacy/Luau.Cli/` and `tools/legacy/ConsoleApp1/`: legacy .NET product/sample areas; do not extend them for Unity runtime work.

## Native Build Path

Luau C++ source is built by `native/luau-ffi/build.rs`. The Rust crate links Luau static libraries and emits generated C# bindings into both `src/Luau.Native` and `src/Luau.Unity/Assets/Luau.Unity/Native`.

Do not hand-edit generated binding files unless the task is explicitly to patch generated output. Prefer regenerating through the native build.

## Unity Package

Required native plugin targets:

- `win-x64/libluau.dll`: Windows Editor and Win64 player.
- `android-arm64/libluau.so`: Android ARM64 player.
- `android-x64/libluau.so`: Android emulator support when it remains low-cost.

Optional platform artifacts can remain as long as they do not block Windows or Android work. Keep plugin `.meta` platform settings in sync with native binaries.

Unity resolves native plugins through Unity plugin importer metadata. Do not add the generic .NET `DllImportResolver` path to Unity runtime code.

## Build And Test Commands

Use this from the repo root:

```powershell
dotnet test Luau.slnx --no-restore
```

Normal .NET build/test must not rewrite Unity package artifacts. To intentionally refresh managed Unity artifacts after building, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Copy-DotNetArtifactsToUnity.ps1 -Configuration Debug
```

The copy script does not overwrite Unity-specific generated native bindings. Regenerate those through `native/luau-ffi/build.rs`.

## Unity Control Protocol

The Unity project at `src/Luau.Unity` is currently opened with Unity `6000.3.19f1` and is available for automated inspection and testing through UCP.

Before doing Unity-side work, read and follow `.agents/skills/unity-control-protocol/SKILL.md`. Run `ucp` commands with `src/Luau.Unity` as the working directory so project discovery works without `--project`:

```powershell
Push-Location src/Luau.Unity
ucp doctor
ucp compile
Pop-Location
```

Do not set a user- or machine-wide `UCP_PROJECT`; this repository is not itself the Unity project root, and a global setting would affect unrelated projects.

## Agent Rules

- Keep Unity runtime changes focused under `src/Luau.Unity/Assets/Luau.Unity`.
- Keep cross-platform VM behavior covered in `tests/Luau.Tests` where possible before adding Unity-only tests.
- Do not commit Unity generated folders such as `Library/`, `Temp/`, `Logs/`, or `Assets/**/obj/`.
- Do not extend NuGet packaging or the CLI unless the user explicitly asks for legacy .NET distribution work.
- If changing native plugin binaries, update the matching `.meta` files and document which platforms were rebuilt.
