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
- Console: `axclaude` uses the current folder. `axclaude C:\my\project` uses that folder. AxClaude continues your last conversation in the folder, or starts a new one when there is none; `axclaude --` alone always starts a new one, and anything else after `--` goes to Claude Code, for example `axclaude -- --resume`.
- File Explorer: right-click a folder and choose "Open in AxClaude".

Ctrl+N starts a new session from inside AxClaude. The notice shows the folder, with a Choose folder button under it, then a list of ways to start Claude: a new conversation, continue the last conversation, choose a conversation to resume, plan mode, edits accepted without asking, and Custom, where you type Claude Code's own command line options, one or more on each line. Enter stops the current session, clears the conversation and starts the new one in the empty window; with "Continue the last conversation" the earlier exchanges come back under their "from previous session" headings, and messages count from 1 again. Escape keeps the current session. Restart Claude (Ctrl+Shift+R) keeps the options you chose and the conversation.

The first time Claude Code sees a folder it asks whether you trust it. Questions like this, and the lists that commands such as `/model` open, come up as an **answer notice**: a quick high trill, then "Claude asks:" with the question's title, and the focus on the current answer. Up and Down Arrow or the answer's number (or y and n) choose, Enter answers, Escape cancels the question. Alt+M, "Answer in the message field", leaves the question open and takes you to the field. When a question offers "Other", the notice has a field "Your own answer" under the answers (Alt+Y): type your answer there and press Enter, and AxClaude sends it as Claude asks for it. If Claude asks for your own words some other way, AxClaude takes you to the message field and says so: type them there and press Ctrl+Enter. Claude's full question, with all its hints, stays in the conversation. When Claude asks several questions at once, they come one after the other, then a review where y submits your answers. A question that allows several answers has check boxes instead: Space or an answer's number ticks it, and Enter sends all the ticked answers. For a second after the notice opens, Enter and the answer keys do nothing, so keys you were typing for a message cannot answer by accident.

Screens that Claude shows until you press a key, such as `/status`, `/tasks` and `/help`, come up as a **screen notice**: the screen's text, and a button for every key the screen offers, for example "View" (Enter), "Close" (Escape) or "Select (Down)". A button sends its key to Claude; if the screen is still open afterwards, the notice comes back with what it shows now.

While Claude waits for an answer, Ctrl+Enter only sends an answer. A message you typed stays in the field, and AxClaude says so; press Ctrl+Enter again to send it all the same. Anything else Claude asks is announced with "Claude needs your answer". Options, "Answer Claude's questions in a notice", turns the notices and this protection off: questions are then only announced, and you answer them in the message field.

## The window

From top to bottom: the menu bar (press Alt), the conversation, the message field, the status bar. Focus starts in the message field.

- The **message field** is called "Message to Claude". Enter starts a new line. Ctrl+Enter sends. Ctrl+Enter on an empty field sends a plain Enter, which is how you confirm a dialog. Page Up and Page Down move one screen of the field at a time and reach the first and last line of your message; Home and End move within the line, Ctrl+Home and Ctrl+End to the start and end of the message.
- The **conversation** is a read-only text called "Conversation". Every line Claude prints is a line here. Arrow keys, Home, End, Page Up, Page Down and your screen reader's reading commands all work. Ctrl+1 goes to the message field and Ctrl+2 to the conversation; when you are there already, the key says so, which helps when you have lost track of where you are. Ctrl+Tab, Ctrl+Shift+Tab or F6 switches between the two. Escape in the conversation goes back to the message field.
- While Claude is still writing a line and you are on it, Down Arrow reads only what has arrived since you last heard it, so you can follow a long reply as it comes without hearing anything twice. Once you move on, the line reads as one line again.
- To copy from the conversation: Shift with the arrow keys selects text, Ctrl+C copies it and says "Copied". While text is selected, new output waits until you press a plain arrow key. Escape clears the selection; a second Escape goes to the message field.
- The **status bar** (NVDA+End) says what Claude is doing (starting, ready, working, waiting for your answer, stopped), how many messages are waiting, the permission mode, the session name and the project folder.

