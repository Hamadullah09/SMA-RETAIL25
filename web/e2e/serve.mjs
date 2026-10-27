// Serves the static export (`out/`) the way RetailPlus.Server does: `index.html` for a folder, a redirect to add the
// trailing slash, `404.html` for unknown paths, and nothing at all under /api (the end-to-end tests answer those).
//
//   node e2e/serve.mjs [port] [folder]
import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const port = Number(process.argv[2] ?? process.env.PORT ?? 4173);
const root = resolve(process.argv[3] ?? join(fileURLToPath(new URL('.', import.meta.url)), '..', 'out'));

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff2': 'font/woff2',
};

if (!existsSync(join(root, 'index.html'))) {
  console.error(`No static export in ${root}. Run "npm run build" first.`);
  process.exit(1);
}

function send(res, status, file) {
  res.writeHead(status, {
    'Content-Type': TYPES[extname(file)] ?? 'application/octet-stream',
    'Cache-Control': file.includes(`${'_next'}`) ? 'public, max-age=31536000, immutable' : 'no-cache',
  });
  createReadStream(file).pipe(res);
}

createServer((req, res) => {
  const url = new URL(req.url ?? '/', 'http://localhost');
  let path;
  try {
    path = decodeURIComponent(url.pathname);
  } catch {
    res.writeHead(400).end();
    return;
  }
  if (path.startsWith('/api/')) {
    res.writeHead(502, { 'Content-Type': 'application/json' }).end(JSON.stringify({ error: 'No API here: the tests answer /api.' }));
    return;
  }
  const file = normalize(join(root, path));
  if (!file.startsWith(root)) {
    res.writeHead(403).end();
    return;
  }
  if (existsSync(file) && statSync(file).isDirectory()) {
    if (!path.endsWith('/')) {
      res.writeHead(308, { Location: path + '/' + url.search }).end();
      return;
    }
    const index = join(file, 'index.html');
    if (existsSync(index)) return send(res, 200, index);
  } else if (existsSync(file)) {
    return send(res, 200, file);
  } else if (existsSync(file + '.html')) {
    return send(res, 200, file + '.html');
  }
  send(res, 404, join(root, '404.html'));
}).listen(port, '127.0.0.1', () => console.log(`Serving ${root} on http://127.0.0.1:${port}`));
