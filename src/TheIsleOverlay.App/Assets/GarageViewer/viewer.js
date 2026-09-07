import * as THREE from 'three';
import { GLTFLoader } from './vendor/GLTFLoader.js';

const state = document.querySelector('#state');
const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'low-power' });
renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
renderer.setClearColor('#0c1418');
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.2;
document.body.prepend(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(32, 1, 0.01, 1000);
scene.add(new THREE.HemisphereLight('#edf6ff', '#303a35', 2));
for (const [color, strength, pos] of [['#fff1dc', 3, [-5,8,6]], ['#73b9d7', 2, [6,4,-5]]]) {
  const light = new THREE.DirectionalLight(color, strength); light.position.set(...pos); scene.add(light);
}
let model, distance = 8, baseDistance = 8, generation = 0;
const render = () => { camera.position.set(-distance, distance * .18, distance * .35); camera.lookAt(0,0,0); renderer.render(scene,camera); };
const resize = () => { renderer.setSize(innerWidth,innerHeight); camera.aspect = innerWidth/innerHeight; camera.updateProjectionMatrix(); render(); };
new ResizeObserver(resize).observe(document.body);
function disposeModel(root) {
  root.traverse(node => { if (!node.isMesh) return; node.geometry.dispose(); for (const material of [].concat(node.material)) { for (const value of Object.values(material)) if (value?.isTexture) value.dispose(); material.dispose(); } });
}
window.chrome.webview.addEventListener('message', async ({data}) => {
  const version = ++generation;
  try {
    const gltf = await new GLTFLoader().loadAsync(data.model);
    if (version !== generation) { disposeModel(gltf.scene); return; }
    if(model) { scene.remove(model); disposeModel(model); }
    model = gltf.scene;
    const bounds = new THREE.Box3().setFromObject(model), center = bounds.getCenter(new THREE.Vector3());
    model.position.sub(center);
    const size = bounds.getSize(new THREE.Vector3());
    const pivot = new THREE.Group(); pivot.add(model); model = pivot;
    const max = Math.max(size.x,size.y,size.z); model.scale.setScalar(4 / max);
    const palette = data.palette || {};
    model.traverse(node => {
      if (!node.isMesh) return;
      node.material = [].concat(node.material).map(original => {
        const material = original.clone();
        const name = `${node.name} ${material.name}`.toLowerCase();
        const channel = /eye|iris/.test(name) ? 'eyes' : /teeth/.test(name) ? 'teeth' : /claw/.test(name) ? 'claws' : /mouth/.test(name) ? 'mouth' : 'body';
        if (material.color && /^#[a-f0-9]{6}$/i.test(palette[channel] || '')) material.color.set(palette[channel]);
        if ('roughness' in material) material.roughness = .78;
        return material;
      });
      if(node.material.length === 1) node.material = node.material[0];
    });
    scene.add(model);
    baseDistance = Math.max(4.5, 2.7 / Math.tan(THREE.MathUtils.degToRad(camera.fov/2)) / Math.min(camera.aspect, 1.8));
    distance = baseDistance;
    state.textContent = ''; render(); window.chrome.webview.postMessage({ready:true});
  } catch { state.textContent = 'Không tải được model 3D. Mở lại Garage để thử lại.'; }
});
let drag;
renderer.domElement.addEventListener('pointerdown', e => { drag = [e.clientX,e.clientY]; renderer.domElement.setPointerCapture(e.pointerId); });
renderer.domElement.addEventListener('pointermove', e => { if(!drag || !model) return; model.rotation.y += (e.clientX-drag[0])*.009; model.rotation.z = THREE.MathUtils.clamp(model.rotation.z+(e.clientY-drag[1])*.005,-.5,.5); drag=[e.clientX,e.clientY]; render(); });
renderer.domElement.addEventListener('pointerup', () => drag=null);
renderer.domElement.addEventListener('pointercancel', () => drag=null);
renderer.domElement.addEventListener('wheel', e => { e.preventDefault(); distance=THREE.MathUtils.clamp(distance*Math.exp(e.deltaY*.001),baseDistance*.4,baseDistance*2); render(); }, {passive:false});
renderer.domElement.addEventListener('webglcontextlost', e => { e.preventDefault(); state.textContent='3D đã tạm dừng. Mở lại Garage để tải lại.'; });
resize();
window.chrome.webview.postMessage({initialized:true});
