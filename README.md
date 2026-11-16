# trench-war

## Adding large files (builds over 25 MB)

GitHub has different limits depending on **how** you upload:

| How you add files | Limit per file | What happens over the limit |
|---|---|---|
| **Browser upload** (drag & drop on github.com) | **25 MB** | Upload is rejected — this is what you're hitting |
| **Git push** (`git add` + `git push` from your computer) | **100 MB** hard limit, warning at 50 MB | Push is rejected with `GH001: Large files detected` |
| **Git LFS** (see Option B) | **2 GB** (Free/Pro) | Works, but uses your LFS quota |
| **GitHub Releases** (see Option A) | **2 GB** per asset | Works — best for game builds |

> **TL;DR for game builds:** don't use the browser upload. Use **GitHub Releases**
> (Option A) for downloadable builds, or **Git LFS + git push** (Option B) if the
> build must live in the repo history. Both are set up in this repo already.

---

### Option A — GitHub Releases (recommended for builds) ✅

Best for `.zip` / `.exe` / `.apk` / `.dmg` / `.pak` builds that players download.
Releases keep the repo small and fast, and each asset can be up to 2 GB.

1. Zip your build first (one zip per platform, e.g. `trench-war-windows.zip`).
   Don't upload loose folders — zip them.
2. On GitHub: go to your repo → **Releases** → **Draft a new release**.
3. Tag it (e.g. `v0.1.0`), add release notes, then drag your zip(s) into
   **"Attach binaries"** and click **Publish release**.
4. Link it from this README so people can find it, e.g.:

   ```markdown
   ## Download
   - [Windows build (v0.1.0)](https://github.com/TriP726/trench-war/releases/tag/v0.1.0)
   ```

Or from the terminal (after `gh auth login`):

```bash
gh release create v0.1.0 ./trench-war-windows.zip --title "v0.1.0" --notes "First build"
```

---

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
git add .gitattributes .gitignore   # first time only
git commit -m "Set up Git LFS for large files"
git push

# Add your large files — LFS handles them automatically via .gitattributes
git add Builds/trench-war-windows.zip
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

---

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

---

### Adding files via this chat

You can also attach your build files to this conversation and I'll add them
to the repo for you (via git push, or as a GitHub Release if they're over
100 MB). Just tell me the version/tag you want for the release.

### Before you upload a build — quick checklist

- [ ] Zip the build (one zip per platform) — don't upload loose folders
- [ ] Remove debug symbols / PDBs unless you need them for crash reports
- [ ] Compress textures/audio (e.g. PNG → WebP, WAV → OGG) where possible
- [ ] Test the zipped build from a clean folder before publishing
