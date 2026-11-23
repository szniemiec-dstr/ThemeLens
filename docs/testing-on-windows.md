# Trying ThemeLens on Windows

The colour engine is tested automatically on Linux and Windows, and the GPU shader is compared with the
reference renderer on the Windows software rasteriser in CI. CI also starts the whole lens on its Windows
desktop: it goes live over a test pattern and over the real screen, survives edit mode and switches off
again. What no machine can tell is what it looks like. Nobody has seen the lens yet, and the control window
has not been run at all. The list below is ordered so that each step tells us something even if a later
one does not work.

You need Windows 11 and the .NET 10 SDK.

```
git clone https://github.com/szniemiec-dstr/ThemeLens
cd ThemeLens
dotnet build ThemeLens.slnx
```

## 1. The automatic tests, on your machine

```
dotnet test --solution ThemeLens.slnx
```

This also runs the tests that need a desktop. Five check the lens window's styles. Two start the whole lens
the way the app does, first over a built-in test pattern and then with real screen capture: a rectangle
appears on your screen for a few seconds each time. If these two pass, the windows, the composition, the
frame loop and the capture all start on your machine, which is most of what is unknown.

If any test fails, its name and message are all I need.

To run only the two that start the lens:

```
dotnet test --project tests/ThemeLens.Platform.Tests --filter-class "*LensRuntimeSmokeTests"
```

## 2. The lens over the real screen

```
dotnet run --project src/ThemeLens.App
```

Do not add `--dev` when you try the lens out. Development mode is for stepping through code with a
debugger: in it the lens is deliberately not kept on top and limited to a quarter of the screen.

| Step | What should happen |
| --- | --- |
| The app starts | A small control window appears. No lens yet. |
| Switch the lens on, over a white document | A rectangle appears. It shows the plain page colour for a moment, then the document with a black page and soft off-white text (Soft dark). No yellow capture border round the screen, no second mouse pointer inside the lens. |
| Click on another window, open a menu | The lens stays on top. A menu or tooltip that opens over the lens is recoloured too, within a fifth of a second. |
| Click inside the lens, type, scroll | Everything goes to the app underneath. The lens keeps up and text stays sharp. |
| Drag the handle (the small bar on the lens's top edge) | The lens follows. Double-click the handle: the lens fills the screen it is on, and goes back on the next double-click. The handle's right-hand button does the same. |
| Press the handle's edit button, or Win+Alt+E | Edit mode: the outline gets thicker and shows a grip on every corner and in the middle of every edge. Drag inside the lens to move it; drag a grip, or anywhere along an edge, to resize it. Arrow keys move, Shift with arrow keys resizes. Enter, Esc or the same button again leaves edit mode. |
| Pick another theme | The lens cross-fades to the new colours in about a fifth of a second. No flash. |
| Move the sliders under "Adjust this theme" | The lens follows while you drag: darker with a lower brightness limit, less blue with more warmth. "Back to the theme's own values" undoes it. |
| Look at "Colours on screen now, and what you see" | One row per colour under the lens: its true colour with name and hex value, its share, the role it was given and the colour it is shown as. It follows the screen within half a second. Choose another role in "Used as": the lens changes and the row says "Pinned" until "Reset mapping". |
| Close the app with the lens on, and start it again | The same theme, your slider values and the lens's place are back, and the lens switches itself on. |
| Move the lens over a dark editor next to a light page | Both halves end up in the theme's colours; the dark half is not inverted to light. |
| Look at the status in the control window | It reads "Live" with a time in milliseconds, and a frame rate. If it reads "Live, reduced speed", tell me the frame rate it shows. |
| Take a screenshot (Win+Shift+S) | The screenshot shows the original screen without the lens. That is intended. |
| Win+Alt+L | Lens off (it fades out), and on again. |
| Win+Alt+X | Everything off at once. |

## 3. Harder cases

| Step | What should happen |
| --- | --- |
| Lens on, then open the Start menu | The Start menu appears above the lens in its own colours. Windows keeps it above every app; that cannot be changed. |
| Lock the screen and unlock (Win+L) | The lens shows the page colour for a moment and then the picture again. It never shows the original bright screen. |
| Unplug or switch off a second monitor while the lens is on it | The lens moves to a screen that is still there. |
| Settings, Privacy, Screenshots and screen recording: switch access off for desktop apps | The lens shows the plain page colour and the status reads "Holding background". (A message that says why is not built yet.) |

## 4. If something is wrong with the picture only

```
dotnet run --project src/ThemeLens.App -- --source pattern
```

This feeds the lens a built-in picture instead of the screen. If the lens is fine over the pattern and wrong
over the screen, the fault is in screen capture; if it is wrong over both, it is in drawing.

## 5. Things I already know are missing

- The first-run window, the tray icon and the settings page.
- Saving your slider values as a theme of your own, and changing the keyboard shortcuts. The logic for both
  exists and is tested; the window has no controls for them yet.
- A message that says why when Windows refuses screen capture.
- HDR screens, and scaling of the lens's inner sizes with display scaling above 100%.
- An installer.

The app keeps its settings in `%AppData%\ThemeLens\settings.json`. Delete that file to start from scratch.

## What to send back

For anything that does not behave as described: which step, what you saw instead, and the text of any error
window. A phone photo of the screen is more useful than a screenshot, because screenshots do not show the lens.
