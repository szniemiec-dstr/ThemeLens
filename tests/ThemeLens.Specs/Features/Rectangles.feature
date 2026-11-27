Feature: Rectangles only
  With "rectangles only" switched on, the lens leaves the screen as it is and changes only large
  rectangles of the wrong kind: bright ones under a dark theme, dark ones under a light theme. How
  bright a rectangle has to be depends on how much of the screen it covers. (FR-26, FR-27, FR-28)

  Rule: Under a dark theme, large bright rectangles are darkened and nothing else is touched

    Scenario: A desktop with windows of different sizes and brightness
      The large window covers 44% of the screen, the white dialog and the grey panel 8% each, the
      note under 1%.

      Given the theme "Soft dark" with rectangles only from 2% to 20% of the screen at 75% brightness
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "large-window" is shown close to the page colour
      And what is written on the "large-window" has a contrast of at least 7 to 1
      And the "title-bar" is shown close to the page colour
      And the "small-dialog" is shown close to the page colour
      And the "grey-panel" is shown as it is on the screen
      And the "tiny-note" is shown as it is on the screen
      And the "dark-window" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen
      And the "picture" is shown as it is on the screen

    Scenario: The same desktop with sharpening on
      The finishing passes must not leak out of the rectangles either.

      Given the theme "Soft dark" with rectangles only from 2% to 20% of the screen at 75% brightness
      And sharpen at 60%
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "large-window" is shown close to the page colour
      And the "grey-panel" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen

    Scenario Outline: How bright a rectangle has to be depends on its size
      Given the theme "Soft dark" with rectangles only from 2% to 20% of the screen at 75% brightness
      And the screen shows a rectangle <brightness>% bright that covers <size>% of the screen
      When the lens renders the frame
      Then the "rectangle" is <outcome>
      And the "desktop" is shown as it is on the screen

      Examples:
        | size | brightness | outcome                        |
        | 25   | 76         | shown close to the page colour |
        | 25   | 100        | shown close to the page colour |
        | 25   | 72         | shown as it is on the screen   |
        | 12   | 92         | shown close to the page colour |
        | 12   | 80         | shown as it is on the screen   |
        | 3    | 100        | shown close to the page colour |
        | 3    | 92         | shown as it is on the screen   |
        | 1.5  | 100        | shown as it is on the screen   |

    Scenario: The size is a share of the screen, not of the lens
      On a screen four times the size of the lens the same dialog covers 2%, not 8%, and stays.

      Given the theme "Soft dark" with rectangles only from 3% to 20% of the screen at 75% brightness
      And the screen is 4 times the size of the lens
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "small-dialog" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen

    Scenario: With the option off the lens changes everything, as before
      Given the theme "Soft dark"
      And the screen shows "desktop-windows.png"
      When the lens renders the frame
      Then the "grey-panel" is shown close to the page colour
      And the "tiny-note" is shown close to the page colour

  Rule: Under a light theme, large dark rectangles are lightened and nothing else is touched

    Scenario: A light desktop with dark windows
      Given the theme "Clear white" with rectangles only from 2% to 20% of the screen at 25% brightness
      And the screen shows "desktop-windows-light.png"
      When the lens renders the frame
      Then the "large-window" is shown close to the page colour
      And what is written on the "large-window" has a contrast of at least 7 to 1
      And the "small-dialog" is shown close to the page colour
      And the "grey-panel" is shown as it is on the screen
      And the "tiny-note" is shown as it is on the screen
      And the "dark-window" is shown as it is on the screen
      And the "desktop" is shown as it is on the screen

    Scenario Outline: How dark a rectangle has to be depends on its size
      Given the theme "Clear white" with rectangles only from 2% to 20% of the screen at 25% brightness
      And the screen shows a rectangle <brightness>% bright that covers <size>% of the screen
      When the lens renders the frame
      Then the "rectangle" is <outcome>
      And the "desktop" is shown as it is on the screen

      Examples:
        | size | brightness | outcome                        |
        | 25   | 24         | shown close to the page colour |
        | 25   | 28         | shown as it is on the screen   |
        | 12   | 8          | shown close to the page colour |
        | 12   | 20         | shown as it is on the screen   |
        | 3    | 0          | shown close to the page colour |
        | 1.5  | 0          | shown as it is on the screen   |

  Rule: The lens never shows a bright rectangle before it has looked

    Scenario: Turning the lens on with rectangles only
      Until the first rectangles are found the lens changes everything, so a white window is never
      shown white first. Then the rest of the screen comes back.

      Given the theme "Soft dark" with rectangles only from 2% to 20% of the screen at 75% brightness
      And the lens is live over "desktop-windows.png"
      Then no frame so far shows the "large-window" brighter than the theme allows
      And after 1 second the "desktop" is shown as it is on the screen
      And after 1 second the "large-window" is shown close to the page colour

    Scenario: Switching the option on while the lens is live
      Given the theme "Soft dark"
      And the lens is live over "desktop-windows.png" for 2 seconds
      When I switch rectangles only on
      Then no frame so far shows the "large-window" brighter than the theme allows
      And after 1 second the "desktop" is shown as it is on the screen
      And after 1 second the "large-window" is shown close to the page colour

    Scenario: Switching the option off again
      What was dark stays dark while the rest of the screen fades back into the theme.

      Given the theme "Soft dark" with rectangles only from 2% to 20% of the screen at 75% brightness
      And the lens is live over "desktop-windows.png" for 2 seconds
      When I switch rectangles only off
      Then no frame so far shows the "large-window" brighter than the theme allows
      And after 1 second the "grey-panel" is shown close to the page colour
      And no frame so far shows the "large-window" brighter than the theme allows

    Scenario: A bright window is closed
      The rectangle that is no longer found fades out; it does not jump.

      Given the theme "Soft dark" with rectangles only from 2% to 20% of the screen at 75% brightness
      And the lens is live over "desktop-windows.png" for 2 seconds
      When the screen changes to "dark-editor.png"
      Then the status is "Live"
      And after 1 second the picture on screen is the screen as it is
