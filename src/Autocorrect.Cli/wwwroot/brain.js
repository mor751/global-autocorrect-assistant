const state = {
  view: "vectors",
  status: null,
  vectorData: null,
  graphData: null,
  transform: { x: 0, y: 0, scale: 1 },
  selectedId: null,
  folderColors: new Map(),
  graphSimulation: null
};

const els = {
  subtitle: document.getElementById("subtitle"),
  statusCard: document.getElementById("statusCard"),
  legendList: document.getElementById("legendList"),
  selectionCard: document.getElementById("selectionCard"),
  searchForm: document.getElementById("searchForm"),
  searchInput: document.getElementById("searchInput"),
  searchResults: document.getElementById("searchResults"),
  vectorCanvas: document.getElementById("vectorCanvas"),
  graphSvg: document.getElementById("graphSvg"),
  loadingOverlay: document.getElementById("loadingOverlay"),
  hudStats: document.getElementById("hudStats"),
  reloadBtn: document.getElementById("reloadBtn"),
  resetBtn: document.getElementById("resetBtn")
};

async function api(path, options) {
  const response = await fetch(path, options);
  if (!response.ok) {
    throw new Error(`API ${path} failed (${response.status})`);
  }
  return response.json();
}

function setLoading(show, text) {
  els.loadingOverlay.classList.toggle("hidden", !show);
  if (text) {
    els.loadingOverlay.querySelector("p").textContent = text;
  }
}

function renderStatus(status) {
  state.status = status;
  els.subtitle.textContent = status.projectRoot || "Vector + AST symbol graph";
  els.statusCard.classList.remove("loading");
  els.statusCard.innerHTML = `
    <div class="title">${escapeHtml(status.projectName || "Project")}</div>
    <div><span class="pill">${escapeHtml(status.status)}</span> · ${escapeHtml(status.ragMode)}</div>
    <div>${status.vectorCount.toLocaleString()} vectors · dim ${status.vectorDimension}</div>
    <div>${status.symbolNodes.toLocaleString()} AST nodes · ${status.symbolEdges.toLocaleString()} edges</div>
    <div>${status.indexedFiles.toLocaleString()} files · ${status.totalChunks.toLocaleString()} chunks</div>
  `;
}

function renderLegend(folders) {
  state.folderColors.clear();
  folders.forEach((item) => state.folderColors.set(item.name, item.color));
  els.legendList.innerHTML = folders.map((item) => `
    <li>
      <span class="swatch" style="color:${item.color}; background:${item.color}"></span>
      <span>${escapeHtml(item.name)} (${item.count})</span>
    </li>
  `).join("");
}

function folderColor(folder) {
  return state.folderColors.get(folder) || "#7aa2ff";
}

function renderSelection(node, kind) {
  if (!node) {
    els.selectionCard.innerHTML = `<p class="muted">Click a node to inspect code context.</p>`;
    return;
  }

  if (kind === "vector") {
    els.selectionCard.innerHTML = `
      <div class="path">${escapeHtml(node.filePath)}</div>
      <div>${escapeHtml(node.chunkType)} · ${escapeHtml(node.symbol || "—")}</div>
      <div class="muted">lines ${node.startLine}-${node.endLine}</div>
      <div class="muted">folder: ${escapeHtml(node.folder)}</div>
    `;
    return;
  }

  els.selectionCard.innerHTML = `
    <div class="path">${escapeHtml(node.label)}</div>
    <div>${escapeHtml(node.type)}</div>
    <div class="muted">${escapeHtml(node.path || node.id)}</div>
  `;
}

function resizeCanvas() {
  const canvas = els.vectorCanvas;
  const rect = canvas.parentElement.getBoundingClientRect();
  const ratio = window.devicePixelRatio || 1;
  canvas.width = Math.floor(rect.width * ratio);
  canvas.height = Math.floor(rect.height * ratio);
  canvas.style.width = `${rect.width}px`;
  canvas.style.height = `${rect.height}px`;
  drawVectorMap();
}

function mapPoint(node) {
  const canvas = els.vectorCanvas;
  const margin = 48 * (window.devicePixelRatio || 1);
  const w = canvas.width - margin * 2;
  const h = canvas.height - margin * 2;
  const x = margin + node.x * w;
  const y = margin + node.y * h;
  return applyTransform(x, y);
}

function applyTransform(x, y) {
  const t = state.transform;
  return {
    x: x * t.scale + t.x,
    y: y * t.scale + t.y
  };
}

