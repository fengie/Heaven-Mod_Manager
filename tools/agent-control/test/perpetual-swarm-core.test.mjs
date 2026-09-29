import test from "node:test";
import assert from "node:assert/strict";
import {
  defaultPerpetualSwarmState,
  normalizePerpetualSwarmState,
  perpetualSwarmDecision,
  recordPerpetualCooldown,
  recordPerpetualWaveStart
} from "../lib/perpetual-swarm-core.mjs";

const NOW = Date.parse("2026-09-29T19:10:00.000Z");

function state(patch = {}) {
  return {
    health: { mode: "healthy" },
    settings: {
      dispatchPaused: false,
      readOnly: false,
      draining: false,
      emergencyStop: false,
      swarmTailRecovery: { enabled: true, armedAt: null },
      ...(patch.settings || {})
    },
    agents: patch.agents || [],
    perpetualSwarm: normalizePerpetualSwarmState({
      enabled: true,
      objective: "Keep improving the project",
      staleAfterMs: 5 * 60_000,
      blockedAfterMs: 10 * 60_000,
      minWaveGapMs: 1_000,
      restartWindowMs: 10 * 60_000,
      maxWaveStartsPerWindow: 3,
      restartCooldownMs: 60_000,
      maxCooldownMs: 10 * 60_000,
      ...(patch.perpetualSwarm || {})
    })
  };
}

test("perpetual swarm defaults disabled and restart-safe", () => {
  const value = defaultPerpetualSwarmState();
  assert.equal(value.enabled, false);
  assert.equal(value.workflowId, "usual-swarm");
  assert.ok(value.staleAfterMs >= 60_000);
  assert.ok(value.maxWaveStartsPerWindow > 0);
});

test("perpetual swarm launches a fresh wave when nothing is active", () => {
  const decision = perpetualSwarmDecision(state(), { now: NOW, providerCapacity: { blocked: false } });
  assert.equal(decision.kind, "launch-wave");
  assert.equal(decision.generation, 1);
});

test("perpetual swarm replaces a stale running worker before starting another wave", () => {
  const decision = perpetualSwarmDecision(state({
    agents: [{
      id: "stuck-1",
      status: "running",
      startedAt: "2026-09-29T18:00:00.000Z",
      lastProgressAt: "2026-09-29T18:30:00.000Z"
    }]
  }), { now: NOW, providerCapacity: { blocked: false } });
  assert.equal(decision.kind, "replace-stuck");
  assert.equal(decision.agentId, "stuck-1");
  assert.equal(decision.reason, "progress-timeout");
});

test("ordinary active workers suppress duplicate waves", () => {
  const decision = perpetualSwarmDecision(state({
    agents: [{
      id: "live-1",
      status: "running",
      startedAt: "2026-09-29T19:08:00.000Z",
      lastProgressAt: "2026-09-29T19:09:30.000Z"
    }]
  }), { now: NOW, providerCapacity: { blocked: false } });
  assert.equal(decision.kind, "wait");
  assert.equal(decision.reason, "active-workers");
});

test("tail recovery drains before a replacement swarm is launched", () => {
  const decision = perpetualSwarmDecision(state({
    settings: { swarmTailRecovery: { enabled: true, armedAt: "2026-09-29T19:00:00.000Z", waveId: "wave-1" } }
  }), { now: NOW, providerCapacity: { blocked: false } });
  assert.equal(decision.kind, "wait");
  assert.equal(decision.reason, "tail-recovery-active");
});

test("provider capacity holds the loop instead of respawning the same blocked lane", () => {
  const decision = perpetualSwarmDecision(state(), {
    now: NOW,
    providerCapacity: {
      blocked: true,
      blockedUntil: "2026-09-29T20:00:00.000Z",
      sourceAgentId: "quota-agent"
    }
  });
  assert.equal(decision.kind, "wait");
  assert.equal(decision.reason, "provider-capacity");
  assert.equal(decision.sourceAgentId, "quota-agent");
});

test("restart intensity backs off instead of storming", () => {
  const decision = perpetualSwarmDecision(state({
    perpetualSwarm: {
      waveStarts: [
        "2026-09-29T19:03:00.000Z",
        "2026-09-29T19:05:00.000Z",
        "2026-09-29T19:07:00.000Z"
      ],
      cooldownLevel: 1
    }
  }), { now: NOW, providerCapacity: { blocked: false } });
  assert.equal(decision.kind, "cooldown");
  assert.equal(decision.reason, "restart-intensity");
  assert.equal(decision.delayMs, 120_000);
});

test("safety controls suspend perpetual work without erasing intent", () => {
  const decision = perpetualSwarmDecision(state({
    settings: { emergencyStop: true }
  }), { now: NOW, providerCapacity: { blocked: false } });
  assert.equal(decision.kind, "wait");
  assert.equal(decision.reason, "emergency-stop");
});

test("wave and cooldown records persist provenance", () => {
  const first = recordPerpetualWaveStart({ enabled: true }, {
    at: "2026-09-29T19:00:00.000Z",
    waveId: "wave-a"
  });
  assert.equal(first.generation, 1);
  assert.equal(first.activeWaveId, "wave-a");
  assert.equal(first.waveStarts.length, 1);

  const cooldown = recordPerpetualCooldown(first, {
    at: "2026-09-29T19:01:00.000Z",
    nextActionAt: "2026-09-29T19:03:00.000Z"
  });
  assert.equal(cooldown.cooldownLevel, 1);
  assert.equal(cooldown.nextActionAt, "2026-09-29T19:03:00.000Z");
});
