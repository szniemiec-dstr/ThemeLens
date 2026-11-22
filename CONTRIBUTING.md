# Contributing

Thank you for looking. The project is at an early stage, so the most useful contributions right now are bug
reports with a description of the screen that went wrong, and new test fixtures for layouts the engine gets
wrong.

## Before you open a pull request

1. Read the relevant part of [`docs/spec.html`](docs/spec.html). Requirements have IDs (FR-10, NFR-02 and so on);
   mention the ones your change touches.
2. `dotnet build ThemeLens.slnx` must pass. Warnings are errors.
3. `dotnet test --solution ThemeLens.slnx` must pass. On Linux or macOS run the two portable projects:
   `dotnet test --project tests/ThemeLens.Core.Tests` and `dotnet test --project tests/ThemeLens.Specs`.
4. Every source file starts with `// SPDX-License-Identifier: GPL-3.0-or-later`.
5. A change to a colour rule needs a scenario in `tests/ThemeLens.Specs/Features`. A new built-in theme has to
   pass the full feature matrix.

## Rules that are never traded away

- Background first (FR-10 to FR-12), no flashes (FR-13) and click-through (FR-02, FR-03) hold in every
  configuration. A change that makes the lens faster or prettier at the cost of one of these will not be merged.
- Screen content never leaves the process (NFR-02). Do not add code that writes frames to disk or to a log, not
  even for debugging.
- No telemetry (NFR-03).

## Dependencies

Anything shipped with the app must carry one of these licences: MIT, BSD-2-Clause, BSD-3-Clause, Apache-2.0,
ISC, Zlib. Anything else, including GPL and LGPL libraries, needs a recorded decision first. See section 12.2
of the specification for why.

## Contributor agreement

Contributions are accepted under the contributor agreement in [`CLA.md`](CLA.md). You keep your copyright.

## Regenerating fixtures and golden images

Fixtures are drawn by code in `tests/ThemeLens.Testing`; no screenshots of third-party apps are used.

```
THEMELENS_UPDATE_FIXTURES=1 dotnet test --project tests/ThemeLens.Specs
THEMELENS_UPDATE_GOLDENS=1  dotnet test --project tests/ThemeLens.Specs
```

Look at the resulting image diff before committing it. An unintended colour change should be visible there.
