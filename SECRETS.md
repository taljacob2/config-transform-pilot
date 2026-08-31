# Secrets and local setup

What has to be configured — in GitHub and on a developer's machine — for this repo to actually
work, and how to set each piece up from scratch. This was previously only explained ad hoc in
chat; this file is the durable version.

## GitHub Actions secrets and variables

Settings → Secrets and variables → Actions, on this repo
(`https://github.com/taljacob2/config-transform-pilot/settings/secrets/actions`) — secrets and
variables are two tabs on that same page. Both are consumed by
`.github/workflows/build-transformed.yml` only — `build.yml` (the one that runs on every
push/PR) needs neither, by design (`CONFIG_MANAGEMENT.md` §8.1 in `config-transform`).

| Secret | Required? | Purpose |
|---|---|---|
| `GIT_CRYPT_KEY_BASE64` | Always | Unlocks `.configtransform/**` (git-crypt encrypted) so the workflow can read the manifests and overlays. |
| `GH_PACKAGES_TOKEN` | Only if `config-transform`'s GitHub Packages are private | Lets `dotnet tool restore` pull `ConfigTransform.Xml`/`.Json`. A workflow's own default `GITHUB_TOKEN` cannot read packages published under a *different* private repository, even one owned by the same account — confirmed the hard way in this repo; see `FINDINGS.md`. |

| Variable | Required? | Purpose |
|---|---|---|
| `CONFIGTRANSFORM_PACKAGES_SOURCE` | Always | The full NuGet v3 feed URL `dotnet tool restore` pulls `ConfigTransform.Xml`/`.Json` from. A **variable**, not a secret — it's a URL, not sensitive, and it's meant to be visible in the workflow run log. For this repo: `https://nuget.pkg.github.com/taljacob2/index.json`. |

Variables, not secrets, because nothing about a feed URL needs to be hidden — the opposite,
actually: it's useful to see which feed a run pulled from directly in the log. There is
deliberately **no default value** baked into the workflow or `nuget.config` for this one (no
`vars.X || 'https://nuget.pkg.github.com/taljacob2/index.json'` fallback) — a silent default
tied to one specific owner is exactly the kind of thing that's easy to copy into another repo
without noticing it's still pointing at someone else's feed. Setting it explicitly, always, costs
one repo variable and avoids that class of mistake entirely. See
`SECRETS_AND_LOCAL_SETUP.md` in `config-transform` §1 for the full reasoning, including how this
generalizes to a GitHub Enterprise Cloud with data residency (`*.ghe.com`) tenant, where the feed
host isn't just `github.com` with a different name — the URL shape itself changes
(`nuget.pkg.github.com` → `nuget.<subdomain>.ghe.com`, no `.pkg.` segment).

### `GIT_CRYPT_KEY_BASE64`

This is the git-crypt symmetric key for `.configtransform/**`, base64-encoded so it can live in
a single-line GitHub secret. It is **not** a GitHub token or credential — it's a raw encryption
key `git-crypt init` generates locally; anyone who has it can decrypt every file under
`.configtransform/` in this repo, forever, so treat it exactly like a password: never commit it,
never paste it anywhere but a secret store, and store a backup somewhere durable (a team
password manager/vault) before it exists in only one place. See `CONFIG_MANAGEMENT.md` §7 in
`config-transform` for the full design reasoning (why git-crypt, why a single symmetric key,
what happens if it's lost — losing it with no backup makes `.configtransform/**` permanently
unrecoverable, by design, not a bug).

**If you're picking up an existing repo (this one) and need the key someone else generated:**
ask whoever set it up to hand it to you out of band (password manager, Vault, 1Password, a
secure paste service with expiry — never Slack/email in plaintext, never a commit, never a PR
comment). Once you have the raw keyfile, skip to "Local developer setup" below.

**If you're setting this up fresh, in a new repo that doesn't have a key yet.** The `git-crypt
init`/`export-key` steps are identical on every platform; only the base64-encode and cleanup
commands differ.

```bash
# Run once, in the repo root, before anything under .configtransform/ is committed:
git-crypt init
echo ".configtransform/** filter=git-crypt diff=git-crypt" >> .gitattributes
git add .gitattributes
git commit -m "Add git-crypt attributes for .configtransform/"

# git-crypt init just generated a new random AES-256 key, stored in .git/git-crypt/
# (never committed — .git/ isn't tracked). Export it to a real file so it can be
# distributed and added as a CI secret:
git-crypt export-key ./git-crypt-key
```