AxClaude never opens a second window. Its questions, errors and help texts are **notices** that take the place of the conversation and the message field, so nothing can hide behind the window. Your screen reader reads the title and the first line; Down Arrow reads the rest. Tab moves to the buttons, Enter chooses the main button, Escape closes the notice. Only the Windows folder, file and font pickers are separate windows.

Alt+F4 closes AxClaude and ends the Claude Code session. If Claude is still working, a notice asks first: Enter closes anyway, Escape keeps AxClaude open. What is done so far is saved, and the next start carries on from it.

## Send a message

When Claude has started, AxClaude says "Claude is ready" with a high ping. Type in the message field and press Ctrl+Enter. AxClaude says "Message sent" with a short low note, and "Claude is responding" with a rising chime when the reply starts. A soft tick, like a typewriter key, marks every new line that arrives from Claude, and two lower knocks mark each tool Claude starts, so you can hear Claude working without reading along. "Claude is done" comes with three falling notes once the whole turn has ended, and a question from Claude with two equal notes and "Claude needs your answer". If the window is in the background, its taskbar button flashes. The Options menu turns each of these off.

You can send while Claude is still working. Claude Code keeps the message and takes it up when it can. AxClaude says "Message waiting" and the status bar counts the waiting messages. The message appears in the conversation when Claude takes it up, usually after its current step. Ctrl+Shift+S (Session, Send the waiting message now) makes Claude take it up at once.

Up and Down in the field only move through what you are typing. Messages you sent earlier are in the conversation: `i` and Shift+I jump between their `# Input` lines, and Enter on a line copies it into the message field.

Keys that go straight to Claude Code:

- Shift+Escape interrupts Claude, or closes one of its dialogs. Plain Escape in the message field does nothing, on purpose: it is easy to press by accident, and it would stop Claude in the middle of its work.
- Shift+Tab switches to the next permission mode (manual, accept edits, plan, auto and so on). The new mode is spoken.
- Ctrl+Up and Ctrl+Down send Up and Down, for Claude's own menus.
- Ctrl+Shift+C sends Ctrl+C, and Ctrl+Shift+M sends Shift+Tab from anywhere in the window. Ctrl+O, Claude's detailed view, is switched off in AxClaude: in screen reader mode it redraws the whole conversation, which doubled it and spoke old replies again. Pressing it says so. To see what the tools print, start the session with the `--verbose` argument (Ctrl+N, Custom, for example `--continue --verbose`): every tool's output then appears in full under its "tool:" line. The Session menu has Ctrl+D and Tab too.

## Slash commands

Anything that starts with a slash is a Claude Code command. Type `/help` and press Ctrl+Enter for the list. Some useful ones:

- `/clear` starts a new conversation. `/compact` shortens the current one to free up space.
- `/model` picks the model. `/permissions` changes what Claude may do without asking. `/config` has the other settings.
- `/resume` goes back to an earlier conversation. `/cost` and `/status` show usage and the session state.
- `/exit` ends the Claude Code session. The window stays open; Ctrl+Shift+R starts Claude again.

The Help menu opens the Claude Code documentation in your browser: the commands, the keys and the command line. Claude Code's autocomplete list is not shown; AxClaude sends exactly what you typed.

## Read the conversation

Every message you send is printed like this: the line `# Input 1`, your message, a blank line, the line `# Output 1 Reply from Claude`, a blank line, then Claude's reply. Both `#` lines are level 1 headings. When AxClaude starts on a folder with a conversation, the earlier exchanges come back under `# Input 1 from previous session` and `# Output 1 from previous session Reply from Claude`, with the message as a `you:` line; new messages count from 1 as usual. Restart Claude (Ctrl+Shift+R) keeps the conversation as it is. New session (Ctrl+N), Change folder and Recent folders clear it first, so another folder starts with an empty window and its own earlier conversation, when it has one. Options, "Show the time in the Input and Output lines" adds the time.

