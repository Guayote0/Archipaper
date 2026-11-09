# Archipaper v1.4 reliability audit

Reconstructed on November 9, 2026 from repository baseline `866a833` (v1.3). The original unpushed audit checkout was removed by workspace maintenance; this is a fresh implementation of the documented fixes, with new validation. It does not claim recovery of the original commit.

## Problems found

- **Critical — persistence races:** saves shared one temporary filename; reads swallowed every failure and returned empty defaults. Concurrent writes could collide and subsequent saves could overwrite important state.
- **Important — schedule resets:** saving preferences, including the implicit save before discovery, restarted the full rotation interval. Repeated interaction could indefinitely delay automatic changes.
- **Important — UI blocking and monitor failures:** scans, decoding and caption generation used the UI thread; a failure for one monitor aborted subsequent monitor updates. Disconnected monitor HRESULTs were ignored.
- **Important — discovery reliability:** repeated first-page requests, duplicate-heavy results, provider timeouts treated as whole-operation cancellation, and swallowed preview cancellation.
- **Important — downloads/imports:** no body-transfer deadline, size ceiling or image validation; unchecked preview fallbacks could approve low-resolution images. Imports blocked the UI and a single corrupt file stopped the batch.
- **Important — build verification:** native command exit codes were not checked, there were no regression tests, and CI did not run on pull requests.
- **Minor — resources:** repeated thumbnail decoding; unused provider download duplication; unbounded logs/caption cache; incomplete cancellation/event cleanup.

## Changes made

- One application-owned STA thread owns rotation scheduling and Windows COM calls. Persisted per-monitor deadlines, resume/display/clock notifications, independent retries (1, 2, 4, 8, then 15 minutes), and safe disposal.
- Settings saves retain deadlines. Save-and-change performs one pass even when an automatic deadline is overdue. Successful monitors are reserved during retries. Fill and orientation preferences remain; different-monitor choices work even with recent avoidance disabled.
- Serialized, snapshot-based JSON writes with unique temporary files, atomic replacement, valid backups and preserved corrupt files. Unrecoverable reads surface instead of silently resetting data.
- Three small provider adapters share bounded downloading. Discovery persists per-query page numbers and job ordering, deduplicates URLs, honors Retry-After, isolates failed providers and supports cancellation with saved partial progress.
- Real JPEG/PNG/BMP signature/dimension/decode validation; actual detected extensions; content hashing across imports and downloaded images. Full-size cached previews are reused. Invalid or small fallback previews remain pending. Explicit reimport can restore a removed item.
- Image metadata and decoded thumbnail caches; background scanning/hashing/rendering; independent monitor errors; temporary caption outputs; source links restricted to HTTP(S).
- Build gates run core and Windows regression executables before publishing. No third-party NuGet dependencies were added.

## Limits and compatibility

Existing settings, approved images, favorites and history retain their locations. New `rotation-state.json` seeds deadlines from history on first migration. `discovery-state.json` preserves page progress and rejected identities. `ARCHIPAPER_DATA_DIR` permits isolated testing without touching the user's collection.

Discovery: 90 seconds total, at most 24 provider searches, 15 seconds per provider query, 24 additions per pass, 96 pending reviews. Previews: 10 seconds/16 MiB. Originals: 45 seconds/64 MiB. Images: 80 megapixels maximum. New imports and approvals require 1200 pixels on the short edge and 1800 on the long edge; chosen-folder images retain their prior size behavior. Original approved images are never cache-evicted.

Logs rotate at 2 MiB with one prior file. Caption copies not accessed for seven days become eligible for deletion when the cache exceeds 512 MiB; recently used outputs are retained. Thumbnails decode at 300 pixels with 256-entry reuse; review previews decode at 600 pixels.

## Verification and remaining risks

Run `dotnet run --project Archipaper.Tests -c Release` for 12 portable checks. Run `dotnet run --project Archipaper.WindowsTests -c Release` on Windows for five integration groups covering codecs, import/deduplication, offline cached approval, 4K captions and the real scheduler with a failing fake desktop backend. These tests do not change the Windows desktop wallpaper.

Local Linux cross-builds verify C#/XAML compilation, not actual Windows shell behavior. The PR's Windows CI is the source of truth for Windows test and installer results. Real dual-monitor Fill/cropping, hourly operation while minimized, sleep/wake and unplug/reconnect still need a smoke test on the user's PC. A hung OS COM server cannot be forcibly recovered in-process.

External providers can still deny requests or provide incomplete metadata; exact architect/category relevance remains provider-dependent. Flickr/Google remain browser searches, not automatic API integrations. No API keys are embedded. Perceptual deduplication of resized/re-encoded copies and virtualization of very large galleries are outside this targeted pass. No user-PC performance measurements are claimed.

The project retains .NET 8 to avoid mixing a major runtime upgrade into this repair; a separately tested upgrade to a supported LTS should be prioritized.
