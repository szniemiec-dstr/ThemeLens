Feature: Lens states
  Off, Holding background, Live and Edit (figure 8 of the specification).

  Scenario: Turning the lens off
    Given the lens is live with the theme "Soft dark"
    When I turn the lens off
    Then the lens state is "Off"
    And no lens is shown
    And nothing is captured

  Scenario: One shortcut removes the lens from edit mode
    Given the lens is live with the theme "Soft dark"
    When I enter edit mode
    And I press the everything-off shortcut
    Then the lens state is "Off"
    And no lens is shown

  Scenario: One shortcut removes the lens while it is holding the background
    Given the lens is live with the theme "Soft dark"
    When the frame source reports a fault
    And I press the everything-off shortcut
    Then the lens state is "Off"
    And no lens is shown

  Scenario: Edit mode keeps the content themed
    Given the lens is live with the theme "Soft dark"
    When I enter edit mode
    Then the lens state is "Edit"
    And the status is "Live"

  Scenario: Leaving edit mode locks the lens again
    Given the lens is live with the theme "Soft dark"
    When I enter edit mode
    And I leave edit mode
    Then the lens state is "Live"
