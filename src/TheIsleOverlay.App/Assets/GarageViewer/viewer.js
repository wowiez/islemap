import * as THREE from 'three';
import { GLTFLoader } from './vendor/GLTFLoader.js';
import { MeshoptDecoder } from './vendor/meshopt_decoder.module.js';
import { createSkinMaterial, updateSkinPalette } from './skin-material.js';

const state = document.querySelector('#state');
const renderer = new THREE.WebGLRenderer({ antialias: false, alpha: true, powerPreference: 'high-performance', preserveDrawingBuffer: false, depth: true, stencil: false });
renderer.setPixelRatio(1);
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
let model, mixer, distance = 8, baseDistance = 8, generation = 0;
let bodyMaterials = [], eyeMaterials = [], latestPalette = {};
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
function applyPalette(palette = {}) {
  for (const material of bodyMaterials) updateSkinPalette(material, palette);
  if (isHex(palette.eyes)) for (const material of eyeMaterials) {
    material.color.set(palette.eyes); material.emissive?.set(palette.eyes);
  }
  window.__viewerDiagnostics = {bodyMaterials: bodyMaterials.length, eyeMaterials: eyeMaterials.length};
}
window.chrome.webview.addEventListener('message', async ({data}) => {
  if (data.palette) latestPalette = data.palette;
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
    if(model) { scene.remove(model); disposeModel(model); }
    model = gltf.scene;
    for (const texture of [pattern, diffuse, utility].filter(Boolean)) {
      texture.flipY = false;
      texture.colorSpace = texture === diffuse ? THREE.SRGBColorSpace : THREE.NoColorSpace;
      texture.needsUpdate = true;
    }
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
