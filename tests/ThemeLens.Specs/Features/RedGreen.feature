Feature: Reds and greens can be told apart
  Red–green assist does not restore colour vision. It moves differences that sit on the
  red–green confusion axis onto lightness and the blue–yellow axis. Simulation uses the
  matrices of Machado, Oliveira and Fernandes (2009).

  Scenario Outline: Status colours under simulated colour blindness
    Given the theme "<theme>"
    And the screen shows "status-dashboard.png"
    When the lens renders the frame
    And the result is viewed with simulated "<vision>"
    Then the "passed" and "failed" markers differ by at least 0.12 in OKLab
    And their lightness differs by at least 0.08

    Examples:
      | theme                 | vision       |
      | Red–green assist dark | protanopia   |
      | Red–green assist dark | deuteranopia |
      | Red–green assist      | protanopia   |
      | Red–green assist      | deuteranopia |

  Scenario Outline: Status colours in a document
    Given the theme "<theme>"
    And the screen shows "light-document.png"
    When the lens renders the frame
    And the result is viewed with simulated "<vision>"
    Then the "passed" and "failed" markers differ by at least 0.10 in OKLab

    Examples:
      | theme                 | vision       |
      | Red–green assist dark | protanopia   |
      | Red–green assist dark | deuteranopia |
      | Red–green assist      | protanopia   |
      | Red–green assist      | deuteranopia |
