---
status: implemented
archived: 2026-07-18
superseded-by: code (run.sh DOTNET_SYSTEM_NET_DISABLEIPV6 default) + docs/MEMORY.md gotcha
---

# Remote Embedded Data Download Timeout — Root-Cause Analysis

## Background

Issue #82 moves `voice-lines.json` and `tenwin_builds.json` out of the repository and into a shared `obj/remote-data/` build cache. An ordinary build downloads only missing files; an explicit publish refreshes both files before seed validation and packaging.

## Current problem

After deleting `src/BazaarPlusPlus/obj/remote-data/`, two consecutive `./run.sh build` attempts downloaded `voice-lines.json` but timed out after 120 seconds while requesting:

`https://bpp-metrics.bazaarplusplus.com/analyzer-v4/mod/tenwin_builds.json`

The same endpoint had succeeded earlier in the session through `./run.sh fetch-data`, so the failure must be separated into a remote-service problem, a network/protocol-path problem, or a client-specific behavior before changing the build implementation.

Captured logs:

- `/tmp/bpp-first-fetch.log`
- `/tmp/bpp-first-fetch-retry.log`

## Candidate cause mechanisms

1. The metrics origin or an upstream proxy accepts the request but intermittently stalls before completing the response.
2. A protocol-specific path differs between curl and .NET `HttpClient` (HTTP/2 negotiation, proxy handling, connection reuse, or content encoding).
3. DNS returns multiple addresses and one route is unhealthy or unreachable from the current machine.
4. The response starts but transfers too slowly to complete within the client's 120-second total timeout.
5. The build client sends headers or request semantics that trigger a different CDN/cache behavior from a normal command-line fetch.

## Verification method

Use the exact URL as the tight feedback loop and capture timing/protocol facts without changing product code:

1. Resolve all DNS addresses and inspect the TLS certificate/handshake.
2. Fetch headers and the complete body with curl while recording connect, first-byte, total time, HTTP version, response code, and byte count.
3. Repeat with HTTP/1.1 and HTTP/2 separately.
4. Compare direct curl behavior with the existing MSBuild `HttpClient` target and with the already-successful voice-lines endpoint.
5. If address families or addresses differ, pin each address individually with `curl --resolve`.

The diagnosis is complete when one candidate mechanism has direct evidence and the alternatives are falsified or materially weakened. No build-pipeline fix should be made during this analysis.

## Findings

1. Both hostnames resolve to the same Cloudflare address sets:
   - IPv4: `104.21.79.75`, `172.67.169.87`
   - IPv6: `2606:4700:3031::6815:4f4b`, `2606:4700:3037::ac43:a957`
2. Full curl downloads over IPv4 succeeded repeatedly. The current payload sizes were 1,135,446 bytes for voice lines and 1,252,971 bytes for tenwin builds; HTTP/1.1 and HTTP/2 both completed in roughly 1–3 seconds.
3. An IPv6-only curl to the metrics hostname could not connect and timed out. macOS routes the Cloudflare IPv6 range through `utun13`, whose advertised public IPv6 path is not working from this machine.
4. The failure was reproduced while observing the live MSBuild process. During the stall, its only HTTPS socket was:

   `TCP [fdbd:dc00:5ea1:280::25f]:58766 -> [2606:4700:3037::ac43:a957]:443 (SYN_SENT)`

   At the same moment, curl fetched both files successfully over IPv4. This rules out a simultaneous origin outage and shows that the .NET process was waiting for an IPv6 TCP connection that never completed.
5. The same exact `./run.sh fetch-data` path completed in 4.84 seconds when invoked with `DOTNET_SYSTEM_NET_DISABLEIPV6=1`. No product code changed for this comparison.
6. A later reproduction stalled on the first voice-lines request rather than the second tenwin request. The symptom is therefore not specific to the metrics endpoint or the larger payload.

The build task creates one `HttpClient` with a two-minute timeout (`src/BazaarPlusPlus/RemoteEmbeddedData.targets:54-57`) and waits synchronously for response headers (`src/BazaarPlusPlus/RemoteEmbeddedData.targets:84-86`). The captured timeout occurs before the body-copy and size-validation steps (`src/BazaarPlusPlus/RemoteEmbeddedData.targets:89-104`), so payload transfer speed and JSON validation are not causal.

## Root cause

The local macOS network configuration advertises an IPv6 route through `utun13`, but that tunnel cannot establish TCP connections to the Cloudflare IPv6 addresses used by both data hostnames. .NET has IPv6 enabled by default and sometimes selects that unreachable address. The connection remains in `SYN_SENT` until the task-level two-minute timeout expires instead of reaching the working IPv4 address.

This is a client/network-path failure, not a bad `tenwin_builds.json`, insufficient `MinBytes`, HTTP-version incompatibility, or sustained remote-service outage.

## Remediation

`run.sh` now defaults `DOTNET_SYSTEM_NET_DISABLEIPV6` to `1` before invoking any .NET command. An explicitly supplied value remains authoritative, so callers with a healthy IPv6 path can opt back in without editing the script. This keeps the workaround scoped to repository build/test/publish commands and leaves the remote URLs unchanged.

Repairing or removing the broken IPv6 route in the local tunnel/VPN configuration remains the system-level remedy. If builds must retain IPv6 generally in the future, the download client would need an explicit dual-stack/fallback policy; that is broader than issue #82 and should be justified separately.

The architecture test for `run.sh` locks the default-with-override form so a later script refactor does not silently reintroduce the observed timeout.
