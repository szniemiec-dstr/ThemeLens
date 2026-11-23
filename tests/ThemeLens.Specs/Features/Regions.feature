Feature: Buttons, chips and toolbars are not text
  A black button on a white page is not text. Under a dark theme it stays dark and keeps its label:
  it does not turn into a bright block, and the gaps between such buttons do not light up. Small
  things drawn on a dark toolbar, like a slider, stay visible. (FR-10)

  Scenario Outline: Dark shapes on a light page
    Given the theme "<theme>"
    And the screen shows "dark-buttons.png"
    When the lens renders the frame
    Then the page colour on screen is the theme's page colour
    And the "first-button" is shown close to the page colour
    And the "gap-between-buttons" is shown close to the page colour
    And the "tile-gutter" is shown close to the page colour
    And what is written on the "first-button" has a contrast of at least <ratio> to 1
    And what is written on the "toolbar-label" has a contrast of at least <ratio> to 1
    And what is written on the "slider" has a contrast of at least 3 to 1

    Examples:
      | theme               | ratio |
      | Soft dark           | 7     |
      | High contrast dark  | 15    |
      | Night warm          | 7     |
      | Clear white         | 7     |
      | Soft paper          | 7     |
      | High contrast light | 15    |
