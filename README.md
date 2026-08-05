# Soft Pitch Glue

Stops (or delays) cooked pitch glue from hardening in Vintage Story, so clutter objects glued with it can be retrieved later.

| | |
|---|---|
| modid | `lionfoxsoftglue` |
| version | 1.0.1 |
| game dependency | 1.20.0 |
| type | code (C#) |

## Building

Requires the Vintage Story install to reference the game assemblies. Set the
`VINTAGE_STORY` environment variable to your install directory, then:

```
dotnet build -c Release
```

Or override per-invocation: `dotnet build -c Release -p:VS_PATH=D:\Vintagestory`

## Status

Deployed on the server at v1.0.1. **Not yet ported to Vintage Story 1.22.x** â€”
1.22 requires .NET 10 and changed parts of the modding API. Porting is tracked
in the private `vintagestory-server-ops` repo (`registry/own-mods.md`).

## History

This mod was built and deployed before it had a repository of its own; its source
lived only inside the `lionfox_servermods` working mirror until 2026-08-05.
