# ThemeLens

A window you place over anything on your screen. Whatever is underneath is shown in the colours you chose:
a black page with soft off-white text, high contrast, reds and greens you can tell apart, or less blue in the
evening. Clicks and typing go straight through to the app below.

> **Status: early development.** Nothing is released yet. "ThemeLens" is a working title.

## Where things stand

| Part | State |
| --- | --- |
| Colour engine (`ThemeLens.Core`): colour maths, cluster analysis, roles, lookup tables, regions, CPU reference renderer | Working and tested on Linux and Windows. This is milestone M1. |
| Engine (`ThemeLens.Engine`): lens states, frame loop, fades, watchdog, settings, shortcuts, the app's logic without windows | Working and tested against a fake clock and fake screen. |
| 13 built-in themes and the theme file schema | Done. Colours the specification does not fix are provisional. |
| Executable specifications: 165 scenario examples, plus golden images for every theme | All passing in CI on Ubuntu and Windows. |
| GPU renderer (`ThemeLens.Platform.Windows`, `lens.hlsl`) | Compared with the reference renderer in CI on the Windows software rasteriser: 15 looks on 12 test screens, all matching. |
| Capture, lens and handle windows, hotkeys (`ThemeLens.Platform.Windows`) | In CI the whole lens starts on a Windows desktop, goes live over a test pattern and over the real screen, survives edit mode and switches off. **Nobody has looked at it yet**: whether the picture is right, clicks pass through and nothing flickers needs a person at a Windows 11 machine. See [`docs/testing-on-windows.md`](docs/testing-on-windows.md). |
| App (`ThemeLens.App`) | A control window: lens on and off, theme, the table of colours found and shown with roles you can change, brightness limit, warmth, contrast, sharpen, keep background, fill screen, edit mode, status. It remembers the theme, your values for it, where the lens was and whether it was on. CI starts it on a Windows desktop and drives it through UI Automation. You can save your values as a theme of your own. First run, the tray icon and the settings page come next (M3). |
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

Press F5, or from a terminal:

```
dotnet build ThemeLens.slnx
dotnet run --project src/ThemeLens.App
```

Switch the lens on in the control window. The lens stays on top of other windows and lets every click
through. Drag its handle (the small bar on its top edge) to move it. The handle's two buttons switch edit mode,
in which the lens is moved by dragging it and resized by dragging its edges, and fill the screen it is on.
Win+Alt+X switches everything off at once.

To run the tests:

```
dotnet test --solution ThemeLens.slnx
```

### When you are debugging

A lens that stays on top is in the way when the debugger stops at a breakpoint: the app is frozen, and so is
the rectangle over your editor. For that there is development mode:

```
dotnet run --project src/ThemeLens.App -- --dev
```

In development mode the lens is **not** kept on top and covers at most a quarter of the screen, so "fill this
screen" and resizing stop short. It is for stepping through code, not for trying the lens out. The second and
third launch configurations in VS Code start it.

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
