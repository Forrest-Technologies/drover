# 0003. One data path for every client: JSON API and Server-Sent Events

- Status: Accepted
- Date: 2026-11-11
- Source: [01-solution-layout.md](../architecture/01-solution-layout.md)

## Context

Screens are shared between the web app and the MAUI shell. The shell cannot use server-side interactivity.

## Decision

The web UI is Blazor WebAssembly with global interactivity. Every screen talks to the server through `Drover.Client`. Run events stream over Server-Sent Events with resume from a sequence number.

## Alternatives considered

- Server-side interactivity on the web. Rejected: a second data path that the apps cannot use.
- A socket library for streaming. Rejected: reconnect does not replay; SSE resumes from a cursor natively.

## Consequences

No per-user UI state on the server. A larger first download on the web.

## Reopen when

First load on mobile browsers proves unacceptable; or voice needs a two-way low-latency channel, which would be added for voice only.
