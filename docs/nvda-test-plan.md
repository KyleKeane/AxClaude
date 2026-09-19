# NVDA manual test plan

Start NVDA before the app. Use the default NVDA settings unless a step says otherwise. Note the app build, the Claude Code version (`claude --version`) and the NVDA version at the top of each run.

Each step says what to do and what NVDA must say or do. A step fails if NVDA says nothing, says the wrong thing, or the caret ends up somewhere else.

## 1. Startup and focus

1. Start the app on a folder that has never been used with Claude. NVDA reads the window title (`<folder> - AxClaude`), then "Message to Claude edit multi line" and the description "Ctrl+Enter sends, Enter starts a new line".
2. Ctrl+Tab: "Conversation read only edit multi line", then the current line.
3. Ctrl+Tab again: the message field.
4. F6 twice: as steps 2 and 3.
5. Tab from the message field: the conversation. Tab again: the message field. Shift+Tab in the conversation: the message field.
6. Alt: the menu bar opens; Left and Right move across Project, Session, Navigate, Options, Help; each item reads its name, mnemonic and shortcut. Escape closes it.
7. NVDA+End: the status bar, "Claude: ready | manual mode on" (or the current state) followed by the project path. Never "No status bar found", whether the window is maximized, sized to the work area or small. Later, once Claude has named the session (the name follows the mode), make the window narrow and press NVDA+End: the state, the mode, the session name and the full path are all read, even though the path is cut short on screen.

## 2. Dialogs and plain conversation

1. With focus in the conversation, read the trust prompt with Down Arrow: "Permission Required: Accessing workspace", the folder path, the `y.` and `n.` options and "Enter y/n:".
2. Ctrl+Tab, type `y`, Ctrl+Enter: "Answer sent". Within a few seconds: "Claude is done" or "Claude needs your answer" (a second startup dialog may follow; answer it).
3. Type `What is 2+2?`, Enter, `Answer in one line.` NVDA reads the new line as you type. Ctrl+Enter: "Message sent"; the field empties and keeps the focus. Then "Claude is responding", then "Claude is done" with the sound.
4. Ctrl+Tab, Shift+O: "# Output 1 Reply from Claude". Down Arrow: a blank line, then Claude's reply. Up Arrow from the marker: a blank line, "Answer in one line.", "What is 2+2?", "# Input 1". No line starts with "you:".
5. `i`: "No next input". Shift+I: "# Input 1".
6. Ctrl+Tab. Up: the message you sent is back in the field and is read. Down: "Message field is empty". Up, then Ctrl+Enter sends it again.

## 3. Reading while output arrives

1. Ask for a long answer ("Write 40 numbered lines"). Ctrl+Tab at once and read the header lines with Up and Down while Claude streams.
2. The caret never moves on its own; NVDA never starts speaking incoming lines; Down Arrow continues from where you were.
3. After "Claude is done", Ctrl+Shift+O: "# Output 2 Reply from Claude", focus in the conversation. Down Arrow: the blank line, then the first reply line.
4. Ask for another long answer and Ctrl+Tab at once. Press Down until you reach the line Claude is still writing, then keep pressing Down every second while it grows. Each press reads text you have not heard yet, never a part you just heard; when nothing is new, NVDA re-reads the current line only because it is the last one. `l` twice a few seconds apart: the total grows, the current number stays.

## 4. Headings and quick keys

