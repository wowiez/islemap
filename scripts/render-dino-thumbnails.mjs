import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import fs from 'node:fs/promises';
import path from 'node:path';

const require = createRequire(import.meta.url);
const moduleRoot = process.env.ISLE_RENDER_NODE_MODULES;
if (!moduleRoot) throw new Error('ISLE_RENDER_NODE_MODULES is required.');
const { chromium } = require(path.join(moduleRoot, 'playwright-core'));

const models = {
  Allosaurus: ['/cdn/skinviewer/Allosaurus/Allosaurus.glb', 0.0261, [0, -2.4, 0]],
  Austroraptor: ['/cdn/skinviewer/Austro/Austroraptor.glb', 0.047, [0, -2.4, 0]],
  Beipiaosaurus: ['/cdn/skinviewer/Beipi/Beipiaosaurus.glb', 0.06, [0, -2.4, 0]],
  Carnotaurus: ['/cdn/skinviewer/Carno/Carnotaurus.glb', 0.0241, [0, -2.4, 0]],
  Ceratosaurus: ['/cdn/skinviewer/Cerato/Ceratosaurus.glb', 0.0315, [0, -2.4, 0]],
  Deinosuchus: ['/cdn/skinviewer/Deino/Deinosuchus.glb', 0.0229, [0, -0.55, 1.5]],
  Diabloceratops: ['/cdn/skinviewer/Dibble/Diabloceratops.glb', 0.03, [0, -2.4, 0]],
  Dilophosaurus: ['/cdn/skinviewer/Dilo/Dilophosaurus.glb', 0.027, [0, -2.4, 0]],
  Dryosaurus: ['/cdn/skinviewer/Dryo/Dryosaurus.glb', 0.047, [0, -2.4, 0]],
  Gallimimus: ['/cdn/skinviewer/Galli/Gallimimus.glb', 0.03, [0, -2.45, 0.4]],
  Herrerasaurus: ['/cdn/skinviewer/Herrera/Herrerasaurus.glb', 0.0574, [0, -2.4, 0]],
  Hypsilophodon: ['/cdn/skinviewer/Hypsi/Hypsilophodon.glb', 0.085, [0, -2.4, 0]],
  Kentrosaurus: ['/cdn/skinviewer/Kentro/Kentrosaurus.glb', 0.039, [0, -2.4, 0]],
  Maiasaura: ['/cdn/skinviewer/Maiasaura/Maiasaura.glb', 0.023, [-2.2, -2.25, 0.85]],
  Omniraptor: ['/cdn/skinviewer/Omni/Omniraptor.glb', 0.047, [0, -2.4, 0]],
  Pachycephalosaurus: ['/cdn/skinviewer/Pachy/Pachycephalosaurus.glb', 0.037, [0, -2.4, 0]],
  Pteranodon: ['/cdn/skinviewer/Pter/Pteranodon.glb', 0.051, [0, -2.15, 0]],
  Stegosaurus: ['/cdn/skinviewer/Stego/Stegosaurus.glb', 0.0252, [0, -2.4, 0]],
  Tenontosaurus: ['/cdn/skinviewer/Teno/Tenontosaurus.glb', 0.0248, [0, -2.4, 0]],
  Triceratops: ['/cdn/skinviewer/Triceratops/Triceratops.glb', 0.019, [0, -2.4, 0]],
  Troodon: ['/cdn/skinviewer/Troodon/Troodon.glb', 0.065, [0, -2.4, 0]],
  Tyrannosaurus: ['/cdn/skinviewer/Tyrannosaurus/Tyrannosaurus.glb', 0.0195, [0, -2.55, 0]]
};

const root = path.resolve(import.meta.dirname, '..');
const rendererUrl = pathToFileURL(path.join(root, 'scripts', 'dino-thumbnail-renderer.html'));
const outputDirectory = path.join(root, 'src', 'TheIsleOverlay.App', 'Assets', 'DinoThumbnails');
await fs.mkdir(outputDirectory, { recursive: true });

const browser = await chromium.launch({
  executablePath: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  headless: true,
  args: ['--disable-web-security', '--allow-file-access-from-files']
});

try {
  const page = await browser.newPage({ viewport: { width: 640, height: 240 }, deviceScaleFactor: 1 });
  page.on('console', message => process.stderr.write(`browser: ${message.text()}\n`));
  page.on('pageerror', error => process.stderr.write(`browser error: ${error.message}\n`));
  page.on('requestfailed', request => process.stderr.write(
    `request failed: ${request.url()} (${request.failure()?.errorText ?? 'unknown'})\n`));
  for (const [species, [modelPath, scale, position]] of Object.entries(models)) {
    if (process.env.SPECIES_FILTER && species !== process.env.SPECIES_FILTER) continue;
    const url = new URL(rendererUrl);
    url.searchParams.set('model', `https://islepilot.eu${modelPath}?v=12`);
    url.searchParams.set('scale', String(scale));
    url.searchParams.set('position', position.join(','));
    url.searchParams.set('color', '#a4a7ab');
    await page.goto(url.href, { waitUntil: 'load', timeout: 30000 });
    await page.waitForFunction(() => document.title === 'READY' || document.title === 'FAILED', undefined, { timeout: 60000 });
    if (await page.title() !== 'READY') throw new Error(`Renderer failed for ${species}.`);
    const slug = species.replace(/[^a-z0-9]/gi, '').toLowerCase();
    await page.screenshot({
      path: path.join(outputDirectory, `${slug}.png`),
      omitBackground: true
    });
    process.stdout.write(`${species}\n`);
  }
} finally {
  await browser.close();
}
