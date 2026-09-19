# AxClaude — Project Specification

Working name: **AxClaude** (rename freely; see Q1).
Version 1.0, 2026-09-19. Every feature is built; this document is kept in step with the code (a behaviour change updates it in the same commit).

---

## 1. Summary

AxClaude is a small, self-contained Windows desktop application for a blind developer who uses the NVDA screen reader. It runs the Claude Code CLI (`claude --ax-screen-reader`) inside a Windows pseudo console (ConPTY), sends what the user types in a plain input field to that console, and turns everything the console prints into a **line-by-line transcript** in a read-only text view. The transcript is navigable with NVDA-style single-key commands (`h` headings, `i` the user's own inputs, `o` the start of each reply) and carries marker lines that the app inserts around every exchange. The app starts against a project folder, like `claude` itself, and the folder can be changed from a menu.

The app is **not** a terminal emulator. It relies on Claude Code's screen reader mode, which prints flat, labelled, linear text, and it only understands the small set of control sequences that ConPTY emits for that output.

## 2. Goals and non-goals

Goals:

- **G1 Screen reader first.** Everything works with NVDA and the keyboard only. Focus, names, roles and announcements are designed, not left to defaults.
- **G2 Lightweight.** One WinForms executable, no third-party runtime dependencies, starts in well under a second, idles at negligible CPU.
- **G3 Faithful.** Everything Claude Code prints in screen reader mode ends up in the transcript, in order, once.
- **G4 Structured.** The transcript is navigable by structure (inputs, replies, headings, tool activity, prompts), not only by line.
- **G5 Project aware.** Claude runs in a chosen folder, and the app always shows which.
- **G6 Robust.** Startup dialogs, crashes, exit and restarts are handled; the UI never freezes on console I/O.

Non-goals: a general terminal emulator (colours, the alternate screen, mouse reporting are ignored; only Claude Code in screen reader mode is supported); the Claude API, the Agent SDK or `-p --output-format stream-json` (D3); other platforms (Windows 10 1809 or later only); a mouse-first UI.

## 3. Users and scenarios

Primary user: a blind software developer on Windows 11 with NVDA, keyboard only, who already uses Claude Code and wants a calmer, navigable way to read its output than a terminal window.

| Id | Scenario | What must be true |
| :-- | :-- | :-- |
| S1 | Start on a project | `axclaude C:\src\myproject` (or the menu) starts Claude there; the title, status bar and Project menu name the folder. |
| S2 | Ask and read | Type, Ctrl+Enter, hear that Claude finished, Ctrl+Tab, Shift+O lands on the start of the latest reply, read down with the arrows. |
| S3 | Answer a prompt | A permission prompt or y/n question appears as plain lines; "Claude needs your answer" is spoken; the user types `1` or `y` and presses Ctrl+Enter. |
| S4 | Review a long reply | `h` / Shift+H move between headings; `1`..`6` by level. |
| S5 | Recall what was asked | `i` / Shift+I move between the user's inputs. |
| S6 | Switch project | Project → Change folder… → the standard folder picker → Claude restarts there; a system line and an announcement confirm it. |
| S7 | Resume | `axclaude C:\src\myproject -- --continue` starts Claude on the last conversation; there is no menu item (FR-1.8). |
| S8 | Claude stops | The transcript says so, the app announces it, Restart is one keystroke away. |
| S9 | Interrupt | Shift+Escape. Plain Escape in the input field is guarded (FR-2.3, D20). |

## 4. Background: Claude Code in screen reader mode

Verified on 2026-09-18 against Claude Code **2.1.277** (native build) and https://code.claude.com/docs/en/accessibility.

### 4.1 Documented behaviour

- Enabled with `claude --ax-screen-reader`, `CLAUDE_AX_SCREEN_READER=1`, or the `axScreenReader` setting. The first line printed is `[Screen Reader Mode: on via flag]`.
- Output is flat text: no boxes, no colour cues, no redraws of unchanged content; spinners are static text; tables read as `Header: value` sentences. The `tui` setting is ignored.
- Every message starts with a label: `you:`, `claude:`, `thinking:`, `tool:`, `tool error:`, `error:`, `warning:`, `Permission Required:`, `Cost:` (at exit).
- Menus become numbered lists followed by an `Enter selection` prompt; the user types the number and presses Enter. Yes/no questions take a typed `y` or `n` and Enter.
- Two pauses exist for terminals: `CLAUDE_AX_STARTUP_QUIET_MS` (3 s after the confirmation line) and `CLAUDE_AX_PREPARK_MS` (50 ms with the cursor parked before a changed line). Both are set to `0` (FR-1.3).
- The **terminal bell** rings when a reply finishes, when a prompt needs an answer, and when a tool that ran longer than 5 s finishes: the app's primary "Claude needs you" signal. OSC 133 marks are also emitted at turn boundaries (unused).
- Shift+Tab cycles permission modes and prints an announcement such as `[plan mode on]` once. Alt+M also cycles on Windows.
- Keys: Enter submits; Escape interrupts or closes a dialog; Ctrl+C interrupts or clears input; Ctrl+D twice exits, as does `/exit`; Up/Down browse history; Tab accepts a completion; Ctrl+U / Ctrl+K / Ctrl+W edit the draft.
- Multi-line pastes collapse to `[Pasted text #N +M lines]`; bracketed paste mode is on (`ESC[?2004h`).

### 4.2 The stream at startup (`probe.vt`, 120×40)

```
ESC[?9001h ESC[?1004h ESC[?25l ESC[2J ESC[m ESC[H ESC]0;claude BEL? ESC[?25h
[Screen Reader Mode: on via flag] CR LF
ESC[?2004h ESC[?2031h ESC[?1004h ESC[>0q ESC[?u ESC[?25l
Permission Required: Accessing workspace: CR LF
<folder path> CR LF ... y. Yes, I trust this folder CR LF n. No, exit CR LF Enter y/n: CR LF
Enter to confirm · Esc to cancel ESC[11;12H ESC[?25h
```

1. The first sequences (win32-input-mode request, focus events, clear, title) come from ConPTY and are tolerated.
2. The client asks for bracketed paste, synchronized output, focus reporting, the terminal version and the kitty keyboard protocol. None needs an answer.
3. The workspace trust dialog is a flat `y/n` prompt shown the first time a folder is used.
4. The cursor is positioned with CUP onto the input line: cursor positioning happens even in screen reader mode, to park the caret and redraw the region below the static transcript.

### 4.3 After the trust prompt (`session.vt`)

- The dialog area was cleared row by row (`ESC[2;1H ESC[K CR LF ESC[K ...`) and replaced by the session header, `auto mode on (shift+tab to cycle)`, `effort: xhigh · /effort` and the prompt.
- A second one-time dialog then overwrote rows 5 and below. Startup dialogs are unpredictable in number; never assume the prompt is ready after a fixed delay.
- A message sent while a y/n dialog was open produced `Please answer y or n.` once; later invalid input was ignored silently. Dialog lines must be shown faithfully.
- Rows are rewritten from column 1 and end with `ESC[K`, so trailing whitespace is not significant.

### 4.4 A conversation (`session4.vt`, 120×40, and the fixtures)

**Frames.** Every redraw is bracketed by `ESC[?25l` and `ESC[?25h`. A frame moves the cursor with CUP to the first row of the *dynamic block*, rewrites that row and the rows below it (each ending with `ESC[K` and `CR LF`), skips unchanged rows with another CUP, erases rows left over from a taller frame, and parks the cursor on the prompt row. Rows above the block are never touched again. A frame taller than the screen scrolls with `CR LF` on the bottom row.

**The dynamic block.** Idle: the mode line and the prompt row `$`. Working: a spinner row (`Forming…`, changing every second), sometimes a `Tip: …` row, the mode line with ` · esc to interrupt`, the prompt row. A multi-line draft adds `ctrl+g to edit in Notepad` above the prompt. The slash-command list and a dialog's `Enter to confirm · Esc to cancel` hint are drawn **below** the cursor row.

**A turn.** `you: What is 2+2?` replaces the mode line; a spinner row is rewritten in place; then `ESC]133;C ESC]133;D BEL`; then the final frame: `claude: 2 + 2 = 4`, the turn summary `Baked for 1s · done 2:47 PM`, the mode line, the prompt.

Facts the design depends on:

1. **Enter must be a separate write.** `text\r` in one write became a pasted two-line draft. `text`, then `\r` later, submits reliably.
2. **The `you:` echo.** After submit, Claude rewrites the first dynamic row as `you: <text>`; a multi-line message continues on unlabelled rows. Slash commands and dialog answers produce no `you:` line.
3. **Reply rendering.** `claude: ` starts the reply; markdown is plain text: headings lose their `#`, the first block shares the `claude:` row, a later heading is a short row between blank rows, list items keep `- `. **No SGR styling** except dim around the resume hint at exit. Heading detection is structural (§7.6).
4. **Hard wrapping.** Claude wraps long lines at the console width with `CR LF`; a continuation row can begin with a space. A wide console (D9) avoids most of it.
5. **Tool activity.** `tool: Reading 1 file… (ctrl+o to expand)` with the command on following rows (`$ printf …`); the final frame collapses it to `Read 1 file (ctrl+o to expand)`. A content row can start with `$ `: only the cursor row may be treated as the prompt.
6. **Turn end.** `ESC]133;C`, `ESC]133;D` and `BEL` arrive together, immediately **before** the final frame with the reply. Announcements wait for the frame (FR-7.2).
7. **Titles.** `ESC]0;…` carries a state glyph and the session name: `◐ Basic arithmetic question` while working, `✳ …` idle.
8. **Bracketed paste marks text as pasted.** Claude answered that an instruction "came from the pasted text rather than from you directly" and did not follow it. The user's words go as typed text (FR-2.5).
9. **Turn summary lines.** `Crunched for 5s · done 2:48 PM`.
10. **Exit.** `/exit` opened the autocomplete list below the prompt, `\r` ran it: marks, `BEL`, mode resets, title cleared, `Resume this session with:` and `claude --resume <id>`, exit.
11. Tip rows appear inside the dynamic block while working and vanish with the final frame; `effort: xhigh · /effort` sits in the block for the first turns.
12. **240 columns behave the same** (`conversation-claude2.1.277-240x50.vt`); a y/n dialog echoes the typed `y` with a backspace-space-`y` sequence before the redraw.
13. **A message sent while Claude works** (`steering-claude2.1.278-240x50.vt`): drawn inside the dynamic block as `you: <text>` over `ctrl+x ctrl+s to send now`, redrawn with the spinner every second on shifting rows (a running Bash tool shows `Running…  (3s · timeout 1m)` and `(ctrl+b to run in background)` above it). When Claude takes it up, the `you:` row stays where it was drawn last, the hint disappears and the reply follows. There is no second echo. FR-4.7 is the rule that came out of it.
14. **A lone `\n` write is Ctrl+J**: a two-line draft, a two-row echo, and a reply that treats it as typed text, not a paste.
15. **The draft is printed without a frame bracket** (`long-message-claude2.1.278-240x50.vt`): `$ ` plus the text, wrapped over several rows, no cursor hide/show, so the app sees it only at a quiet-timer frame end. The echo wraps the same way.
16. **Mode cycling** (smoke recording): `ESC[Z` went from `auto mode on (shift+tab to cycle)` to `manual mode on` (no cycle hint) to `accept edits on (shift+tab to cycle)`. Each change prints `[manual mode on]` on the row under the prompt for one frame with the cursor parked on it. These bracketed rows are Claude's own announcements and the app speaks them.
17. **Ctrl+O** (smoke recordings): after a turn, two presses drew nothing: screen reader mode never re-renders printed rows, so a past tool summary cannot be expanded. On an idle prompt, Ctrl+O replaced the mode line and the prompt with `Showing detailed transcript · ctrl+o to toggle · ctrl+e to show all verbose`, and a message typed afterwards was never taken up. Ctrl+O is a mode switch that hides the prompt, not an expander.

### 4.5 Two Windows process gotchas

1. **Standard handle inheritance.** A launcher with redirected stdio hands its pipes to the child even with `bInheritHandles = false`; Claude Code then sees no TTY and silently switches to `--print` mode. Fix: clear the three standard handles with `SetStdHandle` around `CreateProcess` (FR-1.4).
2. **Nested session variables.** A process started from inside Claude Code carries `CLAUDECODE=1` and other `CLAUDE*` variables; the child environment is built without them (FR-1.3).

## 5. Functional requirements

MUST unless marked SHOULD; everything listed is built.

### FR-1 Launch and process hosting

- FR-1.1: Create a ConPTY of a fixed size (default 240×50, settings `ptyColumns` / `ptyRows`) and start `claude.exe --ax-screen-reader` in the project folder with the extra arguments from the command line.
- FR-1.2: Locate `claude` in this order: `--claude <path>`, the `claudePath` setting, `claude.exe`/`claude.cmd` on `PATH`, `%USERPROFILE%\.local\bin\claude.exe`, `%APPDATA%\npm\claude.cmd`. A `.cmd` shim runs through `cmd.exe /d /s /c`. Nothing found: the notice of FR-1.9, the window stays open.
- FR-1.3: The child environment is the current environment minus every `CLAUDE*` variable, plus `TERM=xterm-256color`, `CLAUDE_AX_STARTUP_QUIET_MS=0`, `CLAUDE_AX_PREPARK_MS=0`, `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1`.
- FR-1.4: Clear the app's own standard handles around `CreateProcess` (§4.5).
- FR-1.5: Console output is read on a background thread and handed to the UI thread in batches (§7.3); never read on the UI thread. Writes go through one serialized writer.
- FR-1.6: When Claude exits: the system line `Claude stopped (exit code N). Press Ctrl+Shift+R to start it again.`, the announcement `Claude stopped`, status `stopped`. No auto-restart.
- FR-1.7: On close, close the pseudo console (Claude exits), wait up to 2 s, then terminate. No Ctrl+C. Claude Code persists the conversation continuously, so `--continue` recovers it. A close (Alt+F4, the close button, Project → Exit) while Claude is in the middle of a turn (working, waiting for an answer, or holding a message sent while it worked) first shows the **Close AxClaude?** notice (D23): "Claude is still working" (or "Claude is waiting for your answer"; plus " and a message you sent is still waiting for it" when one is), ". If you close now, Claude stops in the middle of its work.", "What is done so far is saved. Start AxClaude with -- --continue to carry on later.", "Enter closes anyway. Escape keeps AxClaude open." Buttons **Close anyway** (Enter) and **Keep working** (Escape). An idle Claude closes at once; a Windows shutdown or Task Manager never asks.
- FR-1.8 SHOULD: **Restart Claude** (Ctrl+Shift+R) stops and restarts in the same folder with the same arguments. No resume menu item: `--continue` / `--resume <id>` go after `--` (FR-9.1) and Restart keeps them.
- FR-1.9 SHOULD: When Claude Code is not found, the notice **Claude Code was not found** (D23) lists the paths tried, gives the install command `irm https://claude.ai/install.ps1 | iex` with **Copy install command** (the notice stays; "Copied. Paste it into PowerShell and press Enter" is spoken), **Open install instructions** (`https://code.claude.com/docs/en/setup`), **Locate claude.exe…** (the standard file picker; a choice closes the notice, is saved as `claudePath` and started at once; Cancel returns to the notice) and **Close** (Enter or Escape). A system line says the same; Restart retries after an install.
- FR-1.10 SHOULD: **Updates.** Releases are GitHub releases of `KyleKeane/AxClaude`, each with the zip that `publish.ps1` builds (§9, D25). At startup, when `checkForUpdates` is on (the default; Options → Check for updates when AxClaude starts), the app asks the GitHub API once for the latest release, off the UI thread with a 30 s limit; a failure only goes to the log. A version above the running one (major, minor, build) is announced ("AxClaude 1.0.1 is available. See the Help menu"), written as a system line and offered as Help → **Update to AxClaude 1.0.1…**; otherwise the item reads **Check for updates…** and asks again on demand, reporting the result in a notice: the newest version is in use, or GitHub could not be reached (with **Open releases page**). The update notice shows the release notes (the CHANGELOG section) with **Update now**, **Open release page** and **Later**. Update now downloads the zip into `%LOCALAPPDATA%\AxClaude\updates\<version>` and extracts it (announced: "Downloading AxClaude 1.0.1", then "AxClaude 1.0.1 downloaded. AxClaude closes now and starts again when the update is installed"), then closes the window; the close question of FR-1.7 applies, and Keep working cancels the install and keeps the download. On the way out the app starts that version's own `install.ps1 -WaitForProcess <pid> -Start <folder> [-ContinueConversation] -LogFile %LOCALAPPDATA%\AxClaude\logs\update.log` without a window: it waits for this process to end, installs over `%LOCALAPPDATA%\Programs\AxClaude` and starts the new AxClaude on the same folder, with `-- --continue` when a message was sent in the session. Nothing is downloaded or installed without the user choosing it.

### FR-2 Input

- FR-2.1: A multi-line field "Message to Claude", three lines high, at the bottom. **Ctrl+Enter** sends and clears it; **Enter** inserts a line break (D11). Ctrl+Enter on an empty field sends a bare Enter (confirms dialogs). While Claude is not running, Ctrl+Enter sends nothing, keeps the text and announces "Claude is not running. Press Ctrl+Shift+R to start it again".
- FR-2.2: The text is one write, then `\r` as a **separate** write 100 ms later (§4.4 item 1).
- FR-2.3: **Shift+Escape**, anywhere, sends `ESC` (interrupt / close a dialog); Session → Interrupt Claude does the same. Plain Escape in the input field sends nothing and announces "Escape does nothing here. Press Shift+Escape to interrupt Claude", interrupting other speech (D20). Ctrl+Escape opens the Start menu before any app sees it. Neither key clears the field.
- FR-2.4: Ctrl+Up / Ctrl+Down in the field send Up / Down to Claude (its menus). Claude's own history recall lands on the hidden prompt row, so it is not usable through the app.
- FR-2.5: A multi-line message goes as typed text: each line as its own write, a single `\n` write between lines (Ctrl+J), then `\r`. Bracketed paste is **not** used for the user's own words (§4.4 items 8 and 14).
- FR-2.6: Up on the first line of the field and Down on its last line walk the messages sent in this session (last 100, not persisted). The draft comes back after the newest entry. Each recalled message is announced; the empty draft as "Message field is empty".
- FR-2.7: Shift+Tab in the field sends Shift+Tab to Claude (next permission mode); the new mode is announced (FR-7.7). Tab still moves to the conversation.
- FR-2.8: Sending is never locked while Claude works: Claude Code queues or steers with the message itself (§4.4 item 13). The app announces "Message waiting" instead of "Message sent" and prints the exchange block when Claude takes the message up (FR-4.7).
- FR-2.9: Enter on a conversation line quotes it in the field: a blank line when the field is not empty, the literal line `_ start of copied line from conversation history _`, then `Line 3 of 54: <text>` (the display line number and count as `l` reports them; joined rows as one), then a new line for the comment, where the field's caret goes. Focus and the conversation caret stay. Announces "Line 3 copied to the message" (D21).
- Dropped: a paste-as-pasted-text command, a persisted per-project history, an `@` file picker (Claude Code covers them).

### FR-3 Transcript view

- FR-3.1: A read-only, multi-line, word-wrapped text view "Conversation" fills the window above the field: a native Win32 EDIT control (WinForms `TextBox`), so NVDA reads it line by line and supports review, selection and copying.
- FR-3.2: Each transcript line is one logical line of Claude's output or one app line. Trailing whitespace is trimmed. Rows the terminal soft-wrapped are not joined: at 240 columns Claude hard-wraps first, see FR-3.2a.
- FR-3.2a SHOULD: A plain row inside a `claude:` or `you:` block that follows a row within 20 characters of the console width, and starts with a space or a lowercase letter, is `JoinedToPrevious`; the view shows the two as one line joined with a space (none when the row starts with one). Rows under a `tool:` line, labelled rows, blank rows and rows at a marker never join; a joined row is never a heading; `l` counts a joined line once. The model keeps the rows as separate `Line`s; only the mirror's separator changes (D17). Setting `joinWrappedLines` (default true, file only) turns it off.
- FR-3.3: Output that arrives while the user reads never moves the caret or selection, never scrolls the view away from the caret, never makes NVDA speak. Without focus, the caret tracks the end and the view scrolls to it, so Ctrl+Tab lands on the newest line; the view also scrolls to the caret when it gains focus. A notice opened while the view had focus does not count as losing it (`KeepCaret`). The caret stays on its transcript line and column while lines above change, its own line is rewritten, or lines are hidden or trimmed (§7.7). Output within 250 ms of a key press in the focused view is applied once the keys stop.
- FR-3.4: A screen row keeps its line while it is on screen, so in-place rewrites update the line. A line is committed once its row scrolls off or the screen is reset.
- FR-3.5: Chrome is hidden, decided at frame end (§7.5): (a) every row **below** the cursor row; (b) the cursor row when it is the prompt or the draft; (c) the contiguous block above the cursor row that matches the chrome patterns in `LineClassifier` (mode line, spinner, `Tip:`, `ctrl+g to edit in Notepad`, `effort: … · /effort`, the queued-message hint, the background hint, a bracketed announcement, the completion list). Content rows outside that block are never matched, so `$ printf …` in a reply stays. The mode line and the spinner feed the status bar. No option shows hidden rows; Options → Record raw stream captures the stream for diagnosis.
- FR-3.6: At most `maxTranscriptLines` (default 20 000) lines; beyond that the oldest 10 % that are off screen are dropped in one operation and the view keeps the caret on its line.
- FR-3.7 SHOULD: Ctrl+F (anywhere; Navigate → Find…) opens the **Find** notice (D23): a "Text to find" field with the last text selected, **Find next** (Enter) and **Cancel** (Escape); Enter with an empty field keeps the notice open and announces "Type the text to find". F3 / Shift+F3 repeat the search; with no text yet they open the notice. Case-insensitive, line by line from the line after (before) the caret, wrapping, ending with the caret line. A match focuses the conversation, puts the caret at it and announces the line, prefixed "From the top, " or "From the end, " after a wrap; no match announces "Not found: <text>" and leaves the caret.
- FR-3.8 SHOULD: Project → **Save conversation as…** (Ctrl+S) writes the conversation as shown (hidden rows left out, wrapped rows joined) as UTF-8 without a byte order mark, default name `<folder>-<yyyyMMdd-HHmm>.txt` in the project folder; announces "Conversation saved".
- FR-3.9: **Bookmarks.** `m` in the conversation (Navigate → Bookmark this line; Ctrl+Shift+K from anywhere, which focuses the conversation first) drops a bookmark in front of the caret's line: an app line `Bookmark n` (`LineKind.Bookmark`, `IsMarker`: never classified or hidden, not a heading; *n* counts bookmarks since the session started), announced "Bookmark n added"; the caret stays. On a bookmarked line, or on the bookmark line itself, `m` removes it ("Bookmark n removed"). `k` / Shift+K jump to the next / previous bookmark line and announce it together with the line it marks ("Bookmark 2, claude: …"); otherwise "No next bookmark". A bookmark on a wrapped line (FR-3.2a) goes in front of its first row and leaves the join and the heading rule around it alone (`JoinWrappedRows` steps over bookmark lines). Bookmarks are conversation lines: they are copied and saved with it (FR-3.8), trimmed with it (FR-3.6), and live as long as the conversation in the window. Nothing is persisted separately.
- FR-3.10: Options → Font… (the Windows font dialog); Ctrl+Plus / Ctrl+Minus change the size in 1-point steps (6 to 72) and announce it; "Use the Windows text size" returns to the system message font, which follows the text size slider under Settings → Accessibility. Menus and the status bar use the system font. Colours are the system colours, so high contrast themes apply.
- Dropped: a transcript log file (Claude Code keeps every conversation).

### FR-4 Markers

- FR-4.1: When the user sends a message, the app prints an **exchange block** at the end of the conversation, above the trailing chrome block: `# Input n`, the message as sent (one line per line, trailing whitespace trimmed), a blank line, `# Output n Reply from Claude`, a blank line. *n* counts messages since the session started. Nothing in the block moves afterwards; the reply follows it (D22). The `#` is literal, so a saved conversation is Markdown. Both markers are level 1 headings; Claude's headings are level 2 and deeper. The message lines and blank lines are app lines (`LineKind.UserMessage`), never classified or hidden. A message sent while Claude works gets its block later (FR-4.7). FR-4.6 adds a time stamp.
- FR-4.2: Claude's `you:` echo is hidden, always. The echo block is found by text: from the `you:` row, rows are taken while the text so far, whitespace removed, is a prefix of the sent text, whitespace removed, until equal. Wrapped rows and blank lines inside the message belong to the echo; the rows after it do not.
- FR-4.3: An echo that does not repeat the sent text (a `[Pasted text #1 +5 lines]` placeholder) stays visible after the block.
- FR-4.4: No block for a bare Enter, special keys, slash commands (no echo; their output follows the previous lines) or an answer typed while a prompt waits (the last content line is a Prompt or PromptOption).
- FR-4.5: App system lines read `System: <message>`.
- FR-4.6 SHOULD: Options → **Show the time in the Input and Output lines** (setting `markerTimeStamps`, default off): `# Input 3 (14:32)` and `# Output 3 Reply from Claude (14:32)`, 24-hour. Existing markers keep their text.
- FR-4.7: A message sent while Claude works is drawn by Claude **inside** the working block over the `ctrl+x ctrl+s to send now` hint (§4.4 item 13); those rows are chrome, never the echo. The message is held outside the conversation until the hint disappears and the `you:` block becomes content, which is when Claude takes it up; the block then goes in directly before that echo, and the echo is hidden, so the message and its response arrive together (D16). Meanwhile the status bar shows `1 message waiting` and Up in the field recalls the text. If Claude becomes idle with nothing waiting and no echo came, the block goes in at the end. A pending send is matched to an echo by its text (an echo that repeats nothing is charged to the oldest pending send and stays visible), lives for 30 minutes, and re-attaches when its echo rows move.

### FR-5 Navigation keys in the transcript view

Single keys act only when the view has focus and no modifier other than Shift is held. Shift reverses direction. Each jump moves the caret to the start of the target line, scrolls it into view and announces its text (FR-7). No target: "No next input", "No previous heading", and so on; the caret stays.

| Key | Target |
| :-- | :-- |
| `i` / Shift+I | input marker (`# Input n`) |
| `o` / Shift+O, `r` / Shift+R | output marker (`# Output n Reply from Claude`); `r` is an alias (r for reply) |
| `h` / Shift+H | heading: the markers (level 1) and Claude's headings (level 2 and deeper, §7.6) |
| `1`…`6` / Shift | heading of that level |
| `c` / Shift+C | `claude:` reply line |
| `t` / Shift+T | tool line: `tool:`, `tool error:` or a summary ending in `(ctrl+o to expand)` |
| `d` / Shift+D | turn summary (`… for 5s · done 2:48 PM`), d for done |
| `p` / Shift+P | prompt line (`Permission Required:`, `Enter selection`, `Enter y/n`) |
| `e` / Shift+E | `error:`, `warning:` or `tool error:` |
| `s` / Shift+S | system line |
| `b` / Shift+B | blank line |
| `k` / Shift+K | bookmark line (`Bookmark n`, FR-3.9); the announcement adds the line it marks |
| `m` | bookmark this line, or remove its bookmark (FR-3.9); the caret stays |
| Ctrl+Shift+O | latest output marker, from anywhere; "No response yet" when none |
| Ctrl+Shift+K | bookmark the caret's line, from anywhere (focuses the conversation) |
| Page Up / Page Down | one screen of wrapped lines up / down from the caret, keeping the caret's screen row; reaches the first and last line. The native keys work from the visible page and strand a caret that is scrolled out of view. NVDA reads the new line; the app says nothing. Shift+Page Up/Down stay native. |
| `l` | `Line 12 of 340` (joined rows count once); the caret stays. On demand only: arrow movement never speaks line numbers. |
| Ctrl+F, F3 / Shift+F3 | find (FR-3.7), from anywhere |
| Ctrl+Home / Ctrl+End | start / end (native) |
| Enter | quote the line in the message field (FR-2.9) |
| Escape | focus the input field (nothing is sent) |
| Shift+Escape | send Escape to Claude (FR-2.3) |

The key table is data (`QuickKey(Keys, Name, Func<Line, bool>)` in `TranscriptView`); the keys are fixed in code.

### FR-6 Focus

- FR-6.1: Ctrl+Tab, Ctrl+Shift+Tab and F6 toggle between the field and the view from anywhere.
- FR-6.2: Focus starts in the field and stays there after sending.
- FR-6.3: Tab moves between the field and the view; Shift+Tab does so in the view and goes to Claude from the field (FR-2.7). Neither captures Tab.
- FR-6.4: Alt opens the menu bar; every item has a mnemonic and, where applicable, a displayed shortcut.

### FR-7 Announcements and attention signals

- FR-7.1: Announcements are UI Automation notifications raised on the transcript view (`RaiseAutomationNotification`), which NVDA speaks without moving focus; while a notice shows they are raised on the notice's text or field. Jump results use "most recent" processing (a new jump cancels the previous); status announcements use "all".
- FR-7.2: The bell (BEL) starts or restarts a 400 ms timer, because the bell arrives just before the final frame (§4.4 item 6). When it fires: **"Claude needs your answer"** if the last content line is a Prompt or PromptOption, otherwise **"Claude is done"** (setting `announceBell`, default on).
- FR-7.3: With the bell, the Windows Asterisk sound (`soundOnBell`, default on) and, when the window is not active, a taskbar flash until it is activated (`flashTaskbar`, default on). NVDA speaks notifications only from the active window, so these reach a user working elsewhere.
- FR-7.4: "Speak replies as they arrive" (`speakReplies`, default off): each line of the reply after the newest output marker (the `claude:` line and its unlabelled continuation lines) is announced once final: committed, followed by another visible line, or Claude idle. Tool lines and thinking are never auto-spoken.
- FR-7.5: System events are announced: folder changed, Claude stopped, copy confirmations, recording started and stopped, text size changes, "No next …", find results, "Conversation saved".
- FR-7.6: If notifications are unavailable (the call returns false), the target line is selected so NVDA reads "selected …".
- FR-7.7: Sending announces "Message sent", or "Message waiting" while Claude works, or "Answer sent" when a prompt waited. "Claude is responding" is announced once per output marker when the first reply, thinking or tool line appears after it. Claude's own bracketed announcements (`[accept edits on]`, §4.4 item 16) are spoken as they appear; that confirms a Shift+Tab mode change. "Claude is ready" once per start, when the first mode line appears with Claude idle.

### FR-8 Project folder

- FR-8.1: The folder is chosen in this order: positional argument or `--project`; the current directory unless it is the app's own; the last folder from settings; otherwise the folder picker before Claude starts.
- FR-8.2: Project → **Current folder: C:\path** shows the full path; activating it copies the path and announces "Copied".
- FR-8.3: Project → **Change folder…** (Ctrl+N; Ctrl+O is Claude's key, D14) opens the standard folder picker (`FolderBrowserDialog`, path typeable), then restarts Claude there with the system line `System: Project folder changed to C:\path` and the announcement "Project folder changed to <name>".
- FR-8.4: The folder name is in the title (`<name> - AxClaude`) and the full path in the status bar.
- FR-8.5: Project → **Recent folders** lists the last 10 that still exist; choosing one restarts Claude there.
- FR-8.6 SHOULD: `install.ps1` registers **Open in AxClaude** in the right-click menu of folders and of a folder's background (`HKCU\Software\Classes\Directory\shell\AxClaude` and `Directory\Background\shell\AxClaude`, commands `"AxClaude.exe" "%1"` and `"%V"`), a Start menu shortcut and the `axclaude` shim; `-Uninstall` removes them and the program folder. Per user, no administrator rights.

### FR-9 Command line

```
AxClaude.exe [<folder>] [--project <folder>] [--claude <path>] [--cols N] [--rows N]
             [--no-ax] [--record <file.vt>] [--] [claude arguments…]
```

- FR-9.1: Everything after `--` goes to `claude` verbatim (`--continue`, `--resume <id>`, `--model opus`, `--permission-mode plan`).
- FR-9.2: `--no-ax` omits `--ax-screen-reader` (testing only). `--record` writes the raw stream to a file (the PtyCapture format with the `.chunks.txt` index); Options → Record raw stream for a bug report… does the same at run time. `--cols`, `--rows`, `--claude` override the settings file.
- FR-9.3: A GUI executable started from a console returns at once. `publish.ps1` builds `publish\win-x64` (the self-contained `AxClaude.exe`, `install.ps1`, the guide as `README.md`) and zips it as `publish\AxClaude-<version>-win-x64.zip`; `install.ps1` (run by `publish.ps1` unless `-NoInstall`) puts the files in `%LOCALAPPDATA%\Programs\AxClaude`, writes the `axclaude.cmd` shim in `%USERPROFILE%\.local\bin` (on `PATH` since Claude Code's installer uses it), the Start menu entry and the Explorer entry, and unblocks the files so SmartScreen warns at most once.
- FR-9.4: In development: `dotnet run --project src/AxClaude -- <folder>` or `run.ps1`.
- FR-9.5 SHOULD: `--help` (`-h`, `-?`, `/?`) shows the usage in a message box; `--version` the version; a bad command line its error. These are the only message boxes: no window exists yet (D23).

### FR-10 Menus and status

- **Project (P):** Current folder: … | Change folder… Ctrl+N | Recent folders ▸ | Restart Claude Ctrl+Shift+R | Save conversation as… Ctrl+S | Exit Alt+F4
- **Session (S):** Send message Ctrl+Enter | Interrupt Claude (send Escape) Shift+Esc | Send Ctrl+C Ctrl+Shift+C | Send Ctrl+D | Send Tab | Send Shift+Tab (next permission mode) Ctrl+Shift+M | Send Ctrl+O (Claude's detailed view on or off) Ctrl+O | Send Up | Send Down
- **Navigate (N):** Go to message field Ctrl+Tab | Go to conversation Ctrl+Tab | Latest response Ctrl+Shift+O | Find… Ctrl+F | Find next F3 | Find previous Shift+F3 | Bookmark this line Ctrl+Shift+K (or `m` in the conversation) | Next bookmark `k` | Previous bookmark Shift+K
- **Options (O):** Announce when Claude is done ✓ | Play a sound when Claude is done ✓ | Flash the taskbar button when Claude is done ✓ | Speak replies as they arrive | Show the time in the Input and Output lines | Font… | Larger text Ctrl+Plus | Smaller text Ctrl+Minus | Use the Windows text size | Record raw stream for a bug report… (reads Stop recording while active) | Open settings file
- **Help (H):** Keyboard shortcuts F1 | User guide (`docs/user-guide.md`, embedded) | Claude Code documentation (web) | Claude Code slash commands (web) | Claude Code keys (web) | Claude Code command line (web) | Install or update Claude Code (web) | Copy diagnostics | About. The web items open `https://code.claude.com/docs/en/overview`, `/commands`, `/interactive-mode`, `/cli-reference` and `/setup`.

Ctrl+O belongs to Claude because its tool summaries advertise it. In screen reader mode it toggles the detailed view, whose status row replaces the prompt (§4.4 item 17). The app hides that row, announces "Claude's detailed view is on. Press Ctrl+O to turn it off.", shows `detailed view on, Ctrl+O turns it off` in the status bar until the prompt is back, then announces "Claude's detailed view is off."

Status bar (NVDA+End): `Claude: starting | ready | working Hashing… | waiting for your answer | stopped`, then `1 message waiting` while Claude holds a message it has not taken up (FR-4.7), the permission mode from the mode line, the session name from the console title, and the full project path.

### FR-11 Keys sent to Claude

The Session menu and its shortcuts send raw sequences (Appendix B), fixed in code and listed in the F1 notice. Ctrl+C in the view with a selection stays "copy"; the PTY Ctrl+C is Ctrl+Shift+C.

### FR-12 Settings

- FR-12.1: `%APPDATA%\AxClaude\settings.json` (Appendix C), written atomically on change. A missing or invalid file falls back to the defaults with a system line saying so.
- FR-12.2: Persisted: last folder, recent folders, window placement, option toggles, font, PTY size.
- FR-12.3: Options → Open settings file opens it in the default editor.

### FR-13 Errors and diagnostics

- FR-13.1: Every failure (Claude not found, `CreatePseudoConsole` failure, process exit, settings unreadable) produces a system line and an announcement; a failure of something the user asked for (start, save, open a page or the settings file, a folder that is gone) also shows the **Error** notice (D23). Also handled plainly: a project folder that no longer exists; a Windows without a pseudo console (a sentence naming Windows 10 1809); a recording file that can no longer be written (the recording stops with a system line; the reader thread never dies); a clipboard held by another program ("Could not copy. Try again").
- FR-13.2: A rolling log `%LOCALAPPDATA%\AxClaude\logs\axclaude.log` records lifecycle events. The parser ignores unknown sequences silently; the raw recording is the parser diagnostic.
- FR-13.3: Help → Copy diagnostics copies versions, paths, the Claude state and the last 200 log lines.
- FR-13.4: An unhandled exception on the UI thread is logged and reported in the **Unexpected error** notice, which names the log file (a message box only while no window exists); the app keeps running. Background exceptions are logged.

## 6. User interface

### 6.1 Layout

Menu bar; the conversation (dock Fill); the message field (dock Bottom, three lines); the status bar. Minimum size 600×400. System font and colours throughout.

```
| Project  Session  Navigate  Options  Help                         |
| Conversation (read-only, multi-line, word wrap, vertical scroll)  |
|   # Input 1 / What is 2+2? / (blank) / # Output 1 Reply from Claude / (blank) / claude: 2 + 2 = 4 / Baked for 1s · done 2:47 PM |
| Message to Claude: [                                            ] |
| Claude: ready | manual mode on | C:\projects\myproject             |
```

A notice (D23) takes the place of the conversation and the field, between the menu bar and the status bar: a bold title, the read-only text (or, for Find, a labelled field), and a row of buttons, for example **Close AxClaude?** with **Close anyway** and **Keep working**.

### 6.2 Controls and accessible properties

| Control | Type | AccessibleName | Notes |
| :-- | :-- | :-- | :-- |
| Transcript | `TextBox` Multiline ReadOnly WordWrap ScrollBars=Vertical AcceptsTab=false HideSelection=false MaxLength=0 | "Conversation" | NVDA: "Conversation read only edit multi line". |
| Input | `TextBox` Multiline AcceptsReturn WordWrap, three lines | "Message to Claude" | AccessibleDescription "Ctrl+Enter sends, Enter starts a new line". Ctrl+Enter is handled in `ProcessCmdKey`. |
| Menu | `MenuStrip` | default | Every item has `ShortcutKeys` or `ShortcutKeyDisplayString`. |
| Status | `StatusStrip`, two `ToolStripStatusLabel`s | none: the text is what NVDA+End reads | No `Spring` (such a label is placed outside the strip and not exposed). An item that does not fit is not exposed either, so `StatusLayout` gives the labels fixed widths when the texts do not both fit: the path is clipped first, down to a quarter, then the state; NVDA reads the full text. NVDA looks for a status bar at the bottom-left pixel of the window's UI Automation rectangle, which for WinForms includes the invisible resize border; `MainForm` reports its client area plus the title bar (`MainFormAccessibleObject`, `AccessibleRole.Window`). |
| Notice | `OverlayPanel`: bold title `Label`, read-only multi-line `TextBox` named after the title, an optional labelled single-line `TextBox` ("Text to find"), a `FlowLayoutPanel` of `Button`s with mnemonics | Keyboard shortcuts, User guide, About, Error, Unexpected error, Find, Claude Code was not found, Close AxClaude? | D23. The window hides the conversation and the field, disables the menu, sets `AcceptButton` / `CancelButton` to the notice's default and cancel buttons, and swallows its own shortcuts, the menu's and Alt/F10 while the notice shows; Tab cycles text, field and buttons. Focus lands in the field or at the top of the text (NVDA reads the title, the role and the first line) and returns to the control that had it unless the action moved it (Find lands in the conversation). A notice over a notice replaces it. The Windows folder, file and font pickers stay standard dialogs. |

### 6.3 Keyboard map

Global: Ctrl+Tab / Ctrl+Shift+Tab / F6 toggle field ↔ view; Ctrl+Enter send; Shift+Escape send ESC; Ctrl+F find, F3 / Shift+F3 next / previous; Ctrl+S save; Ctrl+N change folder; Ctrl+Shift+R restart; Ctrl+O send Ctrl+O; Ctrl+Shift+C send Ctrl+C; Ctrl+Shift+M send Shift+Tab; Ctrl+Shift+O latest response; Ctrl+Shift+K bookmark the caret's line; Ctrl+Plus / Ctrl+Minus text size; F1 shortcuts; Alt+F4 exit; Alt menus.

Input field: Enter new line; Ctrl+Enter send; Escape guarded; Shift+Tab send Shift+Tab; Ctrl+Up / Ctrl+Down send arrows; Up on the first line / Down on the last line recall; standard editing keys.

Transcript view: arrows, Home/End, Ctrl+Home/Ctrl+End (native); Page Up/Down, `l` and `m` (app); Enter quote; Shift+arrows select; Ctrl+C copy; Ctrl+A select all; quick keys (FR-5); Escape to the field.

While a notice shows: Tab / Shift+Tab between text, field and buttons; Enter the default (or focused) button; Escape the cancel button; Alt+letter a mnemonic; Ctrl+Plus / Ctrl+Minus still work; every other window and menu shortcut, and Alt or F10 alone, wait. Alt+Space and NVDA+End still work. NVDA's own commands are never intercepted.

### 6.4 Announcement texts

| Event | Text |
| :-- | :-- |
| Jump to a line | the line text; headings add ` heading level N` |
| No target | `No next input` / `No previous heading` etc. |
| `l` | `Line 12 of 340`; `No lines yet` when empty |
| `m`, Ctrl+Shift+K | `Bookmark 3 added` / `Bookmark 3 removed`; `No lines yet` when empty; `The line is no longer in the conversation` when it was trimmed meanwhile |
| `k` / Shift+K | `Bookmark 3, <the line it marks>` (`blank` for an empty line); `No next bookmark` / `No previous bookmark` |
| Enter in the conversation | `Line 12 copied to the message` |
| Escape in the message field | `Escape does nothing here. Press Shift+Escape to interrupt Claude` |
| Find | the line text, prefixed `From the top, ` or `From the end, ` after a wrap; `Not found: <text>`; `Type the text to find` on an empty field |
| Save conversation | `Conversation saved` |
| Ctrl+O | `Claude's detailed view is on. Press Ctrl+O to turn it off.` / `Claude's detailed view is off.` |
| Bell, prompt pending | `Claude needs your answer` |
| Bell, otherwise | `Claude is done` |
| Start | `Claude is ready` |
| Claude exited | `Claude stopped` |
| Send while stopped | `Claude is not running. Press Ctrl+Shift+R to start it again` |
| Folder changed | `Project folder changed to <name>` |
| Copy | `Copied` / `Diagnostics copied`; `Could not copy. Try again` |
| Send | `Message sent` / `Message waiting` / `Answer sent` |
| Reply begins | `Claude is responding` |
| Claude's bracketed row | its text, e.g. `accept edits on` |
| History | the recalled message; `No earlier message` / `No later message`; `Message field is empty` |
| Latest response, none yet | `No response yet` |
| Text size | `Text size 14` / `Font Consolas 12 point` / `Windows text size` |
| Recording | `Recording started` / `Recording stopped` |
| A notice opens | nothing: the focus moves to its text or field and NVDA reads the title, the role and the first line |
| Copy install command | `Copied. Paste it into PowerShell and press Enter` |
| Update found at startup | `AxClaude 1.0.1 is available. See the Help menu` (and a system line) |
| Update now | `Downloading AxClaude 1.0.1`; `AxClaude 1.0.1 downloaded. AxClaude closes now and starts again when the update is installed`; Keep working: `The update was not installed. Help menu, Update AxClaude, when you are ready` |

## 7. Architecture

### 7.1 Projects

```
src/AxClaude.Core/        class library (net10.0), no WinForms
  Pty/        PtyHost (ConPTY P/Invoke), ClaudeLauncher (find claude, command line, environment), StreamRecorder
  Vt/         VtParser (state machine), Screen (cells, rows carrying their Line), Wcwidth
  Transcript/ SessionModel, Line, LineKind, LineClassifier, TranscriptMirror
  Updates/    UpdateCheck (the latest GitHub release: parsing, version comparison), ReleaseInfo
  AppSettings.cs
src/AxClaude/             WinForms app (net10.0-windows): MainForm, TranscriptView, OverlayPanel, HelpText,
                          StatusLayout, StartupOptions, Updater (download, hand-over to install.ps1), Log, Program,
                          AxClaude.ico (tools/make-icon.ps1)
tests/AxClaude.Tests/     xunit: FixtureTests, ReplayTests, SessionModelTests, TranscriptMirrorTests, SettingsTests, UpdateCheckTests
tests/fixtures/           *.vt recordings, *.vt.chunks.txt timing, *.expected.txt transcripts
tools/PtyCapture/         recorder; tools/release-notes.ps1 (the CHANGELOG section of a version)
docs/                     user-guide.md (embedded, shipped as README.md), nvda-test-plan.md
.github/workflows/        build.yml (tests on push), release.yml (tag → zip → GitHub release); release.ps1 starts it
publish.ps1, install.ps1  build the zip; install or remove for the current user
```

### 7.2 Data flow

```
ConPTY output pipe ──(reader thread, one BeginInvoke per burst)──> UI thread: SessionModel.Feed = UTF-8 Decoder ──> VtParser ──> Screen
        ──(frame end: ESC[?25h, or 100 ms quiet)──> SessionModel.EndFrame (row text into lines, chrome, classification, echoes, status)
        ──Changed──> TranscriptView.Sync through TranscriptMirror (edits, caret) + announcements + status bar
Input field / menus ──> MainForm write queue (paced writes) ──> ConPTY input pipe
```

### 7.3 Threading

- Reader thread (`PtyHost`): blocking `Read`; chunks are collected in an inbox and one `BeginInvoke` per burst drains it on the UI thread, so a large output never queues hundreds of window messages ahead of the user's keys; while recording, each chunk is also written to the `.vt` file.
- UI thread: owns the parser, the screen and the `SessionModel`. A frame ends at `ESC[?25h` or after 100 ms without output; `Changed` applies the view edits with redraw suspended (`WM_SETREDRAW`) and restores the caret (§7.7).
- Writes: a queue drained on the UI thread with a delay after each write (30 ms between lines, 100 ms before `\r`).

### 7.4 PtyHost

`PtySession` from `tools/PtyCapture` plus the environment builder (FR-1.3), the std-handle guard (FR-1.4), the graceful stop (FR-1.7) and an `Exited` event from a wait thread. The console size is fixed (D9).

### 7.5 VT parsing and the screen model

The stream is interpreted by a headless screen model, not stripped with regular expressions, because ConPTY expresses in-place rewrites and cursor parking with cursor movement and erase sequences. The model is deliberately small.

**Screen** = `rows × cols` cells, cursor, saved cursor, scroll region, pending-wrap flag, `Dirty` flag per row. Every row carries a stable **line identity** (a `Line` created the first time the row receives text). Scrolling moves rows up and appends a fresh row; the row that leaves the top is **committed**. Insert-line creates a row at the insertion point (its line goes before the displaced row's line) and discards the row pushed out of the region; delete-line removes the row's line. Erasing a row keeps its identity, so a cleared and overwritten dialog updates existing lines. Rows below the cursor row are not part of the transcript.

**Frames.** `ESC[?25h` ends a frame; a 100 ms quiet period also ends one. Chrome, finality and announcements are evaluated at frame end only.

Handled sequences (Appendix A): printable text with wcwidth-aware cell placement and deferred wrap; CR, LF, BS, TAB, BEL (event); CSI cursor moves, erase, insert/delete, scroll, scroll region, save/restore; `ESC[?25h` (frame end); OSC 0/2 title (event); DCS/APC/PM/SOS skipped; `ESC c` and `ESC[!p` reset. Every other sequence, including SGR and the other private modes, is ignored.

**Row text.** At frame end `PullRowText` copies the trimmed text of every dirty row into its line. Rows Claude hard-wrapped are flagged after classification (`JoinWrappedRows`, FR-3.2a) and joined by the mirror.

**Finality.** A line is `Committed` once its row scrolled off or the screen was reset; until then it may be rewritten. Echo handling and speak-replies treat a line as settled when it is committed, followed by another visible line, or Claude is idle.

**Robustness.** The parser produces the same transcript wherever chunk boundaries fall (tested with random splits), ignores unknown sequences without desynchronising, and caps numeric parameters at 65535. Partial scroll regions have not been observed; rows they discard are dropped silently.

### 7.6 Transcript model and classification

`Line { Id; Text; Kind; HeadingLevel; JoinedToPrevious; Committed; Hidden }`. `LineKind`: `Plain`, `InputMarker`, `OutputMarker`, `System`, `UserMessage`, `Bookmark`, `UserEcho`, `ClaudeReply`, `Thinking`, `Tool`, `ToolError`, `Error`, `Warning`, `Prompt`, `PromptOption`, `TurnSummary`.

Classification runs at frame end for every uncommitted line, on the trimmed text: `^you:` UserEcho; `^claude:` ClaudeReply; `^thinking:` Thinking; `^tool:` or `(ctrl+o to expand)$` Tool; `^tool error:` ToolError; `^error:` Error; `^warning:` Warning; `^Permission Required:`, `^Enter selection`, `^Enter y/n` Prompt; `^\d+\. ` or `^[yn]\. ` within 12 visible lines after a Prompt → PromptOption; `^\S+ for … · done ` TurnSummary; `^\[Screen Reader Mode:` System.

Headings are structural (§4.4 item 3): a line (after a `claude:` label) of 1 to 80 characters, not ending in `.`, `,`, `;`, `:`, `?` or `!`, not a list item, with at least one letter, followed by a blank line, is level 2. A line that still begins with `#`s uses the `#` count. The markers are level 1 (FR-4.1). App lines (`Line.IsMarker`) are never classified or hidden.

### 7.7 TranscriptView and TranscriptMirror

`TranscriptMirror` (Core, unit tested) holds the visible lines as one text with their start offsets, separators (`\r\n`, or a space for a `JoinedToPrevious` line, nothing when it starts with one), display line numbers for `l`, and the search behind Ctrl+F. `Update(lines)` compares the new visible list with the old one by line identity and separator (common prefix and suffix; everything between is replaced as one block together with the separator in front of it; a prefix or suffix line whose text changed is replaced on its own) and returns `MirrorEdit`s in descending start order plus `MapPosition(oldCaret)`: the same column of the same line when it is still shown, otherwise the start of the next line still shown, or of the replacement. A frame with no visible change returns at once; otherwise only the entries after the first changed line are recomputed, and the line lists are reused between frames, so a spinner tick allocates nothing and no 20 000-entry list goes to the large object heap. The update is valid until the next call.

The view applies the edits with `Select` + `SelectedText` (EM_SETSEL / EM_REPLACESEL), never `Text = …`, with redraw suspended, then restores the caret through `MapPosition`. Output within 250 ms of a key press in the focused view is held until the keys stop, because NVDA reads the caret line in several messages and text that shifts between them is read wrong; announcements and the status bar are not held. Without focus, the caret goes to the start of the last line and the view scrolls to it with `EM_SCROLLCARET` sent directly: WinForms' `ScrollToCaret` first copies the whole text. Edits land at the bottom of the text, which keeps the word-wrapped EDIT control cheap; an edit near the top re-wraps everything below it (about 0.7 s at 20 000 lines, so a trim, FR-3.6, is a rare one-off).

### 7.8 Announcer

`Announce(text, interrupt)` → `RaiseAutomationNotification(ActionCompleted, interrupt ? MostRecent : All, text)` on the transcript view, or on the notice's text or field while one shows. On `false`, the selection fallback (FR-7.6).

### 7.9 Settings and command line

`AppSettings` loads `settings.json` with `System.Text.Json` (reflection based; the app is not trimmed), sanitises ranges, writes via temp-file rename. `StartupOptions` is a hand-written parser: unknown options before `--` are errors; everything after `--` is opaque.

## 8. Design decisions

- **D1 WinForms, not WPF.** Real Win32 controls (EDIT, menus, status bar) with the most mature NVDA support; no custom UIA providers.
- **D2 A read-only multi-line `TextBox` for the transcript.** A `ListBox` has no partial selection, word wrap or character review and its type-ahead conflicts with quick keys; `RichTextBox` is slower and quirky; a custom control has a large accessibility surface. The plain EDIT control is what NVDA's own log viewer uses.
- **D3 ConPTY with screen reader mode, not `-p --output-format stream-json`.** The user wants the CLI's own interactive behaviour (slash commands, prompts, dialogs, modes) with the rendering Anthropic maintains. Stream-json drops interactive prompts unless the undocumented control protocol is implemented. It remains the fallback; the transcript and view layers would be reused.
- **D4 Own P/Invoke, no PTY package.** Five functions; the reference implementation already works.
- **D5 .NET 10 (LTS)**; `global.json` pins the SDK feature band; self-contained single-file publish.
- **D6 A screen model with diffing rather than escape stripping.** §4.2–4.4 show in-place redraws and cursor parking; stripping would duplicate or garble lines.
- **D7 App markers plus Claude's labels.** The markers are deterministic and immediate; the labels give the inner structure.
- **D8 UIA notifications for announcements.** They speak without stealing focus or moving the caret; NVDA supports them since 2018.3. Fallback: selection.
- **D9 A fixed, wide console (240 columns).** Claude hard-wraps at the console width; 240 keeps most paragraphs on one logical line and the EDIT control wraps visually. The window never resizes the console.
- **D10 No auto-speak by default.** The point is a calm, navigable buffer; the bell announcement and `o` are the fast path. Auto-speak is an option.
- **D11 Ctrl+Enter sends, Enter is a new line.** What Claude Code users do, and it makes the field an ordinary multi-line edit for NVDA.
- **D12 No send lock.** Claude Code queues or steers with a message sent while it works; the app keeps the markers honest (FR-4.7) and says "Message waiting".
- **D13 Own input history.** Claude's history recall draws on the hidden prompt row.
- **D14 Ctrl+O belongs to Claude.** Change folder moved to Ctrl+N.
- **D15 System.Text.Json for settings.** Ships with the runtime.
- **D16 A message sent while Claude works stays out of the conversation until Claude takes it up.** Claude Code owns the queue and draws the waiting message; the app only reports it and prints the block when the echo arrives. Placing the markers at once put them in the middle of the running reply and moved them later.
- **D17 Wrapped rows are joined in the view, not in the model.** Each screen row keeps its `Line`, so rewrites and caret mapping keep working; only the separator changes. File-only switch.
- **D18 Installation is a script.** `install.ps1` does everything a per-user installer would with no dependency, no elevation and nothing to sign.
- **D19 The user guide is one Markdown file**, embedded for Help → User guide and copied into the zip as `README.md`, so the two can never differ.
- **D20 Escape is guarded in the message field.** Blind users press Escape by reflex, and an unguarded one interrupted the running turn. Ctrl+Escape was the first choice, but Windows opens the Start menu on it, and swallowing it needs a global keyboard hook, which must not be installed next to a screen reader.
- **D21 Quoting a conversation line is a literal text block.** No protocol, no hidden state; the user edits or deletes it like any text.
- **D22 The app prints the message itself; Claude's echo is only hidden.** Moving two markers around the echo broke as soon as a message held a blank line. The exchange block is text the app owns, printed once; the echo is matched by text with whitespace ignored and hidden. Nothing moves afterwards.
- **D23 The app never opens a second window of its own.** A screen reader user loses popup windows: a dialog next to the main window is easy to leave behind when the screen reader's focus moves, and then both are stuck. Every dialog is a notice drawn inside the main window by `OverlayPanel` (Find, the shortcuts, the guide, About, errors, the crash report, Claude Code was not found, the close question). The only separate windows are the standard folder, file and font pickers, and the `--help`, `--version` and bad-command-line message boxes shown before any window exists.
- **D24 Performance is measured, not assumed.** The per-frame path (parser, `EndFrame`, mirror update, EDIT control edits) was benchmarked at 20 000 lines (2026-09-19). Three wastes were found and removed: a closure allocated on every iteration of the echo loop (700 KB a frame), the mirror's full rebuild on every frame, and WinForms' `ScrollToCaret` copying the text. A frame end now costs 0.12 ms and allocates nothing at 18 000 lines, down from 0.54 ms. No further machinery (virtualised views, background parsing) is warranted.
- **D25 Updates come from GitHub releases, and the user chooses.** A release is a version tag: GitHub Actions builds the same zip that `publish.ps1` builds locally and attaches it (`.github/workflows/release.yml`, started by `release.ps1`). The app asks for the latest release once at startup (an opt-out setting) and installs only after Update now: no background downloads, no server of the app's own, no installer beyond `install.ps1`, which already exists. Handing over to the new version's own installer means the running executable is never overwritten while it runs, and a failed update leaves the installed version in place.

## 9. Repository, build and run

- `dotnet build AxClaude.sln`, `dotnet test AxClaude.sln`, `dotnet run --project src/AxClaude -- "C:\path"`, or `run.ps1`.
- `publish.ps1`: `dotnet publish src/AxClaude -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` into `publish\win-x64`, plus `install.ps1`, `LICENSE` and the guide as `README.md`, zipped as `publish\AxClaude-<version>-win-x64.zip`, then `install.ps1` unless `-NoInstall`. The version is `<Version>` in `AxClaude.csproj`.
- Warnings are errors in Release; nullable enabled everywhere.
- Git: `main` is always buildable; imperative commit subjects; Co-Authored-By trailer for commits made with Claude.
- Licence: MIT (`LICENSE`), copyright Dr. Kyle Keane, www.kylekeane.com; the same line is in `AxClaude.csproj` (`Authors`, `Copyright`), Help → About, the guide and the README. The only condition is that the notice stays with copies.
- Releases (FR-1.10, D25): `release.ps1 <version>` checks that CHANGELOG.md has a `## <version>` section, runs the tests, sets `<Version>`, commits "Release <version>", tags `v<version>` and pushes. `.github/workflows/release.yml` (a `v*` tag, or `gh workflow run release.yml -f tag=v1.0.1`) checks the tag against `<Version>`, tests, runs `publish.ps1 -NoInstall` and creates the GitHub release with the zip and the CHANGELOG section (`tools/release-notes.ps1`) as notes. `.github/workflows/build.yml` runs the tests on every push to `main` and every pull request.

## 10. Testing

Unit tests (xunit, `tests/AxClaude.Tests`, 49 tests):

- FixtureTests: each `tests/fixtures/*.vt` parses to its `*.expected.txt`; random chunk boundaries give the same transcript; wrapped reply rows join and tool rows do not.
- ReplayTests: `long-message` and `steering` replayed chunk by chunk with the app's send timing; the exchange block precedes the hidden echo, also for a message sent while Claude works.
- SessionModelTests: the exchange block and echo hiding (blank lines, wrapped rows), a message sent while Claude works (held until its echo; the fallback), time stamps, the join rule, slash commands and prompt answers, mode announcements, the Ctrl+O view, rows below the cursor, rewrites, scrolling, blank rows, wide characters, reset, heading levels, bookmarks (toggling and numbering; a wrapped line, and the join and heading rules around a bookmark).
- TranscriptMirrorTests: edits and caret mapping (appends, a rewritten line, inserts and removals above the caret, a hidden line, a replaced block, joined lines, a separator change), display line counting, the search, and, for every frame of every fixture, the edits reproduce the visible text and a caret stays on its line.
- SettingsTests: round trip; a missing or broken file falls back to the defaults.

The WinForms code has no automated tests; `docs/nvda-test-plan.md` covers it, and off-screen probe forms check layout and key handling without disturbing the user. Fixtures are recorded with `tools/PtyCapture` and reviewed for personal data before commit (`tests/fixtures/README.md`).

## 11. Risks and open questions

| Id | Risk | Mitigation |
| :-- | :-- | :-- |
| R1 | ConPTY emits an unexpected repaint pattern that the screen model turns into duplicated or lost lines. | Screen model plus fixtures; Record raw stream captures the stream when it happens. |
| R2 | NVDA does not speak UIA notifications on some machine. | Selection fallback (FR-7.6). |
| R3 | Enter in the same chunk, bracketed paste through ConPTY. | Resolved by the recordings (§4.4 items 1, 8, 14). |
| R4 | Claude Code changes its labels or rendering. | Fixtures pin the version; CLAUDE.md asks for a new fixture per version bump; patterns live in one class. |
| R5 | The EDIT control slows down on very large transcripts. | The cap (FR-3.6); edits land at the bottom (§7.7). |
| R6 | Slash-command autocomplete needs a second Enter. | Ctrl+Enter sends a bare Enter. |
| R7 | The user's `tui: fullscreen` setting. | Ignored in screen reader mode; `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1` as belt and braces. |
| R8 | The heading heuristic misses a heading or promotes a short line. | Conservative rule; `o`, `d` and `c` give reliable coarse navigation. |
| R9 | Tool detail rows collapse at the end of a turn. | Matches the terminal; Record raw stream keeps everything. |
| R10 | Claude changes how a waiting message is drawn. | The `steering` fixture pins it; a change degrades to a visible echo, not scrambled markers. |

Open: **Q1** the product name; **Q3** hide the `you:` echo (yes); **Q4** `o` targets the output marker, `c` Claude's own lines; **Q5** sound on bell (yes). **Q2** was decided on 2026-09-19: `# Input n` and `# Output n Reply from Claude`.

## 12. Status

Built 2026-09-18 to 2026-09-19: everything in §5, checked with NVDA (test plan sections 1 to 10) on the development build. The 2026-09-19 consolidation pass simplified every announcement, menu label and document, removed unused code (OSC 133, bracketed paste, soft-wrap and cursor-column tracking, unused view members) and fixed the per-frame wastes (D24).

Released 1.0.0 on 2026-09-19 through the release workflow (§9), with the MIT licence, the update check (FR-1.10) and the bookmarks.

To do:

- [ ] A short NVDA run of the renamed announcements (test plan 2.6, 5.4, 5.5, 6, 8.1, 8.4, 9.3, 9.9, 10.1), the bookmarks (test plan 4.12) and the update flow (test plan 11) on the released build; anything found goes into 1.0.1 through `release.ps1`.

## Appendix A — Control sequences the screen model handles

C0: BEL (event), BS, HT (next multiple of 8), LF/VT/FF (line feed), CR, ESC. Other C1 and control bytes are ignored.

ESC: `7` save cursor, `8` restore, `D` index, `E` next line, `M` reverse index, `c` reset; sequences with intermediates (charset designations) are ignored.

CSI (parameters default to 1 unless noted): `A` up, `B` down, `C` forward, `D` back, `E` next line, `F` previous line, `G` and `` ` `` column, `H`/`f` position, `d` row, `J` erase in display (0/1/2), `K` erase in line (0/1/2), `X` erase characters, `@` insert blanks, `P` delete characters, `L` insert lines, `M` delete lines, `S` scroll up, `T` scroll down, `r` scroll region, `s`/`u` save/restore, `! p` soft reset. `? 25 h` ends a frame; every other private mode, SGR (`m`) and every query is ignored.

OSC: `0`/`2` title (event); others ignored. DCS, APC, PM, SOS: skipped to ST.

Cell width: `Wcwidth` returns 0 for combining marks and format characters, 2 for East Asian wide and emoji ranges, 1 otherwise. Continuation cells are skipped when a row is read.

## Appendix B — Keys sent to Claude

| Action | Bytes |
| :-- | :-- |
| Enter | `\r` |
| Escape (Shift+Escape in the app) | `\x1b` |
| Ctrl+C | `\x03` |
| Ctrl+D | `\x04` |
| Tab | `\t` |
| Shift+Tab | `\x1b[Z` |
| Up / Down | `\x1b[A` / `\x1b[B` |
| Ctrl+O | `\x0f` |
| Ctrl+J (newline inside the draft) | `\n` as a write of its own |

ConPTY converts these to key events for the client; win32-input-mode is not used.

## Appendix C — Settings file

```json
{
  "lastProjectFolder": "C:\\projects\\myproject",
  "recentFolders": ["C:\\projects\\myproject"],
  "claudePath": null,
  "ptyColumns": 240,
  "ptyRows": 50,
  "fontFamily": null,
  "fontSize": 0,
  "fontBold": false,
  "announceBell": true,
  "soundOnBell": true,
  "flashTaskbar": true,
  "speakReplies": false,
  "markerTimeStamps": false,
  "joinWrappedLines": true,
  "checkForUpdates": true,
  "maxTranscriptLines": 20000,
  "window": { "x": 100, "y": 100, "width": 1000, "height": 700, "maximized": false }
}
```

`fontFamily` null with `fontSize` 0 is the Windows message font. `claudePath` is set by Locate claude.exe or by hand. `joinWrappedLines` has no menu item. `checkForUpdates` is Options → Check for updates when AxClaude starts (FR-1.10). Written atomically; a missing or unreadable file gives the defaults and a system line. Marker formats and chrome patterns are fixed in code: a pattern change needs a fixture anyway.

## Appendix D — Glossary

- **ConPTY / pseudo console:** the Windows API that lets a program host a console application and exchange a VT byte stream with it.
- **VT sequence:** an escape sequence that moves the cursor, erases text or changes modes.
- **Screen reader mode:** Claude Code's `--ax-screen-reader` rendering.
- **Marker line:** a line the app inserts (`# Input n`, `# Output n Reply from Claude`, `System:`).
- **Exchange block:** the input marker, the message, a blank line, the output marker, a blank line (FR-4.1).
- **Bookmark:** an app line `Bookmark n` that the reader drops in front of a line with `m` and finds again with `k` (FR-3.9).
- **Quick key:** a single-letter navigation command in the transcript view, modelled on NVDA browse mode.
- **UIA notification:** a UI Automation event that asks screen readers to speak a string without changing focus.
