# Pilot findings

What this synthetic pilot actually validated against `config-transform`'s design
(`config-transform`'s `docs/CONFIG_MANAGEMENT.md`, especially §11's open-items list), what it
found and fixed, and what it deliberately leaves open for a pilot against a real solution repo.

## What was validated end to end

All three via real GitHub Actions runs, not local reasoning — `build.yml` (every push, base
config only) and `build-transformed.yml` (manual dispatch, full resolve-and-validate pipeline)
against real client/environment combinations:

- **No `src/` assumption holds in practice.** Three projects at genuinely different nesting —
  flat (`OrderProcessor.Framework/`), nested once (`Web/AdminPortal.Web/`), nested twice
  (`apps/billing/BillingApi.Core/`) — all resolve correctly via each resource's own
  repo-root-relative `path` (originally each manifest's explicit `directory` path — see the
  `0.7.0-alpha2` migration section below for how this carried forward). Nothing in the tool or
  the layer schema assumed a common root.
- **The manifest's `directory` field (its `0.7.0-alpha`-era successor is `resources[].path`, see
  below) had no `.csproj`/.NET coupling at all — confirmed by using it that way, not just by
  reading the code.** Raised as a question during this pilot: since the field (originally called
  `project`) was never actually opened or parsed by the tool, is it really just a generic
  directory anchor rather than something `.csproj`-specific? Yes — and `config-transform` was
  refactored to match: the field was renamed `directory`, pointed directly at the directory
  itself (no more fake `.csproj` filename required), and `MANIFEST_SCHEMA.md` stated explicitly
  that this works for a Node.js/Angular/React/Flutter project's JSON config, not just a
  `.csproj`-anchored one. This pilot's four manifests (all `.NET`, since that's what this
  pilot's projects are) were updated to the new schema as part of upgrading to `0.2.0-alpha` — a
  real, if small, proof that the migration is mechanical: drop the fake filename from
  `directory`'s value, rename `relativeToProject` to `relativeToDirectory`, done. This claim's
  successor, `resources[].path` (a path straight to the real config file, no separate directory
  anchor at all), is `CLAUDE.md`'s own "No coupling to any language, ecosystem, or
  `TargetFramework`" bullet as of `0.7.0-alpha` — kept here as history, not superseded reasoning.
- **All three config formats work identically through the same pipeline shape**: App.config,
  Web.config (including `system.web`/`system.webServer`/`<location>`-wrapped XDT transforms —
  not just flat `appSettings`), and appsettings.json.
- **`ConfigTransform.Xml`/`.Json` have no `TargetFramework` coupling to the project a config
  file belongs to.** Raised as a real concern: the actual multi-client repos this design
  targets may have projects on genuinely old TFMs (net35/net40/net45/net47/net472), not just
  net48. Added `LegacyGateway.Framework` — deliberately vanilla net35 (no `Nullable`, no
  `LangVersion` override, classic `class Program { static void Main() }` shape) — and confirmed
  both that it builds cleanly cross-platform via the plain SDK-style `dotnet build` (no legacy
  SDK/targeting-pack setup needed — `Microsoft.NETFramework.ReferenceAssemblies` covers it
  automatically, the same mechanism that already made net48 buildable on `ubuntu-latest`
  earlier in this pilot) and that its App.config resolves through the identical layering as
  every other project's, encoding declaration and all. The earlier
  `<LangVersion>latest</LangVersion>` fix on `OrderProcessor.Framework`/`AdminPortal.Web` was
  needed only because *those* demo projects opted into `Nullable` — a choice specific to them,
  not something old TFMs require or old TFMs' config files need from the tool.
- **"Missing overlay ≠ error" holds for a client with *zero* overlays anywhere.** Under the
  original manifest-based schema, Initech had no `Clients/Initech/` directory at all, for any of
  the three projects — not just a missing file within an existing directory.
  `build-transformed.yml` for `Initech`/`Staging` resolved cleanly using only base + Environments
  layers, no errors, no special-casing needed. **This exact claim inverted under the
  `0.7.0-alpha` self-describing-overlays schema** — see the accepted-cost finding in the
  migration section below: Initech now needs two layer files that declare *nothing*, specifically
  so the old "missing directory falls through to Environments" behavior still holds.
