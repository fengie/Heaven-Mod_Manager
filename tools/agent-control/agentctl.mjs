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
  federation
  providers
  events
  control
  federation-register --file C:\\path\\observation.json
  federation-heartbeat --file C:\\path\\heartbeat.json
  provider-heartbeat PROVIDER [--status online]
  queue
  branches
  sync
  routing
  routing-set --file C:\\path\\routing.json
  routing-clear
  autonomy
  autonomy-set LEVEL
  pause [--reason TEXT]
  resume [--reason TEXT] [--clear-emergency-stop]
  read-only on|off [--reason TEXT]
  drain [--reason TEXT]
  emergency-stop
  stop-swarm [--discard]
  autopilot
  autopilot-start --task "big direction" [--base main] [--max-repairs 3] [--max-iterations 40]
  autopilot-start --task-file C:\\path\\direction.txt [options]
  autopilot-pause
  autopilot-resume
  autopilot-stop
  autopilot-step
  deploy --role support --task "..." [options]
  deploy --role support --task-file C:\\path\\task.txt [options]
  review <agent-id> [--task "..."] [--task-file C:\\path\\review.txt]
  stop <agent-id>
  log <agent-id>

Deploy options:
  --count N
  --base BRANCH
  --model MODEL
  --execution-mode chat|codex
  --boundary NAME
  --priority 0-100
  --machine auto|HOSTNAME
  --depends TASK_ID[,TASK_ID...]

Normal Chat is the default. The local controller does not auto-create ChatGPT chats and never falls back to Codex.
Use --execution-mode codex only when the user explicitly requested a Codex/local worker for that task.

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
  } else if (command === "federation") {
    print(await request("/api/federation"));
  } else if (command === "providers") {
    print(await request("/api/providers"));
  } else if (command === "events") {
    const data = await request("/api/snapshot");
    print(data.recentEvents || []);
  } else if (command === "control") {
    const data = await request("/api/snapshot");
    print({
      settings: data.settings || {},
      autonomyProfile: data.settings?.autonomyLevel
        ? data.autonomyProfiles?.[data.settings.autonomyLevel] || null
        : null,
      routingManifest: data.settings?.routingManifest || null
    });
  } else if (command === "federation-register" || command === "federation-heartbeat") {
    const file = String(flags.file || "").trim();
    if (!file) throw new Error("--file is required.");
    const observation = JSON.parse(fs.readFileSync(file, "utf8"));
    const endpoint = command === "federation-register" ? "/api/federation/observations" : "/api/federation/heartbeat";
    print(await request(endpoint, {
      method: "POST",
      body: JSON.stringify(observation)
    }));
  } else if (command === "provider-heartbeat") {
    const provider = String(flags._[1] || "").trim();
    if (!provider) throw new Error("Provider id is required.");
    print(await request(`/api/federation/providers/${encodeURIComponent(provider)}/heartbeat`, {
      method: "POST",
      body: JSON.stringify({ status: flags.status || "online" })
    }));
  } else if (command === "queue") {
    print(await request("/api/integration"));
  } else if (command === "branches") {
    print(await request("/api/branches"));
  } else if (command === "sync") {
    print(await request("/api/sync", {
      method: "POST",
      body: JSON.stringify({ repositoryWriteAuthorized: true })
    }));
  } else if (command === "routing") {
    print(await request("/api/control/routing-manifest"));
  } else if (command === "routing-set") {
    const file = String(flags.file || "").trim();
    if (!file) throw new Error("--file is required.");
    const manifest = JSON.parse(fs.readFileSync(file, "utf8"));
    print(await request("/api/control/routing-manifest", {
      method: "POST",
      body: JSON.stringify(manifest)
    }));
  } else if (command === "routing-clear") {
    print(await request("/api/control/routing-manifest", {
      method: "POST",
      body: JSON.stringify({ clear: true, reason: "agentctl-routing-clear" })
    }));
  } else if (command === "autonomy") {
    const data = await request("/api/workflows");
    const level = data.settings?.autonomyLevel || null;
    print({
      autonomyLevel: level,
      profile: level ? data.autonomyProfiles?.[level] || null : null
    });
  } else if (command === "autonomy-set") {
    const level = String(flags._[1] || flags.level || "").trim();
    if (!level) throw new Error("Autonomy level is required.");
    print(await request("/api/control/settings", {
      method: "POST",
      body: JSON.stringify({ autonomyLevel: level, reason: "agentctl-autonomy-set" })
    }));
  } else if (command === "pause") {
    print(await request("/api/control/pause", {
      method: "POST",
      body: JSON.stringify({ reason: flags.reason || "agentctl-pause" })
    }));
  } else if (command === "resume") {
    print(await request("/api/control/resume", {
      method: "POST",
      body: JSON.stringify({
        reason: flags.reason || "agentctl-resume",
        clearEmergencyStop: Boolean(flags["clear-emergency-stop"])
      })
    }));
  } else if (command === "read-only") {
    const raw = String(flags._[1] || "").trim().toLowerCase();
    if (!["on", "off"].includes(raw)) throw new Error("read-only requires on or off.");
    print(await request("/api/control/read-only", {
      method: "POST",
      body: JSON.stringify({
        enabled: raw === "on",
        reason: flags.reason || "agentctl-read-only"
      })
    }));
  } else if (command === "drain") {
    print(await request("/api/control/drain", {
      method: "POST",
      body: JSON.stringify({ reason: flags.reason || "agentctl-drain" })
    }));
  } else if (command === "emergency-stop") {
    print(await request("/api/control/emergency-stop", { method: "POST", body: "{}" }));
  } else if (command === "stop-swarm") {
    print(await request("/api/control/stop-swarm", {
      method: "POST",
      body: JSON.stringify({ preserve: !Boolean(flags.discard) })
    }));
  } else if (command === "autopilot") {
    print(await request("/api/autopilot"));
  } else if (command === "autopilot-start") {
    const objective = readTask(flags);
    if (!objective) throw new Error("--task or --task-file is required.");
    print(await request("/api/autopilot/start", {
      method: "POST",
      body: JSON.stringify({
        objective,
        baseBranch: flags.base || "main",
        maxRepairLoops: flags["max-repairs"] === undefined ? undefined : Number(flags["max-repairs"]),
        maxIterations: flags["max-iterations"] === undefined ? undefined : Number(flags["max-iterations"])
      })
    }));
  } else if (command === "autopilot-pause") {
    print(await request("/api/autopilot/pause", { method: "POST", body: JSON.stringify({ reason: "agentctl-pause" }) }));
  } else if (command === "autopilot-resume") {
    print(await request("/api/autopilot/resume", { method: "POST", body: JSON.stringify({ reason: "agentctl-resume" }) }));
  } else if (command === "autopilot-stop") {
    print(await request("/api/autopilot/stop", { method: "POST", body: JSON.stringify({ reason: "agentctl-stop" }) }));
  } else if (command === "autopilot-step") {
    print(await request("/api/autopilot/step", { method: "POST", body: "{}" }));
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
        executionMode: flags["execution-mode"] || "chat",
        boundary: flags.boundary || "",
        priority: flags.priority === undefined ? 50 : Number(flags.priority),
        machine: flags.machine || "auto",
        dependencies: dependencies(flags),
        repositoryWriteAuthorized: true
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
        executionMode: flags["execution-mode"] || "chat",
        boundary: flags.boundary || "",
        priority: flags.priority === undefined ? undefined : Number(flags.priority),
        machine: flags.machine || "auto",
        repositoryWriteAuthorized: true
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
