Feature: Windows Calculator – Basic Arithmetic
  As a QA engineer
  I want to verify basic arithmetic operations using plain-English test scenarios
  So that tests are readable and maintainable without deep coding knowledge

  Background:
    Given the Calculator application is open

  # Section 1 – Smoke tests (one named scenario per operator)

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

  # Section 2 – Edge cases

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

  # Section 3 – Boundary values

  Scenario Outline: Boundary arithmetic – <left> <operator> <right> equals <expected>
    When I calculate <left> <operator> <right>
    Then the display should show <expected>

    Examples: Boundary and sign-change values
      | left      | operator | right | expected   |
      | 999999999 | +        | 1     | 1000000000 |
      | 2000000   | *        | 3     | 6000000    |
      | 0         | +        | 0     | 0          |
      | 1000000   | /        | 4     | 250000     |

  # Section 4 – Identity properties

  Scenario: Dividing a number by itself gives one
    When I divide 123 by 123
    Then the display should show 1

  Scenario: Subtracting a number from itself gives zero
    When I subtract 45 from 45
    Then the display should show 0

  Scenario: Adding zero does not change the value
    When I add 7 and 0
    Then the display should show 7

  # Section 5 – Commutativity

  Scenario: Addition is commutative
    When I calculate 5 + 10
    Then the display should show 15
    When I calculate 10 + 5
    Then the display should show 15

  # Section 6 – Division edge cases

  Scenario: Zero divided by a positive number is zero
    When I divide 0 by 5
    Then the display should show 0

  Scenario: Division with remainder as decimal
    When I divide 10 by 4
    Then the display should show "2.5"

  # Section 7 – Square root

  Scenario: Square root of a perfect square
    When I take the square root of 25
    Then the display should show 5

  Scenario: Square root of 1 is 1
    When I take the square root of 1
    Then the display should show 1

  Scenario: Square root of 0 is 0
    When I take the square root of 0
    Then the display should show 0

  Scenario: Square root of 2 is irrational
    When I take the square root of 2
    Then the display should contain "1.41421356"

  Scenario Outline: Square root of perfect squares – √<number> equals <expected>
    When I take the square root of <number>
    Then the display should show <expected>

    Examples: Small and large perfect squares
      | number | expected |
      | 4      | 2        |
      | 144    | 12       |

  # Section 8 – Squaring (x²)

  Scenario: Squaring zero gives zero
    When I square 0
    Then the display should show 0

  Scenario: Squaring one gives one
    When I square 1
    Then the display should show 1

  Scenario Outline: Squaring numbers – <number>² equals <expected>
    When I square <number>
    Then the display should show <expected>

    Examples: Squares
      | number | expected |
      | 5      | 25       |
      | 12     | 144      |

  # Section 9 – Reciprocal (1/x)

  Scenario: Reciprocal of 1 is 1
    When I take the reciprocal of 1
    Then the display should show 1

  Scenario: Reciprocal of 2 is 0.5
    When I take the reciprocal of 2
    Then the display should show "0.5"

  Scenario: Reciprocal of zero is undefined
    When I take the reciprocal of 0
    Then the display should contain "Cannot divide by zero"

  # Section 10 – Negate (+/-)

  Scenario: Negating a positive number gives a negative result
    When I negate 7
    Then the display should show -7

  Scenario: Negating zero gives zero
    When I negate 0
    Then the display should show 0
