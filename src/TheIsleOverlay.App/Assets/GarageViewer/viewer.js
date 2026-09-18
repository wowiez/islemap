import * as THREE from 'three';
import { GLTFLoader } from './vendor/GLTFLoader.js';

const state = document.querySelector('#state');
const renderer = new THREE.WebGLRenderer({ antialias: false, alpha: true, powerPreference: 'high-performance', preserveDrawingBuffer: false, depth: true, stencil: false });
renderer.setPixelRatio(1);
renderer.setClearColor('#0c1418', 0);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.2;
document.body.prepend(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(32, 1, 0.01, 1000);
scene.add(new THREE.HemisphereLight('#edf6ff', '#303a35', 2.5));
for (const [color, strength, pos] of [['#fff1dc', 10, [-5,8,6]], ['#73b9d7', 4, [6,4,-5]]]) {
  const light = new THREE.DirectionalLight(color, strength); light.position.set(...pos); scene.add(light);
}
let model, mixer, distance = 8, baseDistance = 8, generation = 0;
let bodyMaterials = [], eyeMaterials = [], sourcePixels, tmcPixels, racPixels, regionPixels, racShadePixels, skinCanvas, skinTexture, normalTexture;
const clock = new THREE.Clock();
let targetRotationY = 0, targetRotationZ = 0;
const render = () => {
  camera.position.set(-distance, distance * .18, distance * .35); camera.lookAt(0,0,0);
  if (model) {
    model.rotation.y += (targetRotationY - model.rotation.y) * .22;
    model.rotation.z += (targetRotationZ - model.rotation.z) * .22;
  }
  renderer.render(scene,camera);
};
const resize = () => { renderer.setSize(innerWidth,innerHeight); camera.aspect = innerWidth/innerHeight; camera.updateProjectionMatrix(); render(); };
new ResizeObserver(resize).observe(document.body);
function disposeModel(root) {
  root.traverse(node => { if (!node.isMesh) return; node.geometry.dispose(); for (const material of [].concat(node.material)) { for (const value of Object.values(material)) if (value?.isTexture) value.dispose(); material.dispose(); } });
}
const isHex = value => /^#[a-f0-9]{6}$/i.test(value || '');
const rgb = value => [1, 1, 1, 1].map((_, i) => i < 3 ? parseInt(value.slice(1 + i * 2, 3 + i * 2), 16) : 255);
const colorMarkers = [
  [1, 0, 0], [0, 1, 0], [0, 1 / 255, 245 / 255],
  [0, 1, 241 / 255], [1, 0, 1], [1, 1, 0]
];
const colorKeys = ['display', 'underbelly', 'flank', 'body', 'markings', 'detail'];

function pixelsFrom(texture, width, height) {
  if (!texture?.image) return null;
  const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height;
  const context = canvas.getContext('2d', {willReadFrequently:true});
  context.imageSmoothingEnabled = false; context.drawImage(texture.image, 0, 0, width, height);
  return context.getImageData(0, 0, width, height).data;
}

function buildSkinTexture(pattern, tmc, rac) {
  skinCanvas = document.createElement('canvas');
  skinCanvas.width = pattern.image.width; skinCanvas.height = pattern.image.height;
  sourcePixels = pixelsFrom(pattern, skinCanvas.width, skinCanvas.height);
  tmcPixels = pixelsFrom(tmc, skinCanvas.width, skinCanvas.height);
  racPixels = pixelsFrom(rac, skinCanvas.width, skinCanvas.height);
  regionPixels = new Uint8Array(sourcePixels.length / 4);
  racShadePixels = racPixels ? new Uint8Array(regionPixels.length) : null;
  for (let offset = 0, pixel = 0; offset < sourcePixels.length; offset += 4, pixel++) {
    const red = sourcePixels[offset] / 255, green = sourcePixels[offset + 1] / 255, blue = sourcePixels[offset + 2] / 255;
    const peak = Math.max(red, green, blue);
    if (peak > .04) {
      const normalized = [red / peak, green / peak, blue / peak];
      let nearest = 0, nearestDistance = Infinity;
      for (let channel = 0; channel < colorMarkers.length; channel++) {
        const marker = colorMarkers[channel];
        const distance = (normalized[0] - marker[0]) ** 2 + (normalized[1] - marker[1]) ** 2 + (normalized[2] - marker[2]) ** 2;
        if (distance < nearestDistance) { nearestDistance = distance; nearest = channel; }
      }
      regionPixels[pixel] = nearestDistance <= (nearest === 4 ? .6 : .42) ? nearest + 1 : 0;
    }
    if (racShadePixels) racShadePixels[pixel] = Math.round((1 - (racPixels[offset + 1] / 255) * (racPixels[offset + 2] / 255)) * 255);
  }
  skinTexture = new THREE.CanvasTexture(skinCanvas);
  skinTexture.colorSpace = THREE.SRGBColorSpace;
  skinTexture.flipY = false;
  skinTexture.generateMipmaps = true;
}

function applyPalette(palette = {}) {
  if (!model || !sourcePixels || !skinTexture) return;
  const output = new Uint8ClampedArray(sourcePixels);
  const colors = colorKeys.map(key => isHex(palette[key]) ? rgb(palette[key]) : null);
  const detailColors = ['teeth', 'mouth', 'claws'].map(key => isHex(palette[key]) ? rgb(palette[key]) : null);
  for (let offset = 0, pixel = 0; offset < output.length; offset += 4, pixel++) {
    const region = regionPixels?.[pixel] || 0;
    const selected = region ? colors[region - 1] : null;
    if (selected) { output[offset] = selected[0]; output[offset + 1] = selected[1]; output[offset + 2] = selected[2]; output[offset + 3] = 255; }
    if (tmcPixels) {
      for (let channel = 0; channel < 3; channel++) {
        const selected = detailColors[channel], amount = tmcPixels[offset + channel] / 255;
        if (!selected || amount <= 0) continue;
        output[offset] = Math.round(output[offset] * (1 - amount) + selected[0] * amount);
        output[offset + 1] = Math.round(output[offset + 1] * (1 - amount) + selected[1] * amount);
        output[offset + 2] = Math.round(output[offset + 2] * (1 - amount) + selected[2] * amount);
      }
    }
    if (racShadePixels) {
      const shade = racShadePixels[pixel] / 255;
      output[offset] = Math.round(output[offset] * shade);
      output[offset + 1] = Math.round(output[offset + 1] * shade);
      output[offset + 2] = Math.round(output[offset + 2] * shade);
    }
  }
  skinCanvas.getContext('2d').putImageData(new ImageData(output, skinCanvas.width, skinCanvas.height), 0, 0);
  skinTexture.needsUpdate = true;
  for (const material of bodyMaterials) {
    material.color.set('#ffffff'); material.map = skinTexture; material.normalMap = normalTexture || null;
    material.roughness = .95; material.metalness = 0; material.vertexColors = false; material.needsUpdate = true;
  }
  if (isHex(palette.eyes)) for (const material of eyeMaterials) {
    material.color.set(palette.eyes); material.emissive?.set(palette.eyes).multiplyScalar(.4); material.needsUpdate = true;
  }
  window.__viewerDiagnostics = {bodyMaterials: bodyMaterials.length, eyeMaterials: eyeMaterials.length, hasSkinTexture: true};
}
window.chrome.webview.addEventListener('message', async ({data}) => {
  if (!data.model) { applyPalette(data.palette); return; }
  const version = ++generation;
  try {
    const textureLoader = new THREE.TextureLoader();
    const [gltf, pattern, normal, tmc, rac] = await Promise.all([
      new GLTFLoader().loadAsync(data.model), textureLoader.loadAsync(data.pattern),
      data.normal ? textureLoader.loadAsync(data.normal).catch(() => null) : null,
      data.tmc ? textureLoader.loadAsync(data.tmc).catch(() => null) : null,
      data.rac ? textureLoader.loadAsync(data.rac).catch(() => null) : null
    ]);
    if (version !== generation) { disposeModel(gltf.scene); return; }
    if(model) { scene.remove(model); disposeModel(model); }
    model = gltf.scene;
    normalTexture = normal;
    if (normalTexture) { normalTexture.flipY = false; normalTexture.needsUpdate = true; }
    buildSkinTexture(pattern, tmc, rac);
    const bounds = new THREE.Box3().setFromObject(model), center = bounds.getCenter(new THREE.Vector3());
    model.position.sub(center);
    const size = bounds.getSize(new THREE.Vector3());
    const pivot = new THREE.Group(); pivot.add(model); model = pivot;
    const max = Math.max(size.x,size.y,size.z); model.scale.setScalar(4 / max);
    targetRotationY = model.rotation.y = 0;
    targetRotationZ = model.rotation.z = 0;
    bodyMaterials = []; eyeMaterials = [];
    model.traverse(node => {
      if (!node.isMesh) return;
      node.material = [].concat(node.material).map(original => {
        const material = original.clone();
        const name = `${node.name} ${material.name}`;
        if (/eye|iris|pupil/i.test(name)) eyeMaterials.push(material);
        else bodyMaterials.push(material);
        return material;
      });
      if(node.material.length === 1) node.material = node.material[0];
    });
    applyPalette(data.palette || {});
    scene.add(model);
    mixer = gltf.animations?.length ? new THREE.AnimationMixer(gltf.scene) : null;
    if (mixer) mixer.clipAction(gltf.animations[0]).play();
    baseDistance = Math.max(4.5, 2.7 / Math.tan(THREE.MathUtils.degToRad(camera.fov/2)) / Math.min(camera.aspect, 1.8));
    distance = baseDistance;
    state.textContent = ''; render(); window.chrome.webview.postMessage({ready:true});
  } catch (error) {
    state.textContent = 'Model 3D bị lỗi. Bấm Làm mới để tải lại.';
    window.chrome.webview.postMessage({error:'model-load'});
  }
});
let drag;
renderer.domElement.addEventListener('pointerdown', e => { drag = [e.clientX,e.clientY]; renderer.domElement.setPointerCapture(e.pointerId); });
renderer.domElement.addEventListener('pointermove', e => { if(!drag || !model) return; targetRotationY += (e.clientX-drag[0])*.009; targetRotationZ = THREE.MathUtils.clamp(targetRotationZ+(e.clientY-drag[1])*.005,-.5,.5); drag=[e.clientX,e.clientY]; });
renderer.domElement.addEventListener('pointerup', () => drag=null);
renderer.domElement.addEventListener('pointercancel', () => drag=null);
window.__viewerZoom = deltaY => { distance=THREE.MathUtils.clamp(distance*Math.exp(Number(deltaY)*.001),baseDistance*.4,baseDistance*2); render(); };
renderer.domElement.addEventListener('wheel', e => { e.preventDefault(); e.stopPropagation(); window.__viewerZoom(e.deltaY); }, {passive:false});
let contextRecoveryTimer;
renderer.domElement.addEventListener('webglcontextlost', e => {
  e.preventDefault();
  state.textContent='3D đang khôi phục…';
  clearTimeout(contextRecoveryTimer);
  contextRecoveryTimer = setTimeout(() => window.chrome.webview.postMessage({error:'webgl-context-lost'}), 1500);
});
renderer.domElement.addEventListener('webglcontextrestored', () => {
  clearTimeout(contextRecoveryTimer);
  state.textContent='';
  render();
});
resize();
renderer.setAnimationLoop(() => { if (mixer) mixer.update(Math.min(clock.getDelta(), .05)); render(); });
window.chrome.webview.postMessage({initialized:true});
