// --------------------------------------------------------------------------------------------------------------------
// <copyright file="IdbfsCommands.cs">
//   Copyright (c) 2024 Johannes Deml. All rights reserved.
// </copyright>
// <author>
//   Johannes Deml
//   public@deml.io
// </author>
// --------------------------------------------------------------------------------------------------------------------

using System.IO;
using System.Text;
using Supyrb.Attributes;
using UnityEngine;

namespace Supyrb
{
	/// <summary>
	/// Browser console commands for testing IndexedDB persistent storage (IDBFS).
	///
	/// QUICK START (normal flow):
	///   runUnityCommand("MountIdbfs")
	///   runUnityCommand("WriteIdbfsFile", "hello world")
	///   runUnityCommand("SyncIdbfs")
	///   // reload page
	///   runUnityCommand("MountIdbfs")
	///   runUnityCommand("ReadIdbfsFile")    // should print "hello world"
	///
	/// LAUNCHER / file:// DIAGNOSTICS (run these first when testing your launcher):
	///   runUnityCommand("CheckOriginInfo")       // detect file:// vs blob: vs https
	///   runUnityCommand("CheckIdbAvailable")     // probe whether IDB is actually openable
	///   runUnityCommand("CheckStorageQuota")     // log usage/quota
	///   runUnityCommand("RegisterUnloadSync")    // test beforeunload best-effort sync
	///
	/// EDGE CASE TESTS:
	///   runUnityCommand("TestDoubleMountGuard")
	///   runUnityCommand("TestSyncBeforeMount")
	///   runUnityCommand("TestConcurrentSync")
	///   runUnityCommand("TestLargeFileWrite", 5)
	///   runUnityCommand("TestUnicodeContent")
	///   runUnityCommand("TestWriteWithoutSync")
	///   runUnityCommand("TestReadBeforeMount")
	/// </summary>
	public class IdbfsCommands : WebCommands
	{
		private const string TestFileName   = "idbfs_test.txt";
		private const string GameObjectName = "WebBridge";

		private string TestFilePath => Path.Combine(Application.persistentDataPath, TestFileName);

		// =========================================================================
		// Core: Mount / Sync
		// =========================================================================

		/// <summary>
		/// Mount IDBFS at persistentDataPath and populate memory from IndexedDB.
		/// Must be called before any file I/O or sync operations.
		/// Callback: "MountSuccess[|WARN...]", "MountAlreadyMounted", or "MountFailed:..."
		/// </summary>
		[WebCommand(Description = "Mount IDBFS and populate from IndexedDB")]
		[ContextMenu(nameof(MountIdbfs))]
		public void MountIdbfs()
		{
			Debug.Log("[IDBFS] Mounting IDBFS at: " + Application.persistentDataPath);
			IdbfsTools.MountAndSync(GameObjectName, nameof(OnMountResult));
#if UNITY_EDITOR
			OnMountResult("MountSuccess (editor stub)");
#endif
		}

		private void OnMountResult(string result)
		{
			if (result.StartsWith("MountAlreadyMounted"))
				Debug.LogWarning("<color=#e2b714>[IDBFS] Already mounted — double-mount guard triggered.</color>");
			else if (result.StartsWith("MountSuccess"))
			{
				string warning = result.Contains("|") ? result.Substring(result.IndexOf('|') + 1) : null;
				if (warning != null)
					Debug.LogWarning($"<color=#e2b714>[IDBFS] Mount succeeded with warning:\n{warning}</color>");
				else
					Debug.Log($"<color=#3bb508>[IDBFS] Mount succeeded.</color> persistentDataPath: {Application.persistentDataPath}");
			}
			else
				Debug.LogError($"[IDBFS] Mount failed: {result}");
		}

