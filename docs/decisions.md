# Decisions

Where the code had to settle something the specification (`spec.html`, draft 0.2) leaves open, or where it
departs from the letter of the specification, the choice and the reason are recorded here. Section numbers
refer to the specification.

## Colour engine

### D1. The lightness rule is stated in contrast, not in lightness (6.3)

The specification asks for "one monotonic curve through the anchor pairs". The curve is built on WCAG contrast:
a source colour with contrast *c* against the source page is shown with contrast *f(c)* against the theme's page.
The working space (`LumLab`) is OKLab rescaled so that its first coordinate is exactly the cube root of WCAG
luminance.

Why: every readability promise in the specification is a WCAG ratio and every brightness limit a WCAG
luminance. A curve drawn in plain OKLab lightness turned grey text with 4.5:1 on white into about 2.8:1 on
black, because contrast ratios are not symmetric between light and dark. In contrast terms the same text keeps
about 4:1, and limits are exact whatever hue a colour ends up with.

### D2. Light or dark is decided per region, in the same frame (6.5 "polarity check")

The specification describes the polarity check as "a handful of average values" choosing between two ready
tables. Here every pixel gets its own answer from its surroundings: flat areas that are clearly light or
clearly dark vote, the votes are gathered at two scales (about 32 and 128 pixels), and a whole-lens figure
from the last analysis fills in where no background is in sight. Mid-tones (a blue button, a green pill) cast
only a token vote and follow their context. Text and edges do not vote.

Why: a full-screen lens routinely covers a dark window next to a light one, a light page with a dark code
block, a dark header over a white page. One decision per frame would get half of such a screen wrong, and the
area guard would then have to blank it. Where a light and a dark region meet, each pixel's own lightness
settles the tie, so the boundary gets no glowing band.

### D3. The area guard measures the recoloured picture (6.5 "area guard")

The specification has the shader pass the neighbourhood's average colour through the table. Here the guard
looks at the recoloured output itself: each recoloured pixel is flagged if it differs from the page by more
than an area may, and the guard acts where about 85% of a 16-pixel window is flagged. The flagged area becomes
the page colour; whatever stands out from its immediate surroundings (text, lines) is redrawn towards the
theme's text colour, so text inside a guarded area stays readable.

Why: "table(average)" is not "average(table)". A stale or wrong table can make a textured area bright while
its average maps dark. Measuring the output makes the rule independent of how the table was built; a unit
test runs the guard against an identity table under a dark theme's rules and still gets a black page.

Consequences:

- An "area" is something that contains a solid square of about 17 pixels. Narrower shapes are lines and text.
  The "bright area" measurement in the tests (9.2) uses the same definition: it ignores shapes narrower than
  17 pixels, then reports the largest connected area at its full size. This is stricter than FR-10 for compact
  shapes and deliberately blind to thin lines.
- A new theme limit, `limits.maxAreaContrast`, says how far an area may differ from the page. Default 6 on a
  dark base (anything much brighter than the page is glare) and 12 on a light base (a dark button is harmless;
  only something close to a dark page needs catching). `limits.maxLuminance` stays what the specification says:
  the brightness limit for any pixel, text included.
- The guard costs a second full-size pass. Themes with the guard off and no contrast or sharpen still render
  in one pass.
- Sizes are in pixels at 100% scale. They need to scale with DPI (milestone M4).

### D4. Fills and line work in the same hue family are treated differently (6.2)

A strongly coloured cluster used as text or line work (thin) is shown in the theme's accent colour for its
role. One used as a filled area (a button, a bar, a pill) takes the theme's hue for its role but keeps the
lightness the general rule gives it, held under the area limit.

Why: the label on a fill was mapped by the general rule too, so keeping the fill on that rule preserves the
label's contrast. Painting a button in a colour meant for text made its label unreadable and tripped the area
guard. One hue family can therefore have two anchors per region (one fill, one line work); further colours of
the family are "Other accent".

### D5. Anti-aliased edges are not clusters of their own

A thin coloured cluster that lies between a purer colour and a background (in colour space) is an edge blend.
It follows the general rule and is not an anchor. Likewise, an unthemed colour within 0.07 (OKLab) of a themed
accent is treated as a shade of it. Without this, the rim of a green pill was detected as "thin green" and
shown in the bright accent green: a glowing ring.

Thin clusters report the colour at the far end of their strokes (the member with a seventh of the pixels
beyond it, counted from the background), not their mean: most pixels of thin text are edge blends.

### D6. Role colours are what the lens shows at the theme's own warmth (5.5, 6.6)

The example theme file gives Soft dark `"warmth": 0.2` together with text `#C9C4B8`, and the control window
wireframe shows that text as 12.1:1 at "Warmth 20%". So role colours are taken as the colours shown at the
warmth written in the file. Moving the warmth slider applies only the difference. On the keep base, which has
no role colours, warmth applies in full.

