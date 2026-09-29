import test from "node:test";
import assert from "node:assert/strict";
import {
  PROMPT_LIBRARY_VERSION,
  REQUIRED_REPOSITORY_TRAINING_PATHS,
  renderAgentPrompt
} from "../lib/prompt-templates.mjs";

function render(role = "support") {
  const manifest = REQUIRED_REPOSITORY_TRAINING_PATHS.map(path => `${path} — sha256:deadbeef — 1 bytes`);
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
    repositoryTrainingManifest: manifest
  }).rendered;
}

test("training gate is rendered before the user task", () => {
  const prompt = render();
  const gateIndex = prompt.indexOf("MANDATORY REPOSITORY TRAINING GATE");
  const taskIndex = prompt.indexOf("USER / MANAGER TASK");
  assert.ok(gateIndex >= 0, "training gate must be present");
  assert.ok(taskIndex > gateIndex, "task must appear after training gate");
  assert.match(prompt, /Do not analyze, answer, summarize, plan, or act on the USER \/ MANAGER TASK until repository training is complete/);
  for (const requiredPath of REQUIRED_REPOSITORY_TRAINING_PATHS) assert.ok(prompt.includes(requiredPath), `missing ${requiredPath}`);
});

test("manager prompt requires manager-specific training", () => {
  const prompt = render("manager");
  assert.match(prompt, /01_MANAGER_ORCHESTRATOR\.txt/);
  assert.match(prompt, /before responding to or dispatching work/);
});

test("prompt library version records the training-gate revision", () => {
  assert.equal(PROMPT_LIBRARY_VERSION, "2026.09.29.3");
});
