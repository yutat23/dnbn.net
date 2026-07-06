// SSE接続管理
let eventSource = null;
let isAutoUpdateEnabled = true;
let monitoringLoadInProgress = false;
let modalMonitoringRequestSequence = 0;
let messageViewMode = 'text';
let monitoringSourcesSignature = '';
let activeModalMonitoring = null;
let latestFeatures = {
  messageHistoryEnabled: false,
  sendEnabled: false,
  sendTokenRequired: false
};

// 初期化
document.addEventListener('DOMContentLoaded', () => {
  initializeEventSource();
  setupEventHandlers();
  loadInitialStatus();
  loadMonitoringData();
  window.setInterval(() => {
    loadMonitoringData();
    loadActiveModalMonitoring();
  }, 2000);
});

// EventSourceの初期化
function initializeEventSource() {
  if (eventSource) {
    eventSource.close();
  }

  eventSource = new EventSource('/api/status/stream');

  eventSource.onmessage = (event) => {
    try {
      const data = JSON.parse(event.data);
      updateUI(data);
    } catch (error) {
      console.error('SSEメッセージの解析エラー:', error);
    }
  };

  eventSource.onerror = (error) => {
    console.error('SSE接続エラー:', error);
    // EventSourceは自動的に再接続を試みる
  };

  eventSource.addEventListener('connected', (event) => {
    console.log('SSE接続が確立されました');
  });
}

// イベントハンドラの設定
function setupEventHandlers() {
  // リフレッシュボタン
  document.getElementById('refreshBtn').addEventListener('click', () => {
    loadInitialStatus();
    loadMonitoringData();
    loadActiveModalMonitoring();
  });

  document.getElementById('messageFormat').addEventListener('change', (event) => {
    messageViewMode = event.target.value;
    loadMonitoringData();
    loadActiveModalMonitoring();
  });

  document.getElementById('timelineSourceFilter').addEventListener('change', loadMonitoringData);
  document.getElementById('messageSourceFilter').addEventListener('change', loadMonitoringData);

  document.getElementById('sendBtn').addEventListener('click', sendMessageFromUI);
  document.getElementById('sendText').addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      sendMessageFromUI();
    }
  });

  // モーダル閉じるボタン
  document.getElementById('clientModalClose').addEventListener('click', () => {
    closeMonitoringModal('clientModal');
  });

  document.getElementById('serverModalClose').addEventListener('click', () => {
    closeMonitoringModal('serverModal');
  });

  // モーダル背景クリックで閉じる
  document.getElementById('clientModal').addEventListener('click', (e) => {
    if (e.target.id === 'clientModal') {
      closeMonitoringModal('clientModal');
    }
  });

  document.getElementById('serverModal').addEventListener('click', (e) => {
    if (e.target.id === 'serverModal') {
      closeMonitoringModal('serverModal');
    }
  });
}

// 初期状態を読み込む
async function loadInitialStatus() {
  try {
    const response = await fetch('/api/status');
    const data = await response.json();
    updateUI(data);
  } catch (error) {
    console.error('状態の取得エラー:', error);
  }
}

// UIを更新
function updateUI(data) {
  if (data.clients && data.servers) {
    updateMonitoringSourceOptions(data.clients, data.servers);
  }
  if (data.clients) {
    updateClientsTable(data.clients);
  }
  if (data.servers) {
    updateServersTable(data.servers);
  }
  if (data.features) {
    latestFeatures = data.features;
    updateFeatureControls();
  }
  if (data.clients) {
    updateSendClientOptions(data.clients);
  }
}

function updateMonitoringSourceOptions(clients, servers) {
  const sources = [
    ...clients.map((client) => ({ type: 'Client', name: client.name })),
    ...servers.map((server) => ({ type: 'Server', name: server.name }))
  ];
  const signature = JSON.stringify(sources);
  if (signature === monitoringSourcesSignature) return;
  monitoringSourcesSignature = signature;

  ['timelineSourceFilter', 'messageSourceFilter'].forEach((id) => {
    const select = document.getElementById(id);
    const selected = select.value;
    select.innerHTML = '<option value="">ALL</option>';

    sources.forEach((source) => {
      const option = document.createElement('option');
      option.value = `${source.type}:${source.name}`;
      option.dataset.source = source.name;
      option.dataset.sourceType = source.type;
      option.textContent = `[${source.type.toUpperCase()}] ${source.name}`;
      select.appendChild(option);
    });

    if (Array.from(select.options).some((option) => option.value === selected)) {
      select.value = selected;
    }
  });
}

