import assert from "node:assert/strict";
import test from "node:test";
import { chooseBranchPlan, cleanupDisposition, ownedBranchNames } from "../lib/branch-lifecycle.mjs";

test("reuses an eligible existing branch on strong scope match", () => {
  const plan = chooseBranchPlan({
    task: "Implement updater rollback recovery",
    boundary: "updater-rollback",
    lane: "updater",
    branches: [
      { name: "feature/updater-rollback-20260929", remote: true },
      { name: "feature/unrelated-ui", remote: true }
    ]
  });
  assert.equal(plan.mode, "reused");
  assert.equal(plan.branchName, "feature/updater-rollback-20260929");
});

test("rejects a branch with a live lease", () => {
  const plan = chooseBranchPlan({
    task: "Implement updater rollback recovery",
    boundary: "updater-rollback",
    lane: "updater",
    branches: [{ name: "feature/updater-rollback-20260929", remote: true }],
    leases: [{ status: "active", branchName: "feature/updater-rollback-20260929" }]
  });
  assert.equal(plan.mode, "created");
  assert.equal(plan.candidates[0].reason, "live-owner-or-lease");
});

test("creates a new branch when no strong match exists", () => {
  const plan = chooseBranchPlan({
    task: "Harden branch lifecycle cleanup",
    boundary: "branch-lifecycle",
    branches: [{ name: "feature/frontend-redesign", remote: true }]
  });
  assert.equal(plan.mode, "created");
  assert.equal(plan.branchName, null);
});

test("explicit existing base branch is reused when unowned", () => {
  const plan = chooseBranchPlan({
    task: "Continue existing updater work",
    baseBranch: "agent/auto-updater-20260928",
    branches: [{ name: "agent/auto-updater-20260928", remote: true }]
  });
  assert.equal(plan.mode, "reused");
  assert.equal(plan.branchName, "agent/auto-updater-20260928");
  assert.equal(plan.reason, "explicit-base-branch");
});

test("concurrent branch ownership is denied by ownership inventory", () => {
  const owned = ownedBranchNames({
    agents: [{ status: "running", branchName: "feature/shared-scope" }],
    leases: [{ status: "active", branchName: "feature/shared-scope" }]
  });
  assert.equal(owned.has("feature/shared-scope"), true);

  const plan = chooseBranchPlan({
    task: "Shared scope work",
    boundary: "shared-scope",
    branches: [{ name: "feature/shared-scope", remote: true }],
    agents: [{ status: "running", branchName: "feature/shared-scope" }]
  });
  assert.equal(plan.mode, "created");
});

test("integrated clean branch can finish only after verified deletion", () => {
  assert.deepEqual(cleanupDisposition({
    branchName: "feature/branch-lifecycle",
    branchSha: "abc",
    mainContainsBranchTip: true,
    canonicalTreeContainsBranchDelta: true,
    worktreeDirty: false,
    deletionSucceeded: true
  }), { status: "done", reason: "integrated-branch-cleaned" });
});

test("ancestor-only preservation is not canonical integration proof", () => {
  assert.deepEqual(cleanupDisposition({
    branchName: "feature/preserved-history-only",
    branchSha: "abc",
    mainContainsBranchTip: true,
    canonicalTreeContainsBranchDelta: false,
    deletionSucceeded: true
  }), { status: "preserved", reason: "branch-tip-ancestor-without-canonical-tree-proof" });
});

test("unique branch is preserved", () => {
  assert.deepEqual(cleanupDisposition({
    branchName: "feature/unique",
    branchSha: "abc",
    mainContainsBranchTip: false
  }), { status: "preserved", reason: "unique-or-unmerged-work" });
});

test("cleanup failure prevents completion", () => {
  assert.deepEqual(cleanupDisposition({
    branchName: "feature/integrated",
    branchSha: "abc",
    mainContainsBranchTip: true,
    deletionSucceeded: false
  }), { status: "cleanup-required", reason: "deletion-not-verified" });
});
