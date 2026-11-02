// Local-only, dependency-free Node.js 20+ load probe for QueryForge v2.
// Use a synthetic CSV, not confidential data. Emits latency/status measurements,
// but does not measure server peak memory or prove production readiness.
import { readFile } from 'node:fs/promises';
import { performance } from 'node:perf_hooks';

const filePath = process.argv[2];
const concurrency = Number(process.argv[3] ?? 5);
const base = process.argv[4] ?? 'http://127.0.0.1:5080';
if (!filePath || !Number.isInteger(concurrency) || concurrency < 1 || concurrency > 20) {
  console.error('Usage: node scripts/benchmark-v2.mjs <synthetic.csv> [clients 1..20] [base-url]');
  process.exit(2);
}

const payload = await readFile(filePath);
if (payload.length > 25 * 1024 * 1024) {
  console.error('Synthetic sample must be at most 25 MiB.');
  process.exit(2);
}
const results = await Promise.all(
  Array.from({length:concurrency}, async (_, i) => {
    const body = new FormData();
    body.append('file', new Blob([payload], {type:'text/csv'}), 'loadprobe.csv');
    const start = performance.now();
    try {
      const response = await fetch(base + '/api/v2/datasets', {
        method:'POST',body,signal:AbortSignal.timeout(120_000)
      });
      const ms = Math.round(performance.now() - start);
      const text = await response.text();
      return {client:i+1,status:response.status,ms,error:response.ok ? null : text.slice(0,150)};
    } catch (e) {
      return {client:i+1,status:'network_error',ms:Math.round(performance.now()-start),error:String(e)};
    }
  })
);
const latencies = results.map(x=>x.ms).sort((a,b)=>a-b);
const p = q=>latencies[Math.min(latencies.length-1,Math.ceil(q*latencies.length)-1)];
const summary = {
  inputBytes:payload.length,
  clients:concurrency,
  success:results.filter(x=>x.status===201).length,
  busy:results.filter(x=>x.status===429).length,
  unexpected:results.filter(x=>![201,429].includes(x.status)).length,
  p50ms:p(.5),
  p95ms:p(.95),
  maxMs:latencies.at(-1)
};
console.log(JSON.stringify({summary,requests:results},null,2));
if (summary.unexpected > 0) process.exitCode = 1;
