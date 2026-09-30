import fs from "node:fs";

const BASE = process.env.AGENT_CONTROL_URL || "http://127.0.0.1:7331";

function parseFlags(args) {
  const out = { _: [] };
  for (let i = 0; i < args.length; i += 1) {
    const value = args[i];
    if (!value.startsWith("--")) {
      out._.push(value);
      continue;
    }
    const key = value.slice(2);
    const next = args[i + 1];
    if (next && !next.startsWith("--")) {
      out[key] = next;
      i += 1;
    } else {
      out[key] = true;
    }
  }
  return out;
}

async function request(path, options = {}) {
  const response = await fetch(`${BASE}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(options.headers || {})
    }
  });
  const raw = await response.text();
  let data;
  try { data = JSON.parse(raw); } catch { data = raw; }
  if (!response.ok) throw new Error(typeof data === "string" ? data : (data.error || JSON.stringify(data)));
  return data;
}

function post(path, body = {}) {
  return request(path, { method: "POST", body: JSON.stringify(body) });
}

function usage() {
  console.log(`MHW Agent Control CLI

Inspect:
  status | snapshot | list | tasks | leases | workers | queue | branches
  workflows | recommend | release | search <text>
  log <agent-id> | prompt <agent-id> | takeover <agent-id> [--persist]

Plan / dispatch:
  command <natural language>
  workflow <preset-id> [--execute] [--objective "..."] [options]
  deploy --role support --task "..." [options]
  review <agent-id> [options]
  improve --request "..."
  improve-start <proposal-id> [options]

Lifecycle / safety:
  stop <agent-id>
  preserve-stop <agent-id>
  pause | resume [--clear-emergency] | drain
  read-only on|off
  autonomy observe|assist|coordinate|engineering-autopilot
  emergency-stop
  stop-swarm [--no-preserve]

Evidence:
  evidence <task-id> --type test --result pass [--source-sha SHA]
  review-verdict <agent-id> approved|changes-requested|rejected

Deployment options:
  --count N --base BRANCH --model MODEL --boundary NAME --priority 0-100
  --machine auto|HOSTNAME --depends TASK_ID[,TASK_ID...]
  --authorize-heaven-repo   explicit per-task permission for repository-writing work on heaven
`);
}

function readTextFlag(flags, key, fileKey) {
  if (flags[fileKey]) return fs.readFileSync(String(flags[fileKey]), "utf8").trim();
  return String(flags[key] || "").trim();
}

function readTask(flags) {
  return readTextFlag(flags, "task", "task-file") || String(flags.t || "").trim();
}

function dependencies(flags) {
  return String(flags.depends || "")
    .split(",")
    .map(value => value.trim())
    .filter(Boolean);
}

function commonDispatchBody(flags) {
  return {
    baseBranch: flags.base || "main",
    model: flags.model || "",
    boundary: flags.boundary || "",
    priority: flags.priority === undefined ? undefined : Number(flags.priority),
    machine: flags.machine || "auto",
    dependencies: dependencies(flags),
    repositoryWriteAuthorized: Boolean(flags["authorize-heaven-repo"])
  };
}

function print(value) {
  console.log(JSON.stringify(value, null, 2));
}

const flags = parseFlags(process.argv.slice(2));
const command = flags._[0];

try {
  if (!command || command === "help" || command === "--help") {
    usage();
  } else if (command === "status") {
    print(await request("/api/status"));
  } else if (command === "snapshot") {
    print(await request("/api/snapshot"));
  } else if (command === "list") {
    print(await request("/api/agents"));
  } else if (command === "tasks") {
    print(await request("/api/tasks"));
  } else if (command === "leases") {
    print(await request("/api/leases"));
  } else if (command === "workers") {
    print(await request("/api/workers"));
  } else if (command === "queue") {
    print(await request("/api/integration"));
  } else if (command === "branches") {
    print(await request("/api/branches"));
  } else if (command === "workflows") {
    print(await request("/api/workflows"));
  } else if (command === "recommend") {
    print(await request("/api/recommendations"));
  } else if (command === "release") {
    print(await request("/api/release"));
  } else if (command === "search") {
    const query = flags._.slice(1).join(" ") || String(flags.q || "");
    print(await request(`/api/search?q=${encodeURIComponent(query)}`));
  } else if (command === "sync") {
    print(await post("/api/sync", { repositoryWriteAuthorized: Boolean(flags["authorize-heaven-repo"]) }));
  } else if (command === "command") {
    const text = flags._.slice(1).join(" ") || readTask(flags);
    if (!text) throw new Error("Natural-language command is required.");
    print(await post("/api/command/resolve", {
      command: text,
      ...commonDispatchBody(flags)
    }));
  } else if (command === "workflow") {
    const workflowId = flags._[1];
    if (!workflowId) throw new Error("Workflow id is required.");
    const body = {
      ...commonDispatchBody(flags),
      objective: readTextFlag(flags, "objective", "objective-file")
    };
    const action = flags.execute ? "execute" : "preview";
    print(await post(`/api/workflows/${encodeURIComponent(workflowId)}/${action}`, body));
  } else if (command === "deploy") {
    const task = readTask(flags);
    if (!task) throw new Error("--task or --task-file is required.");
    print(await post("/api/deploy", {
      ...commonDispatchBody(flags),
      role: flags.role || "support",
      task,
      count: Number(flags.count || 1),
      priority: flags.priority === undefined ? 50 : Number(flags.priority)
    }));
  } else if (command === "review") {
    const id = flags._[1];
    if (!id) throw new Error("Agent id is required.");
    print(await post(`/api/agents/${encodeURIComponent(id)}/review`, {
      ...commonDispatchBody(flags),
      task: readTask(flags) || undefined
    }));
  } else if (command === "improve") {
    const requestText = readTextFlag(flags, "request", "request-file");
    if (!requestText) throw new Error("--request or --request-file is required.");
    print(await post("/api/improvements", { request: requestText }));
  } else if (command === "improve-start") {
    const proposalId = flags._[1];
    if (!proposalId) throw new Error("Proposal id is required.");
    print(await post(`/api/improvements/${encodeURIComponent(proposalId)}/start`, commonDispatchBody(flags)));
  } else if (command === "stop" || command === "preserve-stop") {
    const id = flags._[1];
    if (!id) throw new Error("Agent id is required.");
    print(await post(`/api/agents/${encodeURIComponent(id)}/${command}`));
  } else if (command === "log" || command === "prompt" || command === "takeover") {
    const id = flags._[1];
    if (!id) throw new Error("Agent id is required.");
    const method = command === "takeover" && flags.persist ? "POST" : "GET";
    const path = command === "takeover"
      ? `/api/agents/${encodeURIComponent(id)}/takeover`
      : `/api/agents/${encodeURIComponent(id)}/${command}`;
    print(await request(path, method === "GET" ? {} : { method, body: "{}" }));
  } else if (command === "pause" || command === "resume" || command === "drain") {
    const path = command === "drain" ? "/api/control/drain" : `/api/control/${command}`;
    print(await post(path, { clearEmergencyStop: Boolean(flags["clear-emergency"]) }));
  } else if (command === "read-only") {
    if (!["on", "off"].includes(flags._[1])) throw new Error("Use: read-only on|off");
    print(await post("/api/control/read-only", { enabled: flags._[1] === "on" }));
  } else if (command === "autonomy") {
    if (!flags._[1]) throw new Error("Autonomy level is required.");
    print(await post("/api/control/settings", { autonomyLevel: flags._[1] }));
  } else if (command === "emergency-stop") {
    print(await post("/api/control/emergency-stop"));
  } else if (command === "stop-swarm") {
    print(await post("/api/control/stop-swarm", { preserve: !flags["no-preserve"] }));
  } else if (command === "evidence") {
    const taskId = flags._[1];
    if (!taskId) throw new Error("Task id is required.");
    if (!flags.type || !flags.result) throw new Error("--type and --result are required.");
    print(await post(`/api/tasks/${encodeURIComponent(taskId)}/evidence`, {
      type: flags.type, result: flags.result, sourceSha: flags["source-sha"] || "", summary: flags.summary || ""
    }));
  } else if (command === "review-verdict") {
    const agentId = flags._[1];
    const verdict = flags._[2];
    if (!agentId || !verdict) throw new Error("Use: review-verdict <agent-id> approved|changes-requested|rejected");
    print(await post(`/api/integration/${encodeURIComponent(agentId)}/review-verdict`, { verdict }));
  } else {
    usage();
    process.exitCode = 2;
  }
} catch (error) {
  console.error(`ERROR: ${error.message || error}`);
  process.exitCode = 1;
}
