# Claude's screens and the app's dialogs

What each slash command of Claude Code shows in screen reader mode, and which of the app's dialogs answers it (SPEC.md D33). The code form of this list is `SlashCommands.All` in `src/AxClaude.Core/Transcript/SlashCommands.cs`; keep the two in step.

Recorded: 33 commands were opened bare with Claude Code 2.1.281 in `C:\Temp\axclaude-fixture`, each in its own Claude process, and cancelled with Escape (2026-09-24). The others come from https://code.claude.com/docs/en/commands only and were not opened, mostly because typing them alone already acts (`/clear`, `/logout`, `/compact`, `/init`) or opens another program. Skills and plugins also appear as slash commands; they are not listed here.

## The four dialogs

The app does not need to know the command to choose the dialog: the shape of Claude's screen decides. The command only decides whether a list goes to the answer notice or stays in the message field.

1. **None.** The command prints text into the conversation and Claude is back at the `$` prompt, or the session changes or ends, or another program opens. Nothing to answer.
2. **Answer notice** (built, branch `answer-notice`). The cursor sits on `Select with numbers [1-N]. …` or `Enter y/n:` under numbered or `y.`/`n.` answers. The notice shows the title, Claude's lines, and the answers as radio buttons.
3. **Screen notice** (built, branch `answer-notice`). The cursor sits on a key-hint row (`↑/↓ to select · Enter to view · Esc to close`) or on a tab row (`Settings  Status   Config   Usage   Stats`). The notice would show the screen's text read-only and one button per key the hint row names, with Escape for Claude's own Escape.
4. **Typed input.** A command that wants text after it goes in the message field, guarded so that a message is not sent into a question by mistake (SPEC.md FR-2.10). "Other" is answered in the answer notice's "Your own answer" field; on an `Enter text for` row the message field sends your words without the guard, one keystroke at a time.

## Two gaps in the app today

- **Screens that wait on a hint row were not noticed** (fixed: they open the screen notice and count as waiting). Only the rows `Enter y/n`, `Enter selection`, `Select with numbers` and `Permission Required:` count as a question, so `/status`, `/tasks`, `/ide`, `/rewind`, `/autocompact` and the others below give no "Claude needs your answer", and a message typed then goes into the screen.
- **Tab screens are hidden from the conversation** (still so; the screen notice shows their lines). `/status`, `/usage`, `/cost`, `/help` and `/permissions` put the cursor on the tab row at the top, and every row below the cursor is treated as chrome (FR-3.5), so their content never reaches the conversation.

## Answer notice: numbered lists

- `/model`: "Select model", five models with the current one marked. Hints: `←/→` adjusts effort on the same screen, `s` uses the model for this session only, Enter sets it as the default.
- `/effort`: "Effort", six levels, current marked, no text rows.
- `/advisor`: "Advisor (experimental)", three models and "No advisor", current marked; a long explanation above and a recommendation below.
- `/theme`: "Theme", eight themes, current marked. Answer 8 ("New custom theme…") leads further. `Ctrl+T` turns syntax highlighting off.
- `/export`: "Export conversation", copy to clipboard or save to a file. Saving probably asks for a file name next (not recorded).
- `/memory`: "Memory", user instructions, project instructions, open the auto-memory folder. The answers open an editor or a folder.
- `/resume`: "Resume session", the recent conversations of the folder with a search row above. Typing searches; `Ctrl+A` shows all projects, `Ctrl+B` only the current branch.
- `/chrome`: "Claude in Chrome", four actions; the last is a toggle ("Enabled by default: No"). The prompt row says only "Then Enter to submit".
- `/mcp`: "Manage MCP servers", one answer per server with its state. An answer leads to that server's screen, another list (recorded with 2.1.282).
- `/hooks`: "Hooks", 33 hook events. An answer leads to that event's screen, which waits on `Esc to go back`: the screen notice (recorded with 2.1.282). Two-digit answers need the notice's two-digit typing.
- `/config` (also `/settings`): 44 numbered settings under the tab row (`Settings  Status   Config   Usage   Stats`) and the prompt row "Enter a number to change [1-44], or Escape to save and close:", printed after the frame's end. A number flips a true-or-false setting, or opens a numbered list of the setting's values ("Project instructions"), whose Escape returns to the settings; either way the list comes back. Escape on the settings saves and closes (recorded with 2.1.282). The app shows it as "Claude's settings": Change on a true-or-false setting asks True or False in a notice of its own and sends the number only for a change; Change on any other setting opens Claude's list of values.

Extra keys are not offered. `/model` names `s` (this session only) and `←/→` (effort), `/resume` Ctrl+A and Ctrl+B, `/theme` Ctrl+T. They act on the highlighted row, and a typed number answers at once without moving the highlight: `s` tried in `/model` with 2.1.282 applied to the model already highlighted. The notice would first have to move Claude's highlight with the arrow keys, more machinery than these keys are worth; the message field still reaches them.

## Answer notice: questions from Claude's tools

Recorded with Claude Code 2.1.281 (2026-09-24); every answer is typed one keystroke per character, then Enter.

