# BDD Coverage Gap Analysis — Windows Calculator

**Explored:** 2026-04-28 via FLA-UI accessibility tree inspection  
**Current coverage:** Standard mode only (32 scenarios)

---

## Standard Mode — Uncovered Controls

| Control | Accessibility Name | Gap |
|---|---|---|
| Backspace | `"Backspace"` | Behavior not tested |
| Clear Entry vs Clear | `"Clear entry"` / `"Clear"` | Distinction not tested |
| Percent | `"Percent"` | Alias wired, zero scenarios |
| Decimal separator | `"Decimal separator"` | Only tested via calculated results, not direct input |
| Chained operations | — | e.g. `5 + 3 - 2 = 6` never tested |
| Memory | `"Memory store"`, `"Memory add"`, `"Memory subtract"`, `"Memory recall"`, `"Clear all memory"` | Not tested |

---

## Scientific Mode — Entire Mode Uncovered

**Always-visible buttons:** `Square`, `Square root`, `'X' to the exponent`, `Ten to the exponent`, `Log`, `Natural log`, `Reciprocal`, `Absolute value`, `Exponential`, `Modulo`, `Factorial`, `Left parenthesis`, `Right parenthesis`, `Inverse function`, `Pi`, `Euler's number`, `Degrees toggle`, `Scientific notation`

**Trigonometry panel:** `Sine`, `Cosine`, `Tangent`, `Secant`, `Cosecant`, `Cotangent` + hyperbolic variants + inverse variants

**Functions panel:** `Absolute value`, `Floor`, `Ceiling`, `Random`, `Degree minute second`, `Degrees`

---

## Programmer Mode — Entire Mode Uncovered

**Radix selection:** HexaDecimal, Decimal, Octal, Binary  
**Hex digit pad:** `A`, `B`, `C`, `D`, `E`, `F` (disabled in Decimal mode)  
**Operators:** `Left shift`, `Right shift`, `Modulo`, `Left parenthesis`, `Right parenthesis`  
**Bitwise panel:** AND, OR, NOT, XOR, NAND, NOR  
**Bitshift panel:** arithmetic/logical shift variants  
**Word size toggle:** QWORD / DWORD / WORD / BYTE  
**Input modes:** Full keypad / Bit toggling keypad

---

## Date Calculation Mode — Entire Mode Uncovered

**Sub-modes (combobox):**
- Difference between dates
- Add / subtract days from a date

**Controls:** From date picker, To date picker, result text

---

## Converters — All 11 Types Uncovered

Currency, Volume, Length, Weight and mass, Temperature, Energy, Area, Speed, Time, Power, Data, Pressure, Angle

**Shared UI pattern:** numpad → input-unit combobox → output-unit combobox → result label + secondary result label

---

## Proposed Implementation Plan

### P1 — Standard Mode Gaps
*Effort: low — same infrastructure, additions to existing feature file*

| Scenario | Input | Expected |
|---|---|---|
| Backspace removes last digit | Enter `123`, press backspace | `12` |
| Backspace on single digit shows zero | Enter `5`, press backspace | `0` |
| CE clears input but preserves operator | `5 +`, CE, `3 =` | `8` |
| C resets the full expression | `5 +`, C, `3 =` | `3` |
| Percent of a number | `50 %` | `0.5` |
| Percent added to base | `200 + 10 %` | `220` |
| Direct decimal input | `3.14 + 1` | `4.14` |
| Chained addition and subtraction | `5 + 3 - 2` | `6` |
| Chained multiply then add | `2 * 3 + 4` | `10` |

---

### P2 — Scientific Mode
*Effort: medium — new feature file, new step definitions, mode-switching step*

**New infrastructure needed:**
- `Given I switch to Scientific mode` step
- New step defs for: `'X' to the exponent`, `Ten to the exponent`, `Log`, `Natural log`, `Factorial`, `Absolute value`, `Modulo`, `Pi`, `Euler's number`, trig buttons, inverse toggle

