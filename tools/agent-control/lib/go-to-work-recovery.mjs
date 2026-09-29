const CHATGPT_HOSTS = new Set(["chatgpt.com", "www.chatgpt.com", "chat.openai.com"]);

const ACTIVE_STATES = new Set(["starting", "running", "waiting", "blocked", "stale"]);

function clean(value) {
  return String(value ?? "").trim();
}

function validIsoMs(value) {
  const ms = Date.parse(clean(value));
  return Number.isFinite(ms) ? ms : null;
}

function isChatGptProvider(agent) {
  const values = [
    agent?.provider,
    agent?.runtime,
    ...(Array.isArray(agent?.providers) ? agent.providers : [])
  ].map(value => clean(value).toLowerCase());
  return values.some(value => value === "chatgpt" || value.startsWith("chatgpt-"));
}

export function chatgptConversationUrl(agent) {
  const metadata = agent?.source_metadata && typeof agent.source_metadata === "object"
    ? agent.source_metadata
    : {};
  for (const candidate of [
    metadata.conversation_url,
    metadata.chat_url,
    metadata.url
  ]) {
    const raw = clean(candidate);
    if (!raw) continue;
    try {
      const url = new URL(raw);
      if (url.protocol === "https:" && CHATGPT_HOSTS.has(url.hostname.toLowerCase())) {
        return url.toString();
      }
    } catch {}
  }

  const conversationId = clean(
    agent?.conversation_id
    || metadata.conversation_id
    || metadata.chatgpt_conversation_id
  );
  if (/^[A-Za-z0-9_-]{8,160}$/.test(conversationId)) {
    return `https://chatgpt.com/c/${encodeURIComponent(conversationId)}`;
  }
  return null;
}

export function goToWorkPromptHint(value) {
  const text = clean(value).toLowerCase();
  if (!text) return false;
  return [
    "go to work",
    "switch to work",
    "continue in work",
    "handoff to work",
    "work handoff"
  ].some(pattern => text.includes(pattern));
}

export function goToWorkRecoveryCandidate(agent, {
  now = Date.now(),
  staleAfterMs = 45_000,
  cooldownMs = 60_000
} = {}) {
  if (!isChatGptProvider(agent)) return { eligible: false, reason: "not-chatgpt", url: null };

  const metadata = agent?.source_metadata && typeof agent.source_metadata === "object"
    ? agent.source_metadata
    : {};
  if (metadata.go_to_work_recovery_disabled === true) {
    return { eligible: false, reason: "agent-opted-out", url: null };
  }

  const url = chatgptConversationUrl(agent);
  if (!url) return { eligible: false, reason: "conversation-url-missing", url: null };

  const state = clean(agent?.state || agent?.status).toLowerCase();
  if (!ACTIVE_STATES.has(state)) return { eligible: false, reason: "inactive", url };

  const explicitPending = metadata.go_to_work_pending === true
    || metadata.work_handoff_pending === true
    || metadata.handoff_pending === true
    || goToWorkPromptHint(metadata.blocker)
    || goToWorkPromptHint(metadata.ui_state)
    || goToWorkPromptHint(agent?.last_action_summary);

  const lastCheckedMs = validIsoMs(
    metadata.go_to_work_recovery_last_checked_at
    || agent?.go_to_work_recovery_last_checked_at
  );
  const cooldown = Math.max(5_000, Number(cooldownMs) || 60_000);
  if (lastCheckedMs !== null && Number(now) - lastCheckedMs < cooldown) {
    return { eligible: false, reason: "cooldown", url };
  }

  if (explicitPending) return { eligible: true, reason: "handoff-signaled", url };
  if (["waiting", "blocked", "stale"].includes(state)) {
    return { eligible: true, reason: `state-${state}`, url };
  }

  const activityMs = validIsoMs(agent?.last_action_at)
    ?? validIsoMs(agent?.heartbeat_at)
    ?? validIsoMs(agent?.created_at);
  const staleThreshold = Math.max(10_000, Number(staleAfterMs) || 45_000);
  if (activityMs !== null && Number(now) - activityMs >= staleThreshold) {
    return { eligible: true, reason: "running-without-progress", url };
  }

  return { eligible: false, reason: "progress-recent", url };
}

export function planGoToWorkRecoveries(federation, {
  now = Date.now(),
  staleAfterMs = 45_000,
  cooldownMs = 60_000,
  maxPerSweep = 2
} = {}) {
  const agents = Array.isArray(federation?.agents) ? federation.agents : [];
  const limit = Math.max(1, Math.min(8, Math.floor(Number(maxPerSweep) || 2)));
  return agents
    .map(agent => ({ agent, decision: goToWorkRecoveryCandidate(agent, { now, staleAfterMs, cooldownMs }) }))
    .filter(item => item.decision.eligible)
    .sort((a, b) => {
      const aExplicit = a.decision.reason === "handoff-signaled" ? 0 : 1;
      const bExplicit = b.decision.reason === "handoff-signaled" ? 0 : 1;
      if (aExplicit !== bExplicit) return aExplicit - bExplicit;
      const aMs = validIsoMs(a.agent?.last_action_at) ?? 0;
      const bMs = validIsoMs(b.agent?.last_action_at) ?? 0;
      return aMs - bMs;
    })
    .slice(0, limit);
}
