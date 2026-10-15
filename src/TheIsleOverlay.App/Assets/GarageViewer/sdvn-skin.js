// Skin preview for IslePilot web studios (SDVN #3). Their dino textures are region masks: every
// pixel is close to one of six reference colours, one per palette slot. The skin is painted on the
// CPU by swapping each region for its palette colour, then teeth/mouth/claws (TMC mask), ambient
// occlusion (RAC) and glitch effects are layered on top. Written to match the web studio's output.
import * as THREE from 'three';

// Pattern regions in the order the masks use them.
const REGIONS = [
  { slot: 'display', ref: [255, 0, 0] },
  { slot: 'underbelly', ref: [0, 255, 0] },
  { slot: 'flank', ref: [0, 1, 245] },
  { slot: 'body', ref: [0, 255, 241] },
  { slot: 'markings', ref: [255, 0, 255], threshold: 0.6 },
  { slot: 'detail', ref: [255, 255, 0] },
];
// Glitch-lab layer keys -> palette slot.
const LAYER_SLOTS = { md: 'display', f: 'flank', m: 'markings', d1: 'detail', b: 'body', u: 'underbelly', e: 'eyes' };
const HUGE = 0x1000000;
// Palette colours are darkened before lighting, as the studio does.
const PALETTE_GAIN = 0.55;
const BLEED_REACH = 0.5;

const hexRgb = hex => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16));
const rgbHex = rgb => '#' + rgb.map(v => Math.min(255, Math.max(0, Math.round(v))).toString(16).padStart(2, '0')).join('');

export function pixels(image, width, height, smooth = false) {
  const canvas = document.createElement('canvas');
  canvas.width = width; canvas.height = height;
  const context = canvas.getContext('2d');
  context.imageSmoothingEnabled = smooth;
  if (smooth) context.imageSmoothingQuality = 'high';
  context.drawImage(image, 0, 0, width, height);
  return context.getImageData(0, 0, width, height).data;
}

// Out-of-range XYZ values are how glitch skins encode effects. A single huge negative component
// "bleeds": it erodes the region edge and tints it with the other two components.
function bleed([x, y, z]) {
  const values = [x, y, z], sizes = values.map(Math.abs), largest = Math.max(...sizes);
  if (largest < HUGE || values[sizes.indexOf(largest)] >= 0 || sizes.every(v => v >= HUGE)) return null;
  return {
    color: values.map((v, i) => sizes[i] < HUGE ? Math.max(0, v) : 0),
    erosion: Math.max(0, 27 - Math.log2(largest)),
    keep: values.map((v, i) => v < 0 && sizes[i] >= HUGE ? 0 : 1),
  };
}

// One dominant component (10x the others) paints a thin outline around the region.
function outline([x, y, z]) {
  const sizes = [x, y, z].map(Math.abs), sorted = [...sizes].sort((a, b) => b - a);
  if (sorted[0] <= 1 || sorted[0] / Math.max(sorted[1], 1) < 10) return null;
  const negative = [x, y, z].map((v, i) => v < 0 ? sizes[i] : 0), scale = Math.max(...negative);
  if (scale === 0 || scale > 99999) return null;
  return negative.map(v => 1 - v / scale);
}

/** Glitch layers with values outside 0..1, keyed by palette slot. */
export function glitchLayers(glitchLab, glitched) {
  const layers = {};
  if (!glitched || !glitchLab?.layers) return layers;
  for (const [key, layer] of Object.entries(glitchLab.layers)) {
    const slot = LAYER_SLOTS[key];
    const xyz = [Number(layer?.x), Number(layer?.y), Number(layer?.z)];
    if (slot && xyz.every(Number.isFinite) && xyz.some(v => v < 0 || v > 1)) layers[slot] = xyz;
  }
  return layers;
}

/** Palette with glitch slots replaced by the colour the effect shows. */
export function glitchPalette(palette, layers) {
  const result = { ...palette };
  for (const [slot, xyz] of Object.entries(layers)) {
    const info = bleed(xyz);
    const top = Math.max(...xyz, 1);
    result[slot] = rgbHex(info ? info.color.map(v => v * 255) : xyz.map(v => v / top * 255));
  }
  return result;
}

