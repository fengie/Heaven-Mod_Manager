import test from "node:test";
import assert from "node:assert/strict";
import {
  PROMPT_LIBRARY_VERSION,
  REQUIRED_REPOSITORY_TRAINING_PATHS,
  REPOSITORY_CONTEXT_INDEX_PATHS,
  renderAgentPrompt
} from "../lib/prompt-templates.mjs";

function render(role = "support") {
  const manifest = REQUIRED_REPOSITORY_TRAINING_PATHS.map(path => `${path} — sha256:deadbeef — 1 bytes`);
  const contextManifest = REPOSITORY_CONTEXT_INDEX_PATHS.map(path => `${path} — sha256:feedface — 1 bytes`);
  if (role === "manager") {
    manifest.push("_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt — sha256:deadbeef — 1 bytes");
  }
  return renderAgentPrompt({
    role,
    task: "Implement the assigned change.",
    assignment: {
      taskId: "task-test",
      priority: 50,
      boundary: "test-boundary",
      baseBranch: "main",
      baseSha: "abc123",
      branchName: "agent/test"
    },
    machine: "heaven",
    repositoryWriteAuthorized: true,
    repositoryTrainingManifest: manifest,
    repositoryContextManifest: contextManifest
  }).rendered;
}

test("compact training gate is rendered before the user task", () => {
  const prompt = render();
  const gateIndex = prompt.indexOf("MANDATORY REPOSITORY TRAINING GATE");
  const taskIndex = prompt.indexOf("USER / MANAGER TASK");
  assert.ok(gateIndex >= 0, "training gate must be present");
  assert.ok(taskIndex > gateIndex, "task must appear after training gate");
  assert.match(prompt, /compact repository bootstrap is complete/);
  for (const requiredPath of REQUIRED_REPOSITORY_TRAINING_PATHS) assert.ok(prompt.includes(requiredPath), `missing core ${requiredPath}`);
  for (const indexedPath of REPOSITORY_CONTEXT_INDEX_PATHS) assert.ok(prompt.includes(indexedPath), `missing indexed ${indexedPath}`);
});

test("large continuity ledgers are indexed instead of mandatory full rereads", () => {
  for (const path of ["NEXT-AGENT-START-HERE.md", "_AGENT_CONTEXT/CURRENT_STATE.md", "_AGENT_CONTEXT/NEXT_STEPS.md", "_AGENT_CONTEXT/VERIFICATION.md"]) {
    assert.ok(!REQUIRED_REPOSITORY_TRAINING_PATHS.includes(path), `${path} must not be in the full-read core`);
    assert.ok(REPOSITORY_CONTEXT_INDEX_PATHS.includes(path), `${path} must remain hash-indexed`);
  }
  const prompt = render();
  assert.match(prompt, /HASH-VERIFIED; READ TASK-RELEVANT SECTIONS ONLY/);
});

test("startup recovery treats truncation and missing preferred tools as routing conditions", () => {
  const prompt = render();
  assert.match(prompt, /Output truncation is recoverable: paginate\/chunk/);
  assert.match(prompt, /missing gh\/CLI/);
  assert.match(prompt, /GitHub connector\/API/);
  assert.match(prompt, /TRAINING-BLOCKED or EXECUTION-BLOCKED only after reasonable authorized fallback routes are exhausted/);
  assert.match(prompt, /Truncation, missing gh, or no local checkout alone is never sufficient/);
});

test("bug precedents remain indexed and prevention closure is rendered", () => {
  assert.ok(REPOSITORY_CONTEXT_INDEX_PATHS.includes("_AGENT_CONTEXT/BUG_PRECEDENTS.md"));
  const prompt = render();
  assert.match(prompt, /applicable BUG_PRECEDENTS\/LEARNED_RULES entries/);
  assert.match(prompt, /BUG PREVENTION CLOSURE:/);
  assert.match(prompt, /root cause and violated invariant/);
  assert.match(prompt, /regression coverage or strongest durable verifier/);
  assert.match(prompt, /before DONE\/FIXED\/merge\/release/);
});

test("manager prompt requires manager-specific core training", () => {
  const prompt = render("manager");
  assert.match(prompt, /01_MANAGER_ORCHESTRATOR\.txt/);
  assert.match(prompt, /before responding to or dispatching work/);
});

test("prompt library version records bounded context navigation", () => {
  assert.equal(PROMPT_LIBRARY_VERSION, "2026.09.30.3");
});
