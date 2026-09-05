# config-transform-pilot

A synthetic pilot solution repo for exercising
[`config-transform`](https://github.com/taljacob2/config-transform) end to end — self-describing
`configtransform.json` layer resolution, layered overlays, git-crypt at rest, and the
workflow-dispatch merged-output pipeline — against something closer to a real multi-project
solution than the tool's own unit test fixtures.

This is **not** a real product. It exists to validate `config-transform`'s design against
repo-shape variety that unit tests can't exercise: multiple projects at different nesting
depths, no assumed `src/` layout, and three config formats side by side.

## Why this repo exists

`config-transform`'s own test suite proves the merge engines are correct in isolation. It
doesn't prove the layer/CLI/CI wiring holds up in a solution with more than one project, or
that "don't assume a `src/` folder" is actually true rather than just written down. This repo
is that proof, kept fully synthetic and disconnected from any real organization's
infrastructure or secrets — see `docs/ROADMAP.md` in `config-transform` for why a real-world
pilot has to happen in a separate, employer-owned session instead.

## Projects

| Project | Config format | Path (deliberately varied nesting) | Target |
|---|---|---|---|
| `OrderProcessor.Framework` | `App.config` | repo root (flat) | net48 |
| `AdminPortal.Web` | `Web.config` | `Web/AdminPortal.Web/` (nested once) | net48 |
| `BillingApi.Core` | `appsettings.json` | `apps/billing/BillingApi.Core/` (nested twice) | net8.0 |
| `LegacyGateway.Framework` | `App.config` | `legacy/LegacyGateway.Framework/` | net35 — proves the tool has no `TargetFramework` coupling |

None of these are realistic, fully-featured applications — `AdminPortal.Web` in particular is
a plain class library standing in for an ASP.NET-hosted web app (no `System.Web`/IIS hosting
involved), since actually hosting it adds build complexity this pilot doesn't need. What matters
for the pilot is the *shape* of each config file (`appSettings`, `connectionStrings`,
`system.web`/`system.webServer` for XML; nested JSON objects and arrays for JSON) and the
project's location in the repo tree — not that the app itself does anything real.

## Simulated clients and environments

Three clients (`Acme`, `Globex`, `Initech`) × two environments (`Staging`, `Production`), with
deliberately partial overlay coverage — not every client overrides every setting, and not
every project has overlays for every client, to prove that "missing overlay ≠ error" holds in
practice, not just in `config-transform`'s own unit tests.

## Overlay tree

Under the old schema the tree structure itself was self-explanatory from directory names. Under
the new schema the `configtransform.json` files that declare what each layer patches are
git-crypt-encrypted at rest along with everything else under `.configtransform/**` — so this
plaintext rendering of the layout is the map:

```
.configtransform/
├── Environments/
│   ├── Production/    -- all 4 projects
│   └── Staging/        -- OrderProcessor, AdminPortal, BillingApi (not LegacyGateway)
└── Clients/
    ├── Acme/
    │   ├── Production/  -- all 4 projects (extends Environments/Production)
    │   └── Staging/       -- OrderProcessor, BillingApi (extends Environments/Staging)
    ├── Globex/
    │   ├── Production/  -- OrderProcessor, AdminPortal, BillingApi (extends Environments/Production)
    │   └── Staging/       -- AdminPortal only (extends Environments/Staging)
    └── Initech/
        ├── Production/  -- extends Environments/Production, no overlays of its own
        └── Staging/       -- extends Environments/Staging, no overlays of its own
```

`LegacyGateway.Framework` is deliberately patched only at `Environments/Production` and
`Clients/Acme/Production` — every other client/environment combination resolves it to base-only,
and no layer in any Staging chain lists it at all (see the multi-resource-mode finding in
`FINDINGS.md`).

## Setup

Running anything here beyond reading the (encrypted) source requires two GitHub Actions secrets,
one repository variable, and, for local work, git-crypt and a NuGet feed credential. See
`SECRETS.md` for the full setup — what each secret/variable is for, how to generate the git-crypt
key from scratch versus obtaining an existing one, and how to unlock and run the tools on a local
machine.

The `CONFIGTRANSFORM_PACKAGES_SOURCE` repository variable (set to
`https://nuget.pkg.github.com/taljacob2/index.json`) replaces what used to be a URL hardcoded
directly in `nuget.config` — `SECRETS.md` and `FINDINGS.md` explain why, including a real
job-level-scoping bug this change caught in its own first CI run.

## Status

- The four projects above build in CI on both Windows and Linux (`.github/workflows/build.yml`).
- `.configtransform/` holds one `configtransform.json` per layer directory — 2 `Environments/`
  layers and 6 `Clients/<Client>/<Env>/` layers, each pairing a project's own repo-root-relative
  path with an optional `patch` file — covering all four projects across three clients (Acme,
  Globex, Initech) x two environments (Staging, Production), with deliberately partial coverage.
  See "Overlay tree" below for the full layout.
- **Every client/environment combination needs a layer file on disk, even with nothing to
  override.** `Initech` has zero overlays for any project, but its two layer files
  (`Clients/Initech/Production/configtransform.json`, `Clients/Initech/Staging/configtransform.json`)
  still exist, each `extends`-only with `"resources": []` — the opposite of the old schema, where
  Initech needed no `Clients/` directory at all. Without these two files,
  `config-transform`'s `LayerChain.Build` breaks on the first missing file in the chain and
  Initech would silently fall back to *raw base* content, losing the Environment layer too, not
  just the client override. This is `config-transform`'s own documented "accepted cost" of the
  new design — see `FINDINGS.md` for how this pilot hit it directly during migration.
- `.configtransform/**` is encrypted at rest with git-crypt (`.gitattributes`); GitHub's web UI
  correctly shows these files as opaque binary blobs.
- `.github/workflows/build-transformed.yml` (manual `workflow_dispatch`, `client`/`environment`
  inputs) builds all four projects, resolves each project's config file for the requested
  target, validates the merged output is well-formed, and uploads it as a build artifact — per
  `CONFIG_MANAGEMENT.md` §8.2.
- `build-transformed.yml` has run successfully end to end for `Acme`/`Production`,
  `Globex`/`Staging`, and `Initech`/`Staging` — real client overrides, environment-only
  fallback, and "no client overlay directory at all" all confirmed correct. Along the way it
  caught a real bug in `config-transform` itself (a UTF-8/UTF-16 XML-declaration mismatch), fixed
  upstream in `0.1.0-alpha2`. This repo is now pinned to `0.2.0-alpha` (also verified end to end
  across all three client/environment combinations), which additionally renamed the manifest
  schema's `project`/`relativeToProject` fields to `directory`/`relativeToDirectory`. See
  `FINDINGS.md` for the full writeup of both.
- Confirmed the tool has no `TargetFramework` coupling to the projects whose config files it
  resolves: `LegacyGateway.Framework` (net35, deliberately vanilla — no `Nullable`, no
  `LangVersion` override) builds cleanly and resolves its App.config identically to every net48
  project here.
- The NuGet feed URL is no longer hardcoded in `nuget.config` — it's read from a
  `CONFIGTRANSFORM_PACKAGES_SOURCE` repository variable instead, generalizing the pattern for
  GitHub Enterprise Cloud (`*.ghe.com`) tenants documented in `config-transform`'s
  `SECRETS_AND_LOCAL_SETUP.md`. Confirmed end to end for `Acme`/`Production` after fixing a real
  bug the change's own first CI run caught (the env vars need to be job-level, not scoped to a
  single step) — see `FINDINGS.md`. The same bug then hit `build.yml` too (it had never needed
  these secrets before `nuget.config` started referencing the variable repo-wide) and was fixed
  the same way.