- **Partial, per-project coverage works.** Globex has overlays for `AdminPortal.Web` in both
  environments but only `Production` for the other two projects — confirmed via a real run that
  `Globex`/`Staging` correctly fell back to base+Environments for `OrderProcessor.Framework` and
  `BillingApi.Core` while still applying Globex's `AdminPortal.Web` override.
- **Client overrides win over environment overrides, and XDT `Insert` composes correctly with
  existing base content** (not just `SetAttributes` replacing a value): Acme's Production
  `Web.config` shows `<allow users="acme-admin" />` inserted *alongside* the base's untouched
  `<deny users="?" />`, exactly as XDT is supposed to compose them.
- **git-crypt at rest works as documented** (`CONFIG_MANAGEMENT.md` §7): `.configtransform/**`
  shows as opaque binary in GitHub's web UI (confirmed directly via the GitHub API, not just
  assumed), unlocks correctly in CI via a base64 repo secret, and round-trips locally
  (`git-crypt lock`/`unlock`).
- **The GitHub Packages feed + local-tool-manifest flow works for a private feed**, including
  the one real wrinkle: the workflow's own default `GITHUB_TOKEN` cannot read packages
  published under a *different* private repository owned by the same account — a
  `GH_PACKAGES_TOKEN` PAT with `read:packages`, added as a second repo secret, was required.
  This is worth folding back into `CONFIG_MANAGEMENT.md`'s feed-authentication section as a
  concrete requirement, not just "a token is needed."
- **`build-transformed.yml`'s shape from §8.2 works as specified**: `workflow_dispatch`-only
  trigger, `client`/`environment` as required inputs, git-crypt unlock → tool restore → build →
  resolve-per-resource → validate-well-formed, artifact upload. `build.yml` (§8.1) correctly
  needs none of git-crypt, the tool, or secrets — confirmed by the fact PRs/pushes never touch
  any of that.

## A design gap found while thinking ahead to GitHub Enterprise Cloud tenants

Not a bug this pilot's CI hit — a gap noticed while designing for `*.ghe.com` (GitHub Enterprise
Cloud with data residency) support, since the actual multi-client repos this architecture targets
may not all live on plain `github.com`. Two things confirmed against GitHub's own documentation,
not assumed:

- The NuGet feed URL isn't a hostname substitution across hosts. `github.com`'s
  `nuget.pkg.github.com` becomes `nuget.<subdomain>.ghe.com` on a `ghe.com` tenant — the `.pkg.`
  segment is simply absent there. A template that swaps only the host produces
  `nuget.pkg.<subdomain>.ghe.com`, which doesn't exist.
- This repo's own `nuget.config` had `taljacob2`'s feed URL hardcoded directly in the committed
  file — harmless here since this pilot really is `taljacob2`'s, but exactly the kind of thing
  that shouldn't appear as a "default" in a *template* doc meant for other repos to copy, since a
  repo that forgot to override it would silently restore from the wrong account's feed instead of
  failing loudly.

