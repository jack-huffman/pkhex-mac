# Contributing

Thanks for looking. This file is the short version of how the project is put together and
what a change is expected to pass before it lands.

## Setup

- macOS on Apple Silicon and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Clone with submodules; the engine is a pinned PKHeX checkout, not a copy:

  ```sh
  git clone --recurse-submodules https://github.com/jack-huffman/pkhex-mac.git
  cd pkhex-mac
  dotnet build PKHeX.Mac.slnx
  dotnet test PKHeX.Mac.Tests
  dotnet run --project PKHeX.Mac
  ```

## What CI checks

Every push and pull request runs `.github/workflows/ci.yml` on a macOS runner:

1. `dotnet format --verify-no-changes` — formatting and style follow the root `.editorconfig`.
   Run `dotnet format PKHeX.Mac.slnx --exclude upstream` locally to fix anything it flags.
2. `dotnet build -c Release` — analyzers run at `latest-recommended` and **warnings are errors**.
   Unused usings, culture-sensitive formatting and the like fail the build rather than accrue.
3. `dotnet test` — the unit tests.

Tags matching `v*` also produce a `PKHeX.app` bundle as a workflow artifact.

## How the code is organised

```text
PKHeX.Mac/Services/     pure logic with no UI dependency beyond brushes and bitmaps.
                        Everything here is unit-testable and most of it is tested.
PKHeX.Mac/ViewModels/   one view model per editor; MainWindowViewModel is one session
                        (an open save), split into partial files by concern, and
                        WorkspaceViewModel owns the open sessions.
PKHeX.Mac/Views/        the window and its panels. Code-behind is limited to what
                        needs the window: pickers, clipboard, drag ghost, native menu.
PKHeX.Mac.Tests/        xUnit tests. Save-dependent tests build blank saves in memory
                        (Gen 5 round-trips reliably) rather than shipping fixtures.
upstream/PKHeX/         the engine, as a submodule. Never edited here.
```

A few conventions worth knowing before you add to it:

- **Every write to the save reports itself.** Editors take an `onChanged` callback and call it
  once per user action. Bulk commands suppress per-row notifications and report once; see
  `RaidsViewModel.Bulk` or `EventFlagsViewModel.ClearVisible` for the pattern. The counter this
  feeds is what decides whether closing the window warns the user, so a missed call is a bug.
- **Background work goes through `BackgroundRefresh`.** Copy what you need from the save on the
  UI thread (`SaveFile.EnumerateOccupiedSlots` returns copies), compute off it, and apply the
  outcome only if it was not superseded. `BoxInsightsViewModel` is the smallest example.
- **Names come from `GameStringsExtensions`.** `strings.SpeciesName(pk)` and friends never throw on
  an out-of-range id; do not index the string tables directly.
- **Colours that mean something live in `Palette`;** colours that are chrome live in `App.axaml`
  as theme resources with light and dark values.
- **Compiled bindings are on.** A binding to a renamed member is a build error, which is the
  point; keep `x:DataType` on every template.
- **Block keys are looked up by name**, through PKHeX's own hashing lookup, and pinned in tests.
  Do not paste key constants.

## Updating the engine

```sh
./scripts/update-upstream.sh
```

It moves the submodule to the latest PKHeX master, re-syncs sprites, rebuilds and runs the
tests, and prints the upstream changelog for you to read. Read it: a green build proves the
API still exists, not that it still means the same thing.

## Commits

Imperative mood, capitalised, no trailing period, one change per commit: `Store settings under
Application Support`. Explain the *why* in the body when it is not obvious from the diff.