- This repo was pinned to `0.3.0-alpha`, which gives a locked-but-not-yet-`git-crypt unlock`ed
  manifest an actionable error (naming the file and telling you to run `git-crypt unlock`)
  instead of a raw, confusing JSON parse failure — found via a real local run against this repo.
  See `config-transform`'s `docs/CHANGELOG.md` `[0.3.0-alpha]` section.
- This repo was pinned to `0.4.0-alpha`, which adds `--list`: prints a manifest's file entries
  and which `Environments`/`Clients` overlays actually exist on disk, without needing
  `--client`/`--environment`/`--output` — e.g. (pre-`0.7.0-alpha` CLI, no longer runs as written)
  `dotnet tool run configtransform-xml -- --manifest .configtransform/OrderProcessor.Framework/manifest.json --list`.
  See `config-transform`'s `docs/CHANGELOG.md` `[0.4.0-alpha]` section.
- This repo was pinned to `0.5.0-alpha`, which makes `--manifest`/`-m` optional (auto-discovered
  when exactly one `.configtransform/*/manifest.json` exists — not this repo's own layout, which
  has four) and adds short flag aliases (`-m`/`-f`/`-c`/`-e`/`-o`) for less typing interactively —
  e.g. (pre-`0.7.0-alpha` CLI, no longer runs as written)
  `dotnet tool run configtransform-xml -- -m .configtransform/OrderProcessor.Framework/manifest.json -c Globex -e Production --diff`.
  See `config-transform`'s `docs/CHANGELOG.md` `[0.5.0-alpha]` section. Verified via a real
  `build-transformed.yml` `workflow_dispatch` run for `Globex`/`Production` against the published
  `0.5.0-alpha` package.
