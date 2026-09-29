import { defaultFederationState, federationSnapshot, migrateFederationState } from "./federated-registry.mjs";
import { ROLE_TEMPLATES } from "./prompt-templates.mjs";
import { defaultAutopilotState, normalizeAutopilotState } from "./autopilot-core.mjs";
import { defaultFederationState, migrateFederationState } from "./federated-registry.mjs";

export const STATE_VERSION = 5;
export const ACTIVE_STATUSES = new Set(["reserved", "starting", "running", "waiting", "blocked", "stale", "stopping"]);
export const TERMINAL_STATUSES = new Set(["done", "failed", "finished", "stopped", "interrupted", "orphaned"]);

export const AUTONOMY_PROFILES = Object.freeze({
  observe: {
    label: "Observe",
    description: "Monitoring only. No automatic dispatch or repository-changing actions.",
    permissions: []
  },
  assist: {
    label: "Assist",
    description: "Recommend and preview actions; operator starts meaningful work.",
    permissions: ["recommend", "preview"]
  },
  coordinate: {
    label: "Coordinate",
    description: "Manage bounded workers and verification automatically; integration remains approval-gated.",
    permissions: ["recommend", "preview", "dispatch-support", "replace-stale", "request-review", "run-tests"]
  },
  "engineering-autopilot": {
    label: "Engineering Autopilot",
    description: "Perform safe routine coordination, continuity, support, review and integration preparation while repository/release gates remain governed.",
    permissions: ["recommend", "preview", "dispatch-support", "replace-stale", "request-review", "run-tests", "prepare-integration", "maintain-continuity"]
  }
});

export const WORKFLOW_PERMISSION_REQUIREMENTS = Object.freeze({
  "usual-swarm": "dispatch-support",
  "support-current": "dispatch-support",
  integration: "prepare-integration",
  review: "request-review",
  release: "prepare-integration",
  "bug-hunt": "dispatch-support",
  "ui-ux": "dispatch-support",
  "updater-hardening": "dispatch-support",
  verification: "run-tests",
  research: "dispatch-support",
  continuity: "maintain-continuity",
  cleanup: "dispatch-support",
  "self-improve": "prepare-integration"
});

export function autonomyPermissionDecision(state, permission) {
  const level = String(state?.settings?.autonomyLevel || "assist");
  const profile = AUTONOMY_PROFILES[level];
  if (!profile) {
    return {
      allowed: false,
      level,
      permission,
      reason: `Unknown autonomy level "${level}" fails closed.`
    };
  }
  const allowed = profile.permissions.includes(permission);
  return {
    allowed,
    level,
    permission,
    profile: profile.label,
    reason: allowed
      ? `${profile.label} permits ${permission}.`
      : `${profile.label} does not permit ${permission}.`
  };
}

export function workflowPermission(workflowId) {
  return WORKFLOW_PERMISSION_REQUIREMENTS[String(workflowId || "")] || null;
}

export const DEFAULT_MACHINE_POLICIES = Object.freeze({
  heaven: {
    label: "heaven",
    role: "background-worker",
    repositoryWriteAllowed: false,
    foregroundUiAllowed: false,
    preserveResponsiveness: false,
    notes: "Read-only/background worker by default. Per-task repository-write authorization is required."
  },
  heaven2: {
    label: "heaven2",
    role: "primary-development",
    repositoryWriteAllowed: true,
    foregroundUiAllowed: true,
    preserveResponsiveness: true,
    notes: "Primary development machine; use only when the task genuinely needs canonical local development or Windows/MHW validation."
  }
});

