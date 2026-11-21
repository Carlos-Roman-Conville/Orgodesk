# Orgodesk: design

**Standard check** ([architecture standard](../orgo-suite/ARCHITECTURE-STANDARD.md)): **not written yet**; every section below is a placeholder. Missing: 1 to 9. Until it is written, the design lives in: README.md, SCOPE.md, ISSUES.md and `HQ/handoffs/orgodesk`. The next agent that touches this tool fills in what it can and updates this line. Last checked 2026-11-21 by Claude.

## 1. In plain English
What the tool does, who it is for, and how it works, in one short paragraph a non-programmer can follow.

## 2. The big picture
A plain-text diagram of the pieces and how data moves through them, then two or three sentences walking through
one real use from start to finish (for example: "you drop a file in Downloads → the watcher sees it → ...").

## 3. The pieces
| Piece | What it does | Why it is separate | Where in the code |
|---|---|---|---|

## 4. Shared parts and suite place
- Shared parts it uses (orgo-filecore, crc-appshell, orgo-commands, orgo-recognize, Orgo.Windowing) and for what.
- Features it owns (must match SYSTEM.md section 3) and features it calls on other tools for.
- Its commands for OrgoAI (orgo-commands): the actions it lists.

## 5. Data it keeps
| Data | Where | Format | Who may read it |
|---|---|---|---|
What happens to the data on a crash, an upgrade, and an uninstall.

## 6. Words explained
| Word | Meaning here |
|---|---|

## 7. Decisions
One record per big decision (rules 3 to 5). Newest last; never deleted.

### D-1: <the decision in one line>
- **Status:** proposed | accepted (Rex, <date>) | replaced by D-n | needs evidence
- **Options considered:** at least two real ones, each with its cost.
- **Chosen because:** the evidence (test, measurement, source).
- **Cost accepted:** what this choice gives up.
- **Would change if:** what result or event would make us choose again.

## 8. Limits and risks
What it cannot do yet, what could go wrong, and what is not tested. Link ISSUES.md for details.

## 9. How it is checked
The tests and gates that prove it works (unit tests, VM rounds, independent tester rounds), and how to run them.
