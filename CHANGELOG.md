# Changelog

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