export const WORKFLOW_PRESETS = Object.freeze([
  { id: "usual-swarm", label: "Usual Swarm", description: "Fill only missing useful roles toward 1 Manager + 1 Primary Programmer + 4 distinct support lanes." },
  { id: "support-current", label: "Support Current Programmer", description: "Create distinct support lanes around the active programmer without duplicating owned work." },
  { id: "integration", label: "Integrate Finished Work", description: "Inspect completed candidates and prepare evidence-backed selective integration." },
  { id: "review", label: "Review Current Work", description: "Independently review the newest implementation candidate." },
  { id: "release", label: "Prepare Release", description: "Assemble exact-SHA release evidence without silently publishing." },
  { id: "bug-hunt", label: "Bug Hunt", description: "Parallel independent investigation from deliberately different failure angles." },
  { id: "ui-ux", label: "UI / UX Improvement", description: "Isolated frontend/UX lane that avoids backend/updater ownership." },
  { id: "updater-hardening", label: "Updater Hardening", description: "Focused updater safety, rollback, packaging and Windows verification lanes." },
  { id: "verification", label: "Verify Everything", description: "Verification-only workers attempt to prove or disprove current claims." },
  { id: "research", label: "Research Swarm", description: "Parallel investigation with findings rather than uncontrolled implementation." },
  { id: "continuity", label: "Documentation / Continuity", description: "Refresh durable state so a replacement agent can continue immediately." },
  { id: "cleanup", label: "Cleanup / Technical Debt", description: "Identify debt and modify only clearly owned safe scope." },
  { id: "self-improve", label: "Improve Agent Manager", description: "Create a governed self-improvement implementation candidate for the control plane." }
]);

export function defaultControlState({ sessionId, hostname }) {
  return {
    version: STATE_VERSION,
    health: { mode: "healthy", degradedReason: null, recoveredFromBackupAt: null },
    controller: { sessionId, hostname, startedAt: new Date().toISOString() },
    settings: {
      autonomyLevel: "assist",
      dispatchPaused: false,
      readOnly: false,
      emergencyStop: false,
      machinePolicies: structuredClone(DEFAULT_MACHINE_POLICIES),
      routingManifest: null
    },
    autopilot: defaultAutopilotState(),
    federation: defaultFederationState(),
    agents: [],
    tasks: [],
    leases: [],
    events: [],
    notifications: [],
    improvements: [],
    promptHistory: []
  };
}

export function migrateControlState(parsed, context) {
  const base = defaultControlState(context);
  if (!parsed || typeof parsed !== "object") return base;
  return {
    ...base,
    ...parsed,
    version: STATE_VERSION,
    health: { ...base.health, ...(parsed.health || {}) },
    controller: { ...base.controller, ...(parsed.controller || {}), sessionId: context.sessionId, hostname: context.hostname },
    settings: {
      ...base.settings,
      ...(parsed.settings || {}),
      machinePolicies: { ...base.settings.machinePolicies, ...(parsed.settings?.machinePolicies || {}) }
    },
    autopilot: normalizeAutopilotState(parsed.autopilot),
    federation: migrateFederationState(parsed.federation),
    agents: Array.isArray(parsed.agents) ? parsed.agents : [],
    tasks: Array.isArray(parsed.tasks) ? parsed.tasks : [],
    leases: Array.isArray(parsed.leases) ? parsed.leases : [],
    events: Array.isArray(parsed.events) ? parsed.events : [],
    notifications: Array.isArray(parsed.notifications) ? parsed.notifications : [],
    improvements: Array.isArray(parsed.improvements) ? parsed.improvements : [],
    promptHistory: Array.isArray(parsed.promptHistory) ? parsed.promptHistory : []
  };
}

export function isActiveStatus(status) {
  return ACTIVE_STATUSES.has(String(status || ""));
}

export function isTerminalStatus(status) {
  return TERMINAL_STATUSES.has(String(status || ""));
}

export function isIntegrationEligible(agent) {
  return agent?.status === "done" && agent?.exitCode === 0 && agent?.completionEvidence === "authoritative-exit";
}

