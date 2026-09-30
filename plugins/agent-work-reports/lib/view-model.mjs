const ACTIVE = new Set(["assigned", "reserved", "starting", "running", "working", "waiting", "tool_wait", "reviewing", "review", "verifying", "verification", "stopping"]);
const BLOCKED = new Set(["blocked", "capacity-blocked", "retry-pending", "cleanup-required", "needs-attention", "stale", "disconnected"]);
const DONE = new Set(["done", "completed", "merged", "released", "stopped", "retired"]);

function text(value, fallback = "") {
  return value === null || value === undefined ? fallback : String(value);
}

function timestamp(value) {
  const time = Date.parse(value || "");
  return Number.isFinite(time) ? time : 0;
}

function normalizeStatus(value) {
  const status = text(value, "unknown").trim().toLowerCase().replace(/\s+/g, "-");
  if (ACTIVE.has(status)) return status;
  if (BLOCKED.has(status)) return status;
  if (DONE.has(status)) return status;
  if (["failed", "error", "interrupted", "orphaned"].includes(status)) return "failed";
  if (["queued", "open", "pending", "planned"].includes(status)) return "queued";
  return status || "unknown";
}

function statusBucket(status) {
  if (BLOCKED.has(status) || status === "failed") return "blocked";
  if (ACTIVE.has(status)) return "active";
  if (DONE.has(status)) return "done";
  if (status === "queued") return "queued";
  return "other";
}

function fallbackProgress(status) {
  if (DONE.has(status)) return 100;
  if (["reviewing", "review", "verifying", "verification"].includes(status)) return 85;
  if (ACTIVE.has(status)) return 50;
  if (BLOCKED.has(status) || status === "failed") return 50;
  return 0;
}

function groupReports(reports = []) {
  const byAgent = new Map();
  const byTask = new Map();
  for (const report of reports) {
    if (!report?.agentId) continue;
    if (!byAgent.has(report.agentId)) byAgent.set(report.agentId, []);
    byAgent.get(report.agentId).push(report);
    if (report.taskId) {
      if (!byTask.has(report.taskId)) byTask.set(report.taskId, []);
      byTask.get(report.taskId).push(report);
    }
  }
  for (const values of [...byAgent.values(), ...byTask.values()]) {
    values.sort((a, b) => timestamp(b.at) - timestamp(a.at));
  }
  return { byAgent, byTask };
}

function latestReport(list = []) {
  return list.length ? list[0] : null;
}

function managedAgentView(agent, reportsByAgent) {
  const reports = reportsByAgent.get(agent.id) || [];
  const latest = latestReport(reports);
  const status = normalizeStatus(agent.status);
  const progress = latest?.progress ?? fallbackProgress(status);
  return {
    id: agent.id,
    provider: agent.executionProvider || "agent-control",
    source: "managed",
    role: agent.roleLabel || agent.role || "Agent",
    roleKey: agent.role || null,
    status,
    bucket: statusBucket(status),
    taskId: agent.taskId || null,
    task: agent.task || "No task description",
    machine: agent.machine || null,
    branch: agent.branchName || null,
    prNumber: agent.prNumber ?? null,
    progress,
    phase: latest?.phase || null,
    summary: latest?.summary || agent.lastMessage || agent.task || "No update yet",
    details: latest?.details || null,
    startedAt: agent.startedAt || null,
    updatedAt: latest?.at || agent.lastProgressAt || agent.heartbeatAt || agent.updatedAt || null,
    finishedAt: agent.finishedAt || null,
    recentReports: reports.slice(0, 12)
  };
}

function federatedAgentView(agent, reportsByAgent) {
  const id = agent.agent_id || agent.id;
  const reports = reportsByAgent.get(id) || [];
  const latest = latestReport(reports);
  const status = normalizeStatus(agent.effective_state || agent.status);
  return {
    id,
    provider: agent.provider_id || agent.provider || agent.source_provider || "federated",
    source: "federated",
    role: agent.role || "External agent",
    roleKey: agent.role || null,
    status,
    bucket: statusBucket(status),
    taskId: agent.task_id || null,
    task: agent.task || "No task description",
    machine: agent.machine || null,
    branch: agent.branch || null,
    prNumber: agent.pr_number ?? null,
    progress: latest?.progress ?? fallbackProgress(status),
    phase: latest?.phase || null,
    summary: latest?.summary || agent.message || agent.task || "No update yet",
    details: latest?.details || null,
    startedAt: agent.started_at || null,
    updatedAt: latest?.at || agent.heartbeat_at || agent.updated_at || null,
    finishedAt: agent.finished_at || null,
    recentReports: reports.slice(0, 12)
  };
}

function reportOnlyAgentView(agentId, reports) {
  const latest = latestReport(reports);
  const status = latest?.phase === "done" ? "done" : latest?.phase === "blocked" ? "blocked" : "working";
  return {
    id: agentId,
    provider: latest?.source || "report",
    source: "report-only",
    role: "External agent",
    roleKey: null,
    status,
    bucket: statusBucket(status),
    taskId: latest?.taskId || null,
    task: latest?.summary || "Reported work",
    machine: null,
    branch: latest?.branch || null,
    prNumber: null,
    progress: latest?.progress ?? fallbackProgress(status),
    phase: latest?.phase || null,
    summary: latest?.summary || "Reported work",
    details: latest?.details || null,
    startedAt: reports.at(-1)?.at || null,
    updatedAt: latest?.at || null,
    finishedAt: latest?.phase === "done" ? latest.at : null,
    recentReports: reports.slice(0, 12)
  };
}

