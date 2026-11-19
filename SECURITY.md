# Security policy

## Reporting a vulnerability

Please do not open a public issue for security problems. Use GitHub's
[private vulnerability reporting](../../security/advisories/new) for this repository instead,
with steps to reproduce and the version (or commit) you tested.

You can expect an acknowledgement within a week. Fixes are released as a new version and
credited in the release notes unless you prefer to stay anonymous.

## Scope

NeverForget is a local, single-user app: it has no server, no accounts and no network access.
Useful reports include, for example:

- content in a note that runs script or HTML in the app;
- a way for anything other than the bundled `web/` page to reach the native bridge;
- loading code from an unexpected location (DLL planting, path issues);
- problems in the build or install scripts, including the WebView2 download.

## Design notes

- The UI is served from a local folder under the reserved host name `neverforget.example`,
  with a Content-Security-Policy that allows only the app's own scripts and styles.
- Navigation, new windows, downloads and permission requests (camera, microphone, location,
  notifications, clipboard read) are blocked by the host.
- The bridge ignores messages from any origin other than the app page, accepts a fixed list of
  commands and validates every value; all SQL uses bound parameters.
- P/Invoke targets are loaded from `System32` only.
- The WebView2 SDK is pinned to one version, checked against a SHA-256 hash and Microsoft's
  Authenticode signature before use.