Fixed by adding a `CONFIGTRANSFORM_PACKAGES_SOURCE` repository **variable** (not secret — a feed
URL isn't sensitive), read by `nuget.config` through the same `%VAR%` expansion already used for
credentials, with the value now unset in this repo's own committed files entirely — it lives only
in this repo's Settings → Secrets and variables → Actions → Variables tab. See
`config-transform`'s `docs/SECRETS_AND_LOCAL_SETUP.md` §1 for the full design and the cross-host
caveat (Actions egress allowlists, PAT-must-be-minted-on-the-serving-host) that applies when the
consuming repo and the packages-publishing repo live on different GitHub hosts.

**Confirmed end to end on `github.com`** (`build-transformed.yml` run #18) — with one real bug
caught along the way, not anticipated in the initial design: the env vars were first wired onto
only the `dotnet tool restore` step, and the very first run against them failed with `NU1301`
(`dotnet build`'s implicit restore for `OrderProcessor.Framework.csproj` — a project with *no*
dependency on this feed — still enumerates every configured source, so
`%CONFIGTRANSFORM_PACKAGES_SOURCE%` needs to be expandable on every step that runs
`dotnet build`/`dotnet restore`, not just one). Fixed by moving all three env vars
(`GITHUB_ACTOR`/`GITHUB_TOKEN`/`CONFIGTRANSFORM_PACKAGES_SOURCE`) to the job's own `env:` block;
run #18 with that fix succeeded. `config-transform`'s `docs/SECRETS_AND_LOCAL_SETUP.md` §1 was
corrected to show job-level scoping from the start.

**Still not exercised for real**: this pilot itself still lives entirely on `github.com` —
nothing here has actually run against a `ghe.com` tenant. The variable indirection and its
job-level scoping are proven end to end on `github.com`; the `ghe.com` URL shape itself is
confirmed only by documentation, not by a real run against one.

**A second occurrence of the same bug, in the *other* workflow.** `build.yml` (the plain
per-push/PR build, §8.1) had never needed git-crypt, the tool, or any secret before, because it
never touched anything client/environment-specific -- just `dotnet restore`/`dotnet build` on
each project. But `nuget.config` is repo-wide: once its `packageSources` entry started reading
`%CONFIGTRANSFORM_PACKAGES_SOURCE%`, *every* `dotnet restore`/`dotnet build` in the repo needed
the same three env vars, not just the ones that actually consume `config-transform`'s own
package. `build.yml` had no `env:` block at all, so its very first push after the variable
migration failed identically to run #17 -- `NU1301: The local source '.../%CONFIGTRANSFORM_PACKAGES_SOURCE%' doesn't exist` -- on the first `dotnet restore` step. Fixed the same way: added
job-level `env:` (`GITHUB_ACTOR`/`GITHUB_TOKEN`/`CONFIGTRANSFORM_PACKAGES_SOURCE`) and
`permissions: contents: read, packages: read` to `build.yml`'s job, mirroring
`build-transformed.yml`.

**Generalized lesson, worth folding into `config-transform`'s own docs**: once `nuget.config`
parameterizes its `packageSources` value via `%VAR%`, the requirement to have that var expandable
is not scoped to "workflows that use `config-transform`'s CLI tools" -- it's scoped to *any*
workflow, anywhere in the repo, that runs `dotnet build`/`dotnet restore`/`dotnet tool restore`
on anything, because NuGet resolves `nuget.config` per-repo, not per-workflow. A repo adopting
this pattern needs to audit every workflow file that touches `dotnet`, not just the ones it
thinks of as "the config-transform ones."

## A real bug found and fixed upstream

`build-transformed.yml`'s first real run against a merged XML file (not `config-transform`'s
own in-memory unit tests) failed with `xml.etree.ElementTree.ParseError: encoding specified in
XML declaration is incorrect`. Root cause: `XmlLayerMerger.Merge` used a plain `StringWriter`,
whose `Encoding` property reports UTF-16 — so `XmlDocument.Save(writer)` wrote
`<?xml version="1.0" encoding="utf-16"?>` into the merged string, while `CliRunner`'s real-run
path (`--output`) persists that string via `File.WriteAllText`, which defaults to UTF-8. The
file that lands on disk declares one encoding and is actually another.

`config-transform`'s own test suite never caught this because every existing test asserts on
the merged result via `XDocument.Parse(string)` — parsing an already-decoded .NET string, which
ignores the declared encoding entirely. Only a real disk round-trip through a
standards-compliant parser exposes the mismatch — precisely the gap a pilot against something
more realistic than unit fixtures exists to find.

Fixed upstream in `config-transform` (`0.1.0-alpha2`, since `0.1.0-alpha`'s packages were
already published and can't be overwritten): a `StringWriter` subclass reporting
`Encoding.UTF8`, plus a new regression test that writes the merged result to a real temp file
and reloads it with `XDocument.Load(path)` (which *does* honor the declared encoding) instead of
`.Parse(string)`. See `config-transform`'s `docs/CHANGELOG.md` `[0.1.0-alpha2]` entry.

**Lesson for `config-transform` generally**: in-memory string assertions are not equivalent to
a disk round-trip for anything encoding-sensitive. Worth keeping in mind for the JSON side too,
though `JsonLayerMerger` doesn't have an analogous encoding-declaration mechanism to get wrong.

## Migrating to self-describing `configtransform.json` layers (`0.7.0-alpha2`)

`config-transform` replaced `manifest.json` and the fixed base→Environments→Clients rule with
self-describing `configtransform.json` layers (breaking, `0.7.0-alpha`). This pilot's own
migration off the old schema surfaced several findings worth carrying back upstream.

- **The migration is doable while git-crypt-locked, with no decryption at any point.** All 17
  overlay files under `.configtransform/` were relocated with `git mv` while genuinely locked (no
  key available in the migrating session) — git-crypt's clean/smudge filters only run at
  checkout/commit time, not on a rename of an already-encrypted working-tree blob, so every patch
  file's ciphertext moved unchanged (`git diff --cached --find-renames --stat` showed `R100` with
  zero content lines on every one). Only the eight new `configtransform.json` layer files needed
  authoring from scratch — pure structural metadata (which project each layer patches, and its
  `extends` chain), fully derivable from the old directory tree's names, carrying no secrets.
  Genuinely useful for a real repo whose migrating engineer may not hold the git-crypt key.
- **The "accepted cost" is real, and it fails silently, not loudly.** Initech needed two
  hand-written, `extends`-only, `"resources": []` layer files
  (`Clients/Initech/Production/configtransform.json`, `Clients/Initech/Staging/configtransform.json`)
  — under the old schema, Initech needed nothing on disk at all. Without these two files,
  `config-transform`'s `LayerChain.Build` breaks on the very first missing file in the chain and
  returns an *empty* chain — so Initech would resolve to raw *base* content, silently losing the
  Environment layer too, not just the (correctly absent) client override.
  `MANIFEST_SCHEMA.md` documents this as a deliberate design tradeoff, but a real repo hit it on
  its very first migration, which is the difference between a documented caveat and a
  demonstrated one — worth feeding back upstream as a candidate for a `--list`-style "which
  client/environment combinations have no layer file at all?" audit command, so this failure mode
  can be caught before a real deploy rather than by careful reading of the design doc.
- **Multi-resource mode (`--resource` omitted) emits only what the resolved layer *chain*
  lists — not everything that would resolve correctly with an explicit `--resource`.**
  `LegacyGateway.Framework/App.config` is deliberately patched only at `Environments/Production`
  and `Clients/Acme/Production`; no layer in any *Staging* chain lists it at all. An explicit
  `--resource legacy/LegacyGateway.Framework/App.config --client <any> --environment Staging`
  still resolves correctly to base-only content (`LayerChain.ResolveResource` reports "not
  listed, skipping" and falls through), but `--output <dir>` with `--resource` omitted never
  emits a file for it on any Staging dispatch, because `LayerChain.ResolveAllResources` only
  unions `resources[].path` values actually present in the chain. Both behaviors are correct per
  the design; the asymmetry is a real trap for anyone assuming multi-resource mode is a strict
  superset of what the per-resource calls would produce. `build-transformed.yml`'s new
  "Demonstrate multi-resource mode" step makes this directly observable in a CI log: compare
  `publish/LegacyGateway.Framework/` (present, from the explicit `--resource` step) against
  `publish-all/` (absent) on any Staging dispatch. Fixable per-layer, if ever wanted, by listing
  the resource with no `patch` in the relevant Environment layer — a supported shape
  (`ResolveResource` reports "listed with no patch, skipping") that would put it into the union.
  Deliberately not done here, to keep this asymmetry demonstrable rather than paper over it.
- **`--output <dir>` (multi-resource mode) can't produce `.exe.config`-style deploy naming.** It
  mirrors each resource's own repo-root-relative source path under the output directory
  (`publish-all/legacy/LegacyGateway.Framework/App.config`, not
  `LegacyGateway.Framework.exe.config` next to the built exe) — it can't rename a file. For a
  .NET Framework project's actual deploy path, per-resource `--resource ... --output <file>`
  invocations remain necessary; `build-transformed.yml` keeps its four explicit invocations for
  exactly this reason and treats the multi-resource step as a pure demonstration/audit tool
  alongside them, not a replacement.
- **Upstream release-process finding, adjacent to this migration**: `0.7.0-alpha`'s own
  `publish.yml` run pushed packages to GitHub Packages, then failed its own gated smoke test —
  which still invoked the removed `--manifest` flag, a script the `0.7.0-alpha` implementation
  itself missed updating — so no GitHub Release was ever created for that tag. Corrected as
  `0.7.0-alpha2` (script fixed, packages need no changes), which this repo is pinned to. See
  `config-transform`'s `docs/CHANGELOG.md` `[0.7.0-alpha2]` entry and `docs/ROADMAP.md`.
- **Still unexercised by this pilot**: `set --resource` against the new schema (nothing here has
  ever been authored via `set`, old schema or new), and the `--list --resource` tree-wide reverse
  lookup outside the new `build-transformed.yml` `--list` step.

## Migrating to the unified CLI (`0.8.0-alpha`)

`config-transform` merged `ConfigTransform.Xml`/`ConfigTransform.Json` (two separate dotnet
tools, `configtransform-xml`/`configtransform-json`) into one `ConfigTransform.Cli` tool
(`configtransform`), dispatching each resource to the right merge engine by its own file
extension (`config-transform`'s `docs/CHANGELOG.md` `[0.8.0-alpha]` entry). This pilot re-pinned
`.config/dotnet-tools.json` to the single tool and swapped `build-transformed.yml`'s five
invocations (four per-resource `--resource` calls plus `--list`) to the new command name.

- **Golden-output diff against a real pre-migration baseline, not just reasoning about the
  change.** Before touching anything, triggered `build-transformed.yml` on `main` (still pinned
  to `0.7.0-alpha2`, the two-tool CLI) for `Acme`/`Production` — GitHub Actions run
  [#20](https://github.com/taljacob2/config-transform-pilot/actions/runs/33881345182) — as a real
  baseline, since no run had actually happened against the current self-describing-overlays
  schema before this (the prior run predates that migration entirely). Then dispatched the same
  `Acme`/`Production` combination against the migrated branch — run
  [#21](https://github.com/taljacob2/config-transform-pilot/actions/runs/33881513970). All four
  resolved resources (`OrderProcessor.Framework/App.config`, `AdminPortal.Web/Web.config`,
  `BillingApi.Core/appsettings.json`, `LegacyGateway.Framework/App.config`) came back
  **byte-for-byte identical** between the two runs, and `--list`'s output (the full resolved
  layer chain, patch attribution, `extends`) was identical too. The migration is behavior-
  preserving for every already-supported invocation shape, confirmed against real CI output, not
  assumed from reading the source.
- **The real capability change, also confirmed via the same pair of runs.** The "Demonstrate
  multi-resource mode" step went from two separate tool invocations (`configtransform-xml` then
  `configtransform-json`, `--resource` omitted on each) to one `configtransform` call. On the
  `0.7.0-alpha2` baseline run, each of the two calls printed a stderr skip note (`Skipped 1
  resource(s) not in this tool's format (.config, .xml); run the matching tool for those.` and
  the JSON-format equivalent) before the *other* tool picked up the rest. On the `0.8.0-alpha`
  run, the single call wrote all four files with **zero** skip notes — the actual headline
  capability this version delivers, observed directly in a CI log rather than inferred.
- **Negative test: Initech/Staging (zero overlays anywhere) still resolves cleanly under the
  unified CLI.** Dispatched the same branch for `Initech`/`Staging` — run
  [#22](https://github.com/taljacob2/config-transform-pilot/actions/runs/33882117235). `--list`
  showed every resource as "not patched here — inherited from
  `.configtransform/Environments/Staging/configtransform.json`", no error — "missing overlay ≠
  error" (the `0.7.0-alpha2` migration's own accepted-cost finding, above) is unaffected by the
  CLI unification. This dispatch also re-confirms the multi-resource-mode asymmetry from that
  same finding under the new tool: `publish-all/` came back with exactly 3 files (
  `OrderProcessor.Framework/App.config`, `Web/AdminPortal.Web/Web.config`,
  `BillingApi.Core/appsettings.json`) — `legacy/LegacyGateway.Framework/App.config` is correctly
  absent, since no layer in any Staging chain lists it — while the explicit `--resource
  legacy/LegacyGateway.Framework/App.config` step earlier in the same run still resolved it
  successfully to base-only content. Same asymmetry, same root cause, unchanged by which CLI
  binary is doing the resolving.
- **Nothing else needed to change.** `--list`'s output shape, the layer-resolution semantics
  (`extends`, patch attribution, inheritance), and every already-authored `configtransform.json`/
  patch file are untouched — the migration is purely which tool name `build-transformed.yml`
  invokes and how many calls the multi-resource demo step makes.

## Re-pinning to `0.11.0-alpha` (skipping `0.9.0-alpha`/`0.10.0-alpha`)

This pilot's `.config/dotnet-tools.json` sat on `0.8.0-alpha` through two further upstream
releases before this re-pin: `0.9.0-alpha` (the `init` command, plus fixing `--client` to be
optional for a plain resolve — reported against this pilot's own published tool) and
`0.10.0-alpha` (a duplicate re-tag of `0.9.0-alpha`'s exact commit, no code changes — see
`config-transform`'s `docs/CHANGELOG.md`/`docs/ROADMAP.md` for that drift's full writeup).
`0.11.0-alpha` reworks `--list` and the single-resource resolution report for readability — also
reported against this pilot, working from a real `--list`/`--dry-run`/`--diff` session against
this exact repo's output.

- **Can't verify locally.** `.configtransform/**` is git-crypt-encrypted in this pilot
  (`.gitattributes`), and no session working on `config-transform` itself ever holds this
  pilot's key — the same constraint noted throughout this file. Verification instead means a
  real `build-transformed.yml` `workflow_dispatch` run, same as every prior re-pin here.
- **Dispatched `Acme`/`Production` against the re-pinned branch** — run
  [#23](https://github.com/taljacob2/config-transform-pilot/actions/runs/33949961438) — and read
  the actual job log rather than assuming the pin alone was enough.
- **`--list`'s new shape confirmed against real content, for every one of this layer's four
  resources**: `base` first (labeled `(always applied)`), then
  `.configtransform/Environments/Production/configtransform.json` and
  `.configtransform/Clients/Acme/Production/configtransform.json` in that real application order,
  connected by `↓`, each read `patched in` — column-aligned against the longest label on each
  resource block. No more target-layer-first `patched here`/`also patched in`.
- **The single-resource resolution report's new shape confirmed too**, for all four
  `--resource ... --output ...` invocations (`OrderProcessor.Framework/App.config`,
  `Web/AdminPortal.Web/Web.config`, `apps/billing/BillingApi.Core/appsettings.json`,
  `legacy/LegacyGateway.Framework/App.config`): `Resolving '<path>'` header, a `base` entry
  naming the resource's own path, `↓`, then each layer as a two-line entry (label line, then an
  indented `patched in: <patch path>` detail line) — every patch path repo-relative, e.g.
  `.configtransform/Clients/Acme/Production/patch-OrderProcessor.Framework-App.config.xml`, never
  the OS-absolute paths the old report printed.
- **Every other step in the same run still passed**: all four base projects built, all four
  merges wrote valid output (the well-formedness validation step is unchanged and green), and
  multi-resource mode (`--resource` omitted) still resolved both formats in one call with no
  skip note — this readability rework touches only what's printed to the console, never the
  merged file content itself, confirmed directly rather than assumed from the source diff.

## Deliberately not validated by this pilot

- **Real inventory against an actual solution repo.** This pilot's three projects, their config
  shapes, and the simulated client/environment matrix are all invented for coverage, not drawn
  from a real codebase. `CONFIG_MANAGEMENT.md` §11's "no real inventory has been done" item is
  *not* closed by this pilot — only a real pilot (necessarily in a separate session, against the
  actual employer-owned repo) can close it.
- **Deployment transport mechanism** (§8.3) — `build-transformed.yml` here stops at "resolve,
  validate, upload as a build artifact." It does not attempt WinRM, a self-hosted runner, or any
  actual delivery to a Windows Service or IIS site, since that requires real target
  infrastructure this pilot has none of.
- **git-crypt key rotation** — never exercised; the same key has been in place since `init`.
- **Per-client git-crypt key splitting** — not attempted; a single shared key covers all three
  simulated clients here.
- **YAML/`.env` format support** — no fixtures of either format exist in this pilot; still
  purely a design-doc claim, unexercised.
- **`launchSettings.json`/`dotnet user-secrets` accidental-secret check** — not applicable; this
  pilot has no real secrets to accidentally expose.

## Net assessment

The core claims in `CONFIG_MANAGEMENT.md` and `CONFIGTRANSFORM_TOOL_DESIGN.md` — layered
resolution order, no-`src/`-assumption, format-genericness, missing-overlay-is-not-an-error,
git-crypt-at-rest, the two-workflow CI split — all held up under a real (if synthetic) exercise
across three formats and varying repo shapes, and the exercise paid for itself by catching a
real correctness bug the tool's own test suite structurally could not have caught. What remains
open is specifically the parts that require a *real* repo's real content and real deployment
targets, which this synthetic pilot was never going to be able to validate by design.
