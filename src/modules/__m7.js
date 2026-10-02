/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m7 → m7.js
 * 职责：极简 GLB 解析器（零依赖）。 只支持本仓库 Blender 导出的子集：单场景、mesh 节点、POSITION/NORMAL/COLOR_0、材质 baseColorFactor。 为什么自己写而不引 three.js：本机无包管理器兜底（V9 §19 零依赖纪律）， 且解析产物直接喂给自写渲染器，比引入 600KB 运行时更可控。
 * 来源：baseline/game-0.6.0.js 第 2123~2222 行（4526 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：无｜导出：parseGLB
 */
'use strict';

    /**
     * 极简 GLB 解析器（零依赖）。
     * 只支持本仓库 Blender 导出的子集：单场景、mesh 节点、POSITION/NORMAL/COLOR_0、材质 baseColorFactor。
     * 为什么自己写而不引 three.js：本机无包管理器兜底（V9 §19 零依赖纪律），
     * 且解析产物直接喂给自写渲染器，比引入 600KB 运行时更可控。
     */
    function parseGLB(arrayBuffer) {
      const dv = new DataView(arrayBuffer);
      const magic = dv.getUint32(0, true);
      if (magic !== 0x46546c67) throw new Error('不是 GLB 文件（magic 不匹配）');
      const version = dv.getUint32(4, true);
      if (version !== 2) throw new Error('仅支持 glTF 2.0，实得版本 ' + version);
    
      let offset = 12;
      let json = null;
      let bin = null;
      while (offset < dv.byteLength) {
        const len = dv.getUint32(offset, true);
        const type = dv.getUint32(offset + 4, true);
        const body = arrayBuffer.slice(offset + 8, offset + 8 + len);
        if (type === 0x4e4f534a) json = JSON.parse(new TextDecoder().decode(body));
        else if (type === 0x004e4942) bin = body;
        offset += 8 + len + ((4 - (len % 4)) % 4);
      }
      if (!json) throw new Error('GLB 缺少 JSON chunk');
      return buildScene(json, bin);
    }
    
    const COMP = { 5120: Int8Array, 5121: Uint8Array, 5122: Int16Array, 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
    const NUM = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 };
    
    function readAccessor(json, bin, index) {
      const acc = json.accessors[index];
      const view = json.bufferViews[acc.bufferView];
      const TA = COMP[acc.componentType];
      const num = NUM[acc.type];
      const stride = view.byteStride ?? num * TA.BYTES_PER_ELEMENT;
      const base = (view.byteOffset ?? 0) + (acc.byteOffset ?? 0);
      const out = new Float32Array(acc.count * num);
      for (let i = 0; i < acc.count; i++) {
        const dv = new DataView(bin, base + i * stride, num * TA.BYTES_PER_ELEMENT);
        for (let c = 0; c < num; c++) {
          const o = c * TA.BYTES_PER_ELEMENT;
          out[i * num + c] = TA === Float32Array ? dv.getFloat32(o, true)
            : TA === Uint16Array ? dv.getUint16(o, true)
            : TA === Uint8Array ? (acc.normalized ? dv.getUint8(o) / 255 : dv.getUint8(o))
            : TA === Int16Array ? dv.getInt16(o, true)
            : dv.getUint32(o, true);
        }
      }
      return { data: out, num, count: acc.count };
    }
    
    function getColorFactor(json, matIndex) {
      const m = json.materials?.[matIndex];
      const f = m?.pbrMetallicRoughness?.baseColorFactor ?? [0.8, 0.8, 0.8, 1];
      return [f[0], f[1], f[2], f[3] ?? 1];
    }
    
    function buildScene(json, bin) {
      const nodes = [];
      for (const node of json.nodes ?? []) {
        if (node.mesh == null) continue;
        const mesh = json.meshes[node.mesh];
        const prims = mesh.primitives.map((p) => {
          const pos = readAccessor(json, bin, p.attributes.POSITION);
          const nrm = p.attributes.NORMAL != null ? readAccessor(json, bin, p.attributes.NORMAL) : null;
          const idx = p.indices != null ? readAccessor(json, bin, p.indices) : null;
          const color = [0.75, 0.75, 0.75, 1];
          const matIdx = p.material ?? 0;
          const f = getColorFactor(json, matIdx);
          color[0] = f[0]; color[1] = f[1]; color[2] = f[2]; color[3] = f[3];
          // 顶点数组打平为 [x,y,z, nx,ny,nz]
          const vcount = pos.count;
          const verts = new Float32Array(vcount * 6);
          for (let i = 0; i < vcount; i++) {
            verts[i * 6] = pos.data[i * 3];
            verts[i * 6 + 1] = pos.data[i * 3 + 1];
            verts[i * 6 + 2] = pos.data[i * 3 + 2];
            verts[i * 6 + 3] = nrm ? nrm.data[i * 3] : 0;
            verts[i * 6 + 4] = nrm ? nrm.data[i * 3 + 1] : 1;
            verts[i * 6 + 5] = nrm ? nrm.data[i * 3 + 2] : 0;
          }
          const indices = idx ? new Uint16Array(idx.data) : new Uint16Array(vcount).map((_, i) => i);
          return { verts, indices, color, materialName: json.materials?.[matIdx]?.name ?? ('mat' + matIdx) };
        });
        nodes.push({ name: node.name ?? mesh.name, primitives: prims });
      }
      return {
        nodes,
        byName: Object.fromEntries(nodes.map((n) => [n.name, n])),
        materials: (json.materials ?? []).map((m) => ({ name: m.name, color: getColorFactor(json, json.materials.indexOf(m)) })),
        stats: { nodeCount: nodes.length, primitiveCount: nodes.reduce((a, n) => a + n.primitives.length, 0) },
      };
    }
    
    module.exports = { parseGLB };
