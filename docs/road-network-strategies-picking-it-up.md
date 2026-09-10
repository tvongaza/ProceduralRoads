# Picking up the road-network study

For someone — or someone's agent — arriving at
[`docs/road-network-strategies.md`](road-network-strategies.md) and wanting to
ask it questions, check a number, or run it again. Written by an AI assistant,
like the study itself.

Work out which of these you actually need. Most questions stop at level 1.

---

## Level 0: what to know before asking anything

Four facts that the document states but an agent skimming it will miss, and
each of them has already caused a wrong conclusion at least once:

1. **It is an offline study. No gameplay traversal has been validated.** Roads
   are joined *geometrically*. Nothing has been walked, driven or carted, so no
   claim here supports "a player can get from A to B".
2. **The world called `Issue7` is not issue #7's seed.** It is `gqZ5SrFUjk`;
   the issue reports `nRleKzu9bI`, which was never generated. The document
   calls it "world A". Nothing here describes the reporter's map.
3. **The terrain is Valheim 0.221.12, buildid 21981559** — the build before
   1.0. 1.0 changed `GetBiome`, and `GetHeight` reads the biome to choose a
   height function, so terrain, and everything measured on it, may differ on
   1.0. That comparison has not been run.
4. **Two serving measures are reported and they are not interchangeable.**
   `served` is the strict test; `served (+0.5 m)` is a wider proximity test
   that adds 2 to 10 places per run, most of them boundary recoveries and some
   of them incidental. Charts use the `+0.5 m` one; several tables give both.
   Never mix them in one comparison.

The document's Appendix E lists every claim earlier drafts got wrong across
five review rounds. It is worth reading *first* if you intend to quote the
study, because most of those errors were confident sentences sitting on top of
correct numbers.

---

## Level 1: ask questions about the findings (no setup)

Everything needed is in this repository, on this branch.

- The study: [`docs/road-network-strategies.md`](road-network-strategies.md)
- The runs behind it:
  [`validation-results/study-2026-09-10/`](../validation-results/study-2026-09-10/)
  — start with its `README.md`, which says what every file holds.
- The figures:
  [`validation-results/screenshots/study-2026-09-10/`](../validation-results/screenshots/study-2026-09-10/)

Clone and point an agent at the directory:

```bash
git clone --branch docs/validation-gap https://github.com/tvongaza/ProceduralRoads.git
cd ProceduralRoads
```

The published tables are small enough to read directly. Per run
(`<world>.<plan>.r1.*`):

| file | one row per | use it to answer |
|---|---|---|
| `places.csv` | place in the world | which places were selected, planned-and-built and served, by category and island |
| `attempts.csv` | pathfinder search | why a connection failed, how much ground it settled, which connection it belonged to |
| `islands.csv` | island that generated | per-island roads, searches and **seconds** |
| `selection.csv` | candidate place | what each island offered its quota and what the quota took |
| `crossings.csv` | river crossing | fords and bridges, their banks and depth |
| `manifest.json` | run | settings, input hashes, build configuration, stage timings, every headline metric |

**The counting trap.** Three different numbers get called "connected", and a
question phrased loosely will get the wrong one:

- **connections** — decisions the plan made;
- **build searches** — rows in `attempts.csv`; one plan writes *two* rows for
  one connection, so this is not a connection count;
- **planning searches** — searches run to price a candidate edge, which lay no
  road and appear in no attempt row.

`manifest.json` reports all three separately. Section 2 of the document defines
every metric once.

---

## Level 2: check a number in the document

Every headline figure traces to a published run. The document's Appendix D
names the code commit, the build configuration and the input hashes; each
manifest repeats them.

To re-derive a table, read the `manifest.json` for the run it names. To
re-derive a *set* — which places a plan served, which it lost against another
plan — use `places.csv`, matching places by `name` plus rounded `x`/`z`.

Two gotchas:

- **Runs are deterministic.** The three measured repetitions of every run agree
  to the last metre. If your numbers differ, something about the inputs or the
  code differs; it is not noise.
- **Runtimes are not comparable across build configurations.** Debug is 6.4×
  Release on this workload. Compare two runtimes only when their manifests
  agree on `buildConfiguration`, `runtime` and `platform`.

---

## Level 3: run the generator again

The harness is on the `study/road-network-strategies` branch of this
repository. It builds standalone — the test project supplies shims, so no
Valheim assemblies are needed.

```bash
git clone --branch study/road-network-strategies https://github.com/tvongaza/ProceduralRoads.git study
cd study
dotnet build ProceduralRoads.Study/ProceduralRoads.Study.csproj -c Release
./ProceduralRoads.Tests/run-tests.sh    # gate on the exit code, not on the output
```

**Build Release.** A Debug build produces identical output roughly six times
slower, and comparing across the two produced a wrong headline in an earlier
draft of the study.

