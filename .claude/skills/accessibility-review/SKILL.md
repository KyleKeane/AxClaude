---
name: accessibility-review
description: Review a change to the AxClaude UI (focus, key handling, announcements, transcript view) for NVDA regressions against SPEC.md before it is committed. Use after editing anything under src/AxClaude or when asked to check accessibility.
---

# Accessibility review

Read the diff of the change (git diff, or the files named by the user) and check every point below. Report each finding with file and line, and say plainly when a point cannot be verified without running NVDA. Do not rewrite the code unless asked.

1. **Names and roles.** Every new control sets `AccessibleName`. Menu items have a mnemonic (`&`) and either `ShortcutKeys` or `ShortcutKeyDisplayString`. The status labels have no `AccessibleName` and no `Spring` (SPEC.md §6.2). No control relies on a visual label alone.
2. **Caret discipline.** Nothing moves the conversation caret or selection except a user navigation command (the quick keys, find, Backspace, the page keys, Ctrl+Home / Ctrl+End). Output goes through `TranscriptMirror.Update` and the caret is restored with `MapPosition`; while the view has the focus, output waits 250 ms after a key press and for as long as text is selected (`TranscriptView.Sync`). `EM_SCROLLCARET` is sent only on user navigation or while the view has no focus.
3. **Announcements.** Every jump, "no target", lifecycle event and folder change speaks through `MainForm.Announce`: a UI Automation notification on the conversation, or on the notice while one shows, with the selection fallback in `TranscriptView.MoveTo`. Anything spoken per line is joined into one notification per frame (`Arrivals`, D32). Nothing speaks on a plain arrow key; the go-to keys are the on-demand exception (D26).
4. **No second window.** Questions, errors and help texts go through `MainForm.ShowNotice` (`OverlayPanel`), never a `Form` or a `MessageBox` (D23). A new notice names its default and cancel buttons so that Enter and Escape reach them.
5. **Key handling.** Quick keys act only when the conversation has the focus and only without Ctrl or Alt. Ctrl+C with a selection stays copy. Ctrl+Tab, Ctrl+Shift+Tab, F6, Ctrl+1 and Ctrl+2 work from every control. Plain Escape in the message field is a guard that only speaks (D20); Shift+Escape is the interrupt. Escape in the conversation clears a selection, else returns the focus to the field. Handled keys set `SuppressKeyPress` so no ding is played. Ctrl+O is never sent (D14). NVDA's own key combinations are never intercepted, and no global keyboard hook is installed.
6. **Threading.** No pseudo console read or write on the UI thread: output arrives through the inbox that `MainForm.StartClaude` posts with `BeginInvoke`, and a frame ends on `ESC[?25h` or the quiet timer.
7. **Text view invariants.** `\r\n` between lines and a space before a joined row (`TranscriptMirror.SeparatorBefore`); hidden lines leave the view but stay in the model; `MaxLength = 0`; edits go through `Select` and `SelectedText`, never `Text = …`; a frame that changes nothing visible allocates nothing (D24).
8. **Spec alignment.** Behaviour matches SPEC.md sections 5 and 6, the announcement texts of §6.4 word for word. If it intentionally differs, the spec, the user guide and the F1 text in `HelpText` change in the same commit.
9. **Test plan.** State which sections of `docs/nvda-test-plan.md` the change affects and must be rerun.
