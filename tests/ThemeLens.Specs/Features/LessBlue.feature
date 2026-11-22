Feature: Less blue in the evening

  Scenario Outline: Blue output is reduced and text stays readable
    Given the theme "<theme>" with warmth <warmth>%
    And the screen shows "light-document.png"
    When the lens renders the frame
    Then the blue share is at most <share>% of the source's
    And text contrast is at least 7 to 1

    Examples:
      | theme         | warmth | share |
      | Night warm    | 100    | 35    |
      | Evening paper | 60     | 60    |
      | Less blue     | 40     | 75    |
      | Less blue     | 100    | 10    |
      | Soft dark     | 80     | 50    |

  Scenario Outline: Warming a dark screen keeps its text readable
    Given the theme "<theme>" with warmth <warmth>%
    And the screen shows "dark-editor.png"
    When the lens renders the frame
    Then text contrast is at least 7 to 1

    Examples:
      | theme      | warmth |
      | Night warm | 100    |
      | Soft dark  | 100    |
      | Less blue  | 100    |
