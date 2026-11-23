Feature: The picture stays steady

  Scenario: A small moving element does not reshuffle the colours
    Given the theme "Soft dark"
    And a 5 second recording "blinking-cursor-and-spinner"
    When the lens processes the recording
    Then no role changes its cluster
    And mean lens luminance changes by less than 2% between any two frames

  Scenario: Changing theme does not flash
    Given the lens is live with the theme "Soft dark" on "light-document.png"
    When I switch to the theme "Night warm"
    Then mean lens luminance moves only towards its new value
    And the change completes within 250 ms

  Scenario Outline: Changing between any two themes does not flash
    Given the lens is live with the theme "<from>" on "<fixture>"
    When I switch to the theme "<to>"
    Then mean lens luminance moves only towards its new value
    And the change completes within 250 ms

    Examples:
      | from               | to                  | fixture            |
      | Soft dark          | High contrast dark  | light-document.png |
      | Soft dark          | Clear white         | light-document.png |
      | Clear white        | Soft dark           | dark-editor.png    |
      | High contrast dark | Soft paper          | web-buttons.png    |
      | Night warm         | Soft dark           | dark-editor.png    |
      | Soft dark          | Less blue           | light-document.png |
