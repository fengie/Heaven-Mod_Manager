const WORK_WORDS = ["work", "workspace"];
const CHAT_WORDS = ["chat", "chatting", "conversation"];

function clean(value) {
  return String(value ?? "").replace(/[\u21b5\u23ce]/g, " ").replace(/\s+/g, " ").trim();
}

export function normalizeHandoffLabel(value) {
  return clean(value)
    .toLowerCase()
    .replace(/\s+\d+$/g, "")
    .replace(/[^a-z0-9]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

export const DEFAULT_WORK_HANDOFF_SIGNATURES = Object.freeze({
  schema: "agent-control-work-handoff-signatures-v1",
  version: 1,
  declineLabels: ["stay in chat"],
  acceptLabels: ["continue in work", "continue in chatgpt work"],
  learned: [],
  driftObservations: []
});

function unique(values) {
  return [...new Set(values.map(normalizeHandoffLabel).filter(Boolean))];
}

export function normalizeWorkHandoffRegistry(value = {}) {
  const learned = Array.isArray(value?.learned) ? value.learned.filter(item => item && typeof item === "object").slice(-100) : [];
  const driftObservations = Array.isArray(value?.driftObservations) ? value.driftObservations.filter(item => item && typeof item === "object").slice(-100) : [];
  return {
    schema: DEFAULT_WORK_HANDOFF_SIGNATURES.schema,
    version: Math.max(1, Number(value?.version) || 1),
    updatedAt: value?.updatedAt || null,
    declineLabels: unique([
      ...DEFAULT_WORK_HANDOFF_SIGNATURES.declineLabels,
      ...(Array.isArray(value?.declineLabels) ? value.declineLabels : [])
    ]),
    acceptLabels: unique([
      ...DEFAULT_WORK_HANDOFF_SIGNATURES.acceptLabels,
      ...(Array.isArray(value?.acceptLabels) ? value.acceptLabels : [])
    ]),
    learned,
    driftObservations
  };
}

function isButton(item) {
  return String(item?.control_type || "").toLowerCase() === "button"
    && item?.enabled !== false
    && item?.offscreen !== true
    && normalizeHandoffLabel(item?.name);
}

function includesAny(label, phrases) {
  return phrases.some(phrase => label.includes(phrase));
}

function hasWork(label) {
  return includesAny(label, WORK_WORDS);
}

function hasChat(label) {
  return includesAny(label, CHAT_WORDS);
}

function sameActionRow(a, b) {
  const ar = a?.rect;
  const br = b?.rect;
  if (!ar || !br) return false;
  const ay = Number(ar.y) + Number(ar.height || 0) / 2;
  const by = Number(br.y) + Number(br.height || 0) / 2;
  const ax = Number(ar.x) + Number(ar.width || 0) / 2;
  const bx = Number(br.x) + Number(br.width || 0) / 2;
  if (![ay, by, ax, bx].every(Number.isFinite)) return false;
  return Math.abs(ay - by) <= 90 && Math.abs(ax - bx) <= 900;
}

export function detectWorkHandoffAction(tree, registryValue = {}) {
  const registry = normalizeWorkHandoffRegistry(registryValue);
  const items = Array.isArray(tree?.items) ? tree.items : [];
  const buttons = items.filter(isButton).map(item => ({
    ...item,
    normalizedName: normalizeHandoffLabel(item.name)
  }));
  const pageLabels = items.map(item => normalizeHandoffLabel(item?.name)).filter(Boolean);
  const workContext = pageLabels.filter(hasWork);
  if (!workContext.length) return { detected: false, reason: "work-context-missing" };

  const knownAccept = buttons.filter(item => includesAny(item.normalizedName, registry.acceptLabels));
  const knownDecline = buttons.filter(item => includesAny(item.normalizedName, registry.declineLabels));
  if (knownAccept.length >= 1 && knownDecline.length === 1) {
    return {
      detected: true,
      learned: false,
      reason: "known-signature",
      accept: knownAccept[0],
      decline: knownDecline[0]
    };
  }

  const semanticAccept = buttons.filter(item => hasWork(item.normalizedName));
  const pairs = [];
  for (const accept of semanticAccept) {
    for (const candidate of buttons) {
      if (candidate === accept || !sameActionRow(accept, candidate)) continue;
      const explicitChat = hasChat(candidate.normalizedName) && !hasWork(candidate.normalizedName);
      pairs.push({ accept, decline: candidate, explicitChat });
    }
  }

  const chatPairs = pairs.filter(pair => pair.explicitChat);
  const safePairs = chatPairs.length === 1
    ? chatPairs
    : (pairs.length === 1 ? pairs : []);

  if (safePairs.length !== 1) {
    return {
      detected: false,
      reason: "adaptive-signature-ambiguous",
      evidence: { workButtons: semanticAccept.length, candidatePairs: pairs.length, chatPairs: chatPairs.length }
    };
  }

  const pair = safePairs[0];
  return {
    detected: true,
    learned: true,
    reason: pair.explicitChat ? "adaptive-chat-pair" : "adaptive-two-action-pair",
    accept: pair.accept,
    decline: pair.decline
  };
}

export function learnWorkHandoffSignature(registryValue, detection, {
  detectedAt = new Date().toISOString(),
  verification = "card-cleared"
} = {}) {
  const registry = normalizeWorkHandoffRegistry(registryValue);
  if (!detection?.detected || !detection?.learned) return registry;
  const declineLabel = normalizeHandoffLabel(detection?.decline?.name);
  const acceptLabel = normalizeHandoffLabel(detection?.accept?.name);
  if (!declineLabel || !acceptLabel) return registry;

  const duplicate = registry.learned.some(item =>
    normalizeHandoffLabel(item?.declineLabel) === declineLabel
    && normalizeHandoffLabel(item?.acceptLabel) === acceptLabel
  );
  const learned = duplicate ? registry.learned : [
    ...registry.learned,
    { declineLabel, acceptLabel, detectedAt, source: detection.reason, verification }
  ].slice(-100);

  return {
    ...registry,
    version: duplicate ? registry.version : registry.version + 1,
    updatedAt: detectedAt,
    declineLabels: unique([...registry.declineLabels, declineLabel]),
    acceptLabels: unique([...registry.acceptLabels, acceptLabel]),
    learned
  };
}


export function recordWorkHandoffDrift(registryValue, detection, tree, {
  observedAt = new Date().toISOString()
} = {}) {
  const registry = normalizeWorkHandoffRegistry(registryValue);
  const items = Array.isArray(tree?.items) ? tree.items : [];
  const visibleButtons = items
    .filter(isButton)
    .map(item => normalizeHandoffLabel(item.name))
    .filter(Boolean)
    .slice(0, 40);
  const workLabels = items
    .map(item => normalizeHandoffLabel(item?.name))
    .filter(label => label && hasWork(label))
    .slice(0, 40);
  const observation = {
    observedAt,
    reason: detection?.reason || "unknown-ui-drift",
    evidence: detection?.evidence || null,
    visibleButtons,
    workLabels
  };
  const fingerprint = JSON.stringify({
    reason: observation.reason,
    visibleButtons,
    workLabels
  });
  const duplicate = registry.driftObservations.some(item => JSON.stringify({
    reason: item.reason,
    visibleButtons: item.visibleButtons,
    workLabels: item.workLabels
  }) === fingerprint);
  return {
    ...registry,
    version: duplicate ? registry.version : registry.version + 1,
    updatedAt: observedAt,
    driftObservations: duplicate
      ? registry.driftObservations
      : [...registry.driftObservations, observation].slice(-100)
  };
}
