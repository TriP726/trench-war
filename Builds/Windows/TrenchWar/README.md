# Trench War — Mod Pack

One extract, two mods. Built for the **Trench War** build of 2026-11-14
(Unity 6000.4.9f1, Mono).

Contains:

| Mod | What it does |
| --- | --- |
| **Mortar Range Per Nation** | Sets the maximum mortar range independently for each nation (game default is 170). |
| **Sandbox Mode** | Adds a `SANDBOX` entry to the singleplayer mode picker: 6-way FFA vs bots, unlimited resources for you, no win/lose, never eliminated. |

Both are singleplayer/offline only — the game has an anti-cheat and a ranked
online mode, so don't use them in multiplayer.

---

## Install

1. Unzip this folder so its contents sit **next to `TrenchWar.exe`**
   (the same folder as `TrenchWar.exe` and `TrenchWar_Data/`).
2. Start the game. BepInEx and both mods load automatically.

That's it. No other files are touched.

## Using the mods

**Mortar range** — after the first launch, edit:

```
BepInEx/config/xano.trenchwar.mortarrange.cfg
```

```
[Mortar range]
Germany 1940 = 1.5
Great Britain 1940 = 1
USA 1965 = 1
North Vietnam 1965 = 1
Soviet Union 1941 = 1
Japan 1942 = 1
```

`1.5` = 255 range instead of 170.

**Sandbox** — Startmenu → **SINGLEPLAYER** → pick **SANDBOX** → choose nation /
map / bots / AI level → **START BATTLE**. Leave again with Esc → **SURRENDER**
(returns to the title screen).

## Uninstall / disable

Delete `BepInEx/` (and `doorstop_config.ini`, `winhttp.dll`, `.doorstop_version`)
to remove everything, or just delete / rename a single dll in `BepInEx/plugins/`
to disable one mod.

## Logs

`BepInEx/LogOutput.log` — look for `Mortar Range Per Nation loaded` and
`Sandbox mod loaded.`

## Adding more mods later

Drop any new `*.dll` into `BepInEx/plugins/`. See `PLAN.md` for the mod
architecture, the source of both mods, and the planned future mods.
