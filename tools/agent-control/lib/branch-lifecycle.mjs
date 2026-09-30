const RESERVED = new Set(["main", "heaven-bridge"]);
const ACTIVE_TASK_STATUSES = new Set(["reserved", "starting", "running", "waiting", "blocked", "stale", "cleanup-required"]);
const ACTIVE_AGENT_STATUSES = new Set(["reserved", "starting", "running", "waiting", "blocked", "stale", "stopping"]);

function slug(value) {
  return String(value || "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function tokens(value) {
  const stop = new Set([
    "agent", "control", "feature", "fix", "support", "integration", "recovery", "task",
    "main", "branch", "work", "update", "current", "the", "and", "for", "with", "from",
    "2026", "20260929"
  ]);
  return slug(value)
    .split("-")
    .filter(token => token.length >= 4 && !stop.has(token) && !/^\d+$/.test(token));
}

function overlapScore(left, right) {
  const a = new Set(tokens(left));
  const b = new Set(tokens(right));
  if (!a.size || !b.size) return { overlap: 0, ratio: 0 };
  let overlap = 0;
  for (const token of a) if (b.has(token)) overlap += 1;
  return { overlap, ratio: overlap / Math.min(a.size, b.size) };
}

export function ownedBranchNames({ leases = [], tasks = [], agents = [] } = {}) {
  const owned = new Set();
  for (const lease of leases) {
    if (lease?.status === "active" && lease?.branchName) owned.add(String(lease.branchName));
  }
  for (const task of tasks) {
    if (ACTIVE_TASK_STATUSES.has(String(task?.status || "")) && task?.branchName) {
      owned.add(String(task.branchName));
    }
  }
  for (const agent of agents) {
    if (ACTIVE_AGENT_STATUSES.has(String(agent?.status || "")) && agent?.branchName) {
      owned.add(String(agent.branchName));
    }
  }
  return owned;
}

export function chooseBranchPlan({
  task,
  boundary,
  lane,
  baseBranch = "main",
  branches = [],
  leases = [],
  tasks = [],
  agents = [],
  checkedOutBranches = []
} = {}) {
  const branchMap = new Map();
  for (const item of branches) {
    const name = typeof item === "string" ? item : item?.name;
    if (!name) continue;
    const existing = branchMap.get(name) || { name, local: false, remote: false };
    if (typeof item === "string") existing.remote = true;
    else {
      existing.local ||= Boolean(item.local);
      existing.remote ||= Boolean(item.remote);
    }
    branchMap.set(name, existing);
  }

  const owned = ownedBranchNames({ leases, tasks, agents });
  const checkedOut = new Set(checkedOutBranches.map(String));
  const requestedBase = String(baseBranch || "main").trim();
  const decisions = [];

  function reject(name, reason) {
    decisions.push({ name, eligible: false, score: 0, reason });
  }

  function eligible(name, score, reason) {
    decisions.push({ name, eligible: true, score, reason });
  }

  for (const name of [...branchMap.keys()].sort()) {
    if (RESERVED.has(name)) {
      reject(name, "reserved-branch");
      continue;
    }
    if (owned.has(name)) {
      reject(name, "live-owner-or-lease");
      continue;
    }
    if (checkedOut.has(name)) {
      reject(name, "checked-out-in-worktree");
      continue;
    }

    if (requestedBase !== "main" && requestedBase !== "heaven-bridge" && name === requestedBase) {
      eligible(name, 1000, "explicit-base-branch");
      continue;
    }

    let score = 0;
    const reasons = [];
    const boundarySlug = slug(boundary);
    const laneSlug = slug(lane);
    const nameSlug = slug(name);

    if (boundarySlug && !boundarySlug.startsWith("isolated-") && boundarySlug.length >= 6 && nameSlug.includes(boundarySlug)) {
      score += 500;
      reasons.push("boundary-match");
    }
    if (laneSlug && laneSlug.length >= 4 && nameSlug.includes(laneSlug)) {
      score += 300;
      reasons.push("lane-match");
    }

    const taskMatch = overlapScore(task, name);
    if (taskMatch.overlap >= 2 && taskMatch.ratio >= 0.67) {
      score += 100 + (taskMatch.overlap * 10);
      reasons.push(`task-token-match:${taskMatch.overlap}`);
    }

    const prior = tasks.find(item =>
      item?.branchName === name &&
      !ACTIVE_TASK_STATUSES.has(String(item?.status || "")) &&
      (
        (boundary && item?.boundary === boundary) ||
        (lane && item?.lane === lane) ||
        (task && overlapScore(task, item?.objective || "").overlap >= 2)
      )
    );
    if (prior) {
      score += 200;
      reasons.push("prior-compatible-task");
    }

    if (score > 0) eligible(name, score, reasons.join(","));
    else reject(name, "no-strong-scope-match");
  }

  const candidates = decisions
    .filter(item => item.eligible)
    .sort((a, b) => b.score - a.score || a.name.localeCompare(b.name));

  if (candidates.length) {
    const top = candidates[0];
    const tied = candidates.filter(item => item.score === top.score);
    if (tied.length === 1) {
      return {
        mode: "reused",
        branchName: top.name,
        reason: top.reason,
        candidates: decisions
      };
    }
    return {
      mode: "created",
      branchName: null,
      reason: "ambiguous-existing-branch-match",
      candidates: decisions
    };
  }

  return {
    mode: "created",
    branchName: null,
    reason: "no-safe-compatible-existing-branch",
    candidates: decisions
  };
}

export function cleanupDisposition({
  branchName,
  branchSha,
  mainContainsBranchTip,
  canonicalTreeContainsBranchDelta = false,
  worktreeDirty = false,
  deletionSucceeded = false
} = {}) {
  if (!branchName || RESERVED.has(branchName)) {
    return { status: "preserved", reason: "reserved-or-missing-branch" };
  }
  if (!branchSha) {
    return { status: "preserved", reason: "branch-tip-unresolved" };
  }
  if (!mainContainsBranchTip) {
    return { status: "preserved", reason: "unique-or-unmerged-work" };
  }
  if (!canonicalTreeContainsBranchDelta) {
    return { status: "preserved", reason: "branch-tip-ancestor-without-canonical-tree-proof" };
  }
  if (worktreeDirty) {
    return { status: "cleanup-required", reason: "dirty-worktree-after-integration" };
  }
  if (!deletionSucceeded) {
    return { status: "cleanup-required", reason: "deletion-not-verified" };
  }
  return { status: "done", reason: "integrated-branch-cleaned" };
}

export const BRANCH_POLICY_RESERVED = Object.freeze([...RESERVED]);
