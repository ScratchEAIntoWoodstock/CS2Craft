# CS2Craft

**Experimental local Minecraft-in-CS2 prototype. Not a finished SkyCraft-equivalent release.**

This Windows project runs real Minecraft gameplay inside Counter-Strike 2. It reuses the Minecraft/Fabric half of [SkyCraft v0.1.0](https://github.com/chasmlol/SkyCraft/releases/tag/v0.1.0), with a new CounterStrikeSharp host and Direct3D 11 renderer.

A real Dust II client has now shown Minecraft blocks, held items, a shield, hotbar, and inventory. The live test moved the CS2 pawn using Minecraft physics, hit a CS2 bot with a diamond sword (100 → 65 health), moved the sword between inventory slots, and restored normal CS2 movement. See [verification](docs/VERIFICATION.md).

## Install on another machine

See [docs/INSTALL.md](docs/INSTALL.md). In short: Steam with CS2, the .NET 10 SDK, Visual Studio 2022 with C++, Java 25 and about 60 GB free. Double-click `Setup.cmd` (or run `.\tools\Install-Prerequisites.ps1`): it installs whatever is missing from the official vendors (asking first), then offers to build and prepare. `.\tools\Check-Prerequisites.ps1` only lists what is missing.

## Run on this machine

The private CS2 runtime and compiled plugin are prepared in this project.

```powershell
.\tools\Play.ps1
```

In CS2, join a team, open the console with **~**, and enter:

```text
css_cs2craft_play
```

This explicitly starts experimental gameplay once Minecraft and nearby collision are ready. Allow roughly a minute for the development client to load. Minecraft runs hidden, uses its own development save, and saves/quits after CS2 closes. The launcher selects `CS2Craft-de_dust2`, separate from the automated test saves.

Useful commands:

| Command | Action |
|---|---|
| `css_cs2craft_play` | Attach and start experimental Minecraft gameplay when ready |
| `css_cs2craft_status` | Connection, movement, render frames, sections and rejected packets |
| `css_cs2craft_detach` | Restore CS2 movement, input and presentation |
| `css_cs2craft_attach` | Observe/mirror the pawn without enabling Minecraft movement |
| `css_cs2craft_drive` | Developer movement toggle; requires config opt-in |

Controls: WASD, mouse look, left-click attack/mine, right-click use/place, 1–9 or wheel for hotbar, E for inventory, Q to drop, F to swap hands, Space to jump, Shift to sneak. Moving forward sprints automatically, like running in CS2 (`AutoSprint`; Ctrl also sprints). Minecraft still needs more than three drumsticks of food to sprint. F5 cycles first person, behind Steve, and in front of Steve. Steve wears your equipped Minecraft armor.

### Buying, rounds and the default kit

Gear works like CS2. Every life starts with the default kit: a stone sword, a bow with 4 arrows, 16 oak planks and 4 cooked beef. Press **B** for Minecraft's buy menu (CS2's own buy menu is kept closed while you play). Items are paid with your **CS2 money**, under CS2's rules: only in a buy zone, only during buy time (`mp_buytime`), and kills on bots earn CS2 kill rewards ($600 melee or TNT, $300 bow, crossbow or trident; also counted on the scoreboard). `/cs2buy <name>` in Minecraft chat buys without the menu, for example `/cs2buy Iron Armor`.

