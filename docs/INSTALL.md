# Installing CS2Craft on another machine

CS2Craft runs on Windows only. The project folder holds source code; each machine builds it and prepares its own private copy of CS2. Nothing in the Steam installation is modified. Run the steps below in PowerShell from the project folder.

## 1. Copy the project

Copy the project folder without `runtime/`, `.cache/`, `dist/` and any `bin/`, `obj/`, `build/` or `.gradle/` folders; the new machine recreates them. A zip of the folder or a private git repository both work.

Windows marks files from a downloaded zip as untrusted, which stops PowerShell scripts from running. Once, in the project folder:

```powershell
Get-ChildItem -Recurse | Unblock-File
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## 2. Install what is missing

```powershell
.\tools\Install-Prerequisites.ps1
```

It installs only what the machine lacks, all free and from each vendor's official download:

| Item | Source | Licence |
|---|---|---|
| Steam | Valve's `SteamSetup.exe` (you go through its setup window and sign in) | [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/) |
| Counter-Strike 2 | Opens Steam's own install page; you click Install (free to play, about 60 GB). The script carries on with the rest while it downloads and waits for it at the end. | Steam |
| .NET 10 SDK | Microsoft's SDK installer | [.NET licence](https://github.com/dotnet/core/blob/main/license-information.md) |
| Visual Studio 2022 C++ tools | Microsoft's Visual Studio Community installer with only the **Desktop development with C++** workload (includes CMake and the Windows SDK; a few GB). An existing Visual Studio 2022 just gets the workload added. | [Visual Studio Community](https://visualstudio.microsoft.com/license-terms/vs2022-ga-community/): free for individuals |
| Java 25 | Eclipse Temurin 25 from adoptium.net (sets `JAVA_HOME`) | [GPLv2 with Classpath Exception](https://openjdk.org/legal/gplv2+ce.html) |

How it behaves:
- It asks before each install; `-Yes` skips the questions.
- Each vendor installer still shows its own licence and progress.
- Windows asks for administrator rights (UAC) where an installer needs them.
- It refuses any download whose digital signature is not valid and from the expected publisher (Valve Corp., Microsoft Corporation, Eclipse Foundation).
- It deletes the installers afterwards.

At the end it runs the check below. Then it offers to build CS2Craft and prepare the private CS2 copy, which are steps 4 and 5. If an installer asks for a restart, restart Windows and run it again. `-SkipSetup` stops after the check; `-CS2Root` points it at a CS2 installation it did not find.

To install things yourself instead, here is what is needed:

| Install | Notes |
|---|---|
| Windows 10 or 11, 64-bit, with a DirectX 11 graphics card | The renderer hooks CS2's DirectX 11 output. |
| [Steam](https://store.steampowered.com/about/) with **Counter-Strike 2** | Let it finish downloading. Any Steam library folder works. |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | The SDK, not only the runtime. |
| [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) (Community is fine) | Workload **Desktop development with C++**. Keep its default **C++ CMake tools for Windows** and **Windows 11 SDK** (or 10 SDK) components ticked. It has to be Visual Studio 2022, because the native build uses its CMake generator. |
| **Java 25** | The [Minecraft Launcher](https://www.minecraft.net/download) brings one, or install [Eclipse Temurin 25](https://adoptium.net/temurin/releases/?version=25). The scripts find either. Otherwise pass `-JavaHome`. |
| Internet on the first run | The scripts download Metamod, CounterStrikeSharp 1.0.376 (checksum-checked), NuGet packages, Gradle, Fabric and Minecraft 26.3. |
| About **60 GB free** on the project's drive | The private CS2 copy is about 53 GB; builds and caches add a few GB. This is separate from the Steam library that holds CS2 itself. |

**Minecraft.** Minecraft runs as a development client under an offline name (`CS2Crafter`), and the build downloads its game files from Mojang. No Minecraft login is asked for, but every player should own [Minecraft: Java Edition](https://www.minecraft.net/). The installer does not buy or install Minecraft.

## 3. Check the machine

```powershell
.\tools\Check-Prerequisites.ps1
```

It only reads. It checks Windows, Steam and CS2, .NET, Visual Studio C++, CMake, the Windows SDK, Java 25, disk space and internet access. Each line is PASS, WARN or FAIL with what to do, and it ends with the next command to run. If CS2 is not found, add `-CS2Root "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"`. To use a particular Java, add `-JavaHome "C:\path\to\jdk-25"`.

## 4. Build and prepare

```powershell
.\tools\Build.ps1
```

Builds the renderer, the CS2 plugin, the map exporter and the Minecraft mod, and runs every test. It takes a few minutes, longer the first time while Gradle downloads Minecraft.

```powershell
.\tools\Prepare-CS2.ps1
```

Creates the private CS2 copy in `runtime/`, installs Metamod, CounterStrikeSharp, the plugin and the renderer. Add `-CS2Root` if CS2 was not found. Takes a while: it copies about 53 GB.

## 5. Play

```powershell
.\tools\Play.ps1
```

Starts CS2 (Steam must be running) and the hidden Minecraft client. In CS2, join a team, open the console with **~** and enter `css_cs2craft_play`. The first launch also builds Dust II's collision file and downloads Minecraft's assets, so allow a few minutes.

CS2 starts with `-insecure`: VAC is off and only local games with bots work. Only Dust II is prepared.

## Settings

The plugin's settings file is created on first launch at `runtime\game\csgo\addons\counterstrikesharp\configs\plugins\CS2Craft.Server\CS2Craft.Server.json`. The options are listed in the [README](../README.md). It lives in `runtime/`, so copy it over from another machine if you want the same settings.

## Updating

- **After a CS2 update:** run `.\tools\Prepare-CS2.ps1` again to refresh the private copy.
- **After updating the project files:** close CS2, run `.\tools\Build.ps1`, then `.\tools\Prepare-CS2.ps1` (it installs the new plugin and renderer).

## Troubleshooting

| Message | Fix |
|---|---|
| `...cannot be loaded because running scripts is disabled` or `is not digitally signed` | Step 1: `Unblock-File` and `Set-ExecutionPolicy`. |
| `Refusing ...: signature ...` (installer) | A download did not come signed by its publisher (a proxy, a broken download, or a changed link). Nothing was run; try again later or install that item yourself from the vendor's site. |
| `... exited with code ...` (installer) | That vendor installer failed or was cancelled. Rerun `Install-Prerequisites.ps1`; it skips what is already installed. |
| `CS2 not found. Pass -CS2Root...` | Pass the folder that contains `game\bin\win64\cs2.exe`. |
| `Java 25 not found...` | Run `Install-Prerequisites.ps1`, or pass `-JavaHome` with the folder that contains `bin\java.exe`. |
| `Install the Visual Studio C++ tools and CMake` / `Native configure failed` | Add the C++ workload and CMake in the Visual Studio Installer; the check script shows which is missing. |
| `Checksum mismatch` | A download was corrupted or changed: delete `.cache\downloads` and rerun `Prepare-CS2.ps1`. |
| `Run tools/Build.ps1 and tools/Prepare-CS2.ps1 first.` | Do step 4 first. |
| In CS2: `An existing Minecraft/CS2Craft process holds the bridge` | A previous Minecraft is still saving. Wait for it to exit (or end `java.exe` in Task Manager) and try again. |

## Removing

Delete the project folder; `runtime\` holds everything CS2Craft created. The Steam installation of CS2 was never changed. Steam, .NET, Visual Studio and Temurin are ordinary programs you can uninstall from Windows Settings > Apps.