export function classifyAuthoritativeExit(agent, exitCode) {
  if (agent?.stopRequestedAt || agent?.status === "stopping" || agent?.status === "stopped") {
    return "stopped";
  }
  return exitCode === 0 ? "done" : "failed";
}

export function applyPreLaunchFailure(state, {
  taskId,
  leaseId,
  error,
  reason,
  at = new Date().toISOString(),
  retainedWorktree = null,
  retainedBranch = null
}) {
  const task = state.tasks.find(item => item.id === taskId) || null;
  if (task) {
    task.status = "failed";
    task.finishedAt = at;
    task.updatedAt = at;
    task.error = String(error || "Unknown pre-launch failure");
    task.retainedWorktree = retainedWorktree;
    task.retainedBranch = retainedBranch || task.branchName || null;
    task.nextAction = retainedWorktree
      ? "Inspect or remove the retained pre-launch worktree after preserving any useful evidence."
      : null;
  }

  const lease = state.leases.find(item => item.id === leaseId) || null;
  if (lease && lease.status === "active") {
    lease.status = "released";
    lease.releasedAt = at;
    lease.releaseReason = reason;
  }

  return { task, lease };
}

export function machinePolicy(state, machine) {
  const key = String(machine || "").trim().toLowerCase();
  return state.settings?.machinePolicies?.[key] || null;
}

export function canUseMachineForRepositoryWrite(state, machine, explicitAuthorization = false) {
  const key = String(machine || "").trim().toLowerCase();
  const policy = machinePolicy(state, key);
  if (!policy) return { allowed: false, reason: `Machine ${machine} is not registered in policy.` };
  if (policy.repositoryWriteAllowed) return { allowed: true, reason: "machine policy allows repository development" };
  if (key === "heaven" && explicitAuthorization) return { allowed: true, reason: "explicit per-task heaven repository-write authorization" };
  return { allowed: false, reason: policy.notes || "repository writes are not allowed on this machine" };
}

export function buildTaskGraph(tasks = []) {
  const nodes = tasks.map(task => ({
    id: task.id,
    objective: task.objective,
    role: task.role,
    status: task.status,
    owner: task.agentId || null,
    lane: task.lane || null,
    dependencies: Array.isArray(task.dependencies) ? task.dependencies : [],
    blocks: []
  }));
  const byId = new Map(nodes.map(node => [node.id, node]));
  for (const node of nodes) {
    for (const dependency of node.dependencies) {
      const parent = byId.get(dependency);
      if (parent && !parent.blocks.includes(node.id)) parent.blocks.push(node.id);
    }
  }
  return nodes;
}

function activeAgents(state, role = null) {
  return state.agents.filter(agent => isActiveStatus(agent.status) && (!role || agent.role === role));
}

const OCCUPIED_ROUTING_STATUSES = new Set(["claimed", "assigned", "active", "running", "blocked", "review", "verification"]);
const OPEN_ROUTING_STATUSES = new Set(["open", "missing", "unassigned"]);

export function routingManifestStatus(state, now = Date.now()) {
  const manifest = state.settings?.routingManifest;
  if (!manifest || !Array.isArray(manifest.assignments)) {
    return { available: false, current: false, reason: "routing-manifest-unavailable", manifest: null };
  }
  const observedAt = Date.parse(manifest.observedAt || "");
  const expiresAt = Date.parse(manifest.expiresAt || "");
  const timestamp = Number(now);
  if (!Number.isFinite(observedAt) || !Number.isFinite(expiresAt)) {
    return { available: true, current: false, reason: "routing-manifest-missing-freshness", manifest };
  }
  if (expiresAt <= timestamp) {
    return { available: true, current: false, reason: "routing-manifest-expired", manifest };
  }
  return { available: true, current: true, reason: null, manifest };
}

