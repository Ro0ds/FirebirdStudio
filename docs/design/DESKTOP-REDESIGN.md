# Desktop identity and usability plan

This proposal starts from v0.1.4. The goal is a compact database workspace where the active connection, SQL editor, and result of an action are easy to identify at a glance. Each milestone is delivered through small PRs to `development`. The [integration and release workflow](../releasing/DEVELOPMENT.md) describes how those changes are tested and promoted.

## Current interface

- Light and dark modes use the Avalonia Fluent theme, with some colors set directly in the views and SQL colorizer. SQL selection in light mode has been reported as unreadable. Check the same states in the editable SQL tabs and the read-only DDL editors.
- Database Connections has persisted Detailed and Minimal modes. Database Objects combines a tree, filtering, refresh, and category ordering. Rows sometimes repeat their category or expose long metadata strings in limited space.
- Query History appears in a right dock or an editor tab. Its entries fit SQL previews, time, connection, and outcome into a small area.
- Messages displays individual events with severity colors and diagnostic details. Results presents a read-only grid. The lower panel has room to improve the hierarchy of state, summary, and details.
- The interface has English text, inconsistent type sizes, and stock controls. The application has no dedicated window or executable icon.

## Proposed themes

Use neutral backgrounds, an ember accent for the product and primary actions, and a blue keyboard-focus indicator. Treat these colors as starting points for side-by-side review in the editor and grid.

| Role | Light | Dark |
|---|---|---|
| Workspace | `#F7F8FA` | `#141920` |
| Raised surface | `#FFFFFF` | `#1C232D` |
| Primary text | `#182230` | `#EBEEF2` |
| Secondary text | `#526173` | `#AAB7C6` |
| Accent | `#A43E1B` | `#FF9D72` |
| Selected text background | `#FCE2D1` | `#573426` |
| Selected text foreground | `#2D1B14` | `#FFF1E8` |
| Focus indicator | `#245AA7` | `#83B5FF` |

Create named resources for surfaces, borders, text, focus, selection, and severity. Keep SQL syntax colors tied to the same theme. Define foreground and background pairs for normal and selected text, including selections that retain the highlight after focus moves to another control. Check the final rendered selection with keywords, identifiers, strings, and comments.

Use minimum contrast targets of 4.5:1 for normal-size text and 3:1 for essential control indicators against adjacent colors. The proposed selection text pairs measure 13.25:1 in light mode and 9.87:1 in dark mode before rendering. Add a visible boundary or marker wherever a selected state depends on a low-contrast fill. [W3C text contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) · [W3C non-text contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html)

Start manual customization with accent and editor selection colors for each theme. Apply overrides on top of the built-in palette, pair foreground with background, and validate contrast before saving. Provide a preview and a reset action. Read existing settings with the stored theme, connection mode, and category order. Use default colors when overrides are absent or invalid.

## Proposed workspace

```text
Menu and query actions                    Active connection
┌──────────────────┬──────────────────────────┬────────────────┐
│ Connections      │ SQL and inspector tabs    │ History        │
│                  │                          │ Optional dock  │
│ Database Objects │ Editor / object details   │                │
│                  ├──────────────────────────┤                │
│                  │ Results | Messages        │                │
└──────────────────┴──────────────────────────┴────────────────┘
Status and feedback
```

