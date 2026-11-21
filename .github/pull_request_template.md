Closes #

## What and why

<!-- One thing. If it needs "and", it is probably two pull requests. -->

## How it was verified

<!-- Say how the issue's verification plan was carried out. Paste the test names or output that show each acceptance criterion holds. -->

## Checklist

- [ ] Follows `CODE_STYLE.md`; `dotnet build -warnaserror` and `dotnet format --verify-no-changes` are clean
- [ ] Tests added or updated, and `dotnet test` passes
- [ ] New table: ownership class, `IsolateBySpace`, seeder in the isolation suite
- [ ] New package: license checked, row added to `docs/architecture/05-dependencies.md`
- [ ] New adapter or adapter behavior: conformance suite covers it
- [ ] New or changed `sys` function: manifest updated, reason given above
- [ ] No secret, no vendor type outside its adapter, no query outside a tenant scope
- [ ] Interface change: built from `DESIGN.md`, checked against its "never does" list, screenshots attached (dark, light, 360 px wide, each new state)
- [ ] `docs/architecture` still describes what the code does
- [ ] This changes a recorded decision, so an ADR is added or superseded in `docs/adr`