function drawVectorMap() {
  const data = state.vectorData;
  const canvas = els.vectorCanvas;
  if (!data || !canvas.width) {
    return;
  }

  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.save();
  ctx.lineWidth = 0.8 * (window.devicePixelRatio || 1);

  data.edges.forEach(([a, b]) => {
    const na = data.nodes[a];
    const nb = data.nodes[b];
    if (!na || !nb) {
      return;
    }
    const pa = mapPoint(na);
    const pb = mapPoint(nb);
    ctx.strokeStyle = "rgba(94, 231, 255, 0.12)";
    ctx.beginPath();
    ctx.moveTo(pa.x, pa.y);
    ctx.lineTo(pb.x, pb.y);
    ctx.stroke();
  });

  data.nodes.forEach((node) => {
    const p = mapPoint(node);
    const selected = state.selectedId === node.id;
    const radius = (selected ? 7 : 4.5) * (window.devicePixelRatio || 1);
    ctx.beginPath();
    ctx.fillStyle = folderColor(node.folder);
    ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
    ctx.fill();
    if (selected) {
      ctx.strokeStyle = "#ffffff";
      ctx.lineWidth = 2 * (window.devicePixelRatio || 1);
      ctx.stroke();
    }
  });

  ctx.restore();
  els.hudStats.textContent = `${data.count.toLocaleString()} vectors · ${data.edgeCount.toLocaleString()} semantic links · zoom ${state.transform.scale.toFixed(2)}x`;
}

function hitTestVector(clientX, clientY) {
  const data = state.vectorData;
  if (!data) {
    return null;
  }
  const rect = els.vectorCanvas.getBoundingClientRect();
  const ratio = window.devicePixelRatio || 1;
  const x = (clientX - rect.left) * ratio;
  const y = (clientY - rect.top) * ratio;
  let best = null;
  let bestDist = 16 * ratio;

  data.nodes.forEach((node) => {
    const p = mapPoint(node);
    const dx = p.x - x;
    const dy = p.y - y;
    const dist = Math.hypot(dx, dy);
    if (dist < bestDist) {
      bestDist = dist;
      best = node;
    }
  });

  return best;
}

function renderGraph() {
  const data = state.graphData;
  const svg = d3.select(els.graphSvg);
  svg.selectAll("*").remove();

  if (!data || data.nodes.length === 0) {
    els.hudStats.textContent = "No AST graph yet — re-index the project.";
    return;
  }

  const rect = els.graphSvg.getBoundingClientRect();
  const width = rect.width;
  const height = rect.height;
  svg.attr("viewBox", `0 0 ${width} ${height}`);

  const color = d3.scaleOrdinal()
    .domain(["Function", "Component", "Route", "Hook", "Api", "File"])
    .range(["#5ee7ff", "#a78bfa", "#6dffb0", "#ffd166", "#ff7b9c", "#7aa2ff"]);

  const nodes = data.nodes.map((node) => ({ ...node }));
  const links = data.edges.map((edge) => ({ source: edge.from, target: edge.to, type: edge.type }));

  if (state.graphSimulation) {
    state.graphSimulation.stop();
  }

  state.graphSimulation = d3.forceSimulation(nodes)
    .force("link", d3.forceLink(links).id((d) => d.id).distance(48).strength(0.35))
    .force("charge", d3.forceManyBody().strength(-120))
    .force("center", d3.forceCenter(width / 2, height / 2))
    .force("collision", d3.forceCollide(14));

  const g = svg.append("g");
  const link = g.append("g")
    .selectAll("line")
    .data(links)
    .join("line")
    .attr("class", "graph-link");

  const node = g.append("g")
    .selectAll("g")
    .data(nodes)
    .join("g")
    .attr("class", "graph-node")
    .call(d3.drag()
      .on("start", (event, d) => {
        if (!event.active) {
          state.graphSimulation.alphaTarget(0.25).restart();
        }
        d.fx = d.x;
        d.fy = d.y;
      })
      .on("drag", (event, d) => {
        d.fx = event.x;
        d.fy = event.y;
      })
      .on("end", (event, d) => {
        if (!event.active) {
          state.graphSimulation.alphaTarget(0);
        }
        d.fx = null;
        d.fy = null;
      }));

  node.append("circle")
    .attr("r", 7)
    .attr("fill", (d) => color(d.type));

  node.append("text")
    .attr("x", 10)
    .attr("y", 4)
    .text((d) => d.label.length > 28 ? `${d.label.slice(0, 28)}…` : d.label);

  node.on("click", (_, d) => {
    state.selectedId = d.id;
    renderSelection(d, "graph");
  });

  const zoom = d3.zoom()
    .scaleExtent([0.2, 4])
    .on("zoom", (event) => g.attr("transform", event.transform));

  svg.call(zoom);

  state.graphSimulation.on("tick", () => {
    link
      .attr("x1", (d) => d.source.x)
      .attr("y1", (d) => d.source.y)
      .attr("x2", (d) => d.target.x)
      .attr("y2", (d) => d.target.y);
    node.attr("transform", (d) => `translate(${d.x},${d.y})`);
  });

  els.hudStats.textContent = `${data.nodeCount.toLocaleString()} AST nodes · ${data.edgeCount.toLocaleString()} edges`;
}

