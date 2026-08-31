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
  (`apps/billing/BillingApi.Core/`) — all resolve correctly via each manifest's explicit
  `project` path. Nothing in the tool or the manifest schema assumed a common root.
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
- **"Missing overlay ≠ error" holds for a client with *zero* overlays anywhere.** Initech has no
  `Clients/Initech/` directory at all, for any of the three projects — not just a missing file
  within an existing directory. `build-transformed.yml` for `Initech`/`Staging` resolved cleanly
  using only base + Environments layers, no errors, no special-casing needed.
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
  resolve-per-manifest → validate-well-formed, artifact upload. `build.yml` (§8.1) correctly
  needs none of git-crypt, the tool, or secrets — confirmed by the fact PRs/pushes never touch
  any of that.

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
