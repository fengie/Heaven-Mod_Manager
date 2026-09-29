import test from "node:test";
import assert from "node:assert/strict";

import {
  DEFAULT_WORK_HANDOFF_SIGNATURES,
  detectWorkHandoffAction,
  learnWorkHandoffSignature,
  normalizeHandoffLabel,
  normalizeWorkHandoffRegistry
} from "../lib/work-handoff-signatures.mjs";

function button(name, x, y) {
  return { name, control_type: "Button", enabled: true, offscreen: false, rect: { x, y, width: 150, height: 40 } };
}

test("normalizes counters and keyboard glyphs from handoff labels", () => {
  assert.equal(normalizeHandoffLabel("Stay in Chat 29"), "stay in chat");
  assert.equal(normalizeHandoffLabel("Continue in Work ↵"), "continue in work");
});

test("detects the current card using built-in signatures", () => {
  const tree = { items: [
    { name: "Continue in ChatGPT Work", control_type: "Text" },
    button("Stay in Chat 29", 600, 100),
    button("Continue in Work ↵", 790, 100)
  ] };
  const result = detectWorkHandoffAction(tree, DEFAULT_WORK_HANDOFF_SIGNATURES);
  assert.equal(result.detected, true);
  assert.equal(result.learned, false);
  assert.equal(result.decline.name, "Stay in Chat 29");
});

test("adapts to renamed action labels when the Work card still has a unique safe pair", () => {
  const tree = { items: [
    { name: "Move this task to ChatGPT Work", control_type: "Text" },
    button("Keep chatting here", 600, 100),
    button("Open in Work", 790, 100)
  ] };
  const result = detectWorkHandoffAction(tree, DEFAULT_WORK_HANDOFF_SIGNATURES);
  assert.equal(result.detected, true);
  assert.equal(result.learned, true);
  assert.equal(result.reason, "adaptive-chat-pair");
  assert.equal(result.decline.name, "Keep chatting here");
});

test("falls back to a unique two-action row when the decline wording has no chat keyword", () => {
  const tree = { items: [
    { name: "Continue this task in Work", control_type: "Text" },
    button("Not now", 600, 100),
    button("Move to Work", 790, 100)
  ] };
  const result = detectWorkHandoffAction(tree, DEFAULT_WORK_HANDOFF_SIGNATURES);
  assert.equal(result.detected, true);
  assert.equal(result.learned, true);
  assert.equal(result.decline.name, "Not now");
});

test("fails closed when a renamed card has multiple plausible decline buttons", () => {
  const tree = { items: [
    { name: "Continue this task in Work", control_type: "Text" },
    button("Not now", 550, 100),
    button("Later", 650, 100),
    button("Move to Work", 790, 100)
  ] };
  const result = detectWorkHandoffAction(tree, DEFAULT_WORK_HANDOFF_SIGNATURES);
  assert.equal(result.detected, false);
  assert.equal(result.reason, "adaptive-signature-ambiguous");
});

test("verified adaptive detection is learned into the mutable registry", () => {
  const base = normalizeWorkHandoffRegistry({});
  const detection = {
    detected: true,
    learned: true,
    reason: "adaptive-chat-pair",
    decline: { name: "Keep chatting here" },
    accept: { name: "Open in Work" }
  };
  const learned = learnWorkHandoffSignature(base, detection, { detectedAt: "2026-09-29T18:00:00Z" });
  assert.equal(learned.version, base.version + 1);
  assert.ok(learned.declineLabels.includes("keep chatting here"));
  assert.ok(learned.acceptLabels.includes("open in work"));
  assert.equal(learned.learned.length, 1);
});