Warmth 1 removes all blue and 30% of green in linear light. These factors were chosen so that the three
"blue share" examples in 9.3 hold with some margin.

### D7. Soft dark's brightness limit is 0.554, not 0.55

`#C9C4B8` has luminance 0.5536. A limit of 0.55 would clip the theme's own text colour by one 8-bit step.
0.554 still reads "55%" in the control window. The limit is applied before rounding to 8 bits, so a pixel can
exceed it by at most half a step (0.004); the scenario "no pixel is brighter than the theme's brightness
limit" allows exactly that.

### D8. Balanced mode is a simple heuristic for now (6.5)

"Looks like an image" is approximated by the share of 4×4 blocks around a pixel whose luma range is neither
zero (flat) nor large (a hard edge). Proper image detection is planned after 0.1 (section 14).

### D9. Sharpen and local contrast are computed from the recoloured picture before the guard

Both add to lightness only and are switched off inside guarded areas. The clamp to the theme's limits is the
last step of every frame, as the specification requires.

## Engine

### D10. "Frames stop" means the source says so

Windows only delivers a frame when the screen changes, so the absence of frames is normal for a static screen.
The lens returns to Holding background when the frame source reports a fault, when the app is told the display
changed, or when the watchdog sees that the frame loop itself has not run for 500 ms. The watchdog runs on its
own thread and covers the lens through a separate window, because the loop it guards may be stuck.

### D11. Reduced speed

The lens is "Live, reduced speed" when a frame takes more than 1.25 refresh intervals to render, and returns to
"Live" after three frames within budget. The option named as costing most is Strong sharpen, then Local
contrast, then CPU rendering. Nothing is switched off automatically.

### D12. The keep base has no page to fall back on

On a fault, keep-base themes show the last good frame at half brightness, or hide the lens when there is no
frame yet. With Passthrough and no contrast or sharpen the lens body is hidden and capture is stopped.

### D13. Role changes are accepted for the whole mapping at once

A new set of anchors has to win three analyses in a row (section 6.4). The comparison is made on the whole
mapping, not role by role. A jump of more than 0.15 in mean luminance counts as a new scene and is accepted at
once; pins take effect at once.

## Theme files

### D14. Additions to the format

- `description`: one line shown under the name in the theme picker.
- `limits.maxAreaContrast`: see D3.
- `$schema` in the built-in files is the relative path `./theme-1.schema.json`, so editors find the schema
  without a network. The schema's `$id` still carries the placeholder address from the specification.
- Greyscale is `limits.maxChroma: 0`; Dimmer is `limits.maxLuminance: 0.5`, which on the keep base scales the
  whole picture (like turning the backlight down) instead of clipping its top.

### D15. Colours the specification does not give

The specification fixes page and text for every theme and a few accents. The remaining role colours were
chosen to match the mock-up at the top of the specification where it has them, and otherwise to give every
accent at least 4.5:1 against the page (a unit test checks this). They are provisional.

## Open points for the project owner

### Q1. Red–green assist dark: the pair's lightness gap under protanopia

The specification gives Bad `#FFB000` and Good `#4EA3FF` and asks for the two to differ by at least 0.08 in
lightness under simulated protanopia and deuteranopia. With the Machado matrices the pair differs by 0.107 for
normal vision and clearly under deuteranopia, but only by about 0.03 under protanopia, where the orange
darkens. The two stay far apart on the blue–yellow axis (OKLab distance above 0.2), and the scenarios in 9.3
pass, because the markers in the dashboard fixture are fills (D4). Text or line work in these two colours would
not meet the 0.08 figure. A darker blue for Good (around `#3B82F6`) would. The specification's colours are
kept until this is decided.

### Q2. Tests run on Microsoft.Testing.Platform

xUnit v3 on the .NET 10 SDK uses the new test platform, so the commands differ slightly from section 10.2:
`dotnet test --solution ThemeLens.slnx` and `--filter-not-trait "Category=Ui"` instead of
`--filter Category!=Ui`. `.vscode/tasks.json` uses the new forms.

## Known limits of the current engine

- Photographs and full-colour gradients come out safe (no bright area) but not pretty: the lens does not know
  a photo from a button (section 3.2).
- A solid shape wider than about 32 pixels in the opposite polarity of its page (a big black logo on white)
  is treated as a region of its own and merges with the page; its anti-aliased outline remains.
- Very large bold text (strokes wider than about 14 pixels) can count as an area.
- Table building takes about 10 ms and analysis 2 to 5 ms on a 4-core machine: at the edge of the 15 ms
  budget of section 7.6. Both run off the frame loop.