function routingAssignmentOccupied(assignment) {
  const status = String(assignment?.status || "").trim().toLowerCase();
  if (["stale", "superseded", "closed", "done", "abandoned"].includes(status)) return false;
  if (OCCUPIED_ROUTING_STATUSES.has(status)) return true;
  if (OPEN_ROUTING_STATUSES.has(status)) return false;
  return Boolean(assignment?.owner || assignment?.branch);
}

function currentRoutingAssignments(state, now = Date.now()) {
  const routing = routingManifestStatus(state, now);
  return routing.current ? routing.manifest.assignments : [];
}

function roleIsOccupied(state, role, now = Date.now()) {
  if (activeAgents(state, role).length) return true;
  const federated = federationSnapshot(migrateFederationState(state.federation), { now });
  if (federated.agents.some(agent => agent.live && agent.role === role)) return true;
  return currentRoutingAssignments(state, now).some(item => item.role === role && routingAssignmentOccupied(item));
}

export function deploymentBatchCapacity(state, requestedCount, maxActiveAgents) {
  const count = Math.max(1, Math.floor(Number(requestedCount) || 1));
  const maximum = Math.max(0, Math.floor(Number(maxActiveAgents) || 0));
  const active = activeAgents(state).length;
  const available = Math.max(0, maximum - active);
  return {
    count,
    active,
    maximum,
    available,
    allowed: count <= available
  };
}

export function workflowLeasePreflight(state, steps = []) {
  const occupied = new Set(
    (state?.leases || [])
      .filter(lease => lease?.status === "active")
      .map(lease => String(lease?.boundary || "").trim())
      .filter(Boolean)
  );

  for (const work of Array.isArray(steps) ? steps : []) {
    const boundary = String(work?.boundary || "").trim();
    if (!boundary) continue;
    if (occupied.has(boundary)) {
      return {
        allowed: false,
        boundary,
        reason: `Mutable boundary "${boundary}" is already leased or duplicated in this workflow.`
      };
    }
    occupied.add(boundary);
  }

  return { allowed: true, boundary: null, reason: null };
}

function activeLaneSet(state, now = Date.now()) {
  const lanes = new Set(activeAgents(state).map(agent => agent.lane).filter(Boolean));
  const federated = federationSnapshot(migrateFederationState(state.federation), { now });
  for (const agent of federated.agents) {
    if (!agent.live) continue;
    const lane = agent.source_metadata?.lane || null;
    if (lane) lanes.add(lane);
  }
  for (const assignment of currentRoutingAssignments(state, now)) {
    if (assignment?.lane && routingAssignmentOccupied(assignment)) lanes.add(assignment.lane);
  }
  return lanes;
}

export function deriveMission(state, repositoryContext = {}) {
  const federated = federationSnapshot(migrateFederationState(state.federation));
  const externalPrimary = federated.agents.find(agent => agent.live && agent.role === "main");
  const externalManager = federated.agents.find(agent => agent.live && agent.role === "manager");
  const primary = activeAgents(state, "main")[0];
  if (primary?.task) return primary.task;
  if (externalPrimary?.task) return externalPrimary.task;
  const manager = activeAgents(state, "manager")[0];
  if (manager?.task) return manager.task;
  if (externalManager?.task) return externalManager.task;
  const unfinished = state.tasks.find(task => !isTerminalStatus(task.status));
  if (unfinished?.objective) return unfinished.objective;
  if (repositoryContext.nextMilestone) return repositoryContext.nextMilestone;
  return "Assess current canonical repository state and choose the highest-value unblocked engineering action.";
}