		/// <summary>
		/// Flush the in-memory filesystem to IndexedDB.
		/// Callback: "SyncSuccess", "SyncSkippedNotMounted", "SyncSkippedInProgress", or "SyncFailed:..."
		/// </summary>
		[WebCommand(Description = "Flush in-memory FS to IndexedDB")]
		[ContextMenu(nameof(SyncIdbfs))]
		public void SyncIdbfs()
		{
			Debug.Log("[IDBFS] Syncing to IndexedDB...");
			IdbfsTools.SyncToIndexedDb(GameObjectName, nameof(OnSyncResult));
#if UNITY_EDITOR
			OnSyncResult("SyncSuccess (editor stub)");
#endif
		}

		private void OnSyncResult(string result)
		{
			if (result.StartsWith("SyncSuccess"))
				Debug.Log("<color=#3bb508>[IDBFS] Sync to IndexedDB succeeded.</color>");
			else if (result.StartsWith("SyncSkipped"))
				Debug.LogWarning($"<color=#e2b714>[IDBFS] Sync skipped: {result}</color>");
			else
				Debug.LogError($"[IDBFS] Sync failed: {result}");
		}

		// =========================================================================
		// Core: File I/O
		// =========================================================================

		/// <summary>
		/// Write text to the IDBFS test file. Defaults to a UTC timestamp.
		/// Follow up with SyncIdbfs to persist to IndexedDB.
		/// </summary>
		[WebCommand(Description = "Write text to the IDBFS test file (then call SyncIdbfs)")]
		[ContextMenu(nameof(WriteIdbfsFile))]
		public void WriteIdbfsFile(string content = "")
		{
			if (string.IsNullOrEmpty(content))
				content = $"IDBFS test written at {System.DateTime.UtcNow:O}";
			try
			{
				File.WriteAllText(TestFilePath, content);
				Debug.Log($"<color=#4D65A4>[IDBFS] Wrote file:</color> {TestFilePath}\n<color=#4D65A4>Content:</color> {content}");
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[IDBFS] Write failed: {e.Message}");
			}
		}

		/// <summary>
		/// Read the IDBFS test file and log its content.
		/// Only meaningful after MountIdbfs has completed.
		/// </summary>
		[WebCommand(Description = "Read the IDBFS test file and log its content")]
		[ContextMenu(nameof(ReadIdbfsFile))]
		public void ReadIdbfsFile()
		{
			if (!File.Exists(TestFilePath))
			{
				Debug.LogWarning($"[IDBFS] Test file not found: {TestFilePath}\nWriteIdbfsFile + SyncIdbfs first, then reload and MountIdbfs.");
				return;
			}
			try
			{
				string content = File.ReadAllText(TestFilePath);
				Debug.Log($"<color=#4D65A4>[IDBFS] Read file:</color> {TestFilePath}\n<color=#4D65A4>Content:</color> {content}");
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[IDBFS] Read failed: {e.Message}");
			}
		}

		/// <summary>
		/// Delete the IDBFS test file and sync the deletion to IndexedDB.
		/// </summary>
		[WebCommand(Description = "Delete the IDBFS test file and sync")]
		[ContextMenu(nameof(DeleteIdbfsFile))]
		public void DeleteIdbfsFile()
		{
			if (!File.Exists(TestFilePath))
			{
				Debug.Log("[IDBFS] Test file does not exist, nothing to delete.");
				return;
			}
			try
			{
				File.Delete(TestFilePath);
				Debug.Log($"[IDBFS] Deleted: {TestFilePath}. Syncing...");
				SyncIdbfs();
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[IDBFS] Delete failed: {e.Message}");
			}
		}

		/// <summary>
		/// List all files currently in persistentDataPath.
		/// </summary>
		[WebCommand(Description = "List all files in persistentDataPath")]
		[ContextMenu(nameof(LogIdbfsFiles))]
		public void LogIdbfsFiles()
		{
			var dir = Application.persistentDataPath;
			if (!Directory.Exists(dir))
			{
				Debug.Log($"[IDBFS] persistentDataPath does not exist yet: {dir}");
				return;
			}
			string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
			if (files.Length == 0)
			{
				Debug.Log($"[IDBFS] No files in: {dir}");
				return;
			}
			var sb = new StringBuilder();
			sb.AppendLine($"[IDBFS] Files in {dir} ({files.Length} total):");
			foreach (var file in files)
			{
				var info = new FileInfo(file);
				sb.AppendLine($"  {info.Name}  ({info.Length} bytes, modified {info.LastWriteTimeUtc:O})");
			}
			Debug.Log(sb.ToString());
		}