| Area | Proposed behavior |
|---|---|
| Connections | Keep Detailed and Minimal. In both modes show the alias, the connection state in text or an accessible icon, and the current selection. Show host and database details in Detailed mode. Keep common actions discoverable by keyboard and pointer. |
| Database Objects | Group under the active connection. Use one row per node with a type icon, one primary label, and optional secondary detail. Put search, type filtering, and refresh in one header. Keep category order, expansion, incremental loading, and object navigation. |
| SQL workspace | Keep the editor as the largest surface and distinguish SQL tabs from inspectors through names and icons. Show the current connection next to query actions. Keep Execute and Cancel discoverable and preserve shortcuts. Allow the result area to expand without forcing the editor out of view. |
| Query History | Use a short first line for the query and a second line for time, connection, and outcome. Provide a preview and a keyboard-accessible restore action. Keep dock and tab placements and allow the dock to collapse. Consider search and outcome filtering in a separate PR. |
| Results | Put row count, elapsed time when available, and truncation status in a compact header. Give the grid the remaining space. Distinguish waiting, running, zero rows, cancellation, and successful results. Check long values and column resizing. |
| Messages | Use a scan-friendly list with timestamp, severity icon and label, and summary. Show diagnostic text in an expandable or separate detail area with copy support. Keep the navigation to Messages on errors and return to Results after a successful query when Messages was selected. |
| Status | Prioritize the active connection, ongoing work, and actionable feedback. Move build and dependency details to About if the user can still find them easily. |

### Text, controls, and application icon

- Prototype a typography scale of 12 for secondary labels, 13–14 for interface and grid text, and 16 for section titles, in Avalonia device-independent units. Start SQL editors at 14 with independent zoom. Check layout at 100%, 125%, 150%, and 200% Windows scaling.
- Give primary actions such as Execute a verb and an icon. Use secondary styles for routine actions. Give icon-only controls an accessible name, a tooltip, and a keyboard focus indicator. Review disabled states and use an explicit label for destructive operations.
- Prototype a simple ember or flame with a database silhouette, legible at 16 and 24 pixels. Keep the source asset and verify the window, taskbar, and executable icons in the published Windows package. Derive other platform assets as their packaging is introduced.
- Extract UI strings into localization resources. English is the default interface language. Use Brazilian Portuguese as the fallback for missing translations. Let users choose English or Brazilian Portuguese in **Settings** and restore that preference on startup. Format dates and times for the selected locale, allow for longer labels, and preserve SQL, identifiers, and provider diagnostics.

The desktop language resources live in `Localization/Interface.resx` (Brazilian Portuguese fallback) and `Localization/Interface.en.resx` (English). Keep the format placeholders aligned when editing both files. The structural verifier checks that each referenced key has a fallback.

## Milestones and PRs

| Milestone | PRs | Ready when |
|---|---|---|
| 1. Baseline and readability | Add PR validation and a stable-release branch guard in separate work from the selection fix. Fix foreground/background selection in editable SQL and read-only DDL first. | Selection stays readable in both themes, during and after focus changes. The PR check runs build and tests without publishing packages. |
| 2. Shared visual foundation | Introduce theme resources, typography and focus styles, then shared buttons and icons. Deliver the application icon in its own PR. | Menus, dialogs, editors, and grids use the shared styles. Text and focus indicators meet the planned contrast targets. The packaged app displays the icon. |
| 3. Connections and objects | Update connection rows and navigation in one PR. Update the object tree, search header, and object row templates in a second PR. | Both connection modes, keyboard navigation, category ordering, search, virtualized paging, and inspector navigation still work. |
| 4. Query workspace and output | Deliver Query History, Results, and Messages in separate PRs. Integrate the layout once the panels work individually. | Docking and history restore still work. Running, empty, error, truncated, and canceled states are distinguishable. Output and diagnostics can be read and copied. |
| 5. Preferences and language | Add theme color overrides and reset. Deliver the language selector in Settings and localized resources in a separate PR. | Existing settings load. English is the default, the user's language choice persists, and missing translations use Brazilian Portuguese. Primary actions fit in both locales. |
| 6. Release candidate | Run the full smoke checklist on `development`, then open the release PR. | Both Windows package variants launch and retain existing user settings. The release PR contains a tested version and a summary of user-facing changes. |

Before merging each UI PR, attach focused before/after screenshots in light and dark when visual behavior changes. Test keyboard focus, the minimum usable window size, and the affected saved preferences. For shared layout changes, also exercise connection switching, editor recovery, metadata paging, query execution and cancellation, history restore, and result/message clearing. Use disposable Firebird databases for scenarios that need a server.