1. Ask for a markdown answer with two `##` headings and a bulleted list.
2. Ctrl+Home, then `h` repeatedly: the "# Input" and "# Output" line of each exchange, each followed by "heading level 1", and Claude's headings followed by "heading level 2", in order. Shift+H goes back. `1`: only the markers.
3. `2`: only Claude's level 2 headings. `3`: "No next heading level 3".
4. `b`: "blank"; Down Arrow reads the paragraph after it. Shift+B goes back.
5. `o` / Shift+O, `r` / Shift+R (the same lines as `o`), `i` / Shift+I, `c`, `t`, `p`, `e`, `d`, `s`: each announcement matches SPEC.md §6.4.
6. Escape in the conversation: the message field has the focus and nothing was sent (the status still says "ready").
7. Ask for a reply longer than the window ("Write 60 numbered lines"). After "Claude is done", Ctrl+Tab: the newest line. Page Up: a line about one screen higher, once per press, up to the first line, which then repeats. Page Down: one screen at a time down to the last line, which then repeats. The app says nothing itself; NVDA reads each line once.
8. `l`: "Line 37 of 120" or similar. Ctrl+Home, `l`: "Line 1 of 120". Ctrl+End, `l`: the last number equals the total. Arrow keys read only the text, never a number.
9. Ask for one paragraph of about 400 words with no line breaks. Read it: it is one line (Down Arrow from its start goes to the next paragraph or the done line, and `l` before and after differ by one), not fragments that end mid-sentence.
10. Ctrl+F: the Find notice takes the place of the conversation, in the same window: "Text to find edit" and no new window title. Type a word from the paragraph, Enter: the notice closes, focus is in the conversation, NVDA says the line with the word. F3: the next line with the word, or the same line with "From the top" in front when it is the only one. Shift+F3 goes backwards ("From the end" after a wrap). Ctrl+F: the field holds the previous word, selected; type `zzqqx`, Enter: "Not found: zzqqx" and the caret stays. Ctrl+F, delete the text, Enter: "Type the text to find" and the field keeps the focus; Tab: "Find next button", Tab: "Cancel button", Tab: the field again, nothing else. Alt: the menu does not open. Escape: the notice closes and the focus returns to where it was. F3 in the message field works too and moves the focus to the conversation.
11. On a reply line press Enter: "Line 12 copied to the message" (the number `l` reports); focus stays in the conversation. Ctrl+Tab: the field ends with `_ start of copied line from conversation history _`, then `Line 12 of 120:` and the text, then an empty line with the caret. Type a comment, Ctrl+Enter: Claude answers about the quoted line. Back in the conversation, Enter on the long paragraph from step 9: the whole paragraph is copied as one line. Enter on two more lines with text already in the field: each block is separated from the text above by one blank line. Send it: the message appears in full between "# Input n" and "# Output n Reply from Claude", with its blank lines and marker lines, and no "you:" row anywhere.
12. On a reply line press `m`: "Bookmark 1 added"; the caret stays (Up Arrow reads "Bookmark 1", Down Arrow the line again). On a reply line further down, `m`: "Bookmark 2 added". Ctrl+Home, `k`: "Bookmark 1, " followed by the first bookmarked line; `k`: "Bookmark 2, " and the second; `k`: "No next bookmark". Shift+K: "Bookmark 1, …" again; Down Arrow reads the bookmarked line; `m` there: "Bookmark 1 removed", and Up Arrow now reads the line that was above the bookmark. `k`, then `m` on the "Bookmark 2" line itself: "Bookmark 2 removed". On the long paragraph from step 9, `m`, then Up Arrow: "Bookmark 3" sits above the whole paragraph, and Down Arrow reads it as one line still. Ctrl+Tab to the message field, Ctrl+Shift+K: the focus moves to the conversation and "Bookmark 4 added" is said for the line the caret was on. Navigate menu: Bookmark this line, Next bookmark and Previous bookmark do the same as `m`, `k` and Shift+K. Ctrl+S: the saved file holds the "Bookmark" lines in place.

## 5. Permission mode, prompts and interruption

1. In the message field, Shift+Tab: focus stays, NVDA says the new mode ("manual mode on" or similar), NVDA+End shows the same. Shift+Tab until the mode you started with comes back.
2. Ask Claude to create a file. If a permission prompt appears: "Claude needs your answer". Ctrl+Tab, Shift+P: the "Permission Required:" line; Down Arrow: the numbered options and "Enter selection".
3. Type `1`, Ctrl+Enter: "Answer sent", then "Claude is done" when the tool finishes.
4. Ctrl+O once: "Claude's detailed view is on. Press Ctrl+O to turn it off." NVDA+End: "detailed view on, Ctrl+O turns it off". Ctrl+O again: "Claude's detailed view is off." and the status is "ready" again. The conversation does not change on either press, no folder picker opens and nothing else reacts.
5. Ask for something slow, then Escape in the message field while the status says "working": "Escape does nothing here. Press Shift+Escape to interrupt Claude" and the status still says "working". Shift+Escape: the conversation gains Claude's interruption line and the status is "ready".
6. Session menu, Send Ctrl+C: the same as Shift+Escape.

## 6. Sending while Claude works

1. Ask Claude to run `sleep 20` with the Bash tool and then reply OK DONE. While the status says "working", type a second message and Ctrl+Enter: "Message waiting". NVDA+End: "1 message waiting". Ctrl+Tab, Shift+I: only "# Input 1" exists; the second message is not in the conversation yet. Ctrl+Tab back, Up: the waiting message is in the field; Down brings the empty field back.
2. Wait for "Claude is done". In the conversation, Shift+I: "# Input 2". Down Arrow: the second message, a blank line, "# Output 2 Reply from Claude", a blank line, then the part of the reply that answers it. Shift+I again: "# Input 1". No line starts with "you:" or "ctrl+x ctrl+s".

## 7. Project folder

1. Project menu, "Current folder: …" reads the full path; Enter on it: "Copied", and the clipboard holds the path.
2. Ctrl+N opens the standard folder picker; NVDA reads its title and controls; type a path and choose Select Folder.
3. "Project folder changed to <name>"; the conversation gains a "System:" line; the title and the status bar change; Claude restarts, and the trust prompt for the new folder appears if needed.
4. Project menu, Recent folders lists the previous folder; choosing it switches back.