		/// <summary>
		/// Write to sync to read in a single call. Quick in-memory sanity check.
		/// Does NOT verify cross-reload persistence.
		/// </summary>
		[WebCommand(Description = "Run write to sync to read round-trip in one call")]
		[ContextMenu(nameof(RunIdbfsRoundTripTest))]
		public void RunIdbfsRoundTripTest()
		{
			Debug.Log("[IDBFS] Starting round-trip test...");
			string content = $"Round-trip at {System.DateTime.UtcNow:O}";
			WriteIdbfsFile(content);
			IdbfsTools.SyncToIndexedDb(GameObjectName, nameof(OnRoundTripSyncResult));
#if UNITY_EDITOR
			OnRoundTripSyncResult("SyncSuccess (editor stub)");
#endif
		}

		private void OnRoundTripSyncResult(string result)
		{
			if (!result.StartsWith("SyncSuccess"))
			{
				Debug.LogError($"[IDBFS] Round-trip sync failed: {result}");
				return;
			}
			try
			{
				string readBack = File.ReadAllText(TestFilePath);
				Debug.Log($"<color=#3bb508>[IDBFS] Round-trip PASSED.</color>\n<color=#4D65A4>Read back:</color> {readBack}");
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[IDBFS] Round-trip read failed: {e.Message}");
			}
		}

		// =========================================================================
		// Launcher / file:// diagnostics
		// =========================================================================

		/// <summary>
		/// Detect and report the current window.location.protocol and its expected
		/// effect on IDBFS persistence. Run this first when testing a file:// launcher
		/// or a blob: launcher.
		///
		/// Expected results per origin:
		///   https      - Full persistence, no issues.
		///   http       - Full persistence (same-origin).
		///   file       - Chrome: shared IDB for all local files (works, but shared).
		///                Firefox: per-path origin (IDB empty after reload, BROKEN).
		///                Safari: blocked in some versions.
		///   blob-null  - IDB blocked in Chrome, Firefox, Safari. Data will NOT persist.
		///   blob-file  - Same as blob-null. Blob URLs from file:// pages have opaque origin.
		/// </summary>
		[WebCommand(Description = "Report current origin and its effect on IDBFS persistence")]
		[ContextMenu(nameof(CheckOriginInfo))]
		public void CheckOriginInfo()
		{
			IdbfsTools.GetOriginInfo(GameObjectName, nameof(OnOriginInfo));
#if UNITY_EDITOR
			OnOriginInfo("OriginInfo:editor|OK");
#endif
		}

		private void OnOriginInfo(string result)
		{
			string[] parts = result.Replace("OriginInfo:", "").Split('|');
			string origin  = parts.Length > 0 ? parts[0] : "?";
			string status  = parts.Length > 1 ? parts[1] : "?";
			bool isWarning = status.StartsWith("WARN");
			string color   = isWarning ? "#e2b714" : "#3bb508";
			string label   = isWarning ? "WARNING" : "OK";
			Debug.Log($"<color={color}>[IDBFS] Origin: <b>{origin}</b> — {label}</color>\n{status}");
		}

		/// <summary>
		/// Attempt to open a probe IndexedDB database to verify it is actually
		/// usable. Catches: incognito Safari, opaque blob: origins, Firefox
		/// storage partitioning, and browsers with IDB disabled.
		///
		/// Expected results:
		///   IdbAvailable       - IDB is openable; IDBFS should work.
		///   IdbUnavailable:... - IDB is blocked; IDBFS will silently fail or error.
		/// </summary>
		[WebCommand(Description = "Probe whether IndexedDB is openable in the current context")]
		[ContextMenu(nameof(CheckIdbAvailable))]
		public void CheckIdbAvailable()
		{
			Debug.Log("[IDBFS] Probing IndexedDB availability...");
			IdbfsTools.CheckIdbAvailable(GameObjectName, nameof(OnIdbAvailableResult));
#if UNITY_EDITOR
			OnIdbAvailableResult("IdbAvailable (editor stub)");
#endif
		}

