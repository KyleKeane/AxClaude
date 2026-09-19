# AxClaude user guide

AxClaude is a Windows program for using Claude Code with a screen reader. It runs Claude Code for you and shows everything Claude says in a plain text window, one line at a time. You type to Claude in a text field at the bottom. It is made for NVDA and works with any screen reader that reads ordinary Windows text fields.

You are talking to the real Claude Code. Slash commands, questions, permission prompts and modes all work as they do in a terminal. AxClaude only adds a window you can read line by line, a few keys to move around, and spoken notices when Claude needs you.

## What you need

- Windows 10 (version 1809 or later) or Windows 11.
- Claude Code, installed and logged in. If it is missing, AxClaude tells you and gives you the install command. To install it yourself, open Windows PowerShell and run `irm https://claude.ai/install.ps1 | iex`, then run `claude` once and follow the login steps. Claude Code needs a Claude subscription (Pro, Max, Team or Enterprise) or a Console account.

## Install AxClaude

1. Extract the zip to any folder.
2. Open PowerShell in that folder and run `.\install.ps1`. If PowerShell refuses, run `powershell -ExecutionPolicy Bypass -File .\install.ps1`.

This puts AxClaude in your Programs folder and adds a Start menu entry, an "Open in AxClaude" entry in the right-click menu of folders, and the `axclaude` command for consoles. No administrator rights are needed.

To update: close AxClaude, extract the new zip and run `.\install.ps1` again. Your settings stay. If Windows SmartScreen warns about an unknown publisher, choose More info, then Run anyway. It asks once.

To remove: run `.\install.ps1 -Uninstall`.

## Start

Claude works in one project folder at a time. Pick how you start:

- Start menu: AxClaude opens the folder you used last time. The first time, it asks you to choose a folder.
- Console: `axclaude` uses the current folder. `axclaude C:\my\project` uses that folder. Anything after `--` goes to Claude Code: `axclaude -- --continue` picks up your last conversation.
- File Explorer: right-click a folder and choose "Open in AxClaude".

The first time Claude Code sees a folder it asks whether you trust it. Questions like this appear in the conversation as plain text. Type your answer in the message field and press Ctrl+Enter. AxClaude says "Claude needs your answer" whenever a question is waiting.

## The window

From top to bottom: the menu bar (press Alt), the conversation, the message field, the status bar. Focus starts in the message field.

- The **message field** is called "Message to Claude". Enter starts a new line. Ctrl+Enter sends. Ctrl+Enter on an empty field sends a plain Enter, which is how you confirm a dialog.
- The **conversation** is a read-only text called "Conversation". Every line Claude prints is a line here. Arrow keys, Home, End, Page Up, Page Down and your screen reader's reading commands all work. Ctrl+Tab (or F6) switches between the message field and the conversation. Escape in the conversation goes back to the message field.
- The **status bar** (NVDA+End) says what Claude is doing (starting, ready, working, waiting for your answer, stopped), how many messages are waiting, the permission mode, the session name and the project folder.

AxClaude never opens a second window. Its questions, errors and help texts are **notices** that take the place of the conversation and the message field, so nothing can hide behind the window. Your screen reader reads the title and the first line; Down Arrow reads the rest. Tab moves to the buttons, Enter chooses the main button, Escape closes the notice. Only the Windows folder, file and font pickers are separate windows.

Alt+F4 closes AxClaude and ends the Claude Code session. If Claude is still working, a notice asks first: Enter closes anyway, Escape keeps AxClaude open. What is done so far is saved, and `axclaude -- --continue` carries on later.

## Send a message

Type in the message field and press Ctrl+Enter. AxClaude says "Message sent", then "Claude is responding" when the reply starts, then "Claude is done" with a sound. If the window is in the background, its taskbar button flashes. The Options menu turns each of these off.

You can send while Claude is still working. Claude Code keeps the message and takes it up when it can. AxClaude says "Message waiting" and the status bar counts the waiting messages. The message appears in the conversation when Claude takes it up.

Up on the first line of the field, or Down on the last line, brings back messages you sent earlier.

Keys that go straight to Claude Code:

- Shift+Escape interrupts Claude, or closes one of its dialogs. Plain Escape in the message field does nothing, on purpose: it is easy to press by accident, and it would stop Claude in the middle of its work.
- Shift+Tab switches to the next permission mode (manual, accept edits, plan, auto and so on). The new mode is spoken.
- Ctrl+Up and Ctrl+Down send Up and Down, for Claude's own menus.
- Ctrl+Shift+C sends Ctrl+C. Ctrl+O turns Claude's detailed view on and off; AxClaude says which. The Session menu has Ctrl+D and Tab too.

## Slash commands

Anything that starts with a slash is a Claude Code command. Type `/help` and press Ctrl+Enter for the list. Some useful ones:

- `/clear` starts a new conversation. `/compact` shortens the current one to free up space.
- `/model` picks the model. `/permissions` changes what Claude may do without asking. `/config` has the other settings.
- `/resume` goes back to an earlier conversation. `/cost` and `/status` show usage and the session state.
- `/exit` ends the Claude Code session. The window stays open; Ctrl+Shift+R starts Claude again.

The Help menu opens the Claude Code documentation in your browser: the commands, the keys and the command line. Claude Code's autocomplete list is not shown; AxClaude sends exactly what you typed.

## Read the conversation

Every message you send is printed like this: the line `# Input 1`, your message, a blank line, the line `# Output 1 Reply from Claude`, a blank line, then Claude's reply. Both `#` lines are level 1 headings. Options, "Show the time in the Input and Output lines" adds the time.