- Workspace trust at startup: "Permission Required: Accessing workspace", y or n.
- A tool's permission question (`--permission-mode default`): "Permission Required: Create file" or "Bash command", the command or file and a preview, "Do you want to proceed?", then Yes, Yes and always allow / switch mode, and No as the last answer. A `Tip:` row inside it is skipped. "Tab to amend" draws nothing in screen reader mode and is not offered. No returns to the prompt with "What should Claude do instead?".
- Plan approval (`--permission-mode plan`): "Permission Required: Ready to code?", Claude's plan as text, then Yes with auto mode, Yes with manual approval, and "No, keep planning", which asks for feedback on an "Enter text for option 3" row: the message field, announced.
- Claude's own question (AskUserQuestion): a header row (" ☐ Colour"), the question, the answers, then Other and "Chat about this". Other asks for your words on an "Enter text for option 4 (Other)" row: the notice's "Your own answer" field fills it. "Chat about this" ends the question like Escape.
- Several questions in one call: a tab row ("←   ☐ Colour   ☐ Fruit   ✔ Submit   →") above each, one notice per question, then "Review your answers" with y or n.
- Several answers allowed ("comma- or space-separated for several"): the notice has check boxes; the ticked keys go joined with commas.

## Screen notice: screens to read

- `/status`, `/usage`, `/cost`: one tabbed screen, tabs Settings, Status, Config, Usage, Stats; `/cost` and `/usage` open on Usage. Cursor on the tab row; `Esc to cancel` at the bottom; the Usage tab also takes `d` (day) and `w` (week).
- `/help`: tabs Help, General, Commands, Custom commands; shortcuts and pointers. Cursor on the tab row.
- `/diff`: "Uncommitted changes (git diff HEAD)". Hint: `↑/↓ to select · Enter to view · Esc to close`.
- `/tasks`: "Background", the running tasks. Hint: `↑/↓ to select · Enter to view · Esc to close`. A task's details (seen in the background fixture) take `← to go back · Esc/Enter/Space to close · x to stop`.
- `/goal`: "Goal", the current goal or "No goal set". Hint: `Esc to dismiss`.
- `/mobile`: where to get the phone app. Hint: `Esc to close`, with the cursor on the row under it ("Then run /rc to continue this session from your phone"), which the notice shows as its last line (recorded with 2.1.282).
- `/rewind`: "Rewind", "Nothing to rewind to yet." in a new conversation. Hint: `Esc to cancel`. With checkpoints the docs describe a list, probably numbered (not recorded).
- `/ide`: "Select IDE", "No available IDEs detected." Hint: `Enter to confirm · Esc to cancel`. With an IDE running, probably a list.

## Screen notice: panels

- `/permissions`: tabs Permissions, Recently denied, Allow, Ask, Deny, Auto mode, Workspace; a search row; a numbered list starting with "Add a new rule…". Hint: `←/→ to switch · ↓ to select · Esc to cancel`. Cursor on the tab row.
- `/autocompact`: "Auto-compact window", a value adjusted with `←/→`; `Enter to apply · Esc to cancel`.
- `/feedback`: "Feedback drafts"; `Enter to write new feedback · Esc to close`, and "Any other key closes this panel". Writing leads to typed text.
- `/artifacts`: "Artifacts", "Loading artifacts…" at first; a list with actions after (not recorded).
- `/auto-mode-setup`, `/bug` (docs only): a draft to review and save; a consent screen before a report is sent.

## None: text in the conversation

- `/agents`: says the wizard was removed and where subagent files live.
- `/output-style` bare: the available styles and "Usage: /output-style <style>". With a style it switches.
- `/import`: "No other AI coding agents detected" here; with one installed the docs describe a picker.
- `/copy`: "No assistant message to copy" in a new conversation; with replies the docs describe a picker of code blocks, `w` to write a file.
- `/btw` bare: "Usage: /btw <your question>".
- `/context`: works for a moment, then prints the context use.
- `/list-agents`: this session's name and the other Claude sessions.
- `/tui`: the current renderer and "Usage: /tui <default|fullscreen>".
- `/color`, `/heapdump` (docs only): a random prompt colour; a heap snapshot written to a file.

## Typed input

- `/add-dir <path>`, `/cd <path>` (docs only): the path follows the command in the message.

## None: the session changes or ends

- `/clear`, `/compact`, `/exit`, `/logout`, `/plan`, `/branch`, `/fork`, `/background`, `/subtask`, `/teleport` (docs only). Several act at once and some start work that costs tokens; none was opened.

## None: another program opens

- `/login`, `/keybindings`, `/desktop`, `/insights`, `/install-github-app`, `/install-slack-app` (docs only).

## Not known yet

- `/init`, `/fast`, `/voice`, `/upgrade`, `/passes`, `/remote-control` (also `/rc`), `/autofix-pr` (docs only; the docs do not say what the bare command shows).
- `/rename`, `/claim-credit`, `/usage-credits`, `/powerup`: named in hints on recorded screens, not in the docs' list.
