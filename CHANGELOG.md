# Changelog

## 1.6.0 - 2026-09-25

- A question whose answers are too long for one line now lists every answer. Claude wraps a long answer onto the next line, and AxClaude used to stop reading answers there, so the first answers were missing from the list and read as part of the question.
- Claude's "Review your answers" no longer needs a y: AxClaude submits it for you after your last answer, unless a question was left without an answer. Options, "Review answers before they go to Claude", brings the review back.
- Enter on a line of one of Claude's screens (`/status`, `/tasks`, `/help`) closes it, as Escape does, and AxClaude says "Closed". The keys listed at the end still do what they name.
- Claude's working line ("Puzzling…") and its mode line ("auto mode on (shift+tab to cycle) · esc to interrupt") are no longer read out at the end of a reply. When the window was full, Claude left its cursor on the mode line, and AxClaude took both for part of the reply.
- Tab and Shift+Tab now simply move between the conversation and the message field, or Claude's question in its place. Claude's permission mode, which Shift+Tab used to change, is Ctrl+Shift+M from anywhere in the window.
- More keys in every list of Claude's answers, settings and screens: a number jumps forward through the answers starting with it (1, 10, 11), and now Shift with the number jumps back; 0 goes through the answers 1 to 9, Shift+0 back; Page Up and Page Down move ten answers; Home and End, and Ctrl+Home and Ctrl+End, go to the first and the last.
- Your files stay out of your project's git. Save conversation and Record raw stream now offer your Documents folder, with names starting `axclaude-`, and when the project is a git repository AxClaude lists those names in its local exclude file (`.git\info\exclude`, never committed), so git ignores them wherever you save them. Options, "Keep AxClaude's recordings and saved conversations out of git", on by default, turns this off.
- For developers: PtyCapture's `--dump` hides e-mail addresses, account paths, pipe names and session ids unless `--raw` is given, and a test fails when any file in the repository holds an e-mail address or an account path.

## 1.5.1 - 2026-09-25

- Fewer words: every announcement is shorter ("Ready", "Sent", "Responding", "Done", "Claude asks"), and the message field has no description read on every arrival.
- Claude's questions and screens now take the place of the message field instead of covering the window. The conversation stays in view with the whole question in it. The answers are a list named with the question, which NVDA reads each time you arrive; Ctrl+Tab or F6 moves between the conversation and the answers. Enter answers, Escape cancels, Space ticks when several answers are allowed. When a question comes, you are taken to it at once and whatever NVDA was reading stops; Ctrl+Tab goes back to the conversation. Options, "Go to Claude's questions at once, interrupting speech", turns that off: you then stay where you are and hear the question after what is being read. The message field comes back, with what you had typed, when Claude has your answer. Nothing can be sent into a question by mistake, since the field is not there.
- Enter sends the message, and Shift+Enter starts a new line (Ctrl+Enter sent before). The same in New session's Custom arguments.
- When Claude asks several questions at once, each says where you are ("Question 1 of 2: …"), and after your answer AxClaude says what comes next: the next question, the review, or your own answer after Other. A question that follows your answer comes without the trill.
- Choosing "Other" (or any answer that asks for your own words) puts a field in the list's place, named with the question and what you chose. Enter sends your words, Escape goes back to the list. This replaces the "Your own answer" field under the answers.
- /config lists Claude's settings with their values. Enter on one changes it: a true or false setting asks True or False first, any other setting opens Claude's list of its values. The list comes back on the setting you changed. Escape saves and closes.
- /mobile is read with all its lines.
- A background agent counts as background work: closing AxClaude and New session warn about it, and the status says so.
- Installing and updating remove the previous AxClaude completely first, also one installed with the earlier install.ps1, and then install fresh. The installer says what it found. Your settings stay.
- Removing the old installation waits a few seconds if Windows still holds the program file after AxClaude closes.

## 1.5.0 - 2026-09-24