export function supportLanesFor(objective = "") {
  const text = String(objective).toLowerCase();
  if (/updat|release|installer|rollback|packag/.test(text)) {
    return [
      { id: "architecture", title: "Architecture / code-path analysis", focus: "Map updater ownership, invariants, dependencies, and exact change surface." },
      { id: "tests", title: "Tests / regression coverage", focus: "Find missing regression, failure-path, concurrency, and recovery coverage." },
      { id: "adversarial", title: "Failure / security analysis", focus: "Challenge artifact trust, interruption, rollback, stale state, races, and fail-closed behavior." },
      { id: "windows", title: "Windows / packaging verification", focus: "Verify real Windows, packaging, process lifetime, update handoff, and runtime assumptions." }
    ];
  }
  if (/ui|ux|frontend|screen|dashboard|layout|interaction/.test(text)) {
    return [
      { id: "ux-audit", title: "UX architecture audit", focus: "Inspect information hierarchy, interaction cost, navigation, and task clarity." },
      { id: "states", title: "Empty / loading / failure states", focus: "Exercise realistic empty, loading, blocked, stale, failure, and recovery states." },
      { id: "responsive", title: "Responsive / accessibility review", focus: "Check mobile sizing, keyboard flow, semantics, readability, and accessibility." },
      { id: "regression", title: "Frontend integration regression", focus: "Check contracts with backend state, live updates, errors, and existing workflows." }
    ];
  }
  return [
    { id: "architecture", title: "Architecture / code-path analysis", focus: "Map ownership, invariants, dependencies, and collision risk." },
    { id: "tests", title: "Tests / regression coverage", focus: "Find unproven behavior and build targeted regression evidence." },
    { id: "adversarial", title: "Adversarial failure analysis", focus: "Challenge edge cases, races, interruption, stale state, and recovery." },
    { id: "continuity", title: "Documentation / continuity", focus: "Check durable handoff, exact state, and integration/release evidence." }
  ];
}

function step({ role, task, lane = null, boundary, priority = 50, machine = "auto", mode = "implementation", dependencies = [] }) {
  return { role, task, lane, boundary, priority, machine, mode, dependencies };
}

function newestCandidate(state) {
  return state.agents.find(agent => isIntegrationEligible(agent)) || state.agents.find(agent => agent.status === "done") || null;
}

