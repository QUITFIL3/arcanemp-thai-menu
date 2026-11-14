# ArcaneThai desktop GUI

Mode: Operate. Code-led native interface. The user pinned the one-folder, component-selection workflow. The established ArcaneMP preview images provide the dark-and-gold visual reference; no bitmap mockup replaces the working application.

## Composition
A restrained left navigation rail and one working pane. The primary page shows one folder field, a two-row component checklist with real individual status, an inline result, and install/remove actions. Direct-game installation, image previews, and help are secondary pages. Keep native Windows file pickers, window controls, keyboard navigation, and resizing.

## Tokens
| Role | Value |
|---|---|
| Canvas | #18262B |
| Rail / input | #101A1F |
| Surface | #203137 |
| Border | #3C5055 |
| Text | #ECF1EA |
| Secondary text | #AFBEB9 |
| Gold / action | #D6B477 |
| Success | #9DCEAD |
| Error | #F1ADA6 |

Use Leelawadee UI for Thai controls and Georgia only for the English wordmark. Use 8–30 px spacing, 10.5 pt body text, 22 pt page titles, and visible keyboard focus. Native DPI scaling applies. The minimum window is 920×620 client pixels; the main task must remain usable at that size.

## Behavior
Folder inspection is debounced and stale results are ignored. Each component retains an independent status. Selecting both applies the existing engines sequentially with a clear partial-success report if the second operation fails. Detect the selected installation's running game or launcher and enable write actions once it closes. Never invent progress percentages. Keep read-only browsing available while a game runs.

## Verification
Render empty, ready, installed, error, direct-game, gallery, and minimum-size states. Exercise the actual GUI events against temporary copies, including single-component install/remove, combined selection, invalid paths, and navigation. Backend invariants retain their existing checks.
