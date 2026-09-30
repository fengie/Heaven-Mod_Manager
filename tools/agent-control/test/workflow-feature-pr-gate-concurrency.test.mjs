import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '../../..');
const workflowPath = path.join(repoRoot, '.github', 'workflows', 'workflow-feature-pr-gate.yml');

test('workflow feature PR gate collapses superseded work across PRs', () => {
  const workflow = fs.readFileSync(workflowPath, 'utf8');

  assert.match(
    workflow,
    /concurrency:\s*\r?\n(?:\s*#.*\r?\n)*\s*group:\s*workflow-feature-pr-gate\s*\r?\n\s*cancel-in-progress:\s*true/
  );
  assert.doesNotMatch(workflow, /workflow-feature-pr-gate-\$\{\{\s*github\.ref\s*\}\}/);
});
