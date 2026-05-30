---
name: finish-phase
description: Complete an NWO roadmap phase or commit/push work. Use when a ROADMAP phase/sub-phase is done, or when the user says "commit", "push", or "ship it". Ticks the roadmap, syncs docs, runs checks, and commits+pushes to origin/main in the project format.
allowed-tools: Read, Edit, PowerShell
---

# finish-phase

The completion ritual from [CLAUDE.md](../../../CLAUDE.md): keep the roadmap and docs
in sync, verify, then commit and push. NWO commits directly to `main` (repo
convention) under the `Klaboukes` / `barthoukes@gmail.com` identity.

## Procedure

1. **Sanity look:** `& .claude/skills/finish-phase/status.ps1` to see the working
   tree and recent commits.
2. **Tick the roadmap:** mark the completed `[ ]` → `[x]` in
   [docs/ROADMAP.md](../../../docs/ROADMAP.md).
3. **Sync the docs (source of truth):** if behaviour changed, update the relevant
   sections of [docs/MECHANICS.md](../../../docs/MECHANICS.md),
   [docs/ARCHITECTURE.md](../../../docs/ARCHITECTURE.md), or
   [docs/TECH_STACK.md](../../../docs/TECH_STACK.md). The dedicated skills
   (`add-content`, `tune-mechanics`, `hud-ui`) should already have done their slice;
   this is the final consistency pass.
4. **Verify:** run the **`run-checks`** skill and confirm it's green. (Flag any
   button/visual flow that still needs a human F5 run — don't claim it's verified.)
5. **Commit + push:** compose the message yourself (concise subject, blank line, short
   body, trailing `Co-Authored-By: Klaboukes <barthoukes@gmail.com>`), then:

   ```powershell
   & .claude/skills/finish-phase/commit-push.ps1 -Message @'
   feat: Phase X.Y — short subject

   Optional body explaining what shipped.

   Co-Authored-By: Klaboukes <barthoukes@gmail.com>
   '@
   ```

   The script stages all, commits, and pushes to `origin/main`. It never invents the
   message — you pass it. Only auto-commit when a phase is marked complete or the user
   explicitly asked to commit/push.

## Maintenance

If the commit format, identity, or branch convention changes, update this skill and
keep it aligned with the **Git & GitHub** section of CLAUDE.md.
