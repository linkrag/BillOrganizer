# Changelog - Colony Organizer

Versioning: SemVer. 1.0.0 = first Steam Workshop publication (2026-11-07). MINOR = new feature, PATCH = fixes/tweaks. Before each Workshop upload, bump `<modVersion>` in `About/About.xml`, this file and the Changelog section of `Docs/WORKSHOP_DESCRIPTION.txt` together (player-facing text, English).

## 1.3.1 (2026-11-15)
- Fixed: operations (e.g. peg leg) added from a colonist's Operations tab opened the recipe window and made a bill without a body part: the doctor used up the ingredients but no surgery happened.
- Operations now always use the vanilla menu, where you pick the body part (left/right leg...). Colonists, animals and mechs.
- Broken operation bills in existing saves are removed on load, with a message.

## 1.3.0 (2026-11-13)
- New Work task force: raise work types on many colonists for a set time, then priorities return automatically.
- Priorities you change by hand are kept; end it early or take out a single colonist. Saved with the game.
- Optional "interrupt current work" switches colonists over at once, never cutting rescues, treatment or firefighting.
- New Babies column in auto slaughter: babies are protected by default and left out of the limits.
- Until X now steps by what one craft makes (5 for x5 shells), also in vanilla and other mods' bill lists.
- Bill targets snap to whole batches, including new bills, saved bills and Max.
- Task force, baby column and batch stepping can each be turned off in mod settings.

## 1.2.1 (2026-11-12)
- Compact tab has its own paste button next to Actions and a header with a close button, no longer overlapping.
- Locked or removed recipes never show up; clicking such a bill just shows a message.
- Bonus columns now follow the current search and filters.
- Compare panel names are no longer cut short; items without armor no longer show zero armor rows.

## 1.2.0 (2026-11-11)
- New Bonuses and effects: stats when equipped (e.g. mechanitor bandwidth), abilities, shields, implants, mech and drug effects.
- Bonuses appear in details, compare, a list marker and sortable columns; search them with + (e.g. +bandwidth).
- Presets now set the value on first click and add it on repeat clicks; +/- moves one unit.
- Fixed Ctrl/Shift tooltips that had x10 and x100 swapped; they now show your own game keys.
- Queue card +/- moves by whole batches in Until X; minus no longer revives a finished bill.
- Clicking a queue card now opens its recipe more reliably; Until X can no longer go below one batch.

## 1.1.0 (2026-11-10)
- New Copy queue in the queue Actions menu: copy a whole bench queue and paste it on another bench.
- Quantity presets are now multiples (1x, 5x, 25x, 50x) of one repeat, or of the recipe step for Until X.
- Switching between Do X times and Until X now resets the amount instead of carrying it over.
- Fixed mod settings disappearing after toggling the resource list or compact tab.

## 1.0.0 (2026-11-07)
- Searchable recipe window replaces the Add bill menu, with recipes grouped in a tree (weapons, ammo by caliber, apparel by body part, and more).
- Sortable stat columns with bars, a detail panel with ingredients and armor per material, and side-by-side compare.
- Can make N: batches your stockpile allows, the limiting ingredient and a Max button.
- Compact bill tab with a card-style queue: drag to reorder, pause, copy, change material, batch actions.
- Queue templates saved across saves, linked benches sharing one queue, and bench priority in the Bills window.
- Organized resource list with search and sorting; Ctrl+click pins a resource with stock alerts.
- Combat Extended support (optional); a Recipes button on every bench keeps it working with Nice Bill Tab and Dubs Mint Menus.
