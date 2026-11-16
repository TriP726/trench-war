# Trench War

![Trench War cover](https://img.itch.zone/aW1nLzMyMDAxMjQ1LnBuZw==/original/TyFqiz.png)

**Command a war on the map table.** Trench War is a real-time strategy game on a
fully destructible voxel battlefield: train squads, dig in, keep your supply
lorries rolling and watch every shell tear the ground apart.

> **Open multiplayer playtest is live!**

## Download

**Windows 64-bit — `TrenchWar-Windows.zip` (66 MB)**

- **Download on itch.io:** https://xanomusik.itch.io/trench-war
- Unzip and run `TrenchWar.exe` — no install needed.

> **Windows warning?** On the first start Windows SmartScreen may say
> "Windows protected your PC". It does that for every program without a paid
> code signing certificate, which is normal for small indie games and does
> not mean anything is wrong. Click **More info**, then **Run anyway**.
> Updates from inside the game do not show it again.

## Six nations, six ways to fight

![Six nations](https://img.itch.zone/aW1nLzMyMDAyMzMyLnBuZw==/original/tLDBAx.png)

Germany 1940, Great Britain 1940, Soviet Union 1941, Japan 1942, USA 1965 and
North Vietnam 1965. Each comes with its own units, strengths and weaknesses:
Soviet snipers and Katyushas, Japanese kamikaze squadrons, American Hueys and
napalm, Vietnamese ambushes and punji traps.

## Everything breaks

![Everything breaks](https://img.itch.zone/aW1nLzMyMDAyNjM1LmdpZg==/original/u30DLv.gif)

Every shell digs a crater, trenches collapse, woods burn. The atomic bomb
leaves a hole the size of a village.

## Command squads, not soldiers

![Command squads](https://img.itch.zone/aW1nLzMyMDAyNzA0LmdpZg==/original/LHCQ%2Fi.gif)

- Infantry, anti-tank, mortars, pioneers, scouts, snipers, flamethrowers and tanks
- Orders from a pie menu: move, move until contact, claim ground, charge, hold, attack, make camp
- High ground, slopes, camouflage and ambushes, fog of war

## Supply war

![Supply war](https://img.itch.zone/aW1nLzMyMDAyMzQyLmpwZw==/original/0h5HIJ.jpg)

Steelworks, farms and refineries send their goods to your HQ by lorry. Build
them forward for bigger loads and guard the roads, because the enemy will cut them.

## Air support

![Air support](https://img.itch.zone/aW1nLzMyMDAyMzQzLmpwZw==/original/E2%2BpmV.jpg)

Recon flights, carpet bombing, napalm, Huey gunships, kamikaze squadrons and
the atomic bomb.

## Maps and modes

- 6 maps: Battlefield, Fields, Woods, Desert, Jungle and Winter, normal or double size, by day or by night
- Singleplayer against the computer (4 levels, no cheats)
- Multiplayer through our own relay server. No port forwarding, no VPN: open a lobby or join a public one
- 1v1, 2v2, 3v3 and free-for-all for up to 6 players, bots fill empty seats
- Ranked battles with ELO, player levels and 74 challenges that unlock profile icons
- English and German

## Controls

- Camera: WASD, Q/E to turn, mouse wheel to zoom, right or middle mouse drag
- Left click / drag: select squads. Right click: pie menu with orders
- Left click a building: train squads, call air missions. Right click the ground with nothing selected: build
- Tab: scoreboard, Enter: chat
- B: no-build zones, R: resource symbols, F5: new battle, Esc / F10: menu, F11: window / fullscreen

## Screenshots

| | |
|---|---|
| ![Screenshot 1](https://img.itch.zone/aW1hZ2UvNTMyNTE3Mi8zMjAwMjIyNy5qcGc=/original/wz5OqA.jpg) | ![Screenshot 2](https://img.itch.zone/aW1hZ2UvNTMyNTE3Mi8zMjAwMjIzMC5qcGc=/original/8mhCnT.jpg) |
| ![Screenshot 3](https://img.itch.zone/aW1hZ2UvNTMyNTE3Mi8zMjAwMjIzMy5qcGc=/original/2ebLaT.jpg) | ![Screenshot 4](https://img.itch.zone/aW1hZ2UvNTMyNTE3Mi8zMjE1NTYxNC5qcGc=/original/%2BDbULh.jpg) |
| ![Screenshot 5](https://img.itch.zone/aW1hZ2UvNTMyNTE3Mi8zMjE1NTY1Ni5qcGc=/original/uVwJAh.jpg) | ![Screenshot 6](https://img.itch.zone/aW1hZ2UvNTMyNTE3Mi8zMjE1NTY2OC5qcGc=/original/F%2FT95v.jpg) |

*Screenshots and GIFs are hosted on itch.io and hot-linked above so the repo stays small.*

## Info

| | |
|---|---|
| Published | 1 day ago (at time of mirroring) |
| Status | In development |
| Platforms | Windows |
| Author | [xanoMusik](https://xanomusik.itch.io/) |
| Genre | Strategy / Real-time strategy |
| Made with | Unity |
| Tags | Destruction, Historical, Multiplayer, Pixel Art, Singleplayer, Tanks, Voxel, War, World War II |
| Average session | About an hour |
| Languages | English, German |
| Inputs | Keyboard, Mouse |
| Multiplayer | Ad-hoc networked multiplayer |
| AI Disclosure | AI Assisted |
| itch.io page | https://xanomusik.itch.io/trench-war |

---

## For developers: adding large files (builds over 25 MB)

The Windows build (`TrenchWar-Windows.zip`, 66 MB) is **not stored in git
history** — it's hosted on itch.io (primary) and can be mirrored to GitHub
Releases. Here's why, and how to add new builds:

GitHub has different limits depending on **how** you upload:

| How you add files | Limit per file | What happens over the limit |
|---|---|---|
| **Browser upload** (drag & drop on github.com) | **25 MB** | Upload is rejected |
| **Git push** (`git add` + `git push` from your computer) | **100 MB** hard limit, warning at 50 MB | Push is rejected with `GH001: Large files detected` |
| **Git LFS** (see Option B) | **2 GB** (Free/Pro) | Works, but uses your LFS quota |
| **GitHub Releases** (see Option A) | **2 GB** per asset | Works — best for game builds |

> **TL;DR:** don't use the browser upload for the 66 MB build. Use **GitHub
> Releases** (Option A) for downloadable builds, or **Git LFS + git push**
> (Option B) if the build must live in the repo history. Both are set up in
> this repo already (`.gitattributes` + `.gitignore`).

### Option A — GitHub Releases (recommended for builds) ✅

Best for `.zip` / `.exe` / `.pak` builds that players download. Releases keep
the repo small and fast, and each asset can be up to 2 GB.

1. Zip your build first (one zip per platform, e.g. `TrenchWar-Windows.zip`).
   Don't upload loose folders — zip them.
2. On GitHub: go to your repo → **Releases** → **Draft a new release**.
3. Tag it (e.g. `v0.1.0`), add release notes, then drag your zip(s) into
   **"Attach binaries"** and click **Publish release**.
4. Link it from this README so people can find it.

Or from the terminal (after `gh auth login`):

```bash
gh release create v0.1.0 ./TrenchWar-Windows.zip --title "v0.1.0" --notes "Playtest build"
```

### Option B — Git LFS (builds/assets versioned in the repo)

Use this when the large file must be in git history (e.g. textures, models,
audio, `.pak`, `.uasset`, or a build you want cloned with the repo).
This repo already has `.gitattributes` tracking common build/asset types.

**One-time setup on your computer:**

```bash
# Install Git LFS: https://git-lfs.github.com/
git lfs install
git lfs version   # verify it works
```

**Then add files normally (no browser upload):**

```bash
git pull
git add Builds/TrenchWar-Windows.zip
git commit -m "Add Windows build"
git push
```

Verify LFS is working:

```bash
git lfs ls-files   # should list your large files
```

⚠️ **Free quota:** GitHub gives 1 GB LFS storage + 1 GB bandwidth/month on Free.
Every version of a large file counts toward storage, and every clone/download
counts toward bandwidth. That's why Releases (Option A) are better for builds
that change often.

### Option C — Just `git push` (only if file is under 100 MB)

If a file is between 25 MB and 100 MB, the browser will still reject it,
but a local push will work:

```bash
git add path/to/file.zip
git commit -m "Add file"
git push
```

Don't do this for builds you update often — git history keeps every old
version forever, even after you delete the file, and the repo bloats.
Prefer Option A or B.

### Adding files via this chat

You can also attach your build files to this conversation and I'll add them
to the repo for you (via git push, or as a GitHub Release if they're over
100 MB). Just tell me the version/tag you want for the release.

### Before you upload a build — quick checklist

- [ ] Zip the build (one zip per platform) — don't upload loose folders
- [ ] Remove debug symbols / PDBs unless you need them for crash reports
- [ ] Compress textures/audio (e.g. PNG → WebP, WAV → OGG) where possible
- [ ] Test the zipped build from a clean folder before publishing
