# Third-party notices

- **SkyCraft** by chasmlol: https://github.com/chasmlol/SkyCraft, release v0.1.0, commit adba1e3172d667708d22c880205bb3dedce1b178. `minecraft/` and `third_party/skycraft_protocol_v10.h` derive from that MIT-licensed source. License: `third_party/SkyCraft-LICENSE.txt` and `minecraft/LICENSE`.
- Minecraft fork changes: separate default mapping and mirror-save name, CS2Craft display metadata/build name, pinned Loom release, disabled Discord presence and e4mc runtime dependency, adjusted development launch lifecycle. Internal upstream package/mod identifiers are retained for compatibility. Upstream source comments referring to Skyrim have not been comprehensively renamed.
- **MinHook** by Tsuda Kageyu: https://github.com/TsudaKageyu/minhook, v1.3.4, commit c3fcafdc10146beb5919319d0683e44e3c30d537. Vendored native sources and build metadata in `third_party/minhook/`; license in `third_party/minhook/LICENSE.txt`.
- **CounterStrikeSharp**: https://github.com/roflmuffin/CounterStrikeSharp, pinned NuGet/runtime 1.0.376. The API/runtime dependency carries its upstream licenses. The new CS2Craft host and native integration source are provided under GPL-3.0-or-later; copied SkyCraft/MIT and MinHook notices remain in force for their files.
- **Metamod:Source**: https://github.com/alliedmodders/metamod-source, pinned loader build 2.0.0.1474. Downloaded into the ignored local runtime; retain the archive's license notices.
- Minecraft, Fabric, Valve game data and runtime libraries are not relicensed by this project. Game data downloaded by Loom or copied into `runtime/` is local-only and excluded from source packaging. This project is not affiliated with Valve, Mojang or Microsoft.

- **ValveResourceFormat / Source 2 Viewer**: https://github.com/ValveResourceFormat/ValveResourceFormat, pinned NuGet 20.0.6980 (MIT) and its transitive ValvePak dependency. Used only by the local map-export tool. Exported CS2 collision data stays under ignored runtime directories and is excluded from source archives.
- The networked camera approach was informed by the public https://github.com/spitice/cs2-external-view implementation; no plugin source is bundled.
