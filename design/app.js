(() => {
  'use strict';
  const storageKey = 'glasscape-design-v1';
  const defaults = { taskbarEnabled: true, autoHide: true, opacity: 12, wallpaper: 'lake', monitor: 'primary', fit: 'cover', muted: true, volume: 30, startup: false, closeToTray: true, pauseFullscreen: true, pauseBattery: true };
  let state = { ...defaults };
  try {
    const saved = JSON.parse(localStorage.getItem(storageKey) || 'null');
    if (saved && typeof saved === 'object') {
      for (const key of Object.keys(defaults)) if (typeof saved[key] === typeof defaults[key]) state[key] = saved[key];
    }
  } catch (_) { /* Preview remains usable if browser storage is unavailable. */ }
  state.opacity = Math.max(0, Math.min(100, state.opacity));
  state.volume = Math.max(0, Math.min(100, state.volume));
  if (!['primary', 'secondary', 'all'].includes(state.monitor)) state.monitor = 'primary';
  if (!['cover', 'contain'].includes(state.fit)) state.fit = 'cover';

  const $ = id => document.getElementById(id);
  document.querySelectorAll('.brand-icon img, .tiny-glass img').forEach(icon => {
    const markLoaded = () => icon.parentElement.classList.add('has-icon');
    icon.addEventListener('load', markLoaded);
    if (icon.complete && icon.naturalWidth > 0) markLoaded();
  });
  const video = $('wallpaper-video');
  const image = $('wallpaper-image');
  let localVideos = [];
  let activeLocalVideo = null;
  let page = 'overview';
  let toastTimer;
  const wallpapers = {
    lake: { title: '고요한 호수', subtitle: '하루의 시작을 조금 더 차분하게.', alt: '잔잔한 호수와 산이 펼쳐진 배경화면', photo: 'photo-1470770841072-f978cf4d019e' },
    coast: { title: '물결의 여백', subtitle: '일렁이는 물결 너머, 넓은 여백.', alt: '햇빛 아래 푸른 바다와 파도', photo: 'photo-1518837695005-2083093ee35b' },
    forest: { title: '숲 사이의 빛', subtitle: '화면 가득, 천천히 쉬어가는 초록.', alt: '깊고 푸른 숲과 나무', photo: 'photo-1448375240586-882707db888b' }
  };
  if (!wallpapers[state.wallpaper]) state.wallpaper = 'lake';
  const pageCopy = {
    overview: ['나만의 작은 데스크톱 스튜디오', '풍경은 넓게. 화면은 가볍게.', '투명한 작업표시줄과 좋아하는 풍경을 함께 만나보세요.', '화면의 분위기', '조용한 디테일'],
    taskbar: ['풍경과 자연스럽게 이어지는', '작업표시줄의 작은 변화.', '불투명도를 조절하고 배경화면과 어우러지는 모습을 확인하세요.', '작업표시줄', '투명함의 정도'],
    wallpaper: ['머무르고 싶은 풍경', '내 화면에 좋아하는 장면을.', '사진을 선택하거나 내 동영상을 가져와 미리 재생해보세요.', '배경화면', '풍경을 다루는 방법'],
    settings: ['내 데스크톱에 맞춰서', '다음 시작도, 지금처럼.', '시작 방식과 재생 정책을 원하는 대로 구성해보세요.', '사용 환경', '익숙하고 편안하게']
  };

  function persist() {
    try { localStorage.setItem(storageKey, JSON.stringify(state)); } catch (_) { }
  }
  function toast(message) {
    $('toast').textContent = message;
    $('toast').classList.add('visible');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => $('toast').classList.remove('visible'), 3200);
  }
  function setPage(next, focus = false) {
    page = pageCopy[next] ? next : 'overview';
    const copy = pageCopy[page];
    ['page-eyebrow', 'page-title', 'page-description', 'panel-eyebrow', 'panel-title'].forEach((id, i) => $(id).textContent = copy[i]);
    document.querySelectorAll('.nav-item').forEach(button => {
      const active = button.dataset.page === page;
      button.classList.toggle('active', active);
      if (active) button.setAttribute('aria-current', 'page'); else button.removeAttribute('aria-current');
    });
    $('taskbar-controls').hidden = !['overview', 'taskbar'].includes(page);
    $('wallpaper-controls').hidden = !['overview', 'wallpaper'].includes(page);
    $('settings-controls').hidden = page !== 'settings';
    $('library-section').hidden = !['overview', 'wallpaper'].includes(page);
    $('taskbar-detail').hidden = page !== 'taskbar';
    $('settings-detail').hidden = page !== 'settings';
    document.querySelector('.studio').dataset.page = page;
    if (focus) { $('page-title').tabIndex = -1; $('page-title').focus({ preventScroll: true }); }
  }
  function setSwitch(id, value) { $(id).setAttribute('aria-checked', String(value)); }
  function renderSettings() {
    setSwitch('taskbar-enabled', state.taskbarEnabled);
    setSwitch('taskbar-autohide', state.autoHide);
    $('taskbar-preview').classList.toggle('auto-hide', state.autoHide);
    setSwitch('video-muted', state.muted);
    setSwitch('startup-enabled', state.startup);
    setSwitch('close-to-tray', state.closeToTray);
    setSwitch('pause-fullscreen', state.pauseFullscreen);
    setSwitch('pause-battery', state.pauseBattery);
    $('opacity').value = state.opacity;
    $('opacity-value').textContent = `${state.opacity}%`;
    $('opacity').disabled = !state.taskbarEnabled;
    $('opacity-control').classList.toggle('disabled', !state.taskbarEnabled);
    $('opacity').style.background = `linear-gradient(to right, var(--teal) ${state.opacity}%, #35475a ${state.opacity}%)`;
    $('taskbar-preview').style.background = state.taskbarEnabled ? `rgba(24, 33, 45, ${state.opacity / 100})` : 'rgb(32, 42, 55)';
    $('taskbar-preview').style.backdropFilter = state.taskbarEnabled ? 'blur(6px)' : 'none';
    $('preview-status').textContent = state.taskbarEnabled ? `작업표시줄 투명화 켜짐 · 불투명도 ${state.opacity}%` : '작업표시줄 투명화 꺼짐';
    $('volume').value = state.volume;
    $('volume-value').textContent = `${state.volume}%`;
    $('volume').style.background = `linear-gradient(to right, var(--teal) ${state.volume}%, #35475a ${state.volume}%)`;
    $('fit-mode').value = state.fit;
    image.style.objectFit = state.fit;
    video.style.objectFit = state.fit;
    video.muted = state.muted;
    video.volume = state.volume / 100;
    $('monitor-label').textContent = { primary: '주 모니터 · 2560 × 1440', secondary: '보조 모니터 · 1920 × 1080', all: '모든 모니터 · 같은 배경화면' }[state.monitor];
    document.querySelectorAll('[data-monitor]').forEach(button => {
      const selected = button.dataset.monitor === state.monitor;
      button.classList.toggle('selected', selected);
      button.setAttribute('aria-pressed', String(selected));
    });
  }
  function updatePlayback() {
    const hasVideo = !!activeLocalVideo;
    $('play-toggle').disabled = !hasVideo;
    $('play-toggle').setAttribute('aria-label', video.paused ? '동영상 재생' : '동영상 일시정지');
    $('play-toggle').querySelector('use').setAttribute('href', video.paused ? '#i-play' : '#i-pause');
    $('playback-description').textContent = hasVideo ? (video.paused ? '미리보기에서 일시정지됨' : '미리보기에서 반복 재생 중') : '동영상을 가져오면 재생할 수 있어요.';
    $('video-state').hidden = !hasVideo || !video.paused;
  }
  function selectWallpaper(id, announce = true) {
    const local = localVideos.find(item => item.id === id);
    activeLocalVideo = local || null;
    video.pause();
    if (local) {
      video.src = local.url;
      video.hidden = false;
      image.hidden = true;
      $('scene-kind').textContent = 'MY VIDEO';
      $('scene-title').textContent = local.name;
      $('scene-subtitle').textContent = '내 영상으로 채우는 작은 풍경.';
      video.play().catch(() => { updatePlayback(); toast('재생 버튼을 눌러 동영상을 시작하세요.'); });
    } else {
      const wallpaper = wallpapers[id] || wallpapers.lake;
      id = wallpapers[id] ? id : 'lake';
      video.removeAttribute('src');
      video.load();
      video.hidden = true;
      image.hidden = false;
      image.src = `https://images.unsplash.com/${wallpaper.photo}?auto=format&fit=crop&w=1600&q=85`;
      image.alt = wallpaper.alt;
      $('scene-kind').textContent = 'STILL COLLECTION';
      $('scene-title').textContent = wallpaper.title;
      $('scene-subtitle').textContent = wallpaper.subtitle;
      state.wallpaper = id;
      persist();
    }
    document.querySelectorAll('[data-wallpaper]').forEach(card => {
      const selected = card.dataset.wallpaper === id;
      card.classList.toggle('selected', selected);
      card.setAttribute('aria-pressed', String(selected));
    });
    updatePlayback();
    if (announce) toast('배경화면 미리보기를 변경했습니다.');
  }
  function importVideo(file) {
    if (!file) return;
    const ext = file.name.split('.').pop().toLowerCase();
    const acceptedType = !file.type || ['video/mp4', 'video/webm'].includes(file.type);
    if (!['mp4', 'webm'].includes(ext) || !acceptedType) {
      $('import-feedback').textContent = 'MP4 또는 WebM 동영상 파일을 선택해주세요.';
      $('import-feedback').classList.add('error');
      return;
    }
    if (file.size === 0) {
      $('import-feedback').textContent = '빈 파일은 재생할 수 없습니다. 다른 동영상을 선택해주세요.';
      $('import-feedback').classList.add('error');
      return;
    }
    const item = { id: `local-${Date.now()}-${localVideos.length}`, name: file.name.replace(/\.(mp4|webm)$/i, ''), fileName: file.name, url: URL.createObjectURL(file) };
    localVideos.push(item);
    const card = document.createElement('button');
    card.className = 'wallpaper-card local-card';
    card.dataset.wallpaper = item.id;
    card.setAttribute('aria-pressed', 'false');
    const thumbnail = document.createElement('span'); thumbnail.className = 'card-image';
    thumbnail.innerHTML = '<svg aria-hidden="true"><use href="#i-play"/></svg><span class="selected-badge"><svg><use href="#i-check"/></svg>사용 중</span><span class="media-type">동영상</span>';
    const description = document.createElement('span'); description.className = 'card-description';
    const title = document.createElement('strong'); title.textContent = item.name; title.title = item.fileName;
    const meta = document.createElement('small'); meta.textContent = `${ext.toUpperCase()} · ${(file.size / 1024 / 1024).toFixed(1)} MB · 내 동영상`;
    description.append(title, meta); description.insertAdjacentHTML('beforeend', '<svg aria-hidden="true"><use href="#i-chevron"/></svg>');
    card.append(thumbnail, description);
    $('wallpaper-grid').append(card);
    $('import-feedback').textContent = `${item.fileName} — 이 세션에서 재생합니다. 브라우저를 다시 열면 파일을 다시 선택해주세요.`;
    $('import-feedback').classList.remove('error');
    selectWallpaper(item.id, false);
    toast('동영상을 가져왔습니다. 미리보기에서 재생합니다.');
  }

  document.querySelectorAll('.nav-item').forEach(button => button.addEventListener('click', () => { setPage(button.dataset.page); history.replaceState(null, '', `#${button.dataset.page}`); }));
  document.querySelector('.brand').addEventListener('click', () => setPage('overview'));
  window.addEventListener('hashchange', () => setPage(location.hash.slice(1)));
  const switches = { 'taskbar-enabled': 'taskbarEnabled', 'taskbar-autohide': 'autoHide', 'video-muted': 'muted', 'startup-enabled': 'startup', 'close-to-tray': 'closeToTray', 'pause-fullscreen': 'pauseFullscreen', 'pause-battery': 'pauseBattery' };
  Object.entries(switches).forEach(([id, key]) => $(id).addEventListener('click', () => {
    state[key] = !state[key]; renderSettings(); persist();
    if (['startup', 'closeToTray', 'pauseFullscreen', 'pauseBattery'].includes(key)) toast('미리보기 옵션을 저장했습니다. Windows 앱에서 적용될 설정입니다.');
  }));
  $('opacity').addEventListener('input', event => { state.opacity = Number(event.target.value); renderSettings(); persist(); });
  $('volume').addEventListener('input', event => { state.volume = Number(event.target.value); renderSettings(); persist(); });
  $('fit-mode').addEventListener('change', event => { state.fit = event.target.value; renderSettings(); persist(); });
  document.querySelectorAll('[data-monitor]').forEach(button => button.addEventListener('click', () => { state.monitor = button.dataset.monitor; renderSettings(); persist(); toast('모니터 선택을 미리보기에 반영했습니다.'); }));
  $('wallpaper-grid').addEventListener('click', event => { const card = event.target.closest('[data-wallpaper]'); if (card) selectWallpaper(card.dataset.wallpaper); });
  $('play-toggle').addEventListener('click', () => { if (!activeLocalVideo) return; if (video.paused) video.play().catch(() => toast('이 동영상을 재생할 수 없습니다. 다른 파일을 선택해주세요.')); else video.pause(); });
  video.addEventListener('play', updatePlayback); video.addEventListener('pause', updatePlayback);
  video.addEventListener('error', () => {
    if (!activeLocalVideo) return;
    $('import-feedback').textContent = '브라우저가 이 동영상의 형식을 재생할 수 없습니다. H.264 MP4 또는 WebM 파일을 선택해주세요.';
    $('import-feedback').classList.add('error'); updatePlayback();
  });
  [$('import-video'), $('choose-video')].forEach(button => button.addEventListener('click', () => { $('video-file').value = ''; $('video-file').click(); }));
  $('video-file').addEventListener('change', event => importVideo(event.target.files[0]));
  ['dragenter', 'dragover'].forEach(name => $('drop-zone').addEventListener(name, event => { event.preventDefault(); $('drop-zone').classList.add('drag-over'); }));
  $('drop-zone').addEventListener('dragleave', event => { if (!$('drop-zone').contains(event.relatedTarget)) $('drop-zone').classList.remove('drag-over'); });
  $('drop-zone').addEventListener('drop', event => { event.preventDefault(); $('drop-zone').classList.remove('drag-over'); const files = [...event.dataTransfer.files]; if (files.length > 1) toast('첫 번째 동영상을 가져왔습니다. 나머지는 한 번씩 추가해주세요.'); importVideo(files[0]); });
  window.addEventListener('dragover', event => event.preventDefault());
  window.addEventListener('drop', event => event.preventDefault());
  $('reset-settings').addEventListener('click', () => {
    video.pause(); video.removeAttribute('src'); video.load();
    activeLocalVideo = null;
    localVideos.forEach(item => URL.revokeObjectURL(item.url)); localVideos = [];
    document.querySelectorAll('.local-card').forEach(card => card.remove());
    state = { ...defaults }; persist(); renderSettings(); selectWallpaper(state.wallpaper, false);
    $('import-feedback').textContent = ''; $('import-feedback').classList.remove('error');
    toast('미리보기 설정을 초기화했습니다.');
  });
  window.addEventListener('pagehide', event => {
    if (!event.persisted) localVideos.forEach(item => URL.revokeObjectURL(item.url));
  });
  renderSettings(); selectWallpaper(state.wallpaper, false); setPage(location.hash.slice(1));
})();
