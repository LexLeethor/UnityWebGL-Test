// --------------------------------------------------------------------------------------------------------------------
// <copyright file="IdbfsTools.cs">
//   Copyright (c) 2024 Johannes Deml. All rights reserved.
// </copyright>
// <author>
//   Johannes Deml
//   public@deml.io
// </author>
// --------------------------------------------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using UnityEngine;

namespace Supyrb
{
	/// <summary>
	/// C# bindings for the IDBFS jslib plugin.
	/// Provides access to Emscripten's IndexedDB filesystem (IDBFS) so data written
	/// to <see cref="Application.persistentDataPath"/> survives page reloads in WebGL.
	///
	/// Usage:
	///   1. Call <see cref="MountAndSync"/> once at startup (e.g. from Awake).
	///      Wait for the callback before reading any persisted files.
	///   2. After every write you care about, call <see cref="SyncToIndexedDb"/>
	///      so the changes are flushed from memory to IndexedDB.
	/// </summary>
	public static class IdbfsTools
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		[DllImport("__Internal")]
		private static extern void _Idbfs_MountAndSync(string path, string callbackObject, string callbackMethod);

		[DllImport("__Internal")]
		private static extern void _Idbfs_SyncToIndexedDb(string callbackObject, string callbackMethod);

		[DllImport("__Internal")]
		private static extern void _Idbfs_GetOriginInfo(string callbackObject, string callbackMethod);

		[DllImport("__Internal")]
		private static extern void _Idbfs_CheckIdbAvailable(string callbackObject, string callbackMethod);

		[DllImport("__Internal")]
		private static extern void _Idbfs_GetStorageEstimate(string callbackObject, string callbackMethod);

		[DllImport("__Internal")]
		private static extern void _Idbfs_RegisterBeforeUnloadSync();
#endif

		// -------------------------------------------------------------------------
		// Core API
		// -------------------------------------------------------------------------

		/// <summary>
		/// Mounts IDBFS at <see cref="Application.persistentDataPath"/> and populates
		/// the in-memory filesystem from IndexedDB so previously-saved data is readable.
		///
		/// Callback result: "MountSuccess[|WARN_...]", "MountAlreadyMounted",
		/// or "MountFailed:&lt;error&gt;".
		/// </summary>
		public static void MountAndSync(string callbackObject, string callbackMethod)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			_Idbfs_MountAndSync(Application.persistentDataPath, callbackObject, callbackMethod);
#elif UNITY_EDITOR && WEBTOOLS_LOG_CALLS
			Debug.Log($"{nameof(IdbfsTools)}.{nameof(MountAndSync)} called (no-op in editor)");
#endif
		}

		/// <summary>
		/// Flushes the in-memory Emscripten filesystem to IndexedDB.
		/// Call after every write you care about.
		///
		/// Callback result: "SyncSuccess", "SyncSkippedNotMounted",
		/// "SyncSkippedInProgress", or "SyncFailed:&lt;error&gt;".
		/// </summary>
		public static void SyncToIndexedDb(string callbackObject, string callbackMethod)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			_Idbfs_SyncToIndexedDb(callbackObject, callbackMethod);
#elif UNITY_EDITOR && WEBTOOLS_LOG_CALLS
			Debug.Log($"{nameof(IdbfsTools)}.{nameof(SyncToIndexedDb)} called (no-op in editor)");
#endif
		}

		// -------------------------------------------------------------------------
		// Diagnostics / edge-case probes
		// -------------------------------------------------------------------------

		/// <summary>
		/// Reports the current window.location.protocol and its expected effect
		/// on IndexedDB persistence (file://, blob:null, https, etc.).
		///
		/// Callback result: "OriginInfo:&lt;origin&gt;|&lt;OK or WARN_...&gt;"
		/// </summary>
		public static void GetOriginInfo(string callbackObject, string callbackMethod)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			_Idbfs_GetOriginInfo(callbackObject, callbackMethod);
#elif UNITY_EDITOR && WEBTOOLS_LOG_CALLS
			Debug.Log($"{nameof(IdbfsTools)}.{nameof(GetOriginInfo)} called (no-op in editor)");
#endif
		}

		/// <summary>
		/// Probes whether IndexedDB is actually openable in the current context.
		/// Catches incognito Safari, opaque blob origins, and restricted environments.
		///
		/// Callback result: "IdbAvailable" or "IdbUnavailable:&lt;reason&gt;"
		/// </summary>
		public static void CheckIdbAvailable(string callbackObject, string callbackMethod)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			_Idbfs_CheckIdbAvailable(callbackObject, callbackMethod);
#elif UNITY_EDITOR && WEBTOOLS_LOG_CALLS
			Debug.Log($"{nameof(IdbfsTools)}.{nameof(CheckIdbAvailable)} called (no-op in editor)");
#endif
		}

		/// <summary>
		/// Calls navigator.storage.estimate() to return current usage and quota.
		/// Useful for catching storage-quota-exceeded conditions before they cause
		/// a silent sync failure.
		///
		/// Callback result: "StorageEstimate:&lt;usageBytes&gt;/&lt;quotaBytes&gt;",
		/// "StorageEstimateUnavailable", or "StorageEstimateFailed:&lt;error&gt;"
		/// </summary>
		public static void GetStorageEstimate(string callbackObject, string callbackMethod)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			_Idbfs_GetStorageEstimate(callbackObject, callbackMethod);
#elif UNITY_EDITOR && WEBTOOLS_LOG_CALLS
			Debug.Log($"{nameof(IdbfsTools)}.{nameof(GetStorageEstimate)} called (no-op in editor)");
#endif
		}

		/// <summary>
		/// Registers a window.beforeunload handler that attempts a best-effort
		/// final sync when the page is closed or navigated away.
		/// WARNING: async operations during beforeunload are NOT guaranteed to
		/// complete — the browser may kill the page before the callback fires.
		/// This is an explicit edge-case test surface.
		/// </summary>
		public static void RegisterBeforeUnloadSync()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			_Idbfs_RegisterBeforeUnloadSync();
#elif UNITY_EDITOR && WEBTOOLS_LOG_CALLS
			Debug.Log($"{nameof(IdbfsTools)}.{nameof(RegisterBeforeUnloadSync)} called (no-op in editor)");
#endif
		}
	}
}
