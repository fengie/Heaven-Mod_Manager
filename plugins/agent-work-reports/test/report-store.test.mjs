import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { appendReport, normalizeReport, readReports } from "../lib/report-store.mjs";

test("normalizes bounded report fields", () => {
  const report = normalizeReport({ agentId:"agent-1", taskId:"task-1", phase:"progress", summary:"Working", progress:42.4 }, { now: Date.parse("2026-09-30T22:00:00Z") });
  assert.equal(report.agentId, "agent-1");
  assert.equal(report.taskId, "task-1");
  assert.equal(report.progress, 42);
  assert.equal(report.at, "2026-09-30T22:00:00.000Z");
});

test("rejects invalid phase and progress", () => {
  assert.throws(() => normalizeReport({agentId:"a",phase:"wat",summary:"x"}), /phase/i);
  assert.throws(() => normalizeReport({agentId:"a",phase:"progress",summary:"x",progress:101}), /progress/i);
});

test("appends and reads jsonl while ignoring a corrupt line", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "awr-"));
  const file = path.join(dir, "reports.jsonl");
  appendReport(file, {agentId:"a",taskId:"t",phase:"plan",summary:"Plan"}, {now:1});
  fs.appendFileSync(file, "not-json\n");
  appendReport(file, {agentId:"a",taskId:"t",phase:"done",summary:"Done",progress:100}, {now:2});
  const reports = readReports(file);
  assert.equal(reports.length, 2);
  assert.equal(reports[1].phase, "done");
});
