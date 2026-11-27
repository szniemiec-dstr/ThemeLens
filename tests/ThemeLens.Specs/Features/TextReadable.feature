Feature: Text is readable in every theme

  Scenario Outline: Minimum text contrast
    Given the theme "<theme>"
    And the screen shows "<fixture>"
    When the lens renders the frame
    Then text contrast is at least <ratio> to 1
    And text edges stay smooth

    Examples:
      | theme                 | fixture               | ratio |
      | Soft dark             | light-document.png    | 7     |
      | Soft dark             | dark-editor.png       | 7     |
      | Soft dark             | light-spreadsheet.png | 7     |
      | Soft dark             | dark-spreadsheet.png  | 7     |
      | Soft dark             | terminal.png          | 7     |
      | Soft dark             | web-buttons.png       | 7     |
      | Soft dark             | status-dashboard.png  | 7     |
      | High contrast dark    | light-document.png    | 15    |
      | High contrast dark    | dark-editor.png       | 15    |
      | High contrast dark    | light-spreadsheet.png | 15    |
      | High contrast dark    | terminal.png          | 15    |
      | High contrast light   | dark-editor.png       | 15    |
      | High contrast light   | light-document.png    | 15    |
      | High contrast light   | terminal.png          | 15    |
      | High contrast light   | dark-spreadsheet.png  | 15    |
      | Red–green assist dark | light-document.png    | 7     |
      | Red–green assist dark | dark-editor.png       | 7     |
      | Red–green assist dark | status-dashboard.png  | 7     |
      | Night warm            | light-document.png    | 7     |
      | Night warm            | dark-editor.png       | 7     |
      | Night warm            | terminal.png          | 7     |
      | Clear white           | dark-editor.png       | 7     |
      | Clear white           | light-document.png    | 7     |
      | Clear white           | terminal.png          | 7     |
      | Soft paper            | terminal.png          | 7     |
      | Soft paper            | dark-editor.png       | 7     |
      | Soft paper            | light-document.png    | 7     |
      | Evening paper         | light-document.png    | 7     |
      | Evening paper         | dark-editor.png       | 7     |
      | Wake up dark          | light-document.png    | 7     |
      | Wake up dark          | dark-editor.png       | 7     |
      | Wake up dark          | terminal.png          | 7     |
      | Wake up               | light-document.png    | 7     |
      | Wake up               | dark-editor.png       | 7     |
      | Wake up               | terminal.png          | 7     |
      | Wake up sky           | light-document.png    | 7     |
      | Wake up sky           | dark-editor.png       | 7     |
      | Wake up sky           | terminal.png          | 7     |
      | Evening paper         | terminal.png          | 7     |

  Scenario Outline: A lower brightness limit keeps text as readable as the limit allows
    Given the theme "Soft dark" with brightness limit <limit>%
    And the screen shows "light-document.png"
    When the lens renders the frame
    Then text contrast is at least <ratio> to 1
    And no pixel is brighter than the theme's brightness limit

    Examples:
      | limit | ratio |
      | 40    | 8.5   |
      | 30    | 6.5   |
      | 20    | 4.5   |
