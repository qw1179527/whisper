/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m6 → m6.js
 * 职责：极简 WebGL2 渲染器：顶点色 + 单向雾 + 手电光锥叠加。 刻意不做 PBR/阴影：恐怖氛围由"浓雾 + 手电锥形光 + 近黑环境"承担（V9 §11 设计哲学）， 这也是低端机 30fps 红线的现实前提（V9 §14 P2）。
 * 来源：baseline/game-0.6.0.js 第 1908~2123 行（9067 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：无｜导出：createRenderer, makeCamera
 */
'use strict';

    /**
     * 极简 WebGL2 渲染器：顶点色 + 单向雾 + 手电光锥叠加。
     *
     * 刻意不做 PBR/阴影：恐怖氛围由"浓雾 + 手电锥形光 + 近黑环境"承担（V9 §11 设计哲学），
     * 这也是低端机 30fps 红线的现实前提（V9 §14 P2）。
     */
    const VS = `#version 300 es
    in vec3 aPos; in vec3 aNrm;
    uniform mat4 uVP; uniform vec3 uOrigin;
    out vec3 vNrm; out float vDist; out vec3 vWorld;
    void main() {
      vWorld = aPos;
      vec4 p = uVP * vec4(aPos, 1.0);
      gl_Position = p;
      vNrm = aNrm;
      vDist = length(aPos.xz - uOrigin.xz);
    }`;
    
    const FS = `#version 300 es
    precision mediump float;
    in vec3 vNrm; in float vDist; in vec3 vWorld;
    uniform vec3 uColor; uniform vec3 uFogColor;
    uniform float uBase;
    uniform float uFogNear; uniform float uFogFar;
    uniform vec3 uLightPos; uniform vec3 uLightDir; uniform float uLightRange; uniform float uLightCos;
    out vec4 outColor;
    void main() {
      // 环境：极暗基底，模拟"只有手电"的场面
      float ambient = uBase * (0.85 + 0.15 * max(dot(normalize(vNrm), vec3(0.0, 1.0, 0.0)), 0.0));
      // 手电：位置衰减 × 锥角衰减 × 法线朝向
      vec3 toFrag = vWorld - uLightPos;
      float dist = length(toFrag);
      vec3 dir = toFrag / max(dist, 0.001);
      float cone = smoothstep(uLightCos, uLightCos + 0.18, dot(dir, normalize(uLightDir)));
      float falloff = clamp(1.0 - dist / uLightRange, 0.0, 1.0);
      float ndl = clamp(dot(normalize(vNrm), -dir), 0.0, 1.0);
      float torch = cone * falloff * falloff * (0.35 + 0.65 * ndl) * 2.35;
      vec3 lit = uColor * (ambient + torch);
      float fog = clamp((vDist - uFogNear) / max(uFogFar - uFogNear, 0.001), 0.0, 1.0);
      outColor = vec4(mix(lit, uFogColor, fog), 1.0);
    }`;
    
    function createRenderer(canvas) {
      const gl = canvas.getContext('webgl2', { antialias: false, alpha: false, powerPreference: 'high-performance' });
      if (!gl) throw new Error('本机浏览器不支持 WebGL2');
    
      function compile(type, src) {
        const sh = gl.createShader(type);
        gl.shaderSource(sh, src);
        gl.compileShader(sh);
        if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(sh));
        return sh;
      }
      const prog = gl.createProgram();
      gl.attachShader(prog, compile(gl.VERTEX_SHADER, VS));
      gl.attachShader(prog, compile(gl.FRAGMENT_SHADER, FS));
      gl.linkProgram(prog);
      if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog));
      gl.useProgram(prog);
    
      const U = {};
      for (const n of ['uVP', 'uOrigin', 'uColor', 'uFogColor', 'uFogNear', 'uFogFar', 'uLightPos', 'uLightDir', 'uLightRange', 'uLightCos', 'uBase']) {
        U[n] = gl.getUniformLocation(prog, n);
      }
      const aPos = gl.getAttribLocation(prog, 'aPos');
      const aNrm = gl.getAttribLocation(prog, 'aNrm');
    
      const meshes = [];
      const stats = { drawCalls: 0, triangles: 0 };
    
      /** 上传一批几何（verts: [x,y,z,nx,ny,nz]*, idx, color） */
      function upload(verts, idx, color, name) {
        const vao = gl.createVertexArray();
        gl.bindVertexArray(vao);
        const vbo = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        gl.bufferData(gl.ARRAY_BUFFER, verts, gl.STATIC_DRAW);
        gl.enableVertexAttribArray(aPos);
        gl.vertexAttribPointer(aPos, 3, gl.FLOAT, false, 24, 0);
        gl.enableVertexAttribArray(aNrm);
        gl.vertexAttribPointer(aNrm, 3, gl.FLOAT, false, 24, 12);
        const ibo = gl.createBuffer();
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo);
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, idx, gl.STATIC_DRAW);
        gl.bindVertexArray(null);
        const m = { vao, count: idx.length, color, name };
        meshes.push(m);
        return m;
      }
    
      function draw(mesh, mvp, origin, light, opts = {}) {
        gl.bindVertexArray(mesh.vao);
        if (opts.depthWrite === false) gl.depthMask(false);
        if (opts.additive) { gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE); }
        gl.uniformMatrix4fv(U.uVP, false, mvp);
        gl.uniform3f(U.uOrigin, origin.x, 0, origin.z);
        gl.uniform3f(U.uColor, mesh.color[0], mesh.color[1], mesh.color[2]);
        gl.uniform3f(U.uLightPos, light.pos.x, light.pos.y, light.pos.z);
        gl.uniform3f(U.uLightDir, light.dir.x, light.dir.y, light.dir.z);
        gl.uniform1f(U.uLightRange, light.range);
        gl.uniform1f(U.uLightCos, light.cosOuter);
        gl.uniform1f(U.uBase, mesh.base ?? 0.16);
        const triCount = (mesh.count ?? 0) / 3;
        gl.drawElements(gl.TRIANGLES, mesh.count ?? 0, gl.UNSIGNED_SHORT, 0);
        if (opts.additive) gl.disable(gl.BLEND);
        if (opts.depthWrite === false) gl.depthMask(true);
        stats.drawCalls++;
        stats.triangles += Number.isFinite(triCount) ? triCount : 0;
      }
    
      function beginFrame(w, h, fog) {
        gl.viewport(0, 0, w, h);
        gl.enable(gl.DEPTH_TEST);
        gl.disable(gl.BLEND);
        gl.clearColor(fog.color[0], fog.color[1], fog.color[2], 1);
        gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
        gl.uniform3f(U.uFogColor, fog.color[0], fog.color[1], fog.color[2]);
        gl.uniform1f(U.uFogNear, fog.near);
        gl.uniform1f(U.uFogFar, fog.far);
        stats.drawCalls = 0;
        stats.triangles = 0;
      }
    
      /**
       * 按颜色子范围切分上传。
       * 教训：把跨全图的地板大四边形与墙条带塞进同一个 VBO、再按"颜色分组"整体绘制，
       * 会把地板当成墙画——几何数据必须按绘制单元切分，而不是按颜色索引去猜 offset。
       * 每个 (颜色, 子范围) 组合 = 一个 VAO + 一次 draw call，count 精确对应。
       */
      function uploadRanges(verts, indices, ranges, namePrefix) {
        const out = [];
        const vertexStride = 6;
        for (const r of ranges) {
          const vStart = r.start;
          const vEnd = r.start + r.count;
          const localIdx = [];
          for (const i of indices) if (i >= vStart && i < vEnd) localIdx.push(i - vStart);
          if (localIdx.length === 0) continue;
          const sub = verts.slice(vStart * vertexStride, vEnd * vertexStride);
          const mesh = upload(sub, new Uint16Array(localIdx), r.color, namePrefix + ':' + r.name);
          out.push({ mesh, color: r.color, triangles: localIdx.length / 3 });
        }
        return out;
      }
    
      /**
       * 按颜色合并：把同一颜色的多个子范围拼成一段连续缓冲。
       * 动机：内饰有 169 个实例、每个贡献 1~8 个颜色块，逐块绘制让 draw call 冲到 181，
       * 直接违反 V9 的性能门禁（Draw Call ≤120）。合并后每颜色一次 draw call。
       */
      function uploadMerged(verts, indices, ranges, namePrefix) {
        const byColor = new Map();
        for (const r of ranges) {
          const key = r.color.join(',');
          if (!byColor.has(key)) byColor.set(key, { color: r.color, parts: [] });
          byColor.get(key).parts.push(r);
        }
        const out = [];
        for (const { color, parts } of byColor.values()) {
          const vCount = parts.reduce((a, p) => a + p.count, 0);
          const merged = new Float32Array(vCount * 6);
          const mergedIdx = [];
          let vOffset = 0;
          for (const p of parts) {
            merged.set(verts.subarray(p.start * 6, (p.start + p.count) * 6), vOffset * 6);
            for (const i of indices) {
              if (i >= p.start && i < p.start + p.count) mergedIdx.push(vOffset + (i - p.start));
            }
            vOffset += p.count;
          }
          if (mergedIdx.length === 0) continue;
          out.push({ vao: upload(merged, new Uint16Array(mergedIdx), color, namePrefix + ':' + color.join(',')).vao, count: mergedIdx.length, color });
        }
        return out;
      }
    
      return { gl, upload, uploadRanges, uploadMerged, draw, beginFrame, stats, meshes };
    }
    
    /** 列主序 4×4 透视 + 视角矩阵（避免引入 mat4 库） */
    function makeCamera() {
      return {
        projection(fovDeg, aspect, near, far) {
          const f = 1 / Math.tan((fovDeg * Math.PI) / 360);
          return new Float32Array([f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) / (near - far), -1, 0, 0, (2 * far * near) / (near - far), 0]);
        },
        view(px, py, pz, yaw, pitch) {
          const cy = Math.cos(yaw), sy = Math.sin(yaw);
          const cp = Math.cos(pitch), sp = Math.sin(pitch);
          // forward/right/up（yaw 绕 Y，pitch 绕右轴）
          const fx = sy * cp, fy = sp, fz = -cy * cp;
          const rx = cy, ry = 0, rz = sy;
          const ux = ry * fz - rz * fy, uy = rz * fx - rx * fz, uz = rx * fy - ry * fx;
          return new Float32Array([
            rx, ux, -fx, 0,
            ry, uy, -fy, 0,
            rz, uz, -fz, 0,
            -(rx * px + ry * py + rz * pz), -(ux * px + uy * py + uz * pz), fx * px + fy * py + fz * pz, 1,
          ]);
        },
        multiply(a, b) {
          const o = new Float32Array(16);
          for (let c = 0; c < 4; c++) {
            for (let r = 0; r < 4; r++) {
              o[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
            }
          }
          return o;
        },
      };
    }
    
    module.exports = { createRenderer, makeCamera };