export function planWorkflow(workflowId, {
  state,
  mission,
  baseBranch = "main",
  machine = "auto",
  requireReconciledOwnership = false,
  now = Date.now()
}) {
  const workflow = WORKFLOW_PRESETS.find(item => item.id === workflowId);
  if (!workflow) throw new Error(`Unknown workflow preset: ${workflowId}`);
  const objective = String(mission || deriveMission(state)).trim();
  const steps = [];
  const routing = routingManifestStatus(state, now);
  const assignments = routing.current ? routing.manifest.assignments : [];
  const lanes = activeLaneSet(state, now);
  const activeManagers = activeAgents(state, "manager");
  const activeProgrammers = activeAgents(state, "main");

  if (workflowId === "usual-swarm") {
    if (requireReconciledOwnership && !routing.current) {
      return {
        workflow,
        mission: objective,
        baseBranch,
        steps: [],
        blocked: [`Broad swarm dispatch requires a current routing manifest (${routing.reason}). Refresh repository/manager ownership or explicitly use a narrower workflow.`],
        ownership: { reconciled: false, reason: routing.reason }
      };
    }

    if (routing.current && routing.manifest.mode === "authoritative") {
      for (const assignment of assignments) {
        if (routingAssignmentOccupied(assignment)) continue;
        const status = String(assignment?.status || "open").toLowerCase();
        if (!OPEN_ROUTING_STATUSES.has(status) && status !== "") continue;
        steps.push(step({
          role: assignment.role || "support",
          task: assignment.task || assignment.objective || objective,
          lane: assignment.lane || null,
          boundary: assignment.boundary || `routing:${assignment.slotId || slug(assignment.lane || assignment.role || "slot")}`,
          priority: Number.isFinite(Number(assignment.priority)) ? Number(assignment.priority) : 70,
          machine: assignment.machine || machine,
          mode: assignment.mode || (assignment.role === "manager" ? "coordination" : "implementation"),
          dependencies: Array.isArray(assignment.dependencies) ? assignment.dependencies : []
        }));
      }
    } else {
      if (!roleIsOccupied(state, "manager", now)) steps.push(step({ role: "manager", task: `Coordinate this mission: ${objective}`, boundary: "swarm-coordination", priority: 90, machine, mode: "coordination" }));
      if (!roleIsOccupied(state, "main", now)) steps.push(step({ role: "main", task: objective, boundary: `implementation:${slug(objective)}`, priority: 85, machine }));
      for (const lane of supportLanesFor(objective)) {
        if (!lanes.has(lane.id)) steps.push(step({ role: "support", task: `${lane.title}. ${lane.focus} Primary mission: ${objective}`, lane: lane.id, boundary: `support:${lane.id}:${slug(objective)}`, priority: 65, machine, mode: "support" }));
      }
    }
  } else if (workflowId === "support-current") {
    const target = activeProgrammers[0];
    if (!target) return { workflow, mission: objective, baseBranch, steps: [], blocked: ["No active Primary Programmer exists. Start or resume a programming lane first."] };
    for (const lane of supportLanesFor(target.task || objective)) {
      if (!lanes.has(lane.id)) steps.push(step({ role: "support", task: `${lane.title}. ${lane.focus} Support primary agent ${target.id}: ${target.task}`, lane: lane.id, boundary: `support:${lane.id}:${target.id}`, priority: 70, machine, mode: "support", dependencies: [target.taskId].filter(Boolean) }));
    }
  } else if (workflowId === "integration") {
    steps.push(step({ role: "integration", task: `Inspect completed work for safe selective integration. Current mission: ${objective}`, boundary: "integration-queue", priority: 85, machine, mode: "integration" }));
  } else if (workflowId === "review") {
    const target = newestCandidate(state);
    if (!target) return { workflow, mission: objective, baseBranch, steps: [], blocked: ["No completed candidate is available for review."] };
    steps.push(step({ role: "reviewer", task: `Independently review agent ${target.id} on branch ${target.branchName}. Verify implementation, protocol compliance, regressions, tests, error handling, edge cases and integration safety.`, boundary: `review:${target.branchName}`, priority: 80, machine, mode: "review", dependencies: [target.taskId].filter(Boolean) }));
  } else if (workflowId === "release") {
    steps.push(step({ role: "release", task: `Prepare an evidence-backed release candidate for current canonical work. Do not silently publish. Mission context: ${objective}`, boundary: "release-readiness", priority: 95, machine, mode: "release" }));
  } else if (workflowId === "bug-hunt") {
    const bugLanes = [
      ["reproduction", "Reproduce and minimize the failure with exact environment evidence."],
      ["code-path", "Trace the responsible code/data path and competing ownership assumptions."],
      ["adversarial", "Probe races, stale state, interruption, malformed inputs, and recovery."],
      ["regression", "Identify the smallest regression test that would have caught this failure."]
    ];
    for (const [lane, focus] of bugLanes) if (!lanes.has(lane)) steps.push(step({ role: "research", task: `${focus} Problem: ${objective}`, lane, boundary: `bug-hunt:${lane}:${slug(objective)}`, priority: 70, machine, mode: "research" }));
  } else if (workflowId === "ui-ux") {
    steps.push(step({ role: "main", task: `Improve UI/UX in an isolated frontend ownership boundary without touching active backend/updater work. Objective: ${objective}`, lane: "ui-ux", boundary: "ui-ux-isolated", priority: 75, machine }));
  } else if (workflowId === "updater-hardening") {
    const updaterMission = /updat/i.test(objective) ? objective : `Updater hardening: ${objective}`;
    for (const lane of supportLanesFor(updaterMission)) if (!lanes.has(lane.id)) steps.push(step({ role: lane.id === "windows" ? "verification" : "support", task: `${lane.title}. ${lane.focus} Preserve staged install, exact artifact verification, rollback, protected user data, concurrency, private-repo auth, offline/fallback behavior, and real old→new recovery evidence.`, lane: lane.id, boundary: `updater:${lane.id}`, priority: 80, machine, mode: lane.id === "windows" ? "verification" : "support" }));
  } else if (workflowId === "verification") {
    const verify = [
      ["contracts", "Verify code and API contracts against exact current source."],
      ["regression", "Run and expand targeted regression and failure-path coverage."],
      ["concurrency", "Probe concurrency, interruption, stale state, retries and recovery."],
      ["platform", "Check authoritative Windows/runtime/packaging behavior that static tests cannot prove."]
    ];
    for (const [lane, focus] of verify) if (!lanes.has(`verify-${lane}`)) steps.push(step({ role: "verification", task: `${focus} Claims under test: ${objective}`, lane: `verify-${lane}`, boundary: `verification:${lane}:${slug(objective)}`, priority: 75, machine, mode: "verification" }));
  } else if (workflowId === "research") {
    for (const lane of supportLanesFor(objective)) if (!lanes.has(`research-${lane.id}`)) steps.push(step({ role: "research", task: `${lane.title}. ${lane.focus} Research only; return findings/evidence. Mission: ${objective}`, lane: `research-${lane.id}`, boundary: `research:${lane.id}:${slug(objective)}`, priority: 55, machine, mode: "research" }));
  } else if (workflowId === "continuity") {
    steps.push(step({ role: "documentation", task: `Bring durable project state up to date for immediate takeover. Mission: ${objective}`, boundary: "continuity", priority: 60, machine, mode: "documentation" }));
  } else if (workflowId === "cleanup") {
    steps.push(step({ role: "cleanup", task: `Find, rank, and safely address only clearly owned technical debt. Do not widen scope into active work. Mission: ${objective}`, boundary: "technical-debt", priority: 45, machine }));
  } else if (workflowId === "self-improve") {
    steps.push(step({ role: "main", task: `Improve the Agent Manager itself through the governed proposal → isolated implementation → test → review → upgrade-candidate pipeline. Requested improvement: ${objective}`, lane: "agent-manager-self-improve", boundary: "agent-control-self-improvement", priority: 80, machine }));
  }

  return {
    workflow,
    mission: objective,
    baseBranch,
    steps,
    blocked: [],
    ownership: {
      reconciled: routing.current,
      reason: routing.reason,
      source: routing.current ? routing.manifest.source || null : null,
      mode: routing.current ? routing.manifest.mode || "overlay" : null,
      observedAt: routing.current ? routing.manifest.observedAt || null : null,
      expiresAt: routing.current ? routing.manifest.expiresAt || null : null
    }
  };
}

