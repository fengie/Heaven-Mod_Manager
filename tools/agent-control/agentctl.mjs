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
  list
  deploy --role support --task "..." [--count 1] [--base main] [--model MODEL]
  stop <agent-id>
  log <agent-id>

Examples:
  node agentctl.mjs list
  node agentctl.mjs deploy --role support --task "Audit updater rollback" --count 2 --base agent/auto-updater-20260928
`);
}

const flags = parseFlags(process.argv.slice(2));
const command = flags._[0];

try {
  if (!command || command === "help" || command === "--help") {
    usage();
  } else if (command === "status") {
    console.log(JSON.stringify(await request("/api/status"), null, 2));
  } else if (command === "list") {
    console.log(JSON.stringify(await request("/api/agents"), null, 2));
  } else if (command === "deploy") {
    const task = flags.task || flags.t;
    if (!task) throw new Error("--task is required.");
    const result = await request("/api/deploy", {
      method: "POST",
      body: JSON.stringify({
        role: flags.role || "support",
        task,
        count: Number(flags.count || 1),
        baseBranch: flags.base || "main",
        model: flags.model || ""
      })
    });
    console.log(JSON.stringify(result, null, 2));
  } else if (command === "stop") {
    const id = flags._[1];
    if (!id) throw new Error("Agent id is required.");
    console.log(JSON.stringify(await request(`/api/agents/${encodeURIComponent(id)}/stop`, { method: "POST" }), null, 2));
  } else if (command === "log") {
    const id = flags._[1];
    if (!id) throw new Error("Agent id is required.");
    console.log(JSON.stringify(await request(`/api/agents/${encodeURIComponent(id)}/log`), null, 2));
  } else {
    usage();
    process.exitCode = 2;
  }
} catch (error) {
  console.error(`ERROR: ${error.message || error}`);
  process.exitCode = 1;
}
