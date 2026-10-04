import * as THREE from 'three';

const slots = { body: 'skinBody', markings: 'skinMarkings', flank: 'skinFlank',
  underbelly: 'skinUnderbelly', detail: 'skinDetail', display: 'skinDisplay' };
const utilitySlots = ['teeth', 'mouth', 'claws'];
const isHex = value => /^#[a-f0-9]{6}$/i.test(value || '');

export function createSkinMaterial(diffuse, pattern, utility, channels = {}) {
  const uniforms = { skinMask: { value: pattern }, skinAmount: { value: 0 } };
  for (const name of Object.values(slots)) uniforms[name] = { value: new THREE.Color('#808080') };
  for (const slot of utilitySlots) {
    uniforms['skin_' + slot] = { value: new THREE.Color('#808080') };
    uniforms['skin_channel_' + slot] = { value: new THREE.Vector3(
      channels[slot] === 'r' ? 1 : 0, channels[slot] === 'g' ? 1 : 0, channels[slot] === 'b' ? 1 : 0) };
  }
  uniforms.skinUtility = { value: utility };
  const advanced = !!utility && utilitySlots.some(slot => /^[rgb]$/.test(channels[slot] || ''));
  const material = new THREE.MeshStandardMaterial({ map: diffuse, roughness: .6, metalness: .04 });
  material.userData.skinUniforms = uniforms;
  material.customProgramCacheKey = () => 'sbtc-skin-' + (advanced ? 'advanced' : 'basic');
  material.onBeforeCompile = shader => {
    Object.assign(shader.uniforms, uniforms);
    const declarations = `uniform sampler2D skinMask;
uniform float skinAmount;
uniform vec3 skinBody, skinMarkings, skinFlank, skinUnderbelly, skinDetail, skinDisplay;
${advanced ? `uniform sampler2D skinUtility;
uniform vec3 skin_teeth, skin_mouth, skin_claws;
uniform vec3 skin_channel_teeth, skin_channel_mouth, skin_channel_claws;` : ''}
`;
    shader.fragmentShader = declarations + shader.fragmentShader.replace('#include <map_fragment>', `
#ifdef USE_MAP
  vec4 basePixel = texture2D(map, vMapUv);
  vec3 maskPixel = texture2D(skinMask, vMapUv).rgb;
  float red = maskPixel.r, green = maskPixel.g, blue = maskPixel.b;
  float bodyWeight = (1.0 - red) * green * blue;
  float markingWeight = red * (1.0 - green) * blue;
  float flankWeight = (1.0 - red) * (1.0 - green) * blue;
  float bellyWeight = (1.0 - red) * green * (1.0 - blue);
  float detailWeight = red * green * (1.0 - blue);
  float displayWeight = red * (1.0 - green) * (1.0 - blue);
  float blackWeight = (1.0 - red) * (1.0 - green) * (1.0 - blue);
  float totalWeight = max(bodyWeight + markingWeight + flankWeight + bellyWeight + detailWeight + displayWeight + blackWeight, 0.0001);
  vec3 regionColor = (skinBody * (bodyWeight + blackWeight) + skinMarkings * markingWeight
    + skinFlank * flankWeight + skinUnderbelly * bellyWeight + skinDetail * detailWeight
    + skinDisplay * displayWeight) / totalWeight;
  float relief = 0.95 + 0.55 * dot(basePixel.rgb, vec3(0.299, 0.587, 0.114));
  vec3 tintedPixel = regionColor * relief;
  ${advanced ? `vec3 utilityPixel = texture2D(skinUtility, vMapUv).rgb;
  tintedPixel = mix(tintedPixel, skin_teeth * relief, clamp(dot(utilityPixel, skin_channel_teeth), 0.0, 1.0));
  tintedPixel = mix(tintedPixel, skin_mouth * relief, clamp(dot(utilityPixel, skin_channel_mouth), 0.0, 1.0));
  tintedPixel = mix(tintedPixel, skin_claws * relief, clamp(dot(utilityPixel, skin_channel_claws), 0.0, 1.0));` : ''}
  basePixel.rgb = mix(basePixel.rgb, tintedPixel, skinAmount);
  diffuseColor *= basePixel;
#endif`);
  };
  return material;
}

export function updateSkinPalette(material, palette) {
  const uniforms = material.userData.skinUniforms;
  if (!uniforms) return;
  uniforms.skinAmount.value = Object.keys(slots).every(slot => isHex(palette[slot])) ? 1 : 0;
  for (const [slot, name] of Object.entries(slots)) {
    if (isHex(palette[slot])) uniforms[name].value.set(palette[slot]);
  }
  for (const slot of utilitySlots) {
    if (isHex(palette[slot])) uniforms['skin_' + slot].value.set(palette[slot]);
  }
}
