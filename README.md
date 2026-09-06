<div align="center">

<img src="PKHeX.Mac/Assets/img/artwork/a_25.png" height="72" alt="Pikachu">
<img src="PKHeX.Mac/Assets/img/artwork/a_6.png" height="72" alt="Charizard">
<img src="PKHeX.Mac/Assets/img/artwork/a_245.png" height="72" alt="Suicune">
<img src="PKHeX.Mac/Assets/img/artwork/a_384.png" height="72" alt="Rayquaza">
<img src="PKHeX.Mac/Assets/img/artwork/a_888.png" height="72" alt="Zacian">
<img src="PKHeX.Mac/Assets/img/artwork/a_898.png" height="72" alt="Calyrex">

# PKHeX for Mac

**A native macOS front-end for [PKHeX](https://github.com/kwsch/PKHeX).**

[![License](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-macOS%2012%2B%20(Apple%20Silicon)-lightgrey.svg)](#building)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Engine](https://img.shields.io/badge/engine-PKHeX.Core-brightgreen.svg)](https://github.com/kwsch/PKHeX)

</div>

---

## What this is

PKHeX is a Windows Forms application. This project keeps **PKHeX.Core untouched** — the save
parsing, legality engine, encounter tables, all of it — and puts an [Avalonia](https://avaloniaui.net)
interface on top of it that is built for macOS.

Upstream is a **git submodule pinned to a commit**, not a fork and not a vendored copy. None of
Kurt's code is duplicated here. Everything in this repository is the UI layer.

```
PKHeX.Mac/          the interface — views, view models, services
PKHeX.Mac.Tests/    177 tests covering the parts that aren't PKHeX.Core's job
upstream/PKHeX/     submodule → github.com/kwsch/PKHeX
scripts/            update upstream, sync sprites, package the .app
```

## What this isn't

Not a replacement for PKHeX, and not a competitor to it. If you're on Windows, use
[the real thing](https://github.com/kwsch/PKHeX) — it is more capable, more complete, and
better tested. This exists because the Windows Forms UI doesn't run natively on a Mac.

Feature coverage is a subset of upstream's. The engine is identical, so anything PKHeX.Core
can read, this can read; the gap is only in what the interface exposes.

## Highlights

- **Boxes and party** — drag to move, transfer between two open saves in different tabs
- **Trainer, bag, appearance** — with the per-game editors that apply to the loaded save
- **Pokédex, Tera Raids, Mystery Gift album, Hall of Fame, daycare, fusions**
- **Raw save block browser** for anything the UI doesn't surface yet
- **Legality** straight from PKHeX.Core, with a save-wide integrity audit that looks for
  duplicate identifiers across entries rather than checking one Pokémon at a time
- **Crown Tundra editor** for Sword/Shield — see below

### Rescuing a stuck Dynamax Adventure

Sword/Shield picks each Dynamax Adventure's rentals and encounters from a seed, and only
advances that seed when a run **finishes**. If a run can never finish, every retry replays the
identical encounters and the save is stuck permanently — with no in-game way out.

That is not hypothetical. It cost this project's author five months on the mandatory first
Max Lair run before the cause was found.

The Crown Tundra tab rolls a new seed, clears the Dynite Ore entry fee, and exposes the 48
once-per-save legendary capture flags and the Galarian Star Tournament partner flags. Most of
those blocks are unnamed in PKHeX; their keys were recovered by brute-forcing the FNV-1a-64
hash the game uses, and the derivations are pinned in tests against the keys PKHeX does
document, so a typo fails the build instead of writing to the wrong block in someone's save.

## Building

Requires [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) and an Apple Silicon Mac.

```sh
git clone --recurse-submodules https://github.com/jack-huffman/pkhex-mac.git
cd pkhex-mac
dotnet build PKHeX.Mac -c Debug
dotnet run  --project PKHeX.Mac
```

Build a distributable, self-contained bundle:

```sh
./scripts/package-app.sh            # → dist/PKHeX.app
./scripts/package-app.sh --install  # → also replaces /Applications/PKHeX.app
```

The bundle is ad-hoc signed rather than notarized, so macOS will quarantine it on a machine
that didn't build it. Clear that with `xattr -dr com.apple.quarantine /Applications/PKHeX.app`.

## Tracking upstream

```sh
./scripts/update-upstream.sh
```

Fetches the latest PKHeX master, prints the commit range and a link to the release notes,
re-syncs sprites, rebuilds, and runs the tests — rolling back with a printed command if either
step fails.

Compiled bindings are enabled, so a XAML binding pointing at something upstream renamed is a
build error (`AVLN2000`), not a silent runtime failure. What that can't catch is semantics
changing under an unchanged signature, which is why the script makes you look at the changelog.

## Credits

**[PKHeX](https://github.com/kwsch/PKHeX) by Kurt (@kwsch)** and its contributors. The entire
save-editing engine is theirs; this project only draws the windows.

Sprites come from upstream's collections — [pokesprite](https://github.com/msikma/pokesprite)
(MIT) and the [National Pokédex – Icon Dex](https://www.deviantart.com/pikafan2000/art/National-Pokedex-Version-Delta-Icon-Dex-824897934)
project. Optional high-resolution artwork is fetched at build time from
[PokeAPI/sprites](https://github.com/PokeAPI/sprites) and is not redistributed here.

## License

[GPL-3.0](LICENSE), inherited from PKHeX. UI © Jack Huffman; engine © Kurt (Kaphotics).

> We do not support or condone cheating at the expense of others. Do not use significantly
> hacked Pokémon in battle or in trades with those who are unaware hacked Pokémon are in use.