async function loadAll() {
  setLoading(true, "Loading brain status…");
  try {
    const status = await api("/api/status");
    renderStatus(status);

    setLoading(true, "Loading neural map…");
    const vectors = await api("/api/vectors");
    state.vectorData = vectors;
    renderLegend(vectors.folders || []);
    resizeCanvas();

    setLoading(true, "Loading AST graph…");
    state.graphData = await api("/api/graph");
    if (state.view === "graph") {
      renderGraph();
    }
  } catch (error) {
    els.statusCard.classList.remove("loading");
    els.statusCard.innerHTML = `<div class="title" style="color:var(--danger)">${escapeHtml(error.message)}</div>`;
  } finally {
    setLoading(false);
  }
}

function switchView(view) {
  state.view = view;
  document.querySelectorAll(".tab").forEach((tab) => {
    tab.classList.toggle("active", tab.dataset.view === view);
  });
  els.vectorCanvas.classList.toggle("active", view === "vectors");
  els.graphSvg.classList.toggle("active", view === "graph");
  if (view === "vectors") {
    resizeCanvas();
  } else {
    renderGraph();
  }
}

function resetView() {
  state.transform = { x: 0, y: 0, scale: 1 };
  if (state.view === "vectors") {
    drawVectorMap();
  } else {
    renderGraph();
  }
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

document.querySelectorAll(".tab").forEach((tab) => {
  tab.addEventListener("click", () => switchView(tab.dataset.view));
});

els.reloadBtn.addEventListener("click", async () => {
  setLoading(true, "Refreshing brain…");
  try {
    await api("/api/reload", { method: "POST" });
    await loadAll();
  } catch (error) {
    els.statusCard.innerHTML = `<div class="title" style="color:var(--danger)">${escapeHtml(error.message)}</div>`;
    setLoading(false);
  }
});
els.resetBtn.addEventListener("click", resetView);
window.addEventListener("resize", () => {
  if (state.view === "vectors") {
    resizeCanvas();
  } else {
    renderGraph();
  }
});

let panning = false;
let panStart = null;

els.vectorCanvas.addEventListener("wheel", (event) => {
  event.preventDefault();
  const factor = event.deltaY < 0 ? 1.12 : 1 / 1.12;
  const rect = els.vectorCanvas.getBoundingClientRect();
  const ratio = window.devicePixelRatio || 1;
  const mx = (event.clientX - rect.left) * ratio;
  const my = (event.clientY - rect.top) * ratio;
  const before = state.transform.scale;
  const after = Math.min(8, Math.max(0.2, before * factor));
  const scaleFactor = after / before;
  state.transform.x = mx - (mx - state.transform.x) * scaleFactor;
  state.transform.y = my - (my - state.transform.y) * scaleFactor;
  state.transform.scale = after;
  drawVectorMap();
}, { passive: false });

els.vectorCanvas.addEventListener("mousedown", (event) => {
  if (event.button !== 0) {
    return;
  }
  panning = true;
  panStart = { x: event.clientX, y: event.clientY, tx: state.transform.x, ty: state.transform.y };
});

window.addEventListener("mousemove", (event) => {
  if (!panning || !panStart) {
    return;
  }
  const ratio = window.devicePixelRatio || 1;
  state.transform.x = panStart.tx + (event.clientX - panStart.x) * ratio;
  state.transform.y = panStart.ty + (event.clientY - panStart.y) * ratio;
  drawVectorMap();
});

window.addEventListener("mouseup", () => {
  panning = false;
  panStart = null;
});

els.vectorCanvas.addEventListener("click", (event) => {
  if (panStart && Math.hypot(event.movementX, event.movementY) > 4) {
    return;
  }
  const node = hitTestVector(event.clientX, event.clientY);
  state.selectedId = node?.id ?? null;
  renderSelection(node, "vector");
  drawVectorMap();
});

els.searchForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const query = els.searchInput.value.trim();
  if (!query) {
    return;
  }
  els.searchResults.innerHTML = `<p class="muted">Searching…</p>`;
  try {
    const response = await api("/api/search", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ query, topK: 10 })
    });
    if (!response.results?.length) {
      els.searchResults.innerHTML = `<p class="muted">No hits.</p>`;
      return;
    }
    els.searchResults.innerHTML = response.results.map((hit) => `
      <div class="hit" data-path="${escapeHtml(hit.filePath)}">
        <div><span class="score">${hit.score.toFixed(3)}</span> ${escapeHtml(hit.symbol || hit.chunkType)}</div>
        <div class="path">${escapeHtml(hit.filePath)}</div>
        <div class="muted">${escapeHtml(hit.contentPreview || "")}</div>
      </div>
    `).join("");
  } catch (error) {
    els.searchResults.innerHTML = `<p class="muted">${escapeHtml(error.message)}</p>`;
  }
});

loadAll();
