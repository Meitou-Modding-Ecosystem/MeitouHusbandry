# Historical v0.20.1 checks

The local 0.1.0-playtest.1 prototype passed on Windows x64 with Meitou v0.20.1, commit `8c79093d075fac1f4ca8df53cf025b6155cac45f`, API 2. This is historical evidence, not a claim about v0.22.1.

- Framework alone: zero posts and zero hitching actions.
- Stoat: assign, occupied rejection, handler departure, outward player order, idle return, save/reload of the correct Garru, release during return, ordinary movement and revisit.
- Two copies of the same building: independent occupancy and release.
- Simulation: 1,763 identical hashes across one and four workers.
- Authoring: Maestro 0.1.0-playtest.1 on Ubuntu 24.04.5 under WSLg.

[Simulation output](simulation.txt) · [Single-post trace](stoat-trace.txt) · [Two-post trace](two-post-trace.txt) · [Audit](audit.json) · [Editor archive verification](editor-release.txt).

The original local kit and recordings remain preserved separately. Current results are in the repository's top-level RESULTS.md.