		private void OnIdbAvailableResult(string result)
		{
			if (result.StartsWith("IdbAvailable"))
				Debug.Log($"<color=#3bb508>[IDBFS] {result}</color>");
			else
				Debug.LogError($"[IDBFS] {result}");
		}

		/// <summary>
		/// Query navigator.storage.estimate() and log usage and quota in MB.
		/// Use this to check whether you are approaching the quota before a large
		/// write causes a silent sync failure.
		/// </summary>
		[WebCommand(Description = "Log IndexedDB storage usage and quota")]
		[ContextMenu(nameof(CheckStorageQuota))]
		public void CheckStorageQuota()
		{
			Debug.Log("[IDBFS] Requesting storage estimate...");
			IdbfsTools.GetStorageEstimate(GameObjectName, nameof(OnStorageEstimate));
#if UNITY_EDITOR
			OnStorageEstimate("StorageEstimate:0/0 (editor stub)");
#endif
		}

		private void OnStorageEstimate(string result)
		{
			if (result.StartsWith("StorageEstimate:"))
			{
				string data  = result.Substring("StorageEstimate:".Length);
				string[] parts = data.Split('/');
				if (parts.Length == 2 &&
				    long.TryParse(parts[0], out long usage) &&
				    long.TryParse(parts[1], out long quota))
				{
					float usageMb = usage / (1024f * 1024f);
					float quotaMb = quota / (1024f * 1024f);
					float pct     = quota > 0 ? (usage * 100f / quota) : 0f;
					string color  = pct > 80f ? "#b50808" : (pct > 50f ? "#e2b714" : "#3bb508");
					Debug.Log($"<color={color}>[IDBFS] Storage: {usageMb:0.00} MB used / {quotaMb:0.00} MB quota ({pct:0.0}%)</color>");
				}
				else
				{
					Debug.Log($"[IDBFS] {result}");
				}
			}
			else if (result.StartsWith("StorageEstimateUnavailable"))
				Debug.LogWarning("[IDBFS] navigator.storage.estimate() unavailable (file://, incognito, or old browser).");
			else
				Debug.LogError($"[IDBFS] {result}");
		}

		/// <summary>
		/// Register a window.beforeunload handler that attempts a best-effort sync
		/// when the user closes the tab or navigates away.
		/// WARNING: async IDB operations during beforeunload are NOT guaranteed to
		/// complete. This command surfaces that edge case during testing.
		/// </summary>
		[WebCommand(Description = "Register beforeunload sync (tests data-loss-on-close edge case)")]
		[ContextMenu(nameof(RegisterUnloadSync))]
		public void RegisterUnloadSync()
		{
			IdbfsTools.RegisterBeforeUnloadSync();
			Debug.Log("[IDBFS] beforeunload sync handler registered.\n" +
			          "Close the tab now and reopen to test whether data survived.\n" +
			          "<color=#e2b714>WARNING: completion is NOT guaranteed.</color>");
		}

		// =========================================================================
		// Edge-case tests
		// =========================================================================

		/// <summary>
		/// EDGE CASE: Double-mount guard.
		/// Calls MountIdbfs twice without a reload. The second call must return
		/// "MountAlreadyMounted" rather than crashing or corrupting the FS.
		/// </summary>
		[WebCommand(Description = "Edge case: call MountIdbfs twice — second should be a safe no-op")]
		[ContextMenu(nameof(TestDoubleMountGuard))]
		public void TestDoubleMountGuard()
		{
			Debug.Log("[IDBFS][EdgeCase] Testing double-mount guard...");
			MountIdbfs();
			MountIdbfs(); // expected: MountAlreadyMounted, no crash
		}

