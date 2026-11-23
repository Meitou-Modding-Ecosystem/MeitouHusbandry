# Contributing

Start with a small design example: **How would you make a hitching post that an author can place in their own town?** Describe the town-owned data, assigning and releasing an animal, what survives reload, and the smallest in-game test. The shipped example is one answer, not a requirement for every future animal interaction.

Use Issues for a proposed behavior or integration before a large change. A concrete missing animal job or engine hook can then be discussed with Meitou using a reproducible case. Breeding, taming, riding integrations and organization migration are separate follow-ups.

Keep content definitions separate from the runtime that consumes them. Preserve stable pack, FCS and placement IDs. Consumers query the current world; dialogue mods own their lines and conditions.

Use .NET 11, four-space C# indentation, LF endings, nullable annotations and warnings as errors. Keep gameplay state in the per-world simulation system and publish read-only snapshots for consumers. Private renderer observation is confined to QA.

Run `dotnet run --project tests -c Release` after gameplay changes. The suite checks native decisions and movement with one and four workers. See `qa/README.md` for native save, scene and authoring checks. Report the exact game build and distinguish simulated checks from live observations. Never commit game assets, game DLLs, local configuration, trial installations or personal saves.

Contributions follow LICENSING.md.

Source uses LF endings. The projects/ folder preserves Maestro-exported bytes, including its Windows line endings. Release packaging normalizes ZIP timestamps without rewriting the sample payload. After committing tested source and staging audited fixture saves, tools/release-kit.ps1 creates matching source, tester kit and checksums.
