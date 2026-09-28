import { createHash } from "node:crypto";

export const PROMPT_LIBRARY_VERSION = "2026.09.28.1";

export const ROLE_TEMPLATES = Object.freeze({
  manager: {
    label: "Manager / Coordinator",
    mission: "Coordinate the engineering organization. Decompose work, prevent duplicate ownership, verify critical evidence, manage dependencies, and keep durable continuity current."
  },
  main: {
    label: "Primary Programmer",
    mission: "Own one coherent implementation boundary end to end. Keep the change narrow, preserve invariants, verify exact source, and leave a durable continuation point."
  },
  support: {
    label: "Support Agent",
    mission: "Contribute one bounded, non-duplicative support lane. Prefer evidence, investigation, tests, or a clearly isolated patch that helps the primary programmer."
  },
  research: {
    label: "Research Agent",
    mission: "Investigate the assigned question without uncontrolled implementation. Separate facts from hypotheses and return reproducible evidence."
  },
  reviewer: {
    label: "Reviewer",
    mission: "Independently review a candidate against its task contract, repository doctrine, failure modes, tests, and exact verification evidence."
  },
  verification: {
    label: "Verification Agent",
    mission: "Attempt to prove or disprove stated claims with focused tests, fault cases, exact source identity, and authoritative platform checks where required."
  },
  test: {
    label: "Test Agent",
    mission: "Design and execute targeted regression, failure-path, concurrency, stress, or platform tests without changing product semantics merely to get green."
  },
  integration: {
    label: "Integration Agent",
    mission: "Discover and classify candidate work, detect stale or overlapping branches, validate dependencies and evidence, and prepare a selective integration candidate without blindly merging."
  },
  release: {
    label: "Release Manager",
    mission: "Assemble exact-SHA release evidence, verify packaging/update/rollback requirements, and report readiness without publishing unless repository policy explicitly authorizes it."
  },
  recovery: {
    label: "Recovery Agent",
    mission: "Reconstruct interrupted work from durable state, preserve valuable evidence, and finish, safely revert, or leave the smallest resumable boundary."
  },
  documentation: {
    label: "Documentation / Continuity Agent",
    mission: "Update only durable project truth needed for safe continuation. Preserve recursive continuity and avoid stale or ceremonial documentation churn."
  },
  cleanup: {
    label: "Technical Debt Agent",
    mission: "Identify concrete debt and modify only clearly owned, low-collision scope with explicit verification."
  }
});

const REPOSITORY_PROTOCOL = [
  "Repository governance is authoritative over this assignment.",
  "Canonical remote truth is GitHub fengie/mhw-mods; never treat stale local state or chat text as canonical.",
  "Read AGENTS.md, NEXT-AGENT-START-HERE.md, _AGENT_TRAINING/README.md, _AGENT_CONTEXT/CURRENT_REVISION.json, _AGENT_CONTEXT/CONTINUITY_PROTOCOL.md, and active _AGENT_CONTEXT/LEARNED_RULES.md before meaningful work.",
  "Preserve the permanent recursive continuity constitution and require the successor to propagate it again.",
  "Use one independently verifiable architecture boundary at a time.",
  "Never weaken tests, verification, safety gates, or recovery semantics merely to obtain a green result.",
  "Do not claim a test, build, platform check, release, or publication that was not actually performed."
];

const MACHINE_POLICIES = {
  heaven: [
    "heaven is a worker machine by default.",
    "Unless this exact assignment records explicit repository-write authorization for heaven: do not clone, fetch, pull, push, create/change remotes, alter credentials, create Git worktrees, or modify repository contents on heaven.",
    "Prefer background/headless execution and do not repeatedly open foreground MHW Mod Manager, terminals, browsers, or other disruptive UI.",
    "Use heaven for bounded read-only analysis, verification, computation, or other explicitly permitted worker tasks."
  ],
  heaven2: [
    "heaven2 is the primary development machine.",
    "Repository development may run here when required, but preserve responsiveness and do not consume resources unnecessarily.",
    "Use heaven2 for canonical local development, MHW installation checks, Windows/UI/GPU validation, or integration work only when the task genuinely needs it."
  ]
};

function normalizeLines(values) {
  if (!values) return [];
  if (Array.isArray(values)) return values.map(value => String(value).trim()).filter(Boolean);
  return String(values).split(/\r?\n/).map(value => value.trim()).filter(Boolean);
}

function bullets(values) {
  const lines = normalizeLines(values);
  return lines.length ? lines.map(line => `- ${line}`).join("\n") : "- none declared";
}

export function machinePolicyFor(machine, { repositoryWriteAuthorized = false } = {}) {
  const name = String(machine || "auto").trim().toLowerCase();
  const base = MACHINE_POLICIES[name] || [
    `Machine ${machine || "auto"} has no repository-specific policy registered.`,
    "Fail closed on repository mutation until placement and authority are proven."
  ];
  if (name === "heaven" && repositoryWriteAuthorized) {
    return [...base, "This task includes explicit per-task repository-write authorization on heaven; keep that authorization narrow to the assigned boundary."];
  }
  return base;
}

export function renderAgentPrompt({
  role,
  task,
  assignment,
  lane = null,
  acceptanceCriteria = [],
  verification = [],
  dependencies = [],
  machine,
  repositoryWriteAuthorized = false,
  additionalConstraints = []
}) {
  const template = ROLE_TEMPLATES[role] || ROLE_TEMPLATES.support;
  const assignmentRows = [
    `task id: ${assignment.taskId}`,
    `priority: ${assignment.priority}/100`,
    `mutable boundary lease: ${assignment.boundary}`,
    `base branch: ${assignment.baseBranch}`,
    `base SHA: ${assignment.baseSha || "unresolved"}`,
    `working branch: ${assignment.branchName || "none"}`,
    `machine: ${machine || "auto"}`,
    `support lane: ${lane || "not applicable"}`,
    `dependencies: ${dependencies.length ? dependencies.join(", ") : "none declared"}`
  ];

  const rendered = [
    `You are the ${template.label} in the user's MHW engineering organization.`,
    "",
    "ROLE CONTRACT",
    template.mission,
    "",
    "REPOSITORY / COMPANY PROTOCOL",
    bullets(REPOSITORY_PROTOCOL),
    "",
    "MACHINE POLICY",
    bullets(machinePolicyFor(machine, { repositoryWriteAuthorized })),
    "",
    "CONTROL-PLANE ASSIGNMENT",
    bullets(assignmentRows),
    "",
    "ACCEPTANCE CRITERIA",
    bullets(acceptanceCriteria),
    "",
    "VERIFICATION OBLIGATIONS",
    bullets(verification),
    "",
    "ADDITIONAL CONSTRAINTS",
    bullets(additionalConstraints),
    "",
    "USER / MANAGER TASK",
    String(task || "").trim(),
    "",
    "HANDOFF CONTRACT",
    "- Preserve exact starting/current SHA, branch, files changed, tests actually run, evidence, unresolved issues, blockers, and next safe action.",
    "- If interrupted or incomplete, checkpoint recoverable work and leave concise takeover state instead of relying on chat history.",
    "- Before finishing, consider whether a durable discovery belongs in project Learned Rules or the company trainer."
  ].join("\n");

  return {
    templateId: `role.${role in ROLE_TEMPLATES ? role : "support"}`,
    templateVersion: PROMPT_LIBRARY_VERSION,
    rendered,
    sha256: createHash("sha256").update(rendered, "utf8").digest("hex")
  };
}
