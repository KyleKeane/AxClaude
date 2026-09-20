# AxClaude

A small Windows app that runs Claude Code in screen reader mode inside a hidden console and shows the conversation as plain text that NVDA reads line by line, with single-key navigation and a message field at the bottom. You are still talking to the real Claude Code: slash commands, prompts, dialogs and modes pass through.

**Download:** the zip on the [latest release](https://github.com/KyleKeane/AxClaude/releases/latest). Extract it and run `install.ps1`; the user guide inside explains the rest. Installed copies offer newer releases themselves under Help.

- `docs/user-guide.md`: the guide for users. It ships in the zip as `README.md` and under Help, User guide.
- `SPEC.md`: the specification, the design decisions and the status.
- `CLAUDE.md`: the working rules for developing with Claude Code in this repository.
- `docs/nvda-test-plan.md`: the manual test plan for NVDA.
- `tools/PtyCapture`: records raw console output; the recordings are the parser's test fixtures.

## Build and run

```
.\run.ps1                                  # build, then start on the current folder
.\run.ps1 C:\path\to\project               # start on that folder
.\run.ps1 C:\path\to\project -- --resume   # arguments after -- go to claude (default: --continue)
.\run.ps1 C:\path\to\project -New          # a new conversation (PowerShell swallows a bare --)
.\run.ps1 -NoBuild                         # start without building
.\run.ps1 -Test                            # run the unit tests
```

If PowerShell refuses to run the script: `powershell -ExecutionPolicy Bypass -File .\run.ps1`. Without the script: `dotnet build AxClaude.sln`, then `dotnet run --project src/AxClaude -- "C:\path\to\project"`.

Needs Windows 10 1809 or later, the .NET 10 SDK, Claude Code and, for testing, NVDA.

## Publish

```
.\publish.ps1                # self-contained exe + install.ps1 + guide, zipped, then installed for this user
.\publish.ps1 -NoInstall     # build publish\win-x64 and the zip only
```

Send the zip. The recipient extracts it and runs `install.ps1` (no administrator rights); `install.ps1 -Uninstall` removes it again. Close a running installed AxClaude before publishing again. `tools\make-icon.ps1` draws the icon.

## Release

```
.\release.ps1 1.0.1          # tests, sets the version, commits, tags v1.0.1 and pushes
gh run watch                 # follows the GitHub Actions run that builds and publishes the release
```

Add a `## 1.0.1 - <date>` section to `CHANGELOG.md` first: it becomes the release notes, which the app shows in its update notice. GitHub Actions (`.github/workflows/release.yml`) tests, runs `publish.ps1`, and attaches the zip to the release at `github.com/KyleKeane/AxClaude/releases`; installed copies of AxClaude find it there. `build.yml` runs the tests on every push.

## Licence

MIT, see `LICENSE`. AxClaude is made by Dr. Kyle Keane (www.kylekeane.com). Use it, change it and pass it on; keep that notice with it.

The software is provided "as is", without warranty of any kind, and the developer accepts no liability for its use; the full wording is in `LICENSE` and at the end of the user guide. AxClaude is an independent project, not affiliated with or endorsed by Anthropic.