export function interpretCommand(command) {
  const text = String(command || "").trim();
  const lower = text.toLowerCase();
  const rules = [
    [/usual swarm|deploy.*swarm|continue whatever|resume.*work/, "usual-swarm"],
    [/support.*(programmer|updater|whoever)|help.*(programmer|updater|agents)/, "support-current"],
    [/ship|prepare.*release|release readiness/, "release"],
    [/test everything|verify everything|verification/, "verification"],
    [/integrat|merge finished|finished work/, "integration"],
    [/review/, "review"],
    [/bug|investigat.*failure|why.*fail/, "bug-hunt"],
    [/improve.*(ui|ux|frontend)|make.*screen.*better/, "ui-ux"],
    [/fix updater|updater harden|updater/, "updater-hardening"],
    [/research/, "research"],
    [/continuity|handoff|document/, "continuity"],
    [/clean.*stale|technical debt|cleanup/, "cleanup"],
    [/agent manager.*better|improve agent manager|self improve/, "self-improve"]
  ];
  const match = rules.find(([regex]) => regex.test(lower));
  return {
    command: text,
    workflowId: match?.[1] || null,
    confidence: match ? "high" : "low",
    needsClarification: !match,
    explanation: match ? `Mapped to the ${WORKFLOW_PRESETS.find(item => item.id === match[1])?.label} workflow.` : "No safe predefined workflow matched. The command can be stored as a custom objective without automatic dispatch."
  };
}