/**
 * Paints the skin texture. `source` holds pixel arrays at the pattern size: base, rac, noise,
 * mask, tmc (null when missing) and juvenile {data,width,height}.
 */
export function paintSkin(source, palette, layers = {}, overlayDefaults = null, noiseAmount = 0) {
  const { width, height } = source;
  const out = new Uint8ClampedArray(source.base);
  const bleeds = new Map(), outlines = new Map();
  for (const [slot, xyz] of Object.entries(layers)) {
    const region = REGIONS.findIndex(r => r.slot === slot);
    const info = bleed(xyz);
    if (info && region >= 0) bleeds.set(region, info);
    const edge = outline(xyz);
    if (edge && region >= 0) outlines.set(region, edge.map(v => Math.round(v * 255)));
  }

  const refs = REGIONS.map(r => r.ref.map(v => v / 255));
  const colours = REGIONS.map(r => hexRgb(palette[r.slot]).map(v => Math.round(v * PALETTE_GAIN)));
  const thresholds = REGIONS.map(r => r.threshold ?? 0.42);
  const nearestRegion = (r, g, b, limit) => {
    let best = Infinity, index = -1;
    for (let k = 0; k < refs.length; k++) {
      const d = (r - refs[k][0]) ** 2 + (g - refs[k][1]) ** 2 + (b - refs[k][2]) ** 2;
      if (d < best) { best = d; index = k; }
    }
    return best <= (limit ?? thresholds[index]) ? index : -1;
  };

  const edgeOwner = outlines.size ? new Int8Array(width * height).fill(-1) : null;
  for (let p = 0; p < width * height; p++) {
    const i = p * 4, r = out[i] / 255, g = out[i + 1] / 255, b = out[i + 2] / 255;
    const peak = Math.max(r, g, b), lift = peak > 0.04 ? 1 / peak : 1;
    const region = nearestRegion(r * lift, g * lift, b * lift);
    if (region < 0) continue;
    [out[i], out[i + 1], out[i + 2], out[i + 3]] = [...colours[region], 255];
    if (edgeOwner && outlines.has(region)) edgeOwner[p] = region;
  }

  if (edgeOwner) {
    const reach = Math.max(0, Math.round(BLEED_REACH));
    const steps = [[-1, 0], [1, 0], [0, -1], [0, 1], [-1, -1], [1, -1], [-1, 1], [1, 1]];
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
      const owner = edgeOwner[y * width + x];
      if (owner < 0) continue;
      const colour = outlines.get(owner);
      for (const [dx, dy] of steps) for (let s = 1; s <= reach; s++) {
        const nx = x + dx * s, ny = y + dy * s;
        if (nx < 0 || nx >= width || ny < 0 || ny >= height || edgeOwner[ny * width + nx] >= 0) continue;
        const j = (ny * width + nx) * 4, shade = Math.max(out[j], out[j + 1], out[j + 2]) / 255;
        out[j] = Math.round(colour[0] * shade); out[j + 1] = Math.round(colour[1] * shade); out[j + 2] = Math.round(colour[2] * shade);
      }
    }
  }

  // Bleed layers darken the region channels they knock out, read from the juvenile mask.
  const juvenile = source.juvenile;
  if (juvenile && bleeds.size) {
    const regionAt = (x, y) => {
      const j = (y * juvenile.width + x) * 4, d = juvenile.data;
      const peak = Math.max(d[j], d[j + 1], d[j + 2]), lift = peak > 10 ? 255 / peak : 1;
      return nearestRegion(d[j] * lift / 255, d[j + 1] * lift / 255, d[j + 2] * lift / 255, 0.42);
    };
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
      const jx = Math.floor(x * juvenile.width / width), jy = Math.floor(y * juvenile.height / height);
      const region = regionAt(jx, jy), info = bleeds.get(region);
      if (region < 0 || !info) continue;
      const erosion = info.erosion >= 1.5 ? info.erosion : 0;
      // Eroded edge: a neighbour `erosion` texels away is outside this region's colour.
      if (erosion > 0 && [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => {
        const nx = Math.floor(jx + dx * erosion), ny = Math.floor(jy + dy * erosion);
        if (nx < 0 || nx >= juvenile.width || ny < 0 || ny >= juvenile.height) return true;
        const j = (ny * juvenile.width + nx) * 4, ref = refs[region];
        return (juvenile.data[j] / 255 - ref[0]) ** 2 + (juvenile.data[j + 1] / 255 - ref[1]) ** 2 + (juvenile.data[j + 2] / 255 - ref[2]) ** 2 > 0.42;
      })) continue;
      const i = (y * width + x) * 4;
      for (let c = 0; c < 3; c++) out[i + c] = Math.round(out[i + c] * info.keep[c]);
    }
  }

  if (source.tmc) {
    const parts = ['teeth', 'mouth', 'claws'].map(slot => hexRgb(palette[slot]));
    for (let i = 0; i < out.length; i += 4) for (let c = 0; c < 3; c++) {
      const weight = source.tmc[i + c] / 255;
      if (weight <= 0) continue;
      for (let k = 0; k < 3; k++) out[i + k] = Math.round(out[i + k] * (1 - weight) + parts[c][k] * weight);
    }
  }

  if (source.rac) for (let i = 0; i < out.length; i += 4) {
    const occlusion = source.rac[i + 1] / 255 * (source.rac[i + 2] / 255);
    out[i] = Math.round(out[i] * occlusion); out[i + 1] = Math.round(out[i + 1] * occlusion); out[i + 2] = Math.round(out[i + 2] * occlusion);
  }

  // Species without a TMC mask colour mouth/claws/teeth from fixed defaults.
  if (!source.tmc && source.mask && overlayDefaults?.some(o => o.alpha > 0)) {
    const overlays = overlayDefaults.map(o => [...hexRgb(o.color), o.alpha]);
    for (let i = 0; i < out.length; i += 4) for (let c = 0; c < 3; c++) {
      const [r, g, b, alpha] = overlays[c] ?? [0, 0, 0, 0];
      const weight = source.mask[i + c] / 255 * alpha;
      if (weight <= 0) continue;
      out[i] = Math.round(out[i] * (1 - weight) + r * weight);
      out[i + 1] = Math.round(out[i + 1] * (1 - weight) + g * weight);
      out[i + 2] = Math.round(out[i + 2] * (1 - weight) + b * weight);
    }
  }

  if (source.noise && noiseAmount > 0) for (let i = 0; i < out.length; i += 4) {
    const shade = 1 - noiseAmount * (1 - source.noise[i] / 255);
    out[i] = Math.round(out[i] * shade); out[i + 1] = Math.round(out[i + 1] * shade); out[i + 2] = Math.round(out[i + 2] * shade);
  }

  return out;
}

