#!/usr/bin/env node
// Enforces AGENTS.md section 15 where Claude Code can check it: before a worker starts.
// Fails open: if this script cannot run or cannot read its input, the tool call proceeds.
import { closeSync, mkdirSync, openSync, readFileSync, readSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const maxWorkersPerSession = 8;
const maxConversationTokens = 150_000;
const topTier = /opus|fable|mythos/i;
const topTierAgents = new Set(['ux-designer']);
// Agents whose model is pinned by their own definition, or that are light by design.
const pinnedAgents = new Set(['builder', 'clerk', 'ux-designer', 'claude-code-guide', 'statusline-setup', 'web-fetch']);

const deny = (reason) => {
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason: `${reason} See AGENTS.md section 15. The owner can lift this for one session with DROVER_SPEND_OVERRIDE=1.`,
    },
  }));
  process.exit(0);
};

// The last request's size is the conversation's size: fresh input plus what was read from and written to the cache.
const conversationTokens = (transcriptPath) => {
  try {
    const size = statSync(transcriptPath).size;
    const length = Math.min(size, 512 * 1024);
    const buffer = Buffer.alloc(length);
    const file = openSync(transcriptPath, 'r');
    readSync(file, buffer, 0, length, size - length);
    closeSync(file);
    const lines = buffer.toString('utf8').split('\n').reverse();
    for (const line of lines) {
      if (!line.includes('"usage"') || line.includes('"isSidechain":true')) {
        continue;
      }
      try {
        const usage = JSON.parse(line)?.message?.usage;
        if (usage) {
          return (usage.input_tokens ?? 0) + (usage.cache_read_input_tokens ?? 0) + (usage.cache_creation_input_tokens ?? 0);
        }
      } catch {
        // A line cut by the read window; keep looking.
      }
    }
  } catch {
    // No transcript to read.
  }
  return 0;
};

const countWorker = (sessionId) => {
  const directory = join(tmpdir(), 'drover-spend-guard');
  const path = join(directory, `${String(sessionId).replace(/[^\w-]/g, '_')}.count`);
  let count = 0;
  try {
    count = Number.parseInt(readFileSync(path, 'utf8'), 10) || 0;
  } catch {
    // First worker of the session.
  }
  if (count >= maxWorkersPerSession) {
    return false;
  }
  mkdirSync(directory, { recursive: true });
  writeFileSync(path, String(count + 1));
  return true;
};

let input;
try {
  input = JSON.parse(readFileSync(0, 'utf8'));
} catch {
  process.exit(0);
}
if (process.env.DROVER_SPEND_OVERRIDE === '1') {
  process.exit(0);
}

const tool = input.tool_name;
if (tool === 'Workflow') {
  deny('Workflows start many agents at once and are off for this repository.');
}
if (tool !== 'Agent' && tool !== 'Task') {
  process.exit(0);
}

const agent = input.tool_input?.subagent_type ?? 'general-purpose';
const model = input.tool_input?.model;

if (agent === 'fork') {
  deny('A fork copies this whole conversation into a second agent. Start a "builder" or "clerk" with a short brief instead.');
}
if (model && topTier.test(model) && !topTierAgents.has(agent)) {
  deny(`The top tier is only for the "ux-designer" agent. Start "${agent}" with model "sonnet", or "haiku" for mechanical work.`);
}
if (!model && !pinnedAgents.has(agent)) {
  deny(`"${agent}" would inherit this session's model. Use the "builder" agent (standard tier) or the "clerk" agent (light tier), or pass model "sonnet" or "haiku".`);
}

const tokens = conversationTokens(input.transcript_path);
if (tokens > maxConversationTokens) {
  deny(`This conversation is ${tokens.toLocaleString('en-US')} tokens, and every worker report makes it re-read. Write the handoff on the issue and end the session; a fresh session starts the next worker.`);
}
if (!countWorker(input.session_id)) {
  deny(`This session has already started ${maxWorkersPerSession} workers. Write the handoff on the issue and end the session.`);
}
process.exit(0);