In the conversation, these keys move to a line and speak it. Add Shift to go backwards.

- `i`: the next `# Input` line (a message you sent). `o` or `r`: the next `# Output` line (a reply from Claude). Ctrl+Shift+O from anywhere goes to the newest reply.
- `h`: the next heading. `1` to `6`: the next heading of that level. Input and Output lines are level 1; headings inside Claude's replies are level 2 and deeper.
- `c`: the next line that starts a Claude reply. `t`: the next tool line (a file Claude edited or a command it ran). `p`: the next paragraph, the first line after a blank line. `e`: the next error or warning. `d`: the next "done" line at the end of a reply. `s`: the next system line from AxClaude. `q`: the next question Claude asked: AxClaude writes a line `Question: …` in front of each one, which stays when Claude replaces the question with your answer.
- `l`: says where you are, like "Line 12 of 340". Nothing else ever speaks line numbers.
- Page Up and Page Down move one screen at a time, and reach the first and last line.
- Ctrl+F opens Find. Type the text and press Enter; F3 and Shift+F3 find the next and previous match. "Not found" means there is none.
- Enter copies the line into the message field, under a line that says it was copied from the conversation and a line with its number, followed by a line that says your comment starts, and puts you in the field under it, ready to write. Ctrl+2 takes you back to the same conversation line, so you can collect several lines into one message.
- Backspace goes back to the line you were on before the last jump, and again for the jump before that, so a stray `p` or `h` is undone in one press.
- `m` drops a bookmark on the line you are on: a line that says `Bookmark 1` appears just above it, and AxClaude says "Bookmark 1 added". `m` on the same line takes it away again. `k` and Shift+K go to the next and previous bookmark and read the bookmarked line with it. Bookmarks are lines in the conversation, so Ctrl+S saves them with it, and they go when the conversation goes. The Navigate menu has the same commands, and Ctrl+Shift+K bookmarks the line from anywhere in the window.

Long paragraphs are shown as one line. Claude Code's screen decorations (the mode line, spinners, tips) are not shown; they feed the status bar instead. Text that arrives while you read never moves your place and is never spoken on its own, unless you ask for it: Options, "Speak replies as they arrive" has three choices. "None" is the default. "First lines only" speaks the first line of each of Claude's messages, where Claude says what it is doing or what it found, so you follow the major moments of a long turn without its detail. "All lines" speaks every line of Claude's replies. Each message is spoken in one go once it is complete; tool output, thinking and the conversation replayed at startup are never spoken. Options, "Speak each tool call" adds the line that names each tool Claude runs, such as "tool: Read (notes.txt)", as soon as it appears, whichever of the three you chose; what the tool printed is still not spoken. Press Control to silence NVDA when you have heard enough: the text stays in the conversation, and the ticks go on either way.

Ctrl+S saves the conversation as a text file. Ctrl+A and Ctrl+C in the conversation copy it.

## Menus

- **Project**: Open current folder (Ctrl+W), which names the folder and opens it in File Explorer, Change folder, New session (Ctrl+N), Recent folders, Force Claude to restart (Ctrl+Shift+R), Save conversation as (Ctrl+S), Exit.
- **Session**: Send message (Ctrl+Enter), Send the waiting message now (Ctrl+Shift+S), and the keys sent to Claude: Interrupt Claude (Shift+Escape), Send Ctrl+C (Ctrl+Shift+C), Send Ctrl+D, Send Tab, Send Shift+Tab (Ctrl+Shift+M), Send Up, Send Down.
- **Navigate**: Go to message field (Ctrl+1), Go to conversation (Ctrl+2), Latest response (Ctrl+Shift+O), Find, Find next, Find previous, Bookmark this line, Next bookmark, Previous bookmark.
- **Options**: Announce when Claude is done, the chimes, the tick for each new line and tool call, the taskbar flash, Speak replies as they arrive, Speak each tool call, the time in the Input and Output lines, checking for updates at startup, the font and text size, recording for a bug report, the settings file.
- **Help**: Keyboard shortcuts (F1), this guide, the Claude Code documentation pages, the install page, the version you have, the latest release on GitHub, Update or Check for updates, Copy diagnostics, About.

