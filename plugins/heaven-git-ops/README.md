# Heaven Git Ops

Structured Git operations above Heaven Control Plane.

Includes log/show/branch inventory, fetch/fast-forward pull, add/commit/push, safe branch deletion, and `integrate_task_to_main()`.

Push, branch deletion, and integration require explicit confirmation. Branch deletion verifies the branch tip is already an ancestor of `origin/main`. The integration workflow requires a clean tree, rebases against current `origin/main`, optionally runs a caller-supplied verification command, fast-forwards `main`, pushes, verifies remote-main identity, then deletes the completed branch.

Validate with:

```powershell
python .\plugins\heaven-git-ops\verify.py
```
