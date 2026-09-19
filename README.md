# AxClaude

A small Windows app that runs Claude Code in screen reader mode inside a hidden console and shows the conversation as plain text that NVDA reads line by line, with single-key navigation and a message field at the bottom. You are still talking to the real Claude Code: slash commands, prompts, dialogs and modes pass through.

- `docs/user-guide.md`: the guide for users. It ships in the zip as `README.md` and under Help, User guide.
- `SPEC.md`: the specification, the design decisions and the status.
- `CLAUDE.md`: the working rules for developing with Claude Code in this repository.
- `docs/nvda-test-plan.md`: the manual test plan run with NVDA before a release.
- `tools/PtyCapture`: records raw console output; the recordings are the parser's test fixtures.

## Build and run

```
.\run.ps1                                  # build, then start on the current folder
.\run.ps1 C:\path\to\project               # start on that folder
.\run.ps1 C:\path\to\project -- --continue # arguments after -- go to claude
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

## Licence

MIT, see `LICENSE`. AxClaude is made by Dr. Kyle Keane (www.kylekeane.com). Use it, change it and pass it on; keep that notice with it.