| Area | Scenario | Expected |
|---|---|---|
| Factorial | `0!` | `1` |
| Factorial | `1!` | `1` |
| Factorial | `5!` | `120` |
| Modulo | `7 mod 3` | `1` |
| Modulo | `0 mod 5` | `0` |
| Modulo | `9 mod 9` | `0` |
| Power (x^y) | `2 ^ 8` | `256` |
| Power (x^y) | `2 ^ 0` | `1` |
| Ten to exponent | `10 ^ 3` | `1000` |
| Log base 10 | `log(100)` | `2` |
| Log base 10 | `log(1)` | `0` |
| Natural log | `ln(1)` | `0` |
| Pi constant | press π | contains `3.14159265` |
| Euler's number | press e | contains `2.71828182` |
| Absolute value | `\|-5\|` | `5` |
| Absolute value | `\|0\|` | `0` |
| Parentheses | `(2 + 3) * 4` | `20` |
| Parentheses | `(10 - 4) / 2` | `3` |
| Sin (degrees) | `sin(90)` | `1` |
| Cos (degrees) | `cos(0)` | `1` |
| Tan (degrees) | `tan(45)` | `1` |
| Inverse sin | `sin⁻¹(1)` | `90` |

---

### P3 — Programmer Mode
*Effort: medium — new feature file, radix-switching steps, hex digit aliases*

**New infrastructure needed:**
- `Given I switch to Programmer mode` step
- `When I select {word} base` step (HEX/DEC/OCT/BIN radio buttons)
- Hex digit aliases: `["A"] = "A"` … `["F"] = "F"`
- Bitwise operator step defs

| Area | Scenario | Expected |
|---|---|---|
| Base display | Enter `255` (DEC), switch to HEX | `FF` |
| Base display | Enter `255` (DEC), switch to BIN | `11111111` |
| Base display | Enter `255` (DEC), switch to OCT | `377` |
| Hex arithmetic | `A + 5` (HEX) | `F` |
| Hex overflow | `FF + 1` (HEX) | `100` |
| Bitwise AND | `12 AND 10` | `8` |
| Bitwise OR | `12 OR 10` | `14` |
| Bitwise XOR | `12 XOR 10` | `6` |
| Left shift | `1 << 3` | `8` |
| Right shift | `16 >> 2` | `4` |
| BYTE overflow | Enter `256` in BYTE word size | `0` |

---

### P4 — Date Calculation Mode
*Effort: high — date picker requires calendar navigation steps*

**New infrastructure needed:**
- `Given I switch to Date calculation mode` step
- Steps to set From/To dates via calendar pickers
- Result text assertion step

| Scenario | Expected |
|---|---|
| Difference between same dates | `0 days` |
| Difference of exactly 1 year | `1 year, 0 months, 0 days` |
| Difference of exactly 1 month | `0 years, 1 month, 0 days` |
| Add 30 days to a known date | correct resulting date |
| Subtract 7 days from a known date | correct resulting date |

---

### P5 — Converters (representative sample)
*Effort: medium — new step defs for combobox unit selection and result reading*

**New infrastructure needed:**
- `When I convert {number} from {unit} to {unit}` step
- Combobox selection via accessibility name
- Result label assertion

**Temperature (exact, no rounding ambiguity):**

| From | To | Input | Expected |
|---|---|---|---|
| Celsius | Fahrenheit | `0` | `32` |
| Celsius | Fahrenheit | `100` | `212` |
| Celsius | Fahrenheit | `-40` | `-40` |
| Fahrenheit | Celsius | `32` | `0` |

**Length (one additional converter to validate shared UI pattern):**

| From | To | Input | Expected |
|---|---|---|---|
| Kilometers | Meters | `1` | `1000` |
| Meters | Centimeters | `1` | `100` |

---

## Summary

| Phase | Mode | New scenarios | Infrastructure needed | Effort |
|---|---|---|---|---|
| P1 | Standard | ~9 | None | Low |
| P2 | Scientific | ~22 | Mode-switch step, scientific step defs | Medium |
| P3 | Programmer | ~11 | Radix-switch step, hex aliases, bitwise step defs | Medium |
| P4 | Date Calculation | ~5 | Date picker navigation steps | High |
| P5 | Converters | ~6 | Combobox selection step, result assertion step | Medium |
| **Total** | | **~53** | | |
