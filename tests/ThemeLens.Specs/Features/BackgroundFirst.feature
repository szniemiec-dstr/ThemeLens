Feature: The background stays the one I chose
  Inside the lens, the background is always the one the user chose. If the app cannot keep up,
  loses the capture, or is unsure, it shows the plain theme background rather than risk one
  bright frame on a dark theme. (FR-10, FR-11, FR-12)

  Rule: A dark theme never shows a large bright area

    Scenario Outline: Bright content under a dark theme
      Given the theme "<theme>" with keep background "Strict"
      And the screen shows "<fixture>"
      When the lens renders the frame
      Then the page colour on screen is the theme's page colour
      And the largest bright area is at most 0.5% of the lens

      Examples:
        | theme                 | fixture               |
        | Soft dark             | light-document.png    |
        | Soft dark             | light-spreadsheet.png |
        | Soft dark             | photo-on-white.png    |
        | Soft dark             | web-buttons.png       |
        | Soft dark             | status-dashboard.png  |
        | Soft dark             | chart.png             |
        | Soft dark             | dark-editor.png       |
        | Soft dark             | dark-spreadsheet.png  |
        | Soft dark             | terminal.png          |
        | Soft dark             | mixed-halves.png      |
        | High contrast dark    | light-document.png    |
        | High contrast dark    | light-spreadsheet.png |
        | High contrast dark    | web-buttons.png       |
        | High contrast dark    | status-dashboard.png  |
        | High contrast dark    | chart.png             |
        | High contrast dark    | dark-editor.png       |
        | High contrast dark    | terminal.png          |
        | High contrast dark    | mixed-halves.png      |
        | Night warm            | web-buttons.png       |
        | Night warm            | light-document.png    |
        | Night warm            | light-spreadsheet.png |
        | Night warm            | status-dashboard.png  |
        | Night warm            | dark-editor.png       |
        | Night warm            | mixed-halves.png      |
        | Red–green assist dark | status-dashboard.png  |
        | Red–green assist dark | light-document.png    |
        | Red–green assist dark | web-buttons.png       |
        | Red–green assist dark | chart.png             |
        | Red–green assist dark | dark-editor.png       |
        | Red–green assist dark | mixed-halves.png      |

    Scenario Outline: Pictures and gradients under a dark theme
      A photo or a sweep of colours has no page colour to check, but it must not glare either.

      Given the theme "<theme>" with keep background "Strict"
      And the screen shows "<fixture>"
      When the lens renders the frame
      Then the largest bright area is at most 0.5% of the lens

      Examples:
        | theme              | fixture            |
        | Soft dark          | gradient.png       |
        | High contrast dark | gradient.png       |
        | High contrast dark | photo-on-white.png |
        | Night warm         | photo-on-white.png |

    Scenario: Switching from a light app to a dark app
      Given the theme "Soft dark"
      And the lens has shown "light-document.png" for 2 seconds
      When the screen changes to "dark-editor.png"
      Then the very next frame has no large bright area
      And text in that frame is lighter than its background

    Scenario: Switching from a dark app to a light app
      Given the theme "Soft dark"
      And the lens has shown "dark-editor.png" for 2 seconds
      When the screen changes to "light-document.png"
      Then the very next frame has no large bright area
      And text in that frame is lighter than its background

  Rule: A light theme never shows a large dark area

    Scenario Outline: Dark content under a light theme
      Given the theme "<theme>" with keep background "Strict"
      And the screen shows "<fixture>"
      When the lens renders the frame
      Then the page colour on screen is the theme's page colour
      And the largest dark area is at most 0.5% of the lens

      Examples:
        | theme               | fixture              |
        | Clear white         | dark-editor.png      |
        | Clear white         | dark-spreadsheet.png |
        | Clear white         | terminal.png         |
        | Clear white         | light-document.png   |
        | Clear white         | mixed-halves.png     |
        | Soft paper          | dark-editor.png      |
        | Soft paper          | dark-spreadsheet.png |
        | Soft paper          | terminal.png         |
        | Soft paper          | light-document.png   |
        | Soft paper          | mixed-halves.png     |
        | High contrast light | dark-editor.png      |
        | High contrast light | dark-spreadsheet.png |
        | High contrast light | terminal.png         |
        | High contrast light | light-spreadsheet.png|
        | Evening paper       | dark-editor.png      |
        | Evening paper       | terminal.png         |
        | Evening paper       | light-document.png   |
        | Evening paper       | mixed-halves.png     |

  Rule: When the picture cannot be trusted, show the background

    Scenario: Capture stops working
      Given the lens is live with the theme "Soft dark"
      When the frame source reports a fault
      Then the next presented frame is entirely the theme's page colour
      And the status is "Holding background"
      And capture has been restarted

    Scenario: Turning the lens on
      Given the theme "Clear white"
      When I turn the lens on
      Then the first presented frame is entirely the theme's page colour
      And the status becomes "Live" after the first fresh frame

    Scenario Outline: Turning the lens on in any theme with a background
      Given the theme "<theme>"
      When I turn the lens on
      Then the first presented frame is entirely the theme's page colour
      And the status becomes "Live" after the first fresh frame

      Examples:
        | theme                 |
        | Soft dark             |
        | High contrast dark    |
        | Red–green assist dark |
        | Night warm            |
        | Soft paper            |
        | High contrast light   |
        | Evening paper         |

    Scenario: The display changes under the lens
      Sleep and resume, a monitor connected or removed, a new resolution or DPI. (FR-19)

      Given the lens is live with the theme "Soft dark"
      When the display changes
      Then the next presented frame is entirely the theme's page colour
      And the status is "Holding background"
      And capture has been restarted

    Scenario: The frame loop misses its deadline
      Given the lens is live with the theme "Soft dark"
      When the frame loop stalls for 800 ms
      Then the next presented frame is entirely the theme's page colour
      And the status is "Holding background"

    Scenario: The lens comes back after a stall
      Given the lens is live with the theme "Soft dark"
      When the frame loop stalls for 800 ms
      And the frame loop runs again
      Then the status is "Live"
      And every presented frame since then is a complete frame or the page colour

    Scenario: Screen capture is not allowed
      If capture is blocked by policy or unsupported, the app explains why and does not show a lens. (FR-20)

      Given the theme "Soft dark"
      And screen capture is blocked
      When I turn the lens on
      Then no lens is shown
      And the app explains that capture is unavailable