function selectMonitoringSource(sourceType, sourceName) {
  const value = `${sourceType}:${sourceName}`;
  ['timelineSourceFilter', 'messageSourceFilter'].forEach((id) => {
    const select = document.getElementById(id);
    if (Array.from(select.options).some((option) => option.value === value)) {
      select.value = value;
    }
  });
  loadMonitoringData();
}

function getSourceQuery(selectId) {
  const select = document.getElementById(selectId);
  const option = select.options[select.selectedIndex];
  if (!option?.dataset.source) return '';

  const params = new URLSearchParams({
    source: option.dataset.source,
    sourceType: option.dataset.sourceType
  });
  return `?${params.toString()}`;
}

function updateFeatureControls() {
  const sendDisabled = !latestFeatures.sendEnabled;
  document.getElementById('sendDisabledNote').classList.toggle('hidden', !sendDisabled);
  ['sendClient', 'sendText', 'sendToken', 'sendOneWay', 'sendBtn'].forEach((id) => {
    document.getElementById(id).disabled = sendDisabled;
  });
  document.getElementById('sendToken').closest('label').classList.toggle(
    'hidden', !latestFeatures.sendTokenRequired);
}

function updateSendClientOptions(clients) {
  const select = document.getElementById('sendClient');
  const selected = select.value;
  select.innerHTML = '';

  clients.forEach((client) => {
    const option = document.createElement('option');
    option.value = client.name;
    option.textContent = `${client.name}${client.isConnected ? '' : ' [OFFLINE]'}`;
    option.disabled = !client.isConnected;
    select.appendChild(option);
  });

  if (clients.some((client) => client.name === selected && client.isConnected)) {
    select.value = selected;
  }
}

async function loadMonitoringData() {
  if (monitoringLoadInProgress) return;
  monitoringLoadInProgress = true;

  try {
    const [timelineResponse, messagesResponse, analyticsResponse] = await Promise.all([
      fetch(`/api/timeline${getSourceQuery('timelineSourceFilter')}`),
      fetch(`/api/messages${getSourceQuery('messageSourceFilter')}`),
      fetch(`/api/analytics${getSourceQuery('messageSourceFilter')}`)
    ]);
    if (!timelineResponse.ok || !messagesResponse.ok || !analyticsResponse.ok) {
      throw new Error('監視データの取得に失敗しました');
    }

    const [timeline, messages, analytics] = await Promise.all([
      timelineResponse.json(),
      messagesResponse.json(),
      analyticsResponse.json()
    ]);
    updateTimeline(timeline.events || []);
    updateMessages(messages.enabled, messages.messages || []);
    updateAnalytics(analytics.enabled, analytics.clients || []);
  } catch (error) {
    console.error('監視データの取得エラー:', error);
  } finally {
    monitoringLoadInProgress = false;
  }
}

function updateTimeline(events) {
  const tbody = document.getElementById('timelineTableBody');
  tbody.innerHTML = '';

  if (events.length === 0) {
    tbody.innerHTML = '<tr><td colspan="4" class="px-6 py-4 text-center" style="color: #666;">[NO EVENTS]</td></tr>';
    return;
  }

  events.slice().reverse().forEach((entry) => {
    const row = document.createElement('tr');
    row.innerHTML = `
      <td class="px-6 py-3 whitespace-nowrap">${formatTimestamp(entry.timestamp)}</td>
      <td class="px-6 py-3 whitespace-nowrap">${escapeHtml(formatSource(entry))}</td>
      <td class="px-6 py-3 whitespace-nowrap">${escapeHtml(entry.type)}</td>
      <td class="px-6 py-3 message-payload">${escapeHtml(entry.detail || '--')}</td>
    `;
    tbody.appendChild(row);
  });
}

