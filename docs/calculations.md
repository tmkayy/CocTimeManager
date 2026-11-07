# How the maths works

- Percentage reductions stack **multiplicatively**: Gold Pass 20 % + Hammer Jam 50 % → `0.8 × 0.5 = 0.4` of the time → **60 %** off.
- The Builder Potion is applied after that: every real hour of potion does 10 hours of work, so 1 potion saves 9 hours
  (less if the upgrade finishes before the potion runs out; the breakdown shows any wasted potion time).
- Builder Bites (2×) **don't stack with a Builder Potion**: since the February 2026 update, a bite adds nothing
  while a potion is active. So bites are counted as used right after the potion ends (what the game recommends):
  10× during the potion, then 2× during the bites, then normal speed.

Example: 24 h upgrade + 1 potion + 1 bite → 10 h of work in the potion hour, 2 h in the bite hour, 12 h left → **14 h**.

[Imports](importing.md) also handle Research Potions and helpers.
