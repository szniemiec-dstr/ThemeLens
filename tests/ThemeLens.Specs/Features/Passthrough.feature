Feature: Passthrough, contrast and sharpen

  Scenario Outline: Passthrough changes nothing
    Given the theme "Passthrough" with contrast 0% and sharpen 0%
    And the screen shows "<fixture>"
    When the lens renders the frame
    Then the lens picture is identical to the screen

    Examples:
      | fixture            |
      | light-document.png |
      | dark-editor.png    |
      | photo-on-white.png |
      | gradient.png       |

  Scenario Outline: Sharpening stays inside the theme's limits
    Given the theme "<theme>" with sharpen <amount>% in "<mode>" mode
    And the screen shows "light-document.png"
    When the lens renders the frame
    Then edges are stronger than with sharpen off
    And no pixel is brighter than the theme's brightness limit
    And the largest bright area is at most 0.5% of the lens

    Examples:
      | theme      | amount | mode     |
      | Soft dark  | 50     | Standard |
      | Soft dark  | 100    | Strong   |
      | Night warm | 100    | Strong   |
      | Night warm | 50     | Standard |

  Scenario Outline: Sharpening on a light theme never goes brighter than the page
    Given the theme "<theme>" with sharpen <amount>% in "<mode>" mode
    And the screen shows "dark-editor.png"
    When the lens renders the frame
    Then edges are stronger than with sharpen off
    And no pixel is brighter than the page

    Examples:
      | theme         | amount | mode     |
      | Soft paper    | 100    | Standard |
      | Soft paper    | 100    | Strong   |
      | Evening paper | 100    | Strong   |

  Scenario Outline: Contrast never makes text harder to read
    Given the theme "<theme>" with contrast <amount>% in "<mode>" mode
    And the screen shows "light-document.png"
    When the lens renders the frame
    Then text contrast is at least as high as with contrast off
    And text edges stay smooth

    Examples:
      | theme       | amount | mode     |
      | Passthrough | 50     | Standard |
      | Passthrough | 50     | Local    |
      | Soft paper  | 30     | Standard |
      | Soft dark   | 100    | Local    |
      | Soft dark   | 100    | Standard |
      | Clear white | 100    | Local    |

  Scenario: A slow renderer is allowed, an unsafe frame is not
    Given the theme "Soft dark" with sharpen 100% in "Strong" mode
    And a renderer that needs 60 ms for each frame
    When the screen changes from "dark-editor.png" to "light-document.png"
    Then the status is "Live, reduced speed"
    And the option named as slowest is "Strong sharpen"
    And sharpen is still on
    And no presented frame has a large bright area

  Scenario: Passthrough costs nothing
    With contrast and sharpen at zero the lens body is hidden and nothing is captured.

    Given the theme "Passthrough"
    When I turn the lens on
    Then no lens is shown
    And nothing is captured
    And the lens state is "Live"

  Scenario: Leaving Passthrough goes through the background
    Given the theme "Passthrough"
    And the screen shows "light-document.png"
    When I turn the lens on
    And I switch to the theme "Soft dark"
    Then the lens is shown
    And the status is "Live"
    And no presented frame has a large bright area
