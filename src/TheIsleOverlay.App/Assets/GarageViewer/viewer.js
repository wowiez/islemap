import * as THREE from 'three';
import { GLTFLoader } from './vendor/GLTFLoader.js';
import { MeshoptDecoder } from './vendor/meshopt_decoder.module.js';
import { createSkinMaterial, updateSkinPalette } from './skin-material.js';
import { canvasTexture, detailedNormals, glitchLayers, glitchPalette, paintSkin, pixels } from './sdvn-skin.js';

// IslePilot web studio assets (SDVN #3), proxied by the app with a disk cache.
const SDVN = 'https://isle-sdvn.local';
const SDVN_SHARED = '/cdn/skinviewer/shared/';
const SLOT_DEFAULT = '#808080';
const SLOTS = ['body', 'markings', 'flank', 'underbelly', 'detail', 'display', 'eyes', 'teeth', 'mouth', 'claws'];
let sdvnCatalog, sdvnSkin = null, sdvnPaintTimer;

const state = document.querySelector('#state');
const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance', preserveDrawingBuffer: false, depth: true, stencil: false });
// Render at the screen's real pixel density (125-150% Windows scaling blurred a 1x canvas).
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
const maxAnisotropy = renderer.capabilities.getMaxAnisotropy();
const sharpen = texture => { if (texture) { texture.anisotropy = maxAnisotropy; texture.needsUpdate = true; } return texture; };
renderer.setClearColor('#0c1418', 0);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.NoToneMapping;
document.body.prepend(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(32, 1, 0.01, 1000);
scene.add(new THREE.HemisphereLight('#ffffff', '#404040', 1.2));
for (const [color, strength, pos] of [['#ffffff', 1.8, [2.6,3.8,2.4]], ['#ffffff', .8, [-2.8,1.6,-2.4]]]) {
  const light = new THREE.DirectionalLight(color, strength); light.position.set(...pos); scene.add(light);
}
{
  const key = new THREE.DirectionalLight(new THREE.Color(.9025, .875, .882), 16.374); key.position.set(0, 11.644, 2.903);
  const fill = new THREE.HemisphereLight('#dfe6ee', '#3a3631', .7);
  for (const light of [key, fill]) { light.userData.sdvn = true; light.visible = false; scene.add(light); }
}
// Soft studio environment (sky-to-ground gradient) so shaded sides keep detail instead of going flat.
const studioEnvironment = (() => {
  const envScene = new THREE.Scene();
  const geometry = new THREE.SphereGeometry(10, 32, 16);
  const colors = [], top = new THREE.Color('#f4f6f8'), horizon = new THREE.Color('#9aa3a8'), ground = new THREE.Color('#2e2b27');
  for (let i = 0; i < geometry.attributes.position.count; i++) {
    const y = geometry.attributes.position.getY(i) / 10;
    const c = y > 0 ? horizon.clone().lerp(top, y) : horizon.clone().lerp(ground, -y);
    colors.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
  envScene.add(new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.BackSide })));
  const texture = new THREE.PMREMGenerator(renderer).fromScene(envScene, .04).texture;
  geometry.dispose();
  return texture;
})();
let model, mixer, distance = 8, targetDistance = 8, baseDistance = 8, generation = 0;
const cameraDirection = new THREE.Vector3(-1, .18, .35);
let bodyMaterials = [], eyeMaterials = [], latestPalette = {};
const clock = new THREE.Clock();
// Orbit camera like the web studio: dragging moves the camera around the dino, eased each frame.
const baseView = new THREE.Spherical(), view = new THREE.Spherical();
const orbit = { theta: 0, phi: 0, targetTheta: 0, targetPhi: 0 };
const render = () => {
  orbit.theta += (orbit.targetTheta - orbit.theta) * .2;
  orbit.phi += (orbit.targetPhi - orbit.phi) * .2;
  distance += (targetDistance - distance) * .2;
  view.set(distance, THREE.MathUtils.clamp(baseView.phi + orbit.phi, .12, Math.PI - .12), baseView.theta + orbit.theta);
  camera.position.setFromSpherical(view); camera.lookAt(0,0,0);
  renderer.render(scene,camera);
};
const resize = () => { renderer.setSize(innerWidth,innerHeight); camera.aspect = innerWidth/innerHeight; camera.updateProjectionMatrix(); render(); };
new ResizeObserver(resize).observe(document.body);
function disposeModel(root) {
  root.traverse(node => { if (!node.isMesh) return; node.geometry.dispose(); for (const material of [].concat(node.material)) { for (const value of Object.values(material)) if (value?.isTexture) value.dispose(); material.dispose(); } });
}
const isHex = value => /^#[a-f0-9]{6}$/i.test(value || '');
function applyPalette(palette = {}) {
  for (const material of bodyMaterials) updateSkinPalette(material, palette);
  if (isHex(palette.eyes)) for (const material of eyeMaterials) {
    material.color.set(palette.eyes); material.emissive?.set(palette.eyes);
  }
  window.__viewerDiagnostics = {bodyMaterials: bodyMaterials.length, eyeMaterials: eyeMaterials.length};
}
function fullPalette(palette = {}) {
  return Object.fromEntries(SLOTS.map(slot => [slot, isHex(palette[slot]) ? palette[slot] : SLOT_DEFAULT]));
}
function paintSdvn() {
  if (!sdvnSkin) return;
  const { source, texture, dino, eyes, style } = sdvnSkin;
  const layers = glitchLayers(style.glitchLab, style.renderMode === 'glitched' || style.renderMode === 'glitch');
  const palette = glitchPalette(fullPalette(latestPalette), layers);
  const noise = Math.max(0, Math.min(1, Number(style.glitchLab?.sv) || 0));
  canvasTexture(paintSkin(source, palette, layers, dino.overlayDefaults, noise), source.width, source.height, texture);
  for (const material of eyes) { material.color.set(palette.eyes); material.emissive.set(palette.eyes).multiplyScalar(.4); }
  render();
}
async function loadSdvn(request, version) {
  sdvnCatalog ??= await (await fetch('./sdvn-catalog.json')).json();
  const key = Object.keys(sdvnCatalog).find(name => name.toLowerCase() === String(request.species).toLowerCase());
  const dino = key && sdvnCatalog[key];
  if (!dino) throw new Error('species');
  const image = path => path ? new THREE.ImageLoader().setCrossOrigin('anonymous').loadAsync(SDVN + path) : Promise.resolve(null);
  const index = Math.max(1, Number(request.pattern) || 1);
  const [gltf, pattern, normal, detail, noise, rac, juvenile, mask, tmc] = await Promise.all([
    new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).loadAsync(SDVN + dino.glbModel),
    image(dino.patterns[index] ?? dino.patterns[1]), image(dino.normalMap),
    image(SDVN_SHARED + 'T_DinoSkinDetail_5_N.png'), image(SDVN_SHARED + 'T_SkinNoise_M.png'),
    image(dino.racMap), image(dino.juvenilePattern), image(dino.maskMap), image(dino.patternMasks?.[index] ?? dino.tmcMap)
  ]);
  if (version !== generation) { disposeModel(gltf.scene); return null; }
  const width = pattern.width, height = pattern.height;
  const source = {
    width, height, base: pixels(pattern, width, height),
    rac: rac && pixels(rac, width, height), noise: noise && pixels(noise, width, height, true),
    mask: mask && pixels(mask, width, height), tmc: tmc && pixels(tmc, width, height),
    juvenile: juvenile && { data: pixels(juvenile, juvenile.width, juvenile.height), width: juvenile.width, height: juvenile.height }
  };
  const texture = sharpen(canvasTexture(new Uint8ClampedArray(width * height * 4), width, height));
  const body = new THREE.MeshStandardMaterial({ map: texture, normalMap: sharpen(detailedNormals({image: normal}, {image: detail}, dino.detailScale ?? 12)),
    roughness: .95, metalness: 0, envMapIntensity: .4, side: THREE.DoubleSide });
  const eyes = [];
  gltf.scene.traverse(node => {
    if (!node.isMesh) return;
    const original = [].concat(node.material)[0];
    if (/eye|iris|pupil/i.test(original?.name || '')) {
      const eye = new THREE.MeshStandardMaterial({ roughness: .35, metalness: 0 });
      eyes.push(eye); node.material = eye;
    } else node.material = body;
  });
  sdvnSkin = { source, texture, dino, eyes, style: request };
  paintSdvn();
  return gltf;
}
function useSdvnLighting(on) {
  renderer.toneMapping = on ? THREE.ACESFilmicToneMapping : THREE.NoToneMapping;
  scene.environment = on ? studioEnvironment : null;
  scene.environmentIntensity = .6;
  renderer.toneMappingExposure = on ? .99 : 1;
  for (const light of scene.children) if (light.isLight) light.visible = !!light.userData.sdvn === on;
}
window.chrome.webview.addEventListener('message', async ({data}) => {
  if (data.palette) latestPalette = data.palette;
  if (sdvnSkin && !data.sdvn && !data.model) {
    if ('glitchLab' in data || 'renderMode' in data) sdvnSkin.style = { ...sdvnSkin.style, glitchLab: data.glitchLab, renderMode: data.renderMode };
    clearTimeout(sdvnPaintTimer); sdvnPaintTimer = setTimeout(paintSdvn, 120);
    return;
  }
  if (data.sdvn) {
    const version = ++generation;
    try {
      latestPalette = data.sdvn.palette ?? latestPalette;
      const gltf = await loadSdvn(data.sdvn, version);
      if (!gltf) return;
      useSdvnLighting(true);
      // Studio framing: the GLBs are authored for this scale, offset and camera, not for auto-fit.
      showModel(gltf, version, { scale: sdvnSkin.dino.glbScale, position: sdvnSkin.dino.glbPosition });
    } catch (error) {
      state.textContent = 'Model 3D bị lỗi. Bấm Làm mới để tải lại.'; state.classList.remove('loading');
      window.chrome.webview.postMessage({error: error?.message === 'species' ? 'species' : 'model-load'});
    }
    return;
  }
  if (!data.model) { applyPalette(latestPalette); return; }
  const version = ++generation;
  try {
    const textureLoader = new THREE.TextureLoader();
    const [gltf, pattern, diffuse, utility] = await Promise.all([
      new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).loadAsync(data.model), textureLoader.loadAsync(data.pattern),
      textureLoader.loadAsync(data.diffuse),
      data.utility ? textureLoader.loadAsync(data.utility).catch(() => null) : null
    ]);
    if (version !== generation) { disposeModel(gltf.scene); return; }
    sdvnSkin = null; useSdvnLighting(false);
    for (const texture of [pattern, diffuse, utility].filter(Boolean)) {
      texture.flipY = false;
      texture.anisotropy = maxAnisotropy;
      texture.colorSpace = texture === diffuse ? THREE.SRGBColorSpace : THREE.NoColorSpace;
      texture.needsUpdate = true;
    }
    bodyMaterials = []; eyeMaterials = [];
    gltf.scene.traverse(node => {
      if (!node.isMesh) return;
      node.material = [].concat(node.material).map(original => {
        const name = `${node.name} ${original.name}`;
        const isEye = /eye|iris|pupil|cornea/i.test(name);
        const material = isEye ? new THREE.MeshStandardMaterial({roughness:.2, metalness:.25, emissiveIntensity:.55})
          : createSkinMaterial(diffuse, pattern, utility, data.utilityChannels);
        if (isEye) eyeMaterials.push(material);
        else bodyMaterials.push(material);
        return material;
      });
      if(node.material.length === 1) node.material = node.material[0];
    });
    applyPalette(latestPalette);
    showModel(gltf, version);
  } catch (error) {
    state.textContent = 'Model 3D bị lỗi. Bấm Làm mới để tải lại.'; state.classList.remove('loading');
    window.chrome.webview.postMessage({error:'model-load'});
  }
});
function showModel(gltf, version, placement) {
    if(model) { scene.remove(model); disposeModel(model); }
    model = gltf.scene;
    let fitScale = 1;
    if (placement) {
      model.scale.setScalar(placement.scale); model.position.set(...placement.position); model.rotation.y = -Math.PI / 6;
      cameraDirection.set(-19.33, .89, -.02).normalize(); camera.fov = 40;
    } else {
      const bounds = new THREE.Box3().setFromObject(model), center = bounds.getCenter(new THREE.Vector3());
      model.position.sub(center);
      const size = bounds.getSize(new THREE.Vector3());
      fitScale = 4 / Math.max(size.x,size.y,size.z);
      cameraDirection.set(-1, .18, .35); camera.fov = 32;
    }
    camera.updateProjectionMatrix();
    const pivot = new THREE.Group(); pivot.add(model); model = pivot;
    model.scale.setScalar(fitScale);
    baseView.setFromVector3(cameraDirection);
    orbit.theta = orbit.phi = orbit.targetTheta = orbit.targetPhi = 0;
    scene.add(model);
    mixer = gltf.animations?.length ? new THREE.AnimationMixer(gltf.scene) : null;
    if (mixer) mixer.clipAction(gltf.animations[0]).play();
    baseDistance = placement ? 19.35 * Math.max(1, 1.3 / camera.aspect)
      : Math.max(4.5, 2.7 / Math.tan(THREE.MathUtils.degToRad(camera.fov/2)) / Math.min(camera.aspect, 1.8));
    distance = targetDistance = baseDistance;
    state.textContent = ''; state.classList.remove('loading'); render(); window.chrome.webview.postMessage({ready:true});
}
let drag;
renderer.domElement.addEventListener('pointerdown', e => { drag = [e.clientX,e.clientY]; renderer.domElement.setPointerCapture(e.pointerId); });
renderer.domElement.addEventListener('pointermove', e => {
  if (!drag || !model) return;
  // Same feel as three's OrbitControls: a full canvas height of drag turns the view 360 degrees.
  const turn = 2 * Math.PI / Math.max(renderer.domElement.clientHeight, 1);
  orbit.targetTheta -= (e.clientX - drag[0]) * turn;
  orbit.targetPhi = THREE.MathUtils.clamp(orbit.targetPhi - (e.clientY - drag[1]) * turn, .12 - baseView.phi, Math.PI - .12 - baseView.phi);
  drag = [e.clientX, e.clientY];
});
renderer.domElement.addEventListener('pointerup', () => drag=null);
renderer.domElement.addEventListener('pointercancel', () => drag=null);
window.__viewerZoom = deltaY => { targetDistance = THREE.MathUtils.clamp(targetDistance*Math.exp(Number(deltaY)*.001),baseDistance*.4,baseDistance*2); };
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
