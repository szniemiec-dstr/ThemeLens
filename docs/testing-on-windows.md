# Trying ThemeLens on Windows

The colour engine is tested automatically on Linux and Windows, and the GPU shader is compared with the
reference renderer on the Windows software rasteriser in CI. What no machine has done yet is show the lens on
a real desktop. Everything in `ThemeLens.Platform.Windows` and `ThemeLens.App` was written without being run.
Expect the first run to fail somewhere; the list below is ordered so that each step tells us something even
if a later one does not work.

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

This also runs the five lens-window tests that CI can only try (they need a desktop). If any test fails,
its name and message are all I need.

## 2. The lens over a test pattern

```
dotnet run --project src/ThemeLens.App -- --dev --source pattern
```

`--dev` keeps the lens from being topmost and limits it to a quarter of the screen, so nothing can get stuck
over your work. `--source pattern` feeds the lens a built-in picture, so screen capture is not involved yet.

| Step | What should happen |
| --- | --- |
| The app starts | A small control window appears. No lens yet. |
| Switch the lens on | A rectangle appears showing the test pattern in the chosen theme's colours. It shows the plain page colour first, then the picture. |
| Pick another theme | The lens cross-fades to the new colours in about a fifth of a second. No flash. |
| Drag the handle above the lens | The lens follows. Double-click the handle: the lens fills the screen it is on, and goes back on the next double-click. |
| Click inside the lens | The click goes to whatever is underneath, not to the lens. |
| Win+Alt+E | Edit mode: the lens can be moved and resized by its edges. The same keys leave edit mode. |
| Win+Alt+L | Lens off (it fades out), and on again. |
| Win+Alt+X | Everything off at once. |

## 3. The lens over the real screen

```
dotnet run --project src/ThemeLens.App -- --dev
```

| Step | What should happen |
| --- | --- |
| Switch the lens on over a white document | Black page, soft off-white text (Soft dark). No yellow capture border around the screen, no second mouse pointer inside the lens. |
| Type and scroll under the lens | The lens keeps up. Text stays sharp. |
| Move the lens over a dark editor next to a light page | Both halves end up in the theme's colours; the dark half is not inverted to light. |
| Look at the status in the control window | It reads "Live" with a time in milliseconds, and a frame rate. If it reads "Live, reduced speed", tell me the frame rate it shows. |
| Take a screenshot (Win+Shift+S) | The screenshot shows the original screen without the lens. That is intended. |

## 4. Without `--dev`

```
dotnet run --project src/ThemeLens.App
```

The lens is now topmost and may cover the whole screen. Win+Alt+X switches everything off.

| Step | What should happen |
| --- | --- |
| Lens on, then open the Start menu or a context menu over it | The menu is recoloured too, or appears above the lens unrecoloured. Either is worth knowing. |
| Lock the screen and unlock (Win+L) | The lens shows the page colour for a moment and then the picture again. It never shows the original bright screen. |
| Unplug or switch off a second monitor while the lens is on it | The lens moves to a screen that is still there. |
| Settings, Privacy, Screenshots and screen recording: switch access off for desktop apps | The lens shows the plain page colour and the status reads "Holding background". (A message that says why is not built yet.) |

## 5. Things I already know are missing

- The first-run window, the mapping table, the tray icon and the settings page. The control window is a
  minimal one to drive the lens.
- Remembering the lens position per monitor layout (the logic exists and is tested; it is not wired to the windows yet).
- HDR screens, and scaling of the lens's inner sizes with display scaling above 100%.
- An installer.

## What to send back

For anything that does not behave as described: which step, what you saw instead, and the text of any error
window. A phone photo of the screen is more useful than a screenshot, because screenshots do not show the lens.