- A question that offers "Other" has a field "Your own answer" in its notice (Alt+Y): type your answer there and press Enter, and AxClaude sends it when Claude asks for it. If Claude asks for your own words some other way (Other chosen from the list, or "No, keep planning" when you turn down a plan), AxClaude puts you in the message field and says so.
- A question that allows several answers opens the notice with check boxes: Space or an answer's number ticks it, and Enter sends all the ticked answers.
- `q` and Shift+Q in the conversation go to the next and previous question Claude asked. AxClaude writes a line `Question: …` in front of each question and screen, which stays when Claude replaces the question with your answer.
- Claude's questions with several parts come one at a time with the right title, and the review at the end opens the answer notice too. Permission questions (a Bash command, a file, plan approval) are titled by their "Permission Required:" line.
- Fixed: answers of more than one character did not reach Claude. Claude takes an answer as typed keys and ignores several characters sent at once, so "12" in a long list, "1,3" for several answers and your own words were dropped; every answer now goes one keystroke per character.
- Fixed: choosing "Other" left you with nowhere to type.
- A question that Claude redraws in passing no longer closes its notice or is announced again.
- Installing is a double-click: download `install.cmd` from the release page and open it, and it fetches and installs the latest AxClaude. The zip has it too. No PowerShell setting has to change.
- AxClaude is listed in Settings, Apps (and Add or remove programs), with its version and an Uninstall that removes everything the installer added and keeps your settings. While AxClaude is running, Uninstall removes nothing and says to close it first.
- Updates: an installer error is now written to the update log instead of vanishing, and older downloaded versions are removed.

## 1.4.0 - 2026-09-24

