# Design System — STIGForge

## Product Context

- **What this is:** Offline-first Windows STIG hardening platform — Import, Build, Apply, Verify, Prove.
- **Who it's for:** DoD sysadmins, security compliance officers, and ISSO staff managing classified Windows environments.
- **Space / industry:** Government security compliance / DISA STIG automation / CyberCom-adjacent tooling.
- **Project type:** WPF desktop application (Windows 10/11 only). Primary UI is a tabbed dashboard with workflow wizard mode.

---

## Aesthetic Direction

- **Direction:** Industrial / Precision
- **Decoration level:** Minimal — typography, negative space, and semantic color carry all the weight.
  No decorative gradients, no icon-in-circle grids, no blobs.
- **Mood:** Control-room energy. A Bloomberg terminal crossed with a modern security console.
  Authoritative, data-precise, and trustworthy. Every visual decision signals that this tool
  is serious and accurate — not polished for its own sake.
- **What this is NOT:** Cold and clinical government-IT aesthetic. Consumer-playful. Generic SaaS dashboard.

---

## Typography

**Font stack (Windows system fonts — no embedding required):**

```
AppFontFamily: Aptos, Segoe UI, Calibri
MonoFamily:    Cascadia Code, Cascadia Mono, Consolas
```

Aptos is Windows 11's default Office font since 2024 — modern, neutral, highly legible at
13–15px. Segoe UI covers Windows 10. Cascadia Code is Microsoft's open-source monospace
designed for developer consoles; Consolas is the fallback.

**Type scale (8 levels — define all sizes from this table, no ad hoc overrides):**

| Token        | Size  | Weight    | Usage                                  |
|--------------|-------|-----------|----------------------------------------|
| `caption`    | 11px  | 400 / 600 | Meta labels, timestamps, table headers |
| `body`       | 13px  | 400       | Default text, list items, descriptions |
| `body-md`    | 14px  | 400       | Slightly elevated body, status text    |
| `label`      | 13px  | 600       | Form labels, nav items, tab headers    |
| `subhead`    | 15px  | 600       | Card headers, section titles           |
| `title`      | 18px  | 600       | Panel titles, dialog headers           |
| `page-title` | 22px  | 600       | Tab page headings (`PageTitleStyle`)   |
| `hero`       | 28px  | 700       | App header, splash, major callouts     |

Caption table headers: uppercase + letter-spacing 0.5px.
Hero / page-title: letter-spacing −0.3px to −0.5px for tighter display.

---

## Color

### Approach: Restrained — color is earned, not decorative

Accent is the DoD cobalt blue. Status colors are desaturated and serious — not generic
Tailwind UI-kit colors. This distinguishes STIGForge from consumer dashboards.

### Light Mode

| Token               | Hex       | Usage                                          |
|---------------------|-----------|------------------------------------------------|
| `WindowBackground`  | `#F5F7FA` | App window background                          |
| `Surface`           | `#FFFFFF` | Cards, panels, dialogs                         |
| `SurfaceMuted`      | `#F1F4F8` | Tab backgrounds, grid headers, alternating rows|
| `Border`            | `#D5DEE9` | All dividers and element borders               |
| `TextPrimary`       | `#0E1C2E` | Default text — deep navy ink                   |
| `TextMuted`         | `#52647A` | Secondary text, hints, timestamps              |
| `Accent`            | `#0A5AA8` | Primary interactive color (DoD blue)           |
| `AccentSoft`        | `#DBEAFB` | Accent fills: hover states, selection bg       |

### Dark Mode

| Token               | Hex       | Usage                                          |
|---------------------|-----------|------------------------------------------------|
| `WindowBackground`  | `#0C0E14` | App window background (warm-tinted near-black) |
| `Surface`           | `#131720` | Cards, panels, dialogs                         |
| `SurfaceMuted`      | `#0E1118` | Tab backgrounds, headers                       |
| `Border`            | `#252A38` | All dividers                                   |
| `TextPrimary`       | `#F0F4FA` | Default text — slightly cool white (not stark) |
| `TextMuted`         | `#8B9AB5` | Secondary text                                 |
| `Accent`            | `#2563EB` | Primary interactive (more distinctive than blue-500) |
| `AccentSoft`        | `#1E3A8A` | Accent fills in dark context                   |

**Dark mode note:** The `#0C0E14` base uses a subtle warm desaturation that separates it
from the standard VS Code / IDE blue-black. This is intentional — it positions STIGForge
as a precision console rather than a cloned developer theme.

### Semantic / Status Colors

These reflect a **security-operations palette** — not generic web-app colors.
Muted and serious. They communicate gravity.

| Token     | Light Hex   | Dark Hex    | Meaning                              |
|-----------|-------------|-------------|--------------------------------------|
| `Pass`    | `#1F7A4F`   | `#22C577`   | Control passing, mission success     |
| `PassSoft`| `#D3F0E3`   | `#063A21`   | Pass backgrounds                     |
| `Warning` | `#C07000`   | `#F0A020`   | Manual review required, near-failure |
| `WarnSoft`| `#FEF0CD`   | `#3D2600`   | Warning backgrounds                  |
| `Danger`  | `#B81C0E`   | `#F04040`   | Failure, rollback, error             |
| `DangrSft`| `#FDDDD9`   | `#3D0C0C`   | Danger backgrounds                   |

**Workflow step colors** (map to semantic palette):

