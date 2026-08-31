# Secrets and local setup

What has to be configured — in GitHub and on a developer's machine — for this repo to actually
work, and how to set each piece up from scratch. This was previously only explained ad hoc in
chat; this file is the durable version.

## GitHub Actions secrets

Settings → Secrets and variables → Actions, on this repo
(`https://github.com/taljacob2/config-transform-pilot/settings/secrets/actions`). Both are
consumed by `.github/workflows/build-transformed.yml` only — `build.yml` (the one that runs on
every push/PR) needs neither, by design (`CONFIG_MANAGEMENT.md` §8.1 in `config-transform`).

| Secret | Required? | Purpose |
|---|---|---|
| `GIT_CRYPT_KEY_BASE64` | Always | Unlocks `.configtransform/**` (git-crypt encrypted) so the workflow can read the manifests and overlays. |
| `GH_PACKAGES_TOKEN` | Only if `config-transform`'s GitHub Packages are private | Lets `dotnet tool restore` pull `ConfigTransform.Xml`/`.Json`. A workflow's own default `GITHUB_TOKEN` cannot read packages published under a *different* private repository, even one owned by the same account — confirmed the hard way in this repo; see `FINDINGS.md`. |

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

**If you're setting this up fresh, in a new repo that doesn't have a key yet:**

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

# Base64-encode it for the GitHub secret (a secret value is a single line of text):
base64 -w0 ./git-crypt-key > ./git-crypt-key.b64   # macOS: base64 -i ./git-crypt-key -o ./git-crypt-key.b64

# Paste the contents of git-crypt-key.b64 as the GIT_CRYPT_KEY_BASE64 secret's value.

# Then get ./git-crypt-key itself into a password manager/vault immediately, and
# delete the loose files from disk:
rm ./git-crypt-key ./git-crypt-key.b64
```

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
`https://github.com/<owner>?tab=packages` and look at the `ConfigTransform.Xml`/
`ConfigTransform.Json` package listings.

- **Shows "Private"** → you need this secret. The workflow's own `GITHUB_TOKEN` can only read
  packages published from *this* repository — not from `config-transform`, a different private
  repo, even though both are owned by the same account.
- **Shows "Public"** → skip this secret entirely. `build-transformed.yml` falls back to the
  workflow's own `GITHUB_TOKEN` automatically (`secrets.GH_PACKAGES_TOKEN || secrets.GITHUB_TOKEN`).

To create the token: `https://github.com/settings/tokens/new` → check only the `read:packages`
scope (nothing else — this token should not be able to do anything but read packages) → set an
expiry → Generate → paste the value as the `GH_PACKAGES_TOKEN` secret here.

## Local developer setup

To run `dotnet tool run configtransform-xml`/`configtransform-json` locally against this repo's
real (decrypted) overlays — not just to read the encrypted blobs GitHub shows in its web UI:

1. **Install git-crypt.** `apt-get install git-crypt` (Debian/Ubuntu), `brew install git-crypt`
   (macOS), or see git-crypt's own install docs for other platforms.
2. **Clone this repo** normally. Everything under `.configtransform/` will show up as opaque
   binary — that's git-crypt working correctly, not a broken clone.
3. **Get the git-crypt key** from whoever holds it (password manager/vault entry — never via
   git, chat, or email in plaintext), saved locally as e.g. `~/keys/config-transform-pilot.key`.
4. **Unlock:**
   ```bash
   git-crypt unlock ~/keys/config-transform-pilot.key
   ```
   `.configtransform/**` is now readable/writable as plaintext in your working copy. `git-crypt
   lock` re-encrypts it locally if you want to double check the round-trip, or before leaving a
   shared/untrusted machine unattended.
5. **Restore the pinned CLI tools** (`ConfigTransform.Xml`/`.Json`, versions pinned in
   `.config/dotnet-tools.json`):
   ```bash
   export GITHUB_ACTOR=<your-github-username>
   export GITHUB_TOKEN=<a PAT with read:packages>   # only needed if the packages are private
   dotnet tool restore
   ```
   `nuget.config` in this repo's root already points at the GitHub Packages feed and reads
   credentials from these two env vars (`%GITHUB_ACTOR%`/`%GITHUB_TOKEN%`) — nothing is
   hardcoded, so this works the same locally as it does in CI (same PAT as
   `GH_PACKAGES_TOKEN` above works fine here too; a personal PAT is equally valid).
6. **Run the tool** exactly as `build-transformed.yml` does, e.g.:
   ```bash
   dotnet tool run configtransform-xml -- \
     --manifest .configtransform/OrderProcessor.Framework/manifest.json \
     --file App.config --client Acme --environment Production --diff
   ```

No .NET SDK install instructions here beyond `dotnet tool restore` needing one — see
`config-transform`'s own `docs/GETTING_STARTED.md` for the SDK-on-every-developer-machine
assumption this all rests on.