		/// <summary>
		/// EDGE CASE: Sync before mount.
		/// Calls SyncIdbfs immediately without calling MountIdbfs first.
		/// Expected: "SyncSkippedNotMounted" — no crash, clear log message.
		/// </summary>
		[WebCommand(Description = "Edge case: SyncIdbfs before mounting — should be a safe no-op")]
		[ContextMenu(nameof(TestSyncBeforeMount))]
		public void TestSyncBeforeMount()
		{
			Debug.Log("[IDBFS][EdgeCase] Testing sync-before-mount...");
			IdbfsTools.SyncToIndexedDb(GameObjectName, nameof(OnSyncBeforeMountResult));
#if UNITY_EDITOR
			OnSyncBeforeMountResult("SyncSkippedNotMounted (editor stub)");
#endif
		}

		private void OnSyncBeforeMountResult(string result)
		{
			if (result.StartsWith("SyncSkippedNotMounted"))
				Debug.Log("<color=#3bb508>[IDBFS][EdgeCase] Sync-before-mount correctly blocked: " + result + "</color>");
			else
				Debug.LogWarning($"[IDBFS][EdgeCase] Unexpected result for sync-before-mount: {result}");
		}

		/// <summary>
		/// EDGE CASE: Concurrent sync calls.
		/// Fires two SyncToIndexedDb calls back-to-back. The second must return
		/// "SyncSkippedInProgress".
		/// </summary>
		[WebCommand(Description = "Edge case: fire two syncs simultaneously — second should be skipped")]
		[ContextMenu(nameof(TestConcurrentSync))]
		public void TestConcurrentSync()
		{
			Debug.Log("[IDBFS][EdgeCase] Testing concurrent sync calls...");
			IdbfsTools.SyncToIndexedDb(GameObjectName, nameof(OnConcurrentSync1Result));
			IdbfsTools.SyncToIndexedDb(GameObjectName, nameof(OnConcurrentSync2Result));
#if UNITY_EDITOR
			OnConcurrentSync1Result("SyncSuccess (editor stub)");
			OnConcurrentSync2Result("SyncSkippedInProgress (editor stub)");
#endif
		}

		private void OnConcurrentSync1Result(string result) =>
			Debug.Log($"[IDBFS][EdgeCase] Concurrent sync #1 result: {result}");

		private void OnConcurrentSync2Result(string result)
		{
			if (result.StartsWith("SyncSkippedInProgress"))
				Debug.Log("<color=#3bb508>[IDBFS][EdgeCase] Concurrent sync #2 correctly skipped.</color>");
			else
				Debug.LogWarning($"[IDBFS][EdgeCase] Concurrent sync #2 unexpected result: {result}");
		}

		/// <summary>
		/// EDGE CASE: Large file write.
		/// Writes approximately <paramref name="sizeMb"/> MB then syncs.
		/// Use 50 or 100 to probe quota limits. A quota-exceeded condition surfaces
		/// as "SyncFailed:..." in the callback.
		/// </summary>
		[WebCommand(Description = "Edge case: write a large file and sync (default 5 MB)")]
		[ContextMenu(nameof(TestLargeFileWrite))]
		public void TestLargeFileWrite(int sizeMb = 5)
		{
			Debug.Log($"[IDBFS][EdgeCase] Writing {sizeMb} MB file...");
			try
			{
				string chunk  = new string('A', 64 * 1024); // 64 KB
				int chunks    = (sizeMb * 1024) / 64;
				using (var sw = new StreamWriter(TestFilePath, false, System.Text.Encoding.UTF8))
					for (int i = 0; i < chunks; i++)
						sw.Write(chunk);

				long bytes = new FileInfo(TestFilePath).Length;
				Debug.Log($"[IDBFS][EdgeCase] Wrote {bytes / (1024f * 1024f):0.00} MB. Syncing...");
				IdbfsTools.SyncToIndexedDb(GameObjectName, nameof(OnLargeFileSyncResult));
#if UNITY_EDITOR
				OnLargeFileSyncResult("SyncSuccess (editor stub)");
#endif
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[IDBFS][EdgeCase] Large file write failed: {e.Message}");
			}
		}

