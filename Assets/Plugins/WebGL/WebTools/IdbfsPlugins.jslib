var IdbfsPlugins =
{
    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    // Tracks whether IDBFS has been successfully mounted.
    // Guards against double-mount and sync-before-mount edge cases.
    $idbfs_state: {
        mounted: false,
        syncInProgress: false
    },

    // -------------------------------------------------------------------------
    // Helpers (injected into every function via $idbfs_state dependency)
    // -------------------------------------------------------------------------

    // Check whether IDBFS is already mounted at a given path.
    $idbfs_isMountedAt: function(path) {
        if (typeof FS === 'undefined' || !FS || !FS.mounts) return false;
        for (var i = 0; i < FS.mounts.length; i++) {
            if (FS.mounts[i].mountpoint === path) return true;
        }
        return false;
    },

    // Detect the effective storage origin and return a human-readable tag.
    // Returns one of: 'http', 'https', 'file', 'blob-null', 'blob-file', 'unknown'
    $idbfs_detectOrigin: function() {
        var protocol = window.location.protocol;
        var href = window.location.href;
        if (protocol === 'https:') return 'https';
        if (protocol === 'http:')  return 'http';
        if (protocol === 'file:')  return 'file';
        if (protocol === 'blob:') {
            // blob URLs created from a file:// page have href like "blob:null/..."
            // or "blob:file:///..." depending on the browser.
            if (href.indexOf('blob:null') === 0 || href.indexOf('blob:file') === 0) return 'blob-file';
            return 'blob-null';
        }
        return 'unknown';
    },

    // Returns a warning string if the current origin is known to be hostile to
    // IndexedDB persistence, or null if everything looks fine.
    $idbfs_originWarning: function() {
        var origin = idbfs_detectOrigin();
        if (origin === 'blob-null' || origin === 'blob-file') {
            return 'WARN_BLOB_ORIGIN: Blob URLs have an opaque/null origin. ' +
                   'IndexedDB is blocked in most browsers (Chrome, Firefox, Safari). ' +
                   'Data will NOT persist across reloads. ' +
                   'Load via http(s):// or a local server instead.';
        }
        if (origin === 'file') {
            return 'WARN_FILE_ORIGIN: file:// origin detected. ' +
                   'Chrome shares one IndexedDB origin for all local files; persistence works but is shared. ' +
                   'Firefox treats each file path as a unique origin; IndexedDB data may be empty after reload. ' +
                   'Safari blocks IndexedDB on file:// entirely in some versions.';
        }
        return null;
    },

    // -------------------------------------------------------------------------
    // _Idbfs_GetOriginInfo
    // Returns a diagnostic string describing the current origin and its
    // expected IndexedDB behaviour. Sends result via SendMessage.
    // -------------------------------------------------------------------------
    _Idbfs_GetOriginInfo: function(callbackObjectPtr, callbackMethodPtr) {
        function toString(ptr) {
            if (typeof UTF8ToString !== 'undefined') return UTF8ToString(ptr);
            if (typeof Pointer_stringify !== 'undefined') return Pointer_stringify(ptr);
            return ptr;
        }

        var callbackObject = toString(callbackObjectPtr);
        var callbackMethod = toString(callbackMethodPtr);

        var origin = idbfs_detectOrigin();
        var warning = idbfs_originWarning();
        var result = 'OriginInfo:' + origin + (warning ? '|' + warning : '|OK');

        console.log('[IDBFS] Origin check:', result);
        if (callbackObject && callbackMethod) {
            SendMessage(callbackObject, callbackMethod, result);
        }
    },

    // -------------------------------------------------------------------------
    // _Idbfs_CheckIdbAvailable
    // Probes whether IndexedDB is actually usable (blocked in incognito on some
    // browsers, or when storage is partitioned). Sends 'IdbAvailable' or
    // 'IdbUnavailable:<reason>' via SendMessage.
    // -------------------------------------------------------------------------
    _Idbfs_CheckIdbAvailable: function(callbackObjectPtr, callbackMethodPtr) {
        function toString(ptr) {
            if (typeof UTF8ToString !== 'undefined') return UTF8ToString(ptr);
            if (typeof Pointer_stringify !== 'undefined') return Pointer_stringify(ptr);
            return ptr;
        }

        var callbackObject = toString(callbackObjectPtr);
        var callbackMethod = toString(callbackMethodPtr);

        if (typeof indexedDB === 'undefined' || indexedDB === null) {
            var msg = 'IdbUnavailable:indexedDB is undefined (incognito, file://, or old browser)';
            console.error('[IDBFS]', msg);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
            return;
        }

        // Try opening a probe database. This fails in incognito Safari and
        // when the origin is opaque (blob: with null origin).
        var probe = indexedDB.open('__idbfs_probe__', 1);
        probe.onerror = function(e) {
            var msg = 'IdbUnavailable:open failed: ' + (e.target.error ? e.target.error.message : 'unknown');
            console.error('[IDBFS]', msg);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
        };
        probe.onsuccess = function(e) {
            e.target.result.close();
            // Clean up probe db
            try { indexedDB.deleteDatabase('__idbfs_probe__'); } catch(_) {}
            var msg = 'IdbAvailable';
            console.log('[IDBFS]', msg);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
        };
        probe.onblocked = function() {
            var msg = 'IdbUnavailable:open blocked';
            console.warn('[IDBFS]', msg);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
        };
    },

    // -------------------------------------------------------------------------
    // _Idbfs_MountAndSync
    // Mount IDBFS at the given path and populate memory from IndexedDB.
    // Guards: double-mount, blob/file:// origin warning.
    // Sends 'MountSuccess', 'MountAlreadyMounted', or 'MountFailed:<err>'.
    // -------------------------------------------------------------------------
    _Idbfs_MountAndSync: function(pathPtr, callbackObjectPtr, callbackMethodPtr) {
        function toString(ptr) {
            if (typeof UTF8ToString !== 'undefined') return UTF8ToString(ptr);
            if (typeof Pointer_stringify !== 'undefined') return Pointer_stringify(ptr);
            return ptr;
        }

        var path = toString(pathPtr);
        var callbackObject = toString(callbackObjectPtr);
        var callbackMethod = toString(callbackMethodPtr);

        // Guard: double-mount (our own state)
        if (idbfs_state.mounted) {
            var msg = 'MountAlreadyMounted';
            console.warn('[IDBFS] MountAndSync called but IDBFS is already mounted at', path);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
            return;
        }

        // Warn about hostile origins (does not abort — let it try and report)
        var originWarn = idbfs_originWarning();
        if (originWarn) {
            console.warn('[IDBFS]', originWarn);
        }

        // If Unity (or another lib) already mounted IDBFS at this path,
        // skip FS.mount and just populate from IndexedDB.
        if (idbfs_isMountedAt(path)) {
            console.warn('[IDBFS] IDBFS already mounted externally at', path);
            // true = populate (IndexedDB -> memory)
            FS.syncfs(true, function(err) {
                if (err) {
                    var msg = 'MountFailed:syncfs populate failed: ' + err;
                    console.error('[IDBFS]', msg);
                    if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
                } else {
                    idbfs_state.mounted = true;
                    var result = 'MountSuccess' + (originWarn ? '|' + originWarn : '|WARN_ALREADY_MOUNTED');
                    console.log('[IDBFS] Populated from IndexedDB (pre-mounted):', path);
                    if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, result);
                }
            });
            return;
        }

        try { FS.mkdir(path); } catch(e) {}

        try {
            FS.mount(IDBFS, {}, path);
        } catch(mountErr) {
            var msg = 'MountFailed:FS.mount threw: ' + mountErr;
            console.error('[IDBFS]', msg);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
            return;
        }

        // true = populate (IndexedDB -> memory)
        FS.syncfs(true, function(err) {
            if (err) {
                var msg = 'MountFailed:syncfs populate failed: ' + err;
                console.error('[IDBFS]', msg);
                if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
            } else {
                idbfs_state.mounted = true;
                var result = 'MountSuccess' + (originWarn ? '|' + originWarn : '');
                console.log('[IDBFS] Mounted and populated from IndexedDB:', path);
                if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, result);
            }
        });
    },

    // -------------------------------------------------------------------------
    // _Idbfs_SyncToIndexedDb
    // Flush in-memory FS to IndexedDB (memory -> IndexedDB).
    // Guards: sync-before-mount, concurrent sync calls.
    // Sends 'SyncSuccess', 'SyncSkippedNotMounted', 'SyncSkippedInProgress',
    // or 'SyncFailed:<err>'.
    // -------------------------------------------------------------------------
    _Idbfs_SyncToIndexedDb: function(callbackObjectPtr, callbackMethodPtr) {
        function toString(ptr) {
            if (typeof UTF8ToString !== 'undefined') return UTF8ToString(ptr);
            if (typeof Pointer_stringify !== 'undefined') return Pointer_stringify(ptr);
            return ptr;
        }

        var callbackObject = toString(callbackObjectPtr);
        var callbackMethod = toString(callbackMethodPtr);

        // Guard: sync before mount
        if (!idbfs_state.mounted) {
            var msg = 'SyncSkippedNotMounted';
            console.warn('[IDBFS] SyncToIndexedDb called before MountAndSync completed.');
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
            return;
        }

        // Guard: concurrent sync
        if (idbfs_state.syncInProgress) {
            var msg = 'SyncSkippedInProgress';
            console.warn('[IDBFS] SyncToIndexedDb called while a sync is already in progress. Skipping.');
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
            return;
        }

        idbfs_state.syncInProgress = true;

        // false = flush (memory -> IndexedDB)
        FS.syncfs(false, function(err) {
            idbfs_state.syncInProgress = false;
            var result = err
                ? 'SyncFailed:' + err
                : 'SyncSuccess';

            if (err) {
                console.error('[IDBFS] Sync to IndexedDB failed:', err);
            } else {
                console.log('[IDBFS] Synced to IndexedDB successfully');
            }

            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, result);
        });
    },

    // -------------------------------------------------------------------------
    // _Idbfs_GetStorageEstimate
    // Calls navigator.storage.estimate() and returns quota/usage via SendMessage.
    // Sends 'StorageEstimate:<usage>/<quota>' or 'StorageEstimateUnavailable'.
    // -------------------------------------------------------------------------
    _Idbfs_GetStorageEstimate: function(callbackObjectPtr, callbackMethodPtr) {
        function toString(ptr) {
            if (typeof UTF8ToString !== 'undefined') return UTF8ToString(ptr);
            if (typeof Pointer_stringify !== 'undefined') return Pointer_stringify(ptr);
            return ptr;
        }

        var callbackObject = toString(callbackObjectPtr);
        var callbackMethod = toString(callbackMethodPtr);

        if (!navigator.storage || !navigator.storage.estimate) {
            var msg = 'StorageEstimateUnavailable';
            console.warn('[IDBFS]', msg);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
            return;
        }

        navigator.storage.estimate().then(function(estimate) {
            var msg = 'StorageEstimate:' + estimate.usage + '/' + estimate.quota;
            console.log('[IDBFS]', msg);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
        }).catch(function(err) {
            var msg = 'StorageEstimateFailed:' + err;
            console.error('[IDBFS]', msg);
            if (callbackObject && callbackMethod) SendMessage(callbackObject, callbackMethod, msg);
        });
    },

    // -------------------------------------------------------------------------
    // _Idbfs_RegisterBeforeUnloadSync
    // Registers a window.beforeunload handler that attempts a best-effort final
    // sync. Note: async ops during beforeunload are NOT guaranteed to complete
    // (browser may kill the page before the callback fires). This surfaces the
    // edge case in the log so it is visible during testing.
    // -------------------------------------------------------------------------
    _Idbfs_RegisterBeforeUnloadSync: function() {
        if (idbfs_state._beforeUnloadRegistered) return;
        idbfs_state._beforeUnloadRegistered = true;

        window.addEventListener('beforeunload', function(e) {
            if (!idbfs_state.mounted) return;
            if (idbfs_state.syncInProgress) {
                console.warn('[IDBFS] Page unloading while a sync is in progress — data may be lost!');
                return;
            }
            console.warn('[IDBFS] Page unloading — attempting best-effort sync. Completion is NOT guaranteed.');
            // Synchronous IndexedDB is not possible; this fires async and may be killed.
            FS.syncfs(false, function(err) {
                if (err) {
                    console.error('[IDBFS] beforeunload sync failed:', err);
                } else {
                    console.log('[IDBFS] beforeunload sync completed.');
                }
            });
        });

        console.log('[IDBFS] beforeunload sync handler registered.');
    }
};

// Declare shared state and helpers as dependencies so Emscripten includes them.
IdbfsPlugins['_Idbfs_MountAndSync__deps']           = ['$idbfs_state', '$idbfs_detectOrigin', '$idbfs_originWarning', '$idbfs_isMountedAt'];
IdbfsPlugins['_Idbfs_SyncToIndexedDb__deps']        = ['$idbfs_state'];
IdbfsPlugins['_Idbfs_GetOriginInfo__deps']           = ['$idbfs_detectOrigin', '$idbfs_originWarning'];
IdbfsPlugins['_Idbfs_CheckIdbAvailable__deps']       = [];
IdbfsPlugins['_Idbfs_GetStorageEstimate__deps']      = [];
IdbfsPlugins['_Idbfs_RegisterBeforeUnloadSync__deps']= ['$idbfs_state'];

mergeInto(LibraryManager.library, IdbfsPlugins);