- Added `config-transform-pilot.sln` at the repo root, referencing all four projects at their
  real (varying-depth) paths. `dotnet build`/`dotnet test`/opening in an IDE now work from the
  repo root without `cd`-ing into each project's folder first — and staying at the repo root is
  what `config-transform`'s CLI itself already assumes (§"Setup" above), so this also removes a
  real trap, more relevant now than when this was first written, not less: every path a
  `configtransform.json` layer declares (`extends`, `resources[].path`, `resources[].patch`) —
  and `--resource` itself — resolves against the current working directory as the repo root, so
  running the tool from inside a project folder resolves everything relative to the wrong place
  and fails with a confusing "not found" error. `build.yml` now builds via the `.sln` too
  (`dotnet restore`/`build config-transform-pilot.sln`, two commands instead of the previous
  eight, one restore+build pair per project) — which also means CI actually verifies the `.sln`
  itself stays valid, not just each project individually.
- `SECRETS.md`'s "Local developer setup" section is now just this repo's specific values (feed
  URL, example manifest/command) — the step-by-step checklist itself moved to
  `config-transform`'s new `docs/ONBOARDING.md`, generic across any repo that consumes the tool,
  so it isn't duplicated here and in `config-transform`'s own `SECRETS_AND_LOCAL_SETUP.md`.
- **Migrated off `manifest.json` entirely, now pinned to `0.7.0-alpha2`** — `config-transform`
  replaced `manifest.json` and the fixed base→Environments→Clients rule with self-describing
  `configtransform.json` layers (breaking, see `config-transform`'s `docs/CHANGELOG.md`
  `[0.7.0-alpha]`/`[0.7.0-alpha2]` sections). `.configtransform/`'s tree was restructured (see
  "Overlay tree" below), `build-transformed.yml`'s four CLI invocations moved from
  `--manifest`/`--file` to `--resource`, and a new step demonstrates the new multi-resource mode
  (`--resource` omitted, `--output` as a directory) into a scratch `publish-all/` folder. The
  relocation itself needed no git-crypt decryption — every patch file kept its exact
  git-crypt-encrypted content across the move (`git mv` preserves the ciphertext blob, since the
  filter only runs at checkout/smudge time); only the eight new `configtransform.json` layer
  files, which carry no secrets, were authored from scratch. See `FINDINGS.md` for the full
  migration writeup, including the Initech accepted-cost finding and the multi-resource-mode
  gap this pilot's new demo step surfaces.