		private void OnLargeFileSyncResult(string result)
		{
			if (result.StartsWith("SyncSuccess"))
				Debug.Log("<color=#3bb508>[IDBFS][EdgeCase] Large file sync succeeded.</color>");
			else
				Debug.LogError($"[IDBFS][EdgeCase] Large file sync failed (quota exceeded?): {result}");
		}

		/// <summary>
		/// EDGE CASE: Unicode / special characters.
		/// Writes emoji, CJK, Arabic, and null-adjacent characters to verify that
		/// Emscripten's UTF-8 path does not corrupt multibyte content in memory.
		/// Follow up with SyncIdbfs + reload to test persistence.
		/// </summary>
		[WebCommand(Description = "Edge case: write Unicode/emoji content and verify in-memory round-trip")]
		[ContextMenu(nameof(TestUnicodeContent))]
		public void TestUnicodeContent()
		{
			string content = "Hello \U0001F30D | \u65E5\u672C\u8A9E\u30C6\u30B9\u30C8 | \u0627\u0644\u0639\u0631\u0628\u064A\u0629 | \u03A9\u2248\u00E7\u221A\u222B | end";
			Debug.Log($"[IDBFS][EdgeCase] Writing Unicode content:\n{content}");
			try
			{
				File.WriteAllText(TestFilePath, content, System.Text.Encoding.UTF8);
				string readBack = File.ReadAllText(TestFilePath, System.Text.Encoding.UTF8);
				if (readBack == content)
					Debug.Log("<color=#3bb508>[IDBFS][EdgeCase] Unicode in-memory round-trip PASSED.</color> Now call SyncIdbfs + reload to test persistence.");
				else
					Debug.LogError($"[IDBFS][EdgeCase] Unicode FAILED.\nExpected: {content}\nGot: {readBack}");
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[IDBFS][EdgeCase] Unicode test failed: {e.Message}");
			}
		}

		/// <summary>
		/// EDGE CASE: Write without sync (data loss demonstration).
		/// Writes a file but does NOT call SyncIdbfs. Reload the page, mount IDBFS,
		/// then call ReadIdbfsFile — the file should be GONE, proving that
		/// in-memory writes alone are not persisted.
		/// </summary>
		[WebCommand(Description = "Edge case: write without syncing — reload to confirm data loss")]
		[ContextMenu(nameof(TestWriteWithoutSync))]
		public void TestWriteWithoutSync()
		{
			string content = $"Unsaved write at {System.DateTime.UtcNow:O}";
			try
			{
				File.WriteAllText(TestFilePath, content);
				Debug.LogWarning("[IDBFS][EdgeCase] Wrote to in-memory FS but did NOT call SyncIdbfs.\n" +
				                 "<color=#e2b714>Reload now, call MountIdbfs, then ReadIdbfsFile.\n" +
				                 "Expected: file NOT found (data lost without sync).</color>");
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[IDBFS][EdgeCase] Write failed: {e.Message}");
			}
		}

		/// <summary>
		/// EDGE CASE: Read before mount completes.
		/// Fires MountIdbfs then immediately reads, racing the async callback.
		/// Expected: file not found (IndexedDB data not yet populated into memory).
		/// </summary>
		[WebCommand(Description = "Edge case: read immediately after triggering mount — races async populate")]
		[ContextMenu(nameof(TestReadBeforeMount))]
		public void TestReadBeforeMount()
		{
			Debug.Log("[IDBFS][EdgeCase] Triggering mount then reading immediately (race condition test)...");
			IdbfsTools.MountAndSync(GameObjectName, nameof(OnMountResult));
			// Intentional immediate read before async populate completes
			if (File.Exists(TestFilePath))
				Debug.LogWarning("[IDBFS][EdgeCase] File found before mount callback — data was already in memory.");
			else
				Debug.Log("<color=#3bb508>[IDBFS][EdgeCase] File not found before mount completed (expected race result).\n" +
				          "Wait for MountSuccess log, then call ReadIdbfsFile.</color>");
		}
	}
}