## 8. Lifecycle

1. Type `/exit`, Ctrl+Enter: "Claude stopped"; the status says "stopped"; the message field still works. Type a word, Ctrl+Enter: "Claude is not running. Press Ctrl+Shift+R to start it again" and the word is still in the field.
2. Ctrl+Shift+R restarts Claude; "Claude is ready" again.
3. Alt+F4 while the status says "ready": the app closes within two seconds; Task Manager shows no `claude.exe`.
4. Start the app, ask Claude to run `sleep 20` with the Bash tool, and press Alt+F4 while the status says "working". The window stays. NVDA reads "Close AxClaude? read only edit multi line" and the first line "Claude is still working. If you close now, Claude stops in the middle of its work." Down Arrow: the line about `--continue`, then "Enter closes anyway. Escape keeps AxClaude open." Tab: "Close anyway button", Tab: "Keep working button". Escape: the notice goes, the focus is back in the message field, NVDA+End still says "working". Send a second message, then Alt+F4 again: the first line now ends "and a message you sent is still waiting for it". Enter: the app closes within two seconds and no `claude.exe` is left. Start it with `-- --continue`: the conversation is back.
5. Start the app from a shortcut with no folder argument: it opens the last folder without asking, at the same window size.

## 9. Options and diagnostics

1. Ctrl+Plus: "Text size 13" (one more than before); the conversation and the field are larger. Ctrl+Minus goes back. Options, Font opens the Windows font dialog; choose a family and size: "Font <name> <size> point". Options, "Use the Windows text size": "Windows text size". Close and reopen the app: the chosen font is still in use.
2. Options, Speak replies as they arrive: after the next question, NVDA speaks each reply line as it becomes final, without moving the caret. Turn it off again.
3. Options, Record raw stream for a bug report: choose a file; "Recording started" and a "System:" line with the path. Send a message, then Options, Stop recording: "Recording stopped" and a system line with the size. The `.vt` file and its `.chunks.txt` exist.
4. Help, Copy diagnostics: "Diagnostics copied"; paste into Notepad: version, paths and log lines.
5. Help, Keyboard shortcuts (F1): the list takes the place of the conversation, in the same window. NVDA reads "Keyboard shortcuts read only edit multi line" and the first line "Anywhere"; Down Arrow reads the list; Tab reaches the Close button and Tab again returns to the text; NVDA+End still reads the status bar. Escape (or Enter) closes it and the focus returns where it was. F1 from the conversation, then close: the focus is back in the conversation on the same line.
6. Help, User guide: the same kind of notice, starting with "AxClaude user guide"; Escape closes it. Help, About: a notice "About AxClaude" with the version and the paths; Escape closes it.
7. Help, "Claude Code slash commands (web)": the browser opens the commands page at code.claude.com. The other web items open their pages too.
8. Ctrl+S: the Save dialog with a name like `<folder>-20260919-1405.txt`; save it: "Conversation saved". The file holds the conversation as shown, with the "# Input" and "# Output" lines and your messages between them, and no "you:" echo or chrome rows.
9. Options, "Show the time in the Input and Output lines", then send a message: the markers read "# Input 4 (14:05)" and "# Output 4 Reply from Claude (14:05)" (the current time). Earlier markers are unchanged. Turn it off again.

## 10. Claude Code missing, and the installer

1. Start the app with `--claude C:\nowhere\claude.exe`. The notice "Claude Code was not found" takes the place of the conversation with the focus in its text: NVDA reads "Claude Code was not found read only edit multi line" and the first line; Down Arrow reads the path tried, the install command and the way back. Tab reaches "Copy install command", "Open install instructions", "Locate claude.exe..." and "Close". Copy install command: NVDA says it was copied and the notice stays; paste it into Notepad to check. Close: the notice goes, the message field has the focus, the status says "stopped" and the conversation has a "System:" line saying Claude Code was not found. Ctrl+Shift+R shows the notice again; Locate claude.exe opens the standard file picker, and Cancel returns to the notice. Locate again with the real `claude.exe` (usually `%USERPROFILE%\.local\bin\claude.exe`): the notice closes, Claude starts, and the settings file names it under `claudePath` (remove that line afterwards).
2. `.\publish.ps1` from the repository (close the installed AxClaude first, if any). It prints the zip path and the install summary. Start menu: type AxClaude, Enter: the app opens the last folder. File Explorer: a folder's context menu has "Open in AxClaude", and it opens the app on that folder. A new console: `axclaude` starts the app on the current folder.
3. `.\install.ps1 -Uninstall` from `%LOCALAPPDATA%\Programs\AxClaude` (or the repository): the Start menu entry, the Explorer entry and the `axclaude` command are gone; the settings file remains.
