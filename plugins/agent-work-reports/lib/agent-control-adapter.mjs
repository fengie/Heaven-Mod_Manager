const DEFAULT_BASE = "http://127.0.0.1:7331";

function baseUrl(value) {
  const url = new URL(value || process.env.AGENT_CONTROL_BASE_URL || DEFAULT_BASE);
  if (!["127.0.0.1", "localhost", "::1", "[::1]"].includes(url.hostname)) {
    throw new Error("Agent Work Reports only connects to a loopback Agent Control endpoint.");
  }
  return url;
}

async function requestJson(pathname, { timeoutMs = 2500, base = undefined } = {}) {
  const root = baseUrl(base);
  const target = new URL(pathname, root);
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), Math.max(250, Number(timeoutMs) || 2500));
  try {
    const response = await fetch(target, {
      method: "GET",
      headers: { Accept: "application/json" },
      signal: controller.signal
    });
    if (!response.ok) throw new Error(`Agent Control returned HTTP ${response.status}.`);
    return await response.json();
  } finally {
    clearTimeout(timer);
  }
}

export async function loadAgentControlSnapshot(options = {}) {
  return await requestJson("/api/snapshot", options);
}

export async function loadAgentControlLog(agentId, options = {}) {
  const id = encodeURIComponent(String(agentId || "").trim());
  if (!id) throw new Error("agentId is required.");
  return await requestJson(`/api/agents/${id}/log`, options);
}
