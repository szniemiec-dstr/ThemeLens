Feature: Colours on screen are given roles
  The engine detects up to 8 colour clusters under the lens and assigns each a role. (FR-07)

  Scenario Outline: Every screen yields a small set of clusters
    Given the theme "Soft dark"
    And the screen shows "<fixture>"
    When the lens renders the frame
    Then the lens finds at most 8 colour clusters and gives each a role

    Examples:
      | fixture               |
      | light-document.png    |
      | dark-editor.png       |
      | light-spreadsheet.png |
      | dark-spreadsheet.png  |
      | web-buttons.png       |
      | status-dashboard.png  |
      | chart.png             |
      | photo-on-white.png    |
      | gradient.png          |
      | terminal.png          |
      | mixed-halves.png      |

  Scenario: A light document
    Given the theme "Soft dark"
    And the screen shows "light-document.png"
    When the lens renders the frame
    Then the colour "#FFFFFF" is used as "Page"
    And the colour "#222222" is used as "Text"
    And the colour "#F1F3F5" is used as "Panel"
    And the colour "#FFFFFF" is shown as the theme's "Page" colour
    And the colour "#222222" is shown as the theme's "Text" colour
    And the colour "#F1F3F5" is shown as the theme's "Panel" colour

  Scenario: A dark editor
    Given the theme "Soft dark"
    And the screen shows "dark-editor.png"
    When the lens renders the frame
    Then the colour "#1E1E1E" is used as "Page"
    And the colour "#D4D4D4" is used as "Text"
    And the colour "#1E1E1E" is shown as the theme's "Page" colour
    And the colour "#D4D4D4" is shown as the theme's "Text" colour

  Scenario: Status colours
    Given the theme "Red–green assist dark"
    And the screen shows "status-dashboard.png"
    When the lens renders the frame
    Then the colour "#2E9E44" is used as "Good"
    And the colour "#D93025" is used as "Bad"

  Scenario: Greyscale removes colour
    Given the theme "Greyscale"
    And the screen shows "web-buttons.png"
    When the lens renders the frame
    Then the picture has no colour left

  Scenario: Dimmer lowers brightness and nothing else
    Given the theme "Dimmer"
    And the screen shows "light-document.png"
    When the lens renders the frame
    Then no pixel is brighter than the theme's brightness limit
    And text contrast is at least 7 to 1
