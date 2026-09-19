---
name: record-fixture
description: Record a new ConPTY fixture of Claude Code in screen reader mode with tools/PtyCapture, review it for personal data, and add it to tests/fixtures with an expected transcript. Use when a parser bug needs a reproduction or a new Claude Code version needs pinning.
---

# Record a fixture

1. Write a script in the scratchpad (one command per line): `send`, `mark`, `wait <ms> <text>`, `iffound <command>`, `quiet <ms> <max>`, `sleep <ms>`, `exit <ms>`. Answer startup dialogs conditionally, for example:

   ```
   mark
   wait 8000 Yes, I trust this folder
   iffound send y\r
   mark
   quiet 2500 30000
   send Reply with the single word hello.
   sleep 150
   send \r
   mark
   wait 90000 \x07
   quiet 2000 20000
   send /exit\r
   exit 15000
   ```

   Send the text and the `\r` as separate writes. Use a scratch folder as `--cwd` so the recording does not touch a real project.

2. Run: `dotnet run --project tools/PtyCapture -- --cwd <scratch folder> --out <name>.vt --cols 200 --rows 50 --script <script> -- "%USERPROFILE%\.local\bin\claude.exe" --ax-screen-reader`. The tool strips `CLAUDE*` variables from the child environment, so it can be run from inside a Claude Code session.

3. Inspect with `dotnet run --project tools/PtyCapture -- --dump <name>.vt`. Look for the account name in the header, absolute paths outside the scratch folder, session ids and anything else personal. If present, re-record with a neutral folder or note the lines to redact in the fixture README; never hand-edit the bytes of a `.vt` file, because the cursor positions in it depend on the original text lengths.

4. Copy the `.vt` file to `tests/fixtures/`, name it `<topic>-claude<version>-<cols>x<rows>.vt`, and add its expected transcript `<same name>.expected.txt` produced by the parser once it exists (review the transcript by hand the first time).

5. Add a line to `tests/fixtures/README.md` with the date, Claude Code version, script used and what the fixture demonstrates. Commit fixture and expected file together.
