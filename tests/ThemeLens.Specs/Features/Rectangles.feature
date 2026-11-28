Feature: Rectangles only
  With "rectangles only" switched on, the lens leaves the screen as it is and changes only large
  rectangles of one kind: bright ones, to avoid light, or dark ones, to avoid darkness. How bright
  a rectangle has to be depends on its size. A found rectangle is plainly inverted, whatever the
  theme, to its negative, unless the user asks for hues to be kept or for the theme's colours.
  (FR-26, FR-27, FR-28, FR-29)

  Rule: Bright rectangles are inverted, whatever the theme, and nothing else is touched

    Scenario Outline: A desktop with windows of different sizes and brightness
      The large window is as large as a square of 338 pixels, the white dialog and the grey panel
      as one of about 145, the note as one of 49.

      Given the theme "<theme>" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "large-window" is shown dark
      And what is written on the "large-window" has a contrast of at least 7 to 1
      And the "title-bar" is shown dark
      And the "small-dialog" is shown dark
      And the "grey-panel" is shown as it is on the screen
      And the "tiny-note" is shown as it is on the screen
      And the "dark-window" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen
      And the "picture" is shown as it is on the screen

      Examples:
        | theme        |
        | Soft dark    |
        | Clear white  |
        | Wake up sky  |
        | Passthrough  |
        | Less blue    |

    Scenario Outline: How bright a rectangle has to be depends on its size
      Given the theme "Passthrough" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And the screen shows a rectangle <brightness>% bright as large as a square of <side> pixels
      When the lens renders the frame
      Then the "rectangle" is <outcome>
      And the "desktop" is shown as it is on the screen

      Examples:
        | side | brightness | outcome                      |
        | 300  | 76         | shown dark                   |
        | 300  | 100        | shown dark                   |
        | 300  | 72         | shown as it is on the screen |
        | 200  | 90         | shown dark                   |
        | 200  | 80         | shown as it is on the screen |
        | 104  | 100        | shown dark                   |
        | 104  | 92         | shown as it is on the screen |
        | 60   | 100        | shown as it is on the screen |

    Scenario: The smallest size the bar offers is a square of 10 by 10 pixels
      Given the theme "Passthrough" with bright rectangles only, from 10 to 300 pixels at 75% brightness
      And the screen shows a rectangle 100% bright as large as a square of 10 pixels
      When the lens renders the frame
      Then the "rectangle" is shown dark
      And the "desktop" is shown as it is on the screen

    Scenario: A page with dark buttons on it is one rectangle, and the buttons stay
      Given the theme "Passthrough" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And the screen shows "dark-buttons.png"
      When the lens renders the frame
      Then the "gap-between-buttons" is shown dark
      And the "tile-gutter" is shown dark
      And what is written on the "first-button" has a contrast of at least 7 to 1
      And the "toolbar-label" is shown as it is on the screen

    Scenario: The inversion can keep hues
      By default a rectangle is inverted to its negative. With hues kept, light and dark are swapped
      and colours stay the colours they were.

      Given the theme "Passthrough" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And hues are kept when a rectangle is inverted
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "large-window" is shown dark
      And what is written on the "large-window" has a contrast of at least 7 to 1
      And the "grey-panel" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen

  Rule: Dark rectangles are inverted the same way

    Scenario Outline: A light desktop with dark windows
      Given the theme "<theme>" with dark rectangles only, from 100 to 300 pixels at 25% brightness
      And the screen shows "desktop-windows-light.png"
      When the lens renders the frame
      Then the "large-window" is shown light
      And what is written on the "large-window" has a contrast of at least 7 to 1
      And the "small-dialog" is shown light
      And the "grey-panel" is shown as it is on the screen
      And the "tiny-note" is shown as it is on the screen
      And the "dark-window" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen

      Examples:
        | theme       |
        | Clear white |
        | Soft dark   |
        | Passthrough |

    Scenario Outline: How dark a rectangle has to be depends on its size
      Given the theme "Passthrough" with dark rectangles only, from 100 to 300 pixels at 25% brightness
      And the screen shows a rectangle <brightness>% bright as large as a square of <side> pixels
      When the lens renders the frame
      Then the "rectangle" is <outcome>
      And the "desktop" is shown as it is on the screen

      Examples:
        | side | brightness | outcome                      |
        | 300  | 24         | shown light                  |
        | 300  | 28         | shown as it is on the screen |
        | 200  | 8          | shown light                  |
        | 200  | 20         | shown as it is on the screen |
        | 104  | 0          | shown light                  |
        | 60   | 0          | shown as it is on the screen |

  Rule: On request a found rectangle is shown in the theme's colours

    Scenario: Bright rectangles under a dark theme
      Given the theme "Wake up dark" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And found rectangles are shown in the theme's colours
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "large-window" is shown close to the page colour
      And what is written on the "large-window" has a contrast of at least 7 to 1
      And the "small-dialog" is shown close to the page colour
      And the "grey-panel" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen
      And the "picture" is shown as it is on the screen

    Scenario: The theme's finishing passes stay inside the rectangles
      Given the theme "Soft dark" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And found rectangles are shown in the theme's colours
      And sharpen at 60%
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "large-window" is shown close to the page colour
      And the "grey-panel" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen

    Scenario: A theme that cannot turn the rectangle round leaves it to the plain inversion
      Given the theme "Clear white" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And found rectangles are shown in the theme's colours
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "large-window" is shown dark
      And the "desktop" is shown as it is on the screen

  Rule: The lens never shows a bright rectangle before it has looked

    Scenario: Turning the lens on with rectangles only
      Until the first rectangles are found the lens inverts everything bright, so a white window is
      never shown white first. Then the small bright things come back.

      Given the theme "Soft dark" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And the lens is live over "desktop-windows.png"
      Then no frame so far shows the page of the "large-window" bright
      And after 1 second the "desktop" is shown as it is on the screen
      And after 1 second the "tiny-note" is shown as it is on the screen
      And after 1 second the "large-window" is shown dark

    Scenario: Switching the option on while the lens is live
      Given the theme "Soft dark"
      And the lens is live over "desktop-windows.png" for 2 seconds
      When I switch rectangles only on
      Then no frame so far shows the page of the "large-window" bright
      And after 1 second the "desktop" is shown as it is on the screen
      And after 1 second the "large-window" is shown dark

    Scenario: Switching the option off again
      What was dark stays dark while the rest of the screen fades back into the theme.

      Given the theme "Soft dark" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And the lens is live over "desktop-windows.png" for 2 seconds
      When I switch rectangles only off
      Then no frame so far shows the page of the "large-window" bright
      And after 1 second the "grey-panel" is shown close to the page colour
      And no frame so far shows the page of the "large-window" bright

    Scenario: A bright window is closed
      The rectangle that is no longer found fades out; it does not jump.

      Given the theme "Soft dark" with bright rectangles only, from 100 to 300 pixels at 75% brightness
      And the lens is live over "desktop-windows.png" for 2 seconds
      When the screen changes to "dark-editor.png"
      Then the status is "Live"
      And after 1 second the picture on screen is the screen as it is
