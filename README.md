# config-transform-pilot

A synthetic pilot solution repo for exercising
[`config-transform`](https://github.com/taljacob2/config-transform) end to end — manifest
resolution, layered overlays, git-crypt at rest, and the workflow-dispatch merged-output
pipeline — against something closer to a real multi-project solution than the tool's own unit
test fixtures.

This is **not** a real product. It exists to validate `config-transform`'s design against
repo-shape variety that unit tests can't exercise: multiple projects at different nesting
depths, no assumed `src/` layout, and three config formats side by side.

## Why this repo exists

`config-transform`'s own test suite proves the merge engines are correct in isolation. It
doesn't prove the manifest/CLI/CI wiring holds up in a solution with more than one project, or
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

## Status

- The three projects above build in CI on both Windows and Linux (`.github/workflows/build.yml`).
- `.configtransform/` manifests and overlays exist for all three projects, three clients
  (Acme, Globex, Initech) x two environments (Staging, Production), with deliberately partial
  coverage — Initech has no `Clients/` directory at all, for any project.
- `.configtransform/**` is encrypted at rest with git-crypt (`.gitattributes`); GitHub's web UI
  correctly shows these files as opaque binary blobs.
- `.github/workflows/build-transformed.yml` (manual `workflow_dispatch`, `client`/`environment`
  inputs) builds all three projects, resolves each project's config file for the requested
  target, validates the merged output is well-formed, and uploads it as a build artifact — per
  `CONFIG_MANAGEMENT.md` §8.2.
- `build-transformed.yml` has run successfully end to end for `Acme`/`Production`,
  `Globex`/`Staging`, and `Initech`/`Staging` — real client overrides, environment-only
  fallback, and "no client overlay directory at all" all confirmed correct. Along the way it
  caught a real bug in `config-transform` itself (a UTF-8/UTF-16 XML-declaration mismatch),
  now fixed upstream in `0.1.0-alpha2` and pinned here. See `FINDINGS.md` for the full writeup.