## Text size and fonts

Ctrl+Plus and Ctrl+Minus make the text bigger or smaller and say the new size. Options, Font opens the Windows font dialog. Options, "Use the Windows text size" goes back to the Windows setting (Settings, Accessibility, Text size). Colours follow Windows, so high contrast themes work.

## Settings

Everything you change is saved in `%APPDATA%\AxClaude\settings.json`. Options, "Open settings file" opens it. Some settings have no menu item: `claudePath` names the Claude Code program when it is not on PATH, `joinWrappedLines` set to false keeps long paragraphs on separate lines, `ptyColumns` and `ptyRows` size the hidden console Claude writes to (240 by 50), and `maxTranscriptLines` caps the conversation (20000 lines).

## Updates

New versions are published on GitHub. When AxClaude starts, it asks GitHub once whether there is a newer one; Options, "Check for updates when AxClaude starts" turns that off. If there is one, the update notice opens by itself: Enter updates, Escape keeps the version you have, and a "System:" line in the conversation records it.

The Help menu always shows the version you have ("Installed: AxClaude 1.0.0"), the latest release on GitHub ("Latest release: AxClaude 1.0.1 (newer than this one)", which opens the release page) and the update action. Help, "Update to AxClaude 1.0.1" shows what is new, with three buttons. Update now downloads the new version, closes AxClaude, installs it and starts it again on the same folder; if you had a conversation open, it is picked up again. Open release page opens it in the browser. Later keeps the version you have, and the Help menu offers the update until you take it. Help, "Check for updates" asks GitHub at any time and tells you either way.

## If something goes wrong

- "Claude Code was not found": install it with the command in the notice, then press Ctrl+Shift+R. If it is installed somewhere unusual, use "Locate claude.exe" instead: Claude starts at once and the place is remembered.
- An error from AxClaude is a notice inside the window and a "System:" line in the conversation. Escape closes the notice.
- Claude stopped, or the conversation looks wrong: Ctrl+Shift+R restarts Claude in the same folder. While Claude is running it asks first, because a restart quits Claude Code at once and ends anything it runs in the background: Enter restarts, Escape keeps Claude running. Starting AxClaude again brings the conversation back. While Claude is stopped, Ctrl+Enter sends nothing and keeps your text.
- For a bug report: Help, Copy diagnostics puts the version, the paths and the last log lines on the clipboard. Options, "Record raw stream for a bug report" records everything Claude Code prints. The log is in `%LOCALAPPDATA%\AxClaude\logs`.
- An update did not start AxClaude again: `%LOCALAPPDATA%\AxClaude\logs\update.log` says what the installer did. Start AxClaude from the Start menu; if it is still the old version, download the zip from the release page and run `install.ps1` yourself.

## About AxClaude

AxClaude is made by Dr. Kyle Keane, www.kylekeane.com. It is free under the MIT licence: use it, change it and pass it on, as long as the note that says who made it stays with it (the `LICENSE` file next to the program).

## Disclaimer

AxClaude is free software offered under the MIT licence. It is provided "as is" and "as available", without warranty of any kind, express or implied, including but not limited to the implied warranties of merchantability, fitness for a particular purpose, title and non-infringement. The developer has no obligation to provide support, maintenance, updates or corrections. To the fullest extent permitted by law, the developer is not liable for any claim, damages or other liability, whether in contract, tort or otherwise, arising from or in connection with the software or its use, including loss of data or work and anything done, said or charged by Claude Code or the services behind it. You use AxClaude at your own risk.

Everything you type goes to Claude Code and, through it, to Anthropic under Anthropic's own terms; the developer has no access to it. Claude Code can create, change and delete files and run commands in your project folder: what you allow it to do is your responsibility. AxClaude is an independent project, not affiliated with or endorsed by Anthropic. Claude and Claude Code are products of Anthropic.