/** Tangent-space normal map with a tiled detail normal added on top. */
export function detailedNormals(normal, detail, tiles) {
  const width = normal.image.width, height = normal.image.height;
  const base = pixels(normal.image, width, height);
  const dw = detail.image.width, dh = detail.image.height, fine = pixels(detail.image, dw, dh);
  const data = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const i = (y * width + x) * 4;
    const dx = Math.floor(((x / width * tiles) % 1 + 1) % 1 * dw), dy = Math.floor(((y / height * tiles) % 1 + 1) % 1 * dh);
    const j = (dy * dw + dx) * 4;
    const nx = base[i] / 255 * 2 - 1 + fine[j] / 255 * 2 - 1, ny = base[i + 1] / 255 * 2 - 1 + fine[j + 1] / 255 * 2 - 1, nz = base[i + 2] / 255 * 2 - 1;
    const length = Math.hypot(nx, ny, nz) || 1;
    data[i] = Math.round((nx / length * 0.5 + 0.5) * 255);
    data[i + 1] = Math.round((ny / length * 0.5 + 0.5) * 255);
    data[i + 2] = Math.round((nz / length * 0.5 + 0.5) * 255);
    data[i + 3] = 255;
  }
  return canvasTexture(data, width, height);
}

export function canvasTexture(data, width, height, texture) {
  const canvas = texture?.image ?? document.createElement('canvas');
  canvas.width = width; canvas.height = height;
  canvas.getContext('2d').putImageData(new ImageData(data, width, height), 0, 0);
  if (texture) { texture.needsUpdate = true; return texture; }
  texture = new THREE.CanvasTexture(canvas);
  texture.flipY = false;
  texture.minFilter = THREE.LinearMipmapLinearFilter;
  texture.magFilter = THREE.LinearFilter;
  texture.generateMipmaps = true;
  return texture;
}