| State     | Light              | Dark               |
|-----------|--------------------|--------------------|
| Ready     | `#3B82F6`          | `#3B82F6`          |
| Running   | `#C07000` (Warning)| `#F0A020`          |
| Complete  | `#1F7A4F` (Pass)   | `#22C577`          |
| Locked    | `#6B7280`          | `#475569`          |
| Error     | `#B81C0E` (Danger) | `#F04040`          |

---

## Spacing

- **Base unit:** 4px
- **Rhythm:** 8px — most spacing values are multiples of 8
- **Density:** Comfortable-to-compact (data-dense tool, not marketing site)

| Token  | Value | Usage                                                   |
|--------|-------|---------------------------------------------------------|
| `2xs`  | 2px   | Tight icon margins, indicator dots                      |
| `xs`   | 4px   | Gap between badge elements, small internal padding      |
| `sm`   | 8px   | Gap between related elements, small section breaks      |
| `md`   | 16px  | Standard section spacing, form field margins            |
| `lg`   | 20px  | App page padding (`AppPagePadding`)                     |
| `xl`   | 24px  | Generous section spacing                                |
| `2xl`  | 32px  | Major section breaks                                    |
| `3xl`  | 48px  | Between top-level page sections                         |

**Existing WPF tokens (keep):**
- `AppPagePadding` = 20 (Thickness)
- `CardPadding` = 14 (Thickness)
- `SectionSpacing` = 0,0,0,16 (Thickness)

---

## Layout

- **Approach:** Grid-disciplined — consistent column alignment throughout
- **Composition:** Tab-based dashboard (13 tabs). Wizard mode is linear step-through.
- **Window minimum:** 800×500 (existing, keep)
- **Border radius scale:**

| Token        | Value  | Usage                                           |
|--------------|--------|-------------------------------------------------|
| `sm` / micro | 4px    | Tooltips, badge backgrounds                     |
| `md`         | 8px    | Buttons (`UiButtonRadius`), inputs, dropdowns   |
| `lg`         | 12px   | Panels, alerts, section containers (`UiPanelRadius`) |
| `xl`         | 16px   | Cards, dialogs (`UiCardRadius`)                 |
| `full`       | 9999px | Badge pills, status dots, toggle knobs          |

---

## Motion

- **Approach:** Minimal-functional — never laggy, never decorative during a hardening mission
- **Easing:** enter: `ease-out` / exit: `ease-in` / move: `ease-in-out`
- **Duration guide:**

| Category     | Duration   | Usage                                                 |
|--------------|------------|-------------------------------------------------------|
| Micro        | 50–100ms   | Button press feedback, tooltip appear                 |
| Short        | 120–180ms  | Tab switch, panel slide-in                            |
| Medium       | 200–280ms  | Dialog open/close, step state transition              |
| Long         | 350–500ms  | Major state change (mission complete), route change   |

**Rule:** No animation should ever block or distract during an active hardening or SCAP run.
Progress indicators (indeterminate `ProgressBar`) are always acceptable.

---

## Component Conventions

### Data tables
- Header: `caption` + uppercase + letter-spacing 0.5px + `SurfaceMuted` background
- Numeric columns: `font-variant-numeric: tabular-nums`
- Row hover: `SurfaceMuted` background
- Selected: `AccentSoft` background

### Status badges
- Pill shape (`border-radius: full`), bold caption text, uppercase
- Background: soft variant of status color (Pass/Warn/Danger/Info)
- Foreground: full-saturation status color

### Buttons
- Primary: `Accent` background, white text
- Secondary: `SurfaceMuted` background, `Border` border, hover → `AccentSoft`
- Ghost: transparent background, `Accent` text, hover → `AccentSoft`
- Danger: `DangrSft` background, `Danger` border + text
- Disabled: `ButtonDisabledBg`, `ButtonDisabledFg`, `ButtonDisabledBorder`

### Alerts / banners
- Left border accent (3px) in semantic color
- Background: soft semantic color
- Icon + message in flex row

---

## High Contrast Theme

A `HighContrastTheme.xaml` exists and must remain fully maintained. Any new color tokens
added to `LightTheme.xaml` and `DarkTheme.xaml` must also be added to `HighContrastTheme.xaml`
using Windows system high-contrast color resources (`SystemColors.*`).

---

## Decisions Log

| Date       | Decision                                            | Rationale                                                                 |
|------------|-----------------------------------------------------|---------------------------------------------------------------------------|
| 2026-03-23 | Initial design system documented                    | Created by /design-consultation. Codified existing XAML tokens + refined. |
| 2026-03-23 | Status colors shifted to security-ops palette       | Desaturated olive/amber/deep-red vs generic Tailwind — signals gravity.   |
| 2026-03-23 | Dark mode base shifted to `#0C0E14` (warm-tinted)   | Separates from VS Code clone aesthetic; precision instrument feel.        |
| 2026-03-23 | 8-level type scale formalized                       | Ad hoc sizes scattered across XAML → documented scale for consistency.    |
| 2026-03-23 | Light mode text deepened to `#0E1C2E`               | Ink-like navy vs standard dark-gray; slightly more authoritative.         |
| 2026-03-23 | Dark accent refined to `#2563EB`                    | More distinctive than generic blue-500 (#3B82F6).                        |
| 2026-03-23 | Font stack kept as Aptos/Segoe UI                   | Correct for Windows app — no font embedding needed; Aptos is Win11 default. |
