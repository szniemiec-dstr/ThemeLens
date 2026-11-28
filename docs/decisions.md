# Decisions

Where the code had to settle something the specification (`spec.html`, draft 0.4) leaves open, or where it
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
tables. Here every pixel gets its own answer: it lies in a light region, in a dark region, or it is content
of whatever surrounds it.

Why: a full-screen lens routinely covers a dark window next to a light one, a light page with a dark code
block, a dark header over a white page, a row of black buttons on a white page. One decision per frame would
get half of such a screen wrong, and the area guard would then have to blank it.

How a region is found (`RegionMap`, the same code for the renderer and the analysis):

- A region has a **core**: a window of 20 by 20 pixels in which one kind, clearly light or clearly dark,
  outnumbers the other by more than three to one. Mid-tones (a blue button, a photo) are neither kind. The shares
  are counted in pixels, so the answer does not change as a shape moves across the 4-pixel grid the lens
  works on. A dark band 16 pixels thick is a region, with whatever is written on it: a chip, a button, a
  toolbar, a code block. A 16-pixel icon, a thick rule or a letter never fills the window and is content.
- From its cores a region **spreads** along rows and columns through 4×4 blocks that are mostly of its kind.
  This is what makes the gap between two dark buttons part of the light page, however far the gap is from
  open page, while a light scrollbar thumb inside a dark panel, which touches no light region, stays content.
- A block that belongs to one region takes that region's table whole: the label on a dark button is part of
  the button. A block on the edge between a light and a dark region gives each pixel to the region of its
  own kind, so the boundary gets no glowing band.
- Everything else is **content** and takes the table of the regions closest to it; where no region is in
  sight, the whole-lens figure from the last analysis decides. Only that figure listens to mid-tones, faintly.

Earlier versions let every flat block vote and blended the votes over 32 and 128 pixels. That put the
decision somewhere inside flat shapes: part of a black button was a dark region and part of it dark content,
which showed as blotches, and the gaps between neighbouring dark shapes lit up.

Consequences:

- A solid shape of the opposite kind, 16 pixels thick or more, merges with the page: a black button on a
  white page becomes the theme's page colour with its label in the text colour. What remains of its outline
  is the anti-aliasing of its edge, so rounded shapes keep a faint outline and square ones none. Drawing an
  outline where two regions meet would keep such shapes visible; this is not built (see the open points).
- Very large bold type, with strokes of 16 pixels or more (roughly 100-pixel type and up), counts as a region
  and is reduced to its outline in the same way.
- Text within about 4 pixels of a region of the other kind can lose the pixels nearest to it.
- The GPU reads the neighbourhood levels with four texel loads and the reference's own arithmetic, not with
  a sampler. A sampler rounds the position between two texels to 1/256, and next to a threshold that was
  enough to make the shader and the reference differ by up to 100/255 on a few hundred pixels.

### D3. The area guard measures the recoloured picture (6.5 "area guard")

The specification has the shader pass the neighbourhood's average colour through the table. Here the guard
looks at the recoloured output itself: each recoloured pixel is flagged if it differs from the page by more
than an area may, and the flagged pixels are then searched for areas exactly as the source is searched for
regions (D2), with "flagged" and "not flagged" as the two kinds. A block that belongs to an area becomes the
page colour, and whatever stands out from its immediate surroundings (text, lines) is redrawn towards the
theme's text colour, so text inside a guarded area stays readable. A block that only touches an area gives
up its flagged pixels, which is the area's rim.

Why: "table(average)" is not "average(table)". A stale or wrong table can make a textured area bright while
its average maps dark. Measuring the output makes the rule independent of how the table was built; a unit
test runs the guard against an identity table under a dark theme's rules and still gets a black page.

Consequences:

- An "area" is a shape that fills a 20-pixel window to more than three quarters: a band 16 pixels thick, a square of 18.
  Narrower shapes are lines and text; smaller ones (a checkbox, a status dot) are controls and keep their
  colour. This does not depend on the size of the lens, so FR-10 holds on any lens: a test sweeps white
  squares of every size across lenses from 160 by 120 to 1280 by 800 and finds no bright area left.
  The "bright area" measurement in the tests (9.2) ignores shapes narrower than 18 pixels, then reports the
  largest connected area at its full size.
- With regions found as in D2, clearly light and clearly dark shapes that are thick enough to be areas are
  regions already, and coloured fills are kept under the limit when the tables are built: a coloured fill,
  whether the theme has a colour for it or not, is shown no further from the page than three quarters of
  `maxAreaContrast`. The guard is the safety net for what the analysis has not seen yet (the first frames
  after the screen changes) and for tables that are wrong, not the usual way a button gets its colour.
- A new theme limit, `limits.maxAreaContrast`, says how far an area may differ from the page. Default 6 on a
  dark base (anything much brighter than the page is glare) and 12 on a light base (only something close to
  a dark page needs catching). `limits.maxLuminance` stays what the specification says: the brightness limit
  for any pixel, text included.
- The guard costs a second full-size pass and a second run of the small region passes. Themes with the guard
  off and no contrast or sharpen still render in one full-size pass.
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

### D16. Rectangles only: what was asked for, and what had to be settled (5.6, 6.8)