- **Migrated off the two-tool CLI, now pinned to `0.8.0-alpha`** — `config-transform` merged
  `ConfigTransform.Xml`/`ConfigTransform.Json` (`configtransform-xml`/`configtransform-json`)
  into one `ConfigTransform.Cli` tool (`configtransform`), dispatching each resource to the right
  engine by its own extension (see `config-transform`'s `docs/CHANGELOG.md` `[0.8.0-alpha]`
  entry). `.config/dotnet-tools.json` now pins a single `configtransform.cli` entry;
  `build-transformed.yml`'s four per-resource `--resource` invocations and its `--list` step all
  call `configtransform` instead of the two retired command names. The "Demonstrate
  multi-resource mode" step collapses from two separate tool invocations (one per format) into
  one `configtransform` call with `--resource` omitted — the actual headline capability this
  version delivers: both `AdminPortal.Web/Web.config` (XML) and `BillingApi.Core/appsettings.json`
  (JSON) resolve together in a single call, no per-format skip note. See `FINDINGS.md` for the
  golden-output diff against a real pre-migration baseline run.
- **Re-pinned from `0.8.0-alpha` to `0.11.0-alpha`** — three upstream releases at once, since this
  pilot skipped re-pinning for the middle ones: `0.9.0-alpha` (the `init` command, plus fixing
  `--client` to be optional for a plain resolve — reported against this very pilot's published
  tool), `0.10.0-alpha` (a duplicate re-tag of `0.9.0-alpha` with no code changes — see
  `config-transform`'s `docs/CHANGELOG.md`/`docs/ROADMAP.md` for that drift note), and
  `0.11.0-alpha` (a `--list`/single-resource resolution-report readability rework, also reported
  against this pilot: the chain now prints base→arrow→layer in real application order with
  uniform `patched in`/`not patched in` wording instead of target-layer-first `patched here`/
  `also patched in`, and every path is repo-relative and never omitted). See `FINDINGS.md` for
  verification against a real `build-transformed.yml` dispatch.
- **Re-pinned from `0.11.0-alpha` to `0.12.0-alpha`** — three more CLI usability fixes, all
  reported against this pilot's published tool: a trailing bare `help` after other flags now
  short-circuits to the help page instead of throwing `Unrecognized argument: 'help'.`; every CLI
  validation error now ends with a one-line `Try:` example (e.g. a missing `--output` suggests
  adding it or using `--dry-run`/`--diff`); and a mistyped flag close to a real one (e.g.
  `--otuput`) now gets `Try: did you mean --output?` instead of a generic pointer to `--help`. All
  three are CLI-argument-parsing behavior, invisible to this pilot's own pipeline (which never
  passes a malformed flag) — re-verified via a real `build-transformed.yml` dispatch anyway, since
  every re-pin here is confirmed against real CI content rather than assumed from the changelog.
  See `FINDINGS.md` for the run and for a real drift this release hit: the tag was cut before
  `config-transform`'s own CHANGELOG-versioning PR had merged, leaving `0.12.0-alpha`'s GitHub
  Release with an empty body even though the package itself is correct.
- **Re-pinned from `0.12.0-alpha` to `0.13.0-alpha`** — three more real-user-reported fixes:
  `init`'s scan no longer suggests `.config/dotnet-tools.json`/`nuget.config` as candidate
  resources; omitting `--resource` with `--output` colliding against an existing file now fails
  fast with a clear `Try:` hint instead of a raw `IOException`; and `--diff` no longer prints
  git's own file-identity header lines naming meaningless OS temp file paths. Re-verified via a
  real `build-transformed.yml` dispatch — see `FINDINGS.md`, which also notes this tag went out
  clean (real, complete GitHub Release notes), unlike `0.12.0-alpha`'s drift above.
