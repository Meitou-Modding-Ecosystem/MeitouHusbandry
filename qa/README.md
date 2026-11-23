# Repeat the checks

Run from the project root in PowerShell 7 with the .NET 11 SDK. Set `MEITOU_DIR` to the packaged v0.22.1 Windows game directory and `KENSHI_PATH` to your Kenshi installation. The verifier uses a new profile for every run. Each profile has a private mod list, config, captures and save output. It reads the installed Kenshi data through a directory junction.

```powershell
./tools/package.ps1
dotnet run --project tests -c Release
dotnet build qa/Live -c Release

$qa = @{ MeitouDir = $env:MEITOU_DIR; KenshiPath = $env:KENSHI_PATH }
$ready = 'saves/Two Garru outside Stoat'
./qa/run.ps1 @qa -Mode core
./qa/run.ps1 @qa -Mode ride -SeedSave $ready
./qa/run.ps1 @qa -Mode two -SeedSave $ready
```

The main run installs the player ZIPs directly. Its observer invokes the normal F10 handler, submits native player orders, records rendered frames and requests native save and reload. The observer DLL lives outside the mods directory. The two-post run adds a QA-only level overlay from `qa/fixtures` and assigns a different Garru to each instance. The simulation checks use Meitou’s movement and decision systems with a synchronous path service so every tick can be compared across worker counts.

To recreate a starter locally, run `dotnet run --project tools/Seed -c Release` and use `-Mode seed` to generate a save from the independent field-visit start. Run `-Mode prepare -SeedSave <that save folder>` to make a clean save with just the two player mods active. The `second-post` argument to the seed tool creates the test-only overlay. The existing editable town project is preserved when regenerating fixtures.

The renderer and F10 observation hooks belong to the QA program. The installable Husbandry framework uses the released decision, movement, command and save interfaces.


When using the release downloads, extract the source archive first and run these commands from that source folder. Copy saves/ from the tester kit into it, or regenerate the starter using the seed and prepare modes above.

The observer answers the stock gate inspection using DialogueChoice (accept inspection, then [Leave]); it does not suppress dialogue or clear the simulation pause. An unexpected conversation fails the trial. The live runner starts one client at a time and stops only the process it launched.

Run python qa/audit.py <framework-only-profile> <single-post-profile> <two-post-profile> <starter-preparation-profile> after copying the prepared and assigned saves to the documented saves/ paths. This compares every packaged file with the installed copies and checks the saved identity. Python 3.11 or later is needed only for the evidence tools; qa/make-clip.py additionally needs FFmpeg.
