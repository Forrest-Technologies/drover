# 0014. Use the existing server session in the Android preview

- Status: Accepted for the initial Android sideload preview and retained for older servers
- Date: 2026-11-16

## Decision

The preview hosts the shared Drover Razor components locally and uses the existing typed API client. A native HTTP handler holds the existing server session and antiforgery cookies in memory. It sends requests only to the HTTPS origin chosen on the connection screen and does not follow redirects. The server still owns sign-in, authorization and session revocation.

The app stores the server address only. A process restart requires signing in again. This lets people try the Android app against an existing Drover deployment without adding another authentication scheme to the server.

## Before general availability

Replace the preview cookie transport with the native bearer and secure storage design in [Sign-in and secret handling](../architecture/08-auth-and-secrets.md). Native notifications and OAuth return to the app need their own verification. Give the preview its own Android package ID and signing key so it can be installed alongside a future stable app.
