'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { CodingJobs } = require('./coding-jobs');
const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'jarvis-coding-test-'));
const jobs = new CodingJobs({ onUpdate(job) {
  if (job.status === 'running') return;
  try {
    if (job.status !== 'completed') throw new Error(`Codex 실행 실패: ${job.error.slice(-600)}`);
    const result = fs.readFileSync(path.join(directory, 'hello.txt'), 'utf8').trim();
    if (result !== 'JARVIS_TEST') throw new Error('시험 파일 내용이 일치하지 않습니다.');
    console.log('PASS: 실제 Codex가 격리된 작업 폴더에 시험 파일을 만들었습니다.');
  } catch (err) { console.error(err.message); process.exitCode = 1; }
  finally {
    const resolved = fs.realpathSync(directory), parent = fs.realpathSync(os.tmpdir());
    if (path.dirname(resolved).toLowerCase() === parent.toLowerCase() && path.basename(resolved).startsWith('jarvis-coding-test-')) fs.rmSync(resolved, { recursive: true, force: true });
  }
}});
jobs.start({ workspace: directory, task: 'In the current directory create a file named hello.txt containing exactly JARVIS_TEST. Do not modify anything else.' });
