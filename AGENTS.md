# Agent Guide

## Repo Direction

This repo is being reworked into a Unity-first Luau engine. Treat `src/Luau.Unity/Assets/Luau.Unity` as the product surface. The .NET projects remain as a fast build/test harness and as sources for intentionally copied Unity artifacts.

The native Luau VM stays. "Drop .NET native stuff" means removing the generic NuGet/.NET product path, not replacing the Luau C++ VM.

## Major Areas

- `luau/`: Luau C++ submodule. It may be uninitialized in fresh checkouts.
- `native/luau-host/`: sole native CMake build. It links the Luau VM/compiler and exports the narrow, versioned `luau_host_*` ABI.
- `src/Luau.Native/`: handwritten low-level `luau_host` interop used by the .NET harness. It is not a consumer API.
- `src/Luau/`: high-level managed runtime wrapper used by tests and copied into Unity as `Luau.dll`.
- `src/Luau.SourceGenerator/`: Roslyn generators for `CreateFunction` and `[LuauLibrary]`.
- `src/Luau.Unity/Assets/Luau.Unity/`: Unity package runtime/editor/native plugin surface.
- `tests/Luau.Tests/`: primary fast validation suite outside Unity.
- `tools/legacy/Luau.Cli/` and `tools/legacy/ConsoleApp1/`: legacy .NET product/sample areas; do not extend them for Unity runtime work.

## Native Build Path

`native/luau-host/CMakeLists.txt` is the only native build entry point. It verifies the exact clean Luau submodule revision, links the required upstream static libraries, builds `luau_host`, and audits the final binary against the checked-in 85-symbol export allowlist.

`native/luau-host/include/luau_host.h` is the native/managed contract. The corresponding C# declarations are deliberately handwritten in `src/Luau.Native/LuauHost.*` and copied into the Unity package. Keep the header, both C# copies, ABI/layout tests, and export allowlist in sync. There is no binding-generation step.

Supported native presets, run from `native/luau-host`, are:

```powershell
cmake --preset windows-x64
cmake --build --preset windows-x64 --parallel
ctest --preset windows-x64

cmake --preset android-arm64
cmake --build --preset android-arm64 --parallel

cmake --preset android-x64
cmake --build --preset android-x64 --parallel
```

Android configuration uses `ANDROID_NDK_HOME` and NDK r27.2.12479018. Do not add an alternate native build system or restore a broad upstream C API export surface.

## Unity Package

Required native plugin targets:

- `win-x64/luau_host.dll`: Windows Editor and Win64 player.
- `android-arm64/libluau_host.so`: Android ARM64 player.
- `android-x64/libluau_host.so`: Android x64 emulator.

Windows x64 and Android ARM64/x64 are the currently maintained targets. Do not imply support for stale plugins from other platforms. Keep plugin `.meta` platform settings in sync with native binaries.

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

The copy script also copies the reviewed handwritten host declarations; it does not build or install native plugins. Build native binaries with the CMake presets and install them deliberately.

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
