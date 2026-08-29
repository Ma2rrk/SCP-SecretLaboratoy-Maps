(() => {
  'use strict';

  // ---------------- 视觉常量 ----------------

  const PAPER = '#f4f4f0';
  const INK = '#191a1d';
  const LABEL_ON_INK = '#ffffff';
  const PATH_COLOR = '#e8590c';
  const PATH_GLOW = 'rgba(232,89,12,.20)';

  const CAT_COLOR = {
    scp: '#ff6b6b', core: '#ffb238', security: '#6aa8ff',
    checkpoint: '#2bd696', personnel: '#c39bff', utility: '#68d6e8',
    deadend: '#b5794e', corridor: '#6b7280',
  };

  const ARM_W = 0.44;
  const BODY_W = 0.88;
  const BODY_H = 0.62;
  const GAP_UNITS = 30;

  const CELL = 52;
  const PX_PER_UNIT = CELL / 15;
  const MIN_SCALE = PX_PER_UNIT * 0.08;
  const MAX_SCALE = PX_PER_UNIT * 12;
  const PIP_ROOM_LABEL_SCALE = 1.5;

  // 导航代价：相邻房间中心相距 1 格 = 15 米。
  // 电梯换乘的代价拆成两部分（由 api.php 的 links 提供，这里只是兜底默认值）：
  //   walk 电梯房间内走到轿厢的额外路程（重收容侧约 1 个房间、轻收容侧约 2 个房间）
  //   ride 与角色速度无关的固定乘梯时间（秒）
  // 寻路按「步行米数」最小化；ride 是每次换乘的常数，不影响 A/B 电梯的取舍，
  // 只计入最终耗时。
  const STEP_COST = 15;
  const ELEVATOR_WALK = 45;
  const ELEVATOR_RIDE = 8;

  // 移动速度（m/s），用于估算通过时间
  const SPEEDS = [
    { key: 'sneak', label: '潜行', v: 1.6 },
    { key: 'walk', label: '行走', v: 3.9 },
    { key: 'sprint', label: '冲刺', v: 5.4 },
    { key: 'scp173', label: 'SCP-173', v: 7.3 },
    { key: 'scp173b', label: '173 技能', v: 12.4 },
    { key: 'scp096', label: '096 狂暴', v: 8.0 },
  ];

  function fmtTime(sec) {
    sec = Math.round(sec);
    if (sec < 60) return sec + ' 秒';
    return Math.floor(sec / 60) + ' 分 ' + String(sec % 60).padStart(2, '0') + ' 秒';
  }

  const VERSION = (window.__INITIAL__ || {}).version || 0;
  const HISTORY_KEY = 'slmaps_seed_history';
  const OPTS_KEY = 'slmaps_options';
  const PIP_OPTS_KEY = 'slmaps_pip_options';
  const SIDEBAR_KEY = 'slmaps_sidebar_collapsed';
  const CACHE_PREFIX = 'slmaps:map:v' + VERSION + ':';
  const CACHE_INDEX = 'slmaps:cacheidx:v' + VERSION;
  const CACHE_MAX = 40;

  const $ = (id) => document.getElementById(id);

  const el = {
    app: $('app'), sidebar: $('sidebar'), sidebarToggle: $('sidebarToggle'),
    sidebarBackdrop: $('sidebarBackdrop'),
    seedInput: $('seedInput'), loadBtn: $('loadBtn'), randomBtn: $('randomBtn'),
    seedHistory: $('seedHistory'),
    navWrap: $('navWrap'), navFrom: $('navFrom'), navTo: $('navTo'), navWaypoints: $('navWaypoints'),
    navResult: $('navResult'), navHint: $('navHint'),
    positionWrap: $('positionWrap'), posX: $('posX'), posY: $('posY'), posZ: $('posZ'),
    locateBtn: $('locateBtn'), clearPositionBtn: $('clearPositionBtn'), positionInfo: $('positionInfo'),
    jumpWrap: $('jumpWrap'), jumpTabs: $('jumpTabs'),
    searchWrap: $('searchWrap'), searchInput: $('searchInput'), searchResults: $('searchResults'),
    optionsWrap: $('optionsWrap'), optCodes: $('optCodes'), optNames: $('optNames'),
    optMarks: $('optMarks'), optSpawns: $('optSpawns'), spawnRow: $('spawnRow'),
    spawnLegendWrap: $('spawnLegendWrap'), spawnLegend: $('spawnLegend'),
    legendWrap: $('legendWrap'), legend: $('legend'),
    emptyState: $('emptyState'), loadingState: $('loadingState'), errorState: $('errorState'),
    canvasWrap: $('canvasWrap'), canvas: $('mapCanvas'), cacheBadge: $('cacheBadge'),
    roomPanel: $('roomPanel'),
    clearCacheBtn: $('clearCacheBtn'),
    pipWrap: $('pipWrap'), pipMap: $('pipMap'), pipSidebar: $('pipSidebar'),
    pipSeed: $('pipSeed'), pipPosition: $('pipPosition'),
    pipNavigation: $('pipNavigation'), pipSelection: $('pipSelection'), pipBtn: $('pipBtn'),
  };

  const ctx = el.canvas.getContext('2d');

  const state = {
    seed: null,
    data: null,
    rooms: [],
    spawns: [],
    byId: new Map(),
    labels: [],
    bbox: { core: null, light: null, all: null },
    hidden: new Set(),
    hiddenSpawns: new Set(),
    selected: null,
    selectedSpawn: null,
    hover: null,
    hoverSpawn: null,
    nav: { from: null, vias: [], to: null, path: null },
    position: null,
    lightShift: { dx: 0, dz: 0 },
    opts: { codes: false, names: true, marks: true, spawns: true },
    view: { x: 0, y: 0, scale: PX_PER_UNIT },
    dpr: Math.max(1, window.devicePixelRatio || 1),
    dragging: false, moved: false, lastPointer: null,
  };

  let pipWindow = null;
  let pipMapTimer = null;
  let pipDragging = false;
  let pipPointerId = null;
  let pipLastPointer = null;
  let loadController = null;
  let loadRequestId = 0;
  const desktopSidebar = window.matchMedia('(min-width: 861px)');
  let sidebarCollapsed = sidebarPreference();

  // ---------------- 工具 ----------------

  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

  function sidebarPreference() {
    try { return localStorage.getItem(SIDEBAR_KEY) === '1'; }
    catch (_) { return false; }
  }

  function saveSidebarPreference(collapsed) {
    try { localStorage.setItem(SIDEBAR_KEY, collapsed ? '1' : '0'); }
    catch (_) {}
  }

  function syncSidebar() {
    const desktop = desktopSidebar.matches;
    if (desktop) {
      el.sidebar.classList.remove('open');
      el.app.classList.remove('sidebar-mobile-open');
      el.app.classList.toggle('sidebar-collapsed', sidebarCollapsed);
    } else {
      el.app.classList.remove('sidebar-collapsed');
    }

    const expanded = desktop
      ? !el.app.classList.contains('sidebar-collapsed')
      : el.sidebar.classList.contains('open');
    el.app.classList.toggle('sidebar-mobile-open', !desktop && expanded);
    el.sidebarToggle.setAttribute('aria-expanded', String(expanded));
    el.sidebarToggle.setAttribute('aria-label', expanded ? '折叠侧栏' : '展开侧栏');
    el.sidebarToggle.title = expanded ? '折叠侧栏' : '展开侧栏';
    el.sidebarToggle.textContent = expanded ? (desktop ? '‹' : '×') : '☰';
    el.sidebar.toggleAttribute('inert', !expanded);
    el.sidebar.setAttribute('aria-hidden', String(!expanded));
  }

  function toggleSidebar() {
    if (desktopSidebar.matches) {
      sidebarCollapsed = !el.app.classList.contains('sidebar-collapsed');
      saveSidebarPreference(sidebarCollapsed);
    } else {
      el.sidebar.classList.toggle('open');
    }
    syncSidebar();
  }

  function closeMobileSidebar() {
    if (desktopSidebar.matches) return;
    const restoreFocus = el.sidebar.contains(document.activeElement);
    el.sidebar.classList.remove('open');
    syncSidebar();
    if (restoreFocus) el.sidebarToggle.focus();
  }
  const worldToScreen = (rx, rz) => ({
    x: el.canvasWrap.clientWidth - (state.view.x + rx * state.view.scale),
    y: state.view.y - rz * state.view.scale,
  });
  const screenToWorld = (sx, sy) => ({
    rx: (el.canvasWrap.clientWidth - sx - state.view.x) / state.view.scale,
    rz: -(sy - state.view.y) / state.view.scale,
  });
  function zoomViewAt(sx, sy, factor) {
    const before = screenToWorld(sx, sy);
    state.view.scale = clamp(state.view.scale * factor, MIN_SCALE, MAX_SCALE);
    const after = worldToScreen(before.rx, before.rz);
    state.view.x += sx - after.x;
    state.view.y += sy - after.y;
    render();
  }
  const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const zoneLabelOf = (z) =>
    ({ LightContainment: '轻收容区', HeavyContainment: '重收容区', Entrance: '办公区' }[z] || z);
  const roomTitle = (r) => r.short || (r.code ? r.code : r.label);

  // ---------------- 浏览器端缓存 ----------------

  function cacheGet(seed) {
    try {
      const raw = localStorage.getItem(CACHE_PREFIX + seed);
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  }

  function cachePut(seed, json) {
    let idx;
    try { idx = JSON.parse(localStorage.getItem(CACHE_INDEX) || '[]'); } catch { idx = []; }
    if (!Array.isArray(idx)) idx = [];
    idx = idx.filter((s) => s !== seed);
    idx.push(seed);
    const payload = JSON.stringify(json);
    for (let attempt = 0; attempt < 8; attempt++) {
      try {
        localStorage.setItem(CACHE_PREFIX + seed, payload);
        while (idx.length > CACHE_MAX) localStorage.removeItem(CACHE_PREFIX + idx.shift());
        localStorage.setItem(CACHE_INDEX, JSON.stringify(idx));
        return;
      } catch {
        // 配额不足：淘汰最久未使用的一条后重试
        if (idx.length <= 1) { try { localStorage.removeItem(CACHE_PREFIX + seed); } catch {} return; }
        try { localStorage.removeItem(CACHE_PREFIX + idx.shift()); } catch {}
      }
    }
  }

  function cacheClear() {
    const keys = [];
    try {
      for (let i = 0; i < localStorage.length; i++) {
        const k = localStorage.key(i);
        if (k && (k.startsWith('slmaps:map:') || k.startsWith('slmaps:cacheidx:'))) keys.push(k);
      }
      keys.forEach((k) => localStorage.removeItem(k));
    } catch {
      return 0;
    }
    return keys.length;
  }

  function showBadge(text) {
    if (!el.cacheBadge) return;
    el.cacheBadge.textContent = text;
    el.cacheBadge.hidden = false;
    clearTimeout(showBadge._t);
    showBadge._t = setTimeout(() => { el.cacheBadge.hidden = true; }, 2600);
  }

  // ---------------- 数据整形 ----------------
  //
  // 与 slmaps 相同的坐标变换：
  //   重收容 / 办公区：screenX = -x, screenY = +z（整体旋转 180°，办公区在左）
  //   轻收容区：      screenX = +x, screenY = -z，再整体平移到核心区正上方

  // 战利品刷新点的类型表。
  // 一个结构点位往往同时兼容多种结构（例如「电板 + 壁柜 + 实验武器柜」三选一），
  // 所以圆点按 prio 最高（最稀有）的那一种上色，完整候选列表在详情卡里给出。
  const SPAWN_TYPES = {
    ExperimentalWeaponLocker: { label: '实验武器柜', color: '#ff4d4d', prio: 8 },
    ScpPedestal:              { label: 'SCP 物品柜', color: '#2bd696', prio: 7 },
    LargeGunLocker:           { label: '枪械柜',     color: '#ff922b', prio: 6 },
    Scp079Generator:          { label: '电板',       color: '#ffd43b', prio: 5 },
    Workstation:              { label: '改枪台',     color: '#c39bff', prio: 4 },
    StandardLocker:           { label: '储物柜',     color: '#4dabf7', prio: 3 },
    SmallWallCabinet:         { label: '壁柜',       color: '#adb5bd', prio: 2 },
    __item:                   { label: '散落物品',   color: '#68d6e8', prio: 1 },
  };

  // 结构 / 物品 / 刷新点组件的中文名（未收录的直接显示原名）
  const TYPE_NAME = {
    // 结构（与图例 SPAWN_TYPES 保持一致）
    StandardLocker: '储物柜', LargeGunLocker: '枪械柜', SmallWallCabinet: '壁柜',
    ExperimentalWeaponLocker: '实验武器柜', ScpPedestal: 'SCP 物品柜',
    Scp079Generator: '电板', Workstation: '改枪台',
    // 物品
    Adrenaline: '肾上腺素', Medkit: '医疗包', Radio: '对讲机', Flashlight: '手电筒', Coin: '硬币',
    Ammo9x19: '9x19 弹药', Ammo556x45: '5.56x45 弹药',
    ArmorCombat: '战斗护甲', ArmorHeavy: '重型护甲',
    GrenadeHE: '高爆手雷', GrenadeFlash: '闪光弹',
    GunCOM15: 'COM-15', GunCOM18: 'COM-18', GunCrossvec: 'Crossvec',
    GunFSP9: 'FSP-9', GunRevolver: '左轮手枪',
    KeycardGuard: '警卫钥匙卡', KeycardJanitor: '清洁工钥匙卡', KeycardScientist: '科学家钥匙卡',
    KeycardMTFOperative: 'MTF 队员钥匙卡', KeycardZoneManager: '区域主管钥匙卡',
    KeycardFacilityManager: '设施主管钥匙卡', SurfaceAccessPass: '地表通行证',
    // 刷新点组件
    PredefinedItemSpawnpoint: '固定物品点', RandomItemSpawnpoint: '随机物品点',
    RandomItemGroupSpawnpoint: '随机物品组', SinkholeSpawnpoint: '天坑点',
  };
  const typeName = (t) => TYPE_NAME[t] || t;

  function prepareSpawns(json) {
    const list = json.spawns || [];
    for (const s of list) {
      if (s.g === 'light') { s.rx = s.x; s.rz = s.z; }
      else { s.rx = -s.x; s.rz = -s.z; }

      // 该点位可能变成的所有类型；物品点位统一归为「散落物品」
      s.keys = s.k === 'item'
        ? ['__item']
        : (s.t || []).filter((t) => SPAWN_TYPES[t]);
      if (!s.keys.length) s.keys = ['__item'];

      const top = s.keys.reduce((a, b) => (SPAWN_TYPES[a].prio >= SPAWN_TYPES[b].prio ? a : b));
      s.color = SPAWN_TYPES[top].color;
      s.tag = SPAWN_TYPES[top].label;
    }
    return list;
  }

  const spawnVisible = (s) => s.keys.some((k) => !state.hiddenSpawns.has(k));

  function prepareRooms(json) {
    const hcz = json.zones.HeavyContainment;
    const ez = json.zones.Entrance;
    const light = json.zones.LightContainment;
    const core = [...hcz, ...ez];

    for (const r of core) {
      r.rx = -r.x; r.rz = -r.z;
      r.rconn = r.conn.map((c) => ({ dx: -c.dx, dz: -c.dz }));
    }
    for (const r of light) {
      r.rx = r.x; r.rz = r.z;
      r.rconn = r.conn.map((c) => ({ dx: c.dx, dz: c.dz }));
    }

    const coreBox = bboxOf(core);
    const rawLight = bboxOf(light);
    // 对齐到核心区正上方；平移量取整到整格，保证相邻判定始终精确
    const dx = Math.round((coreBox.cx - rawLight.cx) / 15) * 15;
    const dz = Math.round(((coreBox.maxZ + GAP_UNITS) - rawLight.minZ) / 15) * 15;
    for (const r of light) { r.rx += dx; r.rz += dz; }

    const all = [...core, ...light];
    return {
      all, coreBox, lightShift: { dx, dz },
      hczBox: bboxOf(hcz), ezBox: bboxOf(ez),
      lightBox: bboxOf(light), allBox: bboxOf(all),
    };
  }

  function bboxOf(rooms) {
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const r of rooms) {
      minX = Math.min(minX, r.rx); maxX = Math.max(maxX, r.rx);
      minZ = Math.min(minZ, r.rz); maxZ = Math.max(maxZ, r.rz);
    }
    return { minX, maxX, minZ, maxZ, cx: (minX + maxX) / 2, cz: (minZ + maxZ) / 2 };
  }

  // ---------------- 导航图 ----------------

  const cellKey = (r, dx, dz) =>
    r.group + ':' + Math.round(r.rx / 15 + (dx || 0)) + ',' + Math.round(r.rz / 15 + (dz || 0));

  function buildGraph() {
    const byKey = new Map();
    for (const r of state.rooms) { r.adj = []; byKey.set(cellKey(r), r); }
    for (const r of state.rooms) {
      for (const c of r.rconn) {
        const n = byKey.get(cellKey(r, c.dx, c.dz));
        if (n) r.adj.push({ to: n, w: STEP_COST, kind: 'walk' });
      }
    }
    for (const l of (state.data.links || [])) {
      const a = state.byId.get(l.a), b = state.byId.get(l.b);
      if (!a || !b) continue;
      const walk = l.walk != null ? l.walk : ELEVATOR_WALK;
      const ride = l.ride != null ? l.ride : ELEVATOR_RIDE;
      const edge = { w: walk, ride, kind: 'elevator', label: l.label };
      a.adj.push({ ...edge, to: b });
      b.adj.push({ ...edge, to: a });
    }
  }

  /**
   * 全图 Dijkstra。所有楼层、所有电梯都在同一张图里，
   * 因此选出的一定是「从起点到终点的全程最短」路线，
   * 而不是「先走到最近的电梯」这种局部最优。
   */
  function findPath(from, to) {
    if (!from || !to) return null;
    if (from === to) return { rooms: [from], edges: [], cost: 0 };

    const dist = new Map(), prev = new Map(), done = new Set();
    for (const r of state.rooms) dist.set(r, Infinity);
    dist.set(from, 0);

    while (true) {
      let u = null, best = Infinity;
      for (const r of state.rooms) {
        if (done.has(r)) continue;
        const d = dist.get(r);
        if (d < best) { best = d; u = r; }
      }
      if (!u) break;
      if (u === to) break;
      done.add(u);
      for (const e of u.adj) {
        if (done.has(e.to)) continue;
        const nd = best + e.w;
        if (nd < dist.get(e.to)) { dist.set(e.to, nd); prev.set(e.to, { from: u, edge: e }); }
      }
    }

    if (!isFinite(dist.get(to))) return null;
    const rooms = [], edges = [];
    let cur = to;
    while (cur !== from) {
      const p = prev.get(cur);
      if (!p) return null;
      rooms.push(cur); edges.push(p.edge);
      cur = p.from;
    }
    rooms.push(from);
    rooms.reverse(); edges.reverse();
    return { rooms, edges, cost: dist.get(to) };
  }

  /** 将起点→多个途径点→终点的各段最短路径按指定顺序拼成一条路线。 */
  function findRoute(from, vias, to) {
    if (!from || !to) return null;
    const stops = [from, ...(vias || []), to];
    const rooms = [];
    const edges = [];
    const waypointIndices = [];
    let cost = 0;
    for (let i = 0; i < stops.length - 1; i++) {
      const segment = findPath(stops[i], stops[i + 1]);
      if (!segment) return null;
      if (!rooms.length) rooms.push(...segment.rooms);
      else rooms.push(...segment.rooms.slice(1));
      edges.push(...segment.edges);
      cost += segment.cost;
      if (i < stops.length - 2) waypointIndices.push(rooms.length - 1);
    }
    return { rooms, edges, cost, waypointIndices };
  }

  // ---------------- 转向指引 ----------------
  //
  // 渲染坐标是真实世界 XZ 平面的纯旋转（轻收容恒等、重收容/办公整体转 180°），
  // 没有做过镜像，所以左右手性与游戏内一致，可以直接在渲染坐标里判断左右。
  // 玩家朝向 = 上一段的前进方向，每转一次弯朝向就跟着变，因此指引全部是相对的，
  // 只有出发和出电梯这两个「朝向未知」的时刻才退回用地图方位提示。

  const MAP_HINT = { '0,1': '地图上方', '0,-1': '地图下方', '1,0': '地图右方', '-1,0': '地图左方' };

  function dirOf(a, b) {
    const dx = Math.round((b.rx - a.rx) / 15);
    const dz = Math.round((b.rz - a.rz) / 15);
    return { dx, dz };
  }
  const sameDir = (a, b) => a.dx === b.dx && a.dz === b.dz;

  function turnOf(facing, dir) {
    if (sameDir(facing, dir)) return '继续直行';
    if (facing.dx === -dir.dx && facing.dz === -dir.dz) return '掉头';
    // 二维叉积 z 分量：> 0 表示 dir 在 facing 的逆时针侧，也就是玩家的左手边
    return (facing.dx * dir.dz - facing.dz * dir.dx) > 0 ? '左转' : '右转';
  }

  /** 把路径压成若干「转向 + 直行 N 段」的腿 */
  function buildItinerary(path) {
    const legs = [];
    const { rooms, edges } = path;
    const waypointOrders = new Map((path.waypointIndices || []).map((index, order) => [index, order + 1]));
    let facing = null;
    let i = 0;

    while (i < edges.length) {
      if (edges[i].kind === 'elevator') {
        legs.push({ kind: 'elevator', edge: edges[i], room: rooms[i + 1], waypoint: waypointOrders.get(i + 1) || 0 });
        facing = null; // 出电梯后朝向不确定
        i++;
        continue;
      }

      const dir = dirOf(rooms[i], rooms[i + 1]);
      const turn = facing ? turnOf(facing, dir) : null;
      const start = i;
      let n = 0;
      while (i < edges.length && edges[i].kind === 'walk' && sameDir(dirOf(rooms[i], rooms[i + 1]), dir)) {
        n++;
        i++;
        if (waypointOrders.has(i)) break;
      }
      // 途经点只取这一腿中间的房间，不含本腿终点（终点单独显示）
      const via = [];
      let allStraight = true;
      for (let k = start + 1; k < i; k++) {
        if (rooms[k].short) via.push(rooms[k].short);
        if (rooms[k].shape !== 'Straight') allStraight = false;
      }
      facing = dir;
      legs.push({
        kind: 'walk',
        turn,
        n,
        via,
        from: rooms[start],
        room: rooms[i],
        waypoint: waypointOrders.get(i) || 0,
        // 中途全是直走廊 = 这一段没有岔路，可以「一直走到头」
        straightRun: n >= 2 && allStraight,
        hint: turn === null ? MAP_HINT[dir.dx + ',' + dir.dz] : null,
      });
    }
    return legs;
  }

  /** 转弯处的说法随所在房间的形状变化，尽量贴近玩家在现场看到的样子 */
  function turnPhrase(leg) {
    const shape = leg.from ? leg.from.shape : null;
    if (leg.turn === '掉头') return '原路掉头';
    if (shape === 'XShape') return '在十字路口' + leg.turn;
    if (shape === 'TShape') return '在 T 形路口' + leg.turn;
    if (shape === 'Curve') return '顺着弯道' + leg.turn;
    if (leg.from && leg.from.short) return '在' + leg.from.short + leg.turn;
    return leg.turn;
  }

  function legText(leg, isLast) {
    if (leg.kind === 'elevator') {
      // 轻收容在上、重收容在下
      const up = leg.room.zone === 'LightContainment';
      return '乘 ' + (leg.edge.label || '电梯') + (up ? ' 上到' : ' 下到') + zoneLabelOf(leg.room.zone);
    }

    const body = leg.straightRun
      ? `一直走到头（${leg.n} 段走廊）`
      : `走 ${leg.n} 段走廊`;

    let head;
    if (leg.turn) head = turnPhrase(leg) + '，';
    else if (leg.hint) {
      // 起点只有一个可离开的方向时，地图方位不会帮助玩家决策；
      // 直接告诉他从唯一出口出去即可。
      const exitCount = leg.from && leg.from.adj ? leg.from.adj.length : 0;
      head = exitCount <= 1 ? '从唯一出口出去后直行，' : '朝' + leg.hint + '，';
    }
    else head = '';

    return head + body + (isLast ? '，即到终点' : '');
  }

  function recomputePath() {
    state.nav.path = findRoute(state.nav.from, state.nav.vias, state.nav.to);
    renderNavPanel();
    render();
  }

  // ---------------- 路径基元 ----------------

  function roundRectPath(c, x, y, w, h, r) {
    r = Math.min(r, w / 2, h / 2);
    c.beginPath();
    c.moveTo(x + r, y);
    c.arcTo(x + w, y, x + w, y + h, r);
    c.arcTo(x + w, y + h, x, y + h, r);
    c.arcTo(x, y + h, x, y, r);
    c.arcTo(x, y, x + w, y, r);
    c.closePath();
  }

  function octagonPath(c, cx, cy, w, h, cut) {
    const x0 = cx - w / 2, x1 = cx + w / 2, y0 = cy - h / 2, y1 = cy + h / 2;
    const k = Math.min(cut, w / 3, h / 3);
    c.beginPath();
    c.moveTo(x0 + k, y0); c.lineTo(x1 - k, y0); c.lineTo(x1, y0 + k);
    c.lineTo(x1, y1 - k); c.lineTo(x1 - k, y1); c.lineTo(x0 + k, y1);
    c.lineTo(x0, y1 - k); c.lineTo(x0, y0 + k);
    c.closePath();
  }

  function hexPath(c, cx, cy, w, h) {
    const x0 = cx - w / 2, x1 = cx + w / 2, y0 = cy - h / 2, y1 = cy + h / 2;
    const k = w * 0.2;
    c.beginPath();
    c.moveTo(x0 + k, y0); c.lineTo(x1 - k, y0); c.lineTo(x1, cy);
    c.lineTo(x1 - k, y1); c.lineTo(x0 + k, y1); c.lineTo(x0, cy);
    c.closePath();
  }

  /** 死路：梯形，窄的一边朝外，直观表达「走进去出不来」 */
  function deadPath(c, cx, cy, w, h) {
    const x0 = cx - w / 2, x1 = cx + w / 2, y0 = cy - h / 2, y1 = cy + h / 2;
    const k = w * 0.16;
    c.beginPath();
    c.moveTo(x0 + k, y0); c.lineTo(x1 - k, y0);
    c.lineTo(x1, y1); c.lineTo(x0, y1);
    c.closePath();
  }

  function shieldPath(c, cx, cy, w, h) {
    const x0 = cx - w / 2, x1 = cx + w / 2, y0 = cy - h / 2, y1 = cy + h / 2;
    const k = h * 0.34;
    c.beginPath();
    c.moveTo(x0, y0 + k); c.lineTo(x0 + k, y0); c.lineTo(x1 - k, y0);
    c.lineTo(x1, y0 + k); c.lineTo(x1, y1); c.lineTo(x0, y1);
    c.closePath();
  }

  function bodyPath(c, r, cx, cy, cell, grow) {
    grow = grow || 0;
    const w = BODY_W * cell + grow * 2;
    const h = BODY_H * cell + grow * 2;
    switch (r.glyph) {
      case 'circle':
        c.beginPath(); c.ellipse(cx, cy, 0.42 * cell + grow, 0.42 * cell + grow, 0, 0, Math.PI * 2); c.closePath();
        break;
      case 'octagon': octagonPath(c, cx, cy, w, h, cell * 0.16); break;
      case 'hex': hexPath(c, cx, cy, w, h); break;
      case 'shield': shieldPath(c, cx, cy, w, h); break;
      case 'dead': deadPath(c, cx, cy, w, h); break;
      case 'gate': roundRectPath(c, cx - w / 2, cy - h / 2, w, h, cell * 0.04); break;
      default: roundRectPath(c, cx - w / 2, cy - h / 2, w, h, cell * 0.11); break;
    }
  }

  const hasBody = (r) => r.glyph !== 'corridor' && r.glyph !== 'pass';

  // ---------------- 渲染 ----------------

  function resizeCanvas() {
    const rect = el.canvasWrap.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    state.dpr = Math.max(1, window.devicePixelRatio || 1);
    el.canvas.width = Math.round(rect.width * state.dpr);
    el.canvas.height = Math.round(rect.height * state.dpr);
    el.canvas.style.width = rect.width + 'px';
    el.canvas.style.height = rect.height + 'px';
    render();
  }

  function render() {
    const rect = el.canvasWrap.getBoundingClientRect();
    ctx.setTransform(state.dpr, 0, 0, state.dpr, 0, 0);
    ctx.fillStyle = PAPER;
    ctx.fillRect(0, 0, rect.width, rect.height);
    if (!state.rooms.length) return;

    const cell = 15 * state.view.scale;
    const arm = ARM_W * cell;
    const visible = state.rooms.filter((r) => !state.hidden.has(r.category));

    // 1) 剪影：走廊臂 + 房间体，同色实心并集
    ctx.fillStyle = INK;
    for (const r of visible) {
      const p = worldToScreen(r.rx, r.rz);
      for (const c of r.rconn) {
        const len = cell / 2 + 0.6;
        const w = c.dx !== 0 ? len : arm;
        const h = c.dz !== 0 ? len : arm;
        const x = p.x + (c.dx > 0 ? 0 : c.dx < 0 ? -len : -arm / 2);
        const y = p.y + (c.dz > 0 ? -len : c.dz < 0 ? 0 : -arm / 2);
        ctx.fillRect(x, y, w, h);
      }
      if (hasBody(r)) { bodyPath(ctx, r, p.x, p.y, cell); ctx.fill(); }
      else ctx.fillRect(p.x - arm / 2, p.y - arm / 2, arm, arm);
    }

    // 2) 导航路线
    if (state.nav.path) drawPath(cell);

    // 3) 房间标记与文字
    const showName = state.opts.names && cell > 24;
    const showMark = state.opts.marks && cell > 40;
    const showCode = state.opts.codes && cell > 28;
    for (const r of visible) {
      const p = worldToScreen(r.rx, r.rz);
      if (p.x < -cell * 2 || p.x > rect.width + cell * 2 ||
          p.y < -cell * 2 || p.y > rect.height + cell * 2) continue;

      if (!r.short) {
        if (showCode && r.code) {
          const fs = cell * 0.155;
          ctx.font = `600 ${fs}px "Segoe UI",sans-serif`;
          ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
          ctx.lineWidth = fs * 0.5; ctx.strokeStyle = INK; ctx.lineJoin = 'round';
          ctx.strokeText(r.code, p.x, p.y);
          ctx.fillStyle = 'rgba(255,255,255,.72)';
          ctx.fillText(r.code, p.x, p.y);
        }
        continue;
      }

      const color = CAT_COLOR[r.category] || CAT_COLOR.corridor;
      let textY = p.y;
      if (showMark && hasBody(r)) {
        drawMark(r, p.x, p.y - cell * 0.13, cell, color);
        textY = p.y + cell * 0.14;
      }
      if (showName) {
        const fs = cell * 0.17;
        ctx.font = `700 ${fs}px "PingFang SC","Microsoft YaHei","Segoe UI",sans-serif`;
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        if (!hasBody(r)) {
          ctx.lineWidth = fs * 0.42; ctx.strokeStyle = INK; ctx.lineJoin = 'round';
          ctx.strokeText(r.short, p.x, textY);
        }
        ctx.fillStyle = LABEL_ON_INK;
        ctx.fillText(r.short, p.x, textY);
      }
    }

    // 3.5) 战利品刷新点（候选位置）
    if (state.opts.spawns && state.spawns.length && cell > 22) {
      const rad = clamp(cell * 0.055, 2, 5);
      ctx.save();
      ctx.lineWidth = Math.max(1, cell * 0.014);
      ctx.strokeStyle = 'rgba(25,26,29,.9)';
      for (const s of state.spawns) {
        if (!spawnVisible(s)) continue;
        const p = worldToScreen(s.rx, s.rz);
        if (p.x < -20 || p.x > rect.width + 20 || p.y < -20 || p.y > rect.height + 20) continue;
        const on = s === state.selectedSpawn || s === state.hoverSpawn;
        ctx.beginPath();
        ctx.arc(p.x, p.y, on ? rad * 1.5 : rad, 0, Math.PI * 2);
        ctx.fillStyle = s.color;
        ctx.fill();
        ctx.stroke();
        if (on) {
          ctx.save();
          ctx.beginPath();
          ctx.arc(p.x, p.y, rad * 1.5 + 3, 0, Math.PI * 2);
          ctx.strokeStyle = s === state.selectedSpawn ? PATH_COLOR : '#fff';
          ctx.lineWidth = 2;
          ctx.stroke();
          ctx.restore();
        }
      }
      ctx.restore();
    }

    // 4) 悬停 / 选中 / 起终点 / 途径点描边
    for (const r of visible) {
      const isSel = state.selected === r;
      const isFrom = state.nav.from === r;
      const isTo = state.nav.to === r;
      const viaIndex = state.nav.vias.indexOf(r);
      const isVia = viaIndex !== -1;
      if (!isSel && !isFrom && !isTo && !isVia && r !== state.hover) continue;
      const p = worldToScreen(r.rx, r.rz);
      ctx.save();
      ctx.lineWidth = (isFrom || isTo || isVia) ? 3.5 : isSel ? 3 : 2;
      ctx.strokeStyle = (isFrom || isTo) ? PATH_COLOR : isVia ? '#5c7cfa' : isSel ? '#111' : 'rgba(0,0,0,.45)';
      if (hasBody(r)) bodyPath(ctx, r, p.x, p.y, cell, 4);
      else roundRectPath(ctx, p.x - arm / 2 - 4, p.y - arm / 2 - 4, arm + 8, arm + 8, 6);
      ctx.stroke();
      ctx.restore();
      if (isFrom || isTo || isVia) {
        drawEndpointBadge(p, isFrom ? '起' : isTo ? '终' : String(viaIndex + 1), cell, isVia ? '#5c7cfa' : PATH_COLOR);
      }
    }

    if (state.position) drawPositionMarker(state.position, cell);

    // 5) 区域大标题
    for (const l of state.labels) drawZoneLabel(l.box, l.text);
    schedulePiPMapUpdate();
  }

  function drawPath(cell) {
    const path = state.nav.path;
    if (!path || path.rooms.length < 2) return;
    const pts = path.rooms.map((r) => worldToScreen(r.rx, r.rz));

    ctx.save();
    ctx.lineCap = 'round'; ctx.lineJoin = 'round';

    // 外发光
    ctx.strokeStyle = PATH_GLOW;
    ctx.lineWidth = Math.max(8, cell * 0.42);
    strokeSegments(pts, path.edges, false);

    // 主线：步行实线 / 电梯虚线
    ctx.strokeStyle = PATH_COLOR;
    ctx.lineWidth = Math.max(2.5, cell * 0.16);
    strokeSegments(pts, path.edges, false);
    ctx.setLineDash([Math.max(6, cell * 0.22), Math.max(5, cell * 0.18)]);
    strokeSegments(pts, path.edges, true);
    ctx.setLineDash([]);
    ctx.restore();
  }

  function strokeSegments(pts, edges, elevatorOnly) {
    for (let i = 0; i < edges.length; i++) {
      const isElev = edges[i].kind === 'elevator';
      if (isElev !== elevatorOnly) continue;
      ctx.beginPath();
      ctx.moveTo(pts[i].x, pts[i].y);
      ctx.lineTo(pts[i + 1].x, pts[i + 1].y);
      ctx.stroke();
    }
  }

  function drawEndpointBadge(p, text, cell, color = PATH_COLOR) {
    const r = cell * 0.17;
    ctx.save();
    ctx.beginPath();
    ctx.arc(p.x, p.y - BODY_H * cell / 2 - r - 3, r, 0, Math.PI * 2);
    ctx.fillStyle = color; ctx.fill();
    ctx.fillStyle = '#fff';
    ctx.font = `700 ${r * 1.15}px "PingFang SC","Microsoft YaHei",sans-serif`;
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText(text, p.x, p.y - BODY_H * cell / 2 - r - 2);
    ctx.restore();
  }

  function drawPositionMarker(pos, cell) {
    const p = worldToScreen(pos.rx, pos.rz);
    const r = cell * 0.16;
    ctx.save();
    ctx.beginPath();
    ctx.arc(p.x, p.y, r + 4, 0, Math.PI * 2);
    ctx.fillStyle = 'rgba(45, 105, 255, .20)';
    ctx.fill();
    ctx.beginPath();
    ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
    ctx.fillStyle = '#2563eb';
    ctx.fill();
    ctx.fillStyle = '#fff';
    ctx.font = `700 ${r * 1.2}px "Segoe UI",sans-serif`;
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText('我', p.x, p.y + .5);
    ctx.restore();
  }

  function drawMark(r, cx, cy, cell, color) {
    const s = cell * 0.11;
    ctx.save();
    ctx.strokeStyle = color; ctx.fillStyle = color;
    ctx.lineWidth = Math.max(1.4, cell * 0.028);
    switch (r.category) {
      case 'scp':
        ctx.beginPath(); ctx.arc(cx, cy, s, 0, Math.PI * 2); ctx.stroke();
        ctx.beginPath(); ctx.arc(cx, cy, s * 0.36, 0, Math.PI * 2); ctx.fill();
        break;
      case 'core':
        ctx.beginPath();
        ctx.moveTo(cx, cy - s); ctx.lineTo(cx + s, cy);
        ctx.lineTo(cx, cy + s); ctx.lineTo(cx - s, cy);
        ctx.closePath(); ctx.fill();
        break;
      case 'security':
        ctx.beginPath();
        ctx.moveTo(cx - s, cy - s * 0.8); ctx.lineTo(cx + s, cy - s * 0.8);
        ctx.lineTo(cx + s, cy); ctx.lineTo(cx, cy + s); ctx.lineTo(cx - s, cy);
        ctx.closePath(); ctx.fill();
        break;
      case 'checkpoint':
        ctx.fillRect(cx - s, cy - s, s * 0.55, s * 2);
        ctx.fillRect(cx + s * 0.45, cy - s, s * 0.55, s * 2);
        break;
      case 'deadend': // 死路：叉号
        ctx.beginPath();
        ctx.moveTo(cx - s * 0.8, cy - s * 0.8); ctx.lineTo(cx + s * 0.8, cy + s * 0.8);
        ctx.moveTo(cx + s * 0.8, cy - s * 0.8); ctx.lineTo(cx - s * 0.8, cy + s * 0.8);
        ctx.stroke();
        break;
      case 'personnel':
        ctx.beginPath(); ctx.arc(cx, cy - s * 0.45, s * 0.42, 0, Math.PI * 2); ctx.fill();
        ctx.beginPath();
        ctx.moveTo(cx - s * 0.75, cy + s); ctx.lineTo(cx - s * 0.5, cy + s * 0.05);
        ctx.lineTo(cx + s * 0.5, cy + s * 0.05); ctx.lineTo(cx + s * 0.75, cy + s);
        ctx.closePath(); ctx.fill();
        break;
      default:
        ctx.fillRect(cx - s * 0.7, cy - s * 0.7, s * 1.4, s * 1.4);
        break;
    }
    ctx.restore();
  }

  function drawZoneLabel(box, text) {
    if (!box) return;
    const top = worldToScreen(box.cx, box.maxZ);
    const fontPx = state.view.scale * 5.6;
    ctx.save();
    ctx.font = `800 ${fontPx}px "PingFang SC","Microsoft YaHei","Segoe UI",sans-serif`;
    ctx.textAlign = 'center'; ctx.textBaseline = 'bottom';
    const y = top.y - fontPx * 0.75;
    const w = ctx.measureText(text).width;
    ctx.fillStyle = 'rgba(244,244,240,.85)';
    roundRectPath(ctx, top.x - w / 2 - 10, y - fontPx * 1.05, w + 20, fontPx * 1.35, 6);
    ctx.fill();
    ctx.fillStyle = '#101114';
    ctx.fillText(text, top.x, y);
    ctx.restore();
  }

  // ---------------- 视图 ----------------

  function fitBox(box, padUnits) {
    const rect = el.canvasWrap.getBoundingClientRect();
    const w = (box.maxX - box.minX) + padUnits * 2;
    const h = (box.maxZ - box.minZ) + padUnits * 2;
    state.view.scale = clamp(Math.min(rect.width / w, rect.height / h), MIN_SCALE, MAX_SCALE);
    state.view.x = rect.width / 2 - box.cx * state.view.scale;
    state.view.y = rect.height / 2 + box.cz * state.view.scale;
    render();
  }

  function fitTarget(which) {
    if (!state.bbox.all) return;
    fitBox(which === 'core' ? state.bbox.core : which === 'light' ? state.bbox.light : state.bbox.all, 30);
  }

  function fitRooms(rooms) {
    if (!rooms || !rooms.length) return;
    fitBox(bboxOf(rooms), 45);
  }

  function centerOn(r) {
    const rect = el.canvasWrap.getBoundingClientRect();
    state.view.scale = clamp(state.view.scale, PX_PER_UNIT * 0.9, PX_PER_UNIT * 2.4);
    state.view.x = rect.width / 2 - r.rx * state.view.scale;
    state.view.y = rect.height / 2 + r.rz * state.view.scale;
    render();
  }

  /** 刷新点画在房间之上，因此先于房间参与命中判定 */
  function hitTestSpawn(sx, sy) {
    if (!state.opts.spawns) return null;
    const cell = 15 * state.view.scale;
    if (cell <= 22) return null;
    const grab = Math.max(6, clamp(cell * 0.055, 2, 5) + 3);
    let best = null, bestD = Infinity;
    for (const s of state.spawns) {
      if (!spawnVisible(s)) continue;
      const p = worldToScreen(s.rx, s.rz);
      const d = Math.hypot(sx - p.x, sy - p.y);
      if (d <= grab && d < bestD) { bestD = d; best = s; }
    }
    return best;
  }

  function hitTest(sx, sy) {
    const cell = 15 * state.view.scale;
    const arm = ARM_W * cell;
    for (let i = state.rooms.length - 1; i >= 0; i--) {
      const r = state.rooms[i];
      if (state.hidden.has(r.category)) continue;
      const p = worldToScreen(r.rx, r.rz);
      const hw = hasBody(r) ? (BODY_W * cell) / 2 : arm / 2;
      const hh = hasBody(r) ? (BODY_H * cell) / 2 : arm / 2;
      if (Math.abs(sx - p.x) <= hw && Math.abs(sy - p.y) <= hh) return r;
    }
    return null;
  }

  // ---------------- 房间详情 ----------------

  function showRoomPanel(r) {
    state.selected = r;
    state.selectedSpawn = null;
    el.roomPanel.hidden = false;
    const catLabel = (state.data.categories || {})[r.category] || r.category;
    el.roomPanel.innerHTML = `
      <button class="rp-close" id="rpClose" type="button" aria-label="关闭房间详情">✕</button>
      <div class="rp-title">${r.icon ? `<span>${escapeHtml(r.icon)}</span>` : ''}<span>${escapeHtml(r.label)}</span></div>
      <div class="rp-cat"><span class="cat-dot" style="background:${CAT_COLOR[r.category]}"></span>${escapeHtml(catLabel)} · ${escapeHtml(zoneLabelOf(r.zone))}${r.code ? ' · ' + escapeHtml(r.code) : ''}</div>
      <div class="rp-actions">
        <button class="btn btn-sm" id="rpFrom">设为起点</button>
        <button class="btn btn-sm" id="rpTo">设为终点</button>
        <button class="btn btn-sm" id="rpVia">追加途径点</button>
      </div>
      <div class="rp-row"><span>房间标识</span><b>${escapeHtml(r.name)}</b></div>
      <div class="rp-row"><span>形状</span><b>${escapeHtml(r.shape)}</b></div>
      <div class="rp-row"><span>网格坐标</span><b>(${r.cx}, ${r.cy})</b></div>
      <div class="rp-row"><span>世界坐标</span><b>${r.x.toFixed(0)}, ${r.y.toFixed(0)}, ${r.z.toFixed(0)}</b></div>
      <div class="rp-row"><span>朝向</span><b>${r.rotY.toFixed(0)}°</b></div>
    `;
    $('rpClose').onclick = closePanel;
    $('rpFrom').onclick = () => setNav('from', r);
    $('rpTo').onclick = () => setNav('to', r);
    $('rpVia').onclick = () => setNav('via', r);
    updatePiP();
    render();
  }

  function closePanel() {
    el.roomPanel.hidden = true;
    state.selected = null;
    state.selectedSpawn = null;
    render();
    updatePiP();
  }

  function showSpawnPanel(s) {
    state.selected = null;
    state.selectedSpawn = s;
    el.roomPanel.hidden = false;

    const roomMeta = state.rooms.find((r) => r.name === s.room && r.zone === s.zone);
    const roomLabel = roomMeta ? roomMeta.label : s.room;
    const types = (s.t || []).map(typeName);
    const isStructure = s.k === 'structure';

    el.roomPanel.innerHTML = `
      <button class="rp-close" id="rpClose" type="button" aria-label="关闭刷新点详情">✕</button>
      <div class="rp-title"><span class="cat-dot" style="background:${s.color}"></span><span>${escapeHtml(s.tag)}刷新点</span></div>
      <div class="rp-cat">${isStructure ? '结构点位' : '物品点位'} · ${escapeHtml(roomLabel)} · ${escapeHtml(zoneLabelOf(s.zone))}</div>
      ${types.length ? `<div class="rp-types">
        <div class="rp-types-label">${isStructure
          ? (types.length > 1 ? `这一个点位会在下列 ${types.length} 种结构中随机取一种` : '出现的结构')
          : '可能产出'}</div>
        <div class="rp-chips">${types.map((t) => `<span class="rp-chip">${escapeHtml(t)}</span>`).join('')}</div>
      </div>` : ''}
      ${s.cls ? `<div class="rp-row"><span>刷新点类型</span><b>${escapeHtml(typeName(s.cls))}</b></div>` : ''}
      ${!isStructure && s.e ? `<div class="rp-row"><span>空手率</span><b>${s.e}%</b></div>` : ''}
      ${!isStructure && s.u > 1 ? `<div class="rp-row"><span>最多产出</span><b>${s.u} 件</b></div>` : ''}
      ${s.d ? `<div class="rp-row"><span>需开启</span><b>${escapeHtml(s.d)}</b></div>` : ''}
      ${s.sub ? '<div class="rp-row"><span>所在层</span><b>房间夹层 / 下层</b></div>' : ''}
      <div class="rp-row"><span>世界坐标</span><b>${s.wx.toFixed(0)}, ${s.wy.toFixed(0)}, ${s.wz.toFixed(0)}</b></div>
      <div class="rp-note">${s.sub ? '这个点位在该房间的夹层或下层子区域，平面图上按房间位置标示。<br>' : ''}刷新点位置固定，但每局是否真的刷出由与种子无关的随机数决定。</div>
    `;
    $('rpClose').onclick = closePanel;
    render();
    updatePiP();
  }

  // ---------------- 导航面板 ----------------

  function setNav(role, room) {
    if (role === 'via' && (room === state.nav.from || room === state.nav.to)) {
      showBadge('途径点与起点或终点相同，无需重复设置');
      return;
    }
    if (role === 'via') {
      if (state.nav.vias.includes(room)) {
        showBadge('这个房间已经在途径点列表中');
        return;
      }
      state.nav.vias.push(room);
    } else {
      state.nav[role] = room;
      state.nav.vias = state.nav.vias.filter((via) => via !== room);
    }
    recomputePath();
  }

  function renderWaypoints() {
    if (!state.nav.vias.length) {
      el.navWaypoints.innerHTML = '<div class="nav-waypoint-empty">尚未添加途径点（可选）</div>';
      return;
    }
    el.navWaypoints.innerHTML = state.nav.vias.map((room, index) => `
      <div class="nav-waypoint" data-index="${index}">
        <span class="nav-tag via">${index + 1}</span>
        <button class="nav-waypoint-name" type="button" title="在地图上查看">${escapeHtml(roomTitle(room))}</button>
        <span class="nav-waypoint-actions">
          <button type="button" data-action="up" aria-label="上移途径点 ${index + 1}" title="上移" ${index === 0 ? 'disabled' : ''}>↑</button>
          <button type="button" data-action="down" aria-label="下移途径点 ${index + 1}" title="下移" ${index === state.nav.vias.length - 1 ? 'disabled' : ''}>↓</button>
          <button type="button" data-action="remove" aria-label="删除途径点 ${index + 1}" title="删除">✕</button>
        </span>
      </div>`).join('');

    el.navWaypoints.querySelectorAll('.nav-waypoint').forEach((node) => {
      const index = Number(node.dataset.index);
      node.querySelector('.nav-waypoint-name').onclick = () => {
        const room = state.nav.vias[index];
        if (room) { centerOn(room); showRoomPanel(room); }
      };
      node.querySelector('[data-action="up"]').onclick = () => moveWaypoint(index, -1);
      node.querySelector('[data-action="down"]').onclick = () => moveWaypoint(index, 1);
      node.querySelector('[data-action="remove"]').onclick = () => removeWaypoint(index);
    });
  }

  function moveWaypoint(index, offset) {
    const next = index + offset;
    if (next < 0 || next >= state.nav.vias.length) return;
    [state.nav.vias[index], state.nav.vias[next]] = [state.nav.vias[next], state.nav.vias[index]];
    recomputePath();
  }

  function removeWaypoint(index) {
    if (index < 0 || index >= state.nav.vias.length) return;
    state.nav.vias.splice(index, 1);
    recomputePath();
  }

  function renderNavPanel() {
    const put = (node, room, emptyLabel = '未选择') => {
      node.querySelector('.nav-name').textContent = room ? roomTitle(room) : emptyLabel;
      node.classList.toggle('filled', !!room);
    };
    put(el.navFrom, state.nav.from);
    put(el.navTo, state.nav.to);
    renderWaypoints();

    const { from, to, path } = state.nav;
    if (!from || !to) {
      el.navResult.hidden = true;
      el.navHint.hidden = false;
      updatePiP();
      return;
    }
    el.navHint.hidden = true;
    el.navResult.hidden = false;

    if (!path) {
      el.navResult.innerHTML = '<div class="nav-fail">这两个房间之间没有连通路线。</div>';
      updatePiP();
      return;
    }
    if (path.rooms.length === 1) {
      el.navResult.innerHTML = '<div class="nav-sum">起点和终点是同一房间，无需导航。</div>';
      updatePiP();
      return;
    }

    const segments = path.edges.filter((e) => e.kind === 'walk').length;
    const transfers = path.edges.filter((e) => e.kind === 'elevator');
    // 总步行米数 = 走廊段数 × 15 + 各次换乘在电梯房间内的额外路程
    const meters = segments * STEP_COST + transfers.reduce((n, e) => n + e.w, 0);
    // 固定耗时：每次乘梯 8 秒，与角色速度无关
    const fixedSec = transfers.reduce((n, e) => n + (e.ride || 0), 0);
    const zones = [];
    for (const r of path.rooms) {
      const z = zoneLabelOf(r.zone);
      if (zones[zones.length - 1] !== z) zones.push(z);
    }

    const legs = buildItinerary(path);

    el.navResult.innerHTML = `
      <div class="nav-sum">
        <b>${segments}</b> 段走廊 · 约 <b>${meters}</b> m
        ${transfers.length ? ` · <b>${transfers.length}</b> 次换乘` : ''}
      </div>
      <div class="nav-zones">${escapeHtml(zones.join(' → '))}</div>
      <div class="nav-times">
        ${SPEEDS.map((s) => `<div class="nt"><span>${s.label}</span><i>${s.v}</i><b>${fmtTime(meters / s.v + fixedSec)}</b></div>`).join('')}
      </div>
      <div class="nav-note">
        按房间中心连线估算${transfers.length ? `，已含电梯房间内步行 ${transfers.reduce((n, e) => n + e.w, 0)} m 与 ${fixedSec} 秒乘梯时间` : ''}；未计入开门等待与房间内绕行。
      </div>
      <div class="nav-start">
        <span class="cat-dot" style="background:${CAT_COLOR[from.category]}"></span>
        从 ${escapeHtml(from.label)} 出发
      </div>
      <ol class="nav-steps">
        ${legs.map((leg, i) => {
          const last = i === legs.length - 1;
          const destPrefix = last ? '终点 · ' : leg.waypoint ? `途径点 ${leg.waypoint} · ` : '';
          const dest = leg.room.short
            ? `<span class="ns-dest"><span class="cat-dot" style="background:${CAT_COLOR[leg.room.category]}"></span>${destPrefix}${escapeHtml(leg.room.label)}</span>`
            : (leg.room.code ? `<span class="ns-dest ns-corr">${escapeHtml(leg.room.code)}</span>` : '');
          const via = leg.via && leg.via.length
            ? `<span class="ns-via">途经 ${escapeHtml(leg.via.join(' · '))}</span>` : '';
          return `<li class="${leg.kind === 'elevator' ? 'ns-elev' : ''}${last ? ' ns-last' : ''}">
            <span class="ns-act">${leg.kind === 'elevator' ? '🛗 ' : ''}${escapeHtml(legText(leg, last))}</span>
            ${dest}${via}
          </li>`;
        }).join('')}
      </ol>
      <div class="nav-btns">
        <button class="btn btn-sm" id="navFit">查看整条路线</button>
        <button class="btn btn-sm" id="navSwap">起终点对调</button>
      </div>
    `;
    $('navFit').onclick = () => fitRooms(path.rooms);
    $('navSwap').onclick = () => {
      const t = state.nav.from; state.nav.from = state.nav.to; state.nav.to = t;
      state.nav.vias.reverse();
      recomputePath();
    };
    updatePiP();
  }

  // ---------------- 加载 ----------------

  async function loadSeed(seed) {
    seed = String(seed).trim();
    const requestId = ++loadRequestId;
    if (loadController) {
      loadController.abort();
      loadController = null;
    }
    if (!/^\d{1,10}$/.test(seed) || Number(seed) < 1 || Number(seed) > 2147483647) {
      setLoading(false);
      showError('请输入 1 到 2147483647 之间的有效种子');
      return;
    }

    const cached = cacheGet(seed);
    if (cached) {
      setLoading(false);
      applyData(seed, cached);
      showBadge('本地缓存命中，未发起网络请求');
      return;
    }

    const controller = new AbortController();
    loadController = controller;
    setLoading(true);
    try {
      const res = await fetch('api.php?seed=' + encodeURIComponent(seed), { signal: controller.signal });
      const json = await res.json();
      if (requestId !== loadRequestId) return;
      if (!res.ok) throw new Error(json.error || '请求失败');
      cachePut(seed, json);
      applyData(seed, json);
      showBadge(res.headers.get('X-Cache') === 'HIT' ? '服务器缓存命中' : '已生成并写入缓存');
    } catch (e) {
      if (e && e.name === 'AbortError') return;
      if (requestId !== loadRequestId) return;
      showError('生成失败：' + e.message);
    } finally {
      if (requestId === loadRequestId) {
        loadController = null;
        setLoading(false);
      }
    }
  }

  function applyData(seed, json) {
    const p = prepareRooms(json);
    state.spawns = prepareSpawns(json);
    for (const s of state.spawns) {
      if (s.g === 'light') { s.rx += p.lightShift.dx; s.rz += p.lightShift.dz; }
    }
    state.data = json;
    state.seed = seed;
    state.rooms = p.all;
    state.byId = new Map(p.all.map((r) => [r.id, r]));
    state.lightShift = p.lightShift;
    state.bbox = { core: p.coreBox, light: p.lightBox, all: p.allBox };
    state.labels = [
      { box: p.ezBox, text: '办公区' },
      { box: p.hczBox, text: '重收容区' },
      { box: p.lightBox, text: '轻收容区' },
    ];
    state.selected = null;
    state.selectedSpawn = null;
    state.hover = null;
    state.hoverSpawn = null;
    state.nav = { from: null, vias: [], to: null, path: null };
    // 同一页面切换种子时，旧地图的房间引用不能继续作为当前位置标记。
    state.position = null;
    buildGraph();
    updatePlayerPosition(false);
    renderPositionInfo();

    el.roomPanel.hidden = true;
    pushHistory(seed);
    renderHistory();

    el.emptyState.hidden = true;
    el.errorState.hidden = true;
    el.navWrap.hidden = false;
    el.positionWrap.hidden = false;
    el.jumpWrap.hidden = false;
    el.searchWrap.hidden = false;
    el.optionsWrap.hidden = false;
    el.pipWrap.hidden = false;
    el.legendWrap.hidden = false;
    el.canvasWrap.hidden = false;

    $('cnt-core').textContent = json.counts.HeavyContainment + json.counts.Entrance;
    $('cnt-light').textContent = json.counts.LightContainment;

    buildLegend();
    buildSpawnLegend();
    renderNavPanel();
    resizeCanvas();
    fitTarget('all');
    syncUrl();
  }

  const setLoading = (v) => {
    el.loadingState.hidden = !v;
    el.loadBtn.disabled = v;
    if (v) el.errorState.hidden = true;
  };
  function showError(msg) {
    el.errorState.hidden = false; el.errorState.textContent = msg; el.emptyState.hidden = true;
  }
  function syncUrl() {
    if (!state.seed) return;
    const url = new URL(window.location.href);
    url.searchParams.set('seed', state.seed);
    window.history.replaceState(null, '', url);
  }

  // ---------------- 玩家定位 ----------------

  function closestRoomTo(x, y, z) {
    let best = null;
    let bestDistance = Infinity;
    for (const r of state.rooms) {
      // 楼层高度用较小权重参与判断：XZ 才是同一房间内最可靠的位置特征，
      // 但 Y 能区分上下重叠的区域。
      const distance = Math.hypot(r.x - x, r.z - z) + Math.abs(r.y - y) * .2;
      if (distance < bestDistance) { bestDistance = distance; best = r; }
    }
    return best;
  }

  function mapPositionForRoom(x, z, room) {
    if (room.zone === 'LightContainment') {
      return { rx: x + state.lightShift.dx, rz: z + state.lightShift.dz };
    }
    return { rx: -x, rz: -z };
  }

  function renderPositionInfo() {
    const pos = state.position;
    el.positionInfo.hidden = !pos;
    if (!pos) {
      el.positionInfo.textContent = '';
      updatePiP();
      return;
    }
    el.positionInfo.innerHTML = `
      <span>已标记位置 · 参考房间：${escapeHtml(pos.room.label)}（${escapeHtml(zoneLabelOf(pos.room.zone))}）</span>
      <button class="btn btn-sm" type="button">设为起点</button>`;
    el.positionInfo.querySelector('button').onclick = () => setNav('from', pos.room);
    updatePiP();
  }

  function updatePlayerPosition(center) {
    if (!state.rooms.length) return false;
    const rawValues = [el.posX.value, el.posY.value, el.posZ.value].map((v) => v.trim());
    const values = rawValues.map(Number);
    if (rawValues.some((v) => v === '') || values.some((v) => !Number.isFinite(v))) {
      if (center) showBadge('请完整输入有效的 X、Y、Z 坐标');
      return false;
    }
    const [x, y, z] = values;
    const room = closestRoomTo(x, y, z);
    if (!room) return false;
    state.position = { x, y, z, room, ...mapPositionForRoom(x, z, room) };
    renderPositionInfo();
    render();
    if (center) centerOnPoint(state.position);
    return true;
  }

  function centerOnPoint(pos) {
    const rect = el.canvasWrap.getBoundingClientRect();
    state.view.scale = clamp(state.view.scale, PX_PER_UNIT * .9, PX_PER_UNIT * 2.4);
    state.view.x = rect.width / 2 - pos.rx * state.view.scale;
    state.view.y = rect.height / 2 + pos.rz * state.view.scale;
    render();
  }

  function clearPlayerPosition() {
    state.position = null;
    el.posX.value = ''; el.posY.value = ''; el.posZ.value = '';
    renderPositionInfo();
    render();
  }

  // ---------------- 画中画 ----------------

  function loadPiPOptions() {
    let opts = {};
    try { opts = JSON.parse(localStorage.getItem(PIP_OPTS_KEY) || '{}'); } catch {}
    for (const key of ['Map', 'Sidebar', 'Seed', 'Position', 'Navigation', 'Selection']) {
      const input = el['pip' + key];
      if (Object.prototype.hasOwnProperty.call(opts, key)) input.checked = !!opts[key];
    }
    if (!el.pipMap.checked && !el.pipSidebar.checked) el.pipMap.checked = true;
  }

  function savePiPOptions(changedInput = null) {
    if (!el.pipMap.checked && !el.pipSidebar.checked) {
      if (changedInput === el.pipMap) el.pipSidebar.checked = true;
      else el.pipMap.checked = true;
    }
    const opts = {};
    for (const key of ['Map', 'Sidebar', 'Seed', 'Position', 'Navigation', 'Selection']) {
      opts[key] = el['pip' + key].checked;
    }
    try { localStorage.setItem(PIP_OPTS_KEY, JSON.stringify(opts)); } catch {}
    updatePiP();
  }

  function navSummary() {
    const { from, vias, to, path } = state.nav;
    if (!from || !to) return '尚未设置起点和终点';
    if (!path) return '起终点之间没有连通路线';
    const segments = path.edges.filter((e) => e.kind === 'walk').length;
    return `${[from, ...vias, to].map(roomTitle).join(' → ')} · ${segments} 段走廊`;
  }

  function pipNavigationHtml() {
    const { from, to, path } = state.nav;
    if (!from || !to) return '<p class="pip-empty">请在主页面设置起点和终点。</p>';
    if (!path) return '<p class="pip-empty">起终点之间没有连通路线。</p>';
    if (path.rooms.length === 1) return '<p class="pip-empty">已经位于终点，无需导航。</p>';
    const legs = buildItinerary(path);
    return `
      <div class="pip-route">${escapeHtml(navSummary())}</div>
      <ol class="pip-steps">
        ${legs.map((leg, index) => {
          const last = index === legs.length - 1;
          const room = leg.room;
          const destination = last
            ? `终点 · ${room.label}`
            : leg.waypoint
              ? `途径点 ${leg.waypoint} · ${room.label}`
              : (room.short || room.code || '');
          return `<li>
            <strong>${leg.kind === 'elevator' ? '🛗 ' : ''}${escapeHtml(legText(leg, last))}</strong>
            ${destination ? `<small>${escapeHtml(destination)}</small>` : ''}
          </li>`;
        }).join('')}
      </ol>`;
  }

  function ensurePiPLayout() {
    if (!pipWindow || pipWindow.document.getElementById('pipShell')) return;
    pipWindow.document.body.innerHTML = `
      <main class="pip-shell" id="pipShell">
        <section class="pip-map-wrap" id="pipMapWrap">
          <canvas id="pipMapCanvas" aria-label="实时地图，可拖动和缩放主页面视角"></canvas>
          <button class="pip-info-toggle" id="pipInfoToggle" type="button"
            aria-label="隐藏信息边栏" title="隐藏信息边栏">‹</button>
        </section>
        <aside class="pip-info" id="pipInfo"></aside>
      </main>`;

    const canvas = pipWindow.document.getElementById('pipMapCanvas');
    const stopDragging = (event) => {
      if (!pipDragging || event.pointerId !== pipPointerId) return;
      pipDragging = false;
      pipPointerId = null;
      pipLastPointer = null;
      canvas.classList.remove('grabbing');
    };
    canvas.addEventListener('pointerdown', (event) => {
      if (event.button !== 0) return;
      pipDragging = true;
      pipPointerId = event.pointerId;
      pipLastPointer = { x: event.clientX, y: event.clientY };
      canvas.classList.add('grabbing');
      canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointermove', (event) => {
      if (!pipDragging || event.pointerId !== pipPointerId || !pipLastPointer) return;
      const projection = pipMapProjection(canvas);
      if (!projection) return;
      state.view.x += (event.clientX - pipLastPointer.x) / projection.scale;
      state.view.y += (event.clientY - pipLastPointer.y) / projection.scale;
      pipLastPointer = { x: event.clientX, y: event.clientY };
      render();
    });
    canvas.addEventListener('pointerup', stopDragging);
    canvas.addEventListener('pointercancel', stopDragging);
    canvas.addEventListener('wheel', (event) => {
      event.preventDefault();
      const rect = canvas.getBoundingClientRect();
      const point = pipPointToMain(canvas, event.clientX - rect.left, event.clientY - rect.top);
      if (!point) return;
      zoomViewAt(point.x, point.y, Math.exp(-event.deltaY * 0.0012));
    }, { passive: false });
    pipWindow.document.getElementById('pipInfoToggle').addEventListener('click', () => {
      el.pipSidebar.checked = !el.pipSidebar.checked;
      if (!el.pipSidebar.checked) el.pipMap.checked = true;
      savePiPOptions(el.pipSidebar);
    });
  }

  function pipMapProjection(canvas) {
    const pipRect = canvas.getBoundingClientRect();
    const mainRect = el.canvas.getBoundingClientRect();
    if (!pipRect.width || !pipRect.height || !mainRect.width || !mainRect.height) return null;
    const scale = Math.max(pipRect.width / mainRect.width, pipRect.height / mainRect.height);
    return {
      scale,
      offsetX: (pipRect.width - mainRect.width * scale) / 2,
      offsetY: (pipRect.height - mainRect.height * scale) / 2,
    };
  }

  function pipPointToMain(canvas, x, y) {
    const projection = pipMapProjection(canvas);
    if (!projection) return null;
    return {
      x: (x - projection.offsetX) / projection.scale,
      y: (y - projection.offsetY) / projection.scale,
    };
  }

  function drawPiPMap() {
    if (!pipWindow || !el.pipMap.checked) return;
    try {
      const canvas = pipWindow.document.getElementById('pipMapCanvas');
      if (!canvas) return;
      const rect = canvas.getBoundingClientRect();
      if (!rect.width || !rect.height || !el.canvas.width || !el.canvas.height) return;
      const dpr = Math.max(1, pipWindow.devicePixelRatio || 1);
      const width = Math.round(rect.width * dpr);
      const height = Math.round(rect.height * dpr);
      if (canvas.width !== width) canvas.width = width;
      if (canvas.height !== height) canvas.height = height;
      const pipCtx = canvas.getContext('2d');
      pipCtx.setTransform(1, 0, 0, 1, 0, 0);
      pipCtx.fillStyle = PAPER;
      pipCtx.fillRect(0, 0, width, height);
      // 画中画只镜像主页面当前视角；采用铺满裁切，避免因窗口宽高比不同产生大块空白。
      const scale = Math.max(width / el.canvas.width, height / el.canvas.height);
      const drawWidth = el.canvas.width * scale;
      const drawHeight = el.canvas.height * scale;
      pipCtx.drawImage(
        el.canvas,
        (width - drawWidth) / 2,
        (height - drawHeight) / 2,
        drawWidth,
        drawHeight,
      );
      drawPiPRoomNames(pipCtx, width, height, scale, (width - drawWidth) / 2, (height - drawHeight) / 2);
    } catch {
      pipWindow = null;
    }
  }

  /**
   * 小窗中的地图比主画布小很多，直接缩放主画布会把房间名压得难以辨认。
   * 在同一视角上重绘名称，不改变主页面的房间文字或任何地图状态。
   */
  function drawPiPRoomNames(pipCtx, width, height, imageScale, offsetX, offsetY) {
    if (!state.opts.names) return;
    const mainCell = 15 * state.view.scale;
    if (mainCell <= 18) return;

    const scale = state.dpr * imageScale;
    const cell = mainCell * scale;
    const fs = cell * 0.17 * PIP_ROOM_LABEL_SCALE;
    if (fs < 1) return;
    const showMark = state.opts.marks && mainCell > 40;

    pipCtx.save();
    pipCtx.font = `800 ${fs}px "PingFang SC","Microsoft YaHei","Segoe UI",sans-serif`;
    pipCtx.textAlign = 'center';
    pipCtx.textBaseline = 'middle';
    pipCtx.lineJoin = 'round';
    pipCtx.lineWidth = Math.max(.75, fs * 0.32);
    pipCtx.strokeStyle = INK;
    pipCtx.fillStyle = LABEL_ON_INK;

    for (const r of state.rooms) {
      if (!r.short || state.hidden.has(r.category)) continue;
      const main = worldToScreen(r.rx, r.rz);
      const x = offsetX + main.x * scale;
      let y = offsetY + main.y * scale;
      if (showMark && hasBody(r)) y += cell * 0.14;
      if (x < -fs * 2 || x > width + fs * 2 || y < -fs * 2 || y > height + fs * 2) continue;

      // 黑色描边会覆盖底图中较小的原始文字，使新标签保持清晰。
      pipCtx.strokeText(r.short, x, y);
      pipCtx.fillText(r.short, x, y);
    }
    pipCtx.restore();
  }

  function schedulePiPMapUpdate() {
    if (!pipWindow || !el.pipMap.checked || pipMapTimer !== null) return;
    pipMapTimer = setTimeout(() => {
      pipMapTimer = null;
      drawPiPMap();
    }, 33);
  }

  function updatePiP() {
    if (!pipWindow || !state.data) return;
    try {
      ensurePiPLayout();
      const items = [];
      if (el.pipSeed.checked) items.push(['当前种子', escapeHtml(state.seed || '—')]);
      if (el.pipPosition.checked) {
        const p = state.position;
        items.push(['当前位置', p
          ? `${p.x.toFixed(1)}, ${p.y.toFixed(1)}, ${p.z.toFixed(1)}<small>${escapeHtml(p.room.label)} · ${escapeHtml(zoneLabelOf(p.room.zone))}</small>`
          : '未定位']);
      }
      if (el.pipSelection.checked) {
        const selected = state.selected;
        items.push(['选中房间', selected ? `${escapeHtml(selected.label)}<small>${escapeHtml(zoneLabelOf(selected.zone))}</small>` : '未选择']);
      }
      const shell = pipWindow.document.getElementById('pipShell');
      const mapWrap = pipWindow.document.getElementById('pipMapWrap');
      const info = pipWindow.document.getElementById('pipInfo');
      shell.classList.toggle('map-hidden', !el.pipMap.checked);
      shell.classList.toggle('info-hidden', !el.pipSidebar.checked);
      mapWrap.hidden = !el.pipMap.checked;
      info.hidden = !el.pipSidebar.checked;
      const infoToggle = pipWindow.document.getElementById('pipInfoToggle');
      infoToggle.textContent = el.pipSidebar.checked ? '›' : '‹';
      infoToggle.setAttribute('aria-label', el.pipSidebar.checked ? '隐藏信息边栏' : '显示信息边栏');
      infoToggle.title = el.pipSidebar.checked ? '隐藏信息边栏' : '显示信息边栏';
      infoToggle.setAttribute('aria-pressed', String(!el.pipSidebar.checked));
      info.innerHTML = `
        ${items.map(([title, value]) => `<section class="pip-item"><span>${title}</span><strong>${value}</strong></section>`).join('')}
        ${el.pipNavigation.checked ? `<section class="pip-navigation">${pipNavigationHtml()}</section>` : ''}
        ${!items.length && !el.pipNavigation.checked ? '<p class="pip-empty">请在主页面选择要显示的内容。</p>' : ''}`;
      drawPiPMap();
    } catch {
      pipWindow = null;
    }
  }

  async function openPiP() {
    if (!state.data) return;
    const pipApi = window.documentPictureInPicture;
    if (!pipApi) {
      showBadge('当前浏览器不支持文档画中画，请使用新版 Chromium 浏览器');
      return;
    }
    try {
      if (pipWindow) { pipWindow.focus(); updatePiP(); return; }
      pipWindow = await pipApi.requestWindow({ width: 520, height: 340 });
      pipWindow.document.head.innerHTML = `<style>
        :root { color-scheme: dark; font-family: "Microsoft YaHei", "Segoe UI", sans-serif; }
        * { box-sizing: border-box; }
        [hidden] { display: none !important; }
        body { margin: 0; min-width: 320px; height: 100vh; overflow: hidden; background: #17181b; color: #f5f5f5; }
        button:focus-visible { outline: 2px solid #ff9b61; outline-offset: 2px; }
        .pip-shell { display: grid; grid-template-columns: minmax(0, 1fr) minmax(168px, .45fr); height: 100vh; }
        .pip-shell.map-hidden { grid-template-columns: 1fr; }
        .pip-shell.info-hidden { grid-template-columns: 1fr; }
        .pip-map-wrap { position: relative; min-width: 0; min-height: 0; overflow: hidden; background: ${PAPER}; }
        .pip-map-wrap canvas { display: block; width: 100%; height: 100%; cursor: grab; touch-action: none; }
        .pip-map-wrap canvas.grabbing { cursor: grabbing; }
        .pip-info-toggle {
          position: absolute; top: 6px; right: 6px; width: 26px; height: 26px; padding: 0; border: 0;
          border-radius: 6px; background: rgba(23,24,27,.88); color: #f5f5f5;
          font: 700 18px/1 "Segoe UI", sans-serif; cursor: pointer;
        }
        .pip-info-toggle:hover { background: #34363b; }
        .pip-info { min-width: 0; overflow-y: auto; padding: 7px 9px 10px; border-left: 1px solid #34363b; }
        .pip-item { display: grid; gap: 1px; padding: 4px 0; border-bottom: 1px solid #34363b; }
        .pip-item > span { color: #c2c5cc; font-size: 11px; }
        .pip-item strong { font-size: 14px; line-height: 1.3; }
        small { display: block; margin-top: 1px; color: #c2c5cc; font-size: 11px; font-weight: 400; }
        .pip-navigation { padding-top: 5px; }
        .pip-route { color: #ffad7e; font-size: 12px; line-height: 1.35; }
        .pip-steps { margin: 5px 0 0; padding-left: 17px; }
        .pip-steps li { padding: 3px 0; color: #ffad7e; }
        .pip-steps strong { display: block; color: #fff; font-size: 14px; line-height: 1.35; }
        .pip-empty { margin: 0; color: #c2c5cc; font-size: 13px; line-height: 1.4; }
      </style>`;
      pipWindow.addEventListener('pagehide', () => {
        pipWindow = null;
        pipDragging = false;
        pipPointerId = null;
        pipLastPointer = null;
        if (pipMapTimer !== null) clearTimeout(pipMapTimer);
        pipMapTimer = null;
      });
      pipWindow.addEventListener('resize', schedulePiPMapUpdate);
      updatePiP();
    } catch (e) {
      showBadge('无法打开画中画小窗：' + (e && e.message ? e.message : '浏览器拒绝了请求'));
    }
  }

  // ---------------- 图例 / 搜索 / 选项 / 历史 ----------------

  function buildLegend() {
    const cats = state.data.categories || {};
    el.legend.innerHTML = '';
    Object.keys(cats).forEach((key) => {
      const item = document.createElement('button');
      item.type = 'button';
      item.className = 'legend-item' + (state.hidden.has(key) ? ' off' : '');
      item.innerHTML = `<span class="legend-swatch" style="background:${CAT_COLOR[key] || '#888'}"></span><span>${escapeHtml(cats[key])}</span>`;
      item.onclick = () => {
        if (state.hidden.has(key)) state.hidden.delete(key); else state.hidden.add(key);
        item.classList.toggle('off', state.hidden.has(key));
        render();
      };
      el.legend.appendChild(item);
    });
  }

  function renderSearch(query) {
    query = (query || '').trim();
    el.searchResults.innerHTML = '';
    if (!query || !state.data) return;
    const q = query.toLowerCase();
    const hits = state.rooms.filter((r) => (r.short || r.code) && (
      r.label.toLowerCase().includes(q) || r.name.toLowerCase().includes(q) ||
      (r.short && r.short.includes(q)) || (r.code && r.code.toLowerCase().includes(q))));
    if (!hits.length) {
      el.searchResults.innerHTML = '<div class="search-empty">没有找到匹配的房间</div>';
      return;
    }
    hits.slice(0, 30).forEach((r) => {
      const div = document.createElement('button');
      div.type = 'button';
      div.className = 'search-hit';
      div.innerHTML = `<span class="cat-dot" style="background:${CAT_COLOR[r.category]}"></span><span>${escapeHtml(r.short ? r.label : r.code)}</span><span class="zone-tag">${escapeHtml(zoneLabelOf(r.zone))}</span>`;
      div.onclick = () => {
        centerOn(r); showRoomPanel(r);
        closeMobileSidebar();
      };
      el.searchResults.appendChild(div);
    });
  }

  function loadOptions() {
    try { Object.assign(state.opts, JSON.parse(localStorage.getItem(OPTS_KEY) || '{}')); } catch {}
    el.optCodes.checked = state.opts.codes;
    el.optNames.checked = state.opts.names;
    el.optMarks.checked = state.opts.marks;
    el.optSpawns.checked = state.opts.spawns;
  }
  function saveOptions() {
    state.opts.codes = el.optCodes.checked;
    state.opts.names = el.optNames.checked;
    state.opts.marks = el.optMarks.checked;
    state.opts.spawns = el.optSpawns.checked;
    try { localStorage.setItem(OPTS_KEY, JSON.stringify(state.opts)); } catch {}
    render();
  }

  /**
   * 有导入刷新点数据时才显示对应开关与图例。
   * 计数是「有多少个点位可能出现这种类型」，因为一个点位往往兼容多种结构，
   * 所以各项之和会大于点位总数——这是数据的真实形态，不做取整。
   */
  function buildSpawnLegend() {
    const has = state.spawns.length > 0;
    el.spawnRow.hidden = !has;
    el.spawnLegendWrap.hidden = !has;
    if (!has) return;

    const counts = new Map();
    for (const s of state.spawns) {
      for (const k of s.keys) counts.set(k, (counts.get(k) || 0) + 1);
    }
    const keys = [...counts.keys()].sort((a, b) => SPAWN_TYPES[b].prio - SPAWN_TYPES[a].prio);

    el.spawnLegend.innerHTML = keys.map((k) => {
      const t = SPAWN_TYPES[k];
      return `<button type="button" class="legend-item${state.hiddenSpawns.has(k) ? ' off' : ''}" data-key="${k}">
        <span class="legend-swatch dot" style="background:${t.color}"></span>
        <span>${escapeHtml(t.label)}</span><span class="lg-count">${counts.get(k)}</span>
      </button>`;
    }).join('');

    el.spawnLegend.querySelectorAll('.legend-item').forEach((item) => {
      item.onclick = () => {
        const k = item.dataset.key;
        if (state.hiddenSpawns.has(k)) state.hiddenSpawns.delete(k); else state.hiddenSpawns.add(k);
        item.classList.toggle('off', state.hiddenSpawns.has(k));
        render();
      };
    });
    updatePiP();
  }

  const loadHistory = () => {
    try {
      const value = JSON.parse(localStorage.getItem(HISTORY_KEY) || '[]');
      return Array.isArray(value) ? value : [];
    } catch { return []; }
  };
  function pushHistory(seed) {
    let h = loadHistory().filter((s) => s !== seed);
    h.unshift(seed);
    try { localStorage.setItem(HISTORY_KEY, JSON.stringify(h.slice(0, 10))); } catch {}
  }
  function renderHistory() {
    el.seedHistory.innerHTML = '';
    loadHistory().forEach((s) => {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'seed-chip';
      chip.textContent = s;
      chip.onclick = () => { el.seedInput.value = s; loadSeed(s); };
      el.seedHistory.appendChild(chip);
    });
  }

  // ---------------- 事件 ----------------

  el.loadBtn.addEventListener('click', () => loadSeed(el.seedInput.value));
  el.seedInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') loadSeed(el.seedInput.value); });
  el.randomBtn.addEventListener('click', () => {
    const s = String(1 + Math.floor(Math.random() * 2147483646));
    el.seedInput.value = s;
    loadSeed(s);
  });

  el.jumpTabs.addEventListener('click', (e) => {
    const btn = e.target.closest('.zone-tab');
    if (!btn) return;
    fitTarget(btn.dataset.target);
    closeMobileSidebar();
  });

  [el.navFrom, el.navTo].forEach((node) => {
    node.querySelector('.nav-clear').addEventListener('click', () => {
      state.nav[node.dataset.role] = null;
      recomputePath();
    });
    node.addEventListener('click', (e) => {
      if (e.target.closest('.nav-clear')) return;
      const room = state.nav[node.dataset.role];
      if (room) { centerOn(room); showRoomPanel(room); }
    });
  });

  el.searchInput.addEventListener('input', () => renderSearch(el.searchInput.value));
  el.sidebarToggle.addEventListener('click', toggleSidebar);
  el.sidebarBackdrop.addEventListener('click', closeMobileSidebar);
  desktopSidebar.addEventListener('change', syncSidebar);
  [el.optCodes, el.optNames, el.optMarks, el.optSpawns].forEach((c) => c.addEventListener('change', saveOptions));
  el.clearCacheBtn.addEventListener('click', () => {
    const n = cacheClear();
    showBadge('已清除 ' + n + ' 项本地缓存');
  });
  el.locateBtn.addEventListener('click', () => updatePlayerPosition(true));
  el.clearPositionBtn.addEventListener('click', clearPlayerPosition);
  [el.posX, el.posY, el.posZ].forEach((input) => input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') updatePlayerPosition(true);
  }));
  [el.pipMap, el.pipSidebar, el.pipSeed, el.pipPosition, el.pipNavigation, el.pipSelection].forEach((input) => {
    input.addEventListener('change', () => savePiPOptions(input));
  });
  el.pipBtn.addEventListener('click', openPiP);

  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !desktopSidebar.matches && el.sidebar.classList.contains('open')) {
      closeMobileSidebar();
      return;
    }
    if (document.activeElement && document.activeElement.matches('input, textarea, select, [contenteditable="true"]')) return;
    if (!state.data) return;
    const k = e.key.toLowerCase();
    if (k === 'q') fitTarget('core');
    else if (k === 'e') fitTarget('light');
    else if (k === 'a') fitTarget('all');
    else if (k === 'escape') { state.nav = { from: null, vias: [], to: null, path: null }; recomputePath(); }
  });

  el.canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    const rect = el.canvas.getBoundingClientRect();
    const sx = e.clientX - rect.left, sy = e.clientY - rect.top;
    zoomViewAt(sx, sy, Math.exp(-e.deltaY * 0.0012));
  }, { passive: false });

  el.canvas.addEventListener('pointerdown', (e) => {
    state.dragging = true; state.moved = false;
    state.lastPointer = { x: e.clientX, y: e.clientY };
    el.canvas.classList.add('grabbing');
    el.canvas.setPointerCapture(e.pointerId);
  });
  el.canvas.addEventListener('pointermove', (e) => {
    const rect = el.canvas.getBoundingClientRect();
    if (state.dragging) {
      const dx = e.clientX - state.lastPointer.x, dy = e.clientY - state.lastPointer.y;
      if (Math.abs(dx) > 2 || Math.abs(dy) > 2) state.moved = true;
      state.view.x += dx; state.view.y += dy;
      state.lastPointer = { x: e.clientX, y: e.clientY };
      render();
    } else {
      const sx = e.clientX - rect.left, sy = e.clientY - rect.top;
      const spawn = hitTestSpawn(sx, sy);
      const hit = spawn ? null : hitTest(sx, sy);
      if (hit !== state.hover || spawn !== state.hoverSpawn) {
        state.hover = hit;
        state.hoverSpawn = spawn;
        el.canvas.style.cursor = (hit || spawn) ? 'pointer' : 'grab';
        render();
      }
    }
  });
  window.addEventListener('pointerup', (e) => {
    if (!state.dragging) return;
    state.dragging = false;
    el.canvas.classList.remove('grabbing');
    if (state.moved) return;
    const rect = el.canvas.getBoundingClientRect();
    const sx = e.clientX - rect.left, sy = e.clientY - rect.top;
    const spawn = hitTestSpawn(sx, sy);
    const hit = hitTest(sx, sy);
    const target = hit || null;

    // 起终点只能是房间，因此按住修饰键时忽略刷新点
    if (target && (e.ctrlKey || e.metaKey)) { setNav('from', target); return; }
    if (target && e.shiftKey) { setNav('to', target); return; }
    if (target && e.altKey) { setNav('via', target); return; }

    if (spawn) showSpawnPanel(spawn);
    else if (target) showRoomPanel(target);
    else if (state.selected || state.selectedSpawn) closePanel();
  });

  document.querySelectorAll('.zoom-controls button').forEach((btn) => {
    btn.addEventListener('click', () => {
      const act = btn.dataset.act;
      if (act === 'fit') { fitTarget('all'); return; }
      const f = act === 'in' ? 1.25 : 1 / 1.25;
      const rect = el.canvasWrap.getBoundingClientRect();
      const cx = rect.width / 2, cy = rect.height / 2;
      zoomViewAt(cx, cy, f);
    });
  });

  window.addEventListener('resize', resizeCanvas);
  new ResizeObserver(resizeCanvas).observe(el.canvasWrap);

  // ---------------- 初始化 ----------------

  loadOptions();
  loadPiPOptions();
  syncSidebar();
  renderHistory();
  resizeCanvas();

  const init = window.__INITIAL__ || {};
  if (init.seed) { el.seedInput.value = init.seed; loadSeed(init.seed); }
})();
