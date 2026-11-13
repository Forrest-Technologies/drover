# Security policy

## Report a problem privately

Do not open a public issue for a security problem.

Use GitHub's private vulnerability reporting: on the repository page, open the Security tab and choose "Report a vulnerability". Only the maintainers can read the report.

Include what you found, the steps to reproduce it, and the version or commit you tested. If a report touches another person's data on a shared server, say so first; a leak between spaces is the problem Drover is built to prevent.

## What to expect

A maintainer reads each report and replies on the report itself. A confirmed problem is fixed in a pull request and noted in the release notes of the version that carries the fix.

## Supported versions

Drover is in early development. Only the newest release and `main` get fixes.

## Scope

In scope: the Drover server, its web client, the compose files in this repository, and the release workflow.

Out of scope: problems in a model provider, a tool server, or Docker itself. Report those to their maintainers.
