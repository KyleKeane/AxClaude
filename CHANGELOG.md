# Changelog

## Unreleased

- Fixed: a `you:` row that repeats no sent message (quoted in a tool result, replayed at startup, or printed for a slash command) no longer claims a waiting message's Input and Output block, which could land far up in the conversation.
- Fixed: the headings for replayed exchanges stopped at a quoted "[Screen Reader Mode:" row, leaving earlier exchanges without them.
- Fixed: rows that scrolled past within one burst of output were never classified, so `i`, `c` and `t` skipped them.
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
