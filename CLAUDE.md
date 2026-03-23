# STIGForge — AI Agent Instructions

## Design System
Always read `DESIGN.md` before making any visual or UI decisions.
All font choices, colors, spacing, border radii, and aesthetic direction are defined there.
Do not deviate without explicit user approval.
In QA or review mode, flag any code that doesn't match `DESIGN.md`.

Key rules:
- All new color tokens must be added to **all three** theme files:
  `Themes/LightTheme.xaml`, `Themes/DarkTheme.xaml`, `Themes/HighContrastTheme.xaml`
- Font sizes must use the 8-level type scale (11/13/14/15/18/22/28px). No ad hoc values.
- Spacing values must be multiples of 4px. Prefer multiples of 8px.
- Border radii: button=8, input=8, panel=12, card=16, badge/pill=9999.