The request, in two rounds: find rectangles and invert them, bright ones to dark or the other way round; one
bar with two handles for the size at which a pure white rectangle is inverted and the size from which a
brightness threshold applies; the threshold on a second slider; sizes in between take values in between;
smaller ones are not inverted. Then: by default a rectangle is found and inverted irrespective of the theme,
unless the user applies the theme; the first version's "target follows the theme" and "shown in the theme"
stay as settings that can be switched off; the bar reaches from a square of 10 by 10 pixels to half the screen
and runs by the root of the area. These points were not in the request and were settled as follows.

- **Two preview switches, both on.** "Choose bright or dark myself" and "Invert plainly" are the new
  behaviour. Switching the first off lets the theme decide what is looked for; switching the second off shows
  a found rectangle in the theme's colours. Where a theme cannot turn the rectangle round (a bright rectangle
  under a light theme, any rectangle under a Keep theme) it is inverted all the same.
- **The inversion keeps hue and contrast.** It is not the photographic negative. A colour of luminance *Y*
  gets luminance 0.0525 / (*Y* + 0.05) − 0.05, which gives it against black the WCAG contrast it had against
  white; its chromaticity is kept and only as much saturation is given up as the gamut demands. White becomes
  black, `#222222` text keeps 15.9 : 1, a blue link stays blue. The same table turns dark rectangles light.
- **Outside the rectangles nothing is changed at all**, also not by the theme's brightness limit or warmth.
- **Sizes are in screen pixels**, as the side of a square of the same area. The bar is linear in that side.
  Its right end is the square that covers half the screen the lens is on.
- **Brightness** is the Rec. 709 luma of the sRGB values, the grey level a colour picker shows. The threshold
  can be set from 50 to 100% for bright rectangles and from 0 to 50% for dark ones.
- **"Pure white"** means 98% or more, so `#FAFAFA` counts. The allowance shrinks to nothing at the threshold.
- **Rectangular** means: the outline, with enclosed holes filled, is covered to 90% by at most four upright
  rectangles. A page with dark buttons or a dark picture in it is one rectangle. Round shapes and ragged
  areas are left out.
- **What lies in a filled hole** is decided per pixel by the region logic (D2): a dark button of 20 pixels or
  more, with its label, stays; thick dark letters of a headline, which are holes too, are content and are
  inverted with the page.
- **Nine brightness steps** between the threshold and white. A rectangle is judged by the step below its
  brightness, so it may need up to one step (about 3% brightness at the starting values) more than the
  straight line says.
- **Resolution.** The finder works on one sample per 4×4 block, so a size is known to within 4 pixels, and the
  rule is read in the rectangle's favour by that much: a square of 10 by 10 is found wherever it lies; one of
  7 or 8 pixels is found in some positions. On lenses larger than about 2560 by 1440 the finder takes every
  second block, and the smallest rectangle it can see is about 16 pixels.
- **Edges.** Inside a rectangle every pixel is changed. In its outermost ring of blocks and in the ring around
  it, a pixel is changed when it is itself bright enough. The edge is exact to the pixel. The price: dark
  text closer than about 4 to 8 pixels to the edge of a rectangle is not inverted with it.
- **Before the first look** the lens inverts every bright region (or shows everything in the theme, with
  "Invert plainly" off), and the option takes hold once rectangles are known. A rectangle found for the first
  time applies at once; one that is no longer found fades out over 200 ms.

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

### Q2. Should the lens draw an outline where a light and a dark region meet?

Under a dark theme a black button on a white page becomes page-coloured, and only its label shows that it is
there (D2). A thin line in a muted colour along the boundary would keep buttons, cards and panels visible as
outlined shapes. It would be the first thing the lens draws that is not a recoloured pixel, so it is left for
a decision.

### Q4. Rectangles only: three things to confirm

They are also listed in section 14.1 of the specification.

1. The inversion keeps hues and swaps contrast against white for contrast against black (D16). The
   alternative is the plain negative, in which a blue link turns orange.
2. A white photograph is a bright rectangle and is inverted like any other.
3. When the two preview switches move to the advanced settings: one setting for all themes, as now, or one
   per theme?

### Q3. Tests run on Microsoft.Testing.Platform

xUnit v3 on the .NET 10 SDK uses the new test platform, so the commands differ slightly from section 10.2:
`dotnet test --solution ThemeLens.slnx` and `--filter-not-trait "Category=Ui"` instead of
`--filter Category!=Ui`. `.vscode/tasks.json` uses the new forms.

## Known limits of the current engine

- Photographs and full-colour gradients come out safe (no bright area) but not pretty: the lens does not know
  a photo from a button (section 3.2).
- Shapes of the opposite kind merge with the page and keep at most a faint outline (D2).
- A mid-tone bar that ends where a light and a dark region meet (the status bar of a dark editor beside a
  light document) can show a lighter stretch of about 30 pixels at that end.
- Gradients and photographs that run from clearly light to clearly dark are cut into regions along straight
  lines.
- Rectangles only: the rectangles follow the screen with the delay of the analysis, about an eighth of a
  second. A dragged window has a bright leading edge, and a newly opened white window is white for that long.
- Rectangles only: with the smallest size set very low, thick white letters and small bright icons count as
  rectangles and are inverted one by one.
- Rectangles only: a small bright thing inside a dark area that itself lies inside a found rectangle (a white
  button on a dark card on a white page) is inverted with the page, whatever its size.
- Rectangles only: finding them adds about 10 ms to each analysis on a full-HD lens, and the detailed
  picture it needs is about half a megabyte per analysis.
- Analysis takes 4 to 8 ms and table building 5 to 10 ms on a 4-core machine: together at the edge of the
  15 ms budget of section 7.6. Both run off the frame loop.