Then a run, given terrain (see level 4):

```bash
# Only if the built apphost reports "the .NET location: Not found". DOTNET_ROOT
# must be the directory that CONTAINS shared/Microsoft.NETCore.App - which is
# not what `dirname $(which dotnet)` gives on a Homebrew install.
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec   # macOS/Homebrew; /usr/share/dotnet on many Linuxes
./ProceduralRoads.Study/bin/Release/net10.0/roads-study generate \
    <World>.base128.csv <World>.cells8.csv <World>.locations.csv \
    --out runs --plan parity --fallback none --label myrun
```

`--plan` is `parity` (the shipped plan), `routed-mst`, `trunk` or `reverse`
(POI-to-network) — the four the study compares — and also `tree`, `hub` and
`grow`. `--no-routes` skips the multi-megabyte centreline dump. Besides
`generate`, `roads-study` has `islands`, `compare`, `outcomes`, `audit`,
`per-island` and `clustering`.

A whole world takes 3 to 15 seconds to generate, plus about 3 seconds to read
the 8 m terrain.

---

## Level 4: regenerate the terrain

**The terrain dumps are not published.** They are about 250 MB each for the 8 m
lattice, and they are what every run reads. Without them you can check any
number in the study but cannot produce a new run.

**Only world A can be regenerated by anyone else.** Its seed is `gqZ5SrFUjk`.
The seeds of worlds B and C were never recorded and exist only on the machine
that made them, so those two worlds are not reproducible by a third party at
all.

What it takes:

1. Valheim with BepInEx, Jotunn, this mod, and
   [valheimCLI](https://github.com/tvongaza/valheimCLI) on its
   `feature/world-dump-columns` branch — which is where `cli_world_dump` comes
   from.
2. Create a world with seed `gqZ5SrFUjk` and load into it with road generation
   **off**, so the dump carries terrain and no roads. On this branch that is
   the config key `[Debug] GenerateRoadsOnLoad = false` in the mod's BepInEx
   config file. (Later branches move some debug switches to
   `PROCEDURALROADS_*` environment variables; this one has none.)
3. In the console:

   ```
   cli_world_dump 128     # the island grid, and locations.csv
   cli_world_dump 8       # the pathfinding cells - about 250 MB
   cli_world_dump 50      # the raster the pictures are drawn from
   ```

   Samples sit on the world lattice (`-10000 + i * step`), so step 8 lands
   exactly on the cells the pathfinder walks and step 128 on the island grid.
   Rename the outputs to `<World>.base128.csv`, `<World>.cells8.csv`,
   `<World>.map50.csv` and `<World>.locations.csv`.
4. **Dump from the machine that runs the game.** An earlier round of this study
   was invalidated by dumps taken with an unrelated world-generation mod
   loaded; the terrain differed from the game's own.

**Expect your numbers to differ from the study's**, because the study's terrain
is buildid 21981559 and current Valheim is 1.0. Heights and rivers were
byte-identical between those builds, but `GetBiome` changed and `GetHeight`
reads the biome, so a fresh dump is not the same input. If you want to *check*
the study rather than extend it, the manifests' input hashes are the honest
test: matching hashes mean matching inputs.

---

## Level 5: redraw the figures

Pure Python, no libraries, on the study branch:

```bash
python3 scripts/world-svg.py <World>.map50.csv <World>.locations.csv runs/myrun.routes.csv \
    --background <World>.cells8.csv --out world.svg          # maps, --zoom/--zoom-only/--mark
python3 scripts/study-comparison.py tradeoff --runs runs --out chart.svg
python3 scripts/island-sheet.py --out sheet.svg --columns 2 "label=panel.png" ...
python3 scripts/junction-audit.py runs/myrun.routes.csv runs/myrun.attempts.csv
```

The scripts emit SVG. Every one pads its drawing to a square canvas and prints
the drawing's true size, because the rasteriser used here fits an SVG into a
square and chooses the dimension by aspect ratio — a wide, short chart loses its
right-hand edge otherwise. Crop back to the printed size after rasterising.

**Read every exported image at normal size before using it.** In this study a
caption once described a road that was 290 m outside the frame's subject, and a
figure went three revisions with a stale title because its URL was re-pointed
instead of the image being rebuilt.

---

## Good questions to ask it, and bad ones

**It can answer:** why the networks are sparse and what the binding constraint
is; how four connection plans compare on coverage, network shape and
computation across three worlds; which destinations each plan gains and loses
against the shipped one; why a particular connection failed; what each lever in
the mod's configuration is worth.

**It cannot answer:** whether any of this is playable, walkable or fun; what
happens on the reporter's world or on Valheim 1.0; what one particular road
does in game; or whether a plan is faster in a way that survives correcting the
harness's own boundary artifact. Section 6 lists the outstanding evidence in
priority order, and Appendix C lists what the geometry cannot vouch for.
