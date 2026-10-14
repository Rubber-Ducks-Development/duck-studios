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
