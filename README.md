# Unity WebGL Loading Test — with IDBFS

![Preview](./preview.png)

[![](https://img.shields.io/github/release-date/JohannesDeml/UnityWebGL-LoadingTest.svg)](https://github.com/JohannesDeml/UnityWebGL-LoadingTest/releases) [![Tested up to Unity 6.3](https://img.shields.io/badge/tested%20up%20to%20unity-6000.3-green.svg?logo=unity&cacheSeconds=2592000)](https://unity3d.com/get-unity/download/archive)

*Testing Unity's WebGL size, loading time, and IndexedDB (IDBFS) persistent storage across Unity versions (2018.4 – 6000.3) and settings.*

## AGENT QUICK-REFERENCE

> This section is optimised for AI agents and automated tooling. Human readers can skip to [Features](#features).

### Repository layout (files added by this fork)

```
Assets/
  Plugins/WebGL/WebTools/
    IdbfsPlugins.jslib      — JS: mount, sync, origin probe, IDB probe, quota, beforeunload
    IdbfsTools.cs           — C# DllImport wrappers for the above
  Scripts/
    IdbfsCommands.cs        — WebCommands subclass; all IDBFS console commands + edge-case tests
```

### Key invariants

| Invariant | Where enforced |
|---|---|
| IDBFS must be mounted before any file I/O | `idbfs_state.mounted` guard in jslib |
| Only one sync may run at a time | `idbfs_state.syncInProgress` guard in jslib |
| Mounting twice is safe | Double-mount guard returns `MountAlreadyMounted` |
| All JS callbacks use `SendMessage(gameObject, method, resultString)` | IdbfsPlugins.jslib |
| Callback result strings are prefixed tokens, never localised text | See [Callback contract](#callback-result-contract) |
| C# code compiles in editor with no-op stubs | `#if UNITY_WEBGL && !UNITY_EDITOR` guards |

### Callback result contract

Every async JS function sends a result string back to Unity via `SendMessage`. The string always starts with a well-known prefix so C# can `StartsWith` it reliably.

| Prefix | Meaning |
|---|---|
| `MountSuccess` | Mount + populate succeeded. May contain `\|WARN_...` suffix. |
| `MountAlreadyMounted` | Mount called when already mounted (safe no-op). |
| `MountFailed:` | Mount or populate failed. Error follows the colon. |
| `SyncSuccess` | Flush to IndexedDB succeeded. |
| `SyncSkippedNotMounted` | Sync called before mount completed. |
| `SyncSkippedInProgress` | Sync called while a previous sync was running. |
| `SyncFailed:` | Sync failed (quota exceeded, IDB blocked, etc.). |
| `OriginInfo:` | Result of `GetOriginInfo`. Format: `OriginInfo:<origin>\|<OK or WARN_...>` |
| `IdbAvailable` | IndexedDB probe succeeded. |
| `IdbUnavailable:` | IndexedDB probe failed. Reason follows the colon. |
| `StorageEstimate:` | Format: `StorageEstimate:<usageBytes>/<quotaBytes>` |
| `StorageEstimateUnavailable` | `navigator.storage.estimate()` not available. |
| `StorageEstimateFailed:` | Estimate threw. Reason follows the colon. |

### Browser console commands — IDBFS

All commands are callable via `runUnityCommand("CommandName")` or `runUnityCommand("CommandName", parameter)`.

#### Diagnostics (run first when testing a launcher)

```javascript
runUnityCommand("CheckOriginInfo");          // detect protocol: https / http / file / blob-null / blob-file
runUnityCommand("CheckIdbAvailable");        // probe whether IndexedDB is openable
runUnityCommand("CheckStorageQuota");        // navigator.storage.estimate() → usage/quota in MB
runUnityCommand("RegisterUnloadSync");       // register beforeunload best-effort sync handler
```

#### Core flow

```javascript
runUnityCommand("MountIdbfs");               // step 1 — always call first, wait for MountSuccess
runUnityCommand("WriteIdbfsFile", "text");   // step 2 — write a file (defaults to UTC timestamp)
runUnityCommand("SyncIdbfs");               // step 3 — flush to IndexedDB
// reload page
runUnityCommand("MountIdbfs");               // step 4 — repopulate from IndexedDB
runUnityCommand("ReadIdbfsFile");            // step 5 — verify "text" survived the reload
runUnityCommand("LogIdbfsFiles");            // list all files in persistentDataPath
runUnityCommand("DeleteIdbfsFile");          // delete test file and sync
runUnityCommand("RunIdbfsRoundTripTest");    // write + sync + read in one call (no reload)
```

#### Edge-case tests

```javascript
runUnityCommand("TestDoubleMountGuard");     // mount twice → second call returns MountAlreadyMounted
runUnityCommand("TestSyncBeforeMount");      // sync without mounting → SyncSkippedNotMounted
runUnityCommand("TestConcurrentSync");       // two syncs back-to-back → second returns SyncSkippedInProgress
runUnityCommand("TestLargeFileWrite", 5);    // write N MB then sync; increase N to probe quota limit
runUnityCommand("TestUnicodeContent");       // emoji + CJK + Arabic UTF-8 round-trip
runUnityCommand("TestWriteWithoutSync");     // write but no sync → reload → data gone (expected)
runUnityCommand("TestReadBeforeMount");      // read immediately after triggering mount → races async populate
```

### Known origin / IDBFS behaviour matrix

| Launch method | Origin | Chrome | Firefox | Safari |
|---|---|---|---|---|
| `https://` server | `https` | ✅ Full persistence | ✅ Full persistence | ✅ Full persistence |
| `http://` local server | `http` | ✅ Full persistence | ✅ Full persistence | ✅ Full persistence |
| Open `file://` directly | `file` | ⚠️ Shared IDB for ALL local files; works but data is shared across every local HTML file | ❌ Per-path origin; IDB is empty after every reload | ❌ Blocked in many Safari versions |
| Blob launcher on `https://` page | `blob:https://...` | ✅ Inherits https origin | ✅ Inherits https origin | ✅ Inherits https origin |
| Blob launcher on `file://` page | `blob:null` or `blob:file://` | ❌ Opaque/null origin; IDB blocked | ❌ IDB blocked | ❌ IDB blocked |
| Incognito / private mode | any | ⚠️ IDB works but cleared on window close | ⚠️ IDB works but cleared on window close | ❌ IDB blocked |

**Key rule:** If your launcher opens a blob URL whose parent page is on `file://`, the blob gets a `null` origin and IndexedDB is blocked in every major browser. You must serve the launcher from `http://localhost` or `https://` to get a real origin.

### Edge cases catalogue

The following edge cases are tested explicitly by commands in `IdbfsCommands.cs` and guarded in `IdbfsPlugins.jslib`.

| Edge case | Command | Expected result |
|---|---|---|
| Double-mount (mount called twice) | `TestDoubleMountGuard` | Second call returns `MountAlreadyMounted`; no FS corruption |
| Sync before mount completes | `TestSyncBeforeMount` | Returns `SyncSkippedNotMounted`; no crash |
| Concurrent sync calls | `TestConcurrentSync` | Second returns `SyncSkippedInProgress`; no race corruption |
| Large file exceeds quota | `TestLargeFileWrite 100` | Returns `SyncFailed:...`; error surfaced cleanly |
| Unicode / emoji content | `TestUnicodeContent` | Content round-trips byte-for-byte via UTF-8 |
| Write without sync then reload | `TestWriteWithoutSync` | File not found after reload (data loss confirmed) |
| Read immediately after mount trigger | `TestReadBeforeMount` | File not found (async populate not yet complete) |
| Blob URL origin (from file:// page) | `CheckOriginInfo` | Reports `WARN_BLOB_ORIGIN`; IDB probe returns `IdbUnavailable` |
| `file://` origin in Firefox | `CheckOriginInfo` | Reports `WARN_FILE_ORIGIN` |
| Incognito / private mode | `CheckIdbAvailable` | Returns `IdbUnavailable:open failed` in Safari; may succeed in Chrome/Firefox but data clears on window close |
| Page closed before sync completes | `RegisterUnloadSync` then close tab | `beforeunload` handler fires; completion not guaranteed; data may be lost |
| Storage quota nearing limit | `CheckStorageQuota` | Logs usage/quota %; red if >80% |

---

## Features

* Toggle-able in-DOM debug console
* Unity rich text styling support for browser console and debug console
* Easy access to Unity functions through the browser console
* Handy debug functions for timing and memory
* **IDBFS persistent storage tests** — mount, sync, read/write, and a full suite of edge-case diagnostics
* Responsive template layout for mobile compatibility
* GitHub Actions for automated builds via [Game CI](https://game.ci/)
* Tracking different Unity versions from 2018.4 (700+ live demo builds)
* Brotli compression
* Build targets: WebGL1, WebGL2, WebGPU with BiRP and URP

## Live Demos ([All Builds](https://deml.io/experiments/unity-webgl/))

* [Overview of all builds](https://deml.io/experiments/unity-webgl/)
* [Implementation in Godot](https://github.com/JohannesDeml/Godot-Web-LoadingTest)
* [Unity Forum Thread](https://forum.unity.com/threads/webgl-builds-for-mobile.545877/)

## IDBFS Implementation

### File layout

| File | Purpose |
|---|---|
| `Assets/Plugins/WebGL/WebTools/IdbfsPlugins.jslib` | Emscripten JS library. Implements mount, sync, origin detection, IDB probe, storage estimate, and beforeunload handler. |
| `Assets/Plugins/WebGL/WebTools/IdbfsTools.cs` | C# static class with `[DllImport]` bindings to the jslib. No-op stubs for the Editor. |
| `Assets/Scripts/IdbfsCommands.cs` | `WebCommands` subclass. Auto-registered by `WebBridge`. Exposes all commands to the browser console. |

### How IDBFS works in Unity WebGL

Unity WebGL uses Emscripten's in-memory virtual filesystem (MEMFS) by default. Data written to `Application.persistentDataPath` exists only in memory and is lost on page reload. To persist data across reloads you must:

1. Mount `IDBFS` (Emscripten's IndexedDB-backed FS) at `persistentDataPath`.
2. Call `FS.syncfs(true, cb)` to populate memory from IndexedDB (do this once at startup, wait for the callback before reading files).
3. After every write, call `FS.syncfs(false, cb)` to flush memory to IndexedDB.

`IdbfsTools.MountAndSync` and `IdbfsTools.SyncToIndexedDb` wrap these two operations.

### Integration in your own project

```csharp
// In Awake or Start:
IdbfsTools.MountAndSync("MyGameObject", "OnMountDone");

// After mount callback fires:
void OnMountDone(string result)
{
    if (result.StartsWith("MountSuccess"))
    {
        // Safe to read persisted files now
        string data = File.ReadAllText(Path.Combine(Application.persistentDataPath, "save.dat"));
    }
}

// After every save:
File.WriteAllText(Path.Combine(Application.persistentDataPath, "save.dat"), myData);
IdbfsTools.SyncToIndexedDb("MyGameObject", "OnSyncDone");
```

## Browser console — all commands

### IdbfsCommands

```javascript
// Diagnostics
runUnityCommand("CheckOriginInfo");
runUnityCommand("CheckIdbAvailable");
runUnityCommand("CheckStorageQuota");
runUnityCommand("RegisterUnloadSync");

// Core IDBFS flow
runUnityCommand("MountIdbfs");
runUnityCommand("SyncIdbfs");
runUnityCommand("WriteIdbfsFile", "string content");
runUnityCommand("ReadIdbfsFile");
runUnityCommand("DeleteIdbfsFile");
runUnityCommand("LogIdbfsFiles");
runUnityCommand("RunIdbfsRoundTripTest");

// Edge-case tests
runUnityCommand("TestDoubleMountGuard");
runUnityCommand("TestSyncBeforeMount");
runUnityCommand("TestConcurrentSync");
runUnityCommand("TestLargeFileWrite", int sizeMb);
runUnityCommand("TestUnicodeContent");
runUnityCommand("TestWriteWithoutSync");
runUnityCommand("TestReadBeforeMount");
```

### CommonCommands

```javascript
runUnityCommand("AllocateByteArrayMemory", int mb);
runUnityCommand("CheckOnlineStatus");
runUnityCommand("CopyToClipboard", "string text");
runUnityCommand("DeleteAllPlayerPrefs");
runUnityCommand("DisableCaptureAllKeyboardInput");
runUnityCommand("EnableCaptureAllKeyboardInput");
runUnityCommand("FindGameObjectByName", "string name");
runUnityCommand("LogExampleMessages");
runUnityCommand("LogInitializationTime");
runUnityCommand("LogMemory");
runUnityCommand("LogMessage", "string message");
runUnityCommand("LogShaderCompilation", int enabled);
runUnityCommand("LogTextureSupport");
runUnityCommand("LogUserAgent");
runUnityCommand("ReleaseByteArrayMemory");
runUnityCommand("SaveScreenshot");
runUnityCommand("SaveScreenshotSuperSize", int superSize);
runUnityCommand("SetApplicationRunInBackground", int runInBackground);
runUnityCommand("SetApplicationTargetFrameRate", int targetFrameRate);
runUnityCommand("SetTimeFixedDeltaTime", float fixedDeltaTime);
runUnityCommand("SetTimeTimeScale", float timeScale);
runUnityCommand("ThrowDictionaryException");
runUnityCommand("ToggleInfoPanel");
runUnityCommand("TriggerGarbageCollection");
runUnityCommand("UnloadUnusedAssets");
```

### ObjectSpawnerCommands

```javascript
runUnityCommand("AddSpawner");
runUnityCommand("PauseSpawning");
runUnityCommand("RemoveSpawner");
runUnityCommand("ResumeSpawning");
```

### WebBridge

```javascript
runUnityCommand("Help");   // log all available commands
```

## Platform Compatibility

| Platform | Chrome | Firefox | Edge | Safari | IE |
|---|:---:|:---:|:---:|:---:|:---:|
| Windows 10 | ✅ | ✅ | ✅ | ➖ | ❌ |
| Linux | ✅ | ✅ | ✅ | ➖ | ➖ |
| Mac | ✅ | ✅ | ✅ | ✅ | ➖ |
| Android | ✅ | ✅ | ✅ | ➖ | ➖ |
| iOS | ✅ | ✅ | ✅ | ✅ | ➖ |
| Android Smart TV | ✅ | ➖ | ➖ | ➖ | ➖ |

✅ Supported | ⚠️ Warning | ❌ Not supported | ➖ Not applicable

## Notes

* **Brotli compression** — the server must send `Content-Encoding: br`. Without it you will see: `Unable to parse Build/WEBGL.framework.js.br!`. Switch to gzip in Project Settings if your host does not support brotli.
* **iOS** — URP + WebGL1 does not work. BiRP + WebGL2 has performance issues. Use URP + WebGL2 or BiRP + WebGL1 for iOS.
* **Internet Explorer** — not supported (no WASM).
* **Android Smart TV** — expect load times up to ~2 minutes.
* **IDBFS on file:// (Chrome)** — data is written to a single shared IndexedDB origin for all local files. Your game's data is not isolated from other local WebGL games on the same machine.
* **IDBFS on blob: from file://** — IndexedDB is blocked. You must serve from `http://localhost` or `https://` for persistence to work.

## GitHub Build Actions

Continuous integration via [game.ci](https://game.ci/).

* **[release.yml](./.github/workflows/release.yml)** — build and deploy on tag push. Supported tag suffixes: `minsize`, `debug`, `webgl1`, `webgl2`, `webgpu`. Example tag: `6000.0.0f1-urp-webgl2`.
* **[upgrade-unity.yml](./.github/workflows/upgrade-unity.yml)** — manually triggered; upgrades Unity version, updates packages, opens a PR.

## License

MIT (c) Johannes Deml — see [LICENSE](./LICENSE)
