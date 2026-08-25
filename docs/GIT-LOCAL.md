# Local Git authentication

Statefalse runs local Git commands on your Mac. It does not download a GitHub
credential from the backend and it never builds a Git remote URL containing a
PAT. `pull`, `fetch` and `push` use the repository's existing `origin` remote
and the authentication already configured on your Mac.

This is separate from the PAT in **Settings → Personal Access Token**. That PAT
is kept in the backend for GitHub API operations such as creating or merging
pull requests, rerunning workflows and changing draft status. It is not used
by Statefalse to authenticate local Git commands.

## Before using local repositories

Repositories must already be cloned on your Mac. Set **Settings → Workspace
Path** to a directory that contains them, for example:

```text
~/Workspace/
  project-a/.git
  project-b/.git
```

For each repository, check that it has an `origin` remote and that Git works
outside Statefalse:

```bash
cd ~/Workspace/project-a
git remote -v
git status
git fetch origin
```

Statefalse uses the same `origin` and the same local Git configuration. If the
commands above work, the corresponding local operations in Statefalse should
work as well.

## Option 1: SSH (recommended)

SSH avoids entering a credential during normal Git operations.

1. Create a key if you do not already have one:

   ```bash
   ssh-keygen -t ed25519 -C "you@example.com"
   ```

2. Add it to the macOS SSH agent and copy the public key:

   ```bash
   ssh-add --apple-use-keychain ~/.ssh/id_ed25519
   pbcopy < ~/.ssh/id_ed25519.pub
   ```

3. In GitHub, open **Settings → SSH and GPG keys → New SSH key** and paste the
   public key.

4. Test the connection:

   ```bash
   ssh -T git@github.com
   ```

5. Make sure the repository uses an SSH remote. Replace `OWNER/REPOSITORY` as
   appropriate:

   ```bash
   git remote set-url origin git@github.com:OWNER/REPOSITORY.git
   git remote -v
   ```

If you use several keys or a GitHub Enterprise host, configure the matching
host and `IdentityFile` in `~/.ssh/config`. Organization repositories may also
require authorizing the key through the organization's SSO policy.

## Option 2: HTTPS with the macOS Keychain

You can keep an HTTPS remote and let Git store its credential in the macOS
Keychain:

```bash
git config --global credential.helper osxkeychain
```

Use an HTTPS remote, then run a Git operation once:

```bash
git remote set-url origin https://github.com/OWNER/REPOSITORY.git
git fetch origin
```

When prompted, enter your GitHub username and use a GitHub PAT as the
password. GitHub no longer accepts the account password for Git over HTTPS.
The credential helper stores the result in the Keychain so Statefalse can reuse
it without receiving or storing the PAT.

To remove or replace a stored credential, open **Keychain Access**, search for
`github.com`, and delete the relevant internet-password entry. The next Git
operation will ask again.

## What each action does

| Statefalse action | Local Git operation | Effect |
|---|---|---|
| Refresh branches | `git fetch origin --prune` | Updates remote references; does not rewrite working files. |
| Pull/update a branch | Fetch/rebase or pull with rebase, depending on the action | Applies remote changes to the local branch. A rebase can produce conflicts. |
| Update branch | Fetch, checkout, pull/rebase and push | Updates the branch and publishes the result to `origin`. |
| Delete remote branch | `git push origin --delete BRANCH` | Deletes the selected branch on GitHub. |
| Create pull request | GitHub API after the branch is published | Requires a remote branch and suitable GitHub API permissions. |

Review the selected repository and current branch before operations that can
change history or the remote:

```bash
git status
git branch --show-current
git remote -v
```

Commit or stash important local changes first. A protected branch, missing
write permission, an uncommitted change, or a rebase conflict can cause Git to
stop and report an error. Resolve the issue in the repository or in Terminal,
then retry the Statefalse action.

## Troubleshooting

### `Permission denied (publickey)`

The remote is using SSH, but GitHub did not accept an available key. Run
`ssh -T git@github.com`, check that the public key is registered in the correct
GitHub account, and confirm that `ssh-agent` has the private key:

```bash
ssh-add -l
```

### `Authentication failed` or repeated HTTPS prompts

Confirm that `osxkeychain` is configured and that the GitHub PAT is valid,
non-expired and authorized for the repository. Remove the old `github.com`
entry from Keychain Access and retry if the wrong account was stored.

### Push rejected

GitHub may reject a push because the branch is protected, the remote branch
has newer commits, or the account lacks write permission. Inspect the complete
Git error, update/rebase the branch as required, or ask a repository
administrator about its rules.

### Statefalse cannot see the repository

Confirm that the repository is inside the configured Workspace Path, contains
`.git`, has an `origin` remote and is accessible to the macOS user running the
app. Avoid selecting a very broad directory containing unrelated repositories.