Every round starts you over from the default kit, so a bad buy never carries over (set `KeepGearBetweenRounds` to true for CS2's rule, where survivors keep what they bought). Dying also resets you to the default kit. Every round start heals you fully and clears effects; placed blocks, dropped items and arrows are removed. Natural health regeneration is off, as in CS2: heal with potions and golden apples. `/cs2craft loadout` makes your current inventory the default kit for the session (sandbox building); `/cs2craft loadout reset` undoes it.

While you play, the Minecraft HUD shows what CS2's hidden HUD would: money (bottom left), the round clock (top) and a buy reminder during buy time. Every hit on a bot shows in the action bar with a hit sound ("Oli 70 HP (-30)" or "Killed Oli"). Advancement and recipe pop-ups are suppressed.

| Category | Offers (CS2 $) |
|---|---|
| Melee | Stone sword 100, iron sword 400, diamond sword 1000, netherite sword 2200, iron axe 650, diamond axe 1400, netherite axe 2600, mace 3000 |
| Ranged | Bow 300, 16 arrows 100 (carry 64), crossbow 900, trident 1800 |
| Armor | Full sets: leather 300, chainmail 650, iron 1000, diamond 2500, netherite 3500; shield 1000; totem of undying 4000 |
| Utility | Ender pearl 400 (carry 2), 2 TNT with flint and steel 500 (carry 6), splash healing II 400, golden apple 500, regeneration 700, strength 800, swiftness 300, leaping 200, slow falling 150, water bucket 200 |
| Blocks | 32 planks 100, 32 cobblestone 150, 16 glass 100, 8 obsidian 600, 16 ladders 100, 8 cooked beef 50 |

Balance notes. Bot gunfire is Minecraft damage (CS2 damage ÷ 5) that Minecraft armor reduces. Against an AK-47 body shot (36), iron takes 45% off (CS2 kevlar takes 22%), diamond 73% and netherite 74% with no knockback. A raised shield blocks bullets from the front but slows you. Ender pearls teleport you (and your CS2 player) and cost 25 HP. TNT plays CS2's own HE-grenade explosion and sound. Walls shield bots and you from it, and it does about 115 point blank, falling off with distance.

Weapon damage to bots is in CS2 health (bots have 100) for a full-strength, non-critical hit:

| Weapon | CS2 damage |
|---|---|
| Wooden / golden sword | 15 |
| Stone sword | 20 |
| Copper sword | 22 |
| Iron sword | 25 |
| Diamond sword | 30 (4 hits; 3 critical jump hits) |
| Netherite sword | 35 |
| Axes (slower swing) | 25–45 by material |
| Trident (melee / thrown) | 35 / 40 |
| Bow, full draw (always critical) | 36–66 (two arrows kill) |
| TNT | up to about 115, falls off with distance and cover |
| Fist / other items | 4 |

Minecraft's own rules still apply on top: attack charge (spam clicking deals less), critical jump hits ×1.5, Sharpness, Strength and Power. `DamageMultiplier` scales everything.

Config (`addons/counterstrikesharp/configs/plugins/CS2Craft.Server/CS2Craft.Server.json` in the runtime):

| Setting | Default | Effect |
|---|---|---|
| `RespawnDelaySeconds` | 3 | Respawn delay after a death; negative waits for CS2's own respawn |
| `ClearBlocksEachRound` | true | Round-start removal of placed blocks, dropped items and arrows |
| `AutoSprint` | true | Moving forward sprints |
| `BotBlockCollision` | true | Placed blocks get invisible CS2 colliders (bots, players, bullets) |
| `MaxBlockColliders` | 2048 | Collider limit |
| `DamageMultiplier` | 1 | Scales Minecraft damage dealt to bots |
| `Width` / `Height` | 0 / 0 | Minecraft HUD size; 0 follows CS2's window, so the HUD is crisp rather than stretched |
| `BuyMenuKey` | 66 (B) | Virtual key hidden from CS2 while playing, so only Minecraft's buy menu opens; 0 disables |
| `KillRewardMelee` / `KillRewardRanged` | 600 / 300 | CS2 money for a bot killed from Minecraft |
| `KeepGearBetweenRounds` | false | Every round starts from the default kit; true lets survivors keep bought gear, as in CS2 |
| `OccludePlayers` | true | Minecraft blocks behind CS2 players, bots and doors are hidden |

Mouse look now uses native relative input independently of the CS2 character camera. Client and integrated-server movement share exact triangle collision; hidden CS2 weapons are locked against firing. The inventory uses a virtual cursor while CS2 retains its mouse capture. Native mouse/text/wheel routing is implemented; physical desktop-input acceptance still needs manual testing. The automated inventory test verifies bridge cursor/click handling, not real mouse hardware.

To stop Minecraft control, use `css_cs2craft_detach` or close this CS2 session. Dying keeps you linked: you respawn after `RespawnDelaySeconds` (default 3) and gameplay resumes. Placed blocks are cleared at every round start (`ClearBlocksEachRound`). Pause/menu transitions are still rough. If a previous Minecraft instance holds the mapping, let it finish saving before relaunching.

## What works and what remains

| Component | Evidence / limitation |
|---|---|
| Minecraft 26.3 / Fabric fork | Builds; 26 geometry/coverage tests |
| Shared-memory bridge | 26 regression checks |
| Minecraft block placement and removal | Real client mesh stream; placed blocks rendered in live Dust II |
| Textures, sections, scene/avatar mesh backend | Implemented; GPU readback and real-packet replay tests |
| HUD, held items, shield and inventory inside CS2 | Captured from the actual CS2 backbuffer |
| Minecraft movement → CS2 pawn | Live probe moved 2.05 blocks; movement restoration verified |
| Diamond sword → real CS2 bot | Live probe reduced bot health from 100 to 65 |
| Inventory slot interaction | Live CS2 capture confirms sword moved out of hotbar into inventory |
| CS2 damage → Minecraft | Implemented; incoming bot-fire acceptance remains unverified |
| Map collision / wall occlusion | Exact triangles from the installed map physics, including baked props. Players, bots and doors occlude with approximate body/door boxes refreshed every tick; ragdolls and other moving props do not occlude |
| Exact camera / depth / lighting | Dedicated camera entity follows Minecraft eye height and F5; separate depth buffer uses map physics |
| Minecraft character in third person | Steve skin, animated body/equipment, F5 camera; underlying CS2 pawn remains for combat |
| Placed blocks blocking CS2 bots | Invisible 40-unit crate collider per solid block; not yet live-verified. Bots do not path around them: the nav mesh is baked, so they walk into builds and use their own stuck/jump handling |
| Multiplayer | **Not implemented**; one local human plus bots only |

Before launch, the map exporter reads your installed map's physics and caches its triangles locally. Initial collision loads around the player; movement is held at any unloaded boundary. Crouch clearance and sneak edge checks use map geometry. Block placement picks a grid cell outside the hit surface; fractional-height floors can leave a gap beneath a block.

The health bridge is provisional. Lethal bot hits use the suicide API, so kill attribution and full damage/knockback semantics are incomplete. Every Minecraft hit on a bot is logged in the CS2Craft plugin log with the bot's health before and after. CS2 weapon attacks are held while Minecraft owns movement. CS2's HUD and viewmodel are hidden while the linked Minecraft world is active, then restored.

Blocks receive approximate Minecraft lighting and no CS2 shadows. Transparency is sorted at mesh/batch level. Fast camera motion, FOV transitions and dynamic geometry need further work. Do not treat this as a polished release.

## Build / prepare another checkout

Requirements: Windows x64, .NET 10 SDK, Java 25, Visual Studio 2022 C++ tools and CMake. Scripts find Java 25 from the Minecraft Launcher, `JAVA_HOME` or an Eclipse Temurin install (`tools/lib/Prerequisites.ps1`); `-JavaHome` selects another.

```powershell
.\tools\Build.ps1
.\tools\Prepare-CS2.ps1
```

Preparation creates a **private copy of CS2, approximately 53 GB**, in `runtime/`. It copies installed game data, required prefabs, Dust II and executables. It uses no hardlinks or directory junctions and does not patch the Steam installation. Do not redistribute copied game data, Minecraft downloads or saves.

Metamod 2.0.0.1474 and CounterStrikeSharp 1.0.376 are pinned and SHA-256 checked. CS2 updates can require revalidation. All launches use the private runtime with `-insecure -dx11 +sv_lan 1`.

The development Minecraft instance uses a development identity. An authenticated, user-facing Minecraft Launcher profile installer is not implemented. The project does not read Minecraft credentials. The internal upstream `skycraft` mod ID and Java packages are retained; do not install this alongside original SkyCraft.

## Developer validation

Dedicated server startup:

```powershell
.\tools\Start-CS2.ps1 -SmokeTest
```

Live client probe (starts a local team, places blocks, moves, attacks a bot and transfers an inventory item):

```powershell
.\tools\Start-CS2.ps1 -ClientProbe
.\tools\Start-Minecraft-Dev.ps1 -WorldName CS2Craft-MyTest
```

Read `client-probe.json` and `client-gameplay.json` in `runtime/game/csgo/addons/counterstrikesharp/plugins/CS2Craft.Server/`. Captures are TGA files there. Use a fresh test save to make inventory and placement predictable. Close CS2 when done; Minecraft saves and exits.

Synthetic host fixture, in two terminals:

```powershell
dotnet run --project tools/CS2Craft.Probe -c Release -- 120 dist/probe
.\tools\Start-Minecraft-Dev.ps1 -WorldName CS2Craft-Fixture
```

Replay its real Minecraft block/texture capture offscreen:

```powershell
.\native\build\Release\CS2Craft.RendererTest.exe dist/probe/minecraft-world.c2rf dist/probe/world.tga
```

The synthetic fixture is not a CS2 map. Never run it alongside the native compositor against the same mapping: each ring has one consumer.

## Source layout

- `minecraft/`: SkyCraft-derived Fabric client and integrated-server logic.
- `src/CS2Craft.Bridge/`: protocol, coordinates and collision encoding.
- `src/CS2Craft.Server/`: CS2 host, exact geometry streaming, controls and gameplay adapter.
- `native/`: world/HUD renderer, input routing, capture and GPU tests.
- `tests/`, `tools/CS2Craft.Probe/`: bridge and real-Minecraft fixtures.
- `docs/`: architecture and verification details.

Minecraft-side implementation: chasmlol's SkyCraft, MIT, release commit `adba1e3172d667708d22c880205bb3dedce1b178`. New host/native source is GPL-3.0-or-later. See [third-party notices](THIRD-PARTY-NOTICES.md).