export function recommendNextActions({ state, integrationQueue = [], repositoryContext = {} }) {
  const items = [];
  const push = (priority, kind, title, reason, action = null) => items.push({ priority, kind, title, reason, action });
  const uncertain = state.agents.filter(agent => ["interrupted", "orphaned", "stale"].includes(agent.status));
  if (state.settings?.emergencyStop) push(100, "safety", "Emergency stop is active", "Autonomous dispatch is disabled until an operator resumes it.", { type: "resume-dispatch" });
  if (state.health?.mode === "degraded") push(100, "safety", "State registry is degraded", state.health.degradedReason || "Authoritative ownership state could not be recovered.", null);
  if (uncertain.length) push(95, "recovery", `${uncertain.length} worker${uncertain.length === 1 ? "" : "s"} need reconciliation`, "Their exact process ownership or completion could not be proven. Preserve state before replacement.", { type: "inspect-workers", ids: uncertain.map(agent => agent.id) });
  const reviewable = integrationQueue.filter(item => item.state === "candidate" && item.reviewState !== "approved");
  if (reviewable.length) push(85, "review", `${reviewable.length} integration candidate${reviewable.length === 1 ? "" : "s"} need independent review`, "Completed branches should enter review before integration.", { type: "workflow", workflowId: "review" });
  const failed = state.agents.filter(agent => agent.status === "failed");
  const quota = failed.filter(agent => /usage limit|credits|quota/i.test(agent.lastMessage || agent.error || ""));
  if (quota.length) push(80, "runtime", `${quota.length} worker${quota.length === 1 ? "" : "s"} were blocked by runtime quota`, "Retrying immediately is unlikely to add engineering value until runtime capacity is available.", null);
  const activeMain = activeAgents(state, "main");
  const federatedMain = federationSnapshot(migrateFederationState(state.federation)).agents.some(agent => agent.live && agent.role === "main");
  if (!activeMain.length && !federatedMain && !reviewable.length && !state.settings?.dispatchPaused && !state.settings?.emergencyStop) push(60, "dispatch", "No active Primary Programmer is registered", deriveMission(state, repositoryContext), { type: "workflow", workflowId: "usual-swarm" });
  if (!items.length) push(40, "status", "No urgent control-plane action", "Current registered work has no detected blocker requiring operator intervention.", null);
  return items.sort((a, b) => b.priority - a.priority).slice(0, 5);
}

export function takeoverContext(agent, task, evidence = []) {
  return {
    generatedAt: new Date().toISOString(),
    role: agent?.role || task?.role || null,
    agentId: agent?.id || null,
    taskId: task?.id || agent?.taskId || null,
    scope: task?.objective || agent?.task || null,
    status: agent?.status || task?.status || null,
    branch: agent?.branchName || task?.branchName || null,
    baseBranch: agent?.baseBranch || task?.baseBranch || null,
    baseSha: agent?.baseSha || task?.baseSha || null,
    currentSha: agent?.currentSha || null,
    lane: agent?.lane || task?.lane || null,
    machine: agent?.machine || task?.machine || null,
    dependencies: task?.dependencies || agent?.dependencies || [],
    lastMessage: agent?.lastMessage || "",
    evidence,
    unresolved: task?.blockers || [],
    nextAction: task?.nextAction || "Re-establish canonical truth and continue only the smallest unfinished boundary.",
    constraints: [
      "Preserve repository/company governance and recursive continuity.",
      "Do not trust stale status or PID identity over current evidence.",
      "Do not duplicate another active mutable boundary."
    ]
  };
}

export function slug(value, max = 36) {
  return String(value || "task").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, max) || "task";
}

export function roleCatalog() {
  return ROLE_TEMPLATES;
}