function updateMessages(enabled, messages) {
  document.getElementById('messagesDisabledNote').classList.toggle('hidden', enabled);
  const tbody = document.getElementById('messagesTableBody');
  tbody.innerHTML = '';

  if (!enabled || messages.length === 0) {
    const text = enabled ? '[NO MESSAGES]' : '[MESSAGE HISTORY DISABLED]';
    tbody.innerHTML = `<tr><td colspan="7" class="px-6 py-4 text-center" style="color: #666;">${text}</td></tr>`;
    return;
  }

  messages.slice().reverse().forEach((entry) => {
    const row = document.createElement('tr');
    const shownPayload = messageViewMode === 'hex' ? entry.hex : entry.text;
    const capturedBytes = entry.hex ? entry.hex.length / 2 : 0;
    const truncated = entry.sizeBytes > capturedBytes ? ` (${entry.sizeBytes} bytes total)` : '';
    row.innerHTML = `
      <td class="px-6 py-3 whitespace-nowrap">${formatTimestamp(entry.timestamp)}</td>
      <td class="px-6 py-3 whitespace-nowrap">${escapeHtml(formatSource(entry))}</td>
      <td class="px-6 py-3 whitespace-nowrap">${escapeHtml(entry.direction)}</td>
      <td class="px-6 py-3 whitespace-nowrap">${escapeHtml(entry.kind)}</td>
      <td class="px-6 py-3 message-payload">${escapeHtml(shownPayload || '--')}${escapeHtml(truncated)}</td>
      <td class="px-6 py-3 whitespace-nowrap">${entry.sizeBytes}</td>
      <td class="px-6 py-3 whitespace-nowrap">${entry.elapsedMs == null ? '--' : Number(entry.elapsedMs).toFixed(1)}</td>
    `;
    tbody.appendChild(row);
  });
}

function updateAnalytics(enabled, clients) {
  renderAnalytics(document.getElementById('analyticsArea'), enabled, clients);
}

function renderAnalytics(area, enabled, clients) {
  if (!area) return;
  if (!enabled) {
    area.textContent = '';
    return;
  }
  if (clients.length === 0) {
    area.textContent = '[RESPONSE TIME] no samples';
    return;
  }

  area.innerHTML = clients.map((client) =>
    `<span class="analytics-item">${escapeHtml(client.name)}: n=${client.responseCount} ` +
    `min/avg/p95/max=${client.minMs}/${client.avgMs}/${client.p95Ms}/${client.maxMs} ms</span>`
  ).join('');
}

async function sendMessageFromUI() {
  const button = document.getElementById('sendBtn');
  const result = document.getElementById('sendResult');
  const client = document.getElementById('sendClient').value;
  const text = document.getElementById('sendText').value;

  if (!latestFeatures.sendEnabled || !client || text.length === 0) {
    result.textContent = '送信先と電文を入力してください。';
    return;
  }

  button.disabled = true;
  result.textContent = '送信中...';
  try {
    const headers = { 'Content-Type': 'application/json' };
    const token = document.getElementById('sendToken').value;
    if (latestFeatures.sendTokenRequired) {
      headers['X-Dnbn-Send-Token'] = token;
    }
    const response = await fetch('/api/send', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        client,
        text,
        oneWay: document.getElementById('sendOneWay').checked
      })
    });
    const body = await response.json();
    if (!response.ok || body.success === false) {
      throw new Error(body.error || `HTTP ${response.status}`);
    }
    result.textContent = body.response == null ? '送信完了（応答待ちなし）' : `応答: ${body.response}`;
    loadMonitoringData();
  } catch (error) {
    result.textContent = `送信失敗: ${error.message}`;
  } finally {
    button.disabled = !latestFeatures.sendEnabled;
  }
}

function formatTimestamp(value) {
  return value ? new Date(value).toLocaleString('ja-JP') : '--';
}

function formatSource(entry) {
  return entry.sourceType ? `[${entry.sourceType.toUpperCase()}] ${entry.source}` : entry.source;
}

