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
  const text = await response.text();
  let data;
  try { data = JSON.parse(text); } catch { data = text; }
  if (!response.ok) throw new Error(typeof data === "string" ? data : (data.error || JSON.stringify(data)));
  return data;
}

function usage() {
  console.log(`Heaven Agent Control CLI

Commands:
  status
  snapshot
  list
  tasks
  leases
  workers
  queue
  branches
  sync
  deploy --role support --task "..." [options]
  deploy --role support --task-file C:\\path\\task.txt [options]
  review <agent-id> [--task "..."] [--task-file C:\\path\\review.txt]
  stop <agent-id>
  log <agent-id>

Deploy options:
  --count N
  --base BRANCH
  --model MODEL
  --boundary NAME
  --priority 0-100
  --machine auto|HOSTNAME
  --depends TASK_ID[,TASK_ID...]

Examples:
  node agentctl.mjs snapshot
  node agentctl.mjs deploy --role support --task "Audit updater rollback" --count 2 --base agent/auto-updater-20260928
  node agentctl.mjs deploy --role main --task-file C:\\Temp\\task.txt --boundary updater-ui --priority 90
  node agentctl.mjs review support-20260928...
`);
}

function readTask(flags) {
  if (flags["task-file"]) {
    return fs.readFileSync(String(flags["task-file"]), "utf8").trim();
  }
  return String(flags.task || flags.t || "").trim();
}

function dependencies(flags) {
  return String(flags.depends || "")
    .split(",")
    .map(value => value.trim())
    .filter(Boolean);
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
  } else if (command === "sync") {
    print(await request("/api/sync", { method: "POST", body: "{}" }));
  } else if (command === "deploy") {
    const task = readTask(flags);
    if (!task) throw new Error("--task or --task-file is required.");

    print(await request("/api/deploy", {
      method: "POST",
      body: JSON.stringify({
        role: flags.role || "support",
        task,
        count: Number(flags.count || 1),
        baseBranch: flags.base || "main",
        model: flags.model || "",
        boundary: flags.boundary || "",
        priority: flags.priority === undefined ? 50 : Number(flags.priority),
        machine: flags.machine || "auto",
        dependencies: dependencies(flags)
      })
    }));
  } else if (command === "review") {
    const id = flags._[1];
    if (!id) throw new Error("Agent id is required.");
    const task = readTask(flags);

    print(await request(`/api/agents/${encodeURIComponent(id)}/review`, {
      method: "POST",
      body: JSON.stringify({
        task: task || undefined,
        model: flags.model || "",
        boundary: flags.boundary || "",
        priority: flags.priority === undefined ? undefined : Number(flags.priority),
        machine: flags.machine || "auto"
      })
    }));
  } else if (command === "stop") {
    const id = flags._[1];
    if (!id) throw new Error("Agent id is required.");
    print(await request(`/api/agents/${encodeURIComponent(id)}/stop`, { method: "POST" }));
  } else if (command === "log") {
    const id = flags._[1];
    if (!id) throw new Error("Agent id is required.");
    print(await request(`/api/agents/${encodeURIComponent(id)}/log`));
  } else {
    usage();
    process.exitCode = 2;
  }
} catch (error) {
  console.error(`ERROR: ${error.message || error}`);
  process.exitCode = 1;
}