function buildAgents(snapshot, grouped) {
  const agents = [];
  const seen = new Set();
  for (const agent of snapshot?.agents || []) {
    if (!agent?.id || seen.has(agent.id)) continue;
    seen.add(agent.id);
    agents.push(managedAgentView(agent, grouped.byAgent));
  }
  for (const agent of snapshot?.federatedAgents || snapshot?.federation?.agents || []) {
    const id = agent?.agent_id || agent?.id;
    if (!id || seen.has(id)) continue;
    seen.add(id);
    agents.push(federatedAgentView(agent, grouped.byAgent));
  }
  for (const [agentId, reports] of grouped.byAgent.entries()) {
    if (seen.has(agentId)) continue;
    agents.push(reportOnlyAgentView(agentId, reports));
  }
  const order = { blocked: 0, active: 1, queued: 2, other: 3, done: 4 };
  agents.sort((a, b) => (order[a.bucket] - order[b.bucket]) || (timestamp(b.updatedAt) - timestamp(a.updatedAt)) || a.id.localeCompare(b.id));
  return agents;
}

function buildWork(snapshot, grouped, agents) {
  const work = [];
  const seen = new Set();
  const agentsById = new Map(agents.map(agent => [agent.id, agent]));
  for (const task of snapshot?.tasks || []) {
    if (!task?.id || seen.has(task.id)) continue;
    seen.add(task.id);
    const reports = grouped.byTask.get(task.id) || [];
    const latest = latestReport(reports);
    const owner = agentsById.get(task.agentId) || null;
    const status = normalizeStatus(task.status || owner?.status);
    work.push({
      id: task.id,
      title: task.objective || owner?.task || task.id,
      ownerId: task.agentId || owner?.id || null,
      ownerRole: owner?.role || task.roleLabel || task.role || null,
      status,
      bucket: statusBucket(status),
      progress: latest?.progress ?? owner?.progress ?? fallbackProgress(status),
      summary: latest?.summary || task.nextAction || owner?.summary || task.objective || "No update yet",
      branch: task.branchName || owner?.branch || null,
      priority: Number(task.priority || 0),
      updatedAt: latest?.at || task.updatedAt || owner?.updatedAt || task.requestedAt || null,
      dependencies: Array.isArray(task.dependencies) ? task.dependencies : [],
      blockers: Array.isArray(task.blockers) ? task.blockers : [],
      acceptanceCriteria: Array.isArray(task.acceptanceCriteria) ? task.acceptanceCriteria : [],
      verification: Array.isArray(task.verification) ? task.verification : []
    });
  }
  for (const [taskId, reports] of grouped.byTask.entries()) {
    if (seen.has(taskId)) continue;
    const latest = latestReport(reports);
    const owner = agentsById.get(latest?.agentId) || null;
    const status = latest?.phase === "done" ? "done" : latest?.phase === "blocked" ? "blocked" : "working";
    work.push({
      id: taskId,
      title: latest?.summary || taskId,
      ownerId: latest?.agentId || null,
      ownerRole: owner?.role || null,
      status,
      bucket: statusBucket(status),
      progress: latest?.progress ?? fallbackProgress(status),
      summary: latest?.summary || "Reported work",
      branch: latest?.branch || owner?.branch || null,
      priority: 0,
      updatedAt: latest?.at || null,
      dependencies: [], blockers: [], acceptanceCriteria: [], verification: []
    });
  }
  const order = { blocked: 0, active: 1, queued: 2, other: 3, done: 4 };
  work.sort((a, b) => (order[a.bucket] - order[b.bucket]) || (b.priority - a.priority) || (timestamp(b.updatedAt) - timestamp(a.updatedAt)));
  return work;
}

function buildActivity(snapshot, reports) {
  const activity = [];
  for (const report of reports.slice(-200)) {
    activity.push({
      at: report.at,
      kind: `report.${report.phase}`,
      agentId: report.agentId,
      taskId: report.taskId || null,
      message: report.summary,
      source: report.source || "report"
    });
  }
  for (const event of snapshot?.recentEvents || []) {
    activity.push({
      at: event.at || event.createdAt || event.updatedAt || null,
      kind: event.type || event.kind || "agent-control.event",
      agentId: event.agentId || event.agent_id || null,
      taskId: event.taskId || event.task_id || null,
      message: event.message || event.summary || event.type || "Agent Control event",
      source: "agent-control"
    });
  }
  activity.sort((a, b) => timestamp(b.at) - timestamp(a.at));
  return activity.slice(0, 120);
}

export function buildViewModel({ snapshot = null, reports = [], now = Date.now(), sourceError = null } = {}) {
  const grouped = groupReports(reports);
  const agents = buildAgents(snapshot, grouped);
  const work = buildWork(snapshot, grouped, agents);
  const todayStart = new Date(now); todayStart.setHours(0, 0, 0, 0);
  const stats = {
    activeAgents: agents.filter(agent => agent.bucket === "active").length,
    blockedAgents: agents.filter(agent => agent.bucket === "blocked").length,
    doneAgentsToday: agents.filter(agent => agent.bucket === "done" && timestamp(agent.finishedAt || agent.updatedAt) >= todayStart.getTime()).length,
    knownAgents: agents.length,
    openWork: work.filter(item => item.bucket !== "done").length,
    doneWork: work.filter(item => item.bucket === "done").length
  };
  return {
    schema: "agent-work-reports/view/v1",
    generatedAt: new Date(now).toISOString(),
    source: {
      agentControl: snapshot ? "online" : "unavailable",
      agentControlGeneratedAt: snapshot?.generatedAt || null,
      error: sourceError ? String(sourceError).slice(0, 1000) : null
    },
    mission: snapshot?.currentMission || null,
    repository: snapshot?.repository || null,
    stats,
    work,
    agents,
    activity: buildActivity(snapshot, reports)
  };
}