In the conversation, these keys move to a line and speak it. Add Shift to go backwards.

- `i`: the next `# Input` line (a message you sent). `o` or `r`: the next `# Output` line (a reply from Claude). Ctrl+Shift+O from anywhere goes to the newest reply.
- `h`: the next heading. `1` to `6`: the next heading of that level. Input and Output lines are level 1; headings inside Claude's replies are level 2 and deeper.
- `c`: the next line that starts a Claude reply. `t`: the next tool line (a file Claude edited or a command it ran). `p`: the next question that needs an answer. `e`: the next error or warning. `d`: the next "done" line at the end of a reply. `s`: the next system line from AxClaude. `b`: the next blank line.
- `l`: says where you are, like "Line 12 of 340". Nothing else ever speaks line numbers.
- Page Up and Page Down move one screen at a time.
- Ctrl+F opens Find. Type the text and press Enter; F3 and Shift+F3 find the next and previous match. "Not found" means there is none.
- Enter copies the line into the message field, under a line that says it was copied from the conversation, so you can reply to it. You stay in the conversation, so you can copy several lines before you go down and write.
- `m` drops a bookmark on the line you are on: a line that says `Bookmark 1` appears just above it, and AxClaude says "Bookmark 1 added". `m` on the same line takes it away again. `k` and Shift+K go to the next and previous bookmark and read the bookmarked line with it. Bookmarks are lines in the conversation, so Ctrl+S saves them with it, and they go when the conversation goes. The Navigate menu has the same commands, and Ctrl+Shift+K bookmarks the line from anywhere in the window.

Long paragraphs are shown as one line. Claude Code's screen decorations (the mode line, spinners, tips) are not shown; they feed the status bar instead. Text that arrives while you read never moves your place and is never spoken on its own. Options, "Speak replies as they arrive" speaks each reply line when it is complete, if you prefer listening.

Ctrl+S saves the conversation as a text file. Ctrl+A and Ctrl+C in the conversation copy it.

## Menus

- **Project**: the current folder (Enter copies the path), Change folder (Ctrl+N), Recent folders, Restart Claude (Ctrl+Shift+R), Save conversation as (Ctrl+S), Exit.
- **Session**: keys sent to Claude: Send message, Interrupt, Ctrl+C, Ctrl+D, Tab, Shift+Tab, Ctrl+O, Up, Down.
- **Navigate**: the message field, the conversation, the latest reply, Find, Find next, Find previous, Bookmark this line, Next bookmark, Previous bookmark.
- **Options**: the announcements, the sound, the taskbar flash, speaking replies, the time in the Input and Output lines, checking for updates at startup, the font and text size, recording for a bug report, the settings file.
- **Help**: Keyboard shortcuts (F1), this guide, the Claude Code documentation pages, the install page, the version you have, the latest release on GitHub, Update or Check for updates, Copy diagnostics, About.

## Text size and fonts

Ctrl+Plus and Ctrl+Minus make the text bigger or smaller and say the new size. Options, Font opens the Windows font dialog. Options, "Use the Windows text size" goes back to the Windows setting (Settings, Accessibility, Text size). Colours follow Windows, so high contrast themes work.

## Settings

Everything you change is saved in `%APPDATA%\AxClaude\settings.json`. Options, "Open settings file" opens it. Two settings have no menu item: `claudePath` names the Claude Code program when it is not on PATH, and `joinWrappedLines` set to false keeps long paragraphs on separate lines.

## Updates

New versions are published on GitHub. When AxClaude starts, it asks GitHub once whether there is a newer one; Options, "Check for updates when AxClaude starts" turns that off. If there is one, the update notice opens by itself: Enter updates, Escape keeps the version you have, and a "System:" line in the conversation records it.

The Help menu always shows the version you have ("Installed: AxClaude 1.0.0"), the latest release on GitHub ("Latest release: AxClaude 1.0.1 (newer than this one)", which opens the release page) and the update action. Help, "Update to AxClaude 1.0.1" shows what is new, with three buttons. Update now downloads the new version, closes AxClaude, installs it and starts it again on the same folder; if you had a conversation open, it is picked up again. Open release page opens it in the browser. Later keeps the version you have, and the Help menu offers the update until you take it. Help, "Check for updates" asks GitHub at any time and tells you either way.

## If something goes wrong

- "Claude Code was not found": install it with the command in the notice, or use "Locate claude.exe" if it is somewhere unusual. Then press Ctrl+Shift+R.
- An error from AxClaude is a notice inside the window and a "System:" line in the conversation. Escape closes the notice.
- Claude stopped, or the conversation looks wrong: Ctrl+Shift+R restarts Claude in the same folder. Start AxClaude with `-- --continue` to get the conversation back. While Claude is stopped, Ctrl+Enter sends nothing and keeps your text.
- For a bug report: Help, Copy diagnostics puts the version, the paths and the last log lines on the clipboard. Options, "Record raw stream for a bug report" records everything Claude Code prints. The log is in `%LOCALAPPDATA%\AxClaude\logs`.
- An update did not start AxClaude again: `%LOCALAPPDATA%\AxClaude\logs\update.log` says what the installer did. Start AxClaude from the Start menu; if it is still the old version, download the zip from the release page and run `install.ps1` yourself.

## About AxClaude

AxClaude is made by Dr. Kyle Keane, www.kylekeane.com. It is free under the MIT licence: use it, change it and pass it on, as long as the note that says who made it stays with it (the `LICENSE` file next to the program).
