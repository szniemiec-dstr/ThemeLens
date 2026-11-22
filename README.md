# ThemeLens

A window you place over anything on your screen. Whatever is underneath is shown in the colours you chose:
a black page with soft off-white text, high contrast, reds and greens you can tell apart, or less blue in the
evening. Clicks and typing go straight through to the app below.

> **Status: early development.** Nothing is released yet. "ThemeLens" is a working title.

## Where things stand

| Part | State |
| --- | --- |
| Colour engine (`ThemeLens.Core`): colour maths, cluster analysis, roles, lookup tables, CPU reference renderer | Working and tested on Linux and Windows. This is milestone M1. |
| Engine (`ThemeLens.Engine`): lens states, frame loop, fades, watchdog, settings, shortcuts | Working and tested against a fake clock and fake screen. |
| 13 built-in themes and the theme file schema | Done. Colours the specification does not fix are provisional. |
| Executable specifications: 165 scenario examples, plus golden images for every theme | All passing in CI on Ubuntu and Windows. |
| Windows layer (`ThemeLens.Platform.Windows`): capture, Direct3D renderer, lens and handle windows, hotkeys | Written and compiled, **not yet run on a real Windows desktop**. This is the M0 proof and needs testing by hand. |
| App (`ThemeLens.App`) | A minimal control window to drive the lens. First run, the mapping table, tray icon and settings are still to come (M3). |
| Installer, updates, signing | Script only (`build/pack.ps1`), not exercised (M5). |

The full product and technical specification is in [`docs/spec.html`](docs/spec.html). Where the code had to
settle something the specification leaves open, the decision is recorded in
[`docs/decisions.md`](docs/decisions.md).

## The one rule that outranks every other

Inside the lens, the background is always the one you chose. If the app cannot keep up, loses the capture, or
is unsure, it shows the plain theme background rather than risk one bright frame on a dark theme.

## Build and run

You need Windows 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and VS Code with the C# extension.

```
git clone https://github.com/szniemiec-dstr/ThemeLens
cd ThemeLens
code .
```

Press F5. The `ThemeLens` launch configuration starts the app in `--dev` mode, in which the lens is not
topmost and covers at most a quarter of the screen, so stopping at a breakpoint cannot leave a frozen rectangle
over your editor.

From a terminal:

```
dotnet build ThemeLens.slnx
dotnet test --solution ThemeLens.slnx
dotnet run --project src/ThemeLens.App -- --dev
```

The colour engine (`ThemeLens.Core`, `ThemeLens.Engine`) and its tests build and run on Linux and macOS too.
The Windows-only projects compile there but only run on Windows.

## Layout

| Path | What it is |
| --- | --- |
| `src/ThemeLens.Core` | Every colour rule: colour spaces, clustering, roles, the lookup-table builder, a CPU reference renderer. No OS dependencies. |
| `src/ThemeLens.Engine` | Lens state machine, frame loop, watchdog. Talks to interfaces only. |
| `src/ThemeLens.Platform.Windows` | Screen capture, Direct3D 11 renderer, lens and handle windows, hotkeys. |
| `src/ThemeLens.App` | The WPF app: first run, control window, tray. |
| `tests/` | Unit and property tests, executable specifications (Reqnroll), platform and UI tests. |
| `themes/` | Built-in themes and the theme file schema. |

## Privacy

Screen content never leaves the process: no frames on disk, none over the network, none in logs. There is no
telemetry.

One side effect worth knowing: the lens hides itself from screen capture so that it does not capture its own
picture. Screenshots and screen sharing therefore show the original, unthemed screen.

## Licence

ThemeLens is free software under the GNU General Public License, version 3 or (at your option) any later
version. See [`LICENSE`](LICENSE). The built-in theme files in `themes/` are released under CC0; see
[`themes/LICENSE`](themes/LICENSE). The name and logo are covered by [`TRADEMARKS.md`](TRADEMARKS.md).

ThemeLens changes how the screen looks. It makes no medical claims about migraine, sleep or eye health.
