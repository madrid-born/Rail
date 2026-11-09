# Rail design pack

Open [START-HERE.html](START-HERE.html), then launch the interactive prototype. Everything runs locally in Edge or Chrome.

Rail is a Persian-first companion for the Tehran Metro. The proposed experience starts with a station pair and gives riders direct access to line diagrams, departures and notices. Guest access covers trip planning. The account flow starts when a rider chooses to save a station.

| Folder | Contents |
| --- | --- |
| `00-assets` | Copies of the approved Persian Teal app icon and ivory Rail mark |
| `01-prototype` | Clickable RTL app, desktop layout switch and gallery |
| `02-screens` | 20 standard and two dark mobile PNGs, mobile JPGs, and five desktop PNGs |
| `03-design-system` | Brand and semantic tokens, component reference and rendered component sheet |
| `04-handoff` | MAUI and API integration notes, including production dependencies and browser verification |
| `05-overview` | Design studio capture, visual contact sheets and user-flow diagram |
| `06-tools` | Reproducible browser checks, screenshot and overview assembly scripts |

## Try the prototype

- Start at **Journey planner**. The prefilled trip from Tajrish to Farhangsara shows a sample transfer at Imam Khomeini. Change the destination to Imam Khomeini for the direct example. Swap the pair for the no-result example.
- Open **Network map**, pick a line and select an interchange. Open its departures and filter by line before choosing a trip.
- Save a station to try the account gate. The sample account lets you add and remove favorites during the preview. Use the screen index to inspect the empty state.
- Use **Dark** or **Desktop** in the studio toolbar, or open the screen gallery to compare exports. **Reset** returns to the initial sample.

Station names, identifiers, line memberships and colors use a subset of the imported Rail catalog. Travel times, notices and login are sample interactions. The diagram uses schematic positions and covers part of the network. The prototype is for review and MAUI handoff. Use an authoritative timetable and operational information for rider guidance.

## Regenerate after editing

Run the browser script with Node.js and Playwright available. It uses Edge on Windows if found. Set `RAIL_BROWSER` to an alternate Chromium executable when needed. The assembly script requires Python and Pillow. It creates the mobile JPGs and overview sheets from the manifest.

```powershell
node Rail.Statics/Design/06-tools/render-and-check.cjs
python Rail.Statics/Design/06-tools/assemble.py
```

When using Codex’s bundled dependencies, set `NODE_PATH` to the bundled Node modules and run the second command with the bundled Python. Open [verification.json](04-handoff/verification.json) for the check scope and results.

