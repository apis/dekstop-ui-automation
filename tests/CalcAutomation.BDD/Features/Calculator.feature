Feature: Windows Calculator – Basic Arithmetic
  As a QA engineer
  I want to verify basic arithmetic operations using plain-English test scenarios
  So that tests are readable and maintainable without deep coding knowledge

  Background:
    Given the Calculator application is open

  # Section 1 – Named scenarios

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

  # Section 2 – Parameterised outline

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

  # Section 3 – Edge cases

  Scenario: Division by zero shows an error
    When I divide 5 by 0
    Then the display should contain "Cannot divide by zero"

  Scenario: Subtracting a larger number gives a negative result
    When I subtract 10 from 3
    Then the display should show -7

  Scenario: Multiplying any number by zero gives zero
    When I multiply 99999 by 0
    Then the display should show 0

  Scenario: Division producing a decimal result
    When I divide 1 by 4
    Then the display should show "0.25"

  Scenario: Division producing a repeating decimal
    When I divide 2 by 3
    Then the display should contain "0.6666666666"

  # Section 4 – Boundary outline

  Scenario Outline: Edge case arithmetic – <left> <operator> <right> equals <expected>
    When I calculate <left> <operator> <right>
    Then the display should show <expected>

    Examples: Boundary and sign-change values
      | left      | operator | right | expected   |
      | 999999999 | +        | 1     | 1000000000 |
      | 2000000   | *        | 3     | 6000000    |
      | 100       | -        | 200   | -100       |
      | 7         | -        | 7     | 0          |
      | 0         | +        | 0     | 0          |
      | 1000000   | /        | 4     | 250000     |

  # Section 5 – Identity and zero properties

  Scenario: Dividing a number by itself gives one
    When I divide 123 by 123
    Then the display should show 1

  Scenario: Dividing a large number by itself gives one
    When I divide 999 by 999
    Then the display should show 1

  Scenario: Subtracting a number from itself gives zero
    When I subtract 45 from 45
    Then the display should show 0

  Scenario: Adding zero does not change the value
    When I add 7 and 0
    Then the display should show 7

  Scenario Outline: Multiplying by zero always gives zero – <left> * 0
    When I calculate <left> * 0
    Then the display should show 0

    Examples: Multiply by zero cases
      | left   |
      | 1      |
      | 42     |
      | 10000  |

  # Section 6 – Commutativity

  Scenario Outline: Addition is commutative – <a> + <b> == <b> + <a>
    When I calculate <a> + <b>
    Then the display should show <result>
    When I calculate <b> + <a>
    Then the display should show <result>

    Examples: Commutative pairs
      | a  | b  | result |
      | 5  | 10 | 15     |
      | 3  | 7  | 10     |
      | 12 | 8  | 20     |

  # Section 7 – Division edge cases

  Scenario: Zero divided by any positive number is zero
    When I divide 0 by 5
    Then the display should show 0

  Scenario: Zero divided by a large positive number is zero
    When I divide 0 by 999
    Then the display should show 0

  Scenario: Division with remainder as decimal
    When I divide 10 by 4
    Then the display should show "2.5"

  Scenario: Division producing an integer result
    When I divide 144 by 12
    Then the display should show 12