- Claude's questions open an answer notice: the workspace trust question, the lists of commands such as `/model`, `/effort`, `/theme` and `/resume`, and Claude's own multiple-choice questions. A quick high trill, "Claude asks:" with the title, and the focus on the current answer; the arrow keys or the answer's number choose, Enter answers, Escape cancels. Alt+M, "Answer in the message field", leaves the question open for an answer you type. The next question of the same screen opens its own notice, and a notice whose question went away closes. Claude's full question stays in the conversation.
- Screens that wait for a key, such as `/status`, `/usage`, `/help`, `/tasks` and `/goal`, open a screen notice with the screen's text (for `/status` and `/help` it never reached the conversation) and a button for every key the screen offers. They also count as waiting for your answer now.
- While Claude waits for an answer, Ctrl+Enter sends only an answer. A message you typed stays in the field and AxClaude says so; Ctrl+Enter again sends it anyway. A message typed into the trust question used to be lost. Options, "Answer Claude's questions in a notice", turns the notices and this protection off.
- Every notice ends with a line naming its keys ("Enter: Close anyway. Escape: Keep working."), and a notice you did not open yourself (Claude's questions, errors, Claude Code not found, the update offer) plays the notice trill and ignores Enter for a moment, so keys meant for the message field do not answer it.
- Work Claude runs in the background, such as a command started with `run_in_background`, is shown in the status bar ("1 shell in the background"), and closing AxClaude or starting a new session asks first while it runs.
- Restart Claude is now "Force Claude to restart" (Ctrl+Shift+R) and asks first while Claude runs, since it quits Claude Code at once and ends anything it runs in the background.
- Fixed: old content was read out again, and copies of earlier turns appeared in the conversation, when Claude repainted the whole conversation from the top of the screen in a long session. The repaint is now recognised and its copies are hidden; a reply line is also never spoken twice within one reply.
- Fixed: Claude's mode line with background work ("auto mode on · 1 shell · ↓ to manage") appeared in the conversation.
- `docs/claude-screens.md` lists every slash command with what its screen shows and the notice it gets.

## 1.3.0 - 2026-09-20

- Change folder and Recent folders clear the conversation before Claude starts in the new folder, as New session does: the window holds only the "Project folder changed" line and what Claude prints there, with that folder's earlier exchanges under their "from previous session" headings when it has any. Restart Claude keeps the conversation.
- Fixed: the ticks stuttered on the speaker and went ragged once many had played. Every sound went through winmm's PlaySound, which opens and closes the audio device for each call and backs up under quick succession. The sounds now play through one wave-out device that stays open for the whole run: each sound is prepared once and queued with a single write, a chime replaces whatever plays, and a tick is dropped while the previous one is still on its way, so they never pile up. A tick that arrives during a chime now follows it instead of being lost.
- The tool tick is two lower knocks of 12 ms each instead of two 5 ms strikes, so it can be heard on a small speaker. The option that turns the ticks off is now called "Tick for each new line and tool call from Claude"; it always covered both.
- Restart Claude (Ctrl+Shift+R) says "Restarting Claude".
- `run.ps1 -New` starts a new conversation; a bare `--` never reached the app because PowerShell takes it for itself.
- `tools/PtyCapture` records through the app's own console host and child environment (`PtyHost`, `ClaudeLauncher`), so a recording shows exactly what the app sees, and the tool no longer carries a second copy of the ConPTY code.
- The specification, the guide, the test plan and the working notes were checked against the code and brought up to date; the test project shares its helpers in one place.

## 1.2.1 - 2026-09-19

- Fixed: Ctrl+O is no longer sent to Claude. In screen reader mode Claude's detailed view redraws the whole conversation from the first message on screen, which gave the conversation a second copy of every message and made Speak replies read old replies again. Ctrl+O now says why it does nothing and points at `--verbose` for tool output; the Session menu item is gone.

## 1.2.0 - 2026-09-19

- New session (Ctrl+N) clears the conversation before Claude starts again: the window holds only the "New session in" line and what the new session prints, with the earlier exchanges under their "from previous session" headings when the last conversation is continued. Messages and bookmarks count from 1 again. Restart Claude keeps the conversation as before.
- Fixed: the status bar said "ready" with the old permission mode while a restarted Claude was still coming up; it now says "starting" until the new session prints its mode line.
- Speak replies as they arrive is now a choice of three in the Options menu: None, First lines only (the first line of each of Claude's messages, where Claude says what it is doing or what it found) and All lines. Each message is spoken in one piece once it is complete, so a long answer no longer stops after its first lines; tool output, thinking and the conversation replayed at startup are never spoken. The setting is `replySpeech` in the settings file; the old `speakReplies: true` is read as All lines.
- Options → Speak each tool call speaks the `tool:` line of every tool Claude runs as soon as it appears, whichever reply choice is in use; tool output stays unspoken. The setting is `speakToolCalls`.
- The tick for a new line is a touch bolder, like a typewriter key, and a tool call has a tick of its own, two lower strikes like a tiny ratchet, so a tool starting is told from a line arriving. Both stay short and quiet.
- The Project menu item that names the current folder is now "Open current folder" (Ctrl+W from anywhere): it opens the folder in File Explorer. It used to copy the path.
- Page Up and Page Down in the message field move one screen of the field at a time and reach the first and last line of the message; the field's own keys did nothing while the message fit its three lines. The conversation's paging works the same way.
- Fixed: the tick for each new line could stop for the rest of the session when the audio device never finished an earlier sound; the tick now yields to a chime by the clock instead of asking Windows whether a sound is still playing.
- Fixed: a `you:` row that repeats no sent message (quoted in a tool result, replayed at startup, or printed for a slash command) no longer claims a waiting message's Input and Output block, which could land far up in the conversation.
- Fixed: the headings for replayed exchanges stopped at a quoted "[Screen Reader Mode:" row, leaving earlier exchanges without them.
- Fixed: rows that scrolled past within one burst of output were never classified, so `i`, `c` and `t` skipped them.
- Fixed: `h` and the heading keys no longer stop on short tool-output rows or on the last line of a code block; a heading is a short row between blank rows inside a reply.
- Fixed: a question is pending only while Claude's cursor sits on its prompt row, so a prompt quoted in a tool result no longer announces "Claude needs your answer" or turns the next message into an answer. The numbered menus of `/effort` and `/model` ("Select with numbers") now count as questions: the typed number is announced as "Answer sent" and gets no Input and Output block.

## 1.1.0 - 2026-09-19

- Ctrl+N opens New session: a notice with the folder and a Choose folder button, a list of ways to start Claude (new conversation, continue the last conversation, choose a conversation to resume, plan mode, edits accepted without asking) and Custom for Claude Code's own command line arguments, one or more on each line. Enter stops the current session and starts the new one; Restart Claude keeps the choice. Change folder stays in the Project menu, above New session, without a shortcut.
- Sounds: a high ping when Claude is ready, a low note when a message is sent, three rising notes when Claude starts replying, the same notes falling when the whole turn is done, two equal notes when Claude needs an answer, and a soft tick for every new line that arrives from Claude. Options, "Click for each new line from Claude" turns the tick off, and "Play sounds: ready, sent, replying, done, question" the chimes, which replace the Windows Asterisk sound.
- Claude continues the last conversation in the folder by default: AxClaude starts it with `--continue` unless the command line says otherwise (`axclaude --` alone starts a new conversation), and New session preselects "Continue the last conversation". A folder with no earlier conversation starts a new one after Claude's own "No conversation found" line. When AxClaude starts, the exchanges Claude replays get their own Input and Output lines, marked "from previous session"; new messages count from 1 as before. Restart Claude keeps the conversation as it is, without a replay under it.
- Backspace in the conversation goes back to the line you were on before the last jump, and again for the jump before that, so a stray `p` or `h` is undone in one press.
- Enter on a conversation line now ends the copied block with the line "_ start of comment on the copied line _" and moves you into the message field, with the caret under it, ready to type the comment.
- `p` and Shift+P jump to the next and previous paragraph, the first line after a blank line. The prompt jump on `p` and the blank-line jump on `b` are gone.
- Session, "Send the waiting message now" (Ctrl+Shift+S) sends Claude's Ctrl+X Ctrl+S, so a message sent while Claude works is taken up at once instead of after its current step. "No message waiting" when there is none.
- Up and Down in the message field no longer bring back earlier messages; they only move through the text you are typing. Earlier messages are in the conversation under their "# Input" lines, and Enter on a line copies it into the message field.

## 1.0.2 - 2026-09-19

- Ctrl+1 goes to the message field and Ctrl+2 to the conversation. When you are there already, the key says where you are.
- A disclaimer in Help, About and at the end of the user guide: no warranty, no liability, no affiliation with Anthropic.
- Enter on a conversation line puts the line's text on its own line in the message, under "Line 12 of 120:".
- The log's start line and Copy diagnostics name the program file and the process, so a report says which copy of AxClaude was running.
- Following a reply as Claude writes it no longer repeats what you just heard: once you have heard the last row, what arrives next is shown on a line of its own, and Down Arrow reads only that. The line reads as one line again once you move on.
- Selecting text in the conversation works while Claude writes: output waits until the selection is gone, Ctrl+C copies the selection and says "Copied", and Escape clears the selection before it goes to the message field.

## 1.0.1 - 2026-09-19

- The Help menu always shows the version you have, the latest release on GitHub and the update action.
- A newer release opens the update notice when AxClaude starts: Enter updates, Escape keeps the version you have. Options, "Check for updates when AxClaude starts" turns the check off.

## 1.0.0 - 2026-09-19

First release.

- Runs `claude --ax-screen-reader` in a Windows pseudo console and shows everything it prints as a line-by-line conversation that NVDA reads with the arrow keys.
- Every message is printed as a block: `# Input n`, the message, a blank line, `# Output n Reply from Claude`, a blank line. Both `#` lines are level 1 headings, Claude's own headings are level 2 and deeper, and Claude's echo of the message is hidden.
- Single-key navigation in the conversation: `i`, `o` or `r`, `h`, `1` to `6`, `c`, `t`, `p`, `e`, `d`, `s`, `b`, `l` for the line number, Page Up and Page Down from the caret, Ctrl+F with F3 and Shift+F3, Ctrl+Shift+O for the latest reply.
- Bookmarks: `m` drops a `Bookmark n` line in front of the current line and takes it away again, `k` and Shift+K walk through them and read the marked line, Ctrl+Shift+K and the Navigate menu do the same from anywhere. They are part of the conversation, so Ctrl+S saves them with it.
- A multi-line message field: Ctrl+Enter sends, Enter starts a new line, Up and Down at the edges recall earlier messages, Shift+Escape interrupts Claude (plain Escape does nothing and says so), Shift+Tab cycles the permission mode, Ctrl+Up and Ctrl+Down reach Claude's menus.
- Enter on a conversation line copies it into the message field under a marker line, so the next message can reply to it.
- Messages sent while Claude works stay out of the conversation until Claude takes them up, with "Message waiting" and the count in the status bar.
- Spoken notices through UI Automation: message sent, Claude is responding, Claude is done, Claude needs your answer, mode changes, Claude's detailed view; a sound and a taskbar flash for the last two.
- Output that arrives while you read never moves the caret, and paragraphs that Claude wrapped at the console width read as one line.
- Project folder from the command line, the Start menu, File Explorer's "Open in AxClaude" or the `axclaude` command; Change folder, Recent folders, Restart Claude.
- Save the conversation as text, the time in the Input and Output lines, "Speak replies as they arrive", font and text size, high contrast through the system colours.
- A "Claude Code was not found" notice with the install command, the install page and a file picker.
- The app's own dialogs (Find, the shortcuts, the user guide, About, errors, Claude Code was not found) are notices inside the window, never separate windows: Enter is the main button, Escape closes, and the rest of the window waits. Closing the app while Claude is still working asks first.
- Help menu with the keyboard shortcuts, the user guide and the Claude Code documentation pages; Copy diagnostics; a rolling log; a raw stream recorder for bug reports.
- Settings in `%APPDATA%\AxClaude\settings.json`; `--help` and `--version`.
- `install.ps1` installs per user without administrator rights and `install.ps1 -Uninstall` removes it; `publish.ps1` builds the self-contained executable and the zip.
- Updates: at startup AxClaude asks GitHub once whether a newer release exists (Options turns that off) and says so. Help, Check for updates asks on demand, and Update now downloads the release, installs it and starts AxClaude again on the same folder.
- Releases are built and published by GitHub Actions from a version tag (`release.ps1`).
- MIT licence.
