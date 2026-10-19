# Duck Studio 2.1 reference lock

Primary reference: the existing Duck Studio editor, the user's charcoal Build/Error List and texture screenshots, and the Studio 2 reference lock. Preserve Segoe UI, native menus, dense lists, charcoal surfaces, explicit labels and the supplied duck logo.

Secondary guidance: Refero craft-details for keyboard focus, form labels, visible asynchronous progress and reversible actions. The UV selection interaction follows the user's request to locate surfaces; the composition keeps the atlas and 3D model adjacent. Outfit editing uses a scrollable component inspector beside the full-height model. Batch editing keeps selection beside explicit operations and has a final Apply/Cancel commit. Screenshot output uses the real model, a fixed light setup and readable contact sheet captions.

| Decision | Source | Rule / role | Purpose |
| --- | --- | --- | --- |
| Charcoal, compact controls, Segoe UI | Existing editor and user screenshots | Existing product system | Keep new tools familiar |
| UV atlas beside model | User feature request | Direct manipulation | Map a clicked face to its UV triangle |
| Scrollable outfit slots beside model | Existing tattoo tool and craft forms | Inspector fields | Keep preview visible while combining layers |
| Apply/Cancel for batch/LOD changes | Craft navigation/state | Reversible draft commit | Review before changing project |
| Explicit animation approximation labels | Actual renderer implementation | Inform a meaningful choice | Direct exact checks to the game companion |
| Visible progress and cancel | Craft async updates | Operation feedback | Keep large screenshot exports controllable |
| Focus border, dim disabled buttons | Craft focus states | Keyboard affordance | Make keyboard actions and unavailable operations legible |

Verification: offscreen WPF rendering of all six tools, real GTA outfit scene, transparent front/back images, UV coordinates compared with renderer output, and every fitting pose visually reviewed. Empty/unloaded previews show progress or an actionable error. Keep exports outside UI chrome.

## 2.1.4 and 2.1.5 interaction repairs

Reference lock: preserve the existing editor's charcoal surfaces, compact Segoe UI controls, light strokes and native preview palette. The user's bug report owns behavior; Refero motion and icon craft own restraint and readable contrast. No new visual direction is introduced.

| Decision | Source | Rule / role | Purpose |
| --- | --- | --- | --- |
| Stable tab headers and color-only button presses | User report; motion craft | Feedback must not displace frequently used controls | Keep click targets stationary |
| Transparent collapse headers with light vector strokes | Existing charcoal system; icon craft | Functional icons inherit foreground and remain legible on dark surfaces | Remove white arrow backgrounds |
| One clothing category with male rows before female rows | User clarification | Retain the clothing dropdown, without gender dropdowns | Keep the list ordered and compact |
| Next position for each gender and clothing type | User clarification | Append imports and copies after existing positions | Keep additions at the bottom of their gender's rows |
| Double-click a texture to preview its selected variation | User report | Preserve the user's selected object across views | Inspect H without falling back to A |

Validation: programmatic tab-position comparisons, rendered library and preview panels, real YDD with contrasting A/H YTD materials, and persisted orbit-inversion settings.

## 2.1.6 new-item indicator

Reference: the user's clothing-row screenshot and follow-up request for a short automatic fade. Keep the existing row layout and icons; use light yellow (#FFF2B3) only for newly added clothing, with dark foreground (#292611) for readable labels and slot icons. Hold for two seconds, then fade the tint and foreground back over 0.7 seconds, using each addition's elapsed time so refreshes cannot restart the effect. This is transient UI feedback, without changes to saved project data. Verify selected and unselected rows, the hold interval, fade midpoint and automatic completion.

## 2.1.7 additions and compact actions

Reference: the user's clipped toolbar screenshot, existing charcoal controls and Refero icon/craft guidance. Preserve full action labels, consistent outline icons and stable button feedback. Use 16px toolbar icons and compact padding at the standard pane width; wrap actions at narrower widths, retaining their full hit areas. The pants icon remains in the existing 24px outline family, with distinct legs, waistband and pockets. Batch additions reveal only their final row after layout, without animated intermediate scrolling. Verify complete button bounds at 500, 360 and 260px pane widths and a batch with 60 additions.
