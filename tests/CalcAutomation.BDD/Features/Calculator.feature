Feature: Windows Calculator – Basic Arithmetic
  As a QA engineer
  I want to verify basic arithmetic operations using plain-English test scenarios
  So that tests are readable and maintainable without deep coding knowledge

  Background:
    Given the Calculator application is open

  # ── Named scenarios (one per operation) ─────────────────────────────────────

  Scenario: Add two numbers
    When I add 7 and 5
    Then the display should show 12

  Scenario: Subtract two numbers
    When I subtract 4 from 10
    Then the display should show 6

  Scenario: Multiply two numbers
    When I multiply 3 by 3
    Then the display should show 9

  Scenario: Divide two numbers
    When I divide 20 by 4
    Then the display should show 5

  # ── Parameterised outline: QA can add new rows without touching C# code ─────

  Scenario Outline: Parameterised arithmetic – <left> <operator> <right> equals <expected>
    When I calculate <left> <operator> <right>
    Then the display should show <expected>

    Examples: Additional calculations
      | left | operator | right | expected |
      | 8    | +        | 2     | 10       |
      | 15   | -        | 7     | 8        |
      | 6    | *        | 7     | 42       |
      | 18   | /        | 3     | 6        |
      | 100  | /        | 4     | 25       |
      | 9    | *        | 9     | 81       |