Then base64-encode it for the GitHub secret (a secret value is a single line of text):

- **Linux:** `base64 -w0 ./git-crypt-key > ./git-crypt-key.b64`
- **macOS:** `base64 -i ./git-crypt-key -o ./git-crypt-key.b64` (BSD `base64` has no `-w`; it
  already wraps output at 76 columns by default, which is fine for pasting into a GitHub secret
  field — but if you want a guaranteed single line, `base64 -i ./git-crypt-key | tr -d '\n' >
  ./git-crypt-key.b64` instead)
- **Windows (PowerShell)**, no separate `base64` binary needed:
  ```powershell
  [Convert]::ToBase64String([IO.File]::ReadAllBytes("./git-crypt-key")) | Set-Content -NoNewline ./git-crypt-key.b64
  ```
- **Windows, inside Git Bash** (ships with Git for Windows): the Linux command above works
  unchanged — Git Bash includes a real `base64` binary.

Paste the contents of `git-crypt-key.b64` as the `GIT_CRYPT_KEY_BASE64` secret's value. Then get
`./git-crypt-key` itself into a password manager/vault immediately, and delete the loose files
from disk:

- **Linux/macOS/Git Bash:** `rm ./git-crypt-key ./git-crypt-key.b64`
- **Windows (PowerShell):** `Remove-Item ./git-crypt-key, ./git-crypt-key.b64`

If any config files already exist under `.configtransform/` before you run `git-crypt init`,
follow with the migration step (`CONFIG_MANAGEMENT.md` §7.2 in `config-transform`):
```bash
git add --renormalize .
git commit -m "Encrypt .configtransform/ with git-crypt"
git push
```
This encrypts everything under `.configtransform/**` from that commit forward — it does not
retroactively scrub plaintext from earlier commits' history (a separate, real-world concern;
see that same doc's §7.4 if that ever applies to a repo with actual secrets in its history).

### `GH_PACKAGES_TOKEN`

First check whether you actually need it: open
`https://github.com/taljacob2?tab=packages` and look at the `ConfigTransform.Xml`/
`ConfigTransform.Json` package listings.

- **Shows "Private"** → you need this secret. The workflow's own `GITHUB_TOKEN` can only read
  packages published from *this* repository — not from `config-transform`, a different private
  repo, even though both are owned by the same account.
- **Shows "Public"** → skip this secret entirely. `build-transformed.yml` falls back to the
  workflow's own `GITHUB_TOKEN` automatically (`secrets.GH_PACKAGES_TOKEN || secrets.GITHUB_TOKEN`).

To create the token: `https://github.com/settings/tokens/new` → check only the `read:packages`
scope (nothing else — this token should not be able to do anything but read packages) → set an
expiry → Generate → paste the value as the `GH_PACKAGES_TOKEN` secret here.

(Both URLs above are `github.com` because that's where `config-transform` and this pilot both
actually live. If you're copying this doc as a template for a repo on a different GitHub host —
including a `*.ghe.com` data-residency tenant — swap `github.com` for that host in both places;
see `SECRETS_AND_LOCAL_SETUP.md` in `config-transform` §1 for what else changes, and in
particular the cross-host caveat if the packages feed and the consuming repo don't live on the
same host.)

## Local developer setup

For the full step-by-step checklist — install git-crypt, get a PAT, set env vars, unlock,
restore, run a first command — see `config-transform`'s
[`docs/ONBOARDING.md`](https://github.com/taljacob2/config-transform/blob/main/docs/ONBOARDING.md),
which is generic across any repo that consumes `config-transform`. This section only has what's
specific to *this* repo — the values `ONBOARDING.md`'s "Before you start" asks for:

- **This repo does use git-crypt** for `.configtransform/**`. Ask whoever set it up for the key
  (see `GIT_CRYPT_KEY_BASE64` above).
- **Feed URL:** `https://nuget.pkg.github.com/taljacob2/index.json`
- **Example manifest to try first:** `.configtransform/OrderProcessor.Framework/manifest.json`
- **Example first command** (once set up):
  ```
  dotnet tool run configtransform-xml -- --manifest .configtransform/OrderProcessor.Framework/manifest.json --file App.config --client Acme --environment Production --diff
  ```
