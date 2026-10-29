'use strict';

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

class CodingJobs {
  constructor(options = {}) {
    this.executable = options.executable || process.env.JARVIS_CODEX_PATH || 'codex';
    this.spawn = options.spawn || spawn;
    this.jobs = new Map();
    this.onUpdate = options.onUpdate || (() => {});
  }

  start({ task, workspace }) {
    const text = String(task || '').trim();
    if (!text || text.length > 4000) throw new Error('코딩 요청은 1~4000자로 입력해 주세요.');
    if (!path.isAbsolute(workspace) || !fs.statSync(workspace).isDirectory()) throw new Error('코딩 작업 폴더가 올바르지 않습니다.');
    if ([...this.jobs.values()].some(j => j.status === 'running')) throw new Error('이미 실행 중인 코딩 작업이 있습니다.');
    const id = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    const job = { id, status: 'running', workspace, task: text, startedAt: new Date().toISOString(), output: '', error: '' };
    this.jobs.set(id, job);
    const args = ['exec', '--sandbox', 'workspace-write', '--skip-git-repo-check', '--ephemeral', '--cd', workspace, '-'];
    let child;
    try { child = this.spawn(this.executable, args, { cwd: workspace, windowsHide: true, shell: false, stdio: ['pipe', 'pipe', 'pipe'] }); }
    catch (err) { job.status = 'failed'; job.error = err.message; this.onUpdate(job); return job; }
    const limit = 32000;
    child.stdout.on('data', data => { job.output = (job.output + data.toString('utf8')).slice(-limit); });
    child.stderr.on('data', data => { job.error = (job.error + data.toString('utf8')).slice(-limit); });
    child.on('error', err => { if (job.status === 'running') { job.status = 'failed'; job.error = err.message; job.finishedAt = new Date().toISOString(); this.onUpdate(job); } });
    child.on('close', code => {
      if (job.status !== 'running') return;
      job.status = code === 0 ? 'completed' : 'failed';
      job.exitCode = code;
      job.finishedAt = new Date().toISOString();
      this.onUpdate(job);
    });
    child.stdin.on('error', () => {});
    child.stdin.end(text, 'utf8');
    this.onUpdate(job);
    return job;
  }

  latest() { return [...this.jobs.values()].at(-1) || null; }
}

module.exports = { CodingJobs };
