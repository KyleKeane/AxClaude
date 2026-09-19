---
name: accessibility-review
description: Review a change to the AxClaude UI (focus, key handling, announcements, transcript view) for NVDA regressions against SPEC.md before it is committed. Use after editing anything under src/AxClaude or when asked to check accessibility.
---

# Accessibility review

Read the diff of the change (git diff, or the files named by the user) and check every point below. Report each finding with file and line, and say plainly when a point cannot be verified without running NVDA. Do not rewrite the code unless asked.

1. **Names and roles.** Every new control sets `AccessibleName`. Menu items have a mnemonic (`&`) and either `ShortcutKeys` or `ShortcutKeyDisplayString`. No control relies on a visual label alone.
2. **Caret discipline.** Nothing moves the transcript caret or selection except a user navigation command (quick keys, find, Ctrl+Home/End). Batches that update the transcript save and restore the caret and shift it by the delta of edits that precede it. `ScrollToCaret` is only called on user navigation.
3. **Announcements.** Every jump, "no target", bell, lifecycle event and folder change announces through `Announcer` (UIA notification with selection fallback). No `MessageBox` for routine information; message boxes are only for errors that need a decision.
4. **Key handling.** Quick keys act only when the transcript view has focus and only without Ctrl/Alt. Ctrl+C with a selection stays copy. Ctrl+Tab, Ctrl+Shift+Tab and F6 work from every control. Escape in the input sends ESC; Escape in the transcript returns focus. Handled keys set `SuppressKeyPress` so no ding is played. NVDA modifier combinations are never intercepted.
5. **Threading.** No pseudo-console read or write on the UI thread; UI updates come through `BeginInvoke` or the batch timer.
6. **Text view invariants.** Line separator is `\r\n`; `lineStart[]` offsets updated for every edit; hidden lines removed from the view but kept in the model; `MaxLength = 0`.
7. **Spec alignment.** Behaviour matches SPEC.md sections 5 and 6; if it intentionally differs, the spec must change in the same commit.
8. **Test plan.** State which sections of `docs/nvda-test-plan.md` the change affects and must be rerun.