function buildModalMonitoringHtml(prefix) {
  return `
    <div class="modal-monitoring-section">
      <h4 class="text-base font-semibold mb-3">[EVENT LOG]</h4>
      <div class="cyber-table rounded-lg modal-log-scroll">
        <table class="min-w-full">
          <thead>
            <tr>
              <th class="px-4 py-3 text-left text-xs font-medium">TIME</th>
              <th class="px-4 py-3 text-left text-xs font-medium">TYPE</th>
              <th class="px-4 py-3 text-left text-xs font-medium">DETAIL</th>
            </tr>
          </thead>
          <tbody id="${prefix}ModalTimelineBody">
            <tr><td colspan="3" class="px-4 py-3 text-center muted-text">[LOADING]</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <div class="modal-monitoring-section">
      <div class="flex items-center justify-between mb-3">
        <h4 class="text-base font-semibold">[MESSAGES]</h4>
        <label class="flex items-center gap-2 text-xs">
          <span>DISPLAY</span>
          <select id="${prefix}ModalMessageFormat" class="cyber-border rounded format-control">
            <option value="text"${messageViewMode === 'text' ? ' selected' : ''}>TEXT</option>
            <option value="hex"${messageViewMode === 'hex' ? ' selected' : ''}>HEX</option>
          </select>
        </label>
      </div>
      <div id="${prefix}ModalAnalytics" class="mb-3 muted-text"></div>
      <div class="cyber-table rounded-lg modal-log-scroll">
        <table class="min-w-full">
          <thead>
            <tr>
              <th class="px-4 py-3 text-left text-xs font-medium">TIME</th>
              <th class="px-4 py-3 text-left text-xs font-medium">DIR</th>
              <th class="px-4 py-3 text-left text-xs font-medium">KIND</th>
              <th class="px-4 py-3 text-left text-xs font-medium">TEXT / HEX</th>
              <th class="px-4 py-3 text-left text-xs font-medium">SIZE</th>
              <th class="px-4 py-3 text-left text-xs font-medium">RTT(ms)</th>
            </tr>
          </thead>
          <tbody id="${prefix}ModalMessagesBody">
            <tr><td colspan="6" class="px-4 py-3 text-center muted-text">[LOADING]</td></tr>
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function openModalMonitoring(sourceType, sourceName, prefix, modalId) {
  activeModalMonitoring = { sourceType, sourceName, prefix, modalId };
  document.getElementById(`${prefix}ModalMessageFormat`)?.addEventListener('change', (event) => {
    messageViewMode = event.target.value;
    document.getElementById('messageFormat').value = messageViewMode;
    loadMonitoringData();
    loadActiveModalMonitoring();
  });
  loadActiveModalMonitoring();
}

function closeMonitoringModal(modalId) {
  document.getElementById(modalId).classList.add('hidden');
  if (activeModalMonitoring?.modalId === modalId) {
    activeModalMonitoring = null;
    modalMonitoringRequestSequence++;
  }
}

async function loadActiveModalMonitoring() {
  const target = activeModalMonitoring;
  if (!target) return;

  const requestId = ++modalMonitoringRequestSequence;
  const params = new URLSearchParams({
    source: target.sourceName,
    sourceType: target.sourceType
  });

  try {
    const [timelineResponse, messagesResponse, analyticsResponse] = await Promise.all([
      fetch(`/api/timeline?${params}`),
      fetch(`/api/messages?${params}`),
      fetch(`/api/analytics?${params}`)
    ]);
    if (!timelineResponse.ok || !messagesResponse.ok || !analyticsResponse.ok) {
      throw new Error('対象別ログの取得に失敗しました');
    }

    const [timeline, messages, analytics] = await Promise.all([
      timelineResponse.json(),
      messagesResponse.json(),
      analyticsResponse.json()
    ]);
    if (requestId !== modalMonitoringRequestSequence || activeModalMonitoring !== target) return;

    renderModalTimeline(target.prefix, timeline.events || []);
    renderModalMessages(target.prefix, messages.enabled, messages.messages || []);
    renderAnalytics(
      document.getElementById(`${target.prefix}ModalAnalytics`),
      analytics.enabled,
      analytics.clients || []);
  } catch (error) {
    if (requestId !== modalMonitoringRequestSequence || activeModalMonitoring !== target) return;
    const timelineBody = document.getElementById(`${target.prefix}ModalTimelineBody`);
    const messagesBody = document.getElementById(`${target.prefix}ModalMessagesBody`);
    if (timelineBody) {
      timelineBody.innerHTML = `<tr><td colspan="3" class="px-4 py-3 text-center status-offline">${escapeHtml(error.message)}</td></tr>`;
    }
    if (messagesBody) {
      messagesBody.innerHTML = `<tr><td colspan="6" class="px-4 py-3 text-center status-offline">${escapeHtml(error.message)}</td></tr>`;
    }
  }
}

function renderModalTimeline(prefix, events) {
  const tbody = document.getElementById(`${prefix}ModalTimelineBody`);
  if (!tbody) return;
  if (events.length === 0) {
    tbody.innerHTML = '<tr><td colspan="3" class="px-4 py-3 text-center muted-text">[NO EVENTS]</td></tr>';
    return;
  }

  tbody.innerHTML = events.slice().reverse().map((entry) => `
    <tr>
      <td class="px-4 py-3 whitespace-nowrap">${formatTimestamp(entry.timestamp)}</td>
      <td class="px-4 py-3 whitespace-nowrap">${escapeHtml(entry.type)}</td>
      <td class="px-4 py-3 message-payload">${escapeHtml(entry.detail || '--')}</td>
    </tr>
  `).join('');
}

function renderModalMessages(prefix, enabled, messages) {
  const tbody = document.getElementById(`${prefix}ModalMessagesBody`);
  if (!tbody) return;
  if (!enabled || messages.length === 0) {
    const text = enabled ? '[NO MESSAGES]' : '[MESSAGE HISTORY DISABLED]';
    tbody.innerHTML = `<tr><td colspan="6" class="px-4 py-3 text-center muted-text">${text}</td></tr>`;
    return;
  }

  tbody.innerHTML = messages.slice().reverse().map((entry) => {
    const shownPayload = messageViewMode === 'hex' ? entry.hex : entry.text;
    const capturedBytes = entry.hex ? entry.hex.length / 2 : 0;
    const truncated = entry.sizeBytes > capturedBytes ? ` (${entry.sizeBytes} bytes total)` : '';
    return `
      <tr>
        <td class="px-4 py-3 whitespace-nowrap">${formatTimestamp(entry.timestamp)}</td>
        <td class="px-4 py-3 whitespace-nowrap">${escapeHtml(entry.direction)}</td>
        <td class="px-4 py-3 whitespace-nowrap">${escapeHtml(entry.kind)}</td>
        <td class="px-4 py-3 message-payload">${escapeHtml(shownPayload || '--')}${escapeHtml(truncated)}</td>
        <td class="px-4 py-3 whitespace-nowrap">${entry.sizeBytes}</td>
        <td class="px-4 py-3 whitespace-nowrap">${entry.elapsedMs == null ? '--' : Number(entry.elapsedMs).toFixed(1)}</td>
      </tr>
    `;
  }).join('');
}

// クライアントテーブルを更新
function updateClientsTable(clients) {
  const tbody = document.getElementById('clientsTableBody');
  tbody.innerHTML = '';

  if (clients.length === 0) {
    const row = document.createElement('tr');
    row.innerHTML = '<td colspan="7" class="px-6 py-4 text-center" style="color: #666;">[NO CLIENTS]</td>';
    tbody.appendChild(row);
    return;
  }

  clients.forEach((client, index) => {
    const row = document.createElement('tr');
    row.className = 'transition-all cursor-pointer';
    row.style.cssText = 'transition: all 0.2s;';
    row.addEventListener('click', () => {
      selectMonitoringSource('Client', client.name);
      showClientDetail(client);
    });
    row.addEventListener('mouseenter', () => {
      row.style.background = '#f9f9f9';
    });
    row.addEventListener('mouseleave', () => {
      row.style.background = '';
    });

    const statusBadge = client.isConnected
      ? '<span class="status-online font-bold">[ONLINE]</span>'
      : '<span class="status-offline font-bold">[OFFLINE]</span>';

    const remoteAddr = client.remoteHost && client.remotePort
      ? `${client.remoteHost}:${client.remotePort}`
      : '--';

    const uptime = client.connectionDuration || '--:--:--';
    const sentRecv = `${client.messagesSent || 0}/${client.messagesReceived || 0}`;
    const keepAliveTimeout = client.keepAlive?.timeoutCount || 0;
    const errorCount = client.error?.count || 0;
    
    // KeepAlive列の表示: タイムアウト回数と、最後の送信時刻があるかどうかで有効性を判断
    const keepAliveDisplay = keepAliveTimeout > 0 
      ? `<span class="status-offline font-semibold" title="キープアライブタイムアウトが${keepAliveTimeout}回発生">${keepAliveTimeout}</span>`
      : (client.keepAlive?.lastSentAt 
          ? `<span class="status-online" title="キープアライブ正常（タイムアウトなし）">0</span>`
          : `<span style="color: #666;" title="キープアライブ未設定または未送信">--</span>`);
    
    // Errors列の表示: エラーがある場合は強調表示
    const errorDisplay = errorCount > 0
      ? `<span class="status-offline font-semibold" title="エラーが${errorCount}回発生">${errorCount}</span>`
      : `<span class="status-online" title="エラーなし（正常）">0</span>`;

    row.innerHTML = `
      <td class="px-6 py-4 whitespace-nowrap font-medium">${escapeHtml(client.name)}</td>
      <td class="px-6 py-4 whitespace-nowrap">${statusBadge}</td>
      <td class="px-6 py-4 whitespace-nowrap">${escapeHtml(remoteAddr)}</td>
      <td class="px-6 py-4 whitespace-nowrap">${escapeHtml(uptime)}</td>
      <td class="px-6 py-4 whitespace-nowrap">${sentRecv}</td>
      <td class="px-6 py-4 whitespace-nowrap">${keepAliveDisplay}</td>
      <td class="px-6 py-4 whitespace-nowrap">${errorDisplay}</td>
    `;

    tbody.appendChild(row);
  });
}

// サーバーテーブルを更新
function updateServersTable(servers) {
  const tbody = document.getElementById('serversTableBody');
  tbody.innerHTML = '';

  if (servers.length === 0) {
    const row = document.createElement('tr');
    row.innerHTML = '<td colspan="7" class="px-6 py-4 text-center" style="color: #666;">[NO SERVERS]</td>';
    tbody.appendChild(row);
    return;
  }

  servers.forEach((server) => {
    const row = document.createElement('tr');
    row.className = 'transition-all cursor-pointer';
    row.style.cssText = 'transition: all 0.2s;';
    row.addEventListener('click', () => {
      selectMonitoringSource('Server', server.name);
      showServerDetail(server);
    });
    row.addEventListener('mouseenter', () => {
      row.style.background = '#f9f9f9';
    });
    row.addEventListener('mouseleave', () => {
      row.style.background = '';
    });

    const statusBadge = server.isRunning
      ? '<span class="status-online font-bold">[RUNNING]</span>'
      : '<span class="status-offline font-bold">[STOPPED]</span>';

    const uptime = server.uptime || '--:--:--';
    const connectionCount = server.connectionCount || 0;
    const totalConnections = server.totalConnections || 0;
    const sentRecv = `${server.messagesSent || 0}/${server.messagesReceived || 0}`;

    row.innerHTML = `
      <td class="px-6 py-4 whitespace-nowrap font-medium">${escapeHtml(server.name)}</td>
      <td class="px-6 py-4 whitespace-nowrap">${statusBadge}</td>
      <td class="px-6 py-4 whitespace-nowrap">${server.listenPort || '--'}</td>
      <td class="px-6 py-4 whitespace-nowrap">${escapeHtml(uptime)}</td>
      <td class="px-6 py-4 whitespace-nowrap font-semibold">${connectionCount}</td>
      <td class="px-6 py-4 whitespace-nowrap">${totalConnections}</td>
      <td class="px-6 py-4 whitespace-nowrap">${sentRecv}</td>
    `;

    tbody.appendChild(row);
  });
}

// クライアント詳細を表示
function showClientDetail(client) {
  const modal = document.getElementById('clientModal');
  const title = document.getElementById('clientModalTitle');
  const content = document.getElementById('clientModalContent');

  title.textContent = `CLIENT: ${escapeHtml(client.name)}`;

  const statusColor = client.isConnected ? '#00aa00' : '#cc0000';
  const statusText = client.isConnected ? '[ONLINE]' : '[OFFLINE]';

  const html = `
    <div class="space-y-6">
      <div class="grid grid-cols-2 gap-4">
        <div>
          <label class="text-xs font-medium" style="color: #666;">[CONNECTION STATUS]</label>
          <p class="mt-1 font-semibold" style="color: ${statusColor};">${statusText}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[REMOTE ADDRESS]</label>
          <p class="mt-1">${escapeHtml(client.remoteHost && client.remotePort ? `${client.remoteHost}:${client.remotePort}` : '--')}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[CONNECTED AT]</label>
          <p class="mt-1">${client.connectedAt ? new Date(client.connectedAt).toLocaleString('ja-JP') : '--'}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[UPTIME]</label>
          <p class="mt-1">${escapeHtml(client.connectionDuration || '--')}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[MESSAGES SENT]</label>
          <p class="mt-1 font-semibold">${client.messagesSent || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[MESSAGES RECEIVED]</label>
          <p class="mt-1 font-semibold">${client.messagesReceived || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[PENDING REQUESTS]</label>
          <p class="mt-1">${client.pendingRequests || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[RECONNECTING]</label>
          <p class="mt-1">${client.isReconnecting ? 'YES' : 'NO'}</p>
        </div>
      </div>

      <div style="border-top: 1px solid #cccccc; padding-top: 1rem;">
        <h4 class="text-base font-semibold mb-3">[KEEPALIVE]</h4>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="text-xs font-medium" style="color: #666;">[LAST SENT]</label>
            <p class="mt-1">${client.keepAlive?.lastSentAt ? new Date(client.keepAlive.lastSentAt).toLocaleString('ja-JP') : '--'}</p>
          </div>
          <div>
            <label class="text-xs font-medium" style="color: #666;">[LAST RESPONSE]</label>
            <p class="mt-1">${client.keepAlive?.lastResponseReceivedAt ? new Date(client.keepAlive.lastResponseReceivedAt).toLocaleString('ja-JP') : '--'}</p>
          </div>
          <div>
            <label class="text-xs font-medium" style="color: #666;">[TIMEOUT COUNT]</label>
            <p class="mt-1" style="color: ${client.keepAlive?.timeoutCount > 0 ? '#cc0000' : '#00aa00'};">${client.keepAlive?.timeoutCount || 0}</p>
          </div>
        </div>
      </div>

      <div style="border-top: 1px solid #cccccc; padding-top: 1rem;">
        <h4 class="text-base font-semibold mb-3">[ERROR INFO]</h4>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="text-xs font-medium" style="color: #666;">[ERROR COUNT]</label>
            <p class="mt-1" style="color: ${client.error?.count > 0 ? '#cc0000' : '#00aa00'};">${client.error?.count || 0}</p>
          </div>
          <div>
            <label class="text-xs font-medium" style="color: #666;">[LAST ERROR AT]</label>
            <p class="mt-1">${client.error?.lastErrorAt ? new Date(client.error.lastErrorAt).toLocaleString('ja-JP') : '--'}</p>
          </div>
          <div class="col-span-2">
            <label class="text-xs font-medium" style="color: #666;">[LAST ERROR MESSAGE]</label>
            <p class="mt-1 break-words" style="color: ${client.error?.lastError ? '#cc0000' : '#333'}; font-family: monospace;">${escapeHtml(client.error?.lastError || '--')}</p>
          </div>
        </div>
      </div>

      <div style="border-top: 1px solid #cccccc; padding-top: 1rem;">
        <h4 class="text-base font-semibold mb-3">[CONNECTION RETRY]</h4>
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="text-xs font-medium" style="color: #666;">[RETRY ATTEMPTS]</label>
            <p class="mt-1">${client.connectionRetry?.attempts || 0}</p>
          </div>
          <div>
            <label class="text-xs font-medium" style="color: #666;">[LAST ATTEMPT AT]</label>
            <p class="mt-1">${client.connectionRetry?.lastAttemptAt ? new Date(client.connectionRetry.lastAttemptAt).toLocaleString('ja-JP') : '--'}</p>
          </div>
        </div>
      </div>
      ${buildModalMonitoringHtml('client')}
    </div>
  `;

  content.innerHTML = html;
  modal.classList.remove('hidden');
  openModalMonitoring('Client', client.name, 'client', 'clientModal');
}

// サーバー詳細を表示
function showServerDetail(server) {
  const modal = document.getElementById('serverModal');
  const title = document.getElementById('serverModalTitle');
  const content = document.getElementById('serverModalContent');

  title.textContent = `SERVER: ${escapeHtml(server.name)}`;

  const statusColor = server.isRunning ? '#00aa00' : '#cc0000';
  const statusText = server.isRunning ? '[RUNNING]' : '[STOPPED]';

  const sessionsHtml = server.sessions && server.sessions.length > 0
    ? `
      <div class="mt-4 overflow-x-auto">
        <table class="min-w-full" style="border: 1px solid #cccccc;">
          <thead style="background: #f5f5f5;">
            <tr>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">SESSION ID</th>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">SOURCE ENDPOINT</th>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">CONNECTED AT</th>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">LAST MESSAGE</th>
              <th class="px-4 py-3 text-left text-xs font-medium uppercase font-mono">ACTIVE</th>
            </tr>
          </thead>
          <tbody>
            ${server.sessions.map(s => `
              <tr style="border-bottom: 1px solid #eeeeee;">
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${escapeHtml(s.sessionId)}</td>
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${escapeHtml(s.sourceEndpoint)}</td>
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${s.connectedAt ? new Date(s.connectedAt).toLocaleString('ja-JP') : '--'}</td>
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${s.lastMessageReceivedAt ? new Date(s.lastMessageReceivedAt).toLocaleString('ja-JP') : '--'}</td>
                <td class="px-4 py-3 whitespace-nowrap text-sm font-mono">${s.isActive ? '<span class="status-online">[ACTIVE]</span>' : '<span style="color: #666;">[INACTIVE]</span>'}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `
    : '<p class="text-sm font-mono" style="color: #666;">[NO SESSIONS]</p>';

  const html = `
    <div class="space-y-6">
      <div class="grid grid-cols-2 gap-4">
        <div>
          <label class="text-xs font-medium" style="color: #666;">[STATUS]</label>
          <p class="mt-1 font-semibold" style="color: ${statusColor};">${statusText}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[LISTEN PORT]</label>
          <p class="mt-1">${server.listenPort || '--'}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[STARTED AT]</label>
          <p class="mt-1">${server.startedAt ? new Date(server.startedAt).toLocaleString('ja-JP') : '--'}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[UPTIME]</label>
          <p class="mt-1">${escapeHtml(server.uptime || '--')}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[CURRENT CONNECTIONS]</label>
          <p class="mt-1 font-semibold">${server.connectionCount || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[TOTAL CONNECTIONS]</label>
          <p class="mt-1">${server.totalConnections || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[MESSAGES SENT]</label>
          <p class="mt-1 font-semibold">${server.messagesSent || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[MESSAGES RECEIVED]</label>
          <p class="mt-1 font-semibold">${server.messagesReceived || 0}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[LAST CLIENT CONNECTED]</label>
          <p class="mt-1">${server.lastClientConnectedAt ? new Date(server.lastClientConnectedAt).toLocaleString('ja-JP') : '--'}</p>
        </div>
        <div>
          <label class="text-xs font-medium" style="color: #666;">[LAST CLIENT DISCONNECTED]</label>
          <p class="mt-1">${server.lastClientDisconnectedAt ? new Date(server.lastClientDisconnectedAt).toLocaleString('ja-JP') : '--'}</p>
        </div>
      </div>

      <div style="border-top: 1px solid #cccccc; padding-top: 1rem;">
        <h4 class="text-base font-semibold mb-3 font-mono">[SESSIONS] (${server.sessions?.length || 0})</h4>
        ${sessionsHtml}
      </div>
      ${buildModalMonitoringHtml('server')}
    </div>
  `;

  content.innerHTML = html;
  modal.classList.remove('hidden');
  openModalMonitoring('Server', server.name, 'server', 'serverModal');
}

// HTMLエスケープ
function escapeHtml(text) {
  if (text == null) return '--';
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}
