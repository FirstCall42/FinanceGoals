// Contract check (#12): the generated API client in src/app/api must match what the OpenAPI
// document (src/FinanceGoals.Api/openapi) generates. Regenerates into a temporary folder and
// compares file by file. Run by `npm test`, which CI runs; fix a failure with `npm run api:generate`.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join, relative, sep } from 'node:path';

const require = createRequire(import.meta.url);
const generator = require.resolve('ng-openapi-gen/lib/index.js');
const committed = 'src/app/api';

function files(root) {
  const found = [];
  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) walk(path);
      else found.push(relative(root, path).split(sep).join('/'));
    }
  };
  if (existsSync(root)) walk(root);
  return found.sort();
}

// Line endings can differ between a Windows and a Linux checkout.
const text = (path) => readFileSync(path, 'utf8').split('\r\n').join('\n');

const generated = mkdtempSync(join(tmpdir(), 'api-client-'));
try {
  execFileSync(process.execPath, [generator, '--output', generated], { stdio: 'pipe' });
  const expected = files(generated);
  const actual = files(committed);
  const problems = [
    ...expected.filter((f) => !actual.includes(f)).map((f) => `missing: ${f}`),
    ...actual.filter((f) => !expected.includes(f)).map((f) => `not generated any more: ${f}`),
    ...expected
      .filter((f) => actual.includes(f))
      .filter((f) => text(join(generated, f)) !== text(join(committed, f)))
      .map((f) => `differs: ${f}`),
  ];

  if (problems.length > 0) {
    console.error(`The generated API client in ${committed} is out of date:`);
    problems.forEach((p) => console.error(`  ${p}`));
    console.error('Run `npm run api:generate` in web/ and commit the result.');
    process.exitCode = 1;
  } else {
    console.log(`API client is up to date (${expected.length} files).`);
  }
} finally {
  rmSync(generated, { recursive: true, force: true });
}
